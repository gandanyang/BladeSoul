using Oniblade.UI;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T52 处决标记的显示判定（纯逻辑，不需要引擎）。
///
/// 钉的是两类**很难在实机里定位**的错误：
///   · 标记亮了但按 F 没反应（距离够了但窗口已关，或反过来）→ 玩家觉得"这游戏在骗我"
///   · 标记压根不亮 → 玩家根本不知道有处决这个机制
/// </summary>
public class DeathblowMarkerViewTests
{
    [Fact]
    public void 破韧且在距离内_亮标记()
    {
        Assert.True(DeathblowMarkerView.ShouldShow(true, 1.5f, 2.2f));
    }

    [Fact]
    public void 没破韧_不亮()
    {
        Assert.False(DeathblowMarkerView.ShouldShow(false, 1.0f, 2.2f));
    }

    [Theory]
    [InlineData(2.19f, true)]
    [InlineData(2.20f, true)]    // 边界含等号，与 DeathblowResolver 保持一致
    [InlineData(2.21f, false)]
    [InlineData(9.00f, false)]
    public void 距离边界(float distance, bool expected)
    {
        Assert.Equal(expected, DeathblowMarkerView.ShouldShow(true, distance, 2.2f));
    }

    [Fact]
    public void 距离判定是必须的_否则标记会骗人()
    {
        // ★ 这条是"标记骗人"的守门人：
        //   只判 CanBeExecuted 的话，远处刚被破韧的敌人也会亮标记，
        //   玩家跑过去时窗口早关了 —— 标记必须与处决判定同源。
        Assert.False(DeathblowMarkerView.ShouldShow(canBeExecuted: true, distance: 20f, maxDistance: 2.2f));
    }

    [Fact]
    public void 总开关关掉_一律不亮()
    {
        Assert.False(DeathblowMarkerView.ShouldShow(true, 1.0f, 2.2f, markerEnabled: false));
    }

    [Fact]
    public void 非法距离上限_不亮而不是全亮()
    {
        // 数据配错（0 或负数）时宁可什么都不显示，也不要"整个世界都在闪"
        Assert.False(DeathblowMarkerView.ShouldShow(true, 0.5f, 0f));
        Assert.False(DeathblowMarkerView.ShouldShow(true, 0.5f, -1f));
    }

    // ── 紧张度（闪烁节奏）──────────────────────────────────────

    [Fact]
    public void 窗口刚开_紧张度为0()
    {
        Assert.Equal(0f, DeathblowMarkerView.Urgency(120, 120));
    }

    [Fact]
    public void 窗口过半_紧张度约一半()
    {
        Assert.Equal(0.5f, DeathblowMarkerView.Urgency(60, 120), 2);
    }

    [Fact]
    public void 窗口将关_紧张度接近1()
    {
        Assert.True(DeathblowMarkerView.Urgency(1, 120) > 0.99f);
    }

    [Fact]
    public void 窗口已关_紧张度为1()
    {
        Assert.Equal(1f, DeathblowMarkerView.Urgency(0, 120));
    }

    [Fact]
    public void 紧张度用剩余比例_不是绝对帧数()
    {
        // 精英怪窗口若只有 60 帧，剩 30 帧（一半）应与杂兵剩 60 帧（一半）同样紧张。
        // 按绝对帧数算的话短窗口的怪会"从头闪到尾"，看不出"要没了"。
        Assert.Equal(
            DeathblowMarkerView.Urgency(60, 120),
            DeathblowMarkerView.Urgency(30, 60),
            3);
    }

    [Fact]
    public void 总帧数非法_紧张度为0而不是除零()
    {
        Assert.Equal(0f, DeathblowMarkerView.Urgency(10, 0));
        Assert.Equal(0f, DeathblowMarkerView.Urgency(10, -5));
    }
}
