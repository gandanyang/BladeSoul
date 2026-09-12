using System.Collections.Generic;
using Godot;
using Oniblade.Audio;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>一闪测试的四种时机。每个模式一个场景（与 GuardSpam / DodgeTooEarly 同一套做法）。</summary>
public enum IssenTestMode
{
    /// <summary>真一闪窗口内 → 一闪成立。</summary>
    InWindow,

    /// <summary>★ 安全窗（按早了一点点）→ 转格挡，**不挨打**。</summary>
    SafeWindow,

    /// <summary>太早（比安全窗还早）→ 落空，30 帧无防御硬直。</summary>
    TooEarly,

    /// <summary>太晚（敌人判定帧已开始）→ 落空。</summary>
    TooLate,
}

/// <summary>
/// 一闪端到端对照实验（T20 / 04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/IssenInWindow.tscn
///     godot --headless --path . res://scenes/tests/IssenSafeWindow.tscn
///     godot --headless --path . res://scenes/tests/IssenTooEarly.tscn
///     godot --headless --path . res://scenes/tests/IssenTooLate.tscn
///
/// **必须有对照组**是 T12/T13 验证出来的做法：那两张卡各自的对照实验都抓到了真东西
/// （"闪早了确实会挨打 4 次"）。所以这里也按三种时机分开测，尤其是
/// **安全窗必须单独断言**——它是防劝退核心，不能混在"能打出闪"里一起过。
///
/// 四个模式的机器人、帧数、距离、敌人招式完全相同，唯一差别是**按攻击的那一帧**。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class IssenTrainingTest : Node3D
{
    private const int TotalFrames = 520;

    /// <summary>慢镜恢复之后再多跑一会儿，好断言 TimeScale 真的回到了 1.0。</summary>
    private const int SettleFrames = 90;

    [Export] public IssenTestMode Mode { get; set; } = IssenTestMode.InWindow;

    private readonly List<string> _failures = new();

    private EventBus? _bus;
    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;

    private bool _pressedThisAttack;
    private bool _attackKeyHeld;
    private int _attackKeyHeldFrames;

    private int _issenEvents;
    private IssenKind _lastIssenKind = IssenKind.None;
    private int _playerBlocks;
    private int _playerHits;

    private float _minTimeScale = 1f;
    private int _observedIssenDurationFrames = -1;

    /// <summary>
    /// 主循环结束后关掉交战统计。
    ///
    /// 不关的话，尾段那一刀会算进来：假人每隔约 126 帧出一次刀，
    /// 主循环在 520 帧收尾时最后一刀可能刚开始，机器人已经停手了，
    /// 于是"挨打 1 次"——那是**测试自身的尾巴**，不是机制的问题。
    /// 后 90 帧只用来等慢镜恢复，不该再统计任何交战。
    /// </summary>
    private bool _combatWindowClosed;

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

        DifficultyProfile? difficulty = _player.Difficulty;
        Check(difficulty is not null, "玩家没有配难度档，一闪窗口读不到");

        // 窗口宽度由 CombatTuning 合成（08 §3 P1-3 红线），测试也用同一个入口，
        // 免得测试和产线各算一套、互相说服对方是绿的。
        int window = CombatTuning.ResolveIssenWindowFrames(difficulty?.IssenWindowFrames ?? 0);
        int safe = difficulty?.IssenSafeWindowFrames ?? 0;

        GD.Print($"[一闪] 模式 = {Mode}，窗口 {window} 帧 / 安全窗 {safe} 帧");

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        for (int frame = 0; frame < TotalFrames + SettleFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            TickAttackKey();

            if (frame >= TotalFrames)
            {
                // 主循环到此结束：后面只用来等慢镜恢复，不再统计交战。
                // 不关的话尾巴上那一刀会算成"挨打"——假人每约 126 帧出一次刀，
                // 主循环收尾时它可能刚开始，而机器人已经停手了。
                // 那是**测试自身的边界**，不是机制的问题。
                _combatWindowClosed = true;
                continue;
            }

            _minTimeScale = Mathf.Min(_minTimeScale, (float)Engine.TimeScale);

            if (_player.Machine.Current is IssenState issen && _observedIssenDurationFrames < 0)
                _observedIssenDurationFrames = issen.DurationFrames;

            TickRobot(window, safe);
        }

        Input.ActionRelease("attack");

        Report(window, safe);
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
    }

    /// <summary>attack 是 JustPressed 语义：按住 2 帧再松，避开"按下与读取落在同一帧"的边界。</summary>
    private void TickAttackKey()
    {
        if (!_attackKeyHeld)
            return;

        if (++_attackKeyHeldFrames >= 2)
        {
            Input.ActionRelease("attack");
            _attackKeyHeld = false;
        }
    }

    /// <summary>四个模式唯一的差别就在这里：什么时候按下去。</summary>
    private void TickRobot(int window, int safe)
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
        {
            // 这一刀结束了，允许对下一刀再按一次。
            _pressedThisAttack = false;
            return;
        }

        if (_pressedThisAttack)
            return;

        int frame = attack.Sequence.Frame;
        AttackTiming timing = attack.Sequence.Current;

        ThreatPhase phase =
            frame < timing.ActiveStart ? ThreatPhase.Windup
            : frame < timing.ActiveEnd ? ThreatPhase.Active
            : ThreatPhase.Recovery;

        int framesUntilActive = timing.ActiveStart - frame;

        bool press = Mode switch
        {
            IssenTestMode.InWindow => phase == ThreatPhase.Windup
                                      && framesUntilActive >= 1 && framesUntilActive <= window,

            IssenTestMode.SafeWindow => phase == ThreatPhase.Windup
                                        && framesUntilActive > window
                                        && framesUntilActive <= window + safe,

            IssenTestMode.TooEarly => phase == ThreatPhase.Windup
                                      && framesUntilActive > window + safe,

            IssenTestMode.TooLate => phase == ThreatPhase.Active,

            _ => false,
        };

        if (!press)
            return;

        GD.Print($"[一闪] 按下攻击：物理帧 {Engine.GetPhysicsFrames()}，" +
                 $"{phase} 距判定 {framesUntilActive} 帧");

        Input.ActionPress("attack");
        _attackKeyHeld = true;
        _attackKeyHeldFrames = 0;
        _pressedThisAttack = true;
    }

    private void OnHitResolved(HitEvent e)
    {
        if (_combatWindowClosed || e.DefenderId != _player.ActorId)
            return;

        GD.Print($"[一闪] 交战：物理帧 {Engine.GetPhysicsFrames()} {e.Verdict} " +
                 $"attack={e.AttackId} kind={e.IssenKind} dmg={e.Damage}");

        switch (e.Verdict)
        {
            case Verdict.Issen:
                _issenEvents++;
                _lastIssenKind = e.IssenKind;
                break;
            case Verdict.Block:
                _playerBlocks++;
                break;
            case Verdict.Hit:
                _playerHits++;
                break;
        }
    }

    private void Report(int window, int safe)
    {
        GD.Print($"[一闪] 结果：一闪 {_issenEvents} 次（kind={_lastIssenKind}）/ 格挡 {_playerBlocks} / 挨打 {_playerHits}");
        GD.Print($"[一闪] 玩家 HP {_player.Health.Current}/{_player.Health.Max}，" +
                 $"最后一次意图 {_player.LastIssenIntent}，真一闪授予 {_player.ShinIssenGrants} 次，" +
                 $"IssenState 时长 {( _observedIssenDurationFrames < 0 ? "未进入" : _observedIssenDurationFrames.ToString())}");
        GD.Print($"[一闪] 音效序列触发 {AudioDirector.Instance?.IssenSequenceCount ?? -1} 次，" +
                 $"TimeScale 最低 {_minTimeScale:0.###}，结束时 {Engine.TimeScale:0.###}");

        switch (Mode)
        {
            case IssenTestMode.InWindow:
                Check(_issenEvents > 0, "窗口内按攻击却没有打出 Verdict.Issen");
                Check(_lastIssenKind == IssenKind.Shin, $"一闪类型是 {_lastIssenKind}，应为 Shin");
                Check(_player.ShinIssenGrants > 0, "没有授予过真一闪 buff");
                Check(_playerHits == 0, $"一闪成功却挨了 {_playerHits} 次打：一闪没有真正取消掉那一刀");

                // 对杂兵即死。注意挥砍假人是 Invincible，Die() 会把它血量补回满，
                // 所以 IsDead 观察不到——改断言"收益表说它该即死"，那才是这条规格的实体。
                IssenEffect effect = IssenTable.For(IssenKind.Shin, EnemyTier.Grunt);
                Check(effect.InstantKill, "真一闪对杂兵不是即死：IssenTable 的收益被改了");

                // 表现：音效序列 + 慢镜必须降下去、并且必须回来。
                Check((AudioDirector.Instance?.IssenSequenceCount ?? 0) > 0,
                    "一闪没有触发 IssenSlash / IssenImpact 音效序列");

                // "降到 0.25"分两条证：计数器证明代码路径确实把 0.25 写进去了，
                // 采样值证明它确实生效了。**不能断言采样值 == 0.25**——
                // 恢复 Tween 在两次物理帧之间就已经推进，外部永远采不到那个瞬间。
                Check(_player.IssenSlowMoCount > 0, "一闪没有触发慢镜（IssenSlowMoCount 为 0）");
                Check(_minTimeScale < 0.5f,
                    $"慢镜没有生效：采样到的最低 TimeScale 是 {_minTimeScale:0.###}，没有明显低于 1.0");
                Check(Mathf.Abs(Engine.TimeScale - 1.0f) < 0.001f,
                    $"慢镜没有恢复：结束时 TimeScale = {Engine.TimeScale:0.###}（应回到 1.0）");
                break;

            case IssenTestMode.SafeWindow:
                // ★ 这一组是防劝退核心，必须单独成立。
                Check(_player.LastIssenIntent == IssenIntent.SafeGuard,
                    $"按在安全窗里，意图却是 {_player.LastIssenIntent}，应为 SafeGuard");
                Check(_issenEvents == 0, $"安全窗里打出了 {_issenEvents} 次一闪：安全窗不该给一闪");
                Check(_playerHits == 0, $"★ 安全窗挨了 {_playerHits} 次打：防劝退核心失效");
                Check(_player.Health.Current == _player.Health.Max, "★ 安全窗掉了血");
                Check(_playerBlocks > 0, "安全窗没有转成格挡（那一刀既没被闪、也没被挡）");
                Check(Mathf.Abs(Engine.TimeScale - 1.0f) < 0.001f,
                    "安全窗不该触发慢镜，TimeScale 却变了");
                break;

            case IssenTestMode.TooEarly:
            case IssenTestMode.TooLate:
            {
                IssenIntent expected = Mode == IssenTestMode.TooEarly
                    ? IssenIntent.TooEarly
                    : IssenIntent.TooLate;

                Check(_player.LastIssenIntent == expected,
                    $"意图是 {_player.LastIssenIntent}，应为 {expected}");
                Check(_issenEvents == 0, $"落空却打出了 {_issenEvents} 次一闪");
                Check(_observedIssenDurationFrames == IssenState.WhiffDurationFrames,
                    $"落空的 IssenState 时长是 {_observedIssenDurationFrames}，应为 {IssenState.WhiffDurationFrames}");
                Check(_playerHits > 0, "落空之后没有挨打：30 帧无防御硬直没有生效（代价消失了）");
                Check(Mathf.Abs(Engine.TimeScale - 1.0f) < 0.001f,
                    "落空不该触发慢镜，TimeScale 却变了");
                break;
            }
        }

        Flush();
    }

    private void Flush()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[一闪] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print($"[一闪] ✓ {Mode} 通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
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

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }
}
