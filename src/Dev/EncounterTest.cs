using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Levels;

namespace Oniblade.Dev;

/// <summary>
/// 遭遇战端到端验收（T42）：
///     godot --headless --path . res://scenes/tests/Encounter.tscn
///
/// 单测（<c>EncounterLogicTests</c>）证明不了"敌人真的被生成、真的挡路、真的放行"。
/// 这个场景量的是那一整条链：**没进场不刷 → 进场刷 → 打死 → 连续确认 → 放行**。
///
/// 退出码 0 ＝ 全过，1 ＝ 有错。
/// </summary>
public partial class EncounterTest : Node3D
{
    [Export] public int EnemyCount { get; set; } = 3;

    private readonly List<string> _failures = new();
    private EncounterZone _zone = null!;
    private Node3D _player = null!;
    private StaticBody3D _gate = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        // 玩家（用真的 Player.tscn，这样它才在 "player" 组里）
        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<Node3D>();
        _player.Position = new Vector3(0f, 0.1f, 20f);   // 先在圈外
        AddChild(_player);

        // 挡路的门：只靠一个组名接进来
        _gate = new StaticBody3D { Name = "Gate", CollisionLayer = 1 };
        _gate.AddToGroup(EncounterZone.GateGroup);
        _gate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4f, 4f, 0.5f) } });
        _gate.Position = new Vector3(0f, 2f, -3f);
        AddChild(_gate);

        // 遭遇区：3 只假人，进场半径 6m
        _zone = new EncounterZone
        {
            Name = "CombatArea_A",
            Profile = new EncounterProfile
            {
                ActivationRadius = 6f,
                SpawnRadius = 3f,
                EnemyCount = EnemyCount,
                EnemyScenePath = "res://scenes/actors/TrainingDummy.tscn",
                ClearHoldFrames = 12,
            },
        };
        AddChild(_zone);

        await WaitFrames(20);

        Check(_zone.Phase == EncounterPhase.Dormant, "玩家在圈外时**不刷怪**");
        Check(_zone.SpawnedCount == 0, "圈外时一只都没生成");
        Check((_gate.CollisionLayer & 1) != 0, "门一开始是挡路的");

        // 走进圈里
        _player.Position = new Vector3(0f, 0.1f, 0f);
        await WaitFrames(5);

        Check(_zone.Phase == EncounterPhase.Active, "玩家进圈后进入 Active");
        Check(_zone.SpawnedCount == EnemyCount, $"生成了 {EnemyCount} 只（实际 {_zone.SpawnedCount}）");

        // 打死它们（走 HealthMeter 的正规扣血路径，不是 QueueFree）
        int killed = 0;
        foreach (Node child in _zone.GetChildren())
        {
            if (child is not CombatActor enemy)
                continue;

            enemy.Health.Apply(9999);
            killed++;
        }

        Check(killed == EnemyCount, $"打死 {EnemyCount} 只（实际 {killed}）");

        // 清场要连续确认若干帧
        await WaitFrames(30);

        Check(_zone.Phase == EncounterPhase.Cleared, $"清场（实际 {_zone.Phase}）");
        Check(_zone.GateOpened, "放行标记已置位");
        Check(_gate.CollisionLayer == 0, $"门的碰撞层被清掉（实际 {_gate.CollisionLayer}）");
        Check(_zone.ClearFrame > 0, $"记录到清场帧号（{_zone.ClearFrame}）");

        GD.Print("");
        GD.Print(_failures.Count == 0 ? "[遭遇战] ✓ 全部通过" : $"[遭遇战] ✗ {_failures.Count} 条没过：");
        foreach (string f in _failures)
            GD.PrintErr($"[遭遇战]   ✗ {f}");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok)
        {
            GD.Print($"[遭遇战]   ✓ {what}");
            return;
        }

        _failures.Add(what);
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void AddFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(80f, 0.4f, 80f) } });
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
    }
}
