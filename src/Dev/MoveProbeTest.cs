using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 「身体动、腿不动」的定位（试玩反馈）。
///
///     godot --headless --path . res://scenes/tests/MoveProbe.tscn
///
/// 前一次体检是**直接驱动动画器**（腿能摆 45.8°），所以问题不在那。
/// 这一次量的是**真按 W 走**的时候：
/// ① 角色真的有速度吗（不是"被直接挪位置"）？
/// ② 两根大腿骨在实战里到底摆了几度？
///
/// 只要出现"速度很足、腿 0.0°"，就说明**游戏里没人把'我在走'喂给动画器**——
/// 那才是"身体动腿不动"的真身。
/// </summary>
public partial class MoveProbeTest : Node3D
{
    [Export] public int Frames { get; set; } = 180;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        var player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(player);

        await WaitFrames(15);

        Skeleton3D? skel = FindSkeleton(player.GetNodeOrNull<Node3D>("VisualModel") ?? player);
        if (skel is null)
        {
            GD.PrintErr("[移动体检] 找不到骨架");
            GetTree().Quit(1);
            return;
        }

        int lt = skel.FindBone("L_Thigh");
        int rt = skel.FindBone("R_Thigh");

        Quaternion restL = lt >= 0 ? skel.GetBonePoseRotation(lt) : Quaternion.Identity;
        Quaternion restR = rt >= 0 ? skel.GetBonePoseRotation(rt) : Quaternion.Identity;

        float peakLeg = 0f;
        float peakSpeed = 0f;
        Vector3 start = player.GlobalPosition;

        Input.ActionPress("move_forward");

        for (int f = 0; f < Frames; f++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            Vector3 v = player.Velocity;
            peakSpeed = Mathf.Max(peakSpeed, new Vector2(v.X, v.Z).Length());

            if (lt >= 0)
                peakLeg = Mathf.Max(peakLeg,
                    Mathf.RadToDeg(restL.AngleTo(skel.GetBonePoseRotation(lt))));
            if (rt >= 0)
                peakLeg = Mathf.Max(peakLeg,
                    Mathf.RadToDeg(restR.AngleTo(skel.GetBonePoseRotation(rt))));

            if (f % 60 == 0)
            {
                float moved = new Vector2(
                    player.GlobalPosition.X - start.X,
                    player.GlobalPosition.Z - start.Z).Length();
                GD.Print($"[移动体检] 第 {f,3} 帧：速度 {new Vector2(v.X, v.Z).Length():F2} m/s，"
                         + $"已移动 {moved:F2} m，腿峰值 {peakLeg:F1}°");
            }
        }

        Input.ActionRelease("move_forward");

        float totalMoved = new Vector2(
            player.GlobalPosition.X - start.X,
            player.GlobalPosition.Z - start.Z).Length();

        GD.Print("");
        GD.Print($"[移动体检] 3 秒内：峰值速度 {peakSpeed:F2} m/s，实际移动 {totalMoved:F2} m，"
                 + $"大腿骨峰值偏转 {peakLeg:F1}°");

        if (peakSpeed > 0.5f && peakLeg < 5f)
            GD.Print("[移动体检] ✗ **速度是真的、腿没动** —— 游戏里没人把'我在走'喂给动画器");
        else if (peakSpeed > 0.5f && peakLeg >= 5f)
            GD.Print("[移动体检] ✓ 走路时腿在动（那'腿不动'的观感要往别处找）");
        else
            GD.Print("[移动体检] ? 角色根本没动起来（速度近 0）—— 先查输入/移动本身");

        GetTree().Quit(0);
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void AddFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(120f, 0.4f, 120f) } });
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
            return skeleton;

        foreach (Node child in node.GetChildren())
        {
            Skeleton3D? found = FindSkeleton(child);
            if (found is not null)
                return found;
        }

        return null;
    }
}
