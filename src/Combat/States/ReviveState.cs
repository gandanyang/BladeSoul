using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 复活（T22）。血量归零但还有复活次数时进入，播一段起身演出后回到可控。
///
/// 它与 T14 的**原地重开**是两件事，卡片里那张表说得很清楚：
///
/// | | 复活（本状态） | 重开（BattleReset，T14） |
/// |---|---|---|
/// | 发生次数 | 每条命 <c>Difficulty.ReviveCount</c> 次 | 复活次数用完之后 |
/// | 表现 | **当场站起来** | 复位全场 |
/// | 掉资源吗 | 不掉 | 不掉 |
///
/// **整段不可取消**（你被打倒了，就得爬起来），但允许回到
/// <see cref="IdleState"/>（状态自己的正常退出）。
///
/// 无敌帧不在这里管：它由玩家侧的计数器统一给出，因为演出结束后还要
/// **多延续一段**（<c>ActorStats.ReviveInvulnerableFrames</c>），
/// 那一段发生在状态之外，塞进状态里反而管不到。
/// </summary>
public sealed class ReviveState : ActorState
{
	/// <summary>切入前由输入侧写入：起身演出的帧数（<c>ActorStats.RevivePerformanceFrames</c>）。</summary>
	public int DurationFrames { get; set; } = 90;

	public override int TotalFrames => DurationFrames;

	public override void Enter()
	{
		base.Enter();

		// 起身期间站住，不接受移动输入。
		Actor.DesiredVelocity = Vector3.Zero;
	}

	/// <summary>不可取消，只放行正常退出与挨打（挨打在无敌帧里不会发生，留作兜底）。</summary>
	public override bool CanTransitionTo(ActorState next) => next is IdleState or StaggerState;

	public override void Tick()
	{
		base.Tick();

		Actor.DesiredVelocity = Vector3.Zero;

		if (Frame >= DurationFrames)
			ChangeState<IdleState>();
	}
}
