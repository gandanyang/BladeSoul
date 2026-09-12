using Oniblade.Combat;
using Oniblade.Combat.Data;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T13 的闪避无敌帧边界（纯逻辑，不需要引擎）。
///
/// 判定结果不在这里重新实现：全部走冻结的 <see cref="CombatResolver"/>，
/// 所以这些测试同时验证"状态层的帧号"和"裁决器规则 1"能对上——
/// 单看任一边都发现不了"无敌帧差一帧"这种错。
///
/// 帧数用 <c>data/difficulty/*.tres</c> 里的**真实值**。
///
/// T21 之后这里只有**一个**窗口概念：无敌帧本身。
/// 原先那个"完美闪避宽容帧数"已删除——无敌帧之后不再产生 <c>Miss</c>，
/// 而完美闪避只能由 <c>Miss</c> 触发，所以那个参数永远不可能生效（02 §8 的裁定）。
/// 下面的 <see cref="Invulnerability_Ends_Exactly_At_DodgeIFrames"/> 把这条钉住了。
/// </summary>
public class DodgeStateTests
{
    // ── 与 data/difficulty/*.tres 一致 ──────────────────────────
    private const int AsuraIFrames = 7, AsuraRecovery = 20;
    private const int SamuraiIFrames = 8, SamuraiRecovery = 18;
    private const int KenshiIFrames = 10, KenshiRecovery = 15;
    private const int MigotoIFrames = 14, MigotoRecovery = 12;

    /// <summary>
    /// 复刻 <c>DodgeState</c> + <c>DodgeWindow</c> 的时序（纯逻辑版）。
    /// 一帧的顺序：Enter（Begin）→ 仲裁器读快照 → Tick（Advance）。
    /// 所以"按下闪避那一帧"读到的 <see cref="FramesSinceStart"/> 是 0。
    /// </summary>
    private sealed class DodgeSimulator
    {
        private readonly DodgeWindow _window = new();

        public int FramesSinceStart => _window.FramesSinceStart;
        public bool IsInvulnerable => _window.IsInvulnerable;
        public bool IsInRecovery => _window.IsInRecovery;
        public bool IsFinished => _window.IsFinished;
        public int TotalFrames => _window.TotalFrames;

        /// <summary>玩家按下闪避那一帧（对应 DodgeState.Enter → DodgeWindow.Begin）。</summary>
        public void Enter(int iFrames, int recovery) => _window.Begin(iFrames, recovery);

        /// <summary>过一帧（对应 DodgeState.Tick → DodgeWindow.Advance）。</summary>
        public void Step() => _window.Advance();
    }

    private static AttackerSnapshot GruntSlash() => new()
    {
        ActorId = 101,
        Traits = new AttackTraits { Damage = 12, PostureDamage = 20, Parryable = true },
        IsActive = true,
        IsAttackAction = true,
        IssenVulnerable = true,
    };

    private static DefenderSnapshot DefenderOf(DodgeSimulator dodge, IssenKind issen = IssenKind.None) => new()
    {
        ActorId = 1,
        IsInvulnerable = dodge.IsInvulnerable,
        IsActive = false,
        InDeflectWindow = false,
        // 闪避期间不格挡：躲开就是"我一个人扛下来了"，不能靠同时按着防御兜底。
        IsGuarding = false,
        GuardAngleDeg = 0,
        CurrentPosture = 0,
        MaxPosture = 100,
        IssenKind = issen,
    };

    // ── 规则 2：无敌帧数严格等于难度档给的值 ────────────────────

    [Theory]
    [InlineData(AsuraIFrames, AsuraRecovery)]
    [InlineData(SamuraiIFrames, SamuraiRecovery)]
    [InlineData(KenshiIFrames, KenshiRecovery)]
    [InlineData(MigotoIFrames, MigotoRecovery)]
    public void Invulnerable_For_Exactly_The_Profiles_DodgeIFrames(int iFrames, int recovery)
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(iFrames, recovery);

        // 第 0 帧（按下闪避那一帧）就已经无敌——刀落下的同一帧按闪避必须算躲开。
        for (int frame = 0; frame < iFrames; frame++)
        {
            Assert.Equal(frame, dodge.FramesSinceStart);
            Assert.True(dodge.IsInvulnerable);
            Assert.Equal(Verdict.Miss, CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge)).Verdict);
            dodge.Step();
        }

        // 第 iFrames 帧（0 起）之后：无敌结束，正常结算 → 实打实挨这一刀。
        Assert.False(dodge.IsInvulnerable);

        ResolveResult result = CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge));

        Assert.Equal(Verdict.Hit, result.Verdict);
        Assert.Equal(12, result.Damage);
    }

    [Fact]
    public void Invulnerable_Wins_Over_An_Incoming_Issen()
    {
        // 02 §4：规则 1（无敌）排在规则 2（一闪）之前——闪避连一闪都躲得掉。
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiRecovery);

        Assert.Equal(
            Verdict.Miss,
            CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge, IssenKind.Shin)).Verdict);
    }

    // ── T21：无敌帧就是完美闪避窗，没有第二个窗口 ───────────────

    [Fact]
    public void Invulnerability_Ends_Exactly_At_DodgeIFrames()
    {
        // 这条取代了原来的 `Grace_Frames_Do_Not_Extend_Invulnerability`。
        //
        // T21 的裁定：无敌帧之后**不再产生 Miss**，所以"完美闪避"的判定窗
        // 不可能比无敌帧更宽——多出来的那几帧没有任何事件可接，是死配置。
        // 这里把"严格等于 DodgeIFrames"钉死：多一帧都会让这条测试红。
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiRecovery);

        for (int frame = 0; frame < SamuraiIFrames; frame++)
        {
            Assert.True(dodge.IsInvulnerable);
            dodge.Step();
        }

        // 第 DodgeIFrames 帧：刚好出界，一刀不少地挨上。
        Assert.False(dodge.IsInvulnerable);
        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge)).Verdict);
    }

    // ── 整段时长与后摇边界 ─────────────────────────────────────

    [Fact]
    public void Total_Frames_Is_Invulnerable_Plus_Recovery()
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiRecovery);

        Assert.Equal(SamuraiIFrames + SamuraiRecovery, dodge.TotalFrames);

        for (int frame = 0; frame < SamuraiIFrames + SamuraiRecovery; frame++)
        {
            Assert.False(dodge.IsFinished);
            dodge.Step();
        }

        Assert.True(dodge.IsFinished);
    }

    [Fact]
    public void Recovery_Starts_Right_After_The_Last_Invulnerable_Frame()
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiRecovery);

        for (int frame = 0; frame < SamuraiIFrames - 1; frame++)
        {
            Assert.False(dodge.IsInRecovery);
            dodge.Step();
        }

        // 最后一个无敌帧还不算后摇。
        Assert.True(dodge.IsInvulnerable);
        Assert.False(dodge.IsInRecovery);

        dodge.Step();

        Assert.True(dodge.IsInRecovery);
    }

    [Fact]
    public void Zero_DodgeIFrames_Degrades_To_No_Invulnerability()
    {
        // 缺难度档时输入侧会写 0 → 不该凭空变强
        // （与 GuardState 退化到"最窄的可玩窗口"同一个立场）。
        var dodge = new DodgeSimulator();
        dodge.Enter(0, SamuraiRecovery);

        Assert.False(dodge.IsInvulnerable);
        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge)).Verdict);
    }
}
