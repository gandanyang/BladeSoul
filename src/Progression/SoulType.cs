namespace Oniblade.Progression;

/// <summary>
/// 四种魄（03 文档 §6.1）。名字刻意不用鬼武者的"魂"配色命名，规避 IP 风险。
/// </summary>
public enum SoulType
{
    /// <summary>赤魄：经验值 → 升级。</summary>
    Crimson,

    /// <summary>黄魄：立即回复体力。</summary>
    Amber,

    /// <summary>青魄：补充鬼力槽。</summary>
    Azure,

    /// <summary>白魄：稀有，强化武器 / 解锁一闪变体。</summary>
    White,
}
