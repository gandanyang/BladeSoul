using System;
using System.Collections.Generic;
using System.Text;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;
using Oniblade.Utils;

namespace Oniblade.UI;

/// <summary>
/// 本场战斗的统计累加器。**纯逻辑，零 Godot 依赖**——可以脱离引擎单测
/// （04 文档 §14 第 1 层）。
///
/// 数据来源只有两个事件：
/// <code>
/// EventBus.HitResolved → Record(in HitEvent)
/// EventBus.ActorDied   → RecordDeath(...)
/// </code>
///
/// 口径照 02 文档 §11。F8 的输出是"平衡调整的唯一数据来源"，
/// 所以这里不掺任何猜测：没有样本时一律返回 0，不返回 NaN。
/// </summary>
public sealed class BattleStats
{
    /// <summary>面板与报告保留的最近结算条数（04 文档 §13 的 LAST 10 VERDICTS）。</summary>
    public const int RecentCapacity = 10;

    private readonly Dictionary<Verdict, int> _verdicts = new();
    private readonly Dictionary<string, int> _attackUsage = new(StringComparer.Ordinal);
    private readonly HitEvent[] _recent = new HitEvent[RecentCapacity];
    private readonly List<string> _deathLines = new();

    /// <summary>
    /// 玩家 ActorId。设了它就只统计"玩家当防御方"的结算；
    /// 留空（默认）统计全部结算——灰盒期只有玩家会挨打，两者等价。
    /// </summary>
    public int? PlayerActorId { get; set; }

    public int Records { get; private set; }

    /// <summary>首击 / 末击的逻辑帧号；没有结算时都是 -1。</summary>
    public int FirstFrame { get; private set; } = -1;
    public int LastFrame { get; private set; } = -1;

    private int RecentHead { get; set; }
    public int RecentCount { get; private set; }

    /// <summary>计入"弹开成功率"分母的条数：玩家的可防御结算（弹开/格挡/破防/被命中）。</summary>
    public int DefendableRecords { get; private set; }

    /// <summary>分子：其中弹开成功的条数。无样本时为 0。</summary>
    public int PlayerDeflects { get; private set; }

    public int Count(Verdict verdict) => _verdicts.TryGetValue(verdict, out int n) ? n : 0;

    public int Deflects => Count(Verdict.Deflect);
    public int Blocks => Count(Verdict.Block);
    public int GuardBreaks => Count(Verdict.GuardBreak);
    public int HitsTaken => Count(Verdict.Hit);
    public int Clashes => Count(Verdict.Clash);
    public int Issens => Count(Verdict.Issen);
    public int Deathblows => Count(Verdict.Deathblow);
    public int Misses => Count(Verdict.Miss);

    public int Deaths => _deathLines.Count;
    public IReadOnlyList<string> DeathLines => _deathLines;

    /// <summary>
    /// 弹开成功率。分母是玩家挨到的"可防御结算"，不是全部结算——
    /// 否则玩家自己打出去的命中会稀释这个数字。
    /// </summary>
    public double DeflectSuccessRate
        => DefendableRecords == 0 ? 0d : PlayerDeflects / (double)DefendableRecords;

    /// <summary>
    /// 首击到末击的跨度。**不是整场战斗时长**——它只能靠 HitEvent.Frame 推算，
    /// 敌人 AI 的巡逻/靠近时间不在里面。灰盒调参够用，正式 HUD 另算。
    /// </summary>
    public int SpanFrames => FirstFrame < 0 || LastFrame < FirstFrame ? 0 : LastFrame - FirstFrame;

    public double SpanSeconds => Frames.ToSeconds(SpanFrames);

    /// <summary>记录一次结算。逐帧热路径上调用，只做 O(1) 的事。</summary>
    public void Record(in HitEvent e)
    {
        Records++;
        _verdicts[e.Verdict] = Count(e.Verdict) + 1;

        if (!string.IsNullOrEmpty(e.AttackId))
            _attackUsage[e.AttackId] = _attackUsage.TryGetValue(e.AttackId, out int used) ? used + 1 : 1;

        if (FirstFrame < 0 || e.Frame < FirstFrame)
            FirstFrame = e.Frame;
        if (e.Frame > LastFrame)
            LastFrame = e.Frame;

        bool defenderIsPlayer = PlayerActorId is null || PlayerActorId.Value == e.DefenderId;
        if (defenderIsPlayer)
        {
            switch (e.Verdict)
            {
                case Verdict.Deflect:
                    PlayerDeflects++;
                    DefendableRecords++;
                    break;
                case Verdict.Block:
                case Verdict.GuardBreak:
                case Verdict.Hit:
                    DefendableRecords++;
                    break;
            }
        }

        _recent[RecentHead] = e;
        RecentHead = (RecentHead + 1) % RecentCapacity;
        if (RecentCount < RecentCapacity)
            RecentCount++;
    }

