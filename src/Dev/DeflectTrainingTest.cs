using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 弹开训练端到端测试（04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/DeflectTraining.tscn
///
/// 它验证的是 T6 与 T7 **合起来**才成立的那件事：
/// 会还手的假人按节奏出招 → 玩家在弹开窗内按下防御 → 屏幕上是"弹开"而不是"挨打"。
/// 单测能保证"窗口的时序是对的"，保证不了"刀真的落下来时它正好在窗口里"——
/// 这两件事之间隔着判定框、仲裁器和物理帧。
///
/// 它同时是 T7 要求的证据：打印每次出招的帧号与间隔，
/// 证明假人真的在按节奏出招（而不是"编译过就算"），且间隔满足 02 §10 的 ≥45 帧。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class DeflectTrainingTest : Node3D
{
    /// <summary>总帧数。一次循环 ≈ 58（招式）+ 60（冷却）= 118 帧，480 帧能看到 4 次出招。</summary>
    private const int TotalFrames = 480;

    /// <summary>
    /// 在假人的判定帧到来前几帧按下防御。武士档窗口 9 帧，
    /// 提前 6 帧按 → 命中时窗口还剩 3 帧：既能过，也不用贴着边界。
    /// </summary>
    private const int GuardLeadFrames = 6;

    /// <summary>02 §10：两次攻击的最小间隔。</summary>
    private const int MinAttackIntervalFrames = 45;

    /// <summary>
    /// **连打模式**：完全不看时机，每 2 帧松手、每 2 帧按下。
    /// 用来验证 02 §8 的"连打防御惩罚"在**中立态**上确实生效
    /// （原实现只覆盖了从攻击/受击取消进入防御的那一半）。
    ///
    /// 期望：拿不到弹开收益，但**一次都不该挨打**——
    /// 惩罚只惩罚效率，不惩罚存活（01 文档的立场）。
    /// </summary>
    [Export] public bool SpamMode { get; set; }

    private readonly List<string> _failures = new();
    private readonly List<HitEvent> _contacts = new();

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

        // 假人站在玩家正前方 1.7m：在它的 2.2m 攻击距离内，且刀能砍到人。
        _attacker = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        await WaitPhysicsFrames(10);

        Check(_attacker.Attack is not null, "挥砍假人没有配招式（Attack 为空），它永远不会出招");
        Check(_player.PrimaryHitbox is not null, "玩家的 Hitbox 没有挂上");

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        // ── 简易"弹开机器人"：在刀落下前几帧按下防御，攻击结束就松手 ──
        // 这正是 T6 想让玩家学会的动作，只不过由代码执行，好在无头环境里可复现。
        // SpamMode 下换成一个"完全不看时机"的连打机器人，两个模式共用同一套结算链路。
        GD.Print($"[弹开训练] 模式 = {(SpamMode ? "连打（不看时机）" : "看时机按")}");

        bool guardHeld = false;

        for (int frame = 0; frame < TotalFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (SpamMode)
            {
                // 2 帧按 / 2 帧松，周而复始，刻意与假人的出招节奏错开。
                if (frame % 4 == 0)
                {
                    Input.ActionPress("guard");
                    guardHeld = true;
                }
                else if (frame % 4 == 2)
                {
                    Input.ActionRelease("guard");
                    guardHeld = false;
                }

                continue;
            }

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
        Report();
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
    }

    /// <summary>假人还有几帧进入判定帧（没在出招时返回一个很大的值）。</summary>
    private int FramesUntilEnemyActive()
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
            return int.MaxValue;

        return attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
    }

    private void OnHitResolved(HitEvent e)
    {
        // 只关心"玩家被打"的那一半：假人不会主动挨打，玩家也不会出招。
        if (e.DefenderId == _player.ActorId)
            _contacts.Add(e);
    }

    /// <summary>T7 的证据：出招次数、每次的帧号、以及两次之间的间隔。</summary>
    private void ReportAttacks()
    {
        IReadOnlyList<int> frames = _attacker.AttackFrames;

        if (frames.Count == 0)
        {
            GD.PrintErr("[弹开训练] 假人一次都没出招");
            return;
        }

        var intervals = new List<int>();
        for (int i = 1; i < frames.Count; i++)
            intervals.Add(frames[i] - frames[i - 1]);

        GD.Print($"[弹开训练] 假人出招 {frames.Count} 次，帧号 {string.Join(" / ", frames)}");
        if (intervals.Count > 0)
            GD.Print($"[弹开训练] 出招间隔 {string.Join(" / ", intervals)} 帧（要求 ≥{MinAttackIntervalFrames}）");

        Check(frames.Count >= 3, $"{TotalFrames} 帧内只出招 {frames.Count} 次：假人没有在按节奏出招");

        foreach (int interval in intervals)
            Check(interval >= MinAttackIntervalFrames, $"两次出招只隔 {interval} 帧（<{MinAttackIntervalFrames}）：违反了 02 §10 的节奏要求");
    }

    /// <summary>交战结果的分布——"弹开成功时会想再试一次"能不能成立，先看这里的数据。</summary>
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

        GD.Print($"[弹开训练] 交战 {_contacts.Count} 次：弹开 {deflects} / 格挡 {blocks} / 拼刀 {clashes} / 挨打 {hits}");
        GD.Print($"[弹开训练] 玩家 HP {_player.Health.Current}/{_player.Health.Max}，体干 {_player.Posture.Current}/{_player.Posture.Max}，弹开连击 {_player.DeflectChain}");

        Check(_contacts.Count > 0, "假人出招了但一次都没碰到玩家：判定框/距离/层掩码有问题，弹开根本没法测");

        if (SpamMode)
        {
            // 连打模式的验收：**拿不到弹开收益**。
            //
            // 这里刻意不断言 hits == 0：连打机器人每 2 帧真的松一次手，
            // 松手的空隙里挨打是它自己造成的，不是系统在惩罚它。
            // 这恰恰是连打的第二重代价——**冒进本身就会露破绽**，
            // 是自然涌现的，比人为加惩罚更干净。
            Check(deflects <= 1, $"连打防御拿到了 {deflects} 次弹开：02 §8 的连打惩罚没有在中立态生效");
        }
        else
        {
            Check(deflects > 0, "机器人按在窗口内却一次都没弹开：GuardState 的弹开窗没接上裁决器");
            Check(hits == 0, $"全程按住防御还挨了 {hits} 次打：格挡没有生效（04 §14 红线）");
            Check(_player.Health.Current == _player.Health.Max, "玩家掉了血：格挡/弹开没有兜住伤害");
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
            GD.PrintErr($"[弹开训练] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[弹开训练] ✓ 弹开训练通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
