using System.Collections.Generic;
using Godot;

namespace Oniblade.Player;

/// <summary>
/// 程序化人形动画：把玩家状态翻译成 41 骨 Mixamo 骨架的姿势（T38）。
///
/// **为什么是程序化的**：AI 生成的模型只有骨架、没有动画剪辑（Mixamo 动画是后续管线），
/// 但"站着滑行"的观感比灰盒还差。先用代码把姿势做出来，等动画资产到位后整块替换即可。
///
/// **轴向怎么来的**：不同骨架的局部轴朝向不一样，写死 X/Y/Z 一定会错。
/// 这里在启动时把"角色的左右方向"与"前后方向"用每根骨的 **rest 全局姿态** 反算到它的局部空间，
/// 于是"绕这个轴旋转 = 抬落 / 前后摆"对任何骨架都成立。
///
/// ★ **T38 的两条结构性改动**：
/// 1. **每根骨两个自由度**（原来只有一个）。三段斩要能看出**刀路不同**，
///    单轴旋转做不出"斜斩 / 上挑 / 垂直劈"的区别——那只是同一个动作换幅度。
/// 2. **攻击姿势由逻辑帧算出，不再累加 delta**（卡片硬约束：动画只被逻辑帧驱动）。
///    姿势 = f(AttackState.Frame / TotalFrames)，动画与逻辑读的是同一个帧号，
///    所以 04 §13 的 `ANIM SYNC` 偏差**结构上就是 0**，不存在"跑久了会漂"。
/// </summary>
public class HumanoidAnimator
{
	private static readonly string[] Tracked =
	{
		"Hip", "Spine01", "Spine02", "Head",
		"L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
		"L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
	};

	private float _attackDuration;
	private float _issenDuration;
	private float _hitDuration;

	/// <summary>
	/// 帧 → 秒。**60fps 的基准只有一处**（<see cref="Utils.Frames.PerSecond"/>）。
	///
	/// ⚠️ 这里原来是手写的 `Mathf.Max(1, frames) / Utils.Frames.PerSecond`——
	/// 两边都是 **int**，于是整数除法把小于 60 帧的时长全算成了 **0**。
	/// 后果是攻击/一闪/受击的时长恒为 0，姿势被直接跳过（攻击在玩家眼里就是"瞬间到位"）。
	/// 改用 <see cref="Utils.Frames.ToDelta"/>：它内部除以 `(float)`，不会再截断。
	/// </summary>
	private static float Seconds(int frames) => Utils.Frames.ToDelta(Mathf.Max(1, frames));

	private readonly Skeleton3D? _skel;
	private readonly Node3D _root;
	private readonly Dictionary<string, int> _bone = new();

	/// <summary>每根骨的两个旋转轴（局部空间）：左右抬落轴 / 前后摆动轴。</summary>
	private readonly Dictionary<string, Vector3> _axisSide = new();
	private readonly Dictionary<string, Vector3> _axisRise = new();

	private float _phase;
	private float _bob;

	/// <summary>当前攻击是**第几段**（0 起）。三段各有各的刀路，不是同一招换幅度。</summary>
	private int _attackStep;

	private float _issenElapsed = -1f;
	private float _hitElapsed = -1f;
	private float _hitStrength;

	/// <summary>防御姿态的相位（T38：抬起 / 维持 / 放下——原来只有"举着"这一种）。</summary>
	public enum GuardPhase
	{
		None,
		Raising,
		Held,
		Lowering,
	}

	/// <summary>抬起几帧。</summary>
	public const int GuardRaiseFrames = 8;

	/// <summary>放下几帧。</summary>
	public const int GuardLowerFrames = 10;

	private int _guardFrame = -1;
	private int _guardLowerLeft;

	/// <summary>弹开成功后的专用姿势还剩几帧。**与格挡必须一眼可分**（T38 缺口②验收）。</summary>
	private int _deflectLeft;
	private int _deflectTotal;

	public GuardPhase Phase { get; private set; } = GuardPhase.None;

	/// <summary>弹开姿势是否正在播（自检与调试面板用）。</summary>
	public bool IsDeflecting => _deflectLeft > 0;

	/// <summary>当前这一段攻击的刀路编号（自检用：证明三段确实不是同一个动作）。</summary>
	public int AttackStep => _attackStep;

	public HumanoidAnimator(Node3D modelRoot)
	{
		_root = modelRoot;
		_skel = FindSkeleton(modelRoot);
		if (_skel is null)
			return;

		foreach (string name in Tracked)
		{
			int bone = _skel.FindBone(name);
			if (bone < 0)
				continue;

			_bone[name] = bone;
			Basis rest = _skel.GetBoneGlobalRest(bone).Basis;

			Vector3 side = rest.Inverse() * Vector3.Right;
			Vector3 rise = rest.Inverse() * Vector3.Back;

			_axisSide[name] = side.LengthSquared() > 1e-6f ? side.Normalized() : Vector3.Right;
			_axisRise[name] = rise.LengthSquared() > 1e-6f ? rise.Normalized() : Vector3.Back;
		}
	}

