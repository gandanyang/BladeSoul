using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 「危」攻击预警 + 弹开职责边界的对照实验（T12）。
///     godot --headless --path . res://scenes/tests/PerilousGuard.tscn    （横斩，一般攻击）
///     godot --headless --path . res://scenes/tests/PerilousThrust.tscn   （危·突刺）
///
/// 这一对场景是 02 §3 那条裁定的**可执行证明**：
///
/// | 靶子 | 机器人行为 | 期望 |
/// |---|---|---|
/// | 横斩假人（一般） | 每次都在弹开窗内按防御 | 弹开 4 / 挨打 0 |
/// | 枪兵（危·突刺） | 同样在弹开窗内按防御 | 弹开 0 / 格挡 N / 挨打 0 |
///
/// 第二行同时证明两件事：**弹开对危攻击无效**，**但格挡仍然保命**——
/// 危攻击不是"没法应对"，它只是换了一条应对路径（看破或闪避，见 02 §3 与 T13）。
///
/// 两个模式共用同一套机器人代码、同一段帧数、同一个距离、同一个攻击间隔，
/// 唯一的差别是靶子指向的 <c>AttackData</c>，所以两行数字可以直接对比，
/// 而不是两次不同条件下的独立测量。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class PerilousCueTest : Node3D
{
    private const int TotalFrames = 480;

    /// <summary>在判定帧到来前几帧按下防御（与 DeflectTrainingTest 同一套机器人）。</summary>
    private const int GuardLeadFrames = 6;

    /// <summary>true = 用枪兵（危·突刺）；false = 用挥砍假人（一般攻击）。</summary>
    [Export] public bool Spear { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<HitEvent> _contacts = new();

    private EventBus? _bus;
    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;
    private PerilousCue? _cue;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        string path = Spear
            ? "res://scenes/actors/SpearDummy.tscn"
            : "res://scenes/actors/AttackingDummy.tscn";

        _attacker = Load<AttackingDummy>(path);
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        await WaitPhysicsFrames(10);

        Check(_attacker.Attack is not null, "靶子没有配招式（Attack 为空），它永远不会出招");
        Check(_player.PrimaryHitbox is not null, "玩家的 Hitbox 没有挂上");

        _cue = _attacker.GetNodeOrNull<PerilousCue>("PerilousCue");
        Check(_cue is not null, "战斗单位身上没有 PerilousCue：基类的「危」预警没有生效");

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        GD.Print($"[危预警] 靶子 = {(Spear ? "枪兵（危·突刺）" : "横斩假人（一般攻击）")}");

        bool guardHeld = false;

        for (int frame = 0; frame < TotalFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            int framesUntilActive = FramesUntilEnemyActive();

            if (!guardHeld && framesUntilActive <= GuardLeadFrames)
            {
                Input.ActionPress("guard");
                guardHeld = true;
            }
            else if (guardHeld && _attacker.Machine.Current is not AttackState)
            {
                Input.ActionRelease("guard");
                guardHeld = false;
            }
        }

        Input.ActionRelease("guard");

        ReportAttacks();
        ReportContacts();
        ReportCue();
        Report();
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
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
            _contacts.Add(e);
    }

    private void ReportAttacks()
    {
        IReadOnlyList<int> frames = _attacker.AttackFrames;
        Check(frames.Count >= 3, $"{TotalFrames} 帧内只出招 {frames.Count} 次：靶子没有在按节奏出招");

        GD.Print($"[危预警] 靶子出招 {frames.Count} 次，帧号 {string.Join(" / ", frames)}");
    }

    private void ReportContacts()
    {
        int deflects = 0;
        int blocks = 0;
        int hits = 0;
        int clashes = 0;

        foreach (HitEvent e in _contacts)
        {
            switch (e.Verdict)
            {
                case Verdict.Deflect: deflects++; break;
                case Verdict.Block: blocks++; break;
                case Verdict.Hit: hits++; break;
                case Verdict.Clash: clashes++; break;
            }
        }

        GD.Print($"[危预警] 交战 {_contacts.Count} 次：弹开 {deflects} / 格挡 {blocks} / 拼刀 {clashes} / 挨打 {hits}");
        GD.Print($"[危预警] 玩家 HP {_player.Health.Current}/{_player.Health.Max}，" +
                 $"体干 {_player.Posture.Current}/{_player.Posture.Max}");

        Check(_contacts.Count > 0, "靶子出招了但一次都没碰到玩家：判定框/距离/层掩码有问题");

        if (Spear)
        {
            Check(deflects == 0,
                $"危·突刺被弹开了 {deflects} 次：02 §3 的裁定没有落到数据上（该招 Parryable 应为 false）");
            Check(blocks > 0, "危·突刺一次都没被格挡：格挡这条保命路径断了");
            Check(hits == 0,
                $"全程按住防御还挨了 {hits} 次打：危攻击应当是「弹不开但挡得住」，不是无法应对");
        }
        else
        {
            Check(deflects > 0, "一般攻击在窗口内按防御却没弹开：弹开窗没接上裁决器");
            Check(hits == 0, $"全程按住防御还挨了 {hits} 次打：格挡没有生效");
        }
    }

    private void ReportCue()
    {
        if (_cue is null)
            return;

        GD.Print($"[危预警] 预警次数 {_cue.ShowCount}，最后形态 {_cue.LastKind}");

        if (Spear)
        {
            Check(_cue.ShowCount > 0, "枪兵打的是危攻击，却没有弹过预警：Perilous 分支没走到");
            Check(_cue.LastKind == PerilousKind.Thrust,
                $"最后记录的形态是 {_cue.LastKind}，应为 Thrust");
        }
        else
        {
            Check(_cue.ShowCount == 0, $"一般攻击不该弹预警，却弹了 {_cue.ShowCount} 次");
        }
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

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[危预警] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print($"[危预警] ✓ {(Spear ? "危·突刺对照" : "一般攻击基线")}通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
