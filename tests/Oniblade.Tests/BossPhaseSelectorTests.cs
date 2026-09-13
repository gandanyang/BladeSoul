using System.Collections.Generic;
using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// BOSS 阶段判定（T9）。阈值取一组典型的：满血 / 60% / 25%。
/// </summary>
public class BossPhaseSelectorTests
{
    private static readonly float[] Thresholds = { 1.0f, 0.6f, 0.25f };

    [Theory]
    [InlineData(1.0f, 0)]     // 满血 → 第 1 阶段
    [InlineData(0.8f, 0)]
    [InlineData(0.6f, 1)]     // ★ 边界：刚好到就是下一阶段
    [InlineData(0.4f, 1)]
    [InlineData(0.25f, 2)]
    [InlineData(0.01f, 2)]    // 快死了也仍然在最后一个阶段里
    [InlineData(0f, 2)]       // 死了也不许掉出列表
    public void ResolvePhase_PicksTheRightPhase(float ratio, int expected)
    {
        Assert.Equal(expected, BossPhaseSelector.ResolvePhase(ratio, Thresholds));
    }

    [Fact]
    public void ResolvePhase_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, BossPhaseSelector.ResolvePhase(0.5f, new List<float>()));
    }

    [Fact]
    public void IsDescending_AcceptsTheTypicalSet()
    {
        Assert.True(BossPhaseSelector.IsDescending(Thresholds));
    }

    [Theory]
    [InlineData(new[] { 0.6f, 1.0f })]      // 写反了 → 阶段会跳来跳去
    [InlineData(new[] { 1.0f, 1.0f })]      // 重复阈值
    [InlineData(new[] { 1.2f, 0.5f })]      // 第一个超过满血
    [InlineData(new[] { 1.0f, 0f })]        // 最后一个是 0 → 永远进不去
    public void IsDescending_RejectsBadTables(float[] bad)
    {
        Assert.False(BossPhaseSelector.IsDescending(bad));
    }
}
