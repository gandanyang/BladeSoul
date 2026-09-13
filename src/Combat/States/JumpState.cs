using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 跳跃（T41）。**本作有跳跃**（2026-09-13 项目主人裁定，00 §2.3 已解冻）。
///
/// ★ **跳跃不是无敌帧。** 它是"**离开地面判定**"——能不能躲开攻击，
/// 取决于判定框够不够高，而不是"按了跳就无敌"。这条与闪避的**无敌帧**是两套机制，
/// 必须分开实现，否则两个位移机制会互相取消（T41 的硬约束）。
///
/// 重力**复用 `CombatActor` 已有的那套**（它在读 `physics/3d/default_gravity`），
/// 这里不另造一份——所以滞空时长不是写死的帧数，而是 `起跳初速 ÷ 重力` 的结果。
/// </summary>
public sealed class JumpState : ActorState
{
    private float _takeoffSpeed = 5.5f;
    private int _landRecoveryFrames = 12;

    /// <summary>已经离地（用来分辨"还在起跳那几帧"和"真的上天了"）。</summary>
    private bool _airborne;

    /// <summary>已经落地、正在数硬直。</summary>
    private int _landedFrames = -1;

    /// <summary>起跳点的高度（测试量最大高度用）。</summary>
    public float TakeoffY { get; private set; }

    /// <summary>这一跳的最大高度（米）。</summary>
    public float PeakHeight { get; private set; }

    public void Configure(float takeoffSpeed, int landRecoveryFrames)
    {
        _takeoffSpeed = takeoffSpeed;
        _landRecoveryFrames = Mathf.Max(0, landRecoveryFrames);
    }

    public override int TotalFrames => _landRecoveryFrames;

    public override void Enter()
    {
        base.Enter();

        _airborne = false;
        _landedFrames = -1;
        TakeoffY = Actor.GlobalPosition.Y;
        PeakHeight = 0f;
    }

    public override void Tick()
    {
        // 起跳：**连着推几帧**，直到真的离地。
        // 为什么不一帧了事：Enter() 这一帧 `IsOnFloor()` 还是 true，
        // 而 `CombatActor.ApplyMovement` 会把"在地面上"的 Y 速度清零——一帧就没了。
        if (!_airborne)
        {
            if (Actor.IsOnFloor())
            {
                Vector3 v = Actor.Velocity;
                v.Y = _takeoffSpeed;
                Actor.Velocity = v;
                return;
            }

            _airborne = true;
        }

        PeakHeight = Mathf.Max(PeakHeight, Actor.GlobalPosition.Y - TakeoffY);

        // 落地 → 数硬直（这一段不能动，防止无脑连跳）。
        if (_landedFrames < 0)
        {
            if (Actor.IsOnFloor())
                _landedFrames = 0;

            return;
        }

        _landedFrames++;
        Actor.DesiredVelocity = Vector3.Zero;

        if (_landedFrames >= _landRecoveryFrames)
            ChangeState<IdleState>();
    }
}
