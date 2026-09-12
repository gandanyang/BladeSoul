using Oniblade.Combat;
using Oniblade.Combat.Data;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T6 的两条硬规则，都在这里钉死（不需要引擎，纯逻辑）：
///
/// 1. **取消硬直期内不弹开，但仍然格挡** —— 防防御键连打，且"惩罚只惩罚效率，不惩罚存活"。
/// 2. **窗口内（且只在窗口内）才弹开** —— 包括"按住防御不会让窗口一直开着"。
///
/// 判定结果不在这里重新实现：全部走冻结的 <see cref="CombatResolver"/>，
/// 所以这些测试同时也在验证"状态层的时序"和"裁决器的规则"能对上。
/// </summary>
public class GuardStateTests
{
    /// <summary>
    /// 复刻 <c>GuardState</c> + <c>CombatActor.TickTimers</c> 的时序（纯逻辑版）。
    /// 一帧的顺序：基类先递减窗口（<c>TickTimers</c>）→ 状态按本帧判定该不该开窗 → 帧号 +1。
    /// **进入防御的那一帧也要调 <see cref="Step"/>**：Enter 与同一帧的 Tick 都在这一帧里发生。
    /// </summary>
    private sealed class GuardSimulator
    {
        private readonly GuardWindowState _guard = new();
        private int _windowFramesLeft;

        public int WindowOpenCount { get; private set; }

        /// <summary>窗口还剩几帧 —— 对应 <c>CombatActor.DeflectWindowFramesLeft</c>。</summary>
        public int WindowFramesLeft => _windowFramesLeft;

        /// <summary>对应裁决器读的 <c>DefenderSnapshot.InDeflectWindow</c>。</summary>
        public bool InDeflectWindow => _windowFramesLeft > 0;

        /// <summary>对应 <c>IsGuarding</c>：整个防御期间恒为真（包括取消硬直期）。</summary>
        public bool IsGuarding { get; private set; }

        /// <summary>本帧的快照 —— 对应 <c>GuardState.InCancelLock</c>。</summary>
        public bool InCancelLock { get; private set; }

        /// <summary>进入防御（GuardState.Enter）。</summary>
        public void EnterGuard(GuardEntrySource source, int lockFrames)
        {
            IsGuarding = true;
            _guard.Begin(source, lockFrames);
        }

        /// <summary>过一帧（含进入那一帧）。</summary>
        public void Step(int windowFrames)
        {
            if (_windowFramesLeft > 0)
                _windowFramesLeft--;

            InCancelLock = _guard.IsInCancelLock;

            if (_guard.OpensWindowThisFrame)
            {
                _windowFramesLeft = windowFrames;
                WindowOpenCount++;
            }

            _guard.Advance();
        }

        public void ReleaseGuard() => IsGuarding = false;
    }

    private static AttackerSnapshot GruntSlash() => new()
    {
        ActorId = 101,
        Traits = new AttackTraits { Damage = 12, PostureDamage = 20, Parryable = true },
        IsActive = true,
        IsAttackAction = true,
        IssenVulnerable = true,
    };

    private static DefenderSnapshot DefenderOf(GuardSimulator guard) => new()
    {
        ActorId = 1,
        IsInvulnerable = false,
        IsActive = false,
        InDeflectWindow = guard.InDeflectWindow,
        IsGuarding = guard.IsGuarding,
        GuardAngleDeg = 0,
        CurrentPosture = 0,
        MaxPosture = 100,
        IssenKind = IssenKind.None,
    };

    private const int SamuraiWindow = 9;    // data/difficulty/samurai.tres 的默认档
    private const int SamuraiLock = 4;      // GuardCancelLockFrames

    // ── 规则 1：从站立进入，进入那一帧就能弹开 ──────────────────

    [Fact]
    public void Standing_Entry_Opens_The_Window_On_The_Entry_Frame()
    {
        var guard = new GuardSimulator();

        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);
        guard.Step(SamuraiWindow);

        Assert.True(guard.InDeflectWindow);
        Assert.False(guard.InCancelLock);

