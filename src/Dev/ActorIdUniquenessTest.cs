using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// ActorId 唯一性自检（T23）：
///     godot --headless --path . res://scenes/tests/ActorIdUniqueness.tscn
///
/// 这条断言的价值不在"今天是对的"，而在于**它以后会替我们抓住第三次撞号**。
///
/// 背景：`CombatArbiter` 的确定性排序（04 §15：按 ActorId 排序 → 同帧结果可复现）
/// 依赖 ActorId 唯一，而手工填 id 已经**静默撞号两次**：
/// 道场里两个 TrainingDummy 都是 100；SpearDummy 与 RespawnDummy 都是 102。
/// 两次都不报错、不闪、不崩——只让"同帧互击谁先结算"变得不可复现。
///
/// 所以这里刻意放了**两个同类敌人的实例**（同一份 .tscn 实例化两次）：
/// 那是手工填 id 最容易翻车的形态，也是"自动分配"必须证明能扛住的形态。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class ActorIdUniquenessTest : Node3D
{
    /// <summary>手工固定值——用来证明"显式填了非 0 值仍然被尊重"。</summary>
    private const int ManualOverrideId = 777;

    private readonly List<string> _failures = new();

    private CombatActor _manual = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });

        Spawn<PlayerActor>("res://scenes/actors/Player.tscn", new Vector3(0f, 0.1f, 0f));
        Spawn<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn", new Vector3(1.5f, 0.1f, 0f));
        Spawn<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn", new Vector3(-1.5f, 0.1f, 0f));
        Spawn<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn", new Vector3(-3f, 0.1f, 0f));
        Spawn<TrainingDummy>("res://scenes/actors/RespawnDummy.tscn", new Vector3(3f, 0.1f, 0f));
        Spawn<AttackingDummy>("res://scenes/actors/SpearDummy.tscn", new Vector3(4.5f, 0.1f, 0f));

        // 显式填了 id 的那一个：场景里**故意**保留手工覆盖能力。
        _manual = Spawn<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn", new Vector3(-4.5f, 0.1f, 0f), ManualOverrideId);

        // 等到所有 _Ready 都跑完（它们在这一帧之前就已经跑完了，这里只是让物理帧推一下）。
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Report();
    }

    private void Report()
    {
        var seen = new Dictionary<int, string>();
        int total = 0;
        int manualCount = 0;

        foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
        {
            if (node is not CombatActor actor)
                continue;

            total++;

            if (actor.ActorId == 0)
            {
                Check(false, $"{actor.DebugName} 的 ActorId 仍是 0：自动分配没有生效");
                continue;
            }

            if (actor.ActorId == ManualOverrideId)
                manualCount++;

            if (seen.TryGetValue(actor.ActorId, out string? other))
                Check(false, $"ActorId 撞号：{actor.DebugName} 与 {other} 都是 {actor.ActorId}");
            else
                seen[actor.ActorId] = actor.DebugName;
        }

        GD.Print($"[ActorId] 场上 {total} 个战斗单位，唯一 id {seen.Count} 个；" +
                 $"手工覆盖 1 个（{ManualOverrideId} → {_manual.ActorId}），其余自动分配");

        Check(total == 7, $"场上战斗单位是 {total} 个，期望 7 个（场景装配变了？）");
        Check(seen.Count == total, $"唯一 id 只有 {seen.Count} 个，但有 {total} 个单位：存在撞号");
        Check(manualCount == 1, $"走了手工覆盖的单位有 {manualCount} 个，期望正好 1 个");
        Check(_manual.ActorId == ManualOverrideId,
            $"手工填的 ActorId 被覆盖了：期望 {ManualOverrideId}，实际 {_manual.ActorId}");

        foreach (string failure in _failures)
            GD.PrintErr($"[ActorId] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[ActorId] ✓ 唯一性通过（自动分配 + 手工覆盖都正确）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private T Spawn<T>(string path, Vector3 position, int actorId = 0) where T : CombatActor
    {
        T actor = GD.Load<PackedScene>(path).Instantiate<T>();
        actor.ActorId = actorId;      // 0 = 交给自动分配
        actor.Position = position;
        AddChild(actor);
        return actor;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }
}
