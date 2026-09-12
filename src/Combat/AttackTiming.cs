using System;
using System.Collections.Generic;

namespace Oniblade.Combat;

/// <summary>
/// 一招的帧结构，**不含任何 Godot 类型**，因此可以脱离引擎单测。
/// <see cref="Data.AttackData"/> 通过 ToTiming() 转换。
///
/// 帧号规则：0 是招式第一帧。
///   前摇 [0, ActiveStart) / 判定 [ActiveStart, ActiveEnd) / 后摇 [RecoveryStart, TotalFrames)
/// </summary>
public readonly struct AttackTiming
{
    public int StartupFrames { get; init; }
    public int ActiveFrames { get; init; }
    public int RecoveryFrames { get; init; }

    /// <summary>进入后摇后第几帧起可被取消；-1 = 整招不可取消。</summary>
    public int CancelFromRecoveryFrame { get; init; }

    public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;
    public int ActiveStart => StartupFrames;
    public int ActiveEnd => StartupFrames + ActiveFrames;
    public int RecoveryStart => ActiveEnd;

    public bool Cancelable => CancelFromRecoveryFrame >= 0;

    /// <summary>取消窗开启的绝对帧号；不可取消时为 <see cref="int.MaxValue"/>。</summary>
    public int CancelOpenFrame => Cancelable ? RecoveryStart + CancelFromRecoveryFrame : int.MaxValue;

    public bool IsActiveAt(int frame) => frame >= ActiveStart && frame < ActiveEnd;
    public bool IsInRecoveryAt(int frame) => frame >= RecoveryStart && frame < TotalFrames;
    public bool CanCancelAt(int frame) => frame >= CancelOpenFrame;
}

/// <summary>
/// 一套连段的推进器（纯逻辑，可单测）。
///
/// 它只回答一个问题：**"现在能不能接下一段"**——
/// 答案是"必须已经进入后摇的取消窗"。这条规则是反连打的核心：
/// 狂按攻击键不会让你更快，只会让你在能接的时候接到。
/// </summary>
public sealed class AttackSequence
{
    private readonly AttackTiming[] _steps;

    public AttackSequence(IReadOnlyList<AttackTiming> steps)
    {
        _steps = new AttackTiming[steps.Count];
        for (int i = 0; i < steps.Count; i++)
            _steps[i] = steps[i];
    }

    public int StepCount => _steps.Length;

    /// <summary>当前段（0 起）；-1 表示没有在出招。</summary>
    public int StepIndex { get; private set; } = -1;

    /// <summary>当前段已经过了几帧。</summary>
    public int Frame { get; private set; }

    public bool IsRunning => StepIndex >= 0 && StepIndex < _steps.Length;

    public AttackTiming Current => IsRunning ? _steps[StepIndex] : default;

    public void Start()
    {
        if (_steps.Length == 0)
            return;

        StepIndex = 0;
        Frame = 0;
    }

    public void Stop()
    {
        StepIndex = -1;
        Frame = 0;
    }

    /// <summary>推进一帧。</summary>
    public void Tick()
    {
        if (IsRunning)
            Frame++;
    }

    public bool IsFinished => !IsRunning || Frame >= Current.TotalFrames;

    /// <summary>本招当前是否在判定帧（刀真的在扫）。</summary>
    public bool IsActive => IsRunning && Current.IsActiveAt(Frame);

    /// <summary>现在能不能接下一段。</summary>
    public bool CanChain => IsRunning
        && StepIndex + 1 < _steps.Length
        && Current.CanCancelAt(Frame);

    /// <summary>接下一段。返回 false 表示当前不允许接。</summary>
    public bool TryChain()
    {
        if (!CanChain)
            return false;

        StepIndex++;
        Frame = 0;
        return true;
    }
}
