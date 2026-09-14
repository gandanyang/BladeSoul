using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 用**材质**判定模型哪一头是头、哪一头是脚。
///
/// 为什么需要它：连续几轮里，"头朝下还是朝上"都是靠渲染图 + 文字描述来判断的，
/// 而描述反复自相矛盾（同一张图既被说成"头顶在上三分之一"，又被说成"整体倒立"）。
/// 材质名是**数据**，不会随描述漂移：靴子/绑腿的材质名和头盔/头发的材质名不同，
/// 只要看 y 最高那一段和 y 最低那一段各自是什么材质，朝向就没有争议了。
///
///     godot --headless --path . res://scenes/tests/MaterialProbe.tscn
/// </summary>
public partial class MaterialProbeTest : Node3D
{
    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://assets/models/model_player_congyun_03_textured.glb");
        if (scene is null)
        {
            GD.PrintErr("[材质体检] 模型加载失败");
            GetTree().Quit(1);
            return;
        }

        Node root = scene.Instantiate();
        AddChild(root);

        MeshInstance3D? mesh = FindMesh(root);
        if (mesh is null)
        {
            GD.PrintErr("[材质体检] 找不到网格");
            GetTree().Quit(1);
            return;
        }

        Mesh m = mesh.Mesh;
        GD.Print($"[材质体检] surface {m.GetSurfaceCount()} 个");

        // 先收集全部顶点，求绑定位姿的包围盒。
        var all = new System.Collections.Generic.List<(Vector3 P, int Surf)>();
        int surfaces = m.GetSurfaceCount();
        for (int s = 0; s < surfaces; s++)
        {
            Godot.Collections.Array arrays = m.SurfaceGetArrays(s);
            if (arrays.Count == 0)
                continue;

            var v = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (Vector3 p in v)
                all.Add((p, s));
        }

        if (all.Count == 0)
        {
            GD.PrintErr("[材质体检] 没有顶点");
            GetTree().Quit(1);
            return;
        }

        float lo = float.MaxValue;
        float hi = float.MinValue;
        foreach ((Vector3 p, int _) in all)
        {
            lo = Mathf.Min(lo, p.Y);
            hi = Mathf.Max(hi, p.Y);
        }

        GD.Print($"[材质体检] 顶点 {all.Count} 个，y 范围 {lo:F3} .. {hi:F3}");

        // 按 y 分 10 层，每层列出各 surface 的顶点数和材质名。
        for (int k = 0; k < 10; k++)
        {
            float a = lo + (hi - lo) * k / 10f;
            float b = lo + (hi - lo) * (k + 1) / 10f;
            var perSurface = new System.Collections.Generic.Dictionary<int, int>();
            foreach ((Vector3 p, int s) in all)
            {
                if (p.Y < a || p.Y >= b)
                    continue;
                perSurface.TryGetValue(s, out int c);
                perSurface[s] = c + 1;
            }

            var parts = new System.Collections.Generic.List<string>();
            foreach (System.Collections.Generic.KeyValuePair<int, int> kv in perSurface)
            {
                var mat = m.SurfaceGetMaterial(kv.Key) as StandardMaterial3D;
                string name = mat?.ResourceName ?? "?";
                var col = mat?.AlbedoColor ?? Colors.White;
                parts.Add($"{name}({col.ToHtml(false)}) x{kv.Value}");
            }

            parts.Sort();
            GD.Print($"[材质体检] 第{k + 1,2}层 y {a,6:F2}..{b,6:F2}  {string.Join("  ", parts)}");
        }

        // 关键判据：把最上面一层和最下面一层的材质名并排打出来。
        GD.Print("[材质体检] ── 判据 ──");
        GD.Print("[材质体检] 最上层的材质名应该像「头/发/头盔」，最下层应该像「靴/腿/脚」");

        GetTree().Quit(0);
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
