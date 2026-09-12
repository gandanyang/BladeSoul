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
/// 闪避端到端测试（04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/DodgeTraining.tscn
///     godot --headless --path . res://scenes/tests/DodgeTooEarly.tscn   （对照组）
///
/// 它验证的是 T13 里**单测保证不了**的那一半：
/// 单测能证明"第 0~7 帧的帧号是无敌的"，证明不了
/// "刀真的落下来时，玩家的无敌帧正好盖住它"——这两件事之间隔着
/// 输入采集、状态切换、判定框、仲裁器和物理帧顺序。
///
/// 三种结果，两种模式：
///
/// | 模式 | 机器人行为 | 期望 |
/// |---|---|---|
/// | 本场景（及时闪） | 判定帧前一帧按下闪避 | 躲掉 4 次判定 / 不掉血 / 拿到避一闪 buff |
/// | <c>DodgeTooEarly.tscn</c> | 提前 <c>TooEarlyLeadFrames</c> 帧按下 | 无敌帧在刀落下前结束 → 挨打 |
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class DodgeTrainingTest : Node3D
{
    private const int TotalFrames = 480;

    /// <summary>
    /// 判定帧前一帧按下闪避（<c>0</c> = 判定帧当帧）。
    ///
    /// 为什么是 1 而不是 0：测试在物理帧**结束后**按键，玩家要到**下一帧**才读得到。
    /// 所以看到"还差 1 帧进判定"时按下，正好让闪避第 0 帧落在判定帧上，
    /// 8 帧无敌完整覆盖 4 帧判定（02 §2.2 的 0~7 帧）。
    /// </summary>
    [Export] public int DodgeLeadFrames { get; set; } = 1;

    /// <summary>
    /// 对照组：提前这么多帧按下 —— 无敌帧（8 帧）会在刀落下**之前**结束。
    /// 12 帧是刻意取的：它比无敌帧长，又短到不会让整个闪避走完。
    /// </summary>
    [Export] public int TooEarlyLeadFrames { get; set; } = 12;

    /// <summary>对照组模式：提前太多帧闪避，期望挨打。</summary>
    [Export] public bool TooEarly { get; set; }

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

        // 关掉闪避位移：这个测试量的是**无敌帧**，不是"闪出攻击范围"。
        // 让玩家后跳会把距离拉到假人的 2.2m 攻击距离之外，假人就不出招了，
        // 测试会变成"因为够不着所以没挨打"，那是个假绿灯。
        _player.DodgeSpeedScale = 0f;
        AddChild(_player);

        _attacker = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        await WaitPhysicsFrames(10);

        Check(_attacker.Attack is not null, "挥砍假人没有配招式（Attack 为空），它永远不会出招");
        Check(_player.PrimaryHitbox is not null, "玩家的 Hitbox 没有挂上");

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        GD.Print($"[闪避训练] 模式 = {(TooEarly ? $"提前 {TooEarlyLeadFrames} 帧（对照组，期望挨打）" : $"判定帧前 {DodgeLeadFrames} 帧（期望躲开）")}");
        GD.Print($"[闪避训练] 无敌帧配置：{DescribeDodgeProfile()}");

        bool dodgeHeld = false;
        int heldFrames = 0;
        int perfectDodgeGrants = 0;
        int maxBuffFramesLeft = 0;

        for (int frame = 0; frame < TotalFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            // 采样「避一闪」buff。授予发生在仲裁器里（本帧物理的最后一环），
            // 而 TickTimers 要到**下一帧**才扣，所以授予那一帧读到的就是 14。
            if (_player.IssenBuff == IssenKind.Dodge)
            {
                maxBuffFramesLeft = Mathf.Max(maxBuffFramesLeft, _player.IssenBuffFramesLeft);

                if (_player.IssenBuffFramesLeft == DodgeState.PerfectDodgeIssenFrames)
                    perfectDodgeGrants++;
            }

            int framesUntilActive = FramesUntilEnemyActive();
            int lead = TooEarly ? TooEarlyLeadFrames : DodgeLeadFrames;

            if (!dodgeHeld && framesUntilActive <= lead)
            {
                Input.ActionPress("dodge");
                dodgeHeld = true;
                heldFrames = 0;
            }
            else if (dodgeHeld && ++heldFrames >= 2)
            {
                // 松开：dodge 是 JustPressed 语义，按住不会重复触发，
                // 但留一帧再松开可以避开"按下与读取落在同一帧"的边界。
                Input.ActionRelease("dodge");
                dodgeHeld = false;
            }
        }

        Input.ActionRelease("dodge");

        ReportAttacks();
        ReportDodge(perfectDodgeGrants, maxBuffFramesLeft);
        Report();
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
    }

    private string DescribeDodgeProfile()
    {
        DifficultyProfile? difficulty = _player.Difficulty;
        if (difficulty is null)
            return "（玩家没有配难度档，退化到 0 帧无敌）";

        return $"{difficulty.DisplayName}：无敌 {difficulty.DodgeIFrames} 帧 / " +
               $"后摇 {difficulty.DodgeRecoveryFrames} 帧 / 宽容 {difficulty.PerfectDodgeGraceFrames} 帧";
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
        if (e.DefenderId == _player.ActorId)
            _contacts.Add(e);
    }

    /// <summary>T7 证据的复用：确认假人确实在按节奏出招，否则下面的数字没有意义。</summary>
    private void ReportAttacks()
    {
        IReadOnlyList<int> frames = _attacker.AttackFrames;

        Check(frames.Count >= 3, $"{TotalFrames} 帧内只出招 {frames.Count} 次：假人没有在按节奏出招");

        GD.Print($"[闪避训练] 假人出招 {frames.Count} 次，帧号 {string.Join(" / ", frames)}");
    }

    private void ReportDodge(int perfectDodgeGrants, int maxBuffFramesLeft)
    {
        int hits = 0;
        foreach (HitEvent e in _contacts)
        {
            if (e.Verdict == Verdict.Hit)
                hits++;
        }

        GD.Print($"[闪避训练] 玩家被打中 {hits} 次；躲开（Miss）{_player.EvadedAttackCount} 次；" +
                 $"完美闪避 {perfectDodgeGrants} 次");
        GD.Print($"[闪避训练] 玩家 HP {_player.Health.Current}/{_player.Health.Max}，" +
                 $"避一闪 buff 最长观察到 {maxBuffFramesLeft} 帧（应为 {DodgeState.PerfectDodgeIssenFrames}）");

        if (TooEarly)
        {
            // 对照组：闪早了，无敌帧在刀落下前就结束了 → 必须挨打。
            Check(hits > 0, $"提前 {TooEarlyLeadFrames} 帧闪避却一次都没挨打：无敌帧比难度档给的更长");
            Check(_player.EvadedAttackCount == 0, $"提前闪避仍躲开了 {_player.EvadedAttackCount} 次：无敌帧比难度档给的更长");
            Check(perfectDodgeGrants == 0, "提前闪避也拿到了避一闪 buff：完美闪避判定窗过宽");
            return;
        }

        // 及时闪：8 帧无敌盖住 4 帧判定 → 全部 Miss，一刀都不该挨。
        Check(_player.EvadedAttackCount > 0, "在无敌帧内闪避却一次都没躲开：无敌帧没有接进裁决器规则 1");
        Check(hits == 0, $"无敌帧内闪避仍挨了 {hits} 次打：无敌帧没有盖住判定帧");
        Check(_player.Health.Current == _player.Health.Max, "玩家掉了血：无敌帧没有兜住伤害（04 §14 红线）");
        Check(perfectDodgeGrants > 0, "躲开了攻击却没拿到避一闪 buff：完美闪避的奖励闭环没接上");
        Check(maxBuffFramesLeft == DodgeState.PerfectDodgeIssenFrames,
            $"避一闪 buff 最长只观察到 {maxBuffFramesLeft} 帧，应为 {DodgeState.PerfectDodgeIssenFrames} 帧");
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
            GD.PrintErr($"[闪避训练] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print(TooEarly ? "[闪避训练] ✓ 对照组通过（闪早了确实会挨打）" : "[闪避训练] ✓ 闪避训练通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
