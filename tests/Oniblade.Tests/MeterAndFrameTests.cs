using Oniblade.Combat;
using Oniblade.Utils;
using Xunit;

namespace Oniblade.Tests;

public class HealthMeterTests
{
    [Fact]
    public void Apply_Returns_Actual_Damage_And_Clamps_At_Zero()
    {
        var h = new HealthMeter(100);

        Assert.Equal(30, h.Apply(30));
        Assert.Equal(70, h.Current);
        Assert.Equal(70, h.Apply(999));
        Assert.Equal(0, h.Current);
        Assert.True(h.IsDead);
    }

    [Fact]
    public void Heal_Clamps_At_Max()
    {
        var h = new HealthMeter(100);
        h.Apply(40);

        h.Heal(999);

        Assert.Equal(100, h.Current);
    }

    [Fact]
    public void ReviveTo_Restores_A_Percentage()
    {
        var h = new HealthMeter(100);
        h.Apply(100);

        h.ReviveTo(50);

        Assert.Equal(50, h.Current);
        Assert.False(h.IsDead);
    }
}

public class FramesTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(9, 0.15)]
    [InlineData(60, 1.0)]
    public void ToSeconds_Converts_At_60fps(int frames, double expected)
    {
        Assert.Equal(expected, Frames.ToSeconds(frames), 6);
    }

    [Theory]
    [InlineData(0.15, 9)]
    [InlineData(0.1, 6)]
    [InlineData(1.0, 60)]
    public void FromSeconds_Rounds_To_Nearest_Frame(double seconds, int expected)
    {
        Assert.Equal(expected, Frames.FromSeconds(seconds));
    }
}
