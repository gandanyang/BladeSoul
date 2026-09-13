using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 蓄力斩的段位选择（T46）。阈值取 02 §2.1 的 34 / 48 / 62。
///
/// 这个测试要钉死的是一件很容易写错的事：**边界是"大于等于"**——
/// 恰好按住 34 帧就该出第一段（因为那一招的前摇就是 34 帧）。
/// </summary>
public class ChargedAttackSelectorTests
{
    private const int T1 = 34;
    private const int T2 = 48;
    private const int T3 = 62;

    [Theory]
    [InlineData(0, 0)]     // 点一下就松 → 走轻攻击
    [InlineData(1, 0)]
    [InlineData(33, 0)]    // 差一帧也不算
    [InlineData(34, 1)]    // ★ 边界：刚好到就是第一段
    [InlineData(47, 1)]
    [InlineData(48, 2)]
    [InlineData(61, 2)]
    [InlineData(62, 3)]
    [InlineData(200, 3)]   // 一直按住也不会超过第三段
    public void ResolveLevel_PicksTheHighestReachedSegment(int holdFrames, int expected)
    {
        Assert.Equal(expected, ChargedAttackSelector.ResolveLevel(holdFrames, T1, T2, T3));
    }

    [Fact]
    public void ResolveLevel_ZeroThresholds_NeverReturnsZero()
    {
        // 万一有人把阈值配成 0（坏数据），第一段仍然该生效，而不是"永远走轻攻击"。
        Assert.Equal(3, ChargedAttackSelector.ResolveLevel(0, 0, 0, 0));
    }
}
