using System;
using System.Collections.Generic;

namespace Oniblade.Combat;

/// <summary>
/// 处决判定的纯逻辑（T52）——**不依赖场景树**，可以被单元测试直接实例化。
///
/// 它只回答两件事：
///   1. **此刻按交互键，该不该出、出谁的处决？**（<see cref="Resolve"/>）
///   2. **交互键的四个使用者谁优先？**（<see cref="InteractPriority"/>）
///
/// # 为什么处决判定必须独立成类
/// 处决的判据有好几条正交条件（被破韧 / 窗口还开着 / 在距离内 / 自己还能动 /
/// 正在对话），每一条错一点，结果都是"按了没反应"或"随时秒怪"——而这两种
/// 在端到端测试里都表现为"某次按键没生效"，很难定位到是哪条。
/// 单测能把每条边界单独钉死。
///
/// # 机制口径（项目主人口述裁定，不许含糊）
/// · 处决键 = **交互键 F/E**，**普攻键绝不许处决**
///   —— 同键会让"砍几刀再处决"退化成"一想补刀就进处决"。
/// · 处决期间玩家**无敌**。
/// · 敌人被破韧后**不能主动攻击**、只能被打，窗口过了一定时间就恢复。
/// </summary>
public static class DeathblowResolver
{
    /// <summary>
    /// 交互键的四个使用者，**按优先级从高到低**排列。
    ///
    /// 交互键被复用了四次，所以必须有明确的阶梯。规则：
    /// **越"即时、越不可替代"的越优先**。
    ///   · 处决：只有这一瞬间的窗口，错过就没了 → 最高
    ///   · 对话：玩家主动开启的会话，中途被抢会很烦，但不会永久失去
    ///   · 教学收刀：引导性的，晚一帧做完全没关系
    ///   · 笼手深吸：**按住**就生效的持续行为，它天然可以等 → 最低
    /// </summary>
    public static readonly InteractPriority[] PriorityOrder =
    {
        InteractPriority.Deathblow,
        InteractPriority.Dialogue,
        InteractPriority.TutorialSheathe,
        InteractPriority.GauntletAbsorb,
    };

    /// <summary>
    /// 挑一个可以处决的目标。
    ///
    /// <paramref name="candidates"/> 里的元素只需要提供"在哪里、破没破韧、窗口开着没有"，
    /// 所以这个函数可以被单测直接喂假数据，不需要引擎。
    /// </summary>
    /// <param name="selfPosition">处决者（玩家）的位置。</param>
    /// <param name="maxDistance">处决距离（米）。来自 <c>data/combat/deathblow.tres</c>。</param>
    /// <param name="candidates">场上所有潜在目标。</param>
    /// <returns>选中的目标索引；<c>-1</c> = 没有可处决的目标。</returns>
    public static int Resolve(
        System.Numerics.Vector2 selfPosition,
        float maxDistance,
        IReadOnlyList<DeathblowCandidate> candidates)
    {
        if (candidates is null || candidates.Count == 0 || maxDistance <= 0f)
            return -1;

        int best = -1;
        float bestDistanceSq = maxDistance * maxDistance;

        for (int i = 0; i < candidates.Count; i++)
        {
            DeathblowCandidate c = candidates[i];

            if (!c.CanBeExecuted)
                continue;

            float dx = c.Position.X - selfPosition.X;
            float dy = c.Position.Y - selfPosition.Y;
            float distanceSq = dx * dx + dy * dy;

            if (distanceSq > bestDistanceSq)
                continue;

            // 取**最近的**：多个敌人同时破韧时，玩家expect的是"砍我面前这个"，
            // 而不是"列表里第一个"。列表顺序取决于场景树，那不是玩家的意图。
            bestDistanceSq = distanceSq;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// 有没有比"处决"更高优先级的交互消费者在抢这一帧的按键。
    /// 目前处决是最高优先级，所以只要满足处决条件就一定轮到处决——
    /// 但这条**写成函数而不是注释**，将来插入新消费者时不会漏。
    /// </summary>
    public static InteractPriority Winner(bool canDeathblow, bool inDialogue, bool tutorialWantsSheathe)
    {
        if (canDeathblow)
            return InteractPriority.Deathblow;
        if (inDialogue)
            return InteractPriority.Dialogue;
        if (tutorialWantsSheathe)
            return InteractPriority.TutorialSheathe;
        return InteractPriority.GauntletAbsorb;
    }
}

/// <summary>处决判定的输入数据。刻意做成纯数据，便于单测。</summary>
public readonly struct DeathblowCandidate
{
    /// <summary>水平位置（X/Z 平面）。处决是地面动作，不看高度差。</summary>
    public System.Numerics.Vector2 Position { get; init; }

    /// <summary>是否处于破韧态**且窗口还开着**（<c>Ashigaru.CanBeExecuted</c>）。</summary>
    public bool CanBeExecuted { get; init; }

    public static DeathblowCandidate Make(float x, float z, bool canBeExecuted) => new()
    {
        Position = new System.Numerics.Vector2(x, z),
        CanBeExecuted = canBeExecuted,
    };
}

/// <summary>
/// 交互键消费者。**名字即优先级顺序的成员**——新增消费者时请同时想清楚它插在哪一级，
/// 并加进 <see cref="DeathblowResolver.PriorityOrder"/>。
/// </summary>
public enum InteractPriority
{
    /// <summary>处决（最高：只有这一瞬间的窗口）。</summary>
    Deathblow = 0,

    /// <summary>对话推进。</summary>
    Dialogue = 1,

    /// <summary>教学第一幕的收刀引导。</summary>
    TutorialSheathe = 2,

    /// <summary>笼手深吸（按住生效，天然可以等，所以最低）。</summary>
    GauntletAbsorb = 3,
}
