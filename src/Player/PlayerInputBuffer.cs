using System;

namespace Oniblade.Player;

/// <summary>
/// 输入缓冲（纯逻辑，可单测）。
/// 作用：后摇里提前按下的动作不会被吞掉，避免"我明明按了却没反应"——
/// 这是动作游戏里最容易劝退人的一种挫败。
///
/// 用 Consume 而不是 Peek：一次输入只能被一个状态消费，
/// 否则"攻击"会被攻击状态和防御状态同时读到，产生幽灵行为。
/// </summary>
public sealed class PlayerInputBuffer
{
    private const int Capacity = 16;

    private readonly (PlayerAction Action, int Frame)[] _ring = new (PlayerAction, int)[Capacity];
    private int _head;
    private int _count;

    /// <summary>从游戏开始算起的逻辑帧号。</summary>
    public int CurrentFrame { get; private set; }

    public int Count => _count;

    /// <summary>超过这个寿命的输入会被 Tick 自动丢弃。</summary>
    public int DefaultLifetimeFrames { get; init; } = 8;

    /// <summary>每帧最开头调用一次：帧号自增并丢弃过期输入。</summary>
    public void Tick()
    {
        CurrentFrame++;

        while (_count > 0 && CurrentFrame - _ring[_head].Frame > DefaultLifetimeFrames)
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }
    }

    public void Push(PlayerAction action)
    {
        if (_count < Capacity)
        {
            _ring[(_head + _count) % Capacity] = (action, CurrentFrame);
            _count++;
            return;
        }

        // 满了就顶掉最旧的一条，保证"最新输入一定在缓冲里"。
        _ring[_head] = (action, CurrentFrame);
        _head = (_head + 1) % Capacity;
    }

    /// <summary>在 windowFrames 帧内按过该动作吗？命中则消费掉。</summary>
    public bool Consume(PlayerAction action, int windowFrames)
    {
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head + i) % Capacity;
            if (_ring[idx].Action == action && CurrentFrame - _ring[idx].Frame <= windowFrames)
            {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>只看不消费（调试面板用）。</summary>
    public bool Has(PlayerAction action, int windowFrames)
    {
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head + i) % Capacity;
            if (_ring[idx].Action == action && CurrentFrame - _ring[idx].Frame <= windowFrames)
                return true;
        }

        return false;
    }

    /// <summary>某动作距离按下已经过了几帧；没按过返回 -1。</summary>
    public int Age(PlayerAction action)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            int idx = (_head + i) % Capacity;
            if (_ring[idx].Action == action)
                return CurrentFrame - _ring[idx].Frame;
        }

        return -1;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    private void RemoveAt(int logicalIndex)
    {
        for (int i = logicalIndex; i < _count - 1; i++)
        {
            int a = (_head + i) % Capacity;
            int b = (_head + i + 1) % Capacity;
            _ring[a] = _ring[b];
        }

        _count--;
    }
}
