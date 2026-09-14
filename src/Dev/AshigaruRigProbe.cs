using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 一次性探针（T52 前置验证）：**魔骸足兵的骨架在 Godot 侧到底能不能用**。
///
/// 为什么值得单独写这个：绑骨脚本自检只在 **glb 的 JSON 里**证明了 `skins=1 / joints=21`，
/// 那不等于"Godot 导入了 Skeleton3D、骨名能查到、rest 旋转可用"。
/// 本项目栽过一次同类跟头——资产"导出了"就以为能用（AGENTS.md §6：新资产没有 `.import`，
/// `ResourceLoader.Exists` 直接返回 false）。
///
/// 判据（任一不过就是失败，不许打勾）：
///   1. 能实例化 glb（PackedScene，不是 Mesh——T33 那次的坑）
///   2. 里面能找到 `Skeleton3D`（找不到 = Godot 把骨架退化成普通节点）
///   3. `HumanoidAnimator` 的 12 个 Tracked 骨名**一个不少**（少一个动画器就什么都不做）
///   4. 打印每根骨的 rest 旋转角——用来判断能否照搬 `rest * q` 的写法
///      （玩家模型里 L_Thigh 是 180°、L_Upperarm 是 101.4°，正是"像一张纸被翻折"的根因）
/// </summary>
public partial class AshigaruRigProbe : Node
{
    private const string ModelPath = "res://assets/models/ashigaru_rigged.glb";

    private static readonly string[] Tracked =
    {
        "Hip", "Spine01", "Spine02", "Head",
        "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
        "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
    };

    public override void _Ready()
    {
        GD.Print("[足兵骨架] ── 开始验证 ──");

        if (!ResourceLoader.Exists(ModelPath))
        {
            GD.PrintErr($"[足兵骨架] ✗ {ModelPath} 不存在（没有 .import？跑 godot --headless --import）");
            GetTree().Quit(1);
            return;
        }

        var packed = GD.Load<PackedScene>(ModelPath);
        if (packed is null)
        {
            GD.PrintErr("[足兵骨架] ✗ 加载失败");
            GetTree().Quit(1);
            return;
        }

        Node model = packed.Instantiate();
        AddChild(model);

        GD.Print($"[足兵骨架] 实例化成功，节点类型 = {model.GetType().Name}");

        Skeleton3D? skel = FindSkeleton(model);
        if (skel is null)
        {
            GD.PrintErr("[足兵骨架] ✗ 没找到 Skeleton3D —— Godot 把骨架退化成普通节点了，" +
                         "动画器无从下手（这正是「权重写了、skin 没写」的症状）");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"[足兵骨架] ✓ Skeleton3D 找到，骨数 = {skel.GetBoneCount()}");

        int found = 0;
        foreach (string name in Tracked)
        {
            int bone = skel.FindBone(name);
            if (bone < 0)
            {
                GD.PrintErr($"[足兵骨架] ✗ 缺骨 {name}");
                continue;
            }

            found++;

            // rest 旋转角：照抄 MoveProbe 的做法，用欧拉分量的最大绝对值，
            // **不用 quaternion 的 |dot|**（它把 0° 和 180° 看成一样）。
            Vector3 euler = skel.GetBoneRest(bone).Basis.GetRotationQuaternion().GetEuler();
            float deg = Mathf.Max(Mathf.Abs(Mathf.RadToDeg(euler.X)),
                                  Mathf.Max(Mathf.Abs(Mathf.RadToDeg(euler.Y)),
                                            Mathf.Abs(Mathf.RadToDeg(euler.Z))));
            GD.Print($"[足兵骨架]   {name,-12} index={bone,2}  rest 最大角 {deg,7:F1}°");
        }

        GD.Print($"[足兵骨架] Tracked 命中 {found}/{Tracked.Length}");

        // 蒙皮网格是否真的被骨架驱动
        int skinnedMeshes = CountSkinned(model);
        GD.Print($"[足兵骨架] 带蒙皮的 MeshInstance3D = {skinnedMeshes}");

        bool ok = found == Tracked.Length && skinnedMeshes > 0;
        GD.Print(ok
            ? "[足兵骨架] ✓ 通过（骨架可用，命名与 HumanoidAnimator 的 Tracked 完全对齐）"
            : "[足兵骨架] ✗ 失败");

        GetTree().Quit(ok ? 0 : 1);
    }

    private static int CountSkinned(Node node)
    {
        int n = node is MeshInstance3D { Skin: not null } ? 1 : 0;
        foreach (Node child in node.GetChildren())
            n += CountSkinned(child);
        return n;
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
