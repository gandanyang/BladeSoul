using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Enemies;

namespace Oniblade.Dev;

/// <summary>
/// 敌人「被击败」演出的**在线链路**体检。
///
/// 与 <see cref="AshigaruAnimProbe"/> 是互补关系，不是重复：
/// · 那个是**离线**的——自己 new 动画器、自己传合法帧号（<c>f, SamplesPerAction</c>），
///   所以它只能证明「死亡姿势被写出来了」；
/// · 本探针走**真实链路**（真 <c>Ashigaru</c> 节点 → 真 <c>Die()</c> → 真 <c>_PhysicsProcess</c>），
///   证明的是「死亡姿势在游戏里到底有没有被播出来」。
///
/// 为什么两个都得跑：T52 的死亡演出正是「离线探针绿、游戏里坏」。
/// 探针自造的帧号绕过了真实调用路径，而那条路径上写着 <c>0, 0</c>，
/// 并且 <c>CombatActor._PhysicsProcess</c> 在 <c>IsDead</c> 时提前 return，
/// 让 <c>OnTickVisual</c> 一次都不会再被调用。
///
/// 判据带**对照组**：同一具骨架，手动写死亡姿势应当能量出几十度的差。
/// 若连对照组都是 0，说明是探针坏了，而不是演出坏了——那时不下任何结论。
///
/// 退出码：0 = 死亡有演出；1 = **确认缺陷**（死亡后视觉冻结）；2 = 探针自身失效。
/// </summary>
public partial class ActorDeathProbe : Node3D
{
	private const string ScenePath = "res://scenes/enemies/Ashigaru.tscn";
	private const int SampleFrames = 40;

	private static readonly string[] Tracked =
	{
		"Hip", "Spine01", "Spine02", "Head",
		"L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
		"L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
	};

	/// <summary>对照组门槛：离线写死亡姿势至少能量出这么多度，否则本探针不可信。</summary>
	private const float ExpectedFloorDeg = 20f;

	private const string ShotPrefix = "res://assets/references/t52_death";

	private Skeleton3D? _skel;
	private Node3D? _modelRoot;
	private Ashigaru? _enemy;
	private Camera3D? _camera;
	private bool _shot;
	private readonly Dictionary<string, Quaternion> _baseline = new();

	public override void _Ready() => _ = RunAsync();

	// 项目棘轮：`async void` 里的异常会被吞掉（Godot 里表现为一行 ERROR 就没了）。
	// 生命周期方法必须返回 void，所以把 await 全挪进 RunCore，这里只负责兜异常。
	private async System.Threading.Tasks.Task RunAsync()
	{
		try
		{
			await RunCore();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[死亡演出] ✗ 未捕获异常：{ex}");
			GetTree().Quit(1);
		}
	}

