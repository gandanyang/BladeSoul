using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 跳跃端到端验收（T41）：
///     godot --headless --path . res://scenes/tests/Jump.tscn
///
/// 量的是**这一跳的真实数字**：起跳几帧离地、最高点到多少米、滞空多少帧、
/// 落地硬直多少帧。用 `JumpProfile`（data/player/jump.tres）里的值反推期望上限，
/// 所以改了 `.tres` 这个测试不会说假话。
///
/// ⚠️ 没量的两件（诚实记着）：① 跳跃**不是无敌帧**（需要"敌人正在挥刀"的场景）；
/// ② 「危·横扫」用跳跃回避的对照组（需要横扫敌人，当前没有）。
///
/// 退出码 0 ＝ 全过，1 ＝ 有错。
/// </summary>
public partial class JumpTest : Node3D
{
    [Export] public int TotalFrames { get; set; } = 300;
    [Export] public float HeightTolerance { get; set; } = 0.25f;
    [Export] public int RecoveryTolerance { get; set; } = 3;

    private readonly List<string> _failures = new();
    private PlayerActor _player = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        await WaitFrames(20);

        JumpProfile? profile = _player.JumpProfile;
        if (profile is null)
        {
            Fail("玩家身上没有 JumpProfile——数值没接上");
            Finish();
            return;
        }

        float gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        float expectedPeak = profile.TakeoffSpeed * profile.TakeoffSpeed / (2f * gravity);

        float startY = _player.GlobalPosition.Y;
        int takeoffFrame = -1;
        float peak = 0f;
        int airborneFrames = 0;
        int landedFrame = -1;
        bool wasAirborne = false;

        GD.Print($"[跳跃] 参数：初速 {profile.TakeoffSpeed:F2} m/s，重力 {gravity:F2}，"
                 + $"落地硬直 {profile.LandRecoveryFrames} 帧 → 理论最高 {expectedPeak:F2}m");

        Input.ActionPress("jump");

        for (int frame = 0; frame < TotalFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (frame == 2)
                Input.ActionRelease("jump");

            float height = _player.GlobalPosition.Y - startY;
            bool inAir = height > 0.02f;

            if (inAir && takeoffFrame < 0)
                takeoffFrame = frame;

            if (inAir)
            {
                wasAirborne = true;
                airborneFrames++;
                peak = Mathf.Max(peak, height);
            }
            else if (wasAirborne && landedFrame < 0)
            {
                landedFrame = frame;
            }

            // 落地后等它回到 Idle：这一段就是硬直。
            if (landedFrame >= 0 && _player.Machine.Current is IdleState)
            {
                int recovery = frame - landedFrame;
                Finish(profile, takeoffFrame, peak, expectedPeak, airborneFrames, recovery);
                return;
            }
        }

        Fail($"跑满 {TotalFrames} 帧都没回到 Idle（起跳帧 {takeoffFrame}，最高 {peak:F2}m）");
        Finish();
    }

    private void Finish(JumpProfile profile, int takeoffFrame, float peak, float expectedPeak,
        int airborneFrames, int recovery)
    {
        Check(takeoffFrame >= 0 && takeoffFrame <= 2, $"按下后 2 帧内离地（实际第 {takeoffFrame} 帧）");
        Check(Mathf.Abs(peak - expectedPeak) <= HeightTolerance,
            $"最高点 ≈ {expectedPeak:F2}m ± {HeightTolerance:F2}（实际 {peak:F2}m）");
        Check(airborneFrames >= 40,
            $"滞空足够长到能躲横扫（实际 {airborneFrames} 帧 ≈ {airborneFrames / 60f:F2}s）");
        Check(Mathf.Abs(recovery - profile.LandRecoveryFrames) <= RecoveryTolerance,
            $"落地硬直 ≈ {profile.LandRecoveryFrames} 帧 ± {RecoveryTolerance}（实际 {recovery} 帧）");
        Finish();
    }

    private void Finish()
    {
        GD.Print("");
        GD.Print(_failures.Count == 0 ? "[跳跃] ✓ 全部通过" : $"[跳跃] ✗ {_failures.Count} 条没过：");
        foreach (string f in _failures)
            GD.PrintErr($"[跳跃]   ✗ {f}");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok)
        {
            GD.Print($"[跳跃]   ✓ {what}");
            return;
        }

        Fail(what);
    }

    private void Fail(string what) => _failures.Add(what);

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void AddFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(60f, 0.4f, 60f) },
        });
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
    }
}
