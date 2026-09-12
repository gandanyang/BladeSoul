using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T20 的真一闪窗口边界（纯逻辑，不需要引擎）。
///
/// 这里钉的是**一条比一闪本身更重要的边界**：安全窗。
/// 它让"按早了"的后果从"被砍死"降级成"这一下白按，但仍然防住了"。
/// 这条线错了，防劝退设计就塌了，而它在试玩里的表现只是"怎么我老挨打"，极难定位。
///
/// 另一条同样容易写错的是**收招段不算尝试**：
/// 02 §10 要求敌人连段结束后留 ≥20 帧空隙给玩家反打，
/// 若在那个空隙里按攻击也被判成"落空吃 30 帧"，等于系统惩罚设计上要奖励的行为。
///
/// 帧数用 <c>data/difficulty/*.tres</c> 里的**真实值**。
/// </summary>
public class IssenWindowTests
{
    // 与 data/difficulty/*.tres 一致（IssenWindowFrames / IssenSafeWindowFrames）。
    private const int AsuraWindow = 6, AsuraSafe = 4;
    private const int SamuraiWindow = 6, SamuraiSafe = 6;
    private const int KenshiWindow = 9, KenshiSafe = 8;
    private const int MigotoWindow = 12, MigotoSafe = 12;

    // ── 规则 1：前摇内、距命中 ≤ N 帧 → 真一闪 ──────────────────

    [Theory]
    [InlineData(AsuraWindow, AsuraSafe)]
    [InlineData(SamuraiWindow, SamuraiSafe)]
    [InlineData(KenshiWindow, KenshiSafe)]
    [InlineData(MigotoWindow, MigotoSafe)]
    public void Windup_Within_The_Window_Is_A_True_Issen(int window, int safe)
    {
        // 0 帧 = 这一帧就命中（最急的一档），N 帧 = 窗口的最后一档，两端都要成立。
        for (int framesUntilActive = 0; framesUntilActive <= window; framesUntilActive++)
        {
            Assert.Equal(
                IssenIntent.Issen,
                IssenWindow.Evaluate(ThreatPhase.Windup, framesUntilActive, window, safe));
        }
    }

    // ── 规则 3（★ 防劝退核心）：窗口之后、安全窗之内 → 转格挡，不挨打 ──

    [Theory]
    [InlineData(AsuraWindow, AsuraSafe)]
    [InlineData(SamuraiWindow, SamuraiSafe)]
    [InlineData(KenshiWindow, KenshiSafe)]
    [InlineData(MigotoWindow, MigotoSafe)]
    public void Windup_Inside_The_Safe_Window_Becomes_A_Guard_Not_A_Punish(int window, int safe)
    {
        Assert.True(safe > 0, "安全窗为 0 的难度档等于取消了这条防劝退设计");

        for (int framesUntilActive = window + 1; framesUntilActive <= window + safe; framesUntilActive++)
        {
            Assert.Equal(
                IssenIntent.SafeGuard,
                IssenWindow.Evaluate(ThreatPhase.Windup, framesUntilActive, window, safe));
        }
    }

    [Fact]
    public void The_Boundary_Between_Issen_And_SafeGuard_Is_Exact()
    {
        // 差一帧就会让"按早了"变成"挨打"，所以这一条单独钉。
        Assert.Equal(IssenIntent.Issen,
            IssenWindow.Evaluate(ThreatPhase.Windup, SamuraiWindow, SamuraiWindow, SamuraiSafe));

        Assert.Equal(IssenIntent.SafeGuard,
            IssenWindow.Evaluate(ThreatPhase.Windup, SamuraiWindow + 1, SamuraiWindow, SamuraiSafe));
    }

    // ── 太早 → 落空 ─────────────────────────────────────────────

    [Theory]
    [InlineData(AsuraWindow, AsuraSafe)]
    [InlineData(SamuraiWindow, SamuraiSafe)]
    [InlineData(KenshiWindow, KenshiSafe)]
    [InlineData(MigotoWindow, MigotoSafe)]
    public void Windup_Before_The_Safe_Window_Is_TooEarly(int window, int safe)
    {
        int firstTooEarly = window + safe + 1;

        Assert.Equal(IssenIntent.TooEarly,
            IssenWindow.Evaluate(ThreatPhase.Windup, firstTooEarly, window, safe));

        Assert.Equal(IssenIntent.TooEarly,
            IssenWindow.Evaluate(ThreatPhase.Windup, 999, window, safe));
    }

    // ── 太晚 / 收招 / 无威胁 ────────────────────────────────────

    [Fact]
    public void Active_Frames_Are_TooLate()
    {
        // 刀已经在身上了，一闪来不及——但代价只是 30 帧，不是即死。
        Assert.Equal(IssenIntent.TooLate,
            IssenWindow.Evaluate(ThreatPhase.Active, 0, SamuraiWindow, SamuraiSafe));

        Assert.Equal(IssenIntent.TooLate,
            IssenWindow.Evaluate(ThreatPhase.Active, -3, SamuraiWindow, SamuraiSafe));
    }

    [Fact]
    public void Recovery_Is_Not_An_Attempt()
    {
        // ★ 02 §10：敌人连段结束后有 ≥20 帧空隙给玩家反打。
        // 那个空隙里按攻击必须是普通攻击，不能被判成"落空吃 30 帧"。
        Assert.Equal(IssenIntent.NotAnAttempt,
            IssenWindow.Evaluate(ThreatPhase.Recovery, -8, SamuraiWindow, SamuraiSafe));
    }

    [Fact]
    public void No_Threat_Is_Not_An_Attempt()
    {
        Assert.Equal(IssenIntent.NotAnAttempt,
            IssenWindow.Evaluate(ThreatPhase.None, 0, SamuraiWindow, SamuraiSafe));
    }

    // ── buff 时长必须真的盖住那一刀 ─────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(SamuraiWindow)]
    [InlineData(MigotoWindow)]
    public void Buff_Is_Still_Alive_On_The_Frame_The_Hit_Lands(int framesUntilActive)
    {
        // 复刻 CombatActor 的时序：授予发生在 PollLocalInput，
        // 而同一帧的 TickTimers 紧接着就会扣掉 1（它排在 PollLocalInput 之后），
        // 之后每帧再扣 1。所以到敌人判定帧（F + framesUntilActive）时剩余
        //   n - 1 - framesUntilActive
        // 必须 ≥ 1，否则真一闪会在最后关头漏掉这一刀。
        int granted = IssenWindow.BuffFramesFor(framesUntilActive);
        int leftOnImpactFrame = granted - 1 - framesUntilActive;

        Assert.True(leftOnImpactFrame >= 1,
            $"距命中 {framesUntilActive} 帧时授予 {granted} 帧 buff，到命中那一帧只剩 {leftOnImpactFrame} 帧：会漏刀");
    }

    [Fact]
    public void Zero_Frame_Window_Degrades_But_Keeps_The_Safe_Window()
    {
        // 缺难度档时输入侧会写 0：一闪窗退化到"只有命中当帧"，
        // 但安全窗仍然要挡住第一帧之后的那一段（不能退化成"必然挨打"）。
        Assert.Equal(IssenIntent.Issen,
            IssenWindow.Evaluate(ThreatPhase.Windup, 0, 0, SamuraiSafe));

        Assert.Equal(IssenIntent.SafeGuard,
            IssenWindow.Evaluate(ThreatPhase.Windup, 1, 0, SamuraiSafe));
    }
}
