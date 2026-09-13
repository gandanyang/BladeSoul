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

    private readonly TutorialLogic _logic = new();
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
            DialogueSetCount = DialogueCatalogue.Load(DialoguePath).Count;
        else
            GD.PrintErr($"[教学] 找不到台词表：{DialoguePath}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _player ??= FindPlayer();
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
