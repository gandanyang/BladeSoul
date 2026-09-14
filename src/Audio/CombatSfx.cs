namespace Oniblade.Audio;

/// <summary>
/// 战斗音效的**逻辑名字**。音频层负责把它映射到实际音频文件与总线；
/// 战斗代码只说"我发生了弹开"，不关心用的是哪个 .ogg。
/// </summary>
public enum CombatSfx
{
    HitSlash,
    HitBlock,
    Deflect,
    Clash,
    GuardBreak,
    IssenSlash,
    IssenImpact,
    Deathblow,
    WhooshLight,
    WhooshHeavy,
    DodgeWhoosh,
    PerilousThrust,
    PerilousSweep,
    PerilousGrab,

    /// <summary>吸魄：音高随连吸数递增（03 §6.1）。</summary>
    SoulAbsorb,

    // ── T53：弹开的另外三种「攻击性质」档 ──────────────────────────
    //
    // ⚠️ 只能**追加在末尾**。.tres 里的枚举是**整数**（`Type = 0` 那种写法），
    // 往中间插入会让既有数据整体错位，而且错档是静默的。
    //
    // ⚠️ 命名不对称是**故意的**：`Deflect` 就是**斩击档**（那个"金属叮"本来
    // 就是斩该有的声音），不是"通用档"；斩之外的三个 DamageType 才各有一档。
    // 档位→音效的映射集中在 DeflectFeedbackSet 的数据里，调用点不写 if。

    /// <summary>弹开·打击：低频沉闷冲击。</summary>
    DeflectBlunt,

    /// <summary>弹开·突刺：更高频、更短促。</summary>
    DeflectThrust,

    /// <summary>弹开·暗：低频闷响。</summary>
    DeflectDark,
}

/// <summary>
/// 刀风的轻重。**由招式表决定**（<c>AttackData.Whoosh</c>），不按伤害在代码里猜。
///
/// 为什么单独一个枚举：<see cref="CombatSfx"/> 里已经有
/// <c>WhooshLight</c> / <c>WhooshHeavy</c>，但"这一招该用哪个"是**招式的知识**——
/// 突刺几乎不挥砍（该用轻风甚至不出声）、大上段双手劈要用重风、
/// 三段连斩该用递进的音高让耳朵听得出这是第几刀。
/// 这些只有 <c>data/attacks/**/*.tres</c> 知道。
/// </summary>
public enum WhooshKind
{
    /// <summary>不要刀风（例如纯粹的突刺，或"危"抓取）。</summary>
    None = 0,

    Light = 1,
    Heavy = 2,
}