        // 同一帧挥来的杂兵横斩 = 弹开。
        Assert.Equal(Verdict.Deflect, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);
    }

    [Fact]
    public void Neutral_Entry_Ignores_Cancel_Lock_Frames()
    {
        // 从站立进入没有硬直：状态上写着 4 帧取消硬直也不许生效。
        // （GuardState 的 CancelLockFrames 会保留上一次写入的值，Begin 必须按来源把它抹掉。）
        var guard = new GuardSimulator();

        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);
        guard.Step(SamuraiWindow);

        Assert.Equal(1, guard.WindowOpenCount);
        Assert.False(guard.InCancelLock);
        Assert.True(guard.InDeflectWindow);
    }

    // ── 规则 2：取消进入 → 前 4 帧只格挡，第 5 帧才开窗 ──────────

    [Fact]
    public void Cancel_Entry_Blocks_Instead_Of_Deflecting_During_The_Lock()
    {
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Cancel, SamuraiLock);

        for (int frame = 0; frame < SamuraiLock; frame++)
        {
            guard.Step(SamuraiWindow);

            Assert.True(guard.InCancelLock);
            Assert.True(guard.IsGuarding);          // 仍然算格挡
            Assert.False(guard.InDeflectWindow);    // 但弹不开

            ResolveResult result = CombatResolver.Resolve(GruntSlash(), DefenderOf(guard));

            Assert.Equal(Verdict.Block, result.Verdict);
            Assert.Equal(0, result.Damage);         // 一格血都不掉：惩罚不落在存活上
        }
    }

    [Fact]
    public void Cancel_Entry_Opens_The_Window_Right_After_The_Lock()
    {
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Cancel, SamuraiLock);

        for (int frame = 0; frame < SamuraiLock; frame++)
            guard.Step(SamuraiWindow);

        // 硬直正好走满 4 帧，第 5 帧（0 起）窗口打开 → 这一刀弹得开。
        guard.Step(SamuraiWindow);

        Assert.False(guard.InCancelLock);
        Assert.True(guard.InDeflectWindow);
        Assert.Equal(Verdict.Deflect, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);
    }

    [Fact]
    public void Cancel_Lock_Length_Comes_From_The_Difficulty_Profile()
    {
        // 修罗档的 GuardCancelLockFrames 更短就应该更早开窗——这里用参数化的方式
        // 保证"硬直帧数是从难度档读进来的"，而不是代码里写死的 4。
        foreach (int lockFrames in new[] { 0, 2, 4, 6 })
        {
            var guard = new GuardSimulator();
            guard.EnterGuard(GuardEntrySource.Cancel, lockFrames);

            for (int frame = 0; frame < lockFrames; frame++)
            {
                guard.Step(SamuraiWindow);

                Assert.True(guard.InCancelLock);
                Assert.False(guard.InDeflectWindow);
            }

            guard.Step(SamuraiWindow);
            Assert.True(guard.InDeflectWindow);
        }
    }

    // ── 窗口寿命：按住防御不会让窗口一直开着 ────────────────────

    [Fact]
    public void Holding_Guard_Forever_Opens_The_Window_Exactly_Once()
    {
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);

        for (int frame = 0; frame < 120; frame++)
            guard.Step(SamuraiWindow);

        Assert.Equal(1, guard.WindowOpenCount);     // 按住不放 ≠ 一直能弹
        Assert.False(guard.InDeflectWindow);        // 窗口早就关了，仍然在格挡
        Assert.True(guard.IsGuarding);
    }

    [Theory]
    [InlineData(6)]    // 修罗
    [InlineData(9)]    // 武士
    [InlineData(12)]   // 剑客
    [InlineData(16)]   // 見習
    public void Window_Stays_Open_For_Exactly_The_Frames_CombatTuning_Says(int baseFrames)
    {
        // 窗口宽度只许从 CombatTuning 来（08 §3 P1-3 红线）——
        // 这里从难度档基础值一路走到"第几帧之后砍过来只能格挡"。
        int windowFrames = CombatTuning.ResolveDeflectWindowFrames(baseFrames);
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);

        for (int frame = 0; frame < windowFrames; frame++)
        {
            guard.Step(windowFrames);

            Assert.True(guard.InDeflectWindow);
            Assert.Equal(Verdict.Deflect, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);
        }

        // 第 windowFrames 帧（0 起）之后：窗口关闭，回到格挡。
        guard.Step(windowFrames);

        Assert.Equal(0, guard.WindowFramesLeft);
        Assert.Equal(Verdict.Block, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);
    }

    [Fact]
    public void Released_Guard_Stops_Blocking_But_The_Window_Is_A_Countdown()
    {
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);
        guard.Step(SamuraiWindow);
        guard.ReleaseGuard();      // 松开：IsGuarding 立刻失效（GuardState.Exit）

        // 弹开窗是"进入防御后 D 帧"的倒计时（02 §2.2），不绑在"是否还按着"上：
        // 松手不会让已经打开的窗口消失。它的作用只是把判定从"格挡"降级成"挨打"。
        Assert.Equal(Verdict.Deflect, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);

        for (int frame = 0; frame < SamuraiWindow; frame++)
            guard.Step(SamuraiWindow);

        // 窗口过期之后，没了格挡也没了弹开，才是实打实的命中。
        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(GruntSlash(), DefenderOf(guard)).Verdict);
    }

    [Fact]
    public void Guard_Does_Not_Stop_Unblockable_Hits()
    {
        // 「危」攻击格挡无效（02 §3）：防御状态不许把它变成 Block。
        var guard = new GuardSimulator();
        guard.EnterGuard(GuardEntrySource.Neutral, SamuraiLock);
        guard.Step(SamuraiWindow);

        var perilous = new AttackerSnapshot
        {
            ActorId = 101,
            Traits = AttackTraits.Perilous,
            IsActive = true,
            IsAttackAction = true,
        };

        Assert.Equal(Verdict.Hit, CombatResolver.Resolve(perilous, DefenderOf(guard)).Verdict);
    }
}
