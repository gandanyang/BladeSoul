using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// BOSS 的一个阶段（T9 / 08 §3 P2-2）。**阶段是数据，不是代码分支。**
///
/// 03 与 06 大量依赖"P2 不断崖""阶段转换必须是已会机制"，
/// 而 04 里一直没有 `BossPhase` 这个概念——这份资源就是补上那个定义。
/// </summary>
[GlobalClass]
public partial class BossPhaseProfile : Resource
{
    [ExportGroup("标识")]
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>阶段序号（0 ＝ 开场）。只用于可读性与校验。</summary>
    [Export] public int PhaseIndex { get; set; }

    [ExportGroup("切换")]

    /// <summary>进入本阶段的**血量比例阈值**（1.0 ＝ 满血）。列表必须从高到低。</summary>
    [Export] public float HealthThreshold { get; set; } = 1f;

    /// <summary>转场动画名（进这个阶段时播什么）。留空表示硬切。</summary>
    [Export] public string TransitionAnimName { get; set; } = "";

    [ExportGroup("招式池")]

    /// <summary>本阶段能用哪些招。**空池 ＝ 这个阶段不会攻击**，自检会报错。</summary>
    [Export] public Godot.Collections.Array<AttackData> Attacks { get; set; } = new();

    [ExportGroup("AI")]
    [Export] public BossAiOverrides? Ai { get; set; }
}
