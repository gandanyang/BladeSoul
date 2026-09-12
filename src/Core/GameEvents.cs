using Godot;
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

    /// <summary>
    /// 接触点（世界坐标，取防御方受击框的位置，约在胸口）。
    ///
    /// T28 加的：特效层（火花 / 血雾）必须知道"打在哪里"，
    /// 而在那之前事件里只有 id，位置无从得知——每个消费者都得自己去反查场景树。
    /// 与 <see cref="ActorDefeatedEvent.Position"/> 同一个理由：
    /// **消费者需要什么，事件就带上什么**。
    /// </summary>
    public Vector3 Position { get; init; }

    /// <summary>
    /// 这一刀的来向（攻方 → 防御方，水平方向，已归一化）。
    /// 血雾要"顺着刀路"而不是原地爆开（10 §4），所以方向是必须的。
    /// </summary>
    public Vector3 Direction { get; init; }
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

/// <summary>
/// 一个战斗单位被打倒。比 <c>ActorDied(int)</c> 多带**位置与档次**——
/// 因为"魄从尸体里飞出来"这件事必须知道尸体在哪、以及它值多少（03 §6.1）。
/// </summary>
public readonly struct ActorDefeatedEvent
{
    public int ActorId { get; init; }
    public Vector3 Position { get; init; }
    public EnemyTier Tier { get; init; }

    /// <summary>击杀者；暂时不用（想写战斗统计时再填）。</summary>
    public int KillerActorId { get; init; }
}
