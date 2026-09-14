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

	// ── 写入者追踪（诊断用，T52 试玩反馈"弹开时腿反着往前弯曲"）──────────
	//
	// 一个姿势函数只写"自己负责的那几根骨"，而**一帧里会有好几个函数依次写同一根骨**
	// （`AnimateLocomotion` 里的格挡 → `AnimateCombat` 里的弹开/受击/攻击）。
	// 结果是：看到一根骨位置不对时，**光看最终坐标无法判断是谁写的**——
	// 我为此在"哪个参数该改"上白猜了好几轮。
	//
	// 所以在 `Rot()` 这**唯一一个写骨点**上记账：谁最后写的、写成了什么值。
	// 这样 bug 就从"腿怎么翘了"变成"**第 N 帧 Transition 把 L_Calf 写成了 -0.18**"。
	//
	// 开销：一次字典写，且只在 `Diagnostics` 打开时做。正式逻辑不许读它
	// （读它等于把表现层当数据源）。
	private readonly Dictionary<string, (string Writer, float Value)> _writers = new();

	/// <summary>当前正在执行的姿势函数名（由各姿势函数入口设置）。</summary>
	private string _poseSource = "none";

	/// <summary>打开后 <see cref="Rot"/> 会记录每根骨的最后写入者。默认关闭。</summary>
	public bool Diagnostics { get; set; }

	/// <summary>
	/// 格挡的整体下沉深度（米）。**这才是"压重心"的主要手段**——
	/// 腿骨旋转只能把膝盖抬起来，髋部不动就没有下蹲这回事（实测：旋腿 → 膝抬高）。
	/// </summary>
	/// <remarks>
	/// 默认值只是**兜底**（测试/探针直接 new 时会用到）；正式路径由
	/// `PlayerActor` 用 `PlayerProfile` 里的值覆盖——下蹲深度是**角色体格**属性，
	/// 不同角色不该共用一个写死的数（铁律 1）。
	/// </remarks>
	public float GuardCrouchDepth { get; set; } = 0.12f;

	/// <summary>格挡腿部角度（rad）。定标用：探针改这两个值再走真实链路测量。</summary>
	public float GuardThighAngle { get; set; } = 0.35f;

	/// <inheritdoc cref="GuardThighAngle"/>
	public float GuardCalfAngle { get; set; } = 0.45f;

	/// <summary>
	/// 所有姿势函数名。探针把 <see cref="_poseSource"/> 设成这一串里的值——
	/// 这样**拼错来源名会在日志里立刻露出来**，而不是悄悄记成别的。
	/// </summary>
	public static readonly string[] AnimatorSources =
	{
		"Locomotion", "Guard", "Deflect", "Hit", "Dodge", "Jump",
		"Heal", "Death", "Broken", "Issen", "Attack",
	};

	/// <summary>某根骨这一帧的最后写入者（没人写则 "none"）。</summary>
	public string LastWriterOf(string bone)
		=> _writers.TryGetValue(bone, out var w) ? w.Writer : "none";

	/// <summary>某根骨这一帧最后被写成的 side 值（没人写则 NaN）。</summary>
	public float LastValueOf(string bone)
		=> _writers.TryGetValue(bone, out var w) ? w.Value : float.NaN;

	/// <summary>清空这一帧的写入记录（探针每帧渲染前调一次）。</summary>
	public void ClearWriters()
	{
		if (Diagnostics)
			_writers.Clear();
	}

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

	/// <summary>
	/// 低于这个速度就当作"站着不动"，腿部摆动必须**完全停住**。
	/// 走路摆幅有一个 `Max(0.35, ...)` 保底，如果不用这个闸门守着，
	/// `speed01 = 0` 也会算出 0.35、腿照摆 ±23°（人物站桩却在蹬腿）。
	/// </summary>
	public const float MinWalkSpeed = 0.15f;

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
	public void TrackGuard(int guardFrame)
	{
		// 探针专用：定标时要让格挡停在"抬到位"（e 饱和）再量腿，
		// 但 `PlayerActor.OnTickVisual` 每帧都会用真实帧号覆盖进来。
		// 所以给探针留一个强制入口——否则探针改的参数永远被真实帧号盖掉，
		// 量出来的全是 0（这正是格挡定标第一版的结果）。
		if (ForcedGuardFrame >= 0)
			guardFrame = ForcedGuardFrame;

		_guardFrame = guardFrame;
	}

	/// <summary>
	/// 探针专用：非负时强制格挡帧号（`-1` = 正常路径）。
	/// 只在姿势定标用；正式逻辑不许读它。
	/// </summary>
	public int ForcedGuardFrame { get; set; } = -1;



	// ── T38 缺口：五个动作（闪避 / 跳跃 / 喝血 / 死亡 / 体干破裂）──────────────
	//
	// 这五条原来在 `PlayerGaps` 的体检表里**全是 0.1°**——意思是"与站着不动一模一样"。
	// 试玩反馈"人物像个木偶"里有一半与骨骼无关：**根本没有对应姿势**。
	//
	// 与 `PlayDeflect` 同一个模式：调用点给出"这一段总共多少帧"，动画器每帧递减一次。
	// 帧数一律由调用方从 `data/**` 或状态里取——**这里不写死任何游戏数值**（铁律 1）。

	/// <summary>闪避还剩几帧（-1 = 不在闪）。四向由切入时锁定的前后 / 左右分量决定。</summary>
	private int _dodgeLeft = -1;
	private int _dodgeTotal = 1;
	private float _dodgeFwd;
	private float _dodgeSide;

	/// <summary>跳跃进行到哪一段（-1 = 不在跳）。0 = 蹬地、1 = 腾空、2 = 落地缓冲。</summary>
	private int _jumpPhase = -1;
	private int _jumpLandFrames = 1;
	private int _jumpLandElapsed;

	/// <summary>喝血：三段边界与当前帧（-1 = 不在喝）。</summary>
	private int _healFrame = -1;
	private int _healStartup;
	private int _healDrink;
	private int _healRecovery;

	/// <summary>倒地 → 撑起（-1 = 没在演）。</summary>
	private int _deathElapsed = -1;
	private int _deathTotal = 1;

	/// <summary>体干破裂还剩几帧（-1 = 没破）。</summary>
	private int _brokenLeft = -1;
	private int _brokenTotal = 1;

	/// <summary>闪避姿势是否正在播（体检与调试面板用）。</summary>
	public bool IsDodging => _dodgeLeft > 0;

	/// <summary>体干破裂姿势是否正在播。</summary>
	public bool IsPostureBroken => _brokenLeft > 0;

	/// <summary>
	/// 闪避（T38）。<paramref name="worldDirection"/> 是**世界空间**的闪避方向——
	/// 在这里换算成"相对当前朝向"的前后 / 左右分量，因为姿势要的是"往哪边扑"。
	/// 闪避全程不转向（<c>DodgeState</c> 的约定），所以切入那一刻算一次就够。
	/// </summary>
	public void PlayDodge(Vector3 worldDirection, int totalFrames)
	{
		_dodgeTotal = Mathf.Max(1, totalFrames);
		_dodgeLeft = _dodgeTotal;

		Vector3 dir = worldDirection;
		dir.Y = 0f;

		if (dir.LengthSquared() <= 1e-6f)
		{
			// 没有方向（理论上进不来）→ 当后跳处理。总比"原地站着播一个蹲"合理。
			_dodgeFwd = -1f;
			_dodgeSide = 0f;
			return;
		}

		dir = dir.Normalized();
		Basis basis = _root.GlobalTransform.Basis;
		// Godot 里"前方"是 -Z、`Basis.X` 是右手边；两个点积就是姿势要的两个分量。
		_dodgeFwd = dir.Dot(-basis.Z);
		_dodgeSide = dir.Dot(basis.X);
	}

	/// <summary>
	/// 跳跃（T38 / T41）。**逐帧轮询**而不是"播一段"——滞空多少帧是物理结果
	/// （起跳初速 ÷ 重力），数据里没有这个数，所以给不出"总帧数"。
	/// <paramref name="phase"/>：0 = 蹬地下蹲、1 = 腾空、2 = 落地缓冲；-1 = 不在跳。
	/// </summary>
	public void TrackJump(int phase, int landRecoveryFrames)
	{
		if (phase != _jumpPhase)
			_jumpLandElapsed = 0;
		else if (phase == 2)
			_jumpLandElapsed++;

		_jumpPhase = phase;
		_jumpLandFrames = Mathf.Max(1, landRecoveryFrames);

		if (phase < 0)
			_jumpLandElapsed = 0;
	}

	/// <summary>喝血（T38）：三段帧数由调用方给（<c>ActorStats.Heal*Frames</c>）。</summary>
	public void PlayHeal(int startupFrames, int drinkFrames, int recoveryFrames)
	{
		_healStartup = Mathf.Max(0, startupFrames);
		_healDrink = Mathf.Max(0, drinkFrames);
		_healRecovery = Mathf.Max(0, recoveryFrames);
		_healFrame = 0;
	}

	/// <summary>
	/// 倒地 → 撑起（T38）。在**复活演出**里播（<c>ReviveState</c>）——
	/// 原来这里复用的是一次大幅受击，读起来只是"抖了一下"，不是"倒下去再爬起来"。
	/// </summary>
	public void PlayDeath(int totalFrames)
	{
		_deathTotal = Mathf.Max(1, totalFrames);
		_deathElapsed = 0;
	}

	/// <summary>
	/// 体干破裂（T38 / T37 缺口②）：破防那 50 帧硬直必须有**可读的惩罚姿态**，
	/// 否则玩家只觉得自己"卡住了"，而不知道为什么。
	/// </summary>
	public void PlayGuardBreak(int totalFrames)
	{
		_brokenTotal = Mathf.Max(1, totalFrames);
		_brokenLeft = _brokenTotal;
	}

	public void AnimateLocomotion(float speed01, float delta)
	{
		_poseSource = "Locomotion";
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
			// 格挡要**整体下沉**：光靠腿骨旋转做不到（旋腿只会把膝盖抬起来，
			// 髋部不动就没有"压重心"这回事）。所以这里让根骨骼降下去，
			// 腿只做**轻微**的弯曲配合——那样看到的是"人矮了一截"，
			// 而不是"腿翘起来了"（T52 试玩："按住右键格挡的姿势也有问题"）。
			float crouch = ApplyGuardPose();
			_root.Position = new Vector3(0f, -crouch, 0f);

			return;
		}

		Phase = GuardPhase.None;

        // 摆幅：原来 ±0.8 弧度（全速约 ±46°），但实战里 speed01 只有 ~0.7，
        // 落到腿上只有 27°，配上"没有骨盆"就成了滑步。现在抬到 1.15 并**保底一个最小值**：
        // 只要在走（speed01 > 0.15），腿就至少摆到能看出来的程度——
        // 玩家读的是"腿在迈"，不是"角度精确"。
        //
        // ★ 保底必须由 MinWalkSpeed 守着。`Mathf.Max(0.35f, ...)` 本身对 speed01 = 0
        //   也会返回 0.35，于是**站桩不动时腿照样以 ±23° 摆动**（swing 被 speed01 乘成 0，
        //   但 step 没被乘）——人物站着不动却在蹬腿，看起来就是"姿势坏了"。
        //   注释里写的 "speed01 > 0.15" 以前从来没进过代码。
        float step = speed01 > MinWalkSpeed
            ? Mathf.Max(0.35f, Mathf.Min(speed01, 1f)) * 1.15f
            : 0f;
        Rot("L_Thigh", -Mathf.Sin(_phase) * step);
        Rot("R_Thigh", Mathf.Sin(_phase) * step);
        Rot("L_Calf", Mathf.Max(0f, Mathf.Sin(_phase)) * step * 1.1f);
        Rot("R_Calf", Mathf.Max(0f, -Mathf.Sin(_phase)) * step * 1.1f);
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
		float rootY = 0f;

		// 优先级：死亡 > 体干破裂 > 弹开 > 受击 > 闪避 > 跳跃 > 喝血 > 一闪 > 攻击。
		//
		// 前两个（死亡 / 破防）压在最上面，因为它们是**终局与惩罚**：躺下去的人不该被
		// "站着"的姿势盖回来，被打崩的那一下也不该被普通格挡的模样吃掉。
		// 弹开仍然高于受击——它是本作最需要被看清的一下（08 §5）。
		if (_deathElapsed >= 0)
		{
			_poseSource = "Death";
			_deathElapsed++;
			float t = Mathf.Clamp(_deathElapsed / (float)_deathTotal, 0f, 1f);
			rootY = ApplyDeathPose(t);
			if (t >= 1f)
				_deathElapsed = -1;
		}
		else if (_brokenLeft > 0)
		{
			_poseSource = "Broken";
			_brokenLeft--;
			rootY = ApplyBrokenPose(1f - _brokenLeft / (float)_brokenTotal);
		}
		else if (_deflectLeft > 0)
		{
			_poseSource = "Deflect";
			_deflectLeft--;
			ApplyDeflectPose(1f - _deflectLeft / (float)_deflectTotal);
		}
		else if (_hitElapsed >= 0f)
		{
			_poseSource = "Hit";
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
		else if (_dodgeLeft > 0)
		{
			_poseSource = "Dodge";
			_dodgeLeft--;
			rootY = ApplyDodgePose(1f - _dodgeLeft / (float)_dodgeTotal);
		}
		else if (_jumpPhase >= 0)
		{
			_poseSource = "Jump";
			rootY = ApplyJumpPose(_jumpPhase, _jumpLandElapsed / (float)_jumpLandFrames);
		}
		else if (_healFrame >= 0)
		{
			_poseSource = "Heal";
			_healFrame++;
			rootY = ApplyHealPose();
			if (_healFrame > _healStartup + _healDrink + _healRecovery)
				_healFrame = -1;
		}
		else if (_issenElapsed >= 0f)
		{
			_poseSource = "Issen";
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
			_poseSource = "Attack";
			// ★ 必须是 Utils.Frames.ToDelta(attackFrame)，**不能写 attackFrame / Utils.Frames.PerSecond**：
			//   PerSecond 是 int 常量，`int / int` 走整数除法——attackFrame 在 0..59 之间时结果恒为 0，
			//   于是 ApplyAttackPose 的 t 恒为 0、strike 恒为 0、ease 恒为 0，
			//   **三段斩从头到尾都停在"起手"那一拍**（实测：0/8/16 帧姿势逐位相同，R_Upperarm 恒 133.7°）。
			//   后果：玩家看到的是"举着刀不动、然后瞬间切回收招"，没有出刀过程——
			//   这是"完全没有打击感"里与骨骼无关的那一半。
			ApplyAttackPose(Utils.Frames.ToDelta(attackFrame));
		}

		// 动作姿势自带高度变化（下蹲 / 腾空 / 跪地），这时**不要**再叠走路的上下起伏——
		// 否则"跪到地上"会被 ±5cm 的步行 bob 顶得发抖。
		float baseY = rootY != 0f ? 0f : _bob;
		_root.Position = new Vector3(rootX, baseY + rootY, 0f);
	}

	/// <summary>
	/// 格挡三态。<paramref name="guardFrame"/> 是进入防御后的帧号；松手后它变成 -1，
	/// 这时用内部计时把"放下"播完（否则剑会瞬间消失，等于没有收招）。
	/// </summary>
	private float ApplyGuardPose()
	{
		_poseSource = "Guard";
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
			return -GuardCrouchDepth * e;
		}

		if (_guardLowerLeft > 0)
		{
			_guardLowerLeft--;
			Phase = GuardPhase.Lowering;

			float done = 1f - _guardLowerLeft / (float)GuardLowerFrames;
			float lowered = 1f - done;
			ApplyGuardShape(lowered, 0f);
			return -GuardCrouchDepth * lowered;
		}

		Phase = GuardPhase.None;
		return 0f;
	}

	/// <summary>防御姿态本身（<paramref name="e"/> = 1 举到位、0 完全放下）。</summary>
	/// <summary>
	/// 格挡架势。**必须一眼看出"我在防"**——这是被人试玩打回的（T51）：
	/// 上一版的格挡只抬了手臂，而相机在角色**背后**，手臂正好被躯干和头挡住，
	/// 于是"格挡中"和"站着不动"的画面差异只有 **0.47%** 的像素
	/// （同一台游戏相机实测：出刀是 10.86%）。玩家的原话是"进入了但不明显"。
	///
	/// 所以现在不靠手臂，改靠**三件在背后视角下也看得见的事**：
	/// 1. **重心下沉**——两条腿都弯，人整体矮下去（轮廓立刻变矮）；
	/// 2. **侧身**——骨盆和脊柱一起转，肩膀线从"正对"变成"侧对"（轮廓变窄）；
	/// 3. 手臂仍然是主姿态，但抬得更高、更前，让它从躯干两侧**露出来**。
	///
	/// 手臂那两行才是"格挡"的语义，腿和脊柱是让语义**可见**的。两者都不能省。
	/// </summary>
	private void ApplyGuardShape(float e, float breathe)
	{
		_poseSource = "Guard";
		// ① 手臂：抬起来挡在身前。侧轴 1.30 rad ≈ 74°（抬臂），
		//    rise 轴负值是让它往前收，不是往外张——往外张就成了"张开双臂"。
		Rot("L_Upperarm", Mathf.Lerp(0.2f, 1.55f, e), Mathf.Lerp(0f, -0.80f, e));
		Rot("R_Upperarm", Mathf.Lerp(0.2f, 1.45f, e), Mathf.Lerp(0f, -0.70f, e) + breathe);
		Rot("L_Forearm", Mathf.Lerp(0f, -1.45f, e));
		Rot("R_Forearm", Mathf.Lerp(0f, -1.35f, e));

		// ② 重心下沉：两条腿弯下去，人矮一截。
		//    上一版只有 0.12/0.2 rad，从背后几乎看不出来；实测像素差异仅 0.47%。
		//
		// ⚠️ 这四个角度**参数化**了（`GuardThighAngle` 等），不是为了"可配置"，
		//    而是因为本轮踩了一个坑：**探针直接写骨测出来的数字和真实链路不一致**
		//    （探针测到脚离地 65cm，真实链路是 33cm）。姿势定标必须让探针改
		//    **这里**的值再跑真实链路，否则量的是另一个东西。
		Rot("L_Thigh", GuardThighAngle * e);
		Rot("R_Thigh", GuardThighAngle * 0.86f * e);
		Rot("L_Calf", GuardCalfAngle * e);
		Rot("R_Calf", GuardCalfAngle * 0.90f * e);

		// ③ 侧身：骨盆先转，脊柱跟上（分两段才像"人转过来"而不是"整块板子转"）。
		//    负值 = 把左肩送向前方，玩家从背后看到的是"肩膀斜了"。
		Rot("Hip", -0.46f * e);
		Rot("Spine01", 0.22f * e);
		Rot("Spine02", -0.18f * e);
		Rot("Head", 0.06f * e + breathe * 0.5f);
	}

	/// <summary>弹开成功：极短促的"接住了"——手腕一震、肩背一紧、人往后一顿。</summary>
	private void ApplyDeflectPose(float t)
	{
		_poseSource = "Deflect";
		float snap = t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f;
		snap = Mathf.Clamp(snap, 0f, 1f);

		DeflectPoseLog?.Invoke(t, snap);

		Rot("L_Upperarm", 1.05f - 0.45f * snap, -0.35f - 0.30f * snap);
		Rot("R_Upperarm", 1.05f - 0.35f * snap, -0.30f - 0.45f * snap);
		Rot("L_Forearm", -0.95f - 0.55f * snap);
		Rot("R_Forearm", -1.0f - 0.35f * snap);
		Rot("Spine01", 0.10f - 0.30f * snap);
		Rot("Spine02", -0.18f * snap);
		Rot("Head", 0.06f + 0.22f * snap);

		// ★ 腿部（T52 试玩反馈："弹开时主角的腿反着往前弯曲"）。
		//
		// 这里原来是 `Rot("L_Calf", -0.18f)` / `Rot("R_Calf", -0.18f - 0.2f * snap)` ——
		// **小腿符号弄反了**，实测（`scenes/tests/LegPose.tscn`）：
		//   静止站姿   踝膝高差 -46.8 cm（脚在膝下，正常）
		//   弹开第12帧 踝膝高差 -46.7 cm（修好后正常）
		// 修之前那一帧是 **+43.5 cm：脚跑到膝盖上方**，看起来就是腿朝身前折过去。
		//
		// 判据不是"看哪边像前"——本模型 Hip 的 +Z 其实是**身后**，
		// 猜轴猜错过两次。唯一可靠的基准是**和正常站姿比**：脚必须低于膝。
		Rot("L_Thigh", -0.10f);
		Rot("R_Thigh", -0.18f * snap);
		Rot("L_Calf", 0.18f);
		Rot("R_Calf", 0.18f + 0.2f * snap);
	}

	/// <summary>
	/// 弹开姿势每帧的 <c>(t, snap)</c> 回放口。**只为探针/自检存在**，
	/// 正式逻辑不许读它（读它等于把表现层当数据源）。
	/// </summary>
	public System.Action<float, float>? DeflectPoseLog { get; set; }

	// ── 五个新动作的姿势（T38）──────────────────────────────────────────
	//
	// 每条都**覆盖全部 12 根被追踪的骨**。这不是啰嗦，是必须：
	// `PlayerActor.OnTickVisual` 每帧先 `AnimateLocomotion`（它自己 `ResetBonePoses()`
	// 之后摆走路/格挡姿势），再由 `AnimateCombat` **覆盖**。少写一根骨，
	// 那根就会漏出"走路"的摆动——"一边倒地一边迈腿"正是这么来的。

	/// <summary>
	/// 闪避（T38）。**重心先沉下去、再贴着地滑出去**——"变矮"是背后视角下唯一
	/// 读得出来的信号（与 T51 修格挡同一个教训：相机在角色背后）。
	/// 方向决定往哪边倾：前后分量压脊柱，左右分量让两腿分开 ＋ 身体侧倾。
	/// </summary>
	private float ApplyDodgePose(float t)
	{
		_poseSource = "Dodge";
		// 快出慢回：前 35% 是"扑出去"，之后收回站姿
		float env = t < 0.35f
			? Mathf.Sin(t / 0.35f * Mathf.Pi * 0.5f)
			: 1f - (t - 0.35f) / 0.65f;
		env = Mathf.Clamp(env, 0f, 1f);

		float fwd = _dodgeFwd * env;
		float side = _dodgeSide * env;

		// ① 腿：沉下去，并按方向分腿（往哪边闪，哪条腿先蹬）
		Rot("L_Thigh", 1.05f * env + side * 0.30f);
		Rot("R_Thigh", 0.85f * env - side * 0.30f);
		Rot("L_Calf", 1.30f * env);
		Rot("R_Calf", 1.05f * env);

		// ② 脊柱：前后分量把上身压下去，左右分量让身体侧倾
		Rot("Hip", fwd * 0.50f, side * 0.55f);
		Rot("Spine01", fwd * 0.38f, side * 0.45f);
		Rot("Spine02", fwd * 0.22f, side * 0.30f);
		Rot("Head", -fwd * 0.32f, -side * 0.35f);

		// ③ 手臂收拢贴身。不收的话是"张开双臂扑出去"，读起来像失了平衡、不像垫步。
		Rot("L_Upperarm", 1.30f * env, -0.55f * env + side * 0.25f);
		Rot("R_Upperarm", 1.20f * env, -0.50f * env + side * 0.25f);
		Rot("L_Forearm", -1.55f * env);
		Rot("R_Forearm", -1.45f * env);

		// 人真的矮下去。腿弯了而人没矮 = 看起来像"腿折了"。
		return -0.26f * env;
	}

	/// <summary>
	/// 跳跃（T38）。三段分开做，因为它们的**轮廓完全不同**：
	/// 蹬地 = 蹲下去、腾空 = 收腿、落地 = 深蹲再站直。
	/// "腿收没收起来"是"人在空中"与"人在蹲着"唯一的区别，所以腾空段靠它。
	/// </summary>
	private float ApplyJumpPose(int phase, float landT)
	{
		_poseSource = "Jump";
		if (phase == 0)
		{
			Rot("L_Thigh", 1.10f); Rot("R_Thigh", 1.10f);
			Rot("L_Calf", 1.45f); Rot("R_Calf", 1.45f);
			Rot("Hip", 0.30f); Rot("Spine01", 0.34f); Rot("Spine02", 0.22f);
			Rot("Head", -0.30f);
			Rot("L_Upperarm", -0.55f); Rot("R_Upperarm", -0.55f);
			Rot("L_Forearm", -0.25f); Rot("R_Forearm", -0.25f);
			return -0.34f;
		}

		if (phase == 1)
		{
			Rot("L_Thigh", 1.35f); Rot("R_Thigh", 1.15f);
			Rot("L_Calf", 2.00f); Rot("R_Calf", 1.80f);
			Rot("Hip", -0.18f); Rot("Spine01", -0.12f); Rot("Spine02", -0.08f);
			Rot("Head", 0.14f);
			Rot("L_Upperarm", 1.90f); Rot("R_Upperarm", 1.80f);
			Rot("L_Forearm", -0.90f); Rot("R_Forearm", -0.85f);
			return 0.16f;
		}

		// 落地缓冲：从深蹲插值回站立。平方让前几帧最深、最后迅速站直。
		float e = 1f - Mathf.Clamp(landT, 0f, 1f);
		e *= e;
		Rot("L_Thigh", 1.25f * e); Rot("R_Thigh", 1.10f * e);
		Rot("L_Calf", 1.60f * e); Rot("R_Calf", 1.45f * e);
		Rot("Hip", 0.34f * e); Rot("Spine01", 0.38f * e); Rot("Spine02", 0.24f * e);
		Rot("Head", -0.34f * e);
		Rot("L_Upperarm", -0.45f * e); Rot("R_Upperarm", -0.45f * e);
		Rot("L_Forearm", -0.30f * e); Rot("R_Forearm", -0.30f * e);
		return -0.30f * e;
	}

	/// <summary>
	/// 喝血（T38）。三段**必须分得清**——这是 02 §2.4"不背板"设计里唯一给玩家的时间感：
	/// 掏壶（还能被打断）／饮用（喝下去了）／收招（快好了）。
	/// 最可读的是饮用段：**手举到嘴边 ＋ 头后仰**，这个轮廓在任何视角都不会认错。
	/// </summary>
	private float ApplyHealPose()
	{
		_poseSource = "Heal";
		int frame = _healFrame;
		int drinkStart = _healStartup;
		int recoveryStart = _healStartup + _healDrink;

		if (frame < drinkStart)
		{
			// ① 掏壶：右手收到胸前，身体微微沉下去
			float e = _healStartup <= 0 ? 1f : Mathf.Clamp(frame / (float)_healStartup, 0f, 1f);
			e = 1f - Mathf.Pow(1f - e, 2f);

			Rot("R_Upperarm", Mathf.Lerp(-0.10f, 1.10f, e), Mathf.Lerp(0f, -0.85f, e));
			Rot("R_Forearm", Mathf.Lerp(0f, -1.30f, e));
			Rot("L_Upperarm", Mathf.Lerp(-0.10f, -0.35f, e));
			Rot("L_Forearm", -0.20f * e);
			Rot("Hip", 0.10f * e); Rot("Spine01", 0.12f * e); Rot("Spine02", 0.08f * e);
			Rot("Head", 0.14f * e);
			Rot("L_Thigh", 0.40f * e); Rot("R_Thigh", 0.34f * e);
			Rot("L_Calf", 0.55f * e); Rot("R_Calf", 0.48f * e);
			return -0.12f * e;
		}

		if (frame < recoveryStart)
		{
			// ② 饮用：手举到嘴边、头后仰 —— 全三段里唯一的轮廓
			float e = _healDrink <= 0 ? 1f : Mathf.Clamp((frame - drinkStart) / (float)_healDrink, 0f, 1f);
			// 举到 35% 到顶，之后回一点：让这一段自己有起伏，不是一块冰
			float lift = e < 0.35f ? e / 0.35f : 1f - (e - 0.35f) * 0.35f;

			Rot("R_Upperarm", Mathf.Lerp(1.10f, 1.70f, lift), Mathf.Lerp(-0.85f, -1.25f, lift));
			Rot("R_Forearm", Mathf.Lerp(-1.30f, -2.00f, lift));
			Rot("L_Upperarm", -0.35f); Rot("L_Forearm", -0.20f);
			Rot("Hip", 0.06f);
			Rot("Spine01", -0.10f * lift); Rot("Spine02", -0.08f * lift);
			Rot("Head", -0.55f * lift);            // ← 头后仰，最可读的一笔
			Rot("L_Thigh", 0.40f); Rot("R_Thigh", 0.34f);
			Rot("L_Calf", 0.55f); Rot("R_Calf", 0.48f);
			return -0.12f;
		}

		// ③ 收招：手放下、身体站直（剩下的时间自动过渡回 idle）
		float outE = _healRecovery <= 0
			? 0f
			: 1f - Mathf.Clamp((frame - recoveryStart) / (float)_healRecovery, 0f, 1f);

		Rot("R_Upperarm", 0.40f * outE); Rot("R_Forearm", -0.45f * outE);
		Rot("L_Upperarm", -0.10f * outE);
		Rot("Spine01", -0.06f * outE);
		Rot("Head", -0.20f * outE);
		Rot("L_Thigh", -0.14f * outE); Rot("R_Thigh", -0.12f * outE);
		Rot("L_Calf", -0.20f * outE); Rot("R_Calf", -0.18f * outE);
		return -0.04f * outE;
	}

	/// <summary>
	/// 倒地 → 撑起（T38）。**这是"死了"与"挨了一刀"的区别**：
	/// 受击是上半身一抖（人还站着），这里是**腿跪下去、身体折下去**，
	/// 轮廓从"竖着的人"变成"缩在地上的一团"。
	/// </summary>
	private float ApplyDeathPose(float t)
	{
		_poseSource = "Death";
		// 0 ~ 0.42 倒下 → 0.42 ~ 0.55 伏着 → 0.55 ~ 1 撑起来
		float fall = t < 0.42f ? t / 0.42f : 1f;
		float rise = t <= 0.55f ? 0f : Mathf.Clamp((t - 0.55f) / 0.45f, 0f, 1f);

		// 后段被"撑起"的进度抵消 —— 同一套数字走完"倒下再起来"
		float down = Mathf.Clamp(fall - rise, 0f, 1f);
		float downEase = down * down * (3f - 2f * down);   // smoothstep：落下去要沉

		Rot("L_Thigh", -1.75f * downEase); Rot("R_Thigh", -1.60f * downEase);
		Rot("L_Calf", -2.35f * downEase); Rot("R_Calf", -2.15f * downEase);
		Rot("Hip", 0.55f * downEase, 0.35f * downEase);
		Rot("Spine01", 0.95f * downEase); Rot("Spine02", 0.70f * downEase);
		Rot("Head", 0.62f * downEase);
		Rot("L_Upperarm", -0.55f * downEase, 0.45f * downEase);
		Rot("R_Upperarm", -0.62f * downEase, 0.40f * downEase);
		Rot("L_Forearm", 0.35f * downEase); Rot("R_Forearm", 0.32f * downEase);

		return -0.62f * downEase;   // 整个人矮一大截 = 跪到地上了
	}

	/// <summary>
	/// 体干破裂（T38 / T37 缺口②）。**必须与格挡一眼可分**：
	/// 格挡是"手臂抬到身前、身体侧过去"，破防是**手臂垂下去、上半身向后折**——
	/// 几乎每条骨的方向都相反。玩家要能一眼看出"我被打崩了"，而不是"我还在防"。
	/// </summary>
	private float ApplyBrokenPose(float t)
	{
		_poseSource = "Broken";
		// 前 30% 猛地一拉，之后维持：硬直期间一直保持"崩了"的样子
		float env = t < 0.3f ? t / 0.3f : 1f;
		env = 1f - Mathf.Pow(1f - env, 2f);

		Rot("L_Upperarm", -0.85f * env, 0.30f * env);
		Rot("R_Upperarm", -0.95f * env, 0.26f * env);
		Rot("L_Forearm", 0.30f * env); Rot("R_Forearm", 0.26f * env);
		Rot("Hip", -0.30f * env, -0.20f * env);
		Rot("Spine01", -0.62f * env);       // 上半身向后折（与格挡的前倾相反）
		Rot("Spine02", -0.42f * env);
		Rot("Head", -0.50f * env);
		Rot("L_Thigh", -0.95f * env); Rot("R_Thigh", -0.55f * env);   // 单膝先沉
		Rot("L_Calf", 1.30f * env); Rot("R_Calf", -0.85f * env);

		return -0.20f * env;
	}

	/// <summary>
	/// 三段斩的刀路。**必须看得出来不是同一个动作**（T38 硬约束）：
	/// 0 = 上段斜斩（左肩 → 右下）／1 = 反手上挑（右下 → 左上）／2 = 双手大上段垂直劈。
	/// 每段三拍：起手（0~0.35）→ 出刀（0.35~0.6）→ 收招（0.6~1）。
	/// </summary>
	private void ApplyAttackPose(float elapsed)
	{
		_poseSource = "Attack";
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
			break;
				}

		// ★ **弓步**：踏进去，而不是站在原地挥手。
		//
		// 这一段是试玩反馈"砍上去像滑过去"的另一半：上一版只有持刀臂在动，
		// `Hip` 与双腿实测**全程 0.0°**——人平移 0.8~1.5 米而腿一动不动。
		// 两条腿**反向**转（前腿迈出、后腿蹬地）才读得出"这一步是踩下去的"，
		// 同向转只会变成下蹲。三段方向一致（都是右手刀），幅度逐段加大：
		// 第三段是双手大上段劈，跨得最开。
		float lunge = 0.32f + 0.18f * _attackStep;      // 0.32 / 0.50 / 0.68 弧度
		Rot("L_Thigh", -lunge * strikeEase);
		Rot("R_Thigh", lunge * 0.78f * strikeEase);
		Rot("L_Calf", -lunge * 0.55f * strikeEase);
		Rot("R_Calf", -lunge * 0.22f * strikeEase);

		// 骨盆跟着刀路转：第一/三段刀向前压（+），第二段反手刀路反着来（-）。
		// 骨盆一转，重心就转移了——这是"人"和"木偶"的分界线。
		Rot("Hip", (_attackStep == 1 ? -0.22f : 0.22f) * strikeEase);
	}

	/// <summary>绕两个轴给骨骼加一个**增量**旋转。两个自由度才做得出一眼可分的刀路。</summary>
	private void Rot(string name, float side, float rise = 0f)
	{
		if (!_bone.TryGetValue(name, out int bone))
			return;

		// ★ 唯一写骨点：诊断记账在这里做。
		//   不要在每个 Apply* 里各记一次——那会漏掉"绕过来的第二次写入"，
		//   而"最后写入者是谁"正是要查的东西。
		//
		//   代价是个查询：`SetSource` 只在编辑期用，运行时传的是 `AnimatorSources` 里的
		//   常量字符串，所以正式逻辑下这行不进诊断分支。
		if (Diagnostics)
			_writers[name] = (_poseSource, side);

		Quaternion q = new(_axisSide[name], side);

		if (Mathf.Abs(rise) > 0.0001f)
			q *= new Quaternion(_axisRise[name], rise);

		// ★ 必须是「rest 再叠加 q」，不能是「把旋转设成 q」。
		//
		// `SetBonePoseRotation` 是**替换**：它把骨骼的局部旋转整个写成你给的四元数。
		// 这只有在骨架的 rest 旋转等于单位时才等价于"转一点"，而本模型的骨架不是：
		//
		//     Hip / Spine01 / Spine02 / Head   rest 旋转 =   0.0°   → 恰好等价 ✔
		//     L_Thigh / R_Thigh                rest 旋转 = 180.0°   → 传 0 就把腿整个翻过来 ✘
		//     L_Upperarm / R_Upperarm          rest 旋转 = 101.4°   → 传 0 就把手抬到水平 ✘
		//
		// 症状：站着不动时两条腿在大腿根翻了 180°（"像一张纸被翻折"）、双臂横张成 T 型。
		// 走路时 `Rot(..., ±0.4)` 更糟——大腿的 ±23° 摆动被当成"绝对值"用，
		// 于是腿不是前后摆，而是从"完全翻过去"这个基准上乱转。
		//
		// 顺手解释了一个老问题：`AnimProbe` 的"武器骨全局偏转 115.4°"和
		// "两腿交替夹角差 131.8°"都特别大——因为它比的是被替换后的姿势，
		// 里面混着这 180°，并不是真的摆动幅度。
		Quaternion rest = _skel!.GetBoneRest(bone).Basis.GetRotationQuaternion();
		_skel.SetBonePoseRotation(bone, rest * q);
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
