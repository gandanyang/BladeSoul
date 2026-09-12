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

        // WantsToAttack 在前：敌人 AI 的攻击意图不会误消费玩家的输入缓冲。
        if ((Actor.WantsToAttack() || Actor.ConsumeAttackInput()) && Actor.Machine.Has<AttackState>())
        {
            ChangeState<AttackState>();
            return;
        }

        if (Actor.TryGetMoveIntent(out MoveIntent intent) && intent.Direction.LengthSquared() > 0.0001f)
            ChangeState<MoveState>();
    }
}
