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

        // ── 移动稳定性回归测试 ──
        // 曾经的 bug：相机臂挂在身体下面、跟着身体一起转，而移动方向又来自相机，
        // 于是角色去追一个跟着自己转的目标 → 边移动边原地打转。
        // 这个测试就是钉死那个反馈回路：按住一个方向走 1 秒，朝向必须在十几帧内收敛。
        Input.ActionPress("move_right");

        Vector3 posMid = Vector3.Zero;
        Vector3 posEnd = Vector3.Zero;
        float yawEarly = 0f;
        float yawLate = 0f;

        for (int i = 0; i < 60; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (i == 29)
                posMid = player.GlobalPosition;
            else if (i == 44)
                yawEarly = player.Rotation.Y;
            else if (i == 59)
            {
                posEnd = player.GlobalPosition;
                yawLate = player.Rotation.Y;
            }
        }

        Input.ActionRelease("move_right");

        float travelled = new Vector2(posEnd.X - posMid.X, posEnd.Z - posMid.Z).Length();
        float yawDriftDeg = Mathf.RadToDeg(Mathf.Abs(Mathf.AngleDifference(yawEarly, yawLate)));

        GD.Print($"[冒烟] 移动：后半段位移 {travelled:F2} m，后 15 帧朝向漂移 {yawDriftDeg:F1} 度");

        Check(travelled > 1.0f, $"按住 move_right 30 帧只走了 {travelled:F2} m：移动没有生效");
        Check(yawDriftDeg < 5f, $"移动时朝向仍在不停转（后 15 帧漂移 {yawDriftDeg:F1} 度）：相机跟随身体转，导致原地打转");

        // ── 死亡 → 自动重生 ──
        // 会死、又能反复死的靶子，是"击杀流程"（吸魂/忍杀/掉落）的试验台。
        var respawn = Load<TrainingDummy>("res://scenes/actors/RespawnDummy.tscn");
        respawn.Name = "RespawnDummy";
        respawn.Position = new Vector3(4f, 0.1f, -2f);
        respawn.RespawnDelayFrames = 45;   // 测试里缩短等待，机制不变
        AddChild(respawn);
        await WaitPhysicsFrames(10);

        Check(!respawn.IsDead && respawn.Health.Current == respawn.Health.Max,
            "重生假人初始状态不对");

        respawn.Health.Apply(respawn.Health.Max + 1);
        respawn.Die();
        Check(respawn.IsDead, "重生假人没有被杀死（Invincible 或 Die 覆写出问题）");

        await WaitPhysicsFrames(respawn.RespawnDelayFrames + 15);

        GD.Print($"[冒烟] 重生假人：死后 {respawn.RespawnDelayFrames} 帧重生，次数 {respawn.RespawnCount}，血量 {respawn.Health.Current}/{respawn.Health.Max}");

        Check(!respawn.IsDead, "重生假人没有在等待期结束后复活");
        Check(respawn.RespawnCount == 1, $"重生计数应为 1，实际 {respawn.RespawnCount}（可能重生逻辑跑了多次）");
        Check(respawn.Health.Current == respawn.Health.Max, $"重生后血量没回满：{respawn.Health.Current}/{respawn.Health.Max}");
        Check(respawn.Posture.Current == 0, $"重生后体干没清零：{respawn.Posture.Current}");

        // ── 击杀 → 魄火 → 自动吸魂 ──
        // 03 §6.1：魄不是掉在地上的道具，是"从尸体里飞出来、自己飞向笼手的东西"。
        int soulsBefore = player.Gauntlet.SoulCount;
        respawn.Health.Apply(respawn.Health.Max + 1);
        respawn.Die();
        Check(respawn.IsDead, "第二次击杀失败");

        bool sawOrb = await WaitForSoulAbsorb(player, soulsBefore, 180);

        GD.Print($"[冒烟] 吸魂：魄 {soulsBefore} → {player.Gauntlet.SoulCount}，连吸 {player.Gauntlet.ChainCount}，侵蚀 {player.Gauntlet.Erosion}");

        Check(sawOrb, "击杀后没有生成魄火（ActorDefeated 事件或 SoulOrb 生成链断了）");
        Check(player.Gauntlet.SoulCount > soulsBefore, "魄火没有被自动吸进笼手（自动牵引失败）");
        Check(player.Gauntlet.Erosion == 0, $"普通自动牵引不该涨侵蚀，现在涨到了 {player.Gauntlet.Erosion}（03 §6.6 的核心分界）");

        // ── 「深吸」：唯一会推进侵蚀的行为 ──
        await WaitPhysicsFrames(respawn.RespawnDelayFrames + 15);
        Check(!respawn.IsDead, "第三次击杀前假人还没复活");

        int deepBefore = player.Gauntlet.DeepAbsorbCount;
        int erosionBefore = player.Gauntlet.Erosion;

        Input.ActionPress("interact");
        await WaitPhysicsFrames(3);
        Check(player.Gauntlet.IsDeepAbsorbing, "按住交互键没有进入「深吸」");

        respawn.Health.Apply(respawn.Health.Max + 1);
        respawn.Die();
        await WaitForSoulAbsorb(player, player.Gauntlet.SoulCount, 180);
        Input.ActionRelease("interact");

        GD.Print($"[冒烟] 深吸：次数 {deepBefore} → {player.Gauntlet.DeepAbsorbCount}，侵蚀 {erosionBefore} → {player.Gauntlet.Erosion}，阶段 {player.Gauntlet.Stage}");

        Check(player.Gauntlet.DeepAbsorbCount > deepBefore, "「深吸」期间吸到的魄没有被记成深吸");
        Check(player.Gauntlet.Erosion > erosionBefore, "「深吸」没有推进侵蚀（03 §6.6：这是唯一该涨侵蚀的行为）");

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

    /// <summary>等魄火生成并被吸进笼手；返回这期间是否看见过魄火。</summary>
    private async System.Threading.Tasks.Task<bool> WaitForSoulAbsorb(PlayerActor player, int soulsBefore, int maxFrames)
    {
        bool sawOrb = false;

        for (int i = 0; i < maxFrames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (GetTree().GetNodesInGroup("soul_orb").Count > 0)
                sawOrb = true;

            if (player.Gauntlet.SoulCount > soulsBefore)
                break;
        }

        return sawOrb;
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