	private async System.Threading.Tasks.Task RunCore()
	{
		PackedScene? packed = GD.Load<PackedScene>(ScenePath);
		if (packed is null)
		{
			Fail($"加载 {ScenePath} 失败");
			return;
		}

		_enemy = packed.Instantiate<Ashigaru>();
		AddChild(_enemy);

		// 带窗口跑时加 `-- shot` 会多拍两张图（死亡瞬间 / 40 帧后）。
		_shot = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot") >= 0;
		if (_shot)
			BuildStage();

		// 模型是在 OnActorReady 里实例化的，等两帧再找骨架
		await WaitPhysicsFrames(2);

		_skel = FindSkeleton(_enemy);
		if (_skel is null)
		{
			Fail("敌人身上找不到 Skeleton3D（骨架没挂上？）");
			return;
		}

		_modelRoot = FindModelRoot(_enemy, _skel);

		ReportModelBounds();

		GD.Print("[死亡演出] ── 在线链路体检 ──");
		GD.Print($"[死亡演出] 骨架 {_skel.GetBoneCount()} 骨，观察窗 {SampleFrames} 帧");

		// 先拍一张「活着」做构图对照。没有它，就分不清画面里的异常
		// 是"死亡引起的"还是"模型/机位本来就这样"。
		if (_shot)
		{
			await WaitProcessFrames(3);
			Shoot("alive");
		}

		// ── 对照组：手动把死亡姿势写到骨架上（离线路径）──
		//    它同时证明两件事：① 死亡姿势函数本身有内容；② 本探针读得到姿态变化。
		Capture();
		float expected = 0f;
		if (_modelRoot is not null)
		{
			var anim = new AshigaruAnimator(_modelRoot);
			anim.Reset();
			for (int f = 0; f < SampleFrames; f++)
			{
				anim.Animate(1f / 60f, 0f, AshigaruAction.Death, f, SampleFrames);
				expected = Mathf.Max(expected, DeltaFromBaseline());
			}

			anim.Reset();
			await WaitPhysicsFrames(2);   // 等游戏自己的动画器把姿态写回来
		}

		GD.Print($"[死亡演出] 对照组·离线写死亡姿势：与存活姿态最大差 {expected:F1}°" +
				 $"（门槛 {ExpectedFloorDeg:F0}°）");

		// ── 实验组：真实 Die()，然后看这 40 帧里视觉还更不更新 ──
		Capture();
		_enemy.Die();

		// 截图模式下多拍两张：死亡瞬间 / 40 帧后。
		// 两张若逐像素相同，就是「尸体完全静止」最直白的证据。
		await WaitProcessFrames(3);
		if (_shot)
			Shoot("atdie");

		float actual = 0f;
		for (int i = 0; i < SampleFrames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			actual = Mathf.Max(actual, DeltaFromBaseline());
		}

		await WaitProcessFrames(3);
		if (_shot)
			Shoot("after40");

		// 决定性对照（历史使命已完成）：D2 修复前，把模型抬到碰撞体高度能拍出"完整的人"，
		// 由此证明"半截身子"是模型没对齐。**D2 已修**（AshigaruModel.tscn 自带 y=0.875），
		// 这一步现在故意**再抬 0.875**——应拍出"悬空"，反向证明默认位置已经脚底贴地。
		if (_shot && _modelRoot is not null)
		{
			_modelRoot.Position = new Vector3(0f, 0.875f, 0f);
			await WaitProcessFrames(3);
			Shoot("raised");
		}

		GD.Print($"[死亡演出] 实验组·Die() 后 {SampleFrames} 帧：与死亡瞬间最大差 {actual:F2}°");

		// ── 判定 ──
		if (_modelRoot is null)
		{
			Fail("找不到模型根节点，对照组无法执行");
			return;
		}

		if (expected < ExpectedFloorDeg)
		{
			GD.PrintErr($"[死亡演出] ✗ 对照组只量到 {expected:F1}°（门槛 {ExpectedFloorDeg:F0}°）——" +
						"探针自身失效，本次不下任何结论");
			GetTree().Quit(2);
			return;
		}

		if (actual <= 0.01f)
		{
			GD.PrintErr($"[死亡演出] ✗ 敌人死后 {SampleFrames} 帧姿态一动不动（差 {actual:F2}°）——" +
						"死亡演出没接到真实链路上：尸体僵在死前那一帧");
			GD.PrintErr("[死亡演出]   病根：CombatActor._PhysicsProcess 在 IsDead 时 return，" +
						"OnTickVisual 不再被调用；且 Ashigaru.DriveModel 的死亡分支传的是帧号 0, 0");
			GetTree().Quit(1);
			return;
		}

		GD.Print($"[死亡演出] ✓ 敌人死亡后有演出（{SampleFrames} 帧内姿态变化 {actual:F1}°）");
		GetTree().Quit(0);
	}

	/// <summary>
	/// 打印模型的世界包围盒。**把"看着只有半截"这件事从观感变成数字**——
	/// 是模型真的沉下去了、还是被缩放、还是机位不对，包围盒一说就清楚。
	/// </summary>
	private void ReportModelBounds()
	{
		if (_modelRoot is null)
		{
			GD.Print("[死亡演出] 模型根为空，量不了包围盒");
			return;
		}

		GD.Print($"[死亡演出] 敌人在 {_enemy!.GlobalPosition}，模型根 {_modelRoot.GlobalPosition}，" +
				 $"缩放 {_modelRoot.Scale}");

		Aabb? merged = null;
		int count = 0;

		foreach (Node node in AllDescendants(_modelRoot))
		{
			if (node is not MeshInstance3D mesh)
				continue;

			Aabb world = mesh.GlobalTransform * mesh.GetAabb();
			merged = merged is null ? world : merged.Value.Merge(world);
			count++;
		}

		if (merged is null)
		{
			GD.Print("[死亡演出] 模型里没有 MeshInstance3D");
			return;
		}

		Aabb box = merged.Value;
		GD.Print($"[死亡演出] 模型世界包围盒（{count} 个网格）：" +
				 $"min=({box.Position.X:F2},{box.Position.Y:F2},{box.Position.Z:F2}) " +
				 $"size=({box.Size.X:F2},{box.Size.Y:F2},{box.Size.Z:F2})");
		GD.Print("[死亡演出] 站立参考：足兵设计身高 1.6998，脚底应当贴 y=0");

		// D1 验证（2026-09-15）：接触点应取受击体积（胸口），不再是脚底。
		Hurtbox? hurt = _enemy!.GetNodeOrNull<Hurtbox>("Hurtbox");
		GD.Print($"[死亡演出] D1 接触点：hurtbox.GlobalPosition={hurt?.GlobalPosition} → " +
				 $"GlobalContactPoint={hurt?.GlobalContactPoint}（期望 y≈0.875）");
	}

