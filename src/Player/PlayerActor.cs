using System;
using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Dev;
using Oniblade.Progression;

namespace Oniblade.Player;

/// <summary>
/// 玩家。它只负责两件事：**把输入翻译成意图**，以及**把状态翻译成画面**。
/// 判定、伤害、体干全部交给 <see cref="CombatActor"/> 与裁决器。
/// </summary>
public partial class PlayerActor : CombatActor, IGuardInput, IAttackEvasionListener, IHealHost
{
	private static readonly PlayerAction[] WatchedActions = Enum.GetValues<PlayerAction>();

	[Export] public float MouseSensitivity { get; set; } = 0.0025f;

	/// <summary>跳跃参数（T41）。见 <see cref="JumpProfile"/>。</summary>
	[Export] public JumpProfile? JumpProfile { get; set; }
	[Export] public float MinPitch { get; set; } = -70f;
	[Export] public float MaxPitch { get; set; } = 40f;

	/// <summary>招式表（data/attacks/player/player_combo.tres）。</summary>
	[Export] public PlayerAttackSet? Attacks { get; set; }

	/// <summary>难度档。弹开窗、输入缓冲这些宽容参数都从它读，不许写死在代码里。</summary>
	[Export] public DifficultyProfile? Difficulty { get; set; }

	/// <summary>
	/// 弹开窗指示器的外观（`data/player/deflect_cue.tres`）。缺省时不建指示器，
	/// 游戏照常能玩——它只是反馈，不参与任何判定。
	/// </summary>
	[Export] public DeflectCueProfile? DeflectCue { get; set; }

	/// <summary>
	/// 处决参数（`data/combat/deathblow.tres`，T52）：距离、演出帧数、无敌帧、伤害。
	/// 缺省时**处决不能触发**（而不是退化成写死的默认值）——这样"忘了配资源"会
	/// 立刻表现为"按 F 没反应"，而不是悄悄用一个没人审过的数字。
	/// </summary>
	[Export] public DeathblowProfile? Deathblow { get; set; }

	[Export] public Color BodyColor { get; set; } = new(0.22f, 0.26f, 0.34f);
	[Export] public Color AccentColor { get; set; } = new(0.55f, 0.16f, 0.14f);

	/// <summary>
	/// 可选的角色外观模型（例如 <c>assets/models/model_player_congyun_01.glb</c>）。
	/// 配置后用它替换灰盒 <see cref="BlockoutRig"/>——注意静态模型没有骨架，
	/// 只是整体跟随角色变换，不能做四肢动画；灰盒仍然在跑（不可见），
	/// 等有绑骨模型时把这里换掉即可。
	/// </summary>
	[Export] public PackedScene? VisualModel { get; set; }

	/// <summary>外观模型缩放（AI 生成模型被归一化到 ~1.0 单位高，设定身高 ~175cm）。</summary>
	[Export] public float VisualModelScale { get; set; } = 1.75f;

	/// <summary>外观模型朝向修正（导入模型的正脸不一定朝 -Z）。</summary>
	[Export] public Vector3 VisualModelRotationDegrees { get; set; } = Vector3.Zero;


	/// <summary>相机支点相对脚底的高度（米）。</summary>
	[Export] public float CameraHeight { get; set; } = 1.45f;

	/// <summary>
	/// 闪避速度倍率（× <see cref="CombatActor.MoveSpeed"/>）。
	/// 位移 = 无敌帧全速 + 后摇线性衰减，所以 2.4 倍大约是一个身位的垫步。
	/// 放在 <c>[Export]</c> 而不是 .tres 里，是因为它只影响灰盒观感、不影响判定。
	/// </summary>
	[Export] public float DodgeSpeedScale { get; set; } = 2.4f;

	/// <summary>
	/// 一闪的扫描半径（米）。比杂兵 2.2m 的攻击距离略大——
	/// 02 §2.3 的窗口是**时间**上的（命中前 0~N 帧），距离只是"这一刀我要不要搭理"的门槛。
	/// </summary>
	[Export] public float IssenScanRange { get; set; } = 3.0f;

	/// <summary>玩家不会被一闪秒杀（01 文档：任何机制都不该一击终结玩家）。</summary>
	public override EnemyTier IssenTier => EnemyTier.Boss;

	/// <summary>玩家不掉魄——掉的是魔骸的魄（03 §6.1）。</summary>
	public override bool DropsSoulOnDeath => false;

	private Node3D _cameraPivot = null!;
	private SpringArm3D _springArm = null!;
	private BlockoutRig _rig = null!;
	private HumanoidAnimator? _skinAnimator;

	/// <summary>探针专用：拿到表现层的 animator（T52 腿部体检需要它的回放口）。</summary>
	public HumanoidAnimator? SkinAnimatorForProbe => _skinAnimator;

	private readonly PlayerInputBuffer _buffer = new();

	/// <summary>本地帧号，只给"快速重按防御"判定用（02 §8）。</summary>
	private int _localFrame;

	/// <summary>死亡后到原地重开的剩余帧数（T14）。0 = 没在等重开。</summary>
	private int _restartCountdownFrames;

	/// <summary>复活无敌帧的倒计时（T22）：起身演出 + 演出后的一段，都算无敌。</summary>
	private int _reviveInvulnerableFramesLeft;

	/// <summary>上一次松开防御的帧号；从未松过为 -1。</summary>
	private int _lastGuardReleaseFrame = -1;

	private bool _guardHeldLastFrame;

	/// <summary>噬魂笼手：自动牵引魄、连吸反馈、深吸、侵蚀记账。</summary>
	public OniGauntlet Gauntlet { get; private set; } = null!;

	public int InputBufferFrames => Difficulty?.InputBufferFrames ?? 8;

	public override string InputBufferDebug
	{
		get
		{
			var sb = new System.Text.StringBuilder();
			foreach (PlayerAction action in WatchedActions)
			{
				int age = _buffer.Age(action);
				if (age < 0)
					continue;

				if (sb.Length > 0)
					sb.Append(", ");
				sb.Append(action.ToString().ToLowerInvariant()).Append('(').Append(age).Append(')');
			}

			return sb.ToString();
		}
	}

	// ── T37 缺口②③：破防表现 与 半自动防御 ─────────────────────

	/// <summary>
	/// 破防累计次数（本场战斗）。**自检靠它证明基类那个空钩子真的被覆写了**——
	/// 只看"状态机进了 StaggerState"证明不了这件事（敌人也是那么进的）。
	/// </summary>
	public int GuardBreakCount { get; private set; }

	/// <summary>破防的后仰姿态还在放（给截图与自检用）。</summary>
	public bool IsShowingGuardBreak => _guardBreakShowFrames > 0;

	/// <summary>半自动防御累计触发了几次（跨额度窗口，留给自检看）。</summary>
	public int HalfAutoGuardTriggerCount { get; private set; }

	/// <summary>当前额度窗口内已经用掉几次。</summary>
	public int HalfAutoGuardUsedInWindow => _autoGuardUsedInWindow;

	private int _guardBreakShowFrames;

	// ── 蓄力斩（T46）────────────────────────────────────────────
	// **轻攻击仍然是按下瞬间出**（M1 的手感不能动）。
	// 蓄力是"**按住不放**"才会走到的那条路：松开时若已过某段阈值，就打那一记。
	// 与《只狼》一致——先挥一刀，按住转蓄力，松开出蓄力斩。
	private int _attackHoldFrames;
	private bool _attackHeldLastFrame;
	private int _pendingReleaseHoldFrames;

	/// <summary>最近一次蓄力斩的段位（0 ＝ 没有）。测试用。</summary>
	public int LastChargedLevel { get; private set; }

	/// <summary>打出过几次蓄力斩。测试用。</summary>
	public int ChargedCount { get; private set; }

	/// <summary>最近一次按下攻击键一共按住了多少帧。测试用。</summary>
	public int LastAttackHoldFrames { get; private set; }
	private int _autoGuardWindowStartFrame = int.MinValue;
	private int _autoGuardUsedInWindow;

