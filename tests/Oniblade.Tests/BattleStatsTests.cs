using System;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;
using Oniblade.UI;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// 调试面板 F8 的统计口径（02 文档 §11）。
/// 这些断言存在的意义：平衡调整全部靠这几个数字，
/// 它们算错的时候不会有任何报错，只会让人把游戏调歪。
/// </summary>
public class BattleStatsTests
{
    private static HitEvent Hit(
        int frame,
        Verdict verdict,
        int attacker = 1,
        int defender = 0,
        string attackId = "",
        int damage = 0,
        int posture = 0,
        bool killed = false,
        IssenKind issen = IssenKind.None)
        => new()
        {
            Frame = frame,
            Verdict = verdict,
            AttackerId = attacker,
            DefenderId = defender,
            AttackId = attackId,
            Damage = damage,
            PostureDamage = posture,
            Killed = killed,
            IssenKind = issen,
        };

    [Fact]
    public void Empty_Stats_Report_Zero_Rate_Not_NaN()
    {
        var stats = new BattleStats();

        Assert.Equal(0, stats.Records);
        Assert.Equal(0d, stats.DeflectSuccessRate);          // 不许出现 0/0 = NaN
        Assert.Equal(0, stats.DefendableRecords);
        Assert.Equal(0, stats.SpanFrames);
        Assert.Contains("还没有任何结算", stats.FormatReport(0));
    }

    [Fact]
    public void Without_A_Player_Id_Every_Defensive_Resolve_Counts()
    {
        var stats = new BattleStats();

        stats.Record(Hit(10, Verdict.Deflect, attacker: 1, defender: 0, posture: 18));
        stats.Record(Hit(20, Verdict.Block, attacker: 1, defender: 0, posture: 9));
        stats.Record(Hit(30, Verdict.Hit, attacker: 1, defender: 0, damage: 12));
        stats.Record(Hit(40, Verdict.GuardBreak, attacker: 1, defender: 0));
        stats.Record(Hit(50, Verdict.Miss, attacker: 1, defender: 0));    // 闪避无敌 → 不进分母
        stats.Record(Hit(60, Verdict.Hit, attacker: 0, defender: 9, damage: 20));

        Assert.Equal(1, stats.PlayerDeflects);
        Assert.Equal(5, stats.DefendableRecords);   // 认不出玩家时不过滤，所以 6 条里 5 条进分母
        Assert.Equal(0.2d, stats.DeflectSuccessRate, 3);
        Assert.Equal(6, stats.Records);
    }

    [Fact]
    public void Player_Scoped_Rate_Ignores_Resolves_Where_The_Player_Attacks()
    {
        var stats = new BattleStats { PlayerActorId = 0 };

        stats.Record(Hit(10, Verdict.Deflect, attacker: 1, defender: 0, posture: 18));
        stats.Record(Hit(20, Verdict.Block, attacker: 1, defender: 0, posture: 9));
        stats.Record(Hit(30, Verdict.Hit, attacker: 1, defender: 0, damage: 12));
        stats.Record(Hit(40, Verdict.GuardBreak, attacker: 1, defender: 0));
        stats.Record(Hit(50, Verdict.Miss, attacker: 1, defender: 0));            // 不进分母
        stats.Record(Hit(60, Verdict.Hit, attacker: 0, defender: 9, damage: 20)); // 玩家打小怪 → 不进分母

        Assert.Equal(1, stats.PlayerDeflects);
        Assert.Equal(4, stats.DefendableRecords);
        Assert.Equal(0.25d, stats.DeflectSuccessRate, 3);
    }

    [Fact]
    public void Rate_Is_Scoped_To_Player_When_Actor_Id_Known()
    {
        var stats = new BattleStats { PlayerActorId = 0 };

        stats.Record(Hit(10, Verdict.Deflect, attacker: 7, defender: 0));
        stats.Record(Hit(11, Verdict.Hit, attacker: 7, defender: 3, damage: 5));   // 别的单位挨打

        Assert.Equal(1, stats.PlayerDeflects);
        Assert.Equal(1, stats.DefendableRecords);
        Assert.Equal(1d, stats.DeflectSuccessRate, 3);
        Assert.Equal(1, stats.HitsTaken);   // 全局计数不受 PlayerActorId 影响
    }

    [Fact]
    public void Recent_History_Keeps_Latest_Ten_In_Order()
    {
        var stats = new BattleStats();
        for (int frame = 1; frame <= 12; frame++)
            stats.Record(Hit(frame, Verdict.Hit, damage: frame));

        Assert.Equal(BattleStats.RecentCapacity, stats.RecentCount);
        Assert.Equal(12, stats.RecentAt(0).Frame);   // 最新
        Assert.Equal(3, stats.RecentAt(9).Frame);    // 第 10 新
        Assert.Throws<ArgumentOutOfRangeException>(() => stats.RecentAt(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => stats.RecentAt(-1));
    }

    [Fact]
    public void Span_Uses_First_And_Last_Frame_Regardless_Of_Order()
    {
        var stats = new BattleStats();

        stats.Record(Hit(100, Verdict.Hit));
        stats.Record(Hit(40, Verdict.Deflect));
        stats.Record(Hit(70, Verdict.Clash));

        Assert.Equal(40, stats.FirstFrame);
        Assert.Equal(100, stats.LastFrame);
        Assert.Equal(60, stats.SpanFrames);
        Assert.Equal(1.0d, stats.SpanSeconds, 3);
    }

    [Fact]
    public void Report_Lists_Every_Balance_Metric_And_Attack_Usage()
    {
        var stats = new BattleStats();
        stats.Record(Hit(10, Verdict.Hit, attackId: "light_01", damage: 12, posture: 8));
        stats.Record(Hit(20, Verdict.Hit, attackId: "light_01", damage: 12, posture: 8));
        stats.Record(Hit(30, Verdict.Issen, attackId: "light_03", issen: IssenKind.Shin, killed: true));
        stats.Record(Hit(40, Verdict.GuardBreak));
        stats.RecordDeath(3, 44, 1.5f, 0f, -2.25f);

        string report = stats.FormatReport(60);

        Assert.Contains("弹开成功率", report);
        Assert.Contains("交战跨度", report);
        Assert.Contains("一闪次数      1", report);
        Assert.Contains("格挡→破防     1", report);
        Assert.Contains("light_01 ×2", report);
        Assert.Contains("f44 actor#3 @ (1.5, 0.0, -2.2)", report);
        Assert.Contains("ISSEN(Shin)", report);
        Assert.Contains("KILL", report);
    }

    [Fact]
    public void DescribeVerdict_Is_Stable_For_Console_Output()
    {
        string line = BattleStats.DescribeVerdict(Hit(1204, Verdict.Deflect, attacker: 3, defender: 0, posture: 18));

        Assert.Equal("f1204  DEFLECT  a#3→d#0  pst +18", line);
    }

    [Fact]
    public void Reset_Clears_Everything()
    {
        var stats = new BattleStats();
        stats.Record(Hit(10, Verdict.Deflect));
        stats.RecordDeath(1, 10, 0f, 0f, 0f);

        stats.Reset();

        Assert.Equal(0, stats.Records);
        Assert.Equal(0, stats.RecentCount);
        Assert.Equal(0, stats.DefendableRecords);
        Assert.Equal(0, stats.PlayerDeflects);
        Assert.Equal(0, stats.Deaths);
        Assert.Equal(-1, stats.FirstFrame);
        Assert.Equal(0, stats.DeflectSuccessRate);
    }
}