	public bool Valid => _skel is not null && _bone.Count > 0;

	/// <summary>
	/// 开始一段攻击。<paramref name="stepIndex"/> 决定用哪条刀路
	/// （0 上段斜斩 / 1 反手上挑 / 2 双手大上段劈）。
	/// </summary>
	public void PlayAttack(int stepIndex, int totalFrames)
	{
		_attackDuration = Seconds(totalFrames);
		_attackStep = Mathf.Max(0, stepIndex);
	}

	public void PlayIssen(int durationFrames)
	{
		_issenDuration = Seconds(durationFrames);
		_issenElapsed = 0f;
	}

	public void PlayHitReact(float strength, int stunFrames)
	{
		_hitDuration = Seconds(stunFrames);
		_hitElapsed = 0f;
		_hitStrength = Mathf.Clamp(strength, 0f, 2f);
	}

	/// <summary>弹开成功（T38）：短促的顿挫 + 回弹，与"平稳举着剑"的格挡必须一眼分开。</summary>
	public void PlayDeflect(int totalFrames)
	{
		_deflectTotal = Mathf.Max(1, totalFrames);
		_deflectLeft = _deflectTotal;
	}

	/// <summary>
	/// 每帧告诉动画器"防御进行到第几帧"（<c>GuardState.Frame</c>；没在防御时给 -1）。
	/// "放下"那一段由动画器自己记——它要跨过"已经不在防御状态"之后继续播完。
	/// </summary>
	public void TrackGuard(int guardFrame) => _guardFrame = guardFrame;

	public void AnimateLocomotion(float speed01, float delta)
	{
		if (!Valid)
			return;

		_skel!.ResetBonePoses();
		speed01 = Mathf.Clamp(speed01, 0f, 1.5f);
		_phase += delta * Mathf.Lerp(2.5f, 10f, Mathf.Min(speed01, 1f));
		float swing = Mathf.Sin(_phase) * speed01;
		_bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.05f * speed01;

		// 防御三态：抬起 → 维持 → 放下（T38 缺口②的第一半）
		if (_guardFrame >= 0 || _guardLowerLeft > 0)
		{
			ApplyGuardPose();
			return;
		}

		Phase = GuardPhase.None;

        // 摆幅：原来 ±0.8 弧度（全速约 ±46°），但实战里 speed01 只有 ~0.7，
        // 落到腿上只有 27°，配上"没有骨盆"就成了滑步。现在抬到 1.15 并**保底一个最小值**：
        // 只要在走（speed01 > 0.15），腿就至少摆到能看出来的程度——
        // 玩家读的是"腿在迈"，不是"角度精确"。
        float step = Mathf.Max(0.35f, Mathf.Min(speed01, 1f)) * 1.15f;
        Rot("L_Thigh", Mathf.Sin(_phase) * step);
        Rot("R_Thigh", -Mathf.Sin(_phase) * step);
        Rot("L_Calf", -Mathf.Max(0f, Mathf.Sin(_phase)) * step * 1.1f);
        Rot("R_Calf", -Mathf.Max(0f, -Mathf.Sin(_phase)) * step * 1.1f);
        // ★ 骨盆要跟着迈步转（试玩反馈："身体移动腿不动"）。
        // 原来这里**从来没有摆过 Hip**——腿在摆，但骨盆不动、重心不转移，
        // 于是整体观感是"下半身一整块平移过去"，而不是"人在走"。
        // 骨盆一转，两条腿就被"带"起来了，这是走路能被读出来的关键。
        Rot("Hip", swing * 0.22f);
        // 躯干反向微转（对侧手臂与肩的自然联动），幅度小，别抢戏。
        Rot("Spine02", -swing * 0.12f);
		Rot("L_Upperarm", -swing * 0.6f);
		Rot("R_Upperarm", swing * 0.6f);
		Rot("L_Forearm", -Mathf.Abs(swing) * 0.25f);
		Rot("R_Forearm", -Mathf.Abs(swing) * 0.25f);
		Rot("Spine01", 0.12f * Mathf.Min(speed01, 1f));
		Rot("Head", -0.12f * Mathf.Min(speed01, 1f));
	}

