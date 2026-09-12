using Oniblade.Combat;
using Xunit;

namespace Oniblade.Tests;

/// <summary>
/// T18 的三段边界（纯逻辑，不需要引擎）。
///
/// 这个机制的全部设计意图压在**一条边界**上："起手段可以被打断、饮用段不能"。
/// 这条线错了，喝血就退化成另一个东西——
/// 要么"随便被断"（只狼式的背板惩罚），要么"喝血无敌"（受伤不打断也不掉血）。
/// 这两种退化在试玩里都只是"感觉怪怪的"，很难靠手感定位，所以必须钉在单测里。
///
/// 帧数用的是 <c>data/actors/player_stats.tres</c> 里的真实值。
/// </summary>
public class HealStateTests
{
    // 与 data/actors/player_stats.tres 一致。
    private const int Startup = 10;
    private const int Drink = 34;
    private const int Recovery = 10;

    /// <summary>
    /// 复刻 <c>HealState</c> + <c>HealWindow</c> 的时序（纯逻辑版）。
    /// 一帧的顺序：Enter（Begin）→ 受击时询问 CanTransitionTo → Tick（Advance）。
    /// 所以"按下喝血那一帧"读到的 <see cref="Frame"/> 是 0，且属于起手段。
    /// </summary>
    private sealed class HealSimulator
    {
        private readonly HealWindow _window = new();

        public int Frame => _window.Frame;
        public HealPhase Phase => _window.Phase;
        public bool CanBeInterruptedNow => _window.CanBeInterruptedNow;
        public bool IsFinished => _window.IsFinished;
        public int TotalFrames => _window.TotalFrames;
        public bool EnteredDrinkThisFrame => _window.EnteredDrinkThisFrame;
        public bool EnteredRecoveryThisFrame => _window.EnteredRecoveryThisFrame;

        /// <summary>按下喝血那一帧。</summary>
        public void Enter(int startup = Startup, int drink = Drink, int recovery = Recovery) =>
            _window.Begin(startup, drink, recovery);

        /// <summary>过一帧。</summary>
        public void Step() => _window.Advance();
    }

    /// <summary>
    /// 复刻 <c>HealState.CanTransitionTo</c> 的那一行判断。
    /// 只有一个条件，所以这里不会和生产实现漂移（多一个条件就该回来改这里）。
    /// </summary>
    private static bool AcceptsTransition(HealSimulator heal, bool nextIsStagger) =>
        !nextIsStagger || heal.CanBeInterruptedNow;

    // ── 三段边界 ────────────────────────────────────────────────

    [Fact]
    public void Startup_Segment_Is_Interruptible()
    {
        var heal = new HealSimulator();
        heal.Enter();

        for (int frame = 0; frame < Startup; frame++)
        {
            Assert.Equal(HealPhase.Startup, heal.Phase);
            Assert.True(heal.CanBeInterruptedNow);

            // 挨打 → 允许进硬直（也就是"被打断"）。
            Assert.True(AcceptsTransition(heal, nextIsStagger: true));

            heal.Step();
        }
    }

    [Fact]
    public void Drink_Segment_Is_Not_Interruptible()
    {
        var heal = new HealSimulator();
        heal.Enter();

        for (int frame = 0; frame < Startup; frame++)
            heal.Step();

        for (int frame = 0; frame < Drink; frame++)
        {
            Assert.Equal(HealPhase.Drink, heal.Phase);
            Assert.False(heal.CanBeInterruptedNow);

            // 挨打 → **拒绝**进硬直。伤害不在这里发生（它在 CombatActor.ReceiveVerdict
            // 里、状态切换请求之前就已经结算完了），所以这是"不打断"，不是"免疫"。
            Assert.False(AcceptsTransition(heal, nextIsStagger: true));

            heal.Step();
        }
    }

    [Fact]
    public void Recovery_Segment_Is_Not_Interruptible_By_Hits()
    {
        var heal = new HealSimulator();
        heal.Enter();

        for (int frame = 0; frame < Startup + Drink; frame++)
            heal.Step();

        for (int frame = 0; frame < Recovery; frame++)
        {
            Assert.Equal(HealPhase.Recovery, heal.Phase);
            Assert.False(AcceptsTransition(heal, nextIsStagger: true));

            heal.Step();
        }
    }

    [Fact]
    public void Only_The_Startup_Segment_Can_Be_Interrupted()
    {
        // 把整段逐帧走一遍，统计"可被打断"的帧数：必须正好等于起手段长度。
        var heal = new HealSimulator();
        heal.Enter();

        int interruptible = 0;
        for (int frame = 0; frame < heal.TotalFrames; frame++)
        {
            if (AcceptsTransition(heal, nextIsStagger: true))
                interruptible++;

            heal.Step();
        }

        Assert.Equal(Startup, interruptible);
    }

    [Fact]
    public void Non_Stagger_Transitions_Are_Always_Accepted()
    {
        // 受击以外的切换（比如结束回 Idle）不受这条规则影响，否则喝血会卡死。
        var heal = new HealSimulator();
        heal.Enter();

        for (int frame = 0; frame < heal.TotalFrames; frame++)
        {
            Assert.True(AcceptsTransition(heal, nextIsStagger: false));
            heal.Step();
        }
    }

    // ── 总长与两个"跨段帧" ──────────────────────────────────────

    [Fact]
    public void Total_Frames_Are_Startup_Plus_Drink_Plus_Recovery()
    {
        var heal = new HealSimulator();
        heal.Enter();

        Assert.Equal(54, heal.TotalFrames);

        for (int frame = 0; frame < heal.TotalFrames; frame++)
        {
            Assert.False(heal.IsFinished);
            heal.Step();
        }

        Assert.True(heal.IsFinished);
    }

    [Fact]
    public void Entered_Drink_Happens_Exactly_Once_At_The_Startup_Boundary()
    {
        // 扣次数就发生在这一帧。它早了 → 起手被打断也扣次数（违反 02 §2.4）；
        // 它晚了 → 饮用段挨打时会重复扣。
        var heal = new HealSimulator();
        heal.Enter();

        int drinkEntries = 0;
        int drinkEntryFrame = -1;

        for (int frame = 0; frame < heal.TotalFrames; frame++)
        {
            if (heal.EnteredDrinkThisFrame)
            {
                drinkEntries++;
                drinkEntryFrame = frame;
            }

            heal.Step();
        }

        Assert.Equal(1, drinkEntries);
        Assert.Equal(Startup, drinkEntryFrame);
    }

    [Fact]
    public void Entered_Recovery_Happens_Exactly_Once_At_The_Drink_Boundary()
    {
        // 回血生效就在这一帧（"喝完"）。
        var heal = new HealSimulator();
        heal.Enter();

        int recoveryEntries = 0;
        int recoveryEntryFrame = -1;

        for (int frame = 0; frame < heal.TotalFrames; frame++)
        {
            if (heal.EnteredRecoveryThisFrame)
            {
                recoveryEntries++;
                recoveryEntryFrame = frame;
            }

            heal.Step();
        }

        Assert.Equal(1, recoveryEntries);
        Assert.Equal(Startup + Drink, recoveryEntryFrame);
    }

    [Fact]
    public void Zero_Length_Segments_Do_Not_Break_The_Boundaries()
    {
        // 缺数据时输入侧会写 0；退化行为必须是"三段都空、立刻结束"，不许卡死。
        var heal = new HealSimulator();
        heal.Enter(0, 0, 0);

        Assert.True(heal.IsFinished);
        Assert.Equal(0, heal.TotalFrames);
        Assert.False(AcceptsTransition(heal, nextIsStagger: true));
    }
}
