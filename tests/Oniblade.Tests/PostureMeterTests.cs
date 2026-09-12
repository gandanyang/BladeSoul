using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

public class PostureMeterTests
{
    [Fact]
    public void Apply_Accumulates_And_Breaks_At_Max()
    {
        var p = new PostureMeter(100);

        Assert.False(p.Apply(60));
        Assert.Equal(60, p.Current);

        Assert.True(p.Apply(50));
        Assert.Equal(100, p.Current);
        Assert.True(p.IsBroken);
    }

    [Fact]
    public void Apply_Ignores_NonPositive_Damage()
    {
        var p = new PostureMeter(100);

        Assert.False(p.Apply(0));
        Assert.False(p.Apply(-5));
        Assert.Equal(0, p.Current);
    }

    [Fact]
    public void Regen_Does_Not_Start_Before_Delay_Elapses()
    {
        var p = new PostureMeter(100) { RegenDelayFrames = 30, RegenPerSecond = 22f };
        p.Apply(60);

        p.Tick(30, 1.0f);

        Assert.Equal(60, p.Current);
    }

    [Fact]
    public void Regen_Recovers_After_Delay()
    {
        var p = new PostureMeter(100) { RegenDelayFrames = 30, RegenPerSecond = 22f };
        p.Apply(60);
        p.Tick(30, 1.0f);           // 只是把延迟耗完，不回复

        p.Tick(60, 1.0f);           // 一整秒：回复 22

        Assert.Equal(38, p.Current);
    }

    [Fact]
    public void Broken_Posture_Does_Not_Regen_On_Its_Own()
    {
        var p = new PostureMeter(100) { RegenDelayFrames = 30, RegenPerSecond = 22f };
        p.Apply(100);

        p.Tick(300, 1.0f);
        p.Tick(300, 1.0f);

        Assert.Equal(100, p.Current);
        Assert.True(p.IsBroken);
    }

    [Fact]
    public void Reset_Clears_Broken_State()
    {
        var p = new PostureMeter(100);
        p.Apply(100);

        p.Reset();

        Assert.Equal(0, p.Current);
        Assert.False(p.IsBroken);
    }

    [Fact]
    public void ApplyPercent_Uses_Max_Posture()
    {
        var p = new PostureMeter(200);

        p.ApplyPercent(0.70f);

        Assert.Equal(140, p.Current);
    }

    [Theory]
    [InlineData(1.00f, 1.0f)]
    [InlineData(0.71f, 1.0f)]
    [InlineData(0.70f, 0.6f)]
    [InlineData(0.30f, 0.6f)]
    [InlineData(0.29f, 0.3f)]
    [InlineData(0.00f, 0.3f)]
    public void Regen_Multiplier_Drops_With_Health(float healthRatio, float expected)
    {
        Assert.Equal(expected, PostureMeter.RegenMultiplierForHealth(healthRatio), 3);
    }
}