	/// <summary>
	/// 每帧的表现层。
	/// <paramref name="attackFrame"/> 是**当前攻击状态自己的帧号**（没在出招时给 -1）——
	/// 攻击姿势由它算出，所以动画与逻辑不可能漂（04 §12：逻辑帧是权威）。
	/// </summary>
	public void AnimateCombat(float delta, int attackFrame)
	{
		if (!Valid)
			return;

		// 松手之后进"放下"段（只触发一次）
		if (_guardFrame < 0 && _guardLowerLeft == 0 && Phase is GuardPhase.Raising or GuardPhase.Held)
			_guardLowerLeft = GuardLowerFrames;

		float rootX = 0f;

		// 优先级：弹开 > 受击 > 一闪 > 攻击。弹开最高——它是本作最需要被看清的一下。
		if (_deflectLeft > 0)
		{
			_deflectLeft--;
			ApplyDeflectPose(1f - _deflectLeft / (float)_deflectTotal);
		}
		else if (_hitElapsed >= 0f)
		{
			_hitElapsed += delta;
			float t = Mathf.Clamp(_hitElapsed / Mathf.Max(0.0001f, _hitDuration), 0f, 1f);
			float falloff = (1f - t) * (1f - t) * _hitStrength;
			Rot("Spine01", -0.5f * falloff);
			Rot("Spine02", -0.3f * falloff);
			Rot("Head", 0.4f * falloff);
			rootX = Mathf.Sin(_hitElapsed * 90f) * 0.05f * falloff;
			if (t >= 1f)
				_hitElapsed = -1f;
		}
		else if (_issenElapsed >= 0f)
		{
			_issenElapsed += delta;
			float t = Mathf.Clamp(_issenElapsed / Mathf.Max(0.0001f, _issenDuration), 0f, 1f);
			float e = 1f - Mathf.Pow(1f - t, 3f);
			Rot("R_Upperarm", Mathf.Lerp(-2.2f, 1.6f, e), Mathf.Lerp(0.5f, -0.2f, e));
			Rot("R_Forearm", Mathf.Lerp(0.3f, -0.4f, e));
			Rot("L_Upperarm", Mathf.Lerp(0.2f, -0.5f, e));
			Rot("Spine01", Mathf.Lerp(0f, 0.25f, e));
			if (t >= 1f)
				_issenElapsed = -1f;
		}
		else if (attackFrame >= 0 && _attackDuration > 0f)
		{
			ApplyAttackPose(attackFrame / Utils.Frames.PerSecond);
		}

		_root.Position = new Vector3(rootX, _bob, 0f);
	}

	/// <summary>
	/// 格挡三态。<paramref name="guardFrame"/> 是进入防御后的帧号；松手后它变成 -1，
	/// 这时用内部计时把"放下"播完（否则剑会瞬间消失，等于没有收招）。
	/// </summary>
	private void ApplyGuardPose()
	{
		float raise = GuardRaiseFrames;

		if (_guardFrame >= 0)
		{
			_guardLowerLeft = 0;

			float t = Mathf.Min(1f, _guardFrame / raise);
			float e = 1f - Mathf.Pow(1f - t, 2f);

			Phase = _guardFrame < GuardRaiseFrames ? GuardPhase.Raising : GuardPhase.Held;

			// 维持段留一点呼吸起伏，不然像块木板
			float breathe = Phase == GuardPhase.Held ? Mathf.Sin(_guardFrame * 0.08f) * 0.03f : 0f;

			ApplyGuardShape(e, breathe);
			return;
		}

		if (_guardLowerLeft > 0)
		{
			_guardLowerLeft--;
			Phase = GuardPhase.Lowering;

			float done = 1f - _guardLowerLeft / (float)GuardLowerFrames;
			ApplyGuardShape(1f - done, 0f);
			return;
		}

		Phase = GuardPhase.None;
	}

	/// <summary>防御姿态本身（<paramref name="e"/> = 1 举到位、0 完全放下）。</summary>
	private void ApplyGuardShape(float e, float breathe)
	{
		Rot("L_Upperarm", Mathf.Lerp(0.2f, 1.05f, e), Mathf.Lerp(0f, -0.35f, e));
		Rot("R_Upperarm", Mathf.Lerp(0.2f, 1.05f, e), Mathf.Lerp(0f, -0.30f, e) + breathe);
		Rot("L_Forearm", Mathf.Lerp(0f, -0.95f, e));
		Rot("R_Forearm", Mathf.Lerp(0f, -1.0f, e));
		Rot("L_Thigh", -0.12f * e);
		Rot("R_Thigh", -0.12f * e);
		Rot("L_Calf", -0.2f * e);
		Rot("R_Calf", -0.2f * e);
		Rot("Spine01", 0.10f * e);
		Rot("Head", 0.06f * e);
	}

