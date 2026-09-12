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
/// 四档难度用的是 <c>data/difficulty/*.tres</c> 里的**真实数值**，不是编出来的。
/// </summary>
public class DodgeStateTests
{
    // ── 与 data/difficulty/*.tres 一致 ──────────────────────────
    private const int AsuraIFrames = 7, AsuraRecovery = 20, AsuraGrace = 2;
    private const int SamuraiIFrames = 8, SamuraiRecovery = 18, SamuraiGrace = 3;
    private const int KenshiIFrames = 10, KenshiRecovery = 15, KenshiGrace = 4;
    private const int MigotoIFrames = 14, MigotoRecovery = 12, MigotoGrace = 6;

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
        public bool IsInPerfectDodgeWindow => _window.IsInPerfectDodgeWindow;
        public bool IsInRecovery => _window.IsInRecovery;
        public bool IsFinished => _window.IsFinished;
        public int TotalFrames => _window.TotalFrames;

        /// <summary>玩家按下闪避那一帧（对应 DodgeState.Enter → DodgeWindow.Begin）。</summary>
        public void Enter(int iFrames, int grace, int recovery) =>
            _window.Begin(iFrames, grace, recovery);

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
    [InlineData(AsuraIFrames, AsuraGrace, AsuraRecovery)]
    [InlineData(SamuraiIFrames, SamuraiGrace, SamuraiRecovery)]
    [InlineData(KenshiIFrames, KenshiGrace, KenshiRecovery)]
    [InlineData(MigotoIFrames, MigotoGrace, MigotoRecovery)]
    public void Invulnerable_For_Exactly_The_Profiles_DodgeIFrames(int iFrames, int grace, int recovery)
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(iFrames, grace, recovery);

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
        dodge.Enter(SamuraiIFrames, SamuraiGrace, SamuraiRecovery);

        Assert.Equal(
            Verdict.Miss,
            CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge, IssenKind.Shin)).Verdict);
    }

    // ── 规则 6：完美闪避宽容 ────────────────────────────────────

    [Fact]
    public void Perfect_Dodge_Window_Covers_The_Invulnerable_Frames_Plus_Grace()
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiGrace, SamuraiRecovery);

        for (int frame = 0; frame < SamuraiIFrames + SamuraiGrace; frame++)
        {
            Assert.True(dodge.IsInPerfectDodgeWindow);
            dodge.Step();
        }

        Assert.False(dodge.IsInPerfectDodgeWindow);
    }

    [Fact]
    public void Grace_Frames_Do_Not_Extend_Invulnerability()
    {
        // ⚠️ 规格歧义（已写进完成报告，等制作人裁定）：
        // 02 §8 说"无敌帧结束后 3 帧内仍算完美闪避"，
        // 但 T13 验收 #2 要求"第 DodgeIFrames 帧之后 → 正常结算"。
        // 两条不能同时成立：无敌帧之后不再产生 Miss，而完美闪避只能由 Miss 触发。
        //
        // 本实现按验收 #2 执行（无敌严格 = DodgeIFrames），
        // 因此宽容帧当前只放宽**判定窗**、不放宽**无敌**，是惰性的。
        // 这个测试把当前语义钉住，裁定后再改。
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiGrace, SamuraiRecovery);

        for (int frame = 0; frame < SamuraiIFrames; frame++)
            dodge.Step();

        Assert.False(dodge.IsInvulnerable);
        Assert.True(dodge.IsInPerfectDodgeWindow);
        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge)).Verdict);
    }

    // ── 整段时长与后摇边界 ─────────────────────────────────────

    [Fact]
    public void Total_Frames_Is_Invulnerable_Plus_Recovery()
    {
        var dodge = new DodgeSimulator();
        dodge.Enter(SamuraiIFrames, SamuraiGrace, SamuraiRecovery);

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
        dodge.Enter(SamuraiIFrames, SamuraiGrace, SamuraiRecovery);

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
        dodge.Enter(0, 0, SamuraiRecovery);

        Assert.False(dodge.IsInvulnerable);
        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(GruntSlash(), DefenderOf(dodge)).Verdict);
    }
}
