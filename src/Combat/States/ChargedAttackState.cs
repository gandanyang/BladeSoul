using System;
using Godot;
using Oniblade.Combat.Data;

namespace Oniblade.Combat.States;

/// <summary>
/// 蓄力斩（T46 / 02 §2.1）。**一次一招、不进连段**。
///
/// ★ 关键：**前摇已经在蓄力时花掉了**——所以这里的时序把 `StartupFrames` 记成 **0**。
/// 否则玩家要等两遍（先蓄 34 帧，再"前摇"34 帧），那是明显的重手。
/// 02 §2.1 前摇列的 34/48/62 就是**蓄力阈值**，两者是同一段时间。
///
/// 为什么不塞进 <see cref="AttackState"/>：那个类的整条逻辑都围着"连段"转
/// （`CanChain` / `TryChain` / 后摇取消窗），而蓄力斩 02 §2 裁定**不可取消**——
/// 两者规则相反，硬塞会让两边都难读。
/// </summary>
public sealed class ChargedAttackState : ActorState
{
    private AttackData? _data;
    private AttackTiming _timing;
    private AttackSequence _sequence = new(Array.Empty<AttackTiming>());

    /// <summary>本招起点（量位移用，测试也读它）。</summary>
    public Vector3 Origin { get; private set; }

    public AttackData? Data => _data;

    public void Configure(AttackData data)
    {
        _data = data;

        _timing = new AttackTiming
        {
            StartupFrames = 0,                 // ★ 见类型注释：前摇＝蓄力那几帧
            ActiveFrames = data.ActiveFrames,
            RecoveryFrames = data.RecoveryFrames,
            CancelFromRecoveryFrame = -1,      // 02 §2：蓄力斩不可取消
        };

        _sequence = new AttackSequence(new[] { _timing });
    }

    public override int TotalFrames => _timing.TotalFrames;

    public override void Enter()
    {
        base.Enter();

        if (_data is null)
        {
            ChangeState<IdleState>();
            return;
        }

        Origin = Actor.GlobalPosition;
        _sequence.Start();
        Actor.PrimaryHitbox?.SetAttack(_data);
        Actor.PrimaryHitbox?.SetActive(false);
        Actor.SetCurrentAttack(_data.Id);
        Actor.OnAttackStarted(_data);
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
        if (!_sequence.IsRunning || _data is null)
        {
            ChangeState<IdleState>();
            return;
        }

        Actor.PrimaryHitbox?.SetActive(_sequence.IsActive);

        // 位移：与 AttackState 同一套权重（前快后慢，均值 1.0，总位移＝AdvanceDistance）。
        int advanceEnd = Mathf.Max(1, _timing.ActiveEnd);
        if (_sequence.Frame < advanceEnd && _data.AdvanceDistance > 0f)
        {
            float t = _sequence.Frame / (float)advanceEnd;
            float weight = 1.6f - 1.2f * t;
            float perFrame = _data.AdvanceDistance * weight / advanceEnd;

            Vector3 forward = -Actor.GlobalTransform.Basis.Z;
            forward.Y = 0f;
            Actor.DesiredVelocity = forward.Normalized() * (perFrame * Utils.Frames.PerSecond);
        }

        _sequence.Tick();
        SetFrame(_sequence.Frame);

        if (_sequence.IsFinished)
        {
            Actor.PrimaryHitbox?.SetActive(false);
            ChangeState<IdleState>();
        }
    }
}
