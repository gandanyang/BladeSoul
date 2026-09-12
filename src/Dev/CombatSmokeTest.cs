using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 端到端冒烟测试（04 文档 §14 第 2 层）：
///     godot --headless --path . res://scenes/tests/CombatSmoke.tscn
///
/// 它验证的是**单测验证不了的那一半**：
/// 状态机 -> 判定框 -> 仲裁器 -> 裁决器 -> 数值落地，这条链路真的通。
/// 单元测试能保证"规则是对的"，但保证不了"刀真的砍到了人"。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class CombatSmokeTest : Node3D
{
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        var player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        player.Name = "Player";
        player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(player);

        var dummy = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        dummy.Name = "Dummy";
        dummy.Position = new Vector3(0f, 0.1f, -1.6f);
        AddChild(dummy);

        // 等 _Ready、组注册、物理世界就绪
        await WaitPhysicsFrames(10);

        int hpBefore = dummy.Health.Current;
        int postureBefore = dummy.Posture.Current;
        int playerHpBefore = player.Health.Current;

        Check(player.PrimaryHitbox is not null, "玩家的 Hitbox 没有挂上");
        Check(dummy.PrimaryHitbox is not null, "木桩的 Hitbox 没有挂上");

        // 结构自检：判定框/受击框有没有真的挂上（挂漏了脚本会静默失效，很难查）
        var hurtbox = dummy.GetNodeOrNull<Hurtbox>("Hurtbox");
        Check(player.PrimaryHitbox?.Shape is not null, "玩家 Hitbox 没有形状");
        Check(hurtbox is not null, "木桩 Hurtbox 节点没挂 Hurtbox 脚本");
        Check(hurtbox is null || hurtbox.CollisionLayer == 16, "木桩 Hurtbox 不在 EnemyHurtbox 层(16)");

        // 绕过输入，直接进攻击状态（无头环境没有键盘）
        player.Machine.ForceChange<AttackState>();
        Check(player.Machine.Current is AttackState, "玩家没有进入 AttackState");

        // 采样整段出招。注意体干会自然回复，所以必须取**过程中的峰值**，
        // 不能在结束后再读——那时候它已经回到 0 了。
        var probe = new List<Hurtbox>();
        int activeFrames = 0;
        int maxOverlaps = 0;
        int peakPosture = postureBefore;
        int lowestHp = hpBefore;

        for (int i = 0; i < 90; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            Hitbox? hitbox = player.PrimaryHitbox;
            if (hitbox is { IsActiveThisFrame: true })
            {
                activeFrames++;
                maxOverlaps = Mathf.Max(maxOverlaps, hitbox.Query(probe));
            }

            peakPosture = Mathf.Max(peakPosture, dummy.Posture.Current);
            lowestHp = Mathf.Min(lowestHp, dummy.Health.Current);
        }

        GD.Print($"[冒烟] 判定帧 {activeFrames} 帧，形状查询最大命中 {maxOverlaps} 个受击框");
        GD.Print($"[冒烟] 木桩 HP {hpBefore} -> 最低 {lowestHp}（收尾 {dummy.Health.Current}），体干峰值 {peakPosture}");

        Check(activeFrames > 0, "整段出招没有一帧处于判定帧：AttackState 没有驱动 Hitbox");
        Check(maxOverlaps > 0, "判定帧开着但形状查询没命中木桩：层/掩码/形状/位置有问题");
        Check(lowestHp < hpBefore, $"木桩没有掉血（最低 {lowestHp}）：判定→裁决→落地这条链路没通");
        Check(peakPosture > postureBefore, $"木桩体干没有上涨（峰值 {peakPosture}）：体干没接上");
        Check(player.Health.Current == playerHpBefore, "玩家不该在打木桩时掉血");
        Check(player.Machine.Current is IdleState, $"玩家出招结束后应回到 IdleState，实际是 {player.Machine.Current?.GetType().Name}");

        Report();
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
            GD.PrintErr($"[冒烟] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[冒烟] ✓ 战斗链路通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
