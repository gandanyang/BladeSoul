using System;
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
public partial class PlayerActor : CombatActor, IGuardInput, IAttackEvasionListener
{
	private static readonly PlayerAction[] WatchedActions = Enum.GetValues<PlayerAction>();

	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float MinPitch { get; set; } = -70f;
	[Export] public float MaxPitch { get; set; } = 40f;

	/// <summary>招式表（data/attacks/player/player_combo.tres）。</summary>
	[Export] public PlayerAttackSet? Attacks { get; set; }

	/// <summary>难度档。弹开窗、输入缓冲这些宽容参数都从它读，不许写死在代码里。</summary>
	[Export] public DifficultyProfile? Difficulty { get; set; }

	[Export] public Color BodyColor { get; set; } = new(0.22f, 0.26f, 0.34f);
	[Export] public Color AccentColor { get; set; } = new(0.55f, 0.16f, 0.14f);

	/// <summary>相机支点相对脚底的高度（米）。</summary>
	[Export] public float CameraHeight { get; set; } = 1.45f;

	/// <summary>
	/// 闪避速度倍率（× <see cref="CombatActor.MoveSpeed"/>）。
	/// 位移 = 无敌帧全速 + 后摇线性衰减，所以 2.4 倍大约是一个身位的垫步。
	/// 放在 <c>[Export]</c> 而不是 .tres 里，是因为它只影响灰盒观感、不影响判定。
	/// </summary>
	[Export] public float DodgeSpeedScale { get; set; } = 2.4f;

	/// <summary>玩家不会被一闪秒杀（01 文档：任何机制都不该一击终结玩家）。</summary>
	public override EnemyTier IssenTier => EnemyTier.Boss;

	/// <summary>玩家不掉魄——掉的是魔骸的魄（03 §6.1）。</summary>
	public override bool DropsSoulOnDeath => false;

	private Node3D _cameraPivot = null!;
	private SpringArm3D _springArm = null!;
	private BlockoutRig _rig = null!;
	private readonly PlayerInputBuffer _buffer = new();

	/// <summary>本地帧号，只给"快速重按防御"判定用（02 §8）。</summary>
	private int _localFrame;

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

		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		// 笼手挂在玩家身上，吸附点就是玩家位置。
		Gauntlet = new OniGauntlet { Name = "OniGauntlet" };
		AddChild(Gauntlet);
		if (EventBus.Instance is { } bus)
			Gauntlet.Attach(bus);

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

		// 记录"松开防御"的那一帧，供快速重按判定使用。
		bool guardHeld = Input.IsActionPressed("guard");
		if (_guardHeldLastFrame && !guardHeld)
			_lastGuardReleaseFrame = _localFrame;
		_guardHeldLastFrame = guardHeld;

		// 「深吸」：按住交互键强行吸魄，代价是侵蚀（03 §6.1）。
		Gauntlet.SetDeepAbsorbing(Input.IsActionPressed("interact"));

		if (Input.IsActionJustPressed("attack"))
			_buffer.Push(PlayerAction.Attack);
		if (Input.IsActionJustPressed("guard"))
			_buffer.Push(PlayerAction.Guard);
		if (Input.IsActionJustPressed("dodge"))
			_buffer.Push(PlayerAction.Dodge);

		TryEnterDodge();
		TryEnterGuard();
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

		GuardState guard = Machine.Get<GuardState>();
		guard.EntrySource = source;
		guard.CancelLockFrames = Difficulty?.GuardCancelLockFrames ?? 0;

		// 弹开窗只能由 CombatTuning 合成（08 §3 P1-3 红线）。
		// 缺难度档时退化到最窄的可玩窗口（下限 4 帧），不会凭空变强。
		guard.DeflectWindowFrames = CombatTuning.ResolveDeflectWindowFrames(Difficulty?.DeflectWindowFrames ?? 0);

		guard.MoveScale = Stats?.GuardMoveScale ?? 1f;

		Machine.ForceChange<GuardState>();
	}

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

		// 攻击中：只有进入后摇的取消窗之后才允许被闪避取消（02 §1 取消表）。
		// 窗口没开时**不消费缓冲**，让这次输入继续躺在缓冲里等窗口打开。
		if (Machine.Current is AttackState attack && !IsAttackCancelWindowOpen(attack))
			return;

		if (!_buffer.Consume(PlayerAction.Dodge, Difficulty?.DodgeBufferFrames ?? 0))
			return;

		DodgeState dodge = Machine.Get<DodgeState>();
		dodge.InvulnerableFrames = Difficulty?.DodgeIFrames ?? 0;
		dodge.RecoveryFrames = Difficulty?.DodgeRecoveryFrames ?? 0;
		dodge.PerfectDodgeGraceFrames = Difficulty?.PerfectDodgeGraceFrames ?? 0;
		dodge.Speed = MoveSpeed * DodgeSpeedScale;
		dodge.Direction = ResolveDodgeDirection();

		Machine.Change<DodgeState>();
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
		|| (Machine is { Current: DodgeState dodge } && dodge.IsInvulnerableNow);

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

	public override bool ConsumeAttackInput()
		=> !Gauntlet.IsDeepAbsorbing && _buffer.Consume(PlayerAction.Attack, InputBufferFrames);

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

	public override void OnAttackStarted(AttackData data) => _rig.PlayAttack();

	protected override void OnTickVisual(float dt, float speed01)
	{
		_rig.AnimateLocomotion(speed01, dt);
		_rig.AnimateCombat(dt);
	}

	protected override void OnDamaged(int damage) => _rig.PlayHitReact(1f);

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		// 弹开的顿帧、体干、弹一闪 buff、连击数全在 CombatActor.ReceiveVerdict 里结算完了，
		// 这里只负责"表现层进弹开状态"（T6 规则 6）。
		if (result.Verdict == Combat.Verdict.Deflect)
			Machine.ForceChange<DeflectState>();

		if (result.Verdict is Combat.Verdict.Block or Combat.Verdict.Deflect or Combat.Verdict.Clash)
			_rig.PlayHitReact(0.5f);
	}
}
