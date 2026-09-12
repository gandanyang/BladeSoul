using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Vfx;

namespace Oniblade.Dev;

/// <summary>
/// 战斗特效层验证（T28）：
///     godot --headless --path . res://scenes/tests/CombatVfx.tscn
///
/// 卡片要求的"四种特效各一张截图"我做不到（无渲染输出），但卡片里那几条
/// **可验证的硬指标**在这里被逐条断言了，而且比截图更严：
///
/// 1. 四种 Verdict 各自生成对应的特效；
/// 2. **弹开火花 ≤8 帧内消失**——这是 10 §4 的第一原则（"不能盖住判定"），
///    截图只能看出"看起来挺短"，这里数的是帧；
/// 3. 一闪的全屏闪在 6 帧（0.1s）内消失；
/// 4. **特效层不碰战斗逻辑**：灌一个 Damage=999 的 Hit 事件，场上不许掉血；
/// 5. 降级顺序是"先砍雾、再砍粒子"，全屏闪不参与降级。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class CombatVfxTest : Node3D
{
    private readonly List<string> _failures = new();

    private CombatVfxDirector _director = null!;
    private TrainingDummy _dummy = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        // 场上放一个真的战斗单位：用来证明"特效层不碰战斗逻辑"。
        _dummy = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        _dummy.Position = new Vector3(0f, 0.1f, -2f);
        AddChild(_dummy);

        await WaitPhysicsFrames(10);

        if (CombatVfxDirector.Instance is not { } director)
        {
            GD.PrintErr("[特效] ✗ CombatVfxDirector 没有注册成 autoload");
            GetTree().Quit(1);
            return;
        }

        _director = director;
        _director.ForceDegradationLevel(0);

        // 任何一步抛异常都必须走到 Report/Quit。
        // 否则 async void 会静默中断，场景永不退出——CI 里表现为"挂住"，
        // 而不是"失败"。（第一版就是这么挂的：无头下自动降级把粒子砍了，
        // FirstLive 返回 null，随后的打印 NRE，进程一直跑到超时。）
        try
        {
            await CheckDeflectSpark();
            await CheckClashSpark();
            await CheckBloodMist();
            await CheckIssenFlash();
            await CheckCombatUntouched();
            await CheckDegradation();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        _director.ResumeAutoDegradation();

        Report();
    }

    // ── 四种特效 ───────────────────────────────────────────────

    private async System.Threading.Tasks.Task CheckDeflectSpark()
    {
        int before = _director.DeflectSparkCount;
        Raise(Verdict.Deflect);

        Check(_director.DeflectSparkCount == before + 1, "弹开没有生成火花");
        Check(Live<SparkBurst>() > 0, "弹开火花不在场景里");

        SparkBurst? spark = FirstLive<SparkBurst>();
        Check(spark is not null && !spark.IsClash, "弹开的火花被当成了拼刀火花");

        // 10 §4 的第一原则：≤8 帧。
        Check(spark!.LifetimeFrames <= SparkBurst.DeflectLifetimeFrames,
            $"弹开火花寿命 {spark.LifetimeFrames} 帧，超过 {SparkBurst.DeflectLifetimeFrames}");

        await WaitPhysicsFrames(SparkBurst.DeflectLifetimeFrames + 1);

        Check(Live<SparkBurst>() == 0,
            $"弹开火花 {SparkBurst.DeflectLifetimeFrames + 1} 帧后仍在场景里：它盖住了判定");

        GD.Print($"[特效] 弹开火花：{spark?.LifetimeFrames ?? -1} 帧内消失（要求 ≤{SparkBurst.DeflectLifetimeFrames}）");
    }

    private async System.Threading.Tasks.Task CheckClashSpark()
    {
        int before = _director.ClashSparkCount;
        Raise(Verdict.Clash);

        Check(_director.ClashSparkCount == before + 1, "拼刀没有生成火花");

        SparkBurst? spark = FirstLive<SparkBurst>();
        Check(spark is not null && spark.IsClash, "拼刀火花没有标记成 clash（会做得和弹开一样小）");

        await WaitPhysicsFrames(SparkBurst.ClashLifetimeFrames + 1);

        Check(Live<SparkBurst>() == 0, "拼刀火花没有自行消失");

        GD.Print($"[特效] 拼刀火花：{spark?.LifetimeFrames ?? -1} 帧内消失");
    }

    private async System.Threading.Tasks.Task CheckBloodMist()
    {
        int before = _director.BloodMistCount;
        Raise(Verdict.Hit, damage: 12);

        Check(_director.BloodMistCount == before + 1, "命中没有生成血雾");
        Check(Live<BloodMist>() > 0, "血雾不在场景里");

        await WaitPhysicsFrames(BloodMist.LifetimeFrames + 1);

        Check(Live<BloodMist>() == 0, "血雾没有自行消失");

        GD.Print($"[特效] 命中血雾：{BloodMist.LifetimeFrames} 帧内消失");
    }

    private async System.Threading.Tasks.Task CheckIssenFlash()
    {
        int flashBefore = _director.ScreenFlashCount;
        int mistBefore = _director.BloodMistCount;

        Raise(Verdict.Issen);

        Check(_director.ScreenFlashCount == flashBefore + 1, "一闪没有触发全屏闪");
        Check(Live<ScreenFlash>() > 0, "全屏闪不在场景里");
        Check(_director.BloodMistCount == mistBefore + 1, "一闪没有同时生成血雾（10 §4 要求两者都有）");

        ScreenFlash? flash = FirstLive<ScreenFlash>();
        Check(flash is not null && flash.LifetimeFrames == ScreenFlash.FlashFrames,
            $"全屏闪时长 {flash?.LifetimeFrames ?? -1} 帧，应为 {ScreenFlash.FlashFrames}");

        await WaitPhysicsFrames(ScreenFlash.FlashFrames + 1);

        Check(Live<ScreenFlash>() == 0,
            $"全屏闪 {ScreenFlash.FlashFrames + 1} 帧后仍在屏幕上：它会糊住玩家的视野");

        GD.Print($"[特效] 一闪：全屏闪 {flash?.LifetimeFrames ?? -1} 帧（0.1s）+ 血雾");
    }

    // ── 特效层不许碰战斗 ───────────────────────────────────────

    private async System.Threading.Tasks.Task CheckCombatUntouched()
    {
        int hpBefore = _dummy.Health.Current;
        int postureBefore = _dummy.Posture.Current;

        Raise(Verdict.Hit, damage: 999);

        await WaitPhysicsFrames(2);

        Check(_dummy.Health.Current == hpBefore,
            $"特效层把 {hpBefore - _dummy.Health.Current} 点伤害算到了单位头上：它越界了");
        Check(_dummy.Posture.Current == postureBefore, "特效层改了体干：它越界了");

        GD.Print($"[特效] 边界检查：灌入 Damage=999 的命中事件后，单位 HP 仍是 {_dummy.Health.Current}/{_dummy.Health.Max}");
    }

    // ── 降级顺序 ───────────────────────────────────────────────

    private async System.Threading.Tasks.Task CheckDegradation()
    {
        // 等级 1：砍雾，粒子还在。
        _director.ForceDegradationLevel(1);

        int mistBefore = _director.BloodMistCount;
        int sparkBefore = _director.DeflectSparkCount;

        Raise(Verdict.Hit);
        Raise(Verdict.Deflect);

        Check(_director.BloodMistCount == mistBefore, "降级等级 1 没有砍掉血雾（顺序应先砍雾）");
        Check(_director.DeflectSparkCount == sparkBefore + 1, "降级等级 1 把粒子也砍了（顺序错了，应该先砍雾）");

        // 等级 2：再砍粒子，但全屏闪必须保留。
        _director.ForceDegradationLevel(2);
        await WaitPhysicsFrames(SparkBurst.DeflectLifetimeFrames + 2);

        int sparkBefore2 = _director.DeflectSparkCount;
        int flashBefore = _director.ScreenFlashCount;

        Raise(Verdict.Deflect);
        Raise(Verdict.Issen);

        Check(_director.DeflectSparkCount == sparkBefore2, "降级等级 2 没有砍掉粒子");
        Check(_director.ScreenFlashCount == flashBefore + 1,
            "降级等级 2 把一闪的全屏闪也砍了：它是最核心的反馈，不参与降级");

        GD.Print($"[特效] 降级顺序：等级1 砍雾 / 等级2 砍粒子 / 全屏闪始终保留（累计跳过 {_director.SkippedByDegradationCount} 次）");

        _director.ForceDegradationLevel(0);
    }

    // ── 工具 ───────────────────────────────────────────────────

    private static void Raise(Verdict verdict, int damage = 0)
    {
        EventBus.Instance?.RaiseHitResolved(new HitEvent
        {
            AttackerId = 1,
            DefenderId = 2,
            Verdict = verdict,
            AttackId = "vfx_test",
            IssenKind = IssenKind.None,
            Damage = damage,
            PostureDamage = 0,
            HitStopFrames = 0,
            Frame = (int)Engine.GetPhysicsFrames(),
            Killed = false,
            Position = new Vector3(0f, 1.1f, -2f),
            Direction = Vector3.Forward,
        });
    }

    /// <summary>场上还活着的同类特效数量（<c>QueueFree</c> 之后就不再算数）。</summary>
    private int Live<T>() where T : Node
    {
        int count = 0;

        foreach (Node child in _director.GetChildren())
        {
            if (child is T && !child.IsQueuedForDeletion())
                count++;
        }

        return count;
    }

    private T? FirstLive<T>() where T : Node
    {
        foreach (Node child in _director.GetChildren())
        {
            if (child is T typed && !child.IsQueuedForDeletion())
                return typed;
        }

        return null;
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
            GD.PrintErr($"[特效] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[特效] ✓ 四种特效、寿命上限、战斗边界、降级顺序全部通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
