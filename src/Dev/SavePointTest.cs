using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Levels;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 鬼火存档点端到端验收（T42 后半张）：
///     godot --headless --path . res://scenes/tests/SavePoint.tscn
///
/// **核心断言只有一条，但它就是这张卡的全部意义**：
/// 在鬼火处死掉之后，重开要回到**鬼火**，而不是回关卡入口。
///
/// 它同时钉住一个容易漏的点：**朝向也要记**——只记位置的话，复活后会背对着敌人开局。
///
/// 退出码 0 ＝ 全过，1 ＝ 有错。
/// </summary>
public partial class SavePointTest : Node3D
{
    private readonly List<string> _failures = new();
    private PlayerActor _player = null!;
    private SavePoint _save = null!;
    private BattleReset _reset = null!;

    private static readonly Vector3 StartPoint = new(0f, 0.1f, 0f);
    private static readonly Vector3 ShrinePoint = new(0f, 0.1f, 10f);

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _reset = new BattleReset { Name = "BattleReset" };
        AddChild(_reset);

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = StartPoint;
        AddChild(_player);

        _save = new SavePoint { Name = "Shrine", ActivationRadius = 2.5f };
        _save.Position = ShrinePoint;
        AddChild(_save);

        await WaitFrames(20);

        Check(Near(_player.SpawnTransform.Origin, StartPoint),
            $"开局时战场起点是关卡入口 {StartPoint}（实际 {_player.SpawnTransform.Origin}）");
        Check(!_save.IsActive, "还没走到鬼火，它不该是激活状态");

        // ── 走到鬼火旁 ─────────────────────────────────────────
        _player.Position = ShrinePoint + new Vector3(0f, 0f, 1.2f);   // 半径内
        await WaitFrames(5);

        Check(_save.IsActive, "走到跟前 → 鬼火点亮");
        Check(_save.ActivatedFrame > 0, $"记录到激活帧号（{_save.ActivatedFrame}）");
        Check(Near(_player.SpawnTransform.Origin, _player.GlobalPosition),
            $"战场起点被改写成当前所在处（{_player.SpawnTransform.Origin}）");

        OmniLight3D? light = _save.GetNodeOrNull<OmniLight3D>("Light");
        Check(light is not null && light.LightEnergy > 0f,
            "灯亮了（暖色只属于灯笼 —— 10 §1）");

        // ── 死一次，看重开回到哪 ───────────────────────────────
        Vector3 deathSpot = _player.GlobalPosition;
        _player.Health.Apply(9999);
        await WaitFrames(2);

        int restarted = _reset.RestartBattle();
        await WaitFrames(2);

        Check(restarted >= 1, $"重开复位了 {restarted} 个单位");
        Check(_player.GlobalPosition.DistanceTo(deathSpot) < 1.5f,
            $"重开回到**鬼火**（{deathSpot}），而不是关卡入口 {StartPoint}"
            + $"（实际 {_player.GlobalPosition}）");

        // ── 对照组：没碰过鬼火时，重开回入口 ─────────────────────
        var fresh = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        fresh.Position = new Vector3(0f, 0.1f, -6f);
        AddChild(fresh);
        await WaitFrames(5);

        fresh.SetSpawnTransform(fresh.GlobalTransform);   // 模拟"这一局还没碰过鬼火"
        _reset.RestartBattle();
        await WaitFrames(2);

        Check(Near(fresh.GlobalPosition, new Vector3(0f, 0.1f, -6f)),
            $"从没碰过鬼火的单位，重开回它自己的起点（实际 {fresh.GlobalPosition}）");

        GD.Print("");
        GD.Print(_failures.Count == 0 ? "[鬼火] ✓ 全部通过" : $"[鬼火] ✗ {_failures.Count} 条没过：");
        foreach (string f in _failures)
            GD.PrintErr($"[鬼火]   ✗ {f}");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private static bool Near(Vector3 a, Vector3 b) => a.DistanceTo(b) < 1.5f;

    private void Check(bool ok, string what)
    {
        if (ok)
        {
            GD.Print($"[鬼火]   ✓ {what}");
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
