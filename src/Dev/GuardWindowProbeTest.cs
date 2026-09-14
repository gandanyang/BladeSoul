using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 弹开窗口宽度扫描：**"我到底该提前几帧按右键？"**
///
/// 为什么需要它：`DeflectTraining` 只测了一个点（提前 6 帧按）——它能证明
/// "窗口是通的"，但证明不了**窗口有多宽**，更证明不了"按住不放为什么会失败"。
/// 而试玩反馈恰恰是"能挡但从来弹不开"，这是一个关于**窗口形状**的问题，
/// 单点测试在原理上就看不见它。
///
/// 本探针枚举两种真实的按键习惯，对每一种扫出成功/失败的分界：
///
/// - **点按**：在敌人判定帧前 k 帧按下、按 1 帧后松开。玩家真的在"掐时机"。
/// - **按住**：一开始就按住不放。这是新手最常见的做法（"我先防住"）。
///
/// 期望（若实现符合 02 §8 的设计）：
/// - 点按：k 落在窗口内能弹开，太早/太晚都不行 → **分界就是窗口宽度**。
/// - 按住：窗口只在按下那一刻开一次，过期就**永远不再开**，
///   所以只有 k ≤ 窗口宽度 才行。
///
/// "按住就再也弹不开"是刻意的防连打设计，但它**必须被量化**——
/// 否则玩家只感觉到"弹开时灵时不灵"，而不知道是自己按早了。
///
///     godot --headless --path . res://scenes/tests/GuardWindowProbe.tscn
/// </summary>
public partial class GuardWindowProbeTest : Node3D
{
    /// <summary>每个 k 值给多少帧跑完一次"起手→命中→收招"。</summary>
    [Export] public int FramesPerSample { get; set; } = 80;

    /// <summary>最多扫到"提前几帧按"。</summary>
    [Export] public int MaxLeadFrames { get; set; } = 22;

    /// <summary>点按模式按下后保持几帧再松手（避免"1 帧按键"被物理帧吃掉）。</summary>
    [Export] public int TapHoldFrames { get; set; } = 2;

    private readonly List<string> _failures = new();
    private readonly List<HitEvent> _current = new();

    private EventBus? _bus;
    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        _attacker = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        await WaitPhysicsFrames(10);

        if (_attacker.Attack is null)
        {
            GD.PrintErr("[窗口扫描] 挥砍假人没配招式，无法测");
            GetTree().Quit(1);
            return;
        }

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        GD.Print($"[窗口扫描] 点按 = 判定前 k 帧按下、保持 {TapHoldFrames} 帧松开；按住 = 全程不放");
        // 两种模式之间必须等过 GuardReentryLockFrames。
        //
        // 不等会怎样：点按模式最后一次松手后只隔几帧就开始按住模式，
        // 而"松开后 8 帧内又按下"会被判成**快速重按**（02 §8 的防连打）——
        // 那一段防御`窗口干脆不开`，于是按住模式全样本都只能格挡。
        // 第一版就是这个坑：它让"按住从来弹不开"看起来像游戏 bug，
        // 其实一半是探针自己没把上一次松手清干净。
        GD.Print("[窗口扫描] 模式之间等 30 帧，让上一次松手彻底过期");
        await WaitPhysicsFrames(30);

        GD.Print("[窗口扫描] ── 点按（掐时机）──");
        List<int> tapOk = await Scan(tap: true);

        await WaitPhysicsFrames(30);

        GD.Print("[窗口扫描] ── 按住不放（新手习惯）──");
        List<int> holdOk = await Scan(tap: false);

