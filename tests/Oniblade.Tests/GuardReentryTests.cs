using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 02 §8「连打防御惩罚」在中立态上的那一半。
/// 它堵的是"从站立反复点按防御 = 每次都能重新开窗"的漏洞。
/// </summary>
public class GuardReentryTests
{
    [Fact]
    public void First_Press_Is_Never_A_Quick_Reentry()
    {
        // 从没松过手 → 第一次按下不该被惩罚
        Assert.False(GuardReentry.IsQuickReentry(currentFrame: 100, lastGuardReleaseFrame: -1, reentryLockFrames: 8));
    }

    [Fact]
    public void Re_Press_Inside_The_Lock_Is_Punished()
    {
        // 第 100 帧松手，第 104 帧又按 → 间隔 4 < 8 → 快速重按
        Assert.True(GuardReentry.IsQuickReentry(currentFrame: 104, lastGuardReleaseFrame: 100, reentryLockFrames: 8));
    }

    [Theory]
    [InlineData(107, true)]    // 间隔 7，仍锁着
    [InlineData(108, false)]   // 间隔 8，刚好放行
    [InlineData(140, false)]   // 间隔很久，正常按下
    public void Lock_Boundary_Is_Exclusive(int currentFrame, bool expected)
    {
        Assert.Equal(expected, GuardReentry.IsQuickReentry(currentFrame, lastGuardReleaseFrame: 100, reentryLockFrames: 8));
    }

    [Fact]
    public void Zero_Lock_Disables_The_Punishment()
    {
        // 某个难度档把锁定期设成 0 就等于关掉这条惩罚——用来验证"惩罚是可拔掉的"
        Assert.False(GuardReentry.IsQuickReentry(currentFrame: 101, lastGuardReleaseFrame: 100, reentryLockFrames: 0));
        Assert.False(GuardReentry.IsQuickReentry(currentFrame: 101, lastGuardReleaseFrame: 100, reentryLockFrames: -1));
    }
}