	/// <summary>弹开成功：极短促的"接住了"——手腕一震、肩背一紧、人往后一顿。</summary>
	private void ApplyDeflectPose(float t)
	{
		float snap = t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f;
		snap = Mathf.Clamp(snap, 0f, 1f);

		Rot("L_Upperarm", 1.05f - 0.45f * snap, -0.35f - 0.30f * snap);
		Rot("R_Upperarm", 1.05f - 0.35f * snap, -0.30f - 0.45f * snap);
		Rot("L_Forearm", -0.95f - 0.55f * snap);
		Rot("R_Forearm", -1.0f - 0.35f * snap);
		Rot("Spine01", 0.10f - 0.30f * snap);
		Rot("Spine02", -0.18f * snap);
		Rot("Head", 0.06f + 0.22f * snap);
		Rot("L_Thigh", -0.10f);
		Rot("R_Thigh", -0.18f * snap);
		Rot("L_Calf", -0.18f);
		Rot("R_Calf", -0.18f - 0.2f * snap);
	}

	/// <summary>
	/// 三段斩的刀路。**必须看得出来不是同一个动作**（T38 硬约束）：
	/// 0 = 上段斜斩（左肩 → 右下）／1 = 反手上挑（右下 → 左上）／2 = 双手大上段垂直劈。
	/// 每段三拍：起手（0~0.35）→ 出刀（0.35~0.6）→ 收招（0.6~1）。
	/// </summary>
	private void ApplyAttackPose(float elapsed)
	{
		float t = Mathf.Clamp(elapsed / Mathf.Max(0.0001f, _attackDuration), 0f, 1f);

		// 出刀那一拍走得快——节奏本身就是可读性
		float strike = Mathf.Clamp((t - 0.35f) / 0.25f, 0f, 1f);
		float strikeEase = 1f - Mathf.Pow(1f - strike, 3f);

		switch (_attackStep)
		{
			case 0:
				Rot("R_Upperarm", Mathf.Lerp(-2.30f, 1.40f, strikeEase), Mathf.Lerp(0.55f, -0.35f, strikeEase));
				Rot("R_Forearm", Mathf.Lerp(0.30f, -0.35f, strikeEase));
				Rot("L_Upperarm", Mathf.Lerp(0.20f, -0.45f, strikeEase), Mathf.Lerp(0f, 0.25f, strikeEase));
				Rot("Spine01", Mathf.Lerp(-0.18f, 0.26f, strikeEase));
				Rot("Spine02", Mathf.Lerp(-0.10f, 0.16f, strikeEase));
				Rot("Head", Mathf.Lerp(0.08f, -0.06f, strikeEase));
				break;

			case 1:
				// 与第一段的旋转方向**相反**：这就是"刀路不同"而不是"幅度不同"
				Rot("R_Upperarm", Mathf.Lerp(1.75f, -1.95f, strikeEase), Mathf.Lerp(-0.45f, 0.60f, strikeEase));
				Rot("R_Forearm", Mathf.Lerp(-0.55f, 0.25f, strikeEase));
				Rot("L_Upperarm", Mathf.Lerp(-0.25f, 0.35f, strikeEase), Mathf.Lerp(0.30f, -0.20f, strikeEase));
				Rot("Spine01", Mathf.Lerp(0.30f, -0.28f, strikeEase));
				Rot("Spine02", Mathf.Lerp(0.18f, -0.14f, strikeEase));
				Rot("Head", Mathf.Lerp(-0.10f, 0.12f, strikeEase));
				break;

			default:
				// 双手大上段垂直劈：双手举过头顶 → 全力劈下；收招最长
				float up = Mathf.Lerp(-2.55f, 1.50f, strikeEase);
				Rot("R_Upperarm", up, Mathf.Lerp(0.85f, -0.55f, strikeEase));
				Rot("L_Upperarm", up + 0.12f, Mathf.Lerp(0.80f, -0.50f, strikeEase));
				Rot("R_Forearm", Mathf.Lerp(0.45f, -0.60f, strikeEase));
				Rot("L_Forearm", Mathf.Lerp(0.40f, -0.65f, strikeEase));
				Rot("Spine01", Mathf.Lerp(-0.30f, 0.42f, strikeEase));
				Rot("Spine02", Mathf.Lerp(-0.22f, 0.28f, strikeEase));
				Rot("Head", Mathf.Lerp(0.16f, -0.14f, strikeEase));
				Rot("L_Thigh", -0.10f * strikeEase);
				Rot("R_Thigh", -0.22f * strikeEase);
				break;
		}
	}

	/// <summary>绕两个轴设置骨骼姿势。两个自由度才做得出一眼可分的刀路。</summary>
	private void Rot(string name, float side, float rise = 0f)
	{
		if (!_bone.TryGetValue(name, out int bone))
			return;

		Quaternion q = new(_axisSide[name], side);

		if (Mathf.Abs(rise) > 0.0001f)
			q *= new Quaternion(_axisRise[name], rise);

		_skel!.SetBonePoseRotation(bone, q);
	}

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
}
