using Godot;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// **按游戏的真实挂载方式**摆主角，出一张图——用来把"姿势坏"和"挂载坏"分开。
///
/// 为什么要有它：`ModelShowcase` 会主动把模型原点**补偿到脚底**
/// （<c>root.Position += new Vector3(0, -box.Position.Y * scale, 0)</c>），
/// 所以它在哪儿都好看；而 `PlayerActor` **只设 Scale / Rotation，不补偿原点**。
/// 两者不一致意味着：展示场景里正常 ≠ 游戏里正常。
/// 这个场景复刻后者——原点就在脚下、不加任何补偿——所以它坏了就是真坏了。
///
/// 用法（**必须带窗口**，headless 是 dummy 渲染器存出来全黑）：
///     godot --path . res://scenes/tests/PlayerMountShot.tscn
///
/// 退出码 0 = 已出图。
/// </summary>
public partial class PlayerMountShot : Node3D
{
	[ExportGroup("被测对象")]
	/// <summary>要摆的玩家场景。默认就是游戏里真正用的那份。</summary>
	[Export] public PackedScene? PlayerScene { get; set; }

	[ExportGroup("挂载")]
	/// <summary>★ 与 Player.tscn 保持一致。改了模型就同步改这里，否则量的是别的东西。</summary>
	[Export] public float VisualScale { get; set; } = 0.888f;
	[Export] public Vector3 VisualRotationDegrees { get; set; } = new(0f, 180f, 0f);

	[ExportGroup("相机")]
	[Export] public float CameraDistance { get; set; } = 3.2f;
	[Export] public float CameraHeight { get; set; } = 1.5f;
	[Export] public float LookAtHeight { get; set; } = 0.9f;

	[ExportGroup("出图")]
	[Export] public string OutputPath { get; set; } = "res://assets/references/_mount_rest.png";
	/// <summary>摆好之后等几帧再抓，等渲染器真的画过。</summary>
	[Export] public int WarmupFrames { get; set; } = 20;

	[ExportGroup("攻击姿势连拍")]
	/// <summary>&gt;0 时不用 Player.tscn，改用包装层直接驱动 <see cref="HumanoidAnimator"/> 摆攻击姿势，
	/// 并对 <see cref="AttackFrames"/> 里的每一帧各出一张图——这是把「姿势坏」和「权重坏」分开的关键证据。</summary>
	[Export] public int AttackTotalFrames { get; set; } = 0;
	[Export] public int AttackStep { get; set; } = 2;
	/// <summary>要出图的帧号，逗号分隔。</summary>
	[Export] public string AttackFrames { get; set; } = "0,9,18,27,36";
	[Export] public string AttackOutputPrefix { get; set; } = "res://assets/references/_attack_f";

	private int _frames;
	private Camera3D? _camera;
	private HumanoidAnimator? _animator;