    /// <summary>记录一次死亡位置（02 §11 的"死亡位置分布"）。</summary>
    public void RecordDeath(int actorId, int frame, float x, float y, float z)
        => _deathLines.Add($"f{frame} actor#{actorId} @ ({x:F1}, {y:F1}, {z:F1})");

    /// <summary>取最近第 n 条结算（0 = 最新）。越界抛异常，调用方先看 <see cref="RecentCount"/>。</summary>
    public HitEvent RecentAt(int indexFromLatest)
    {
        if (indexFromLatest < 0 || indexFromLatest >= RecentCount)
            throw new ArgumentOutOfRangeException(nameof(indexFromLatest));

        int slot = (RecentHead - 1 - indexFromLatest + 2 * RecentCapacity) % RecentCapacity;
        return _recent[slot];
    }

    /// <summary>F7 重开时清空。</summary>
    public void Reset()
    {
        _verdicts.Clear();
        _attackUsage.Clear();
        _deathLines.Clear();
        Array.Clear(_recent, 0, _recent.Length);
        Records = 0;
        FirstFrame = -1;
        LastFrame = -1;
        RecentHead = 0;
        RecentCount = 0;
        DefendableRecords = 0;
        PlayerDeflects = 0;
    }

    /// <summary>一条结算的单行描述，面板与 F8 报告共用。</summary>
    public static string DescribeVerdict(in HitEvent e)
    {
        var sb = new StringBuilder(48);
        sb.Append('f').Append(e.Frame).Append("  ").Append(e.Verdict.ToString().ToUpperInvariant());
        if (e.IssenKind != IssenKind.None)
            sb.Append('(').Append(e.IssenKind).Append(')');
        sb.Append("  a#").Append(e.AttackerId).Append("→d#").Append(e.DefenderId);
        if (e.Damage > 0)
            sb.Append("  dmg ").Append(e.Damage);
        if (e.PostureDamage != 0)
            sb.Append("  pst ").Append(e.PostureDamage > 0 ? "+" : "").Append(e.PostureDamage);
        if (e.Killed)
            sb.Append("  KILL");
        return sb.ToString();
    }

    /// <summary>F8 的 console 报告（纯文本，无 BBCode）。</summary>
    public string FormatReport(int nowFrame)
    {
        var sb = new StringBuilder(768);
        sb.AppendLine("═══ 战斗统计（F8，02 文档 §11 口径）═══");

        if (Records == 0)
        {
            sb.AppendLine("  本场还没有任何结算记录。");
            sb.Append("  （逻辑帧 ").Append(nowFrame).Append("）先砍到木桩，再按一次 F8。");
            return sb.ToString();
        }

        sb.Append("  结算次数      ").Append(Records).AppendLine();
        if (DefendableRecords == 0)
        {
            // 只打不防的一局里没有分母——别显示一个看着像"0% 弹开"的假数字。
            sb.AppendLine("  弹开成功率    —（本场还没有玩家的可防御结算）");
        }
        else
        {
            sb.Append("  弹开成功率    ").Append(DeflectSuccessRate.ToString("P1"))
              .Append("  (").Append(PlayerDeflects).Append('/').Append(DefendableRecords).Append(')')
              .AppendLine("      健康区：新手 10~25% / 熟练 40~60%");
        }
        sb.Append("  交战跨度      ").Append(SpanSeconds.ToString("F1")).Append("s  (")
          .Append(SpanFrames).AppendLine(" 帧)   健康区：杂兵 15~40s / BOSS 90~240s");
        sb.Append("  一闪次数      ").Append(Issens).AppendLine("       健康区：≥2");
        sb.Append("  格挡→破防     ").Append(GuardBreaks).AppendLine("       健康区：≤2");
        sb.Append("  死亡次数      ").Append(Deaths).AppendLine();

        sb.Append("  裁决分布      ");
        bool first = true;
        foreach (Verdict verdict in Enum.GetValues<Verdict>())
        {
            if (!first) sb.Append(" / ");
            first = false;
            sb.Append(verdict).Append(' ').Append(Count(verdict));
        }
        sb.AppendLine();

        sb.Append("  招式使用      ");
        if (_attackUsage.Count == 0)
        {
            sb.Append("—（HitEvent.AttackId 为空）");
        }
        else
        {
            var usage = new List<KeyValuePair<string, int>>(_attackUsage);
            usage.Sort(static (a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });
            for (int i = 0; i < usage.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(usage[i].Key).Append(" ×").Append(usage[i].Value);
            }
        }
        sb.AppendLine();

        sb.Append("  死亡位置      ");
        if (_deathLines.Count == 0)
        {
            sb.Append('—');
        }
        else
        {
            for (int i = 0; i < _deathLines.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(_deathLines[i]);
            }
        }
        sb.AppendLine();

        sb.AppendLine("  最近结算");
        for (int i = 0; i < RecentCount; i++)
            sb.Append("    ").Append(DescribeVerdict(RecentAt(i))).AppendLine();

        return sb.ToString();
    }
}
