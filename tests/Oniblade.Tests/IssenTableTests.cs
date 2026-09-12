using Oniblade.Combat;
using Oniblade.Combat.Data;
using Xunit;

namespace Oniblade.Tests;

/// <summary>02 文档 §2.3 的一闪收益表。</summary>
public class IssenTableTests
{
    [Theory]
    [InlineData(IssenKind.Shin)]
    [InlineData(IssenKind.Deflect)]
    [InlineData(IssenKind.Dodge)]
    [InlineData(IssenKind.Clash)]
    [InlineData(IssenKind.Chain)]
    public void Grunt_Issen_Is_Always_Instant_Kill(IssenKind kind)
    {
        var e = IssenTable.For(kind, EnemyTier.Grunt);

        Assert.True(e.InstantKill);
        Assert.Equal(0f, e.PostureDamagePercent);
    }

    [Theory]
    [InlineData(IssenKind.Shin, 0.60f)]
    [InlineData(IssenKind.Dodge, 0.60f)]
    [InlineData(IssenKind.Deflect, 0.70f)]
    [InlineData(IssenKind.Clash, 0.70f)]
    public void Elite_Issen_Shaves_Posture_By_Kind(IssenKind kind, float expected)
    {
        var e = IssenTable.For(kind, EnemyTier.Elite);

        Assert.False(e.InstantKill);
        Assert.Equal(expected, e.PostureDamagePercent);
        Assert.Equal(0, e.StunFrames);
    }

    [Theory]
    [InlineData(IssenKind.Shin, 0.25f)]
    [InlineData(IssenKind.Deflect, 0.30f)]
    public void Boss_Issen_Shaves_Less_Posture_But_Always_Stuns(IssenKind kind, float expected)
    {
        var e = IssenTable.For(kind, EnemyTier.Boss);

        Assert.False(e.InstantKill);
        Assert.Equal(expected, e.PostureDamagePercent);
        Assert.Equal(16, e.StunFrames);
    }

    [Fact]
    public void No_Buff_Means_No_Effect()
    {
        var e = IssenTable.For(IssenKind.None, EnemyTier.Grunt);

        Assert.False(e.InstantKill);
        Assert.Equal(0f, e.PostureDamagePercent);
    }
}
