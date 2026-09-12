using System;
using System.Collections.Generic;

namespace Oniblade.Combat.States;

/// <summary>
/// 状态机（POCO，可单测）。
///
/// **延迟切换**是这个类存在的全部理由：如果允许在 Tick 中途直接改 Current，
/// 当前状态的 Frame 会在同一帧被重置，帧数据判定就会整体偏移 1 帧。
/// 所有切换请求统一排队到本次 Tick 结束后处理。
/// </summary>
public sealed class StateMachine
{
    private readonly Dictionary<Type, ActorState> _states = new();
    private readonly CombatActor _owner;
    private ActorState? _pending;

    public StateMachine(CombatActor owner) => _owner = owner;

    public ActorState? Current { get; private set; }

    public T Add<T>(T state) where T : ActorState
    {
        state.Attach(_owner, this);
        _states[typeof(T)] = state;
        return state;
    }

    public T Get<T>() where T : ActorState => (T)_states[typeof(T)];

    public bool Has<T>() where T : ActorState => _states.ContainsKey(typeof(T));

    /// <summary>请求切换（帧末生效）。</summary>
    public void Change<T>() where T : ActorState
    {
        if (_states.TryGetValue(typeof(T), out ActorState? next))
            _pending = next;
    }

    /// <summary>无视 `CanTransitionTo` 的强制切换（受击、处决这类必须打断的情况）。</summary>
    public void ForceChange<T>() where T : ActorState
    {
        if (!_states.TryGetValue(typeof(T), out ActorState? next))
            return;

        Current?.Exit();
        Current = next;
        Current.Enter();
        _pending = null;
    }

    /// <summary>初始化用：立刻进入第一个状态。</summary>
    public void Start<T>() where T : ActorState => ForceChange<T>();

    public void Tick()
    {
        Current?.Tick();

        if (_pending is null || _pending == Current)
        {
            _pending = null;
            return;
        }

        if (Current is null || Current.CanTransitionTo(_pending))
        {
            Current?.Exit();
            Current = _pending;
            Current.Enter();
        }

        _pending = null;
    }
}