	public override void _Ready()
	{
		// 地面：一块看得见的板，不做碰撞——这里只看外观，不需要物理。
		var floor = new MeshInstance3D { Name = "Floor" };
		var plane = new PlaneMesh { Size = new Vector2(8f, 8f) };
		floor.Mesh = plane;
		var floorMat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.33f, 0.35f) };
		floor.MaterialOverride = floorMat;
		AddChild(floor);

		var sun = new DirectionalLight3D { Name = "Sun" };
		sun.RotationDegrees = new Vector3(-52f, 28f, 0f);
		sun.LightEnergy = 1.1f;
		AddChild(sun);

		if (PlayerScene is not null && AttackTotalFrames <= 0)
		{
			var player = PlayerScene.Instantiate<Node3D>();
			player.Name = "Player";
			AddChild(player);
			GD.Print($"[挂载截图] 玩家已放置 scale={VisualScale} rot={VisualRotationDegrees}");
		}
		else if (AttackTotalFrames > 0)
		{
			// 攻击姿势连拍：不经过 PlayerActor（它的物理/状态机会掺进来），
			// 直接拿包装层驱动 HumanoidAnimator——只**调用**它，不改它的代码。
			var wrapper = GD.Load<PackedScene>("res://scenes/actors/PlayerVisual.tscn");
			var visual = wrapper.Instantiate<Node3D>();
			visual.Name = "Player";
			visual.Scale = Vector3.One * VisualScale;
			visual.RotationDegrees = VisualRotationDegrees;
			AddChild(visual);
			_animator = new HumanoidAnimator(visual);
			_animator.PlayAttack(AttackStep, AttackTotalFrames);
			GD.Print($"[挂载截图] 攻击姿势连拍 step={AttackStep} total={AttackTotalFrames} 帧={AttackFrames}");
		}
		else
		{
			GD.PrintErr("[挂载截图] 没给 PlayerScene");
		}

		_camera = new Camera3D
		{
			Name = "ShotCam",
			Position = new Vector3(0f, CameraHeight, CameraDistance),
			Fov = 45f,
		};
		AddChild(_camera);
		_camera.LookAt(new Vector3(0f, LookAtHeight, 0f), Vector3.Up);
		_camera.Current = true;
	}

	public override void _Process(double delta)
	{
		_frames++;

		// ── 攻击姿势连拍分支：逐帧驱动，命中帧号就出图 ──
		if (_animator is not null)
		{
			int f = _frames - 1;                     // 第 1 帧对应 attackFrame 0
			_animator.AnimateLocomotion(0f, (float)delta);
			_animator.AnimateCombat((float)delta, f);
			foreach (var s in AttackFrames.Split(','))
			{
				if (!int.TryParse(s.Trim(), out int want) || want != f)
				{
					continue;
				}
				string p = $"{AttackOutputPrefix}{want}.png";
				Image shot = GetViewport().GetTexture().GetImage();
				Error e = shot.SavePng(p);
				GD.Print(e == Error.Ok
					? $"[挂载截图] 攻击第 {want} 帧 → {p}"
					: $"[挂载截图] 写不出去 {p}（{e}）");
			}
			if (f >= AttackTotalFrames + 4)
			{
				GetTree().Quit(0);
			}
			return;
		}

		if (_frames != WarmupFrames)
		{
			return;
		}

		// 把量出来的事实打出来——光看图容易骗自己，数字不会。
		var player = GetNodeOrNull<Node3D>("Player");
		if (player is not null)
		{
			var visual = player.GetNodeOrNull<Node3D>("VisualModel");
			if (visual is not null)
			{
				// `Node3D` 没有 GetAabb——原来那行 `visual.GetAabb()` 编译不过，
				// 整个项目跟着构建失败。改成去拿下面的 MeshInstance3D，
				// 它才有 GetAabb()（返回的是 mesh 在**该节点局部空间**里的包围盒）。
				// ★ 必须递归找：glTF 导入是 根→Skeleton3D→MeshInstance3D 三层，
				//   只看直接子节点一个都找不到（第一版就是这么空跑的）。
				Aabb aabb = default;
				bool found = false;
				foreach (Node child in visual.FindChildren("*", "MeshInstance3D", true, false))
				{
					if (child is MeshInstance3D mi && mi.Mesh is not null)
					{
						// 拿到的是**世界空间**包围盒（乘过骨骼当前的姿势）——
						// 这正是我们要量"脚底陷没陷进地里"的东西。
						aabb = mi.GlobalTransform * mi.GetAabb();
						found = true;
						break;
					}
				}

				if (!found)
				{
					GD.PrintErr("[挂载截图] VisualModel 子树里没有 MeshInstance3D，量不到 AABB");
				}
				else
				{
					GD.Print($"[挂载截图] 世界包围盒  脚底 y={aabb.Position.Y:F3}  头顶 y={aabb.End.Y:F3}  身高={aabb.Size.Y:F3}");
					GD.Print($"[挂载截图] 玩家原点 y={player.GlobalPosition.Y:F3}（地面在 y=0）");
					GD.Print(aabb.Position.Y < -0.05f
						? $"[挂载截图] ★ 脚底陷进地里 {(-aabb.Position.Y):F3} 米——挂载没补偿模型原点"
						: $"[挂载截图] 脚底离地 {aabb.Position.Y:F3} 米");
				}
			}
			else
			{
				GD.PrintErr("[挂载截图] 玩家身上没有 VisualModel 节点");
			}
		}

		var img = GetViewport().GetTexture().GetImage();
		Error err = img.SavePng(OutputPath);
		if (err == Error.Ok)
		{
			GD.Print($"[挂载截图] 已出图：{OutputPath}（{img.GetWidth()}×{img.GetHeight()}）");
		}
		else
		{
			GD.PrintErr($"[挂载截图] 写不出去：{OutputPath}（{err}）");
		}
		GetTree().Quit(err == Error.Ok ? 0 : 1);
	}
}