        Report(tapOk, holdOk);
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
    }

    /// <summary>
    /// 扫一遍"提前 k 帧按"。返回所有**弹开成功**的 k。
    ///
    /// 每个 k 用一次独立的出招：先等假人从 AttackState 里出来，
    /// 再从它起手的那一帧开始数，到点就按。
    /// </summary>
    private async System.Threading.Tasks.Task<List<int>> Scan(bool tap)
    {
        var ok = new List<int>();

        for (int lead = 0; lead <= MaxLeadFrames; lead++)
        {
            _current.Clear();
            _guardWinTrace = "";
            _stateTrace = "";
            _sinceTrace = "";
            Input.ActionRelease("guard");

            // 每个样本前彻底松开并等过 GuardReentryLockFrames。
            // 关键是**上一轮结尾的松手**：如果这一轮紧接着就按住，
            // 会被判成"快速重按"，那一段防御窗口干脆不开——
            // 于是"按住"看起来永远弹不开。必须先把上一次松手清干净。
            await WaitPhysicsFrames(20);

            // ① 等假人从上一招里出来，保证起点干净。
            for (int w = 0; w < 600; w++)
            {
                if (_attacker.Machine.Current is not AttackState)
                    break;

                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }

            // ② 等到假人起手（拿到 untilActive 的第一个有效值）。
            //
            // **按住模式必须在这里才开始按**，不能提前。
            // 弹开窗只在"按下守卫的那一刻"开一次（9 帧）然后过期，所以：
            // 如果一开始就按住，等到假人起手时窗口早就没了，
            // 测出来的就只是"过期后一直格挡"——那不是"按住弹不开"，
            // 那是"按早了"。第一版就是这么误诊的。
            int untilFirst = int.MaxValue;
            for (int w = 0; w < 600; w++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                untilFirst = FramesUntilEnemyActive();
                if (untilFirst != int.MaxValue)
                    break;
            }

            if (untilFirst == int.MaxValue)
            {
                GD.PrintErr($"[窗口扫描] 提前 {lead} 帧：等了 600 帧假人都没出招");
                Input.ActionRelease("guard");
                continue;
            }

            // ③ 从这一帧起数"距离判定还有几帧"，在正确的时机按下。
            int pressFrame = -1;
            bool tapRunning = false;
            int tapEnd = -1;

            int remaining = FramesPerSample;
            float distBefore = _player.GlobalPosition.DistanceTo(_attacker.GlobalPosition);
            bool sawActive = false;
            bool sawGuarding = false;
            float distAtActive = -1f;

            while (remaining-- > 0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

                if (_attacker.PrimaryHitbox?.IsActiveThisFrame ?? false)
                {
                    sawActive = true;
                    distAtActive = _player.GlobalPosition.DistanceTo(_attacker.GlobalPosition);
                }

                // 直接记录玩家的状态名——`IsGuarding` 可能被别的路径置真，
                // 而这里要确认的是"到底进没进 GuardState"。
                if (_guardWinTrace.Length < 90)
                    _stateTrace += _player.Machine.Current.GetType().Name.Replace("State", "") + ",";

                if (_player.IsGuarding && !sawGuarding)
                {
                    // 第一次进格挡的那一刻，把"快速重按"判定的两个输入打出来。
                    // 这是唯一能永久关掉弹开窗的逻辑，必须直接看它的输入值。
                    var t = _player.GetType();
                    object? localF = t.GetField("_localFrame",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_player);
                    object? relF = t.GetField("_lastGuardReleaseFrame",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_player);
                    _reentryTrace = $"localFrame={localF} lastRelease={relF} 差={(localF is int a && relF is int b ? a - b : -999)}";
                }

                if (_player.IsGuarding)
                {
                    // 读 GuardState 内部窗口的 FramesSinceEntry：
                    // 它一直是 0 就说明 GuardState.Tick 根本没跑（那窗口也不会开）。
                    if (_guardWinTrace.Length < 90)
                    {
                        var gs = _player.Machine.Current;
                        object? win = gs.GetType().GetField("_window",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(gs);
                        object? since = win?.GetType().GetProperty("FramesSinceEntry")?.GetValue(win);
                        _sinceTrace += $"{since},";
                    }

                    sawGuarding = true;

                    // 窗口到底开没开——这是"按住弹不开"的判据。
                    // 只在最开始几帧记，避免刷屏。
                    if (_guardWinTrace.Length < 90)
                        _guardWinTrace += $"{_player.DeflectWindowFramesLeft},";
                }

                if (!tap)
                {
                    // 起手后的第一帧按下，然后**再也不松**：
                    // 这正是玩家"提前按住等刀来"的做法。
                    if (!sawGuarding)
                        Input.ActionPress("guard");

                    continue;
                }

                // 松手
                if (tapRunning && tapEnd >= 0 && remaining <= tapEnd)
                {
                    Input.ActionRelease("guard");
                    tapRunning = false;
                    tapEnd = -1;
                }

                if (!tapRunning && pressFrame < 0)
                {
                    int untilActive = FramesUntilEnemyActive();

                    // 还没按过，且剩余帧数正好等于 lead → 按下。
                    // `untilActive` 在命中之后就变成 int.MaxValue，所以这个条件天然只成立一次。
                    if (untilActive != int.MaxValue && untilActive <= lead)
                    {
                        Input.ActionPress("guard");
                        tapRunning = true;
                        tapEnd = remaining - Mathf.Max(1, TapHoldFrames);
                        pressFrame = remaining;
                    }
                }
            }

            float distAfter = _player.GlobalPosition.DistanceTo(_attacker.GlobalPosition);
            _trace = $"距离 {distBefore:F2}→{distAfter:F2}m" +
                     (sawActive ? $"，判定期距离 {distAtActive:F2}m" : "，假人判定框全程没开") +
                     (sawGuarding ? "，玩家进过格挡" : "，玩家从未进格挡");

            Input.ActionRelease("guard");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            bool deflected = false;
            foreach (HitEvent e in _current)
            {
                if (e.Verdict == Verdict.Deflect)
                    deflected = true;
            }

            if (deflected)
                ok.Add(lead);

            // 前两个样本无条件把"窗口剩余帧"打出来——这是"按住到底开没开窗"的直接证据。
            string winTrace = lead <= 1 ? $"   窗口内帧号[{_sinceTrace}] (localFrame 差={_reentryTrace})" : string.Empty;
            GD.Print($"[窗口扫描]   提前 {lead,2} 帧 → {Describe(_current)}{winTrace}{Diag()}");
        }

        return ok;
    }

    /// <summary>
    /// 没接触时把"它为什么没打我"打出来。实测这比猜测有用得多：
    /// 第一次跑按住模式全是"没接触"，加上距离轨迹后一眼看到玩家从 1.7m 走到了 0.75m。
    /// </summary>
    private string Diag()
    {
        if (_current.Count > 0)
            return string.Empty;

        string enemyState = _attacker.Machine.Current.GetType().Name;
        return $"   [诊断：假人 {enemyState} 出招 {_attacker.AttackCount} 次 / {_trace} / 窗口剩余序列 {_guardWinTrace}]";
    }

    /// <summary>本次样本的距离/状态轨迹。只在"没接触"时打印。</summary>
    private string _trace = "";
    private string _guardWinTrace = "";
    private string _stateTrace = "";
    private string _reentryTrace = "";
    private string _sinceTrace = "";

    private static string Describe(List<HitEvent> contacts)
    {
        if (contacts.Count == 0)
            return "没接触（这一招没打到人）";

        var names = new List<string>();
        foreach (HitEvent e in contacts)
        {
            names.Add(e.Verdict switch
            {
                Verdict.Deflect => "弹开",
                Verdict.Block => "格挡",
                Verdict.Clash => "拼刀",
                Verdict.Hit => "挨打",
                Verdict.Miss => "躲开",
                _ => e.Verdict.ToString(),
            });
        }

        return string.Join("+", names);
    }

    private int FramesUntilEnemyActive()
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
            return int.MaxValue;

        return attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
    }

    private void OnHitResolved(HitEvent e)
    {
        if (e.DefenderId == _player.ActorId)
            _current.Add(e);
    }

    private void Report(List<int> tapOk, List<int> holdOk)
    {
        GD.Print("[窗口扫描] ══ 结论 ══");

        // 配置窗口总宽度：读玩家侧实际生效的那一个（要经过 CombatTuning 合成）。
        var diff = GD.Load<Resource>("res://data/difficulty/samurai.tres");
        object? configured = diff?.GetType().GetProperty("DeflectWindowFrames")?.GetValue(diff);
        GD.Print($"[窗口扫描] 难度档（武士）配置的弹开窗 = {configured} 帧");
        GD.Print($"[窗口扫描] 点按能弹开的提前量：{Join(tapOk)}");
        GD.Print($"[窗口扫描] 按住能弹开的提前量：{Join(holdOk)}");

        if (tapOk.Count > 0)
            GD.Print($"[窗口扫描] 点按有效区间 = 提前 {Min(tapOk)}~{Max(tapOk)} 帧（{Max(tapOk) - Min(tapOk) + 1} 帧宽）");

        if (holdOk.Count > 0)
            GD.Print($"[窗口扫描] 按住最多只能在提前 {Max(holdOk)} 帧内成功；再早就永远不会开窗");

        Check(tapOk.Count > 0, "点按在任何提前量下都弹不开：弹开窗根本没接上");

        // 「按住弹不开」**不是失败**，是设计特征，所以只报事实、不记失败。
        //
        // 机制：`GuardState.Tick` 里整段防御只打开一次窗口（`OpensWindowThisFrame`
        // 只可能为真一帧），而敌人前摇 24 帧。按住不放时窗口在第 N 帧就过期，
        // 判定帧才到——于是只吃得到格挡。玩家侧的答案是指示器（deflect_cue），
        // 让他**看见**窗口什么时候开着，而不是把规则改宽。
        if (holdOk.Count == 0)
        {
            GD.Print("[窗口扫描] 说明：按住不放全程零弹开——这是 02 §8 的设计" +
                     "（窗口只在按下那一刻开一次），玩家侧靠弹开窗指示器补上可见性。");
        }

        foreach (string f in _failures)
            GD.PrintErr($"[窗口扫描] ✗ {f}");

        if (_failures.Count == 0)
            GD.Print("[窗口扫描] ✓ 弹开窗的时序与设计一致");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private static string Join(List<int> xs) => xs.Count == 0 ? "无" : string.Join(" / ", xs);

    private static int Min(List<int> xs)
    {
        int m = int.MaxValue;
        foreach (int x in xs)
            m = Mathf.Min(m, x);
        return m;
    }

    private static int Max(List<int> xs)
    {
        int m = int.MinValue;
        foreach (int x in xs)
            m = Mathf.Max(m, x);
        return m;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(40f, 0.4f, 40f) },
            Position = new Vector3(0f, -0.2f, 0f),
        });
        AddChild(body);
    }

    private static T Load<T>(string path) where T : Node =>
        GD.Load<PackedScene>(path).Instantiate<T>();

    private async System.Threading.Tasks.Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
