using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 弹开反馈的**四档集合**（T53）。
///
/// 两个消费者（特效层 <c>CombatVfxDirector</c> 与音频层 <c>AudioDirector</c>）
/// 都只经 <see cref="Load"/> 取同一个实例——Godot 对同一路径返回同一个
/// <see cref="Resource"/>，所以"一个数字一个来源"成立，
/// 而这两层之间**不需要互相认识**（音频层不必依赖特效层）。
/// 唯一的查表入口是 <see cref="For"/>。
/// </summary>
[GlobalClass]
public partial class DeflectFeedbackSet : Resource
{
    /// <summary>
    /// 数据路径。按 `data/combat/**` 的既有约定：**路径常量写在代码里**，
    /// 资源本身可以在场景/节点上覆盖（同 <c>Hud.DeathblowProfilePath</c>）。
    /// </summary>
    public const string DefaultPath = "res://data/combat/deflect_feedback.tres";

    [Export] public DeflectFeedbackProfile? Slash { get; set; }
    [Export] public DeflectFeedbackProfile? Thrust { get; set; }
    [Export] public DeflectFeedbackProfile? Blunt { get; set; }
    [Export] public DeflectFeedbackProfile? Dark { get; set; }

    /// <summary>
    /// 取四档集合。**缺数据时返回 null，不抛异常**——按项目纪律，
    /// 配置缺失应当表现为"退化成旧观感"（弹开照旧有音效和火花），
    /// 而不是让战斗直接崩掉。
    ///
    /// ★ 刻意**不自己缓存**：<c>GD.Load</c> 对同一路径本来就返回同一个
    /// <see cref="Resource"/> 实例，再存一份静态引用只会把资源钉在内存里
    /// ——实测会在退出时报 "5 resources still in use"（1 个 set + 4 个档）。
    /// </summary>
    public static DeflectFeedbackSet? Load() =>
        ResourceLoader.Exists(DefaultPath) ? GD.Load<DeflectFeedbackSet>(DefaultPath) : null;

    /// <summary>
    /// 按攻击性质取档。默认落到斩击档——`DamageType.Slash` 是
    /// <c>AttackData.Type</c> 的默认值，也是"说不清性质"时的安全档。
    /// </summary>
    public DeflectFeedbackProfile? For(DamageType type) => type switch
    {
        DamageType.Blunt => Blunt,
        DamageType.Thrust => Thrust,
        DamageType.Dark => Dark,
        _ => Slash,
    };
}
