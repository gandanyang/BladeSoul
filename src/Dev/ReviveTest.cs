using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 复活端到端对照实验（T22）：
///     godot --headless --path . res://scenes/tests/ReviveTest.tscn     （武士档，ReviveCount=1）
///     godot --headless --path . res://scenes/tests/ReviveMigoto.tscn   （見習档，ReviveCount=3）
///
/// 它验证的是 T22 与 T14 的**分工**：
///   还有复活次数 → 当场站起来（不触发重开）
///   次数用尽     → 交给 T14 的 BattleReset 原地重开
///
/// 判据是 <c>BattleReset.RestartCount</c> 有没有涨——涨了说明走的是重开，
/// 没涨说明确实是"当场站起来"。这比看状态名可靠：两条路最后都会回到 IdleState。
///
/// 見習 档那一遍同时验证了"复活次数来自难度档"（05 §2：1/1/2/3），
/// 而不是代码里写死的 1。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class ReviveTest : Node3D
{
    private const int MaxRecoverFrames = 400;
    private const int MoveProbeFrames = 12;

    /// <summary>期望能复活几次（武士 1 / 見習 3）。之后那一次必须触发重开。</summary>
    [Export] public int ExpectedRevives { get; set; } = 1;

    /// <summary>可选：覆盖玩家难度档（用来跑 見習 档）。</summary>
    [Export] public DifficultyProfile? Difficulty { get; set; }

    private readonly List<string> _failures = new();

    private PlayerActor _player = null!;
    private BattleReset _reset = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        _reset = new BattleReset { Name = "BattleReset" };
        AddChild(_reset);
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);

        if (Difficulty is not null)
            _player.Difficulty = Difficulty;

        AddChild(_player);

        await WaitPhysicsFrames(10);

        GD.Print($"[复活] 难度 = {_player.Difficulty?.DisplayName}，" +
                 $"复活次数 {_player.RevivesLeft}，期望能复活 {ExpectedRevives} 次");

        Check(_player.RevivesLeft == ExpectedRevives,
            $"进场时复活次数是 {_player.RevivesLeft}，期望 {ExpectedRevives}（难度档没接上？）");

        // 连杀 ExpectedRevives 次应该都复活，第 ExpectedRevives+1 次必须重开。
        for (int attempt = 1; attempt <= ExpectedRevives + 1; attempt++)
        {
            bool revived = await KillPlayerAndWait(attempt);

            if (attempt <= ExpectedRevives)
            {
                Check(revived,
                    $"第 {attempt} 次死亡触发了重开，但应该还有复活次数（应能复活 {ExpectedRevives} 次）");
            }
            else
            {
                Check(!revived,
                    $"第 {attempt} 次死亡（次数已用尽）仍然是复活：复活次数没有被真正消耗");
            }
        }

        await ProbeMovement();
        Report();
    }

    /// <summary>把玩家打死，等他回到可控。返回 true = 走的是复活（重开计数没涨）。</summary>
    private async System.Threading.Tasks.Task<bool> KillPlayerAndWait(int attempt)
    {
        int restartsBefore = _reset.RestartCount;
        int revivesBefore = _player.RevivesLeft;
        int frameBefore = (int)Engine.GetPhysicsFrames();

        // Health.Apply 只改数字、**不会触发 Die()**（那只发生在 ReceiveVerdict 里），
        // 所以这里必须显式调 Die()。
        _player.Health.Apply(_player.Health.Max);
        _player.Die();

        for (int frame = 0; frame < MaxRecoverFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (!_player.IsDead && _player.Machine.Current is IdleState)
            {
                int used = (int)Engine.GetPhysicsFrames() - frameBefore;
                bool revived = _reset.RestartCount == restartsBefore;

                GD.Print($"[复活] 第 {attempt} 次死亡：{(revived ? "复活" : "重开")}，" +
                         $"耗时 {used} 帧，HP {_player.Health.Current}/{_player.Health.Max}，" +
                         $"剩余复活 {_player.RevivesLeft}");

                Check(_player.Health.Current == _player.Health.Max,
                    $"第 {attempt} 次死亡后血量没有恢复：{_player.Health.Current}/{_player.Health.Max}");
                Check(_player.Health.Current != 0, "血量还是 0：站起来的时候没有回血");

                if (revived)
                {
                    Check(_player.RevivesLeft == revivesBefore - 1,
                        $"复活次数没有按预期扣减：{revivesBefore} → {_player.RevivesLeft}");
                }
                else
                {
                    // 重开之后次数补满（01 §0 规则 1：死亡没有持久性惩罚）。
                    Check(_player.RevivesLeft == ExpectedRevives,
                        $"重开后复活次数没有补满：{_player.RevivesLeft}，期望 {ExpectedRevives}");
                }

                return revived;
            }
        }

        Check(false, $"第 {attempt} 次死亡后 {MaxRecoverFrames} 帧内没有回到可控状态");
        return false;
    }

    /// <summary>复活/重开之后必须真的能吃输入——这是"回到可控"的定义。</summary>
    private async System.Threading.Tasks.Task ProbeMovement()
    {
        Vector3 before = _player.GlobalPosition;

        Input.ActionPress("move_right");

        for (int i = 0; i < MoveProbeFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Input.ActionRelease("move_right");

        float moved = _player.GlobalPosition.DistanceTo(before);
        GD.Print($"[复活] 最后一次恢复后按 move_right 移动了 {moved:0.###} m");

        Check(moved > 0.05f, $"恢复后按 move_right 只动了 {moved:0.###} m：没有真正回到可控状态");
    }

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[复活] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print($"[复活] ✓ 通过（难度 {_player.Difficulty?.DisplayName}：复活 {ExpectedRevives} 次后重开）");

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
