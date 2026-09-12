using System;

namespace Oniblade.Combat;

/// <summary>
/// 判定窗的**唯一计算入口**（08 文档 §3 P1-3 的红线）。
///
/// 弹开窗会被至少四处修改：难度档基础值（6~16 帧）、「技」线升级（+1/级）、
/// DDA（最多 +4）、以及将来可能出现的别的修正。
/// 如果它们各自去改 <c>DifficultyProfile.DeflectWindowFrames</c>，
/// 最后没有任何人知道场上这一帧到底从哪来——而它是全项目最重要的一个数字。
///
/// 所以：**除了这个函数，任何地方不许自己算弹开窗。**
///
/// 纯逻辑，可单测。
/// </summary>
public static class CombatTuning
{
    /// <summary>低于这个宽度就不是"时机判定"了，任何难度都不许低于它。</summary>
    public const int MinDeflectWindowFrames = 4;

    /// <summary>高于这个宽度弹开会变成"按住就赢"，失去意义。</summary>
    public const int MaxDeflectWindowFrames = 20;

    /// <summary>「技」线升级最多给多少帧（03 文档 §6.2：数值成长要转化为操作宽容度，但不能无限）。</summary>
    public const int MaxUpgradeDeflectBonusFrames = 2;

    /// <summary>隐式 DDA 最多给多少帧（05 文档 §3）。</summary>
    public const int MaxDdaDeflectBonusFrames = 4;

    /// <summary>
    /// 合成最终的弹开窗。
    /// 注意「半自动防御」不在这里：它改变的是"谁按下了防御键"，
    /// 不是窗口本身有多宽，混进来会让这个函数的语义变脏。
    /// </summary>
    public static int ResolveDeflectWindowFrames(
        int difficultyBaseFrames,
        int upgradeBonusFrames = 0,
        int ddaBonusFrames = 0)
    {
        int upgrade = Math.Clamp(upgradeBonusFrames, 0, MaxUpgradeDeflectBonusFrames);
        int dda = Math.Clamp(ddaBonusFrames, 0, MaxDdaDeflectBonusFrames);

        return Math.Clamp(
            difficultyBaseFrames + upgrade + dda,
            MinDeflectWindowFrames,
            MaxDeflectWindowFrames);
    }
}
