using System;
using Godot;
using Oniblade.Audio;
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
        PlayWhooshAt(current, _sequence.Frame);

        // 步进：只在"前摇 + 判定"期间推进，后摇站住不动；
        // 速度**前快后慢**（权重 1.6 → 0.4，均值 1.0，总位移仍等于 AdvanceDistance）。
        // 用整招恒定速度会变成"滑过去"——那是走路不是出刀。
        // 出刀应该是"踏进去，然后站定"。
        int advanceEnd = Mathf.Max(1, current.ActiveEnd);
        if (_sequence.Frame < advanceEnd && current.AdvanceDistance > 0f)
        {
            float t = _sequence.Frame / (float)advanceEnd;
            float weight = 1.6f - 1.2f * t;
            float perFrame = current.AdvanceDistance * weight / advanceEnd;

            Vector3 forward = -Actor.GlobalTransform.Basis.Z;
            forward.Y = 0f;
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

    /// <summary>
    /// 挥刀音：在**判定开始那一帧**响，不是在按下按键那一帧。
    ///
    /// 为什么必须是判定帧：刀风跟的是**刀的运动**，不是玩家的意图。
    /// 按下去就响的话，前摇 8 帧（约 0.13 秒）里声音已经过去了，等刀真的扫到人时
    /// 反而没有声音接上——听起来就是"声音和动作对不上"，比没有声音更糟。
    ///
    /// 每段只响一次：用帧号精确命中 <c>ActiveStart</c>，而不是"在判定期内每帧都响"。
    /// </summary>
    private void PlayWhooshAt(AttackData data, int frame)
    {
        if (data.Whoosh == WhooshKind.None)
            return;

        if (frame != data.ActiveStart)
            return;

        AudioDirector? audio = AudioDirector.Instance;
        if (audio is null)
            return;

        CombatSfx sfx = data.Whoosh == WhooshKind.Heavy
            ? CombatSfx.WhooshHeavy
            : CombatSfx.WhooshLight;

        audio.PlayCombatAt(
            sfx,
            Actor.GlobalPosition,
            data.WhooshPitchScale,
            data.WhooshVolumeDb);
    }
}
