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

            // 一次性诊断：Hip 到底有没有被写进去。
            // （腿生效而骨盆不生效，不合常理——原始四元数一看就知道。）
            if (f == 25)
            {
                int hipBone = skel.FindBone("Hip");
                if (hipBone >= 0 && idle.ContainsKey("Hip"))
                {
                    Quaternion now = skel.GetBonePoseRotation(hipBone);
                    GD.Print($"[动画体检] （诊断）第 25 帧 Hip：静止 {idle["Hip"]} → 当前 {now}"
                             + $"，夹角 {Mathf.RadToDeg(idle["Hip"].AngleTo(now)):F2}°");
                }
            }
        }

        GD.Print("[动画体检] 轻斩·壹：每根骨相对静止姿势的最大偏转");
        foreach (string name in AllNames())
            GD.Print($"[动画体检]   {name,-12} {peak[name],7:F1}°");

        // ── 走路：腿到底动没动（"模型移动问题"通常就是这里）────────────
        // 挥手挥得再大，只要腿不动，人就是"飘"过去的——这就是滑步。
        var idle2 = new Dictionary<string, Quaternion>();
        foreach (string name in AllNames())
        {
            int bone = skel.FindBone(name);
            if (bone >= 0)
                idle2[name] = skel.GetBonePoseRotation(bone);
        }

        var walkPeak = new Dictionary<string, float>();
        foreach (string name in AllNames())
            walkPeak[name] = 0f;

        for (int f = 0; f < 120; f++)
        {
            animator.AnimateLocomotion(1f, 1f / 60f);   // 全速走
            animator.AnimateCombat(1f / 60f, -1);

            foreach (string name in AllNames())
            {
                int bone = skel.FindBone(name);
                if (bone < 0 || !idle2.ContainsKey(name))
                    continue;

                float deg = Mathf.RadToDeg(idle2[name].AngleTo(skel.GetBonePoseRotation(bone)));
                walkPeak[name] = Mathf.Max(walkPeak[name], deg);
            }
        }

        GD.Print("[动画体检] 走路（全速，2 秒）：每根骨相对静止姿势的最大偏转");
        foreach (string name in AllNames())
            GD.Print($"[动画体检]   {name,-12} {walkPeak[name],7:F1}°");

        float legMotion = Mathf.Max(
            Mathf.Max(walkPeak["L_Thigh"], walkPeak["R_Thigh"]),
            Mathf.Max(walkPeak["L_Calf"], walkPeak["R_Calf"]));

        GD.Print(legMotion < 5f
            ? $"[动画体检] ✗ 走路时腿几乎不动（最大 {legMotion:F1}°）—— **这就是滑步**"
            : $"[动画体检] ✓ 走路时腿在动（最大 {legMotion:F1}°）");

        // ── 两腿是"交替"还是"同相"（同相＝像兔子蹦）──────────────────
        float maxPhaseGap = 0f;
        for (int f = 0; f < 120; f++)
        {
            animator.AnimateLocomotion(1f, 1f / 60f);
            animator.AnimateCombat(1f / 60f, -1);

            int lt = skel.FindBone("L_Thigh");
            int rt = skel.FindBone("R_Thigh");
            if (lt < 0 || rt < 0)
                break;

            float gap = Mathf.RadToDeg(
                skel.GetBonePoseRotation(lt).AngleTo(skel.GetBonePoseRotation(rt)));
            maxPhaseGap = Mathf.Max(maxPhaseGap, gap);
        }

        GD.Print(maxPhaseGap < 5f
            ? $"[动画体检] ✗ 两条大腿**同相**（最大夹角差只有 {maxPhaseGap:F1}°）—— 走路会像兔子蹦"
            : $"[动画体检] ✓ 两条大腿交替（最大夹角差 {maxPhaseGap:F1}°）");

        // ── 脚底有没有贴地（游戏里只做了 ×1.75 缩放，没做落地修正）──────
        if (visual is not null)
        {
            Aabb box = SubtreeAabb(visual);
            GD.Print($"[动画体检] 模型包围盒（世界坐标）：minY {box.Position.Y:F3} / maxY {(box.Position.Y + box.Size.Y):F3}");
            GD.Print($"[动画体检] 玩家原点 Y = {player.GlobalPosition.Y:F3}，胶囊半径 0.35 / 高 1.75"
                     + "（胶囊底面约在原点 Y - 0.775）");

            float feetY = box.Position.Y;
            float capsuleBottom = player.GlobalPosition.Y - 0.775f;

            GD.Print(Mathf.Abs(feetY - capsuleBottom) < 0.15f
                ? $"[动画体检] ✓ 脚底与胶囊底面基本对齐（差 {feetY - capsuleBottom:F3}m）"
                : $"[动画体检] ✗ 脚底与胶囊底面**差了 {feetY - capsuleBottom:F3}m**"
                  + "—— 模型要么悬空要么陷进地里，**这就是'移动看起来不对'的一半**");
        }

        GetTree().Quit(0);
    }

    /// <summary>把子树里所有 MeshInstance3D 的包围盒并起来（量"脚底在哪"用）。</summary>
    private static Aabb SubtreeAabb(Node3D root)
    {
        bool any = false;
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        void Walk(Node node, Transform3D xform)
        {
            Transform3D local = xform * (node is Node3D n3 ? n3.Transform : Transform3D.Identity);

            if (node is MeshInstance3D mesh)
            {
                Aabb box = mesh.GetAabb();
                for (int i = 0; i < 8; i++)
                {
                    Vector3 world = local * box.GetEndpoint(i);
                    min = min.Min(world);
                    max = max.Max(world);
                }

                any = true;
            }

            foreach (Node child in node.GetChildren())
                Walk(child, local);
        }

        Walk(root, Transform3D.Identity);
        return any ? new Aabb(min, max - min) : default;
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
