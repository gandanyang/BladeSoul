using Godot;

namespace Oniblade.Combat.States;

/// <summary>受击 / 破防硬直。时长由施加者设定（受击 18 帧、体干破裂 50 帧）。</summary>
public sealed class StaggerState : ActorState
{
    public int Duration { get; set; } = 18;

    public override int TotalFrames => Duration;

    public override void Exit() => Actor.PrimaryHitbox?.SetActive(false);

    public override void Tick()
    {
        base.Tick();

        Actor.DesiredVelocity = Vector3.Zero;

        if (Frame >= Duration)
            ChangeState<IdleState>();
    }
}
