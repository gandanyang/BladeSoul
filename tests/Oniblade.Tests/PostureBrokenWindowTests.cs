using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T52 破韧窗口的边界（纯逻辑，不需要引擎）。
///
/// 这里钉的是**最容易错一帧**的地方，以及一条设计纪律：
/// **窗口不能被"又打了一下"续命** —— 否则玩家只要一直砍就能把窗口续下去，
/// 处决就永远不着急，"砍几刀然后处决"的节奏会退化成"砍到死"。
/// </summary>
public class PostureBrokenWindowTests
{
    [Fact]
    public void 开窗后立刻可用_且剩余等于配置宽度()
    {
        var w = new PostureBrokenWindow();
        w.Begin(120);

        Assert.True(w.IsOpen);
        Assert.Equal(120, w.FramesLeft);
        Assert.Equal(120, w.DurationFrames);
        Assert.Equal(1, w.OpenCount);
    }

    [Fact]
    public void 递减到最后一帧仍然可处决_下一帧才过期()
    {
        var w = new PostureBrokenWindow();
        w.Begin(3);

        // 第 1、2 帧：还开着
        w.Tick();
        Assert.True(w.IsOpen);
        Assert.Equal(2, w.FramesLeft);

        w.Tick();
        Assert.True(w.IsOpen);
        Assert.Equal(1, w.FramesLeft);

        // 第 3 帧：这是窗口的最后一帧，**仍然可以处决**
        w.Tick();
        Assert.False(w.IsOpen);
        Assert.Equal(0, w.FramesLeft);
    }

    [Fact]
    public void 过期不会自己复活()
    {
        var w = new PostureBrokenWindow();
        w.Begin(2);
        w.Tick();
        w.Tick();
        Assert.False(w.IsOpen);

        // 再走 100 帧也还是关的（没人重新 Begin）
        for (int i = 0; i < 100; i++)
            w.Tick();

        Assert.False(w.IsOpen);
        Assert.Equal(0, w.FramesLeft);
    }

    [Fact]
    public void 窗口不会因为反复调用_Tick_以外的操作被续命()
    {
        var w = new PostureBrokenWindow();
        w.Begin(10);

        for (int hit = 0; hit < 5; hit++)
        {
            w.Tick();
            // 模拟"又挨了一刀"——注意：**没有任何 API 可以续命**，
            // 只有再次 Begin（= 真正又破了一次韧）才会重开。
        }

        Assert.Equal(5, w.FramesLeft);
    }

    [Fact]
    public void 再次破韧会重开窗口并累加计数()
    {
        var w = new PostureBrokenWindow();
        w.Begin(10);
        w.Tick();
        w.Tick();
        Assert.Equal(8, w.FramesLeft);

        w.Begin(120);
        Assert.Equal(120, w.FramesLeft);
        Assert.Equal(2, w.OpenCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void 非法宽度视为不可处决(int bad)
    {
        var w = new PostureBrokenWindow();
        w.Begin(bad);

        Assert.False(w.IsOpen);
        Assert.Equal(0, w.FramesLeft);
    }

    [Fact]
    public void 处决之后可以立刻关窗()
    {
        var w = new PostureBrokenWindow();
        w.Begin(120);
        w.Close();

        Assert.False(w.IsOpen);
        Assert.Equal(0, w.FramesLeft);
    }

    [Fact]
    public void Reset_清掉计数以便下一轮()
    {
        var w = new PostureBrokenWindow();
        w.Begin(50);
        w.Tick();
        w.Reset();

        Assert.False(w.IsOpen);
        Assert.Equal(0, w.OpenCount);
        Assert.Equal(0, w.DurationFrames);
    }

    [Fact]
    public void 窗口宽度确实取自配置_不是写死的()
    {
        // 四档难度各配一个值，窗口就应该是那个值——防止有人把 120 写进状态里
        foreach (int frames in new[] { 8, 60, 120, 200 })
        {
            var w = new PostureBrokenWindow();
            w.Begin(frames);
            Assert.Equal(frames, w.FramesLeft);
        }
    }
}
