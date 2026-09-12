using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 防御状态需要的唯一一样东西：**这一帧防御键按住没有**。
///
/// 为什么不放在 <see cref="CombatActor"/> 上：它是所有战斗单位的基类，
/// 而"防御键"只属于玩家（M1 的敌人还不会防御），且 CombatActor 的接口已冻结（T6 硬约束）。
/// 接口定义在 Combat 侧，是为了让 Combat.States 不必反向引用 Oniblade.Player（04 §2.2）。
/// </summary>
public interface IGuardInput
{
	/// <summary>
	/// 防御键是否按住。**必须读这一帧的真实输入，不能走输入缓冲**：
	/// "按住"是一个持续状态，用缓冲会凭空造出"松开又按下"的连打。
	/// </summary>
	bool IsGuardHeld { get; }
}

/// <summary>
/// 持续格挡（02 文档 §1、§2.2）。按住 guard 进入，松开退出。
///
/// 它只负责三件事：
/// 1. 把 <see cref="CombatActor.IsGuarding"/> 置真 —— 裁决器规则 5 靠它把"挨打"变成"格挡"；
/// 2. 在**正确的那一帧**打开弹开窗（<see cref="GuardWindowState"/> + <see cref="CombatTuning"/>）；
/// 3. 格挡姿态：移动降速、攻击键不生效（本状态从不消费攻击输入）。
///
/// 参数由输入侧在切入之前写入（与 <c>StaggerState.Duration</c> 同一个用法），
/// 所以本状态不知道 <c>DifficultyProfile</c> 的存在，也不需要知道是谁在按防御。
/// 窗口宽度**必须**由 <see cref="CombatTuning.ResolveDeflectWindowFrames"/> 算好再传进来（08 §3 P1-3 红线）。
/// </summary>
public sealed class GuardState : ActorState
{
	private readonly GuardWindowState _window = new();

	/// <summary>切入前由输入侧写入：从哪来（决定要不要付取消硬直）。用完即复位。</summary>
	public GuardEntrySource EntrySource { get; set; } = GuardEntrySource.Neutral;

	/// <summary>切入前由输入侧写入：取消硬直帧数（<c>Difficulty.GuardCancelLockFrames</c>）。</summary>
	public int CancelLockFrames { get; set; }

	/// <summary>切入前由输入侧写入：弹开窗帧数（<c>CombatTuning.ResolveDeflectWindowFrames(...)</c>）。</summary>
	public int DeflectWindowFrames { get; set; }

	/// <summary>切入前由输入侧写入：格挡时的移动速度倍率（<c>ActorStats.GuardMoveScale</c>）。</summary>
	public float MoveScale { get; set; } = 1f;

	/// <summary>取消硬直中（调试面板与测试用）。</summary>
	public bool InCancelLock { get; private set; }

	/// <summary>进入后的第几帧打开弹开窗（测试用）。</summary>
	public int DeflectWindowOpenFrame => _window.OpenFrame;

	public override void Enter()
	{
		base.Enter();

		Actor.IsGuarding = true;
		_window.Begin(EntrySource, CancelLockFrames);
		InCancelLock = _window.IsInCancelLock;

		// 用掉就复位：下次忘了写参数时，宁可当成"从站立进入"，也不要沿用上一次的取消硬直。
		EntrySource = GuardEntrySource.Neutral;

		// 窗口**不在这里**打开：基类的 TickTimers 在本帧的 Machine.Tick 之前已经先扣过一帧，
		// 在 Enter 里开窗会让窗口比 CombatTuning 给的宽度少 1 帧。开窗统一放在 Tick 里做。
	}

	public override void Exit()
	{
		Actor.IsGuarding = false;
		base.Exit();
	}

	public override void Tick()
	{
		base.Tick();

		if (Actor is not IGuardInput input || !input.IsGuardHeld)
		{
			ChangeState<IdleState>();
			return;
		}

		// 顺序不能反：先按本帧判定（该不该开窗、是不是还在取消硬直里），再推进帧号。
		InCancelLock = _window.IsInCancelLock;

		if (_window.OpensWindowThisFrame)
			Actor.OpenDeflectWindow(DeflectWindowFrames);

		_window.Advance();
		ApplyGuardMovement();
	}

	/// <summary>
	/// 格挡姿态的移动：能走，但减速（02 §2.2）。
	/// 这里**不消费攻击键**——按住防御不会出刀，这是我们想要的行为。
	/// </summary>
	private void ApplyGuardMovement()
	{
		if (!Actor.TryGetMoveIntent(out MoveIntent intent) || intent.Direction.LengthSquared() <= 0.0001f)
			return;

		Vector3 direction = intent.Direction.Normalized();

		Actor.DesiredVelocity = direction * (Actor.MoveSpeed * Mathf.Max(0f, MoveScale));
		Actor.DesiredYaw = Mathf.Atan2(-direction.X, -direction.Z);
		Actor.HasDesiredYaw = true;
	}
}
