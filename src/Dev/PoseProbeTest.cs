using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 静止姿态下的朝向体检：把头部、脚底、武器、骨骼高度一次性摊开。
///
/// 用途：当有人说"人物头朝下/翻了"时，这一屏数字直接给出答案——
/// 头顶在哪、脚底在哪、脊椎是不是往上的。不需要靠肉眼猜。
/// </summary>
public partial class PoseProbeTest : Node3D
{
    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://assets/models/model_player_congyun_03_textured.glb");
        if (scene is null)
        {
            GD.Print("[朝向体检] ✗ 模型加载失败");
            GetTree().Quit(1);
            return;
        }

        Node root = scene.Instantiate();
        AddChild(root);

        var skel = FindSkeleton(root);
        var mesh = FindMesh(root);
        if (skel is null || mesh is null)
        {
            GD.Print($"[朝向体检] ✗ 没找到骨架({skel is not null})或网格({mesh is not null})");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"[朝向体检] 骨架 {skel.GetBoneCount()} 根骨，网格 surface {mesh.Mesh.GetSurfaceCount()} 个");

        // ── 骨骼的静止世界高度 ──
        GD.Print("[朝向体检] 骨骼静止世界位置（x, y, z；y 越大越高）：");
        string[] watch = { "Head", "Neck", "Spine02", "Hip", "L_Foot", "L_Toe", "R_Foot", "R_Toe",
                           "L_Hand", "R_Hand", "Weapon_R", "Scabbard" };
        foreach (string name in watch)
        {
            int b = skel.FindBone(name);
            if (b < 0)
            {
                GD.Print($"[朝向体检]   {name,-12} 不存在");
                continue;
            }

            Vector3 g = skel.GetBoneGlobalRest(b).Origin;
            GD.Print($"[朝向体检]   {name,-12} ({g.X:F3}, {g.Y:F3}, {g.Z:F3})");
        }

        // ── ResetBonePoses() 到底做了什么：姿势位置会不会被清零 ──
        // 这是 HumanoidAnimator.AnimateLocomotion 每帧的第一句。如果它把骨骼位置
        // 清成 0，整条骨链就会塌到父骨头上，蒙皮后的顶点自然一团糟。
        GD.Print("[朝向体检] ResetBonePoses() 前后的姿势（bone, pose.Origin）：");
        foreach (string nm2 in new[] { "Hip", "Spine02", "Neck", "L_Thigh", "L_Calf", "L_Toe", "L_Hand" })
        {
            int b2 = skel.FindBone(nm2);
            if (b2 < 0)
                continue;
            Transform3D before = skel.GetBonePose(b2);
            Vector3 rest2 = skel.GetBoneRest(b2).Origin;
            GD.Print($"[朝向体检]   {nm2,-10} 复位前 pose.Origin=({before.Origin.X:F3},{before.Origin.Y:F3},{before.Origin.Z:F3})"
                     + $" rest.Origin=({rest2.X:F3},{rest2.Y:F3},{rest2.Z:F3})");
        }

        skel.ResetBonePoses();

        foreach (string nm2 in new[] { "Hip", "Spine02", "Neck", "L_Thigh", "L_Calf", "L_Toe", "L_Hand" })
        {
            int b2 = skel.FindBone(nm2);
            if (b2 < 0)
                continue;
            Transform3D after = skel.GetBonePose(b2);
            Vector3 rest2 = skel.GetBoneRest(b2).Origin;
            string verdict = after.Origin.DistanceTo(rest2) < 0.001f ? "与 rest 一致 ✓" : "✗ 与 rest 不一致";
            GD.Print($"[朝向体检]   {nm2,-10} 复位后 pose.Origin=({after.Origin.X:F3},{after.Origin.Y:F3},{after.Origin.Z:F3})"
                     + $" rest.Origin=({rest2.X:F3},{rest2.Y:F3},{rest2.Z:F3})  {verdict}");
        }
        // ── 网格顶点的世界包围盒 ──
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int verts = 0;
        for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = mesh.Mesh.SurfaceGetArrays(s);
            if (arrays.Count == 0)
                continue;

            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            verts += v.Length;
            foreach (Vector3 p in v)
            {
                lo = new Vector3(Mathf.Min(lo.X, p.X), Mathf.Min(lo.Y, p.Y), Mathf.Min(lo.Z, p.Z));
                hi = new Vector3(Mathf.Max(hi.X, p.X), Mathf.Max(hi.Y, p.Y), Mathf.Max(hi.Z, p.Z));
            }
        }

        GD.Print($"[朝向体检] 网格顶点 {verts} 个，绑定位姿包围盒 "
                 + $"x[{lo.X:F3},{hi.X:F3}] y[{lo.Y:F3},{hi.Y:F3}] z[{lo.Z:F3},{hi.Z:F3}]");

        // ── 骨骼挂接链：逐级向上打印 Head 的父链，看有没有哪一环被翻了 ──
        int hb = skel.FindBone("Head");
        if (hb >= 0)
        {
            GD.Print("[朝向体检] Head 的父链（从根往下）：");
            var chain = new System.Collections.Generic.List<int>();
            for (int b = hb; b >= 0; b = skel.GetBoneParent(b))
                chain.Insert(0, b);
            foreach (int b in chain)
            {
                Transform3D t = skel.GetBoneGlobalRest(b);
                Vector3 up = t.Basis.Y;
                GD.Print($"[朝向体检]   {skel.GetBoneName(b),-12} origin=({t.Origin.X:F3},{t.Origin.Y:F3},{t.Origin.Z:F3})"
                         + $" 局部Y轴=({up.X:F2},{up.Y:F2},{up.Z:F2})");
            }
        }

        // ── 姿势基（不是静止基）里有没有哪个骨骼带着 180° 翻转 ──
        GD.Print("[朝向体检] 每根骨的姿势基（相对父骨；静止时应各自等于 rest 基）：");
        for (int b = 0; b < skel.GetBoneCount(); b++)
        {
            Transform3D t = skel.GetBonePose(b);
            Vector3 x = t.Basis.X;
            Vector3 y = t.Basis.Y;
            Vector3 z = t.Basis.Z;
            bool flipped = y.Y < 0f;
            GD.Print($"[朝向体检]   {skel.GetBoneName(b),-14} pose局部X=({x.X:F2},{x.Y:F2},{x.Z:F2})"
                     + $" Y=({y.X:F2},{y.Y:F2},{y.Z:F2}) Z=({z.X:F2},{z.Y:F2},{z.Z:F2})"
                     + (flipped ? "   ← Y 轴朝下" : ""));
        }

        // ── 蒙皮后的实际位置：把静止姿势下每个顶点算出来（走 Skeleton 的蒙皮矩阵）──
        GD.Print("[朝向体检] 结论判据：Head 的 y 必须 > Hip 的 y，且 L_Toe 的 y 必须 < Hip 的 y");

        GetTree().Quit(0);
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s)
            return s;
        foreach (Node c in n.GetChildren())
        {
            Skeleton3D? r = FindSkeleton(c);
            if (r is not null)
                return r;
        }

        return null;
    }

    private static MeshInstance3D? FindMesh(Node n)
    {
        if (n is MeshInstance3D m && m.Mesh is not null && m.Mesh.GetSurfaceCount() > 0)
            return m;
        foreach (Node c in n.GetChildren())
        {
            MeshInstance3D? r = FindMesh(c);
            if (r is not null)
                return r;
        }

        return null;
    }
}
