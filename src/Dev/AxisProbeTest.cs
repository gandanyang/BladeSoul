using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 打印 `HumanoidAnimator` 缓存的两个旋转轴在世界空间里到底指向哪。
///
/// 为什么需要它：`Rot(name, side, rise)` 不是绕"世界右/后"转，而是绕
/// `rest.Inverse() * Vector3.Right`。这个轴的正确性只有把结果画出来才知道：
/// 如果 `L_Thigh` 的轴其实指向"世界右"，那么 `Rot("L_Thigh", 0.4)` 就是把腿
/// **向侧面踢出去**（或向上抬），而不是前后迈步——腿会看起来"朝上翘"。
///
/// 这个探针把每个关键骨的两个轴在**世界空间**里打出来，并明确标注：
/// 轴是不是水平的、是不是前后方向（前后 = 迈步，上下/左右 = 不是迈步）。
///
///     godot --headless --path . res://scenes/tests/AxisProbe.tscn
/// </summary>
public partial class AxisProbeTest : Node3D
{
    private static readonly string[] Watch =
    {
        "Hip", "Spine01", "Spine02", "Neck", "Head",
        "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
        "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
    };

    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://assets/models/model_player_congyun_03_textured.glb");
        if (scene is null)
        {
            GD.PrintErr("[轴体检] 模型加载失败");
            GetTree().Quit(1);
            return;
        }

        Node root = scene.Instantiate();
        AddChild(root);

        Skeleton3D? skel = FindSkeleton(root);
        if (skel is null)
        {
            GD.PrintErr("[轴体检] 找不到骨架");
            GetTree().Quit(1);
            return;
        }

        GD.Print("[轴体检] 轴说明：side=「侧摆」轴，rise=「前后/起伏」轴（都在世界空间）");
        GD.Print("[轴体检] 判据：Thigh / Upperarm 的 side 轴应该接近「前后」(±Z 或 ±X，水平)");
        GD.Print("[轴体检]       side 轴如果有明显的 Y 分量（|Y| 大），转动就会把肢体抬起来");

        foreach (string name in Watch)
        {
            int bone = skel.FindBone(name);
            if (bone < 0)
            {
                GD.Print($"[轴体检] {name,-12} 不存在");
                continue;
            }

            Basis rest = skel.GetBoneGlobalRest(bone).Basis;

            // 与 HumanoidAnimator 构造函数逐字一致
            Vector3 side = (rest.Inverse() * Vector3.Right).Normalized();
            Vector3 rise = (rest.Inverse() * Vector3.Back).Normalized();

            // 换回世界空间看它们实际指向哪
            Vector3 sideWorld = (rest * side).Normalized();
            Vector3 riseWorld = (rest * rise).Normalized();

            string sideVerdict = AxisVerdict(sideWorld);
            string riseVerdict = AxisVerdict(riseWorld);

            GD.Print($"[轴体检] {name,-12} side 世界=({sideWorld.X,6:F2},{sideWorld.Y,6:F2},{sideWorld.Z,6:F2}) {sideVerdict}");
            GD.Print($"[轴体检] {"",-12} rise 世界=({riseWorld.X,6:F2},{riseWorld.Y,6:F2},{riseWorld.Z,6:F2}) {riseVerdict}");
        }

        GD.Print("[轴体检] 完");
        GetTree().Quit(0);
    }

    /// <summary>把一个世界空间的轴翻译成人话。</summary>
    private static string AxisVerdict(Vector3 axis)
    {
        float ax = Mathf.Abs(axis.X);
        float ay = Mathf.Abs(axis.Y);
        float az = Mathf.Abs(axis.Z);

        string dir;
        if (ay >= ax && ay >= az)
            dir = axis.Y > 0f ? "上下轴（↑）——转动会把肢体抬/压" : "上下轴（↓）——转动会把肢体抬/压";
        else if (az >= ax)
            dir = "前后轴（Z）——这才是迈步/抡臂该绕的轴";
        else
            dir = "左右轴（X）——侧摆";

        return $"→ {dir}";
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
}
