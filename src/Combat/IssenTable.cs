using Oniblade.Combat.Data;

namespace Oniblade.Combat;

/// <summary>一闪的收益。纯数据，可单测。</summary>
public readonly struct IssenEffect
{
    /// <summary>杂兵：即死。</summary>
    public bool InstantKill { get; init; }

    /// <summary>精英 / BOSS：按最大体干的百分比扣除。</summary>
    public float PostureDamagePercent { get; init; }

    /// <summary>BOSS：额外硬直帧数，给玩家反打窗口。</summary>
    public int StunFrames { get; init; }
}

/// <summary>
/// 一闪收益表（源自 02 文档 §2.3）。
/// 设计意图：一闪对杂兵是"秒杀"，对精英是"重削体干"，对 BOSS 只是"削体干 + 一个硬直窗口"。
/// 这样一闪既爽，又不会让 BOSS 战退化成一闪猜拳。
/// </summary>
public static class IssenTable
{
    public static IssenEffect For(IssenKind kind, EnemyTier tier)
    {
        if (kind == IssenKind.None)
            return new IssenEffect();

        if (tier == EnemyTier.Grunt)
            return new IssenEffect { InstantKill = true };

        bool strong = kind is IssenKind.Deflect or IssenKind.Clash or IssenKind.Chain;
        bool boss = tier == EnemyTier.Boss;

        return new IssenEffect
        {
            PostureDamagePercent = strong ? (boss ? 0.30f : 0.70f) : (boss ? 0.25f : 0.60f),
            StunFrames = boss ? 16 : 0,
        };
    }
}
