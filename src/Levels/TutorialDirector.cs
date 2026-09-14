using Godot;
using Oniblade.Combat.States;
using Oniblade.Dialogue;
using Oniblade.Player;

namespace Oniblade.Levels;

/// <summary>
/// 教学第一幕《收剑》的导演（T47 / [docs/15](../../docs/15-开场教学流程.md)）。
///
/// 它观测玩家、推进三段（**一次只教一个机制**），并在玩家**卡住两次**之后
/// 才给一次"师父的声音"（05 §5 铁律）。规则在 <see cref="TutorialLogic"/> 里（纯逻辑、有单测）；
/// 这里只做三件事：**量**、**喂**、**报**。
///
/// **观测方式刻意零侵入**：轻攻击数的是"状态机**进入** AttackState 的上升沿"，
/// 蓄力斩读 `PlayerActor.ChargedCount`——都不需要往 `PlayerActor` 里塞计数器。
/// （写这段时 `PlayerActor.cs` 正被 T38 改着，照 AGENTS.md §5 不该碰它。）
///
/// 数值是 `[Export]`（改它零编译）；**台词在 `data/dialogue/tutorial_act1.json`**，
/// 一字不改照抄 03 §4.0。
///
/// ⚠️ 与 `DialogueBox` 的**显示接线还没做**：那个盒子自己持有一份台词表（`ayame.json`），
/// 要播这一份得先让它能接外部台词表。本卡先保证**规则与数据**正确（可被断言），
/// 显示接线单独一步——否则改 `DialogueBox` 会连累已经通过的那几项对话自检。
/// </summary>
public partial class TutorialDirector : Node3D
{
    [ExportGroup("三段的目标")]
    [Export] public float MoveDistance { get; set; } = 6f;
    [Export] public int LightAttackTarget { get; set; } = 3;
    [Export] public int ChargedAttackTarget { get; set; } = 1;

    [ExportGroup("提示纪律")]
    [Export] public int StuckFramesPerFailure { get; set; } = 300;
    [Export] public int HintsAfterFailures { get; set; } = 2;

    [ExportGroup("台词")]
    [Export] public string DialoguePath { get; set; } = "res://data/dialogue/tutorial_act1.json";

    [ExportGroup("收刀")]
    /// <summary>收刀时镜头停几秒（docs/15：「玩家按键→刀入鞘→镜头停半秒」）。0 ＝ 不停。</summary>
    [Export] public double SheathePauseSeconds { get; set; } = 0.5;

    /// <summary>要用的对话框（在关卡场景里指过来）。留空＝只跑规则不显示，测试就是这么跑的。</summary>
    [Export] public DialogueBox? Box { get; set; }
    [Export] public string MoveHint { get; set; } = "站着不动，剑不会自己走。";
    [Export] public string LightHint { get; set; } = "三下。连着来。";
    [Export] public string ChargedHint { get; set; } = "按住。别急着松。";

    public TutorialBeat Beat => _logic.Beat;
    public bool Completed => _logic.Beat == TutorialBeat.Done;

    /// <summary>已经过了几段。</summary>
    public int BeatsPassed { get; private set; }

    /// <summary>提示播过几次 / 最近一次是什么（验收读这三个）。</summary>
    public int HintRequests { get; private set; }
    public string LastHint { get; private set; } = "";
    public TutorialBeat LastHintBeat { get; private set; } = TutorialBeat.Move;

    /// <summary>台词表读进来几组（说明那份 JSON 有效）。</summary>
    public int DialogueSetCount { get; private set; }

    /// <summary>玩家做过「收刀」（第三幕会回收这个事实）。</summary>
    public bool Sheathed { get; private set; }

    private readonly TutorialLogic _logic = new();
    private DialogueCatalogue? _lines;
    private PlayerActor? _player;
    private Vector3 _origin;
    private float _travelled;
    private int _lightSwingCount;
    private bool _wasAttacking;
    private int _chargedAtStart;

    public override void _Ready()
    {
        _logic.StuckFramesPerFailure = StuckFramesPerFailure;
        _logic.HintsAfterFailures = HintsAfterFailures;

        if (ResourceLoader.Exists(DialoguePath))
        {
            _lines = DialogueCatalogue.Load(DialoguePath);
            DialogueSetCount = _lines.Count;
        }
        else
            GD.PrintErr($"[教学] 找不到台词表：{DialoguePath}");
    }