	protected override void OnActorReady()
	{
		// 敌人（含挥砍假人）靠这个组找玩家——和 EnemyController 用的是同一个约定。
		AddToGroup("player");

		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
		_springArm.RotationDegrees = new Vector3(-12f, 0f, 0f);

		// ★ 相机臂必须和身体朝向**解耦**。
		// 它挂在玩家下面，若跟着身体转就会形成反馈回路：
		// 移动方向来自相机 → 角色转向移动方向 → 相机跟着转 → 移动方向又变了……
		// 表现就是"边移动边原地打转"。TopLevel 让它只吃世界变换，
		// 位置由 SyncCameraRig() 每帧手动跟随身体。
		_cameraPivot.TopLevel = true;

		_rig = new BlockoutRig();
		_rig.Build(BodyColor, AccentColor, true);
		AddChild(_rig);

		if (VisualModel is not null)
		{
			var visual = VisualModel.Instantiate<Node3D>();
			visual.Name = "VisualModel";
			visual.Scale = Vector3.One * VisualModelScale;
			visual.RotationDegrees = VisualModelRotationDegrees;
			AddChild(visual);
			_rig.Visible = false;
			_skinAnimator = new HumanoidAnimator(visual)
		{
			// 下蹲深度是**角色体格**属性，从 data/actors 读（铁律 1：数值不进 C#）。
			GuardCrouchDepth = Stats?.GuardCrouchDepth ?? 0f,
		};
		}


		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		BuildDeflectCue();

		// 笼手挂在玩家身上，吸附点就是玩家位置。
		Gauntlet = new OniGauntlet { Name = "OniGauntlet" };
		AddChild(Gauntlet);
		if (EventBus.Instance is { } bus)
			Gauntlet.Attach(bus);

		// 带满次数进场（BOSS 战前 / 死亡重开后由 T14 调 RefillHealCharges）。
		RefillHealCharges();
		RefillRevives();

		// T37 缺口①：难度档的体干恢复旋钮要在这里接上，否则四档手感完全一样。
		ApplyDifficulty();

		if (Attacks is not null)
			Machine.Get<AttackState>().Configure(Attacks.BuildLightCombo());

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	/// <summary>
	/// 在基类的四个状态之上注册防御层（02 文档 §1）：
	/// <see cref="GuardState"/> 按住格挡、<see cref="DeflectState"/> 弹开成功后的 12 帧、
	/// <see cref="DodgeState"/> 垫步闪避（T13）。
	/// </summary>
	protected override void RegisterStates(StateMachine machine)
	{
		base.RegisterStates(machine);
		machine.Add(new GuardState());
		machine.Add(new DeflectState());
		machine.Add(new DodgeState());
		machine.Add(new HealState());
		machine.Add(new IssenState());
		machine.Add(new ReviveState());
		machine.Add(new ChargedAttackState());
		machine.Add(new JumpState());
		// T52：处决演出。**必须注册**——没注册时 `Change<T>()` 是静默丢弃的，
		// 症状是"按 F 完全没反应"，一点报错都没有。
		machine.Add(new DeathblowExecuteState());
	}

	/// <summary>防御键是否按住（<see cref="IGuardInput"/>）。敌人不实现它，所以不受防御状态影响。</summary>
	public bool IsGuardHeld => Input.IsActionPressed("guard");

	private void SyncCameraRig()
	{
		_cameraPivot.GlobalPosition = GlobalPosition + new Vector3(0f, CameraHeight, 0f);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_cameraPivot.RotateY(-motion.Relative.X * MouseSensitivity);
			_springArm.RotateX(-motion.Relative.Y * MouseSensitivity);

			Vector3 springRotation = _springArm.Rotation;
			springRotation.X = Mathf.Clamp(springRotation.X, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			_springArm.Rotation = springRotation;
		}

		if (@event.IsActionPressed("pause"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	/// <summary>
	/// 在顿帧判断**之前**采集输入（见 <see cref="CombatActor._PhysicsProcess"/>）：
	/// 顿帧期间玩家提前按下的攻击必须进缓冲，否则会被"卡帧"吞掉，
	/// 那正是玩家最容易觉得"我明明按了"的时刻。
	/// </summary>
	protected override void PollLocalInput()
	{
		SyncCameraRig();
		_localFrame++;
		_buffer.Tick();

		// 复活无敌帧在这里递减：它要跨过 ReviveState 的边界继续生效，
		// 所以不能挂在状态里（状态退出时就会一起消失）。
		if (_reviveInvulnerableFramesLeft > 0)
			_reviveInvulnerableFramesLeft--;

		// 破防后仰同理：它要盖住 StaggerState 的前半段。
		if (_guardBreakShowFrames > 0)
			_guardBreakShowFrames--;

		// 记录"松开防御"的那一帧，供快速重按判定使用。
		bool guardHeld = Input.IsActionPressed("guard");
		if (_guardHeldLastFrame && !guardHeld)
			_lastGuardReleaseFrame = _localFrame;
		_guardHeldLastFrame = guardHeld;

		// 「深吸」：按住交互键强行吸魄，代价是侵蚀（03 §6.1）。
		//
		// ★ 顺序（T52）：**处决优先于深吸**。两者共用交互键，而深吸是"按住持续生效"、
		// 处决是"只在这一瞬间的窗口"。若让深吸先吃掉按键，玩家在破韧窗口里按住 F
		// 会变成"一直在吸魄、就是不处决"——这是交互键复用最容易踩的坑。
		// 完整的优先级阶梯在 `DeathblowResolver.PriorityOrder`（含对话与教学收刀）。
		bool deepAbsorb = Input.IsActionPressed("interact");
		if (Input.IsActionJustPressed("interact") && TryEnterDeathblow())
			deepAbsorb = false;

		Gauntlet.SetDeepAbsorbing(deepAbsorb);

		if (Input.IsActionJustPressed("attack"))
			_buffer.Push(PlayerAction.Attack);
		if (Input.IsActionJustPressed("guard"))
			_buffer.Push(PlayerAction.Guard);
		if (Input.IsActionJustPressed("dodge"))
			_buffer.Push(PlayerAction.Dodge);
		if (Input.IsActionJustPressed("item_use"))
			_buffer.Push(PlayerAction.ItemUse);

		// 顺序即优先级：补给最后生效。
		// 三个方法都用延迟切换（Change/ForceChange），同一个物理帧里
		// 后面的调用会覆盖前面的 —— 所以"保命动作优先于补给动作"是白拿的。
		// 蓄力斩（T46）**故意排在最前**：它是慢招，同一帧里保命动作应该盖过它。
		TrackAttackCharge();
		TryEnterChargedAttack();
		TryEnterHeal();
		TryEnterDodge();
		TryEnterJump();
		TryEnterGuard();

		// 半自动防御（T37 缺口③）**必须排在手动之后**：
		// 手动弹开已经成的时候不许它抢功、更不许扣额度（见 TryHalfAutoGuard）。
		TryHalfAutoGuard();
	}

	/// <summary>
	/// 处决（T52）：破韧窗口内按交互键 → 演出 + 全程无敌。
	///
	/// **触发键是交互键 F/E，不是普攻键** —— 这条是项目主人的硬裁定：
	/// 同键会让"砍几刀再处决"退化成"一想补刀就进处决"，
	/// 玩家就再也感受不到"我先打崩它、再从容补刀"的节奏了。
	/// 所以本方法的**唯一**调用点是 <c>PollLocalInput</c> 里的交互键分支，
	/// 普攻链（<c>ConsumeAttackInput</c> 那条路）永远碰不到它。
	///
	/// 目标选择与距离判定在纯逻辑类 <see cref="DeathblowResolver"/> 里（有单测），
	/// 本方法只负责"拿到最近的可处决目标 → 配置状态 → 切入"。
	/// </summary>
	private bool TryEnterDeathblow()
	{
		// 处决参数缺省时**不出招**（而不是退化成一个写死的默认值）：
		// 忘了配 `data/combat/deathblow.tres` 应当表现为"按 F 没反应"，
		// 让人一眼看出是配置问题，而不是悄悄用一个没人审过的数字。
		if (Deathblow is null || IsDead)
			return false;

		// 已经在自己的一套动作里时不打断自己（同 TryEnterGuard 的纪律）
		if (Machine.Current is AttackState or ChargedAttackState or HealState
			or IssenState or DeathblowExecuteState or JumpState)
			return false;

		IDeathblowTarget? target = FindNearestDeathblowTarget();
		if (target is null)
			return false;

		DeathblowExecuteState state = Machine.Get<DeathblowExecuteState>();
		state.TotalFramesValue = Deathblow.TotalFrames;
		state.InvulnerableFrames = Deathblow.InvulnerableFrames;
		state.HitFrameValue = Deathblow.HitFrame;
		state.Damage = Deathblow.Damage;
		state.Target = target as CombatActor;

		Machine.Change<DeathblowExecuteState>();

		// ★ **必须告诉目标"你正在被处决"**（T52 端到端第一次跑就抓到了这一条）。
		//
		// 漏掉这一句的症状极其隐蔽：玩家侧演出照常播、伤害照常落地、敌人照常死，
		// 所以"看起来全对"。但敌人其实一直停在 `PostureBrokenState` 里——
		// 而**破韧态本身就不动**，于是"演出期间目标被钉住"这条断言照样通过。
		// 换句话说，少调这一句，整条链路唯一的外在表现是
		// **"被处决"这个动作从来没播过**（8 个动作里白做了一个）。
		target.BeginBeingExecuted(Deathblow.TotalFrames);

		// 转向目标：处决必须"面朝着它打"，否则演出里刀是朝空气挥的。
		if (target is Node3D node)
		{
			Vector3 toTarget = node.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			if (toTarget.LengthSquared() > 0.0001f)
				Rotation = new Vector3(0f, Mathf.Atan2(-toTarget.X, -toTarget.Z), 0f);
		}

		return true;
	}

	/// <summary>
	/// 场上离得最近的、能处决的目标。
	///
	/// 用 <see cref="IsInstanceValid"/> 过滤：敌人被打死时节点会先释放，
	/// 只判 null 会拿到失效引用（本项目修过同类缺陷）。而且这里**每帧都重查**，
	/// 不做缓存——处决窗口只有 2 秒，缓存晚一帧就意味着窗口白白流走。
	/// </summary>
	private IDeathblowTarget? FindNearestDeathblowTarget()
	{
		if (Deathblow is null)
			return null;

		var candidates = new List<DeathblowCandidate>();
		var targets = new List<IDeathblowTarget>();

		foreach (Node node in GetTree().GetNodesInGroup(CombatActor.GroupName))
		{
			if (!IsInstanceValid(node) || node is not IDeathblowTarget target)
				continue;

			targets.Add(target);
			candidates.Add(DeathblowCandidate.Make(
				target.GlobalPosition.X,
				target.GlobalPosition.Z,
				target.CanBeExecuted));
		}

		int index = DeathblowResolver.Resolve(
			new System.Numerics.Vector2(GlobalPosition.X, GlobalPosition.Z),
			Deathblow.MaxDistance,
			candidates);

		return index >= 0 && index < targets.Count ? targets[index] : null;
	}

	/// <summary>跳跃（T41）。**只在"能自由行动"的状态起跳**——闪避/受击/一闪期间按跳不算，
	/// 否则玩家能在受罚时用跳跃把自己救出来，那就等于给了一个免费的取消手段。
	/// 数值全部来自 <see cref="JumpProfile"/>（data/player/jump.tres）。
	/// </summary>
	private void TryEnterJump()
	{
		if (!Input.IsActionJustPressed("jump") || IsDead)
			return;

		if (Machine.Current is not (IdleState or MoveState))
			return;

		if (!Machine.Has<JumpState>())
			return;

		if (JumpProfile is not null)
			Machine.Get<JumpState>().Configure(JumpProfile.TakeoffSpeed, JumpProfile.LandRecoveryFrames);

		Machine.ForceChange<JumpState>();
	}

	/// <summary>
	/// 跳跃进行到哪一段（T38 的动画要它）：-1 = 没在跳、0 = 蹬地、1 = 腾空、2 = 落地缓冲。
	///
	/// **为什么用物理量反推、而不是问 `JumpState`**：`JumpState` 的 `_airborne` /
	/// `_landedFrames` 都是私有的，可它们说的本就是"离没离地、落地多久了"——
	/// `IsOnFloor()` 与 `Velocity.Y` 已经把同一件事讲清楚了：起跳那几帧脚还在地上、
	/// 但 Y 速度已经朝上，这正是"蹬地"。为了让动画读同一件事去给状态类开三个只读属性，
	/// 不如直接问物理（也少一处需要同步的接口）。
	/// </summary>
	private int JumpAnimPhase()
	{
		if (Machine.Current is not JumpState)
			return -1;

		if (!IsOnFloor())
			return 1;                                   // 腾空

		return Velocity.Y > 0.01f ? 0 : 2;              // 蹬地 / 落地缓冲
	}

	/// <summary>
	/// 跟踪攻击键的**按住时长**，并在松开的那一帧记下来（T46）。
	/// 判定本身在 <see cref="ChargedAttackSelector"/> 里（纯逻辑，有单测）；
	/// 这里只负责数帧。
	/// </summary>
	private void TrackAttackCharge()
	{
		bool held = Input.IsActionPressed("attack");

		if (Input.IsActionJustPressed("attack"))
			_attackHoldFrames = 0;
		else if (held)
			_attackHoldFrames++;

		if (!held && _attackHeldLastFrame)
		{
			LastAttackHoldFrames = _attackHoldFrames;
			_pendingReleaseHoldFrames = _attackHoldFrames;
			_attackHoldFrames = 0;
		}

		_attackHeldLastFrame = held;
	}

	/// <summary>
	/// 松开攻击键后，若按住时长够了就打蓄力斩。阈值**只从招式数据读**（各段的 StartupFrames），
	/// 不在代码里写死。够不着第一段就什么都不做——那次按键已经在按下时走过普通轻攻击了。
	/// </summary>
	private void TryEnterChargedAttack()
	{
		if (_pendingReleaseHoldFrames <= 0)
			return;

		int hold = _pendingReleaseHoldFrames;
		_pendingReleaseHoldFrames = 0;

		if (IsDead || Attacks is null)
			return;

		// 只能从"能出招"的状态起。一闪 / 闪避 / 喝血 / 硬直期间按出来的蓄力一律不算——
		// 尤其是**一闪优先**：那次按键在按下时已经被 TryIssen 判过了。
		if (Machine.Current is not (IdleState or MoveState or AttackState))
			return;

		int level = ChargedAttackSelector.ResolveLevel(
			hold,
			Attacks.Charged1?.StartupFrames ?? int.MaxValue,
			Attacks.Charged2?.StartupFrames ?? int.MaxValue,
			Attacks.Charged3?.StartupFrames ?? int.MaxValue);

		AttackData? data = level switch
		{
			3 => Attacks.Charged3,
			2 => Attacks.Charged2,
			1 => Attacks.Charged1,
			_ => null,
		};

		if (data is null || !Machine.Has<ChargedAttackState>())
			return;

		Machine.Get<ChargedAttackState>().Configure(data);
		Machine.ForceChange<ChargedAttackState>();

		LastChargedLevel = level;
		ChargedCount++;
	}

	/// <summary>
	/// 防御键 → <see cref="GuardState"/>（T6 规则 1 与规则 3）。
	///
	/// **刻意不走输入缓冲**：防御是"按住"的持续状态，不是一次动作。
	/// 走缓冲会把"松开一帧再按下"变成看不见的连打，正好绕开取消硬直。
	///
	/// 取消来源只有两种，其余一律按"从站立进入"处理：
	/// - 攻击中：只有进入后摇的取消窗之后才允许被防御取消（02 §1 取消表），
	///   在那之前按住防御不生效——后摇该走完就得走完；
	/// - 受击硬直：允许直接取消进防御（T6 规则 3 把"受击"列为取消来源）。
	/// 这两种都算 <see cref="GuardEntrySource.Cancel"/>，代价是前几帧弹不开——
	/// **受罚的是效率，不是存活**。
	/// </summary>
	private void TryEnterGuard()
	{
		if (IsDead || !Input.IsActionPressed("guard"))
			return;

		// 已经站在防御里 / 正在弹开收招里，不要重复切入。
		if (Machine.Current is GuardState or DeflectState)
			return;

		GuardEntrySource source;
		switch (Machine.Current)
		{
			case AttackState attack:
				if (!IsAttackCancelWindowOpen(attack))
					return;

				source = GuardEntrySource.Cancel;
				break;

			case StaggerState:
				source = GuardEntrySource.Cancel;
				break;

			// 闪避后摇可以被防御取消（T13 规则 4），但**无敌帧内不让**：
			// 那几帧正是闪避的全部价值，被防御取消等于把玩家的 i-frame 吃掉，
			// 表现出来就是"我明明闪了却还是被打中"。注意按下防御的那一刻
			// DodgeState 的帧号还是 0，所以必须查 IsInvulnerableNow 而不是"有没有在闪"。
			case DodgeState dodge:
				if (dodge.IsInvulnerableNow)
					return;

				source = GuardEntrySource.Neutral;
				break;

			// 喝血：**饮用段**不能被防御自己取消。
			// 那一段的全部承诺就是"玩家永远能喝完"，而按住防御是玩家的常态姿势——
			// 若允许取消，实际效果就是"只要按着防御就永远喝不进去"。
			// 起手（掏壶）与收招（放回）允许取消，这是 02 §2.4 明文写的。
			case HealState heal:
				if (heal.Phase == HealPhase.Drink)
					return;

				source = GuardEntrySource.Neutral;
				break;

			// 一闪（含安全窗与落空）**整段不可取消**，防御也不例外（02 §2.3）。
			// 这里是 ForceChange 的入口，IssenState.CanTransitionTo 拦不住它，必须显式拒绝。
			// 安全窗那一段本身就在格挡姿态里，所以"按早了不挨打"不受这条影响。
			case IssenState:
				return;

			// 复活起身整段不可取消（T22）：防御也不行，你正在从地上爬起来。
			case ReviveState:
				return;

			default:
				source = GuardEntrySource.Neutral;
				break;
		}

		// 02 §8：松开防御后马上又按下 → 这一段防御**直接不开窗**（仍然格挡）。
		// 只"晚开 4 帧"是不够的：按住 ≥5 帧时前后窗口会首尾相接，等于永远开着。
		if (source != GuardEntrySource.Cancel
			&& GuardReentry.IsQuickReentry(_localFrame, _lastGuardReleaseFrame, Difficulty?.GuardReentryLockFrames ?? 0))
		{
			source = GuardEntrySource.QuickReentry;
		}

		EnterGuard(source);
	}

	/// <summary>
	/// 真正切进 <see cref="GuardState"/>。**手动与半自动防御共用这一条路**
	/// （T37 实现纪律：不许另写一套"更宽松的窗口"，否则两套规则日后必然打架）。
	///
	/// 弹开窗只能由 <see cref="CombatTuning"/> 合成（08 §3 P1-3 红线）。
	/// 缺难度档时退化到最窄的可玩窗口（下限 4 帧），不会凭空变强。
	/// </summary>
	private void EnterGuard(GuardEntrySource source)
	{
		GuardState guard = Machine.Get<GuardState>();
		guard.EntrySource = source;
		guard.CancelLockFrames = Difficulty?.GuardCancelLockFrames ?? 0;
		guard.DeflectWindowFrames = CombatTuning.ResolveDeflectWindowFrames(Difficulty?.DeflectWindowFrames ?? 0);
		guard.MoveScale = Stats?.GuardMoveScale ?? 1f;

		Machine.ForceChange<GuardState>();
	}

	/// <summary>
	/// 半自动防御（T37 缺口③ / 05 §126-128）。**只有最简单档（見習）开**，语义照抄规格：
	///
	/// - 触发条件：**按住防御**，且**手动弹开还没成**，敌人这一招**距判定还有 ≤2 帧**；
	/// - **只对「一般攻击」生效，对「危」攻击一律不生效**；
	/// - **每 10 秒最多 3 次**——它不是无敌，是把"精准时机"换成"资源管理"。
	///
	/// 实现方式刻意选"**在命中前 2 帧重进一次 GuardState**"：
	/// 走的是 <see cref="EnterGuard"/>（= 手动弹开那条路），窗口宽度照旧由
	/// <see cref="CombatTuning"/> 合成，裁决器那边完全不知道有这回事。
	/// 重进时用 <see cref="GuardEntrySource.Neutral"/>，所以不付取消硬直、当帧之后立即开窗。
	/// </summary>
	private void TryHalfAutoGuard()
	{
		if (IsDead || Difficulty is not { HalfAutoGuard: true })
			return;

		// 必须"按住防御"——半自动防的是"按早了"，不是"没按"。
		if (!Input.IsActionPressed("guard"))
			return;

		if (Machine.Current is DeflectState or IssenState or ReviveState)
			return;

		// 喝血的饮用段不能被任何东西取消（T18 承诺"永远能喝完"），半自动也不例外。
		if (Machine.Current is HealState { Phase: HealPhase.Drink })
			return;

		// 手动弹开已经成了：不抢功，也不扣额度。
		if (DeflectWindowFramesLeft > 0)
			return;

		if (!FindThreat(out ThreatPhase phase, out int framesUntilActive, out bool perilous))
			return;

		// 只在"还没打出来"时给，且必须已经进入提前量之内。
		if (phase != ThreatPhase.Windup)
			return;
		if (framesUntilActive > Difficulty.HalfAutoGuardLeadFrames)
			return;

		// 05 §128：危攻击一律不生效——它该吃危的那条规则（弹开或闪避）。
		if (perilous)
			return;

		if (!TryConsumeHalfAutoGuardCharge())
			return;

		HalfAutoGuardTriggerCount++;
		EnterGuard(GuardEntrySource.Neutral);
	}

	/// <summary>
	/// 扣一次半自动防御的额度。规格是"每 10 秒最多 3 次"，
	/// 所以这里是一个**固定窗口**：窗口内用完就不给了，等下一个窗口。
	/// 阈值全部来自难度档（T37 硬约束：2 帧 / 3 次 / 10 秒不许写死在 C# 里）。
	/// </summary>
	private bool TryConsumeHalfAutoGuardCharge()
	{
		int windowFrames = Mathf.Max(1, Difficulty!.HalfAutoGuardWindowFrames);
		int maxTriggers = Mathf.Max(0, Difficulty.HalfAutoGuardMaxTriggers);

		bool windowExpired = _autoGuardWindowStartFrame == int.MinValue
			|| _localFrame - _autoGuardWindowStartFrame >= windowFrames;

		if (windowExpired)
		{
			_autoGuardWindowStartFrame = _localFrame;
			_autoGuardUsedInWindow = 0;
		}

		if (_autoGuardUsedInWindow >= maxTriggers)
			return false;

		_autoGuardUsedInWindow++;
		return true;
	}

	// ── T37 缺口①②：接上难度旋钮 / 破防表现 ────────────────────

	/// <summary>
	/// 把难度档里"与玩家体干有关"的旋钮写进玩家的表（T37 缺口①）。
	///
	/// 为什么要有这个方法：<c>DifficultyProfile.PlayerPostureRegenScale</c> 四档都填了值，
	/// 但**代码里没有任何地方读它**，于是"给手残玩家更快回架势"（05）这条可及性杠杆是空的。
	/// 照既有用法——**由持有 <c>Difficulty</c> 的那一侧写入**（和
	/// <c>GuardState.CancelLockFrames</c> / <c>DodgeState.InvulnerableFrames</c> 同一个手法）。
	/// </summary>
	public void ApplyDifficulty()
	{
		Posture.RegenScale = Difficulty?.PlayerPostureRegenScale ?? 1f;
	}

	/// <summary>
	/// 玩家破防（T37 缺口②）。基类是空方法，<c>TrainingDummy</c> 覆写了、**玩家没有**——
	/// 所以破防时只有状态机进了 50 帧硬直，人物姿态上什么都没发生，
	/// 玩家只会觉得"我卡住了"，而不知道为什么。
	///
	/// 这里做两件事：记一个**可观测**的标记（自检证明这个钩子真的被调过），
	/// 以及放一个**明显区别于普通格挡的姿态**。
	///
	/// **T38 之后**：真模型有了**专属破防姿势**——手臂垂下去、上半身向后折，
	/// 与格挡"抬手到身前"的形状正好相反（实测两者差 144°）。
	/// 灰盒 `BlockoutRig` 仍然复用受击通道，它只有那一种表达。
	/// </summary>
	protected override void OnPostureBroken()
	{
		GuardBreakCount++;
		_guardBreakShowFrames = GuardBreakShowFrames;
		_rig?.PlayGuardBreak();
		_skinAnimator?.PlayGuardBreak(GuardBreakShowFrames);

		// ★ 必须清架势，和 TrainingDummy / AttackingDummy 的 OnPostureBroken 保持一致。
		// 不清的后果是**死亡螺旋**：PostureMeter.Tick 在 IsBroken 时直接 return（不回复），
		// 于是玩家被破防一次之后架势永远停在满值，之后每次格挡都立刻再破防，
		// 除了死一次（复活流程里才有 Reset）没有任何出路。
		Posture.Reset();
	}

	/// <summary>破防后仰持续多少帧（≈50 帧硬直的前半段，和 <c>GuardBreakStunFrames</c> 同量级）。</summary>
	public const int GuardBreakShowFrames = 34;

	/// <summary>半自动防御的剩余次数（给 HUD 的弱提示）。非見習档恒 0。</summary>
	public override int HalfAutoGuardChargesLeftForUi => Difficulty is { HalfAutoGuard: true }
		? Mathf.Max(0, Difficulty.HalfAutoGuardMaxTriggers - _autoGuardUsedInWindow)
		: 0;

	/// <summary>攻击后摇的取消窗开了没有（02 §1：轻斩壹从后摇第 6 帧起可被防御取消）。</summary>
	private static bool IsAttackCancelWindowOpen(AttackState attack) =>
		attack.Sequence.IsRunning && attack.Sequence.Current.CanCancelAt(attack.Sequence.Frame);

	/// <summary>
	/// 闪避键 → <see cref="DodgeState"/>（T13 / 02 §2.2）。
	///
	/// 与防御**刻意不同**，闪避走**输入缓冲**：02 §8 要求"闪避缓冲 10 帧，连打闪避键不会空"，
	/// 而且攻击后摇里预输入的闪避必须等到取消窗打开那一刻才生效。
	/// 缓冲里的一帧输入只会被消费一次，所以不会出现"一次按键连闪两下"。
	///
	/// 方向由输入侧算好写进状态——相机臂是 TopLevel、和身体解耦（见 OnActorReady），
	/// 状态类拿不到相机，也不该知道相机在哪。
	/// </summary>
	private void TryEnterDodge()
	{
		if (IsDead || Machine.Current is StaggerState or DodgeState)
			return;

		// 喝血的饮用段不能被闪避自己取消（理由同 TryEnterGuard 里的注释）。
		if (Machine.Current is HealState healing && healing.Phase == HealPhase.Drink)
			return;

		// 攻击中：只有进入后摇的取消窗之后才允许被闪避取消（02 §1 取消表）。
		// 窗口没开时**不消费缓冲**，让这次输入继续躺在缓冲里等窗口打开。
		if (Machine.Current is AttackState attack && !IsAttackCancelWindowOpen(attack))
			return;

		if (!_buffer.Consume(PlayerAction.Dodge, Difficulty?.DodgeBufferFrames ?? 0))
			return;

		DodgeState dodge = Machine.Get<DodgeState>();
		dodge.InvulnerableFrames = Difficulty?.DodgeIFrames ?? 0;
		dodge.RecoveryFrames = Difficulty?.DodgeRecoveryFrames ?? 0;
		dodge.Speed = MoveSpeed * DodgeSpeedScale;

		Vector3 dodgeDirection = ResolveDodgeDirection();
		dodge.Direction = dodgeDirection;

		Machine.Change<DodgeState>();

		// T38：闪避的姿势要**知道往哪边扑**——左右闪与前后闪的轮廓不一样。
		// 方向在这一刻锁定（闪避全程不转向），所以传一次就够；
		// 帧数取"无敌 + 后摇"，两个数都来自难度档，动画器里不存任何数值。
		_skinAnimator?.PlayDodge(dodgeDirection, dodge.InvulnerableFrames + dodge.RecoveryFrames);
	}

	/// <summary>闪避方向：有方向输入就朝那个方向闪；没有就后跳（相对相机向后退）。</summary>
	private Vector3 ResolveDodgeDirection()
	{
		if (TryGetMoveIntent(out MoveIntent intent) && intent.Direction.LengthSquared() > 0.0001f)
			return intent.Direction.Normalized();

		// 相机基的 +Z 是"镜头背面"方向，沿它走就是远离视线 = 后跳。
		Vector3 backward = _cameraPivot.GlobalTransform.Basis.Z;
		backward.Y = 0f;
		return backward.Normalized();
	}

	/// <summary>无敌帧（02 §4 规则 1：裁决器直接给 Miss，连一闪都打不中）。</summary>
	public override bool IsInvulnerableNow =>
		base.IsInvulnerableNow
		|| _reviveInvulnerableFramesLeft > 0
		|| (Machine is { Current: DodgeState dodge } && dodge.IsInvulnerableNow)
		// T52 处决：**演出期间全程无敌**。
		//
		// 这里刻意**不新增裁决分支**：`CombatResolver` 规则 1 已经在读
		// `DefenderSnapshot.IsInvulnerable`（它由这个属性填充），判 Miss 之后
		// 连一闪都打不中。于是"处决期间另一个敌人来砍，玩家不掉血"
		// **是白拿的**——而且对**所有**敌人同时生效，不用一个个去改。
		|| (Machine is { Current: DeathblowExecuteState deathblow } && deathblow.IsInvulnerableNow);

	/// <summary>本场战斗成功躲开的攻击次数（完美闪避的证据，T13 端到端测试断言它）。</summary>
	public int EvadedAttackCount { get; private set; }

	/// <summary>
	/// 裁决器判了 Miss（<see cref="IAttackEvasionListener"/>）——这是"完美闪避"唯一的信息来源：
	/// Miss 不走 <c>ReceiveVerdict</c>，所以 <c>OnVerdictReceived</c> 永远等不到它。
	/// </summary>
	public void OnAttackEvaded(CombatActor attacker)
	{
		EvadedAttackCount++;

		if (Machine is { Current: DodgeState dodge })
			dodge.OnAttackEvaded();
	}

	// ── 喝血（T18 / 02 §2.4）──────────────────────────────────────

	/// <summary>还剩几次喝血。次数耗尽后按 <c>item_use</c> 不会有任何反应。</summary>
	public int HealChargesLeft { get; private set; }

	/// <summary>
	/// 补满喝血次数。BOSS 战前与死亡重开后调用（01 §0 规则 1：
	/// 死亡不永久消耗资源，所以补给必须是免费且自动的）。
	/// T18 只负责把它做成公开方法，真正的调用点在 T14 的重开协议里。
	/// </summary>
	public void RefillHealCharges() => HealChargesLeft = Stats?.HealCharges ?? 0;

	void IHealHost.ConsumeHealCharge()
	{
		if (HealChargesLeft > 0)
			HealChargesLeft--;
	}

	int IHealHost.ApplyHeal(int requested)
	{
		int before = Health.Current;
		Health.Heal(requested);
		return Health.Current - before;
	}

	/// <summary>
	/// 喝血键（<c>item_use</c>）→ <see cref="HealState"/>。
	///
	/// 与攻击/闪避一样走输入缓冲：按了就有反应，不会因为在后摇里而被吞掉。
	/// 没次数时**不消费缓冲**（提前 return），所以"没次数"这件事不会被误判成"按了没用"。
	/// </summary>
	private void TryEnterHeal()
	{
		if (IsDead || HealChargesLeft <= 0)
			return;

		if (Machine.Current is StaggerState or HealState or DodgeState)
			return;

		// 攻击中：只有后摇的取消窗之后才允许被喝血取消（与防御/闪避同一张表）。
		if (Machine.Current is AttackState attack && !IsAttackCancelWindowOpen(attack))
			return;

		if (!_buffer.Consume(PlayerAction.ItemUse, InputBufferFrames))
			return;

		HealState heal = Machine.Get<HealState>();
		heal.StartupFrames = Stats?.HealStartupFrames ?? 0;
		heal.DrinkFrames = Stats?.HealDrinkFrames ?? 0;
		heal.RecoveryFrames = Stats?.HealRecoveryFrames ?? 0;
		heal.HealAmount = Mathf.RoundToInt((Stats?.MaxHealth ?? 100) * (Stats?.HealPercent ?? 0f));

		Machine.Change<HealState>();

		// T38：喝血的三段姿势（掏壶 / 饮用 / 收招）。帧数直接来自状态里刚写入的三个字段——
		// 动画与逻辑读的是同一组数字，所以"手举到嘴边"那一拍**结构上**就落在饮用段里（04 §12）。
		_skinAnimator?.PlayHeal(heal.StartupFrames, heal.DrinkFrames, heal.RecoveryFrames);
	}

	// ── 死亡与原地重开（T14 / 01 §0 规则 1）─────────────────────

	/// <summary>
	/// 死亡分两条路（T22 + T14）：
	/// 还有复活次数 → **当场站起来**；次数用尽 → 交给 <see cref="BattleReset"/> 原地重开。
	/// 两条都不掉持久资源（01 §0 规则 1）。
	/// </summary>
	protected override void OnDeath()
	{
		if (RevivesLeft > 0)
		{
			RevivesLeft--;
			EnterRevive();
			return;
		}

		_restartCountdownFrames = Stats?.DeathRestartDelayFrames ?? 0;
	}

	// ── 复活（T22）──────────────────────────────────────────────

	/// <summary>还剩几次复活。用尽之后死亡会走 T14 的原地重开。</summary>
	public int RevivesLeft { get; private set; }

	/// <summary>HUD 的"血瓶圆点"读这个（T31）。</summary>
	public override int HealChargesLeftForUi => HealChargesLeft;

	/// <summary>
	/// 补满复活次数。次数来自**难度档**（05 §2 的难度表：修罗 1 / 武士 1 / 剑客 2 / 見習 3），
	/// 所以它是"可及性杠杆"而不是写死的常量。
	/// 进场与重开后各调一次（01 §0 规则 1：死亡没有持久性惩罚）。
	/// </summary>
	public void RefillRevives() => RevivesLeft = Difficulty?.ReviveCount ?? 0;

	/// <summary>
	/// 当场站起来（T22）。
	///
	/// ⚠️ 必须把 <c>IsDead</c> 置回 false：<see cref="CombatActor"/>._PhysicsProcess 在
	/// <c>IsDead</c> 时会**直接 return**，状态机根本不会推进——不置回去的话
	/// 玩家会永久躺在地上，看起来像卡死。
	/// （<c>IsDead</c> 是 <c>protected set</c>，只有派生类能写，所以这件事只能在 PlayerActor 里做。）
	///
	/// 这条路径由 <see cref="OnDeath"/> 调用，而 OnDeath 是 <c>Die()</c> 的最后一句，
	/// 所以 <c>Die()</c> 返回时 IsDead 已经变回 false 了。
	/// </summary>
	private void EnterRevive()
	{
		IsDead = false;

		// 血量回满：复活的意义是"再来一次"，只留一丝血会让它变成折磨。
		Health.Heal(Health.Max);
		Posture.Reset();

		int performance = Mathf.Max(1, Stats?.RevivePerformanceFrames ?? 0);
		int settle = Mathf.Max(0, Stats?.ReviveInvulnerableFrames ?? 0);

		// 无敌覆盖"演出 + 演出之后"，否则刚站起来就会被同一套连招带走，
		// 复活次数等于白给，玩家只会觉得被耍了。
		_reviveInvulnerableFramesLeft = performance + settle;

		ReviveState revive = Machine.Get<ReviveState>();
		revive.DurationFrames = performance;

		// 灰盒仍然复用一次大幅受击——`BlockoutRig` 只有"抖一下"这一种表达。
		_rig.PlayHitReact(2f, HitStunFrames);

		// T38：真模型**有了自己的死亡姿势**——倒地 → 伏着 → 撑起来。
		// 上一版这里复用的也是受击，读起来只是"抖了一下"，玩家看不出自己刚死过一次。
		// 帧数就用复活演出自己的时长（`ActorStats.RevivePerformanceFrames`，来自数据）。
		_skinAnimator?.PlayDeath(performance);

		Machine.ForceChange<ReviveState>();
	}

	/// <summary>
	/// 倒计时必须在**基类跑完之后**自己做：<see cref="CombatActor"/> 在 <c>IsDead</c> 时
	/// 会直接 return（死人不动状态机），所以重开逻辑没地方寄生。
	/// 与 <c>TrainingDummy._PhysicsProcess</c> 是同一个手法。
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		if (_restartCountdownFrames <= 0)
			return;

		_restartCountdownFrames--;

		if (_restartCountdownFrames == 0)
			BattleReset.Instance?.RestartBattle();
	}

	/// <summary>
	/// 玩家这一侧的复位：清掉只属于玩家的临时状态，并把喝血次数补满
	/// （01 §0 规则 2 + T18 卡片：死亡后自动补满，不永久消耗）。
	/// 持久资源（升级、魄、侵蚀）在这里**故意什么都不做**。
	/// </summary>
	public override void ResetForBattle()
	{
		_restartCountdownFrames = 0;
		_localFrame = 0;
		_lastGuardReleaseFrame = -1;
		_guardHeldLastFrame = false;
		_buffer.Clear();
		Gauntlet.SetDeepAbsorbing(false);

		RefillHealCharges();
		RefillRevives();

		// T37：破防标记与半自动额度都是**本场**的量，重开要清干净；
		// 难度旋钮也要重新写一遍（战斗中途换档的场景由这里兜住）。
		GuardBreakCount = 0;
		_guardBreakShowFrames = 0;
		_autoGuardWindowStartFrame = int.MinValue;
		_autoGuardUsedInWindow = 0;
		HalfAutoGuardTriggerCount = 0;
		ApplyDifficulty();

		base.ResetForBattle();
	}

	// ── 一闪（T20 / 02 §2.3·§3·§4）──────────────────────────────

	/// <summary>慢镜倍率（02 §7：0.25）。</summary>
	public const float IssenSlowMoScale = 0.25f;

	/// <summary>慢镜恢复所需的**真实**秒数（02 §7：0.3s）。</summary>
	public const double IssenSlowMoRestoreSeconds = 0.3;

	/// <summary>连锁一闪的上限（02 §2.3：最多 3 连）。</summary>
	public const int MaxChainIssen = 3;

	/// <summary>连锁一闪授予的 buff 时长（帧）。02 §4：一闪命中后 12 帧内可再按。</summary>
	public const int ChainIssenBuffFrames = 12;

	/// <summary>连锁一闪的搜索半径（米）。02 §2.3：≤8m 内最近的敌人。</summary>
	private const float ChainIssenRange = 8f;

	/// <summary>连锁一闪落地时与目标保持的距离（米），避免直接站进它身体里。</summary>
	private const float ChainIssenStandoff = 1.2f;

	private Tween? _issenSlowMo;

	/// <summary>
	/// 慢镜触发过几次（T20 端到端测试用）。
	///
	/// 为什么需要它：<c>Engine.TimeScale</c> 只会在按下那一瞬间等于 0.25，
	/// 而恢复 Tween 在两次物理帧之间就已经开始推进了，外部逐帧采样**永远采不到 0.25 本身**。
	/// 所以"降下去了"这件事由这个计数器证明，"确实生效了"由采样到的低位值证明，两条一起才完整。
	/// </summary>
	public int IssenSlowMoCount { get; private set; }

	/// <summary>本次按键被解释成了什么（端到端测试与调试面板用）。</summary>
	public IssenIntent LastIssenIntent { get; private set; } = IssenIntent.NotAnAttempt;

	/// <summary>已经授予过多少次真一闪 buff（结算前的授予次数，测试用）。</summary>
	public int ShinIssenGrants { get; private set; }

	private int _chainIssenCount;

	/// <summary>
	/// 这一次攻击键按下去，算不算一闪。
	/// 返回 true = 已消费（一闪 / 安全窗 / 落空），false = 放行走普通攻击。
	/// </summary>
	private bool TryIssen()
	{
		// 连锁一闪：手里还有 Chain buff 时按攻击是**主动追击**，不需要敌人先出招。
		if (IssenBuffFramesLeft > 0 && IssenBuff == IssenKind.Chain)
			return TryChainIssen();

		// 连锁窗口已经过去了 → 计数归零（下一次一闪重新从第 1 连算起）。
		_chainIssenCount = 0;

		if (!FindThreat(out ThreatPhase phase, out int framesUntilActive, out _))
		{
			LastIssenIntent = IssenIntent.NotAnAttempt;
			return false;
		}

		// 窗口宽度只能由 CombatTuning 合成（08 §3 P1-3 红线：不许直接读难度档字段）。
		int windowFrames = CombatTuning.ResolveIssenWindowFrames(Difficulty?.IssenWindowFrames ?? 0);
		int safeFrames = Difficulty?.IssenSafeWindowFrames ?? 0;

		IssenIntent intent = IssenWindow.Evaluate(phase, framesUntilActive, windowFrames, safeFrames);
		LastIssenIntent = intent;

		switch (intent)
		{
			case IssenIntent.Issen:
				// 02 §4：**输入时捕获意图**——把结果记成 buff，
				// 真正的伤害等敌人的刀落下来那一帧由裁决器规则 2 结算。
				GrantIssen(IssenKind.Shin, IssenWindow.BuffFramesFor(framesUntilActive));
				ShinIssenGrants++;
				EnterIssen(IssenState.ResolveDurationFrames, guardInstead: false, playSlash: false);
				PlayIssenSlowMo();
				return true;

			case IssenIntent.SafeGuard:
				// ★ 按早了：不算一闪，但自动转格挡姿态 —— **不挨打**。
				// 时长取"到敌人命中那一帧 + 2"，所以那一刀落下时玩家还在格挡里。
				EnterIssen(framesUntilActive + 2, guardInstead: true, playSlash: false);
				return true;

			default:
				// 太早 / 太晚 → 落空，30 帧无防御硬直（有代价，但不是即死）。
				// 落空也要挥出那一刀，玩家才知道"我刚才按了、但没对"。
				EnterIssen(IssenState.WhiffDurationFrames, guardInstead: false, playSlash: true);
				return true;
		}
	}

	/// <summary>
	/// 附近有没有敌人正在挥刀。有的话给出它的相位与"距离判定帧还有几帧"。
	///
	/// 同时挑**最近**的那一个：多个敌人一起挥刀时，一闪应该照顾眼前这个。
	/// </summary>
	private bool FindThreat(out ThreatPhase phase, out int framesUntilActive, out bool perilous)
	{
		phase = ThreatPhase.None;
		framesUntilActive = 0;
		perilous = false;

		float best = float.MaxValue;

		foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
		{
			if (node == this || node is not CombatActor other || other.IsDead)
				continue;

			if (other.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
				continue;

			Vector3 delta = other.GlobalPosition - GlobalPosition;
			delta.Y = 0f;

			float distanceSquared = delta.LengthSquared();
			if (distanceSquared > IssenScanRange * IssenScanRange)
				continue;

			int frame = attack.Sequence.Frame;
			AttackTiming timing = attack.Sequence.Current;

			ThreatPhase candidate =
				frame < timing.ActiveStart ? ThreatPhase.Windup
				: frame < timing.ActiveEnd ? ThreatPhase.Active
				: ThreatPhase.Recovery;

			// 收招段不算威胁：02 §10 要求"敌人连段结束后必定有 ≥20 帧空隙给玩家反打"，
			// 那个空隙里按攻击必须是普通攻击，不能变成"落空吃 30 帧"。
			if (candidate == ThreatPhase.Recovery)
				continue;

			if (distanceSquared >= best)
				continue;

			best = distanceSquared;
			phase = candidate;
			framesUntilActive = timing.ActiveStart - frame;

			// T37 缺口③：半自动防御要靠它把「危」排除掉（05 §128）。
			perilous = other.IsIncomingAttackPerilous;
		}

		return phase != ThreatPhase.None;
	}

	/// <summary>
	/// 连锁一闪（02 §2.3 / T20 规则 7）：一闪命中后 12 帧内再按攻击 →
	/// 瞬移到 ≤8m 内最近的敌人重复一次一闪，上限 3 连。
	///
	/// 它和真一闪最大的区别是**没有来招可弹**——所以走不了裁决器规则 2（那条需要有攻方）。
	/// 这里由玩家侧主动选出目标，再套用同一张 <see cref="IssenTable"/> 的收益，
	/// 保证"一闪的收益只有一处定义"。
	/// </summary>
	private bool TryChainIssen()
	{
		if (_chainIssenCount >= MaxChainIssen)
			return false;

		if (!FindChainTarget(out CombatActor target))
			return false;

		_chainIssenCount++;

		// 用掉这一次 Chain buff（下次要重新挣）。
		IssenBuff = IssenKind.None;
		IssenBuffFramesLeft = 0;

		// 瞬移到目标近旁，站在它原来朝向玩家的那一侧，避免穿过它的身体。
		Vector3 approach = GlobalPosition - target.GlobalPosition;
		approach.Y = 0f;
		approach = approach.LengthSquared() > 0.0001f ? approach.Normalized() : Vector3.Back;
		GlobalPosition = target.GlobalPosition + approach * ChainIssenStandoff;

		IssenEffect effect = IssenTable.For(IssenKind.Chain, target.IssenTier);

		if (effect.InstantKill)
		{
			target.Health.Apply(target.Health.Max);
			target.Die();
		}
		else
		{
			target.ApplyPosturePercent(effect.PostureDamagePercent);

			if (effect.StunFrames > 0)
			{
				target.Machine.Get<StaggerState>().Duration = effect.StunFrames;
				target.Machine.Change<StaggerState>();
			}
		}

		// 还没到上限 → 再发一次 Chain buff，让下一刀可以继续连。
		if (_chainIssenCount < MaxChainIssen)
			GrantIssen(IssenKind.Chain, ChainIssenBuffFrames);

		EnterIssen(IssenState.ResolveDurationFrames, guardInstead: false, playSlash: true);
		PlayIssenSlowMo();
		return true;
	}

	private bool FindChainTarget(out CombatActor target)
	{
		target = null!;

		float best = ChainIssenRange * ChainIssenRange;

		foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
		{
			if (node == this || node is not CombatActor other || other.IsDead)
				continue;

			Vector3 delta = other.GlobalPosition - GlobalPosition;
			float distanceSquared = delta.X * delta.X + delta.Z * delta.Z;

			if (distanceSquared >= best)
				continue;

			best = distanceSquared;
			target = other;
		}

		return target is not null;
	}

	private void EnterIssen(int durationFrames, bool guardInstead, bool playSlash)
	{
		IssenState state = Machine.Get<IssenState>();
		state.DurationFrames = Mathf.Max(1, durationFrames);
		state.GuardInstead = guardInstead;

		if (playSlash)
			_rig.PlayIssen(state.DurationFrames);

		// 用 ForceChange：一闪必须能打断当前动作（02 §3 裁定：一闪应对一切攻击）。
		Machine.ForceChange<IssenState>();
	}

	/// <summary>
	/// 一闪慢镜（02 §7：<c>TimeScale = 0.25</c>，0.3 秒平滑恢复）。
	///
	/// ⚠️ **必须 <c>SetIgnoreTimeScale(true)</c>**：Tween 默认吃 <c>Engine.TimeScale</c>，
	/// 于是"0.3 秒恢复"会被慢镜自己压慢 4 倍、变成 1.2 秒；玩家连续一闪时
	/// 时间就再也爬不回 1.0——"慢镜卡住不恢复"是这类实现最常见的 bug
	/// （T20 卡片点名要求断言 TimeScale 确实回到了 1.0）。
	/// </summary>
	private void PlayIssenSlowMo()
	{
		_issenSlowMo?.Kill();

		IssenSlowMoCount++;
		Engine.TimeScale = IssenSlowMoScale;

		_issenSlowMo = CreateTween();
		_issenSlowMo.SetIgnoreTimeScale(true);
		_issenSlowMo.TweenMethod(
			Callable.From<double>(value => Engine.TimeScale = (float)value),
			(double)IssenSlowMoScale,
			1.0,
			IssenSlowMoRestoreSeconds);
		_issenSlowMo.TweenCallback(Callable.From(() => Engine.TimeScale = 1.0));
	}

	/// <summary>
	/// 攻击键有两个出口：**一闪优先**，其次才是普通攻击（02 §3 裁定：一闪应对一切攻击）。
	/// 判定走 02 §4 的"输入时捕获意图"——按下的那一瞬间扫描附近正在挥刀的敌人，当场定结果。
	/// </summary>
	public override bool ConsumeAttackInput()
	{
		if (Gauntlet.IsDeepAbsorbing)
			return false;

		if (!_buffer.Consume(PlayerAction.Attack, InputBufferFrames))
			return false;

		// TryIssen 吃掉了这次按键（一闪 / 安全窗 / 落空都算）→ 不再走普通攻击。
		return !TryIssen();
	}

	public override bool TryGetMoveIntent(out MoveIntent intent)
	{
		// 深吸期间无法移动——这是它的代价（03 §6.1）。
		if (Gauntlet.IsDeepAbsorbing)
		{
			intent = default;
			return false;
		}

		Vector2 raw = Input.GetVector("move_left", "move_right", "move_back", "move_forward");

		Vector3 forward = -_cameraPivot.GlobalTransform.Basis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();

		Vector3 right = _cameraPivot.GlobalTransform.Basis.X;
		right.Y = 0f;
		right = right.Normalized();

		Vector3 direction = right * raw.X + forward * raw.Y;
		if (direction.LengthSquared() > 1f)
			direction = direction.Normalized();

		intent = new MoveIntent
		{
			Direction = direction,
			Sprint = Input.IsActionPressed("sprint") && raw.Y > 0.1f,
		};

		return direction.LengthSquared() > 0.0001f;
	}

	public override void OnAttackStarted(AttackData data)
	{
		// 基类实现负责「危」攻击的预警（T12）。玩家现在没有危招式，
		// 但覆写时不调 base 是个定时炸弹——等哪天真加了就会静默丢失预警。
		base.OnAttackStarted(data);
		_rig.PlayAttack(data.TotalFrames);

		// T38：把**这是第几段连击**告诉动画器——三段各有各的刀路，
		// 不能是"同一个挥砍换幅度"（那正是这次试玩反馈骂的那件事）。
		_skinAnimator?.PlayAttack(CurrentComboStep(), data.TotalFrames);
	}

	/// <summary>当前连击进行到第几段（0 起）。取不到就按第一段处理。</summary>
	private int CurrentComboStep() =>
		Machine.Has<AttackState>() && Machine.Get<AttackState>().Sequence.IsRunning
			? Mathf.Max(0, Machine.Get<AttackState>().Sequence.StepIndex)
			: 0;

	protected override void OnTickVisual(float dt, float speed01)
	{
		_rig.AnimateLocomotion(speed01, dt);
		_rig.AnimateCombat(dt);

		UpdateDeflectCue();

		if (_skinAnimator is not null)
		{
			// T38：防御改成**三态**（抬起/维持/放下），靠"进入防御后的帧号"驱动；
			// 攻击姿势同样由**状态自己的帧号**算出，所以动画与逻辑不会漂（04 §12）。
			_skinAnimator.TrackGuard(Machine.Current is GuardState guard ? guard.Frame : -1);
			// T38：跳跃要**逐帧**知道自己在哪一段（蹬地 / 腾空 / 落地缓冲）——
			// 滞空多少帧是物理结果、不是数据里的固定帧数，所以它没法像攻击那样"播一段"。
			_skinAnimator.TrackJump(JumpAnimPhase(), JumpProfile?.LandRecoveryFrames ?? 0);
			_skinAnimator.AnimateLocomotion(speed01, dt);
			_skinAnimator.AnimateCombat(dt, Machine.Current is AttackState attack ? attack.Frame : -1);
		}
	}

	// ── 弹开窗指示器（T51）───────────────────────────────────────────
	//
	// 它解决的是**认知问题**，不是判定问题。实测结论（`GuardWindowProbe`）：
	// 弹开窗只在按下防御键那一刻开一次、宽 DeflectWindowFrames 帧，之后整段防御
	// 再也不开。而敌人前摇 24 帧 > 窗口宽度，所以"按住右键等刀来"必然错过——
	// 玩家体验到的就是"能挡但从来弹不开"。
	//
	// 规则的形状没问题（点按的有效区间精确等于配置宽度），缺的是**玩家看不见它**。
	// 这个环就是那条反馈：窗口开着的每一帧，脚下有一圈光，越接近关闭越亮。

	private MeshInstance3D? _deflectCue;
	private StandardMaterial3D? _deflectCueMaterial;

	/// <summary>
	/// 建指示器。**代码里不写任何尺寸/颜色**——全部来自 <see cref="DeflectCue"/>（铁律 1）。
	/// 没配资源就不建，游戏照常能玩：它只是反馈，不参与判定。
	/// </summary>
	private void BuildDeflectCue()
	{
		if (DeflectCue is null)
			return;

		var ring = new TorusMesh
		{
			// TorusMesh 的内径是**洞的半径**，不是管子的粗细，所以这里要减出来。
			InnerRadius = Mathf.Max(0.01f, DeflectCue.Radius - DeflectCue.Thickness),
			OuterRadius = DeflectCue.Radius + DeflectCue.Thickness,
		};

		_deflectCueMaterial = new StandardMaterial3D
		{
			AlbedoColor = DeflectCue.OpenColor,
			EmissionEnabled = true,
			Emission = DeflectCue.EmissionColor,
			EmissionEnergyMultiplier = DeflectCue.EmissionEnergy,
			// 光环不该被自身阴影吃暗，也不该挡住地面判定。
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		_deflectCue = new MeshInstance3D
		{
			Name = "DeflectCue",
			Mesh = ring,
			MaterialOverride = _deflectCueMaterial,
			// 纯表现层：不投影、不参与任何碰撞查询。
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};

		AddChild(_deflectCue);
	}

	/// <summary>
	/// 每帧同步指示器。
	///
	/// **它只回答一个是非题：「现在按下去算不算弹开」。**
	///
	/// 所以它是一盏**静态的灯**：不渐变、不脉冲、不自转。
	///
	/// 两次都是实测把动效否掉的：
	/// 1. 第一版加了"越接近关闭越亮"——**方向是反的**。光环亮着本身就意味着
	///    "现在按下去能弹开"，再去强调"快没了"只会读成"该按了"，而它恰恰马上要熄。
	/// 2. 第二版留了自转当"活气"。实测同一窗口内第 10 帧 vs 第 14 帧，
	///    0.61% 像素在变、最大单像素差 197，差异全落在脚下环的区域
	///    （y 400~519，质心 576,467）——环在肉眼里是圆，`TorusMesh` 的几何并不
	///    各向同性，转起来每帧轻微闪动。
	///
	/// 一个"能不能弹开"的判据不该自带噪声。会说话的东西只能有一句台词：
	/// **亮 = 能弹开，灭 = 只剩格挡。**
	///
	/// 灭的代价是真实的：窗口过期后再按住只有格挡，每挨一刀扣 20~45 架势槽
	/// （`CombatResolver` 第 5 条：Block 走 `atk.Traits.PostureDamage`，
	/// 而 Deflect 的只给敌人 +18、自己 0）。
	/// </summary>
	private void UpdateDeflectCue()
	{
		if (_deflectCue is null || DeflectCue is null)
			return;

		if (DeflectWindowFramesLeft <= 0)
		{
			_deflectCue.Visible = false;
			return;
		}

		// 开窗那一刻**只写一次**材质与变换。
		//
		// 为什么不是"每帧写同样的值"——那样逻辑上等价，**但实测不等价**：
		// 每帧赋 `AlbedoColor` / `Emission` 会不断触发材质属性更新，实测同一窗口内
		// 相邻两帧就有 0.61% 像素在变、最大单像素差 197，且全都落在脚下环的区域——
		// 环看起来在闪。一个"能不能弹开"的判据不该自带噪声。
		// 所以它是：开一次、写一次、然后不动。
		if (_deflectCue.Visible)
			return;

		_deflectCue.Visible = true;
		_deflectCue.Position = new Vector3(0f, DeflectCue.HeightOffset, 0f);
		_deflectCue.Scale = Vector3.One;
		_deflectCue.Rotation = Vector3.Zero;

		if (_deflectCueMaterial is not null)
		{
			_deflectCueMaterial.AlbedoColor = DeflectCue.OpenColor;
			_deflectCueMaterial.Emission = DeflectCue.EmissionColor;
			_deflectCueMaterial.EmissionEnergyMultiplier = DeflectCue.EmissionEnergy;
		}
	}

	protected override void OnDamaged(int damage)
	{
		_rig.PlayHitReact(1f, HitStunFrames);
		_skinAnimator?.PlayHitReact(1f, HitStunFrames);
	}

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		// 弹开的顿帧、体干、弹一闪 buff、连击数全在 CombatActor.ReceiveVerdict 里结算完了，
		// 这里只负责"表现层进弹开状态"（T6 规则 6）。
		if (result.Verdict == Combat.Verdict.Deflect)
		{
			Machine.ForceChange<DeflectState>();

			// T38：弹开要有**专用姿势**。这一条是本项目的立命之本——
			// 之前弹开只有粒子和音效，人物姿势和普通格挡一模一样，等于"接住了"这件事没被看见。
			_skinAnimator?.PlayDeflect(Machine.Get<DeflectState>().TotalFrames);
		}

		if (result.Verdict is Combat.Verdict.Block or Combat.Verdict.Deflect or Combat.Verdict.Clash)
		{
			_rig.PlayHitReact(0.5f, HitStunFrames);
			_skinAnimator?.PlayHitReact(0.5f, HitStunFrames);
		}

		// 一闪结算成功 → 现在才播那一刀（T20）。
		// 放在结算时机而不是按键时机：真一闪从按下到生效隔着 0~N 帧，
		// 那几帧正是"刀还没落下来"的紧张感，提前播会把节奏拆坏。
		// 这条也是**弹一闪**（弹开 → buff → 敌人下一刀被一闪）唯一能播到动画的地方。
		if (result.Verdict == Combat.Verdict.Issen)
		{
			int issenFrames = Machine.Get<IssenState>().DurationFrames;
			_rig.PlayIssen(issenFrames);
			_skinAnimator?.PlayIssen(issenFrames);
		}
	}
}
