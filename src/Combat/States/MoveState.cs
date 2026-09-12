using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 地面移动。冲刺（Sprint）折在这里做，没有单独成状态——
/// 它和走路只有速度差别，没有帧数据差别，拆开只是多一个类。
/// </summary>
public sealed class MoveState : ActorState
{
    public override void Tick()
    {
        base.Tick();

        if (Actor.ConsumeAttackInput() && Actor.Machine.Has<AttackState>())
        {
            ChangeState<AttackState>();
            return;
        }

        if (!Actor.TryGetMoveIntent(out MoveIntent intent) || intent.Direction.LengthSquared() <= 0.0001f)
        {
            ChangeState<IdleState>();
            return;
        }

        Vector3 direction = intent.Direction.Normalized();
        float speed = intent.Sprint ? Actor.SprintSpeed : Actor.MoveSpeed;

        Actor.DesiredVelocity = direction * speed;
        Actor.DesiredYaw = Mathf.Atan2(-direction.X, -direction.Z);
        Actor.HasDesiredYaw = true;
    }
}
