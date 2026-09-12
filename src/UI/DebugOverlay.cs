using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;

namespace Oniblade.UI;

/// <summary>
/// 调试面板（Autoload，04 文档 §13）。F1~F8。
///
/// 三条设计约束（TASKS.md §T2）：
/// · **只通过 <see cref="ICombatActorDebug"/> 与 <see cref="EventBus"/> 读数据**，
///   不引用 CombatActor 具体类型——所以它能和战斗管线并行开发，管线没写完也能跑。
/// · 找不到任何战斗单位时显示"无目标"，绝不报错。
/// · 面板不改游戏逻辑。唯一的写操作是 F2/F3 的引擎时间控制、F4 的无敌开关、
///   F5 的画线，以及 F7 的重开。
///
/// 命令行（只影响调试，不影响正常启动）：
/// <code>
/// godot --path . -- --debug-panel           启动时直接展开面板（截图用）
/// godot --path . -- --debug-panel-dump      无头打印一份面板文本后退出（留证据用）
/// godot --path . -- --debug-panel-wires     启动时直接打开 F5 的判定框线框
/// </code>
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    private const int RefreshIntervalFrames = 6;   // 10Hz，与 04 §15 的 AI 决策频率一致
    private const int PanelLayerIndex = 100;
    private const int MaxEnemyRows = 8;
    private const int BarWidth = 18;
    private const int CircleSegments = 20;
    private const int MaxScanNodes = 4000;
    private const string CombatActorGroup = "combat_actor";
    private const string PanelArg = "--debug-panel";
    private const string DumpArg = "--debug-panel-dump";
    private const string WiresArg = "--debug-panel-wires";

    // ── BBCode 颜色 ──
    private const string CHeader = "[color=#6f8fbf]";
    private const string CAccent = "[color=#8fd0ff]";
    private const string CGood = "[color=#6fdc8c]";
    private const string CWarn = "[color=#ffcc66]";
    private const string CBad = "[color=#ff7a7a]";
    private const string CCyan = "[color=#66d9e8]";
    private const string CGold = "[color=#ffd479]";
    private const string CMuted = "[color=#7a8698]";
    private const string CEnd = "[/color]";

    [ExportGroup("启动")]
    /// <summary>启动时是否直接展开面板（默认只显示一行 F1 提示）。</summary>
    [Export] public bool StartVisible { get; set; }

    /// <summary>面板收起时是否显示底部的 "F1 调试面板" 提示。</summary>
    [Export] public bool ShowHintWhenHidden { get; set; } = true;

    private PanelContainer? _panel;
    private RichTextLabel? _text;
    private Label? _hint;
    private MeshInstance3D? _wireMesh;
    private ImmediateMesh? _wire;
    private StandardMaterial3D? _wireMaterial;

    private readonly StringBuilder _sb = new(2048);
    private readonly List<(Node Node, ICombatActorDebug Debug)> _actors = new(16);
    private readonly Dictionary<string, AttackData> _attacks = new(StringComparer.Ordinal);
    private readonly BattleStats _stats = new();

    private bool _panelVisible;
    private bool _slowMotion;
    private bool _invulnerable;
    private bool _drawShapes;
    private bool _showEnemy = true;
    private bool _cheatUnsupported;

    private bool _stepMode;
    private bool _stepAdvanceArmed;
    private bool _dumpWhenReady;
    private int _wireLines;
    private bool _wireDiagEnabled;
    private readonly List<string> _wireDiag = new();
    private int _originalMaxSteps = 8;
    private int _refreshCounter;
    private bool _forceRefresh = true;
    private bool _useBlockGlyphs;

    public override void _Ready()
    {
        Layer = PanelLayerIndex;
        // F3 会把整棵树冻住，面板必须仍然能收输入、能重画。
        ProcessMode = ProcessModeEnum.Always;

        _originalMaxSteps = Engine.MaxPhysicsStepsPerFrame;

        BuildUi();
        BuildAttackIndex();
        Subscribe();

        bool dump = HasUserArg(DumpArg);
        _drawShapes = HasUserArg(WiresArg);
        SetPanelVisible(StartVisible || dump || HasUserArg(PanelArg));
        _dumpWhenReady = dump;

        // 这里刻意**不**刷新：Autoload 的 _Ready 可能早于主场景节点的 _Ready，
        // 那时候 CombatActor 的血/体干表还是 null。第一帧 _Process 再读就稳了。
        _forceRefresh = true;
    }

    public override void _ExitTree()
    {
        Unsubscribe();

        Engine.TimeScale = 1.0;
        Engine.MaxPhysicsStepsPerFrame = _originalMaxSteps;
        SceneTree? tree = GetTree();
        if (tree is not null && GodotObject.IsInstanceValid(tree))
            tree.Paused = false;

        if (_wireMesh is not null && GodotObject.IsInstanceValid(_wireMesh))
            _wireMesh.QueueFree();
        _wireMesh = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        Key code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

        switch (code)
        {
            case Key.F1:
                SetPanelVisible(!_panelVisible);
                break;
            case Key.F2:
                ToggleSlowMotion();
                break;
            case Key.F3:
                StepFrame(release: key.ShiftPressed);
                break;
            case Key.F4:
                ToggleInvulnerable();
                break;
            case Key.F5:
                _drawShapes = !_drawShapes;
                UpdateWireframes();
                GD.Print($"[调试面板] F5 判定框线框 {(_drawShapes ? "ON" : "OFF")}：{_wireLines} 条线段 / {_actors.Count} 个单位");
                break;
            case Key.F6:
                _showEnemy = !_showEnemy;
                break;
            case Key.F7:
                RestartBattle();
                break;
            case Key.F8:
                DumpStats();
                break;
            default:
                return;
        }

        _forceRefresh = true;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_dumpWhenReady)
        {
            _dumpWhenReady = false;
            Refresh();
            if (_drawShapes)
            {
                _wireDiagEnabled = true;
                UpdateWireframes();   // 一并走一遍 F5 的代码路径，免得它只在人手按时才炸
            }
            GD.Print(StripBbcode(BuildPanelText()));
            GD.Print($"[调试面板] F5 判定框：{(_drawShapes ? "on" : "off")}，{_wireLines} 条线段 / {_actors.Count} 个单位");
            foreach (string line in _wireDiag)
                GD.Print(line);

            // F4 没法在无头下"真挨一刀"验证，但可以验证整条写入链路：
            // 往 IDebugCheatable 写 true 之后，只读侧 ICombatActorDebug.IsInvulnerable 必须立刻为 true。
            _invulnerable = true;
            ApplyInvulnerable();
            GD.Print($"[调试面板] F4 无敌链路：{ProbeCheatLink()}");
            _invulnerable = false;
            ApplyInvulnerable();

            GD.Print($"[调试面板] {DumpArg}：面板文本输出完毕（逻辑帧 {NowFrame()}）。");
            GetTree().Quit();
            return;
        }

        // F3 的单帧步进：上一轮迭代把游戏解冻了一个物理帧，这里立刻重新冻住。
        if (_stepAdvanceArmed)
        {
            _stepAdvanceArmed = false;
            GetTree().Paused = true;
        }

        if (_drawShapes)
            UpdateWireframes();

        _refreshCounter++;
        if (_forceRefresh || _refreshCounter >= RefreshIntervalFrames)
        {
            _refreshCounter = 0;
            _forceRefresh = false;
            Refresh();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 键位行为
    // ─────────────────────────────────────────────────────────────

    private void SetPanelVisible(bool visible)
    {
        _panelVisible = visible;
        if (_panel is not null)
            _panel.Visible = visible;
        if (_hint is not null)
            _hint.Visible = ShowHintWhenHidden && !visible;
    }

    private void ToggleSlowMotion()
    {
        _slowMotion = !_slowMotion;
        Engine.TimeScale = _slowMotion ? 0.25 : 1.0;
    }

    /// <summary>
    /// F3：暂停并单帧步进。
    /// 第一次按进入步进模式（立刻前进一帧），之后每按一次前进一帧；
    /// <c>Shift+F3</c> 退出步进模式并解除暂停。
    /// </summary>
    private void StepFrame(bool release)
    {
        if (release)
        {
            _stepMode = false;
            _stepAdvanceArmed = false;
            Engine.MaxPhysicsStepsPerFrame = _originalMaxSteps;
            GetTree().Paused = false;
            return;
        }

        if (!_stepMode)
        {
            _stepMode = true;
            Engine.MaxPhysicsStepsPerFrame = 1;   // 保证一次只走一个物理帧
            GetTree().Paused = true;
        }

        // 解冻 → 下一次迭代跑完一个物理帧 → 回到 _Process 时再冻住。
        GetTree().Paused = false;
        _stepAdvanceArmed = true;
    }

    private void ToggleInvulnerable()
    {
        _invulnerable = !_invulnerable;
        ApplyInvulnerable();
    }

    private void RestartBattle()
    {
        _stepMode = false;
        _stepAdvanceArmed = false;
        Engine.MaxPhysicsStepsPerFrame = _originalMaxSteps;
        Engine.TimeScale = 1.0;
        _slowMotion = false;
        _invulnerable = false;
        _drawShapes = false;
        _actors.Clear();
        _stats.Reset();

        GD.Print("[调试面板] F7 重开当前战斗（ReloadCurrentScene；原地重开协议见 08 §P1-2，属 M1）");
        Error err = GetTree().ReloadCurrentScene();
        if (err != Error.Ok)
            GD.PrintErr($"[调试面板] 重开失败：{err}");
    }

    private void DumpStats()
    {
        GD.Print(_stats.FormatReport(NowFrame()));
    }

    // ─────────────────────────────────────────────────────────────
    // 数据采集
    // ─────────────────────────────────────────────────────────────

    private void Subscribe()
    {
        if (EventBus.Instance is { } bus)
        {
            bus.HitResolved += OnHitResolved;
            bus.ActorDied += OnActorDied;
        }
    }

    private void Unsubscribe()
    {
        if (EventBus.Instance is { } bus)
        {
            bus.HitResolved -= OnHitResolved;
            bus.ActorDied -= OnActorDied;
        }
    }

    private void OnHitResolved(HitEvent e)
    {
        _stats.Record(e);
        _forceRefresh = true;
    }

    private void OnActorDied(int actorId)
    {
        int frame = NowFrame();
        foreach ((Node node, ICombatActorDebug debug) in _actors)
        {
            if (!GodotObject.IsInstanceValid(node) || debug.ActorId != actorId)
                continue;
            if (node is Node3D node3)
            {
                Vector3 p = node3.GlobalPosition;
                _stats.RecordDeath(actorId, frame, p.X, p.Y, p.Z);
            }
            else
            {
                _stats.RecordDeath(actorId, frame, 0f, 0f, 0f);
            }
            break;
        }

        _forceRefresh = true;
    }

    private void Refresh()
    {
        CollectActors();
        SyncPlayerActorId();
        ApplyInvulnerable();

        if (_panelVisible && _text is not null)
            _text.Text = BuildPanelText();
    }

    /// <summary>
    /// 认得出玩家就按玩家口径统计（02 §11 的"弹开成功率"只该算玩家挨到的招），
    /// 认不出就退回"不过滤"——灰盒期只有一个单位在挨打，两者等价。
    /// </summary>
    private void SyncPlayerActorId()
    {
        foreach ((Node node, ICombatActorDebug debug) in _actors)
        {
            if (GodotObject.IsInstanceValid(node) && IsPlayerNode(node))
            {
                _stats.PlayerActorId = debug.ActorId;
                return;
            }
        }

        _stats.PlayerActorId = null;
    }

    private void CollectActors()
    {
        _actors.Clear();

        AddFromGroup(CombatActorGroup);
        if (_actors.Count == 0)
        {
            // 战斗管线还没把单位挂进 combat_actor 组时的兜底，方便 T1 并行开发。
            AddFromGroup("player");
            AddFromGroup("enemy");
        }
        if (_actors.Count == 0 && _panelVisible)
            CollectByScan(GetTree().Root, 0, MaxScanNodes);

        // 玩家排最前，其余按 ActorId 稳定排序（04 §15 的"同帧顺序可复现"）。
        _actors.Sort(static (a, b) =>
        {
            int byPlayer = (IsPlayerNode(a.Node) ? 0 : 1).CompareTo(IsPlayerNode(b.Node) ? 0 : 1);
            return byPlayer != 0 ? byPlayer : a.Debug.ActorId.CompareTo(b.Debug.ActorId);
        });
    }

    private void AddFromGroup(string group)
    {
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is ICombatActorDebug debug && !Contains(node))
                _actors.Add((node, debug));
        }
    }

    private bool Contains(Node node)
    {
        foreach ((Node existing, ICombatActorDebug _) in _actors)
        {
            if (ReferenceEquals(existing, node))
                return true;
        }
        return false;
    }

    private void CollectByScan(Node node, int depth, int budget)
    {
        if (depth > 32 || budget <= 0)
            return;

        if (node is ICombatActorDebug debug && !Contains(node))
            _actors.Add((node, debug));

        foreach (Node child in node.GetChildren())
            CollectByScan(child, depth + 1, budget - 1);
    }

    /// <summary>F4：把无敌开关同步给玩家单位。没实现写接口时只记一笔，不报错。</summary>
    private void ApplyInvulnerable()
    {
        _cheatUnsupported = false;

        foreach ((Node node, ICombatActorDebug _) in _actors)
        {
            if (!GodotObject.IsInstanceValid(node) || !IsPlayerNode(node))
                continue;

            if (node is IDebugCheatable cheatable)
            {
                cheatable.DebugInvulnerable = _invulnerable;
            }
            else if (node.HasMethod("SetDebugInvulnerable"))
            {
                node.Call("SetDebugInvulnerable", _invulnerable);
            }
            else
            {
                _cheatUnsupported = true;
            }
            break;
        }
    }

    private static bool IsPlayerNode(Node node)
    {
        if (node.IsInGroup("player") || node.Name == "Player")
            return true;

        if (node is ICombatActorDebug debug)
        {
            string name = debug.DebugName;
            return name.Contains("Player", StringComparison.OrdinalIgnoreCase)
                || name.Contains("玩家", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>F4 的自证：写进去之后，只读侧要立刻看得到。</summary>
    private string ProbeCheatLink()
    {
        foreach ((Node node, ICombatActorDebug debug) in _actors)
        {
            if (!GodotObject.IsInstanceValid(node) || !IsPlayerNode(node))
                continue;

            if (node is not IDebugCheatable)
                return $"玩家 {debug.DebugName} 没实现 IDebugCheatable（F4 无效）";

            bool reported = debug.IsInvulnerable;
            return reported
                ? $"写 DebugInvulnerable=true → ICombatActorDebug.IsInvulnerable=true ✓"
                : $"写 DebugInvulnerable=true 但 IsInvulnerable 仍为 false ✗";
        }

        return "场上没有玩家单位（F4 无处可写）";
    }

    // ─────────────────────────────────────────────────────────────
    // 面板文本
    // ─────────────────────────────────────────────────────────────

    private string BuildPanelText()
    {
        try
        {
            return BuildPanelTextCore();
        }
        catch (Exception ex)
        {
            // 面板永远不许把游戏搞崩。读到还没初始化完的战斗单位时退化成一行提示，
            // 下一个刷新周期（6 帧后）自己会恢复。
            return CMuted + "面板读取中（战斗单位尚未初始化完）：" + ex.GetType().Name + CEnd;
        }
    }

    private string BuildPanelTextCore()
    {
        _sb.Clear();

        ICombatActorDebug? player = null;
        foreach ((Node node, ICombatActorDebug debug) in _actors)
        {
            if (GodotObject.IsInstanceValid(node) && IsPlayerNode(node))
            {
                player = debug;
                break;
            }
        }

        AppendHeader("STATE");
        if (_actors.Count == 0)
        {
            AppendLine(CMuted + "  无目标（没有单位在 combat_actor 组里）" + CEnd);
            AppendLine(CMuted + "  玩家的 CombatActor 一进组，这里就会自己亮起来。" + CEnd);
        }
        else if (player is null)
        {
            AppendLine(CWarn + $"  有 {_actors.Count} 个战斗单位，但没一个被认成玩家" + CEnd);
        }
        else
        {
            AppendLine($"  {CAccent}{player.DebugName}{CEnd}  {player.StateName}  {FrameText(player)}");
            AppendAttackDetail(player);
            AppendLine(CMuted + $"  场上单位 {_actors.Count}（其他 {_actors.Count - 1}）" + CEnd);
        }

        AppendHeader("WINDOWS");
        if (player is null)
        {
            AppendLine(CMuted + "  —" + CEnd);
        }
        else
        {
            AppendLine(player.DeflectWindowFramesLeft > 0
                ? $"  弹开窗     {CGood}OPEN {player.DeflectWindowFramesLeft}f{CEnd}"
                : $"  弹开窗     {CMuted}CLOSED{CEnd}");

            AppendLine(player.IssenBuff != IssenKind.None
                ? $"  一闪 buff  {CGood}{player.IssenBuff}  {player.IssenBuffFramesLeft}f left{CEnd}"
                : $"  一闪 buff  {CMuted}—{CEnd}");

            AppendLine($"  格挡       {(player.IsGuarding ? CCyan + "GUARD" + CEnd : CMuted + "—" + CEnd)}"
                     + $"   无敌 {(player.IsInvulnerable ? CCyan + "YES" + CEnd : CMuted + "no" + CEnd)}"
                     + (player.HitStopFramesLeft > 0 ? $"   顿帧 {CWarn}{player.HitStopFramesLeft}f{CEnd}" : string.Empty));

            AppendLine(CMuted + "  输入缓冲   未暴露（PlayerInputBuffer 不在 ICombatActorDebug 上）" + CEnd);
        }

        AppendHeader("VITALS");
        if (_actors.Count == 0)
        {
            AppendLine(CMuted + "  —" + CEnd);
        }
        else
        {
            foreach ((Node node, ICombatActorDebug debug) in _actors)
            {
                if (!GodotObject.IsInstanceValid(node)
                    || !TryReadVitals(debug, out int health, out int maxHealth, out int posture, out int maxPosture))
                    continue;
                AppendLine($"  {VitalsName(node, debug)}  HP  {Bar(health, maxHealth)}  {health}/{maxHealth}");
                AppendLine($"            PST {Bar(posture, maxPosture)}  {posture}/{maxPosture}");
            }
        }

        AppendHeader("ENEMY");
        if (!_showEnemy)
        {
            AppendLine(CMuted + "  （F6 已关闭）" + CEnd);
        }
        else
        {
            int shown = 0;
            foreach ((Node node, ICombatActorDebug debug) in _actors)
            {
                if (!GodotObject.IsInstanceValid(node) || IsPlayerNode(node)
                    || !TryReadVitals(debug, out int health, out int maxHealth, out int posture, out int maxPosture))
                    continue;
                if (shown >= MaxEnemyRows)
                    break;
                shown++;
                AppendLine($"  {CAccent}#{debug.ActorId} {debug.DebugName}{CEnd}  {debug.StateName}  {FrameText(debug)}");
                AppendLine(CMuted + $"    HP {health}/{maxHealth}  PST {posture}/{maxPosture}"
                                 + (string.IsNullOrEmpty(debug.CurrentAttackId) ? string.Empty : $"  招式 {debug.CurrentAttackId}") + CEnd);
            }
            if (shown == 0)
                AppendLine(CMuted + "  无敌人" + CEnd);
            else
                AppendLine(CMuted + "  反应延迟/下一步不在 ICombatActorDebug 上，见完成报告" + CEnd);
        }

        AppendHeader($"LAST {BattleStats.RecentCapacity} VERDICTS");
        if (_stats.RecentCount == 0)
        {
            AppendLine(CMuted + "  —（本场还没有结算）" + CEnd);
        }
        else
        {
            for (int i = 0; i < _stats.RecentCount; i++)
            {
                HitEvent e = _stats.RecentAt(i);
                AppendLine($"  {VerdictColor(e.Verdict)}{BattleStats.DescribeVerdict(e)}{CEnd}");
            }
        }

        AppendHeader("ANIM SYNC");
        AppendLine(player is null
            ? CMuted + "  anim — / logic —" + CEnd
            : $"  anim 未接入 / logic {player.StateFrame}   {CMuted}（动画层 M2 后接入，偏差暂不可算）{CEnd}");

        AppendHeader("KEYS");
        AppendLine(CMuted + "  F1 面板  " + CEnd + $"F2 慢放 {OnOff(_slowMotion)}  "
                 + CMuted + "F3 单帧  " + CEnd + $"F4 无敌 {OnOff(_invulnerable)}  "
                 + CMuted + "F5 判定框 " + CEnd + OnOff(_drawShapes) + "  "
                 + CMuted + "F6 敌人 " + CEnd + OnOff(_showEnemy) + "  "
                 + CMuted + "F7 重开  F8 统计" + CEnd);
        AppendLine(CMuted + "  F3 单帧步进：每次前进 1 帧，Shift+F3 恢复" + CEnd);
        if (_cheatUnsupported && _invulnerable)
            AppendLine(CWarn + "  F4 无效：玩家单位未实现 IDebugCheatable（见完成报告）" + CEnd);

        return _sb.ToString();
    }

    private void AppendAttackDetail(ICombatActorDebug actor)
    {
        if (string.IsNullOrEmpty(actor.CurrentAttackId))
            return;

        AttackData? data = ResolveAttack(actor.CurrentAttackId);
        if (data is null)
        {
            AppendLine(CWarn + $"  招式 {actor.CurrentAttackId}（找不到对应的 .tres，帧窗不可算）" + CEnd);
            return;
        }

        AppendLine($"  招式 {CAccent}{data.Id}{CEnd}  前摇 {data.StartupFrames} / 判定 {data.ActiveFrames} / 后摇 {data.RecoveryFrames}  共 {data.TotalFrames} 帧");

        string phase = data.IsActiveAt(actor.StateFrame) ? CGood + "判定中" + CEnd
            : data.IsInRecoveryAt(actor.StateFrame) ? CWarn + "后摇" + CEnd
            : data.StartupFrames > 0 && actor.StateFrame < data.ActiveStart ? CMuted + "前摇" + CEnd
            : CMuted + "已结束" + CEnd;

        string cancel = data.Cancelable
            ? (data.CanCancelAt(actor.StateFrame) ? CGood + $"已开（第 {data.CancelOpenFrame} 帧起）" + CEnd
                                                  : CMuted + $"未开（第 {data.CancelOpenFrame} 帧起）" + CEnd)
            : CMuted + "本招不可取消" + CEnd;

        AppendLine($"  当前相位 {phase}   取消窗 {cancel}");
    }

    private static string FrameText(ICombatActorDebug actor)
        => actor.StateTotalFrames > 0
            ? $"frame {actor.StateFrame}/{actor.StateTotalFrames}"
            : $"frame {actor.StateFrame}";

    /// <summary>
    /// 血/体干表是战斗单位在 _Ready 里建的；读得太早会是 null。
    /// 这里把"读不到"当成"这一帧跳过它"，而不是让面板炸掉。
    /// </summary>
    private static bool TryReadVitals(
        ICombatActorDebug debug,
        out int health, out int maxHealth, out int posture, out int maxPosture)
    {
        try
        {
            health = debug.Health;
            maxHealth = debug.MaxHealth;
            posture = debug.Posture;
            maxPosture = debug.MaxPosture;
            return true;
        }
        catch (Exception)
        {
            health = 0;
            maxHealth = 0;
            posture = 0;
            maxPosture = 0;
            return false;
        }
    }

    private static string VitalsName(Node node, ICombatActorDebug debug)
        => IsPlayerNode(node) ? $"{CAccent}{debug.DebugName}{CEnd}" : $"{CMuted}#{debug.ActorId} {debug.DebugName}{CEnd}";

    private string Bar(int value, int max)
    {
        int filled = max <= 0 ? 0 : Math.Clamp((int)Math.Round(value / (double)max * BarWidth), 0, BarWidth);
        char full = _useBlockGlyphs ? '█' : '#';
        char empty = _useBlockGlyphs ? '░' : '.';
        string colour = max > 0 && value * 4 <= max ? CBad : max > 0 && value * 2 <= max ? CWarn : CGood;
        return colour + new string(full, filled) + CMuted + new string(empty, BarWidth - filled) + CEnd;
    }

    private static string OnOff(bool value) => value ? CGood + "on" + CEnd : CMuted + "off" + CEnd;

    private static string VerdictColor(Verdict verdict) => verdict switch
    {
        Verdict.Deflect => CGood,
        Verdict.Issen => CGold,
        Verdict.Deathblow => CGold,
        Verdict.Clash => CWarn,
        Verdict.Block => CCyan,
        Verdict.Hit => CBad,
        Verdict.GuardBreak => CBad,
        _ => CMuted,
    };

    private void AppendHeader(string title)
    {
        if (_sb.Length > 0)
            _sb.Append('\n');
        _sb.Append(CHeader).Append("── ").Append(title).Append(' ');
        int pad = 32 - title.Length;
        if (pad > 0)
            _sb.Append('─', pad);
        _sb.Append(CEnd).Append('\n');
    }

    private void AppendLine(string line) => _sb.Append(line).Append('\n');

    // ─────────────────────────────────────────────────────────────
    // 招式数据索引（只读 data/**/*.tres，为了算出"当前在第几帧"）
    // ─────────────────────────────────────────────────────────────

    private void BuildAttackIndex()
    {
        ScanAttacks("res://data/attacks");
    }

    private void ScanAttacks(string path)
    {
        using DirAccess? dir = DirAccess.Open(path);
        if (dir is null)
            return;

        dir.ListDirBegin();
        while (true)
        {
            string name = dir.GetNext();
            if (string.IsNullOrEmpty(name))
                break;
            if (name is "." or "..")
                continue;

            string full = path.PathJoin(name);
            if (dir.CurrentIsDir())
            {
                ScanAttacks(full);
                continue;
            }
            if (!name.EndsWith(".tres", StringComparison.Ordinal))
                continue;

            // 用无类型 Load + 类型检查：data/attacks/ 下还有别的资源类型
            // （例如 T1 的 PlayerAttackSet），泛型重载会直接抛 InvalidCastException。
            if (ResourceLoader.Load(full) is not AttackData data)
                continue;

            if (!string.IsNullOrEmpty(data.Id))
                _attacks[data.Id] = data;
            _attacks.TryAdd(name.GetBaseName(), data);
        }
        dir.ListDirEnd();
    }

    private AttackData? ResolveAttack(string id)
        => !string.IsNullOrEmpty(id) && _attacks.TryGetValue(id, out AttackData? data) ? data : null;

    // ─────────────────────────────────────────────────────────────
    // F5：判定框线框
    // ─────────────────────────────────────────────────────────────

    private void UpdateWireframes()
    {
        if (_wire is null || _wireMesh is null || _wireMaterial is null)
            return;

        EnsureWireMeshParent();
        _wire.ClearSurfaces();

        if (!_drawShapes || _actors.Count == 0)
        {
            _wireMesh.Visible = false;
            _wireLines = 0;
            return;
        }

        var writer = new LineWriter(_wire, _wireMaterial);
        if (_wireDiagEnabled)
        {
            _wireDiag.Clear();
            writer.EnableDiagnostics(_wireDiag);
        }
        foreach ((Node node, ICombatActorDebug _) in _actors)
        {
            if (GodotObject.IsInstanceValid(node))
                AppendNodeShapes(node, writer, 0);
        }
        writer.Finish();

        _wireLines = writer.Lines;
        _wireMesh.Visible = writer.Lines > 0;
    }

    private void EnsureWireMeshParent()
    {
        if (_wireMesh is null)
            return;
        if (GodotObject.IsInstanceValid(_wireMesh) && _wireMesh.IsInsideTree())
            return;

        if (_wireMesh is null || !GodotObject.IsInstanceValid(_wireMesh))
        {
            _wireMesh = new MeshInstance3D
            {
                Name = "DebugOverlayWireframes",
                TopLevel = true,                       // 顶点已经是世界坐标
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                Mesh = _wire,
            };
        }

        try
        {
            GetTree().Root.AddChild(_wireMesh);
        }
        catch (Exception)
        {
            // Autoload 的 _Ready 期间不能往 Root 挂子节点（"busy setting up children"）；
            // 下一次 _Process 再试即可，F5 本来也不是启动路径上的东西。
        }
    }

    private static void AppendNodeShapes(Node node, LineWriter writer, int depth)
    {
        if (depth > 8)
            return;

        if (node is Node3D node3)
        {
            if (node is CollisionShape3D { Shape: { } collisionShape })
            {
                AppendShape(writer, collisionShape, node3.GlobalTransform, ShapeColor(node.Name.ToString()), $"{node3.GetPath()}");
            }
            else
            {
                // 04 §7 的 Hitbox 是 "Node3D + 一个 Shape3D 属性"，不是 CollisionShape3D，
                // 所以扫一遍这个节点暴露出来的 Shape3D 型 Godot 属性。
                // （CollisionShape3D 自己也有个 shape 属性，走上面的分支，别重复画。）
                AppendPropertyShapes(node3, writer);
            }
        }

        foreach (Node child in node.GetChildren())
            AppendNodeShapes(child, writer, depth + 1);
    }

    private static void AppendPropertyShapes(Node3D node3, LineWriter writer)
    {
        foreach (Godot.Collections.Dictionary property in node3.GetPropertyList())
        {
            try
            {
                if (!property.TryGetValue("name", out Variant nameVariant))
                    continue;
                string propertyName = nameVariant.AsString();
                if (propertyName is "script" or "owner" or "mesh" or "material_override")
                    continue;

                Variant value = node3.Get(propertyName);
                if (value.VariantType != Variant.Type.Object || value.As<Shape3D>() is not { } shape)
                    continue;

                AppendShape(writer, shape, node3.GlobalTransform, ShapeColor($"{node3.Name}/{propertyName}"), $"{node3.GetPath()}.{propertyName}");
            }
            catch (Exception)
            {
                // 某个属性取不到值不该让整个面板崩掉。
            }
        }
    }

    private static Color ShapeColor(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("hurt"))
            return new Color(0.30f, 0.85f, 1.00f);   // 青 = 可被打的
        if (lower.Contains("hit"))
            return new Color(1.00f, 0.35f, 0.35f);   // 红 = 打人的
        return new Color(1.00f, 0.80f, 0.20f);       // 橙 = 身体等其他碰撞
    }

    private static void AppendShape(LineWriter writer, Shape3D shape, Transform3D xform, Color colour, string source)
    {
        writer.BeginShape(source, shape);
        switch (shape)
        {
            case BoxShape3D box:
                AppendBox(writer, xform, box.Size * 0.5f, colour);
                break;
            case SphereShape3D sphere:
                AppendCircle(writer, xform, Vector3.Right, Vector3.Forward, sphere.Radius, 0f, colour);
                AppendCircle(writer, xform, Vector3.Right, Vector3.Up, sphere.Radius, 0f, colour);
                AppendCircle(writer, xform, Vector3.Forward, Vector3.Up, sphere.Radius, 0f, colour);
                break;
            case CapsuleShape3D capsule:
                AppendCapsule(writer, xform, capsule.Radius, capsule.Height, colour);
                break;
            case CylinderShape3D cylinder:
                AppendCylinder(writer, xform, cylinder.Radius, cylinder.Height, colour);
                break;
            case ConvexPolygonShape3D convex:
                AppendBox(writer, xform, ConvexHalfSize(convex.Points), colour);
                break;
            default:
                break;   // WorldBoundary / Segment / Concave / HeightMap 不画
        }
        writer.EndShape();
    }

    private static void AppendBox(LineWriter writer, Transform3D xform, Vector3 half, Color colour)
    {
        Vector3[] corner = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corner[i] = xform * new Vector3(
                (i & 1) == 0 ? -half.X : half.X,
                (i & 2) == 0 ? -half.Y : half.Y,
                (i & 4) == 0 ? -half.Z : half.Z);
        }

        for (int i = 0; i < BoxEdges.Length; i += 2)
            writer.Add(corner[BoxEdges[i]], corner[BoxEdges[i + 1]], colour);
    }

    private static readonly int[] BoxEdges =
    {
        0, 1, 1, 3, 3, 2, 2, 0,   // 底面
        4, 5, 5, 7, 7, 6, 6, 4,   // 顶面
        0, 4, 1, 5, 2, 6, 3, 7,   // 立柱
    };

    private static void AppendCircle(LineWriter writer, Transform3D xform, Vector3 axisA, Vector3 axisB, float radius, float yOffset, Color colour)
    {
        Vector3 previous = xform * (axisA * radius + Vector3.Up * yOffset);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float t = Mathf.Tau * i / CircleSegments;
            Vector3 point = xform * (axisA * (Mathf.Cos(t) * radius) + axisB * (Mathf.Sin(t) * radius) + Vector3.Up * yOffset);
            writer.Add(previous, point, colour);
            previous = point;
        }
    }

    private static void AppendArc(LineWriter writer, Transform3D xform, Vector3 axisA, Vector3 axisB, float radius, float yOffset, float from, float to, Color colour)
    {
        const int segments = 8;
        Vector3 previous = xform * (axisA * (Mathf.Cos(from) * radius) + axisB * (Mathf.Sin(from) * radius) + Vector3.Up * yOffset);
        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(from, to, i / (float)segments);
            Vector3 point = xform * (axisA * (Mathf.Cos(t) * radius) + axisB * (Mathf.Sin(t) * radius) + Vector3.Up * yOffset);
            writer.Add(previous, point, colour);
            previous = point;
        }
    }

    private static void AppendCapsule(LineWriter writer, Transform3D xform, float radius, float height, Color colour)
    {
        float half = Mathf.Max(0f, height * 0.5f - radius);

        AppendCircle(writer, xform, Vector3.Right, Vector3.Forward, radius, half, colour);
        AppendCircle(writer, xform, Vector3.Right, Vector3.Forward, radius, -half, colour);

        for (int i = 0; i < 4; i++)
        {
            float t = Mathf.Tau * i / 4f;
            var offset = new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
            writer.Add(xform * (offset + Vector3.Up * half), xform * (offset - Vector3.Up * half), colour);
        }

        AppendArc(writer, xform, Vector3.Right, Vector3.Up, radius, half, 0f, Mathf.Pi, colour);
        AppendArc(writer, xform, Vector3.Forward, Vector3.Up, radius, half, 0f, Mathf.Pi, colour);
        AppendArc(writer, xform, Vector3.Right, Vector3.Up, radius, -half, 0f, -Mathf.Pi, colour);
        AppendArc(writer, xform, Vector3.Forward, Vector3.Up, radius, -half, 0f, -Mathf.Pi, colour);
    }

    private static void AppendCylinder(LineWriter writer, Transform3D xform, float radius, float height, Color colour)
    {
        float half = height * 0.5f;
        AppendCircle(writer, xform, Vector3.Right, Vector3.Forward, radius, half, colour);
        AppendCircle(writer, xform, Vector3.Right, Vector3.Forward, radius, -half, colour);

        for (int i = 0; i < 4; i++)
        {
            float t = Mathf.Tau * i / 4f;
            var offset = new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
            writer.Add(xform * (offset + Vector3.Up * half), xform * (offset - Vector3.Up * half), colour);
        }
    }

    private static Vector3 ConvexHalfSize(Vector3[] points)
    {
        if (points.Length == 0)
            return Vector3.Zero;

        Vector3 min = points[0];
        Vector3 max = points[0];
        for (int i = 1; i < points.Length; i++)
        {
            min = min.Min(points[i]);
            max = max.Max(points[i]);
        }
        return (max - min) * 0.5f;
    }

    /// <summary>ImmediateMesh 的薄封装：第一条线时才真正 SurfaceBegin。</summary>
    private sealed class LineWriter
    {
        private readonly ImmediateMesh _mesh;
        private readonly Material? _material;
        private bool _begun;
        private List<string>? _diag;
        private string? _shapeSource;
        private string? _shapeKind;
        private int _shapeStartLines;

        public LineWriter(ImmediateMesh mesh, Material? material)
        {
            _mesh = mesh;
            _material = material;
        }

        public int Lines { get; private set; }

        public void EnableDiagnostics(List<string> diag) => _diag = diag;

        public void BeginShape(string source, Shape3D shape)
        {
            if (_diag is null)
                return;
            _shapeSource = source;
            _shapeKind = shape.GetType().Name;
            _shapeStartLines = Lines;
        }

        public void EndShape()
        {
            if (_diag is null || _shapeSource is null)
                return;
            _diag.Add($"    {_shapeSource}  {_shapeKind}  {Lines - _shapeStartLines} 线");
            _shapeSource = null;
        }

        public void Add(Vector3 from, Vector3 to, Color colour)
        {
            if (!_begun)
            {
                _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
                _begun = true;
            }

            _mesh.SurfaceSetColor(colour);
            _mesh.SurfaceAddVertex(from);
            _mesh.SurfaceSetColor(colour);
            _mesh.SurfaceAddVertex(to);
            Lines++;
        }

        public void Finish()
        {
            if (!_begun)
                return;
            _mesh.SurfaceEnd();
            _begun = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // UI 搭建与工具
    // ─────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        _panel = new PanelContainer
        {
            Name = "Panel",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(12, 12),
            Visible = false,
        };
        _panel.AddThemeStyleboxOverride("panel", BuildPanelStyle());

        _text = new RichTextLabel
        {
            Name = "Text",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(520, 0),
        };
        _text.AddThemeFontSizeOverride("normal_font_size", 12);
        _panel.AddChild(_text);
        AddChild(_panel);

        _hint = new Label
        {
            Name = "Hint",
            Text = "F1  调试面板",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 12,
            OffsetTop = -30,
            OffsetRight = 220,
            OffsetBottom = -8,
        };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        _hint.AddThemeColorOverride("font_color", new Color(0.62f, 0.70f, 0.80f, 0.75f));
        AddChild(_hint);

        _wire = new ImmediateMesh();
        _wireMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            DisableReceiveShadows = true,
            AlbedoColor = Colors.White,
        };
        _wireMesh = new MeshInstance3D
        {
            Name = "DebugOverlayWireframes",
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            Mesh = _wire,
        };

        _useBlockGlyphs = CanRenderBlockGlyphs();
    }

    private bool CanRenderBlockGlyphs()
    {
        try
        {
            Font? font = _text?.GetThemeDefaultFont();
            return font is not null && font.HasChar('█');
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static StyleBoxFlat BuildPanelStyle() => new()
    {
        BgColor = new Color(0.04f, 0.05f, 0.07f, 0.86f),
        BorderColor = new Color(0.35f, 0.45f, 0.60f, 0.90f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10,
        ContentMarginRight = 10,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    private static int NowFrame() => (int)Engine.GetPhysicsFrames();

    private static bool HasUserArg(string arg)
    {
        foreach (string candidate in OS.GetCmdlineUserArgs())
        {
            if (string.Equals(candidate, arg, StringComparison.Ordinal))
                return true;
        }
        foreach (string candidate in OS.GetCmdlineArgs())
        {
            if (string.Equals(candidate, arg, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>把 BBCode 剥成纯文本，给 F8 / --debug-panel-dump 的 console 输出用。</summary>
    private static string StripBbcode(string text)
        => Regex.Replace(text.Replace("[lb]", "[").Replace("[rb]", "]"), @"\[[^\]]*\]", string.Empty);
}
