using System;
using Godot;

namespace Oniblade.Core;

/// <summary>
/// 全局事件总线（Autoload）。游戏内部一律用强类型 C# event，不用 Godot signal——
/// 只有需要编辑器连线的地方（UI 按钮）才用 [Signal]。
///
/// ⚠️ static 单例上的 C# event 会强引用订阅者。节点被 QueueFree() 后如果没退订，
/// 事件会一直持有已释放对象。**规矩：每个订阅者必须在 _ExitTree() 里退订。**
/// </summary>
public partial class EventBus : Node
{
    public static EventBus? Instance { get; private set; }

    public event Action<int, int>? HealthChanged;      // actorId, value
    public event Action<int, int>? PostureChanged;     // actorId, value
    public event Action<int>? PostureBroken;           // actorId
    public event Action<HitEvent>? HitResolved;
    public event Action<int>? ActorDied;               // actorId
    public event Action<DeflectChainEvent>? DeflectChainChanged;
    public event Action<SoulGainedEvent>? SoulGained;

    /// <summary>有人被打倒（带位置与档次）。魄火靠它生成。</summary>
    public event Action<ActorDefeatedEvent>? ActorDefeated;

    /// <summary>难度档切换（HUD 与 AI 都要重新读参数）。</summary>
    public event Action<string>? DifficultyChanged;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree() => Instance = null;

    public void RaiseHealthChanged(int actorId, int value) => HealthChanged?.Invoke(actorId, value);

    public void RaisePostureChanged(int actorId, int value) => PostureChanged?.Invoke(actorId, value);

    public void RaisePostureBroken(int actorId) => PostureBroken?.Invoke(actorId);

    public void RaiseHitResolved(in HitEvent e)
    {
        HitResolved?.Invoke(e);
        PostureChanged?.Invoke(e.DefenderId, e.PostureDamage);
        if (e.Killed)
            ActorDied?.Invoke(e.DefenderId);
    }

    public void RaiseDeflectChain(in DeflectChainEvent e) => DeflectChainChanged?.Invoke(e);

    public void RaiseSoulGained(in SoulGainedEvent e) => SoulGained?.Invoke(e);

    public void RaiseActorDefeated(in ActorDefeatedEvent e) => ActorDefeated?.Invoke(e);

    public void RaiseDifficultyChanged(string difficultyId) => DifficultyChanged?.Invoke(difficultyId);
}
