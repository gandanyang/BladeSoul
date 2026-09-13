using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 动画"看得见吗"的体检（试玩反馈：挥刀/一闪好像没绑骨骼、没有打击感）。
///
///     godot --headless --path . res://scenes/tests/AnimProbe.tscn
///
/// 前面已经查实：模型的骨骼名与动画器要找的 12 个名字**全部对得上**，
/// 所以不是"静默失效"。那就要回答**下一层**的问题——**到底哪几根骨在动、动了多少**：
///
/// * 如果动的是腿/腰而**持刀那只手几乎不动**，那玩家看到的就是"人滑过去、刀没挥"。
/// * 逐骨打出角度，就能一眼看出这件事，不用靠猜。
///
/// 退出码 0 ＝ 跑完（它只报数，不下结论）。
/// </summary>
public partial class AnimProbeTest : Node3D
{
    /// <summary>挥砍动作采样帧数。</summary>
    [Export] public int Frames { get; set; } = 40;

    private static readonly string[] Names =
    {
        "Hip", "Spine01", "Spine02", "Head",
        "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
        "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
    };

    /// <summary>
    /// **武器骨骼**（T48 / T25 的缺口）：刀挂在右手，鞘挂在腰。
    ///
    /// 为什么单独盯它们：试玩反馈"挥刀好像没绑骨骼"——实测发现持刀臂摆了 **133.7°**，
    /// 但**模型里根本没有武器节点**（刀烘进了网格、绑在腰胯上），
    /// 所以玩家看到的是"空手抡胳膊、刀一动不动"。
    /// 这几根骨一到位，下面这张表里就会出现它们的偏转，"刀跟手走"才是自动的。
    /// </summary>
    private static readonly string[] WeaponNames = { "Weapon_R", "Weapon_L", "Scabbard" };

    private static IEnumerable<string> AllNames()
    {
        foreach (string n in Names)
            yield return n;

        foreach (string n in WeaponNames)
            yield return n;
    }

    public override void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });

        var player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(player);

        Node3D? visual = player.GetNodeOrNull<Node3D>("VisualModel");
        Skeleton3D? skel = FindSkeleton(visual ?? player);

        if (skel is null)
        {
            GD.PrintErr("[动画体检] 找不到 Skeleton3D");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"[动画体检] 骨架 {skel.GetBoneCount()} 根骨；模型根节点 {visual?.Name}");

        // 逐一确认动画器要找的名字在不在。
        int missing = 0;
        foreach (string name in Names)
        {
            int bone = skel.FindBone(name);
            if (bone < 0)
            {
                missing++;
                GD.PrintErr($"[动画体检] ✗ 找不到骨骼：{name}");
            }
        }

        GD.Print($"[动画体检] 12 个要找的名字里，缺 {missing} 个");

        // 武器骨骼（T48）：缺了就是"刀不跟手"，与握刀手摆动多少无关。
        int weaponsMissing = 0;
        foreach (string name in WeaponNames)
        {
            if (skel.FindBone(name) < 0)
                weaponsMissing++;
        }

        GD.Print(weaponsMissing == 0
            ? "[动画体检] ✓ 武器骨骼齐全（刀/鞘会跟着手与腰走）"
            : $"[动画体检] ✗ 武器骨骼缺 {weaponsMissing}/{WeaponNames.Length} 根"
              + $"（{string.Join(" / ", WeaponNames)}）—— **刀不跟手，正是试玩反馈的那一条**");

        // 逐骨量：idle 静止 → 挥砍过程中的最大偏转。
        if (visual is null)
        {
            GD.PrintErr("[动画体检] 玩家身上没有 VisualModel");
            GetTree().Quit(1);
            return;
        }

        var animator = new HumanoidAnimator(visual);
        GD.Print($"[动画体检] 动画器 Valid = {animator.Valid}");

        var idle = new Dictionary<string, Quaternion>();
        for (int f = 0; f < 20; f++)
        {
            animator.AnimateLocomotion(0f, 1f / 60f);
            animator.AnimateCombat(1f / 60f, -1);
        }

        foreach (string name in AllNames())
        {
            int bone = skel.FindBone(name);
            if (bone >= 0)
                idle[name] = skel.GetBonePoseRotation(bone);
        }

        var peak = new Dictionary<string, float>();
        foreach (string name in AllNames())
            peak[name] = 0f;

        animator.PlayAttack(0, 26);      // 轻斩·壹（26 帧）

        for (int f = 0; f < Frames; f++)
        {
            animator.AnimateLocomotion(0f, 1f / 60f);
            animator.AnimateCombat(1f / 60f, f);   // ★ 攻击姿势由**帧号**算出（04 §12）

            foreach (string name in AllNames())
            {
                int bone = skel.FindBone(name);
                if (bone < 0 || !idle.ContainsKey(name))
                    continue;

                float deg = Mathf.RadToDeg(idle[name].AngleTo(skel.GetBonePoseRotation(bone)));
                peak[name] = Mathf.Max(peak[name], deg);
            }
        }

        GD.Print("[动画体检] 轻斩·壹：每根骨相对静止姿势的最大偏转");
        foreach (string name in AllNames())
            GD.Print($"[动画体检]   {name,-12} {peak[name],7:F1}°");

        GetTree().Quit(0);
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
