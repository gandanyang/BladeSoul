using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 弹开窗的合成规则（08 文档 §3 P1-3）。
/// 它是全项目最重要的一个数字，四个来源的叠加必须只有一条路径。
/// </summary>
public class CombatTuningTests
{
    [Theory]
    [InlineData(6)]    // 修罗
    [InlineData(9)]    // 武士（默认）
    [InlineData(12)]   // 剑客
    [InlineData(16)]   // 見習
    public void Bare_Difficulty_Value_Passes_Through(int baseFrames)
    {
        Assert.Equal(baseFrames, CombatTuning.ResolveDeflectWindowFrames(baseFrames));
    }

    [Fact]
    public void Upgrade_And_Dda_Add_On_Top()
    {
        // 武士 9 + 技线 1 + DDA 2 = 12
        Assert.Equal(12, CombatTuning.ResolveDeflectWindowFrames(9, 1, 2));
    }

    [Fact]
    public void Upgrade_Bonus_Is_Capped()
    {
        // 技线最多 +2：9 + 99 + 0 → 11
        Assert.Equal(11, CombatTuning.ResolveDeflectWindowFrames(9, 99, 0));
    }

    [Fact]
    public void Dda_Bonus_Is_Capped()
    {
        // DDA 最多 +4：9 + 0 + 99 → 13
        Assert.Equal(13, CombatTuning.ResolveDeflectWindowFrames(9, 0, 99));
    }

    [Fact]
    public void Result_Is_Clamped_At_Both_Ends()
    {
        // 已经是最宽的一档再加满，也不许超过上限——否则弹开退化成"按住就赢"
        Assert.Equal(CombatTuning.MaxDeflectWindowFrames, CombatTuning.ResolveDeflectWindowFrames(16, 2, 4));
        // 低于下限的窗口不是"时机判定"，是抽奖
        Assert.Equal(CombatTuning.MinDeflectWindowFrames, CombatTuning.ResolveDeflectWindowFrames(2));
    }

    [Fact]
    public void Negative_Bonuses_Are_Ignored_Not_Subtracted()
    {
        // 负数的"加成"一定是上游算错了。宁可忽略它，也不要把难度偷偷调高。
        Assert.Equal(9, CombatTuning.ResolveDeflectWindowFrames(9, -5, -3));
    }
}
