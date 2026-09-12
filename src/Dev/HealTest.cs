using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 喝血端到端测试（04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/HealMidDrink.tscn
///
/// 它验证的是 T18 里**单测保证不了**的那一半：
/// 单测能证明"饮用段的帧号拒绝进入硬直"，证明不了
/// "刀真的在饮用段落到身上时，伤害照扣、动作照走"——
/// 这两件事之间隔着仲裁器、<c>ReceiveVerdict</c> 的结算顺序和状态机的延迟切换。
///
/// 三条断言缺一不可，它们就是"不背板但要付代价"这个设计的可执行证明：
///
/// 1. **回血生效了** —— 玩家确实被治好（不是白挨一顿打）
/// 2. **没被推进硬直** —— 动作没被打断（所以玩家不需要背招找安全窗口）
/// 3. **伤害照常扣了** —— 不是无敌（所以"什么时候喝"仍然是一个真实的决策）
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class HealTest : Node3D
{
    private const int MaxFrames = 900;

    /// <summary>
    /// 在"距判定帧还有 [min, max] 帧"这个窗口里按下喝血。
    ///
    /// 为什么必须是**有上下界的区间**，而不是一个字面上的"提前 N 帧"：
    /// <c>FramesUntilEnemyActive()</c> 在刀已经挥过之后会返回**负数**，
    /// 于是 "&lt;= N" 这种写法会把"上一刀的后摇里"也当成"快出刀了"命中，
    /// 结果是在假人收招时按下喝血，整段 54 帧都在冷却里度过，一刀都不挨。
    /// 第一版就是这么写的，无头跑出来是"饮用段挨打 0 次"。
    ///
    /// 数值范围：按下后玩家在**下一帧**才读到输入，所以刀落在喝血第 <c>f-1</c> 帧。
    /// 饮用段是第 10~43 帧，取 f ∈ [12, 24] → 落在第 11~23 帧，稳稳在饮用段里。
    /// </summary>
    [Export] public int HealLeadMinFrames { get; set; } = 12;

    [Export] public int HealLeadMaxFrames { get; set; } = 24;

    /// <summary>先掉到 MaxHealth 减去这个值以下再开始喝，留出足够的回复空间。</summary>
    [Export] public int WoundedMargin { get; set; } = 30;

    private readonly List<string> _failures = new();

    private EventBus? _bus;
    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;

    private bool _healTriggered;
    private bool _itemUseHeld;
    private int _itemUseHeldFrames;
    private bool _drinkObserved;
    private bool _staggeredDuringDrink;
    private int _damageDuringDrink;
    private int _hitsDuringDrink;
    private int _hpBeforeHeal;
    private HealState? _heal;

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

        Check(_attacker.Attack is not null, "挥砍假人没有配招式（Attack 为空），它永远不会出招");
        Check(_player.Stats is not null, "玩家没有配 ActorStats，喝血的帧数与回复量都读不到");
        Check(_player.HealChargesLeft > 0, "玩家进场时喝血次数为 0，RefillHealCharges 没有生效");

        _bus = EventBus.Instance;
        if (_bus is not null)
            _bus.HitResolved += OnHitResolved;

        GD.Print($"[喝血] 起始 HP {_player.Health.Current}/{_player.Health.Max}，" +
                 $"次数 {_player.HealChargesLeft}");

        for (int frame = 0; frame < MaxFrames && _heal is null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            TickItemUseKey();

            if (!_healTriggered)
            {
                TryStartHeal();
                continue;
            }

            ObserveHeal();
        }

        Report();
    }

    public override void _ExitTree()
    {
        if (_bus is not null)
            _bus.HitResolved -= OnHitResolved;
    }

    /// <summary>item_use 是 JustPressed 语义，按住 2 帧再松，避开"按下与读取落在同一帧"的边界。</summary>
    private void TickItemUseKey()
    {
        if (!_itemUseHeld)
            return;

        if (++_itemUseHeldFrames >= 2)
        {
            Input.ActionRelease("item_use");
            _itemUseHeld = false;
        }
    }

    /// <summary>先挨够打，然后在下一刀的判定帧到来前按下喝血。</summary>
    private void TryStartHeal()
    {
        // 还没掉够血就先挨着：回血被上限截断的话，"回复了"这条断言就测不出来了。
        if (_player.Health.Current > _player.Health.Max - WoundedMargin)
            return;

        int framesUntilActive = FramesUntilEnemyActive();
        if (framesUntilActive < HealLeadMinFrames || framesUntilActive > HealLeadMaxFrames)
            return;

        Input.ActionPress("item_use");
        _itemUseHeld = true;
        _itemUseHeldFrames = 0;

        _healTriggered = true;
        _hpBeforeHeal = _player.Health.Current;
    }

    /// <summary>喝血进行中逐帧采样：现在在不在饮用段、有没有被硬直顶掉。</summary>
    private void ObserveHeal()
    {
        if (_player.Machine.Current is HealState heal)
        {
            if (heal.Phase == HealPhase.Drink)
                _drinkObserved = true;

            // Completed 只在"喝完"那一帧为真，置上之后主循环就会退出。
            if (heal.Completed)
                _heal = heal;

            return;
        }

        // 没走完就离开了喝血状态 → 被硬直顶掉了（02 §2.4 承诺饮用段不会发生）。
        if (_drinkObserved && _player.Machine.Current is StaggerState)
            _staggeredDuringDrink = true;
    }

    private int FramesUntilEnemyActive()
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
            return int.MaxValue;

        return attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
    }

    /// <summary>
    /// 命中事件的采样点很关键：它由仲裁器在 <c>defender.ReceiveVerdict</c> **之后**抛出，
    /// 而状态切换是延迟到帧末的，所以此刻 <c>Machine.Current</c> 一定还是 HealState ——
    /// 正好能读出"这一刀落在哪一段"。
    /// </summary>
    private void OnHitResolved(HitEvent e)
    {
        if (e.DefenderId != _player.ActorId || e.Verdict != Verdict.Hit)
            return;

        if (_player.Machine.Current is HealState heal && heal.Phase == HealPhase.Drink)
        {
            _hitsDuringDrink++;
            _damageDuringDrink += e.Damage;
        }
    }

    private void Report()
    {
        GD.Print($"[喝血] 是否触发={_healTriggered}，是否进入过饮用段={_drinkObserved}，" +
                 $"饮用段挨打 {_hitsDuringDrink} 次（共 {_damageDuringDrink} 伤害）");

        if (_heal is null)
        {
            Check(false, $"在 {MaxFrames} 帧内没有走完一次喝血（触发={_healTriggered}，进入饮用段={_drinkObserved}）");
            Flush();
            return;
        }

        GD.Print($"[喝血] 喝血前 HP {_hpBeforeHeal} → 实际回复 {_heal.HealedAmount} → " +
                 $"结束时 HP {_player.Health.Current}/{_player.Health.Max}，" +
                 $"剩余次数 {_player.HealChargesLeft}");

        // ① 没被打断：走到了"喝完"那一帧。
        Check(_heal.Completed, "喝血没有走到收招段：饮用段被硬直打断了（02 §2.4 承诺不会）");
        Check(!_staggeredDuringDrink, "在饮用段被推进了 StaggerState：受击仍然打断了动作");

        // ② 回血生效：玩家确实被治好了。
        Check(_heal.HealedAmount > 0, "实际回复量为 0：喝血没有生效（或被血量上限完全截断）");

        // ③ 伤害照常结算：不是"喝血无敌"。
        Check(_hitsDuringDrink > 0,
            "饮用段一次都没挨打：这个测试没测到它该测的东西（时序没对上，检查 HealLeadFrames）");
        Check(_damageDuringDrink > 0, "饮用段挨打了但伤害为 0：喝血变成了无敌帧");

        // ④ 次数消耗：走到饮用段就要扣一次（02 §2.4）。
        Check(_player.HealChargesLeft == (_player.Stats?.HealCharges ?? 0) - 1,
            $"喝血后剩余次数是 {_player.HealChargesLeft}，应为 {( _player.Stats?.HealCharges ?? 0) - 1}");

        Flush();
    }

    private void Flush()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[喝血] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[喝血] ✓ 喝血测试通过（回复了 / 没打断 / 伤害照扣）");

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
