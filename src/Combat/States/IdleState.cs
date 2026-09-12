using Godot;

namespace Oniblade.Combat.States;

/// <summary>站立：没有移动输入时的默认状态。有输入切到 <see cref="MoveState"/>。</summary>
public sealed class IdleState : ActorState
{
    public override void Enter()
    {
        base.Enter();
        Actor.DesiredVelocity = Vector3.Zero;
    }

    public override void Tick()
    {
        base.Tick();

        if (Actor.ConsumeAttackInput() && Actor.Machine.Has<AttackState>())
        {
            ChangeState<AttackState>();
            return;
        }

        if (Actor.TryGetMoveIntent(out MoveIntent intent) && intent.Direction.LengthSquared() > 0.0001f)
            ChangeState<MoveState>();
    }
}