    public override void _PhysicsProcess(double delta)
    {
        // 收刀（docs/15 第二子段「收刀（交互）」）：任何时刻按下 interact 都算，
        // 第一次会给「镜头停半秒」——这是教玩家「收刀也是一个动作」，第三幕会回收。
        if (Input.IsActionJustPressed("interact"))
            NotifySheathe();

        // 重查条件**不能只判 null**。
        //
        // Godot 节点被释放后，C# 侧的包装对象**仍然非 null**，只是内部指针失效——
        // 于是 `??=` 永远不会重查，`_player.GlobalPosition` 每物理帧抛一次
        // ObjectDisposedException（实测：check.ps1 第 22 步刷 755 次 / 21159 行日志）。
        // `IsInstanceValid` 才是"这个引擎对象还活着吗"的正确问法。
        if (!GodotObject.IsInstanceValid(_player))
            _player = FindPlayer();

        if (_player is not PlayerActor actor)
            return;

        if (_origin == Vector3.Zero)
        {
            _origin = actor.GlobalPosition;
            _chargedAtStart = actor.ChargedCount;
        }

        // 轻攻击：数"进入攻击状态"的**上升沿**（一次挥砍算一次，连段里每一段都算）。
        bool attacking = actor.Machine.Current is AttackState;
        if (attacking && !_wasAttacking)
            _lightSwingCount++;
        _wasAttacking = attacking;

        // 累计水平位移：只算平面（跳起来不算"走得更远"）。
        Vector3 flat = actor.GlobalPosition - _origin;
        _travelled = Mathf.Max(_travelled, new Vector2(flat.X, flat.Z).Length());

        bool done = _logic.Beat switch
        {
            TutorialBeat.Move => _travelled >= MoveDistance,
            TutorialBeat.LightAttack => _lightSwingCount >= LightAttackTarget,
            TutorialBeat.ChargedAttack => actor.ChargedCount - _chargedAtStart >= ChargedAttackTarget,
            _ => false,
        };

        TutorialBeat before = _logic.Beat;

        if (_logic.NotifyProgress(done))
        {
            BeatsPassed++;
            GD.Print($"[教学] 第 {BeatsPassed} 段过了（{before}）→ 现在教 {_logic.Beat}");

            // 过一段就播那一段的师父台词（叙事，不是奖励——所以它跟着剧情节拍走）。
            ShowSet(before switch
            {
                TutorialBeat.Move => "act1_lesson_1",
                TutorialBeat.LightAttack => "act1_lesson_2",
                _ => "act1_lesson_3",
            });
            return;
        }

        if (_logic.ShouldShowHint)
        {
            ShowHint();
            _logic.MarkHintShown();
        }
    }

    private void ShowHint()
    {
        HintRequests++;
        LastHintBeat = _logic.Beat;
        LastHint = _logic.Beat switch
        {
            TutorialBeat.Move => MoveHint,
            TutorialBeat.LightAttack => LightHint,
            _ => ChargedHint,
        };

        GD.Print($"[教学] 师父的声音（{_logic.Beat}，已失败 {_logic.FailureCount} 次）：{LastHint}");
    }

    /// <summary>
    /// 收刀：记录事实 ＋ 「镜头停半秒」（docs/15）。镜头停顿用 <c>Engine.TimeScale = 0</c>，
    /// 恢复计时走 <c>ignoreTimeScale</c> 的计时器（物理帧在冻结期间不会走）。
    /// 拆成公共方法是为了端到端测试可以直接触发（不依赖真实按键）。
    /// </summary>
    public void NotifySheathe()
    {
        if (Sheathed)
            return;

        Sheathed = true;
        if (SheathePauseSeconds <= 0)
            return;

        Engine.TimeScale = 0.0;
        GD.Print($"[教学] 收刀。镜头停 {SheathePauseSeconds:0.##} 秒。");
        var timer = GetTree().CreateTimer(SheathePauseSeconds,
            processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        timer.Timeout += () => Engine.TimeScale = 1.0;
    }

    /// <summary>播一组教学台词——走 <see cref="DialogueBox.ShowExternal"/>，不换盒子自己的常驻台词表。</summary>
    private void ShowSet(string setId)
    {
        if (Box is null || _lines is null)
            return;

        if (Box.ShowExternal(_lines, setId))
            GD.Print($"[教学] 台词组 {setId} 已交给对话框");
    }

    private PlayerActor? FindPlayer()
    {
        foreach (Node node in GetTree().GetNodesInGroup("player"))
        {
            if (node is PlayerActor actor)
                return actor;
        }

        return null;
    }
}
