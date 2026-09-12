using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 一闪（02 §2.3 / T20）。三种入口共用这一个状态，靠时长与 <see cref="GuardInstead"/> 区分：
///
/// | 入口 | 时长 | 姿态 | 说明 |
/// |---|---|---|---|
/// | 一闪成立 | 22 帧 | 无防御 | 收招是纯决策成本 |
/// | ★ 安全窗 | 敌人命中前剩余帧 + 2 | **保持格挡姿态** | "不算一闪，但不挨打" |
/// | 落空 | 30 帧 | 无防御 | 太早/太晚的代价 |
///
/// **整段不可取消**（02 §2.3：一闪与落空都不能被取消）——
/// 这是"一闪不是无代价的万能键"的唯一来源。落到 <see cref="CanTransitionTo"/> 上时
/// 只放行回 <see cref="IdleState"/>（那是状态自己的正常退出，不是玩家取消）。
/// </summary>
public sealed class IssenState : ActorState
{
	/// <summary>一闪成立后的后摇（02 §2.3：22 帧）。</summary>
	public const int ResolveDurationFrames = 22;

	/// <summary>落空的挥空硬直（02 §2.3：30 帧，此期间无防御）。</summary>
	public const int WhiffDurationFrames = 30;

	/// <summary>切入前由输入侧写入：这一段持续多少帧。</summary>
	public int DurationFrames { get; set; } = ResolveDurationFrames;

	/// <summary>
	/// 切入前由输入侧写入：整段是否保持格挡姿态（安全窗专用）。
	/// 用掉即复位，避免下次忘了写参数时"落空"也莫名带着格挡。
	/// </summary>
	public bool GuardInstead { get; set; }

	public override int TotalFrames => DurationFrames;

	/// <summary>本段是否处于格挡姿态（端到端测试断言"安全窗确实不挨打"靠它）。</summary>
	public bool IsGuardingNow => GuardInstead;

	public override void Enter()
	{
		base.Enter();

		Actor.IsGuarding = GuardInstead;
		GuardInstead = false;
	}

	public override void Exit()
	{
		Actor.IsGuarding = false;
		base.Exit();
	}

	/// <summary>
	/// 不可取消：只放行回 <see cref="IdleState"/>。
	/// 别写成 <c>=> false</c>——那会把状态自己的退出也一起挡掉，角色会永久卡在闪后摇里。
	/// </summary>
	public override bool CanTransitionTo(ActorState next) => next is IdleState;

	public override void Tick()
	{
		base.Tick();

		// 站住：一闪的收招是"收势停顿"，不接受移动输入。
		Actor.DesiredVelocity = Vector3.Zero;

		if (Frame >= DurationFrames)
			ChangeState<IdleState>();
	}
}