	private static IEnumerable<Node> AllDescendants(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			yield return child;
			foreach (Node grand in AllDescendants(child))
				yield return grand;
		}
	}

	/// <summary>带窗口跑时才搭的舞台：地板 + 光 + 环境 + 机位。</summary>
	private void BuildStage()
	{
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(16f, 0.2f, 16f) },
			Position = new Vector3(0f, -0.1f, 0f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.29f, 0.28f) },
		});

		AddChild(new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-45f, 35f, 0f),
			LightEnergy = 1.2f,
			ShadowEnabled = true,
		});

		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.12f, 0.13f, 0.16f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.45f, 0.47f, 0.55f),
				AmbientLightEnergy = 0.6f,
			},
		});

		// 机位：正侧方看全身，脚和地面都在画面里——"有没有倒下去"看脚最直白。
		_camera = new Camera3D { Fov = 45f };
		AddChild(_camera);
		_camera.LookAtFromPosition(
			new Vector3(2.8f, 1.4f, 3.4f),
			new Vector3(0f, 0.80f, 0f));
		_camera.MakeCurrent();
	}

	private void Shoot(string label)
	{
		if (_camera is not null && GetViewport().GetCamera3D() != _camera)
			_camera.MakeCurrent();

		Image image = GetViewport().GetTexture().GetImage();
		if (image is null || image.IsEmpty())
		{
			GD.PrintErr($"[死亡演出] 抓 {label} 时是空帧——无头模式是 dummy renderer，请带窗口跑");
			return;
		}

		string path = $"{ShotPrefix}_{label}.png";
		Error error = image.SavePng(path);
		GD.Print(error == Error.Ok
			? $"[死亡演出] 已拍 {path}"
			: $"[死亡演出] ✗ 保存 {path} 失败：{error}");
	}

	private async System.Threading.Tasks.Task WaitProcessFrames(int count)
	{
		for (int i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task WaitPhysicsFrames(int count)
	{
		for (int i = 0; i < count; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private void Capture()
	{
		_baseline.Clear();
		if (_skel is null)
			return;

		foreach (string name in Tracked)
		{
			int bone = _skel.FindBone(name);
			if (bone >= 0)
				_baseline[name] = _skel.GetBonePoseRotation(bone);
		}
	}

	/// <summary>与基线相比，**欧拉分量差的最大值**（规约到 (-180,180]）。</summary>
	private float DeltaFromBaseline()
	{
		if (_skel is null)
			return 0f;

		float worst = 0f;

		foreach (string name in Tracked)
		{
			if (!_baseline.TryGetValue(name, out Quaternion baseline))
				continue;

			int bone = _skel.FindBone(name);
			if (bone < 0)
				continue;

			Vector3 a = baseline.GetEuler();
			Vector3 b = _skel.GetBonePoseRotation(bone).GetEuler();

			for (int i = 0; i < 3; i++)
			{
				float ax = Mathf.RadToDeg(a[i]);
				float bx = Mathf.RadToDeg(b[i]);
				float diff = Mathf.Abs(PosMod(bx - ax + 180f, 360f) - 180f);
				if (diff > worst)
					worst = diff;
			}
		}

		return worst;
	}

	private static float PosMod(float a, float b) => a - b * Mathf.Floor(a / b);

	private static Skeleton3D? FindSkeleton(Node node)
	{
		if (node is Skeleton3D skeleton)
			return skeleton;

		foreach (Node child in node.GetChildren())
		{
			Skeleton3D? found = FindSkeleton(child);
			if (found is not null)
				return found;
		}

		return null;
	}

	/// <summary>找挂着这具骨架的那个直接子节点（= 动画器的模型根）。</summary>
	private static Node3D? FindModelRoot(Node parent, Skeleton3D skel)
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is not Node3D candidate)
				continue;

			for (Node? n = skel; n is not null; n = n.GetParent())
			{
				if (n == candidate)
					return candidate;
			}
		}

		return null;
	}

	private void Fail(string message)
	{
		GD.PrintErr($"[死亡演出] ✗ {message}");
		GetTree().Quit(2);
	}
}
