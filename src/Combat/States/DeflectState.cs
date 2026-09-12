using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 弹开成功后的收招（02 文档 §1、§2.3：12 帧特殊状态，可派生**弹一闪**）。
///
/// 这 12 帧里玩家保持防御姿态：紧接着打来的第二刀仍然算格挡，不会因为
/// "弹开成功反而露出破绽"而受罚。弹一闪（按攻击派生强化斩）属于一闪家族，
/// 排在 M3，本状态只需要把 12 帧站稳，并允许玩家继续按住防御接下一发。
///
/// 12 帧是 T6 规格里明确允许的一个常量（另一个是 <see cref="GuardWindowState"/> 读的难度档数值）。
/// </summary>
public sealed class DeflectState : ActorState
{
	/// <summary>弹开状态的固定帧数。</summary>
	public const int DurationFrames = 12;

	public override int TotalFrames => DurationFrames;

	public override void Enter()
	{
		base.Enter();
		Actor.IsGuarding = true;
	}

	public override void Exit()
	{
		Actor.IsGuarding = false;
		base.Exit();
	}

	public override void Tick()
	{
		base.Tick();

		// 弹开不接受移动输入：这是"站稳了"的 12 帧，和攻击后摇一样是决策成本。
		Actor.DesiredVelocity = Vector3.Zero;

		if (Frame < DurationFrames)
			return;

		// 还按着防御就回防御（窗口重新打开，连弹才有节奏），否则回站立。
		if (Actor is IGuardInput input && input.IsGuardHeld)
		{
			ChangeState<GuardState>();
			return;
		}

		ChangeState<IdleState>();
	}
}
