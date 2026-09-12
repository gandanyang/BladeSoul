using System;
using Godot;
using Oniblade.Combat.Data;

namespace Oniblade.Combat.States;

/// <summary>
/// 出招。内部靠 <see cref="AttackSequence"/> 推进连段（壹→贰→叁）。
///
/// 关键规则：**后摇不能被攻击键取消**。想在第二段接上，必须等第一段进入
/// <see cref="AttackTiming.CancelOpenFrame"/>。这条是反连打的核心——
/// 狂按不会更快，只会让你在能接的那一帧接到。
/// </summary>
public sealed class AttackState : ActorState
{
    private AttackData[] _attacks = Array.Empty<AttackData>();
    private AttackSequence _sequence = new(Array.Empty<AttackTiming>());

    public AttackSequence Sequence => _sequence;

    public override int TotalFrames => _sequence.IsRunning ? _sequence.Current.TotalFrames : 0;

    public void Configure(AttackData[] attacks)
    {
        _attacks = attacks;
        var timings = new AttackTiming[attacks.Length];
        for (int i = 0; i < attacks.Length; i++)
            timings[i] = attacks[i].ToTiming();

        _sequence = new AttackSequence(timings);
    }

    public override void Enter()
    {
        base.Enter();

        if (_attacks.Length == 0)
        {
            ChangeState<IdleState>();
            return;
        }

        _sequence.Start();
        BeginStep(_attacks[0]);
    }

    public override void Exit()
    {
        Actor.PrimaryHitbox?.SetActive(false);
        Actor.PrimaryHitbox?.SetAttack(null);
        Actor.SetCurrentAttack(string.Empty);
        Actor.OnAttackEnded();
    }

    public override void Tick()
    {
        if (!_sequence.IsRunning)
        {
            ChangeState<IdleState>();
            return;
        }

        AttackData current = _attacks[_sequence.StepIndex];
        Actor.PrimaryHitbox?.SetActive(_sequence.IsActive);

        // 三段位移都集中在"前摇 + 判定"期间，后摇站住不动。
        bool advancing = _sequence.Frame < current.ActiveEnd;
        if (advancing && current.AdvanceDistance > 0f)
        {
            Vector3 forward = -Actor.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            float perFrame = current.AdvanceDistance / Mathf.Max(1, current.ActiveEnd);
            Actor.DesiredVelocity = forward.Normalized() * (perFrame * Utils.Frames.PerSecond);
        }

        // 接下一段
        if (_sequence.CanChain && Actor.ConsumeAttackInput() && _sequence.TryChain())
        {
            BeginStep(_attacks[_sequence.StepIndex]);
            SetFrame(0);
            return;
        }

        _sequence.Tick();
        SetFrame(_sequence.Frame);

        if (_sequence.IsFinished)
        {
            Actor.PrimaryHitbox?.SetActive(false);
            ChangeState<IdleState>();
        }
    }

    private void BeginStep(AttackData data)
    {
        Actor.PrimaryHitbox?.SetAttack(data);
        Actor.PrimaryHitbox?.SetActive(false);
        Actor.SetCurrentAttack(data.Id);
        Actor.OnAttackStarted(data);
    }
}
