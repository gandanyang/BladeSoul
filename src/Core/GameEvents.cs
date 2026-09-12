using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Progression;

namespace Oniblade.Core;

/// <summary>一次攻防结算的完整记录。广播给 HUD、音频、调试面板、战斗统计。</summary>
public readonly struct HitEvent
{
    public int AttackerId { get; init; }
    public int DefenderId { get; init; }
    public Verdict Verdict { get; init; }

    /// <summary>攻方招式 Id（调试面板与统计用）。</summary>
    public string AttackId { get; init; }

    /// <summary>一闪的种类；非一闪时为 None。</summary>
    public IssenKind IssenKind { get; init; }

    public int Damage { get; init; }
    public int PostureDamage { get; init; }
    public int HitStopFrames { get; init; }

    /// <summary>结算发生的逻辑帧号。</summary>
    public int Frame { get; init; }

    /// <summary>防御方是否因本次结算死亡/被忍杀。</summary>
    public bool Killed { get; init; }
}

/// <summary>弹开连击数变化（音频用它做音高递增）。<see cref="Chain"/> 为 0 表示断连。</summary>
public readonly struct DeflectChainEvent
{
    public int ActorId { get; init; }
    public int Chain { get; init; }
}

/// <summary>获得魄。</summary>
public readonly struct SoulGainedEvent
{
    public SoulType Type { get; init; }
    public int Amount { get; init; }
}
