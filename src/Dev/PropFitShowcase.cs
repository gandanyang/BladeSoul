using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 道具装配验收（T27）：把**程序化生成的阵笠与打刀**（`tools/gen_props.py`）
/// 真的装到魔骸足兵身上，回答两件事：
///
/// 1. **装得上吗** —— 笠的落点不是手填的，是 `gen_props.py --fit` 对着足兵本体
///    求出来的（从下往上扫，第一个不穿模的高度 = 0.7015m，接触距离 0.29mm，
///    压低 2mm 立刻穿模 2 处）。这个场景把那个数字当**输入**用，
///    所以"盖住头、不穿模"在这里会被复现一次。
/// 2. **读起来对吗** —— 10 §1 要求"**一眼看得出它曾经是个人**"，
///    而阵笠是足兵身上唯一一件人造物。只有看图才能判断，数值断言做不到。
///
/// ⚠️ **必须带窗口跑**——`--headless` 是 dummy 渲染器，存出来是全黑。
/// ⚠️ 这是 `scenes/tests/` 下的**展示场景**：它不改 `Dojo.tscn`、不改敌人 actor。
///    真要把笠与刀挂到场上，是"谁装配敌人 actor 谁决定挂法"（见 T27-HANDOFF）。
///
/// 道具是**独立部件**（T39 的建议：先只生成人，笠与刀单独生成、每件都简单、可复用）。
/// 各自局部原点的含义（装配参考点，不是世界原点）：
///   - 阵笠：**笠缘平面的圆心**（倾斜已经烘进网格，所以这里不用再给角度）
///   - 打刀：**鍔**（握把与刀身的分界，+y 指向切先）
/// </summary>
public partial class PropFitShowcase : Node3D
{
    [ExportGroup("本体")]
    [Export] public string BodyModelPath { get; set; } = "res://assets/models/ashigaru_v2d_colored.glb";

    /// <summary>本体归一化后的身高（米）。10 §2.1 的设定身高是 1.70。</summary>
    [Export] public float BodyTargetHeight { get; set; } = 1.70f;

    [Export] public float BodyYawDegrees { get; set; }

    [ExportGroup("阵笠")]
    [Export] public string HatModelPath { get; set; } = "res://assets/models/jingasa_v1_colored.glb";

    /// <summary>
    /// 笠缘圆心在**本体局部空间**的位置。
    /// y 来自 `python tools/gen_props.py --what jingasa --fit assets/models/ashigaru_v2d_clean.glb`。
    /// </summary>
    [Export] public Vector3 HatOffset { get; set; } = new(0.0009f, 0.7015f, -0.0639f);

    [ExportGroup("打刀")]
    [Export] public string BladeModelPath { get; set; } = "res://assets/models/uchigatana_v1_colored.glb";

    /// <summary>握把（鍔）落在右手上——足兵是 A 字姿，右手在最外点 (0.680, 0.037, -0.057)。</summary>
    [Export] public Vector3 BladeOffset { get; set; } = new(0.680f, 0.037f, -0.057f);

    /// <summary>切先朝下的"拖刀"读法。挂法本身是**美术提案**，不是定案（见 T27-HANDOFF）。</summary>
    [Export] public Vector3 BladeRotationDegrees { get; set; } = new(180f, 0f, 0f);

    [ExportGroup("相机")]
    [Export] public float CameraFov { get; set; } = 42f;
    [Export] public float CameraMargin { get; set; } = 1.22f;

    [ExportGroup("画布")]
    [Export] public int Width { get; set; } = 1100;
    [Export] public int Height { get; set; } = 1100;

    [Export] public string OutputPath { get; set; } = "res://assets/references/t27_ashigaru_dressed.png";

    /// <summary>摆好之后等几帧再抓图——要等渲染器真的画过，否则可能抓到空帧。</summary>
    [Export] public int WarmupFrames { get; set; } = 8;

    private int _frames;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T27 · 足兵 + 阵笠 + 打刀";

        BuildEnvironment();
        BuildFloor();
        BuildLights();

        Node3D? body = Instantiate(BodyModelPath, "魔骸足兵");
        if (body is null)
        {
            GetTree().Quit(1);
            return;
        }

        bool any = false;
        Aabb bare = SubtreeAabb(body, Transform3D.Identity, ref any);
        float scale = bare.Size.Y > 0.001f ? BodyTargetHeight / bare.Size.Y : 1f;
        GD.Print($"[道具] 本体：原始高 {bare.Size.Y:F3}m → 缩放 ×{scale:F4}（目标 {BodyTargetHeight:F2}m）");

        // 一个 figure 节点持有 缩放 / 朝向，本体与道具都挂在它下面——
        // 这样道具的偏移直接用**本体局部空间的米**写，不必各自再换算一次。
        var figure = new Node3D
        {
            Name = "Figure",
            Scale = Vector3.One * scale,
            Position = new Vector3(0f, -bare.Position.Y * scale, 0f),
            RotationDegrees = new Vector3(0f, BodyYawDegrees, 0f),
        };

        AddChild(figure);
        figure.AddChild(body);

        Mount(figure, HatModelPath, "阵笠", HatOffset, Vector3.Zero);
        Mount(figure, BladeModelPath, "打刀", BladeOffset, BladeRotationDegrees);

        // ── 量出来（不是"看着对"）──
        bool _ = false;
        Aabb whole = SubtreeAabb(figure, Transform3D.Identity, ref _);
        GD.Print($"[道具] 装好后整体：宽 {whole.Size.X:F3} × 高 {whole.Size.Y:F3} × 深 {whole.Size.Z:F3} m");
        GD.Print($"[道具] 阵笠把总高从 {bare.Size.Y * scale:F3}m 抬到 {whole.Size.Y:F3}m");
        GD.Print($"[道具] 判断「盖住头」：笠缘平面 {(figure.Position.Y + HatOffset.Y * scale):F3}m，"
                 + $"本体头顶 {(figure.Position.Y + bare.End.Y * scale):F3}m "
                 + $"→ 笠缘在头顶{(HatOffset.Y < bare.End.Y ? "**下方**（罩住整颗头，阵笠的正常戴法）" : "上方（像顶了个盘子）")}");

        BuildCamera(whole);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(OutputPath);

        if (error == Error.Ok)
            GD.Print($"[道具] 已出图：{OutputPath}（{image.GetWidth()}×{image.GetHeight()}）");
        else
            GD.PrintErr($"[道具] 写不出去：{OutputPath}（{error}）");

        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private Node3D? Instantiate(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path))
        {
            GD.PrintErr($"[道具] 找不到模型：{path}");
            return null;
        }

        Node3D root = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        root.Name = label;
        EnsureVertexColorMaterial(root);
        return root;
    }

    private void Mount(Node3D parent, string path, string label, Vector3 offset, Vector3 rotationDegrees)
    {
        Node3D? prop = Instantiate(path, label);
        if (prop is null)
            return;

        prop.Position = offset;
        prop.RotationDegrees = rotationDegrees;
        parent.AddChild(prop);

        bool any = false;
        Aabb box = SubtreeAabb(prop, Transform3D.Identity, ref any);
        GD.Print($"[道具] {label}：局部偏移 {offset}  朝向 {rotationDegrees}  "
                 + $"自身包围盒 {box.Size.X:F3}×{box.Size.Y:F3}×{box.Size.Z:F3}m");
    }

    /// <summary>
    /// 魔骸与道具都**只有顶点色、没有贴图**（Hunyuan3D 与本工具都不产 UV）。
    /// glTF 导入不一定打开"顶点色当反照率"，那就补一个材质——
    /// 补的是**颜色通道的使用方式**，不是改颜色。
    ///
    /// ⚠️ 这段和 `ModelShowcase.ReportMaterials` 是同一件事。**故意没抽公共方法**：
    ///    `ModelShowcase.cs` 是 T35 的文件，多 agent 并行时改它风险大于这十几行重复。
    ///    真要做成公共的就等哪天有卡一起收（已记在 T27-HANDOFF）。
    /// </summary>
    private static void EnsureVertexColorMaterial(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is ArrayMesh arrayMesh)
        {
            bool hasVertexColor = false;
            for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
            {
                if ((arrayMesh.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatColor) != 0)
                    hasVertexColor = true;
            }

            if (hasVertexColor)
            {
                var material = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.85f,
                };

                for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
                    mesh.SetSurfaceOverrideMaterial(i, material);
            }
            else
            {
                GD.PrintErr($"[道具] {mesh.Name}：**没有顶点色**——引擎只能给白色反照率"
                            + "（跑过 tools/paint_mesh.py 了吗？）");
            }
        }

        foreach (Node child in node.GetChildren())
            EnsureVertexColorMaterial(child);
    }

    private static Aabb SubtreeAabb(Node node, Transform3D xform, ref bool any)
    {
        Transform3D local = xform * (node is Node3D n3 ? n3.Transform : Transform3D.Identity);
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        if (node is MeshInstance3D mesh)
        {
            Aabb box = mesh.GetAabb();

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = local * box.GetEndpoint(corner);
                min = min.Min(world);
                max = max.Max(world);
            }

            any = true;
        }

        foreach (Node child in node.GetChildren())
        {
            Aabb childBox = SubtreeAabb(child, local, ref any);

            if (childBox.Size == Vector3.Zero && childBox.Position == Vector3.Zero)
                continue;

            min = min.Min(childBox.Position);
            max = max.Max(childBox.End);
        }

        return any ? new Aabb(min, max - min) : default;
    }

    private void BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.06f, 0.07f, 0.09f),
            // 10 §3.1：低饱和青灰，暖色只留给灯笼
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.30f, 0.36f, 0.46f),
            AmbientLightEnergy = 0.55f,
        };

        AddChild(new WorldEnvironment { Environment = environment });
    }

    private void BuildFloor()
    {
        var floorMesh = new PlaneMesh { Size = new Vector2(14f, 14f) };
        floorMesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.21f, 0.24f),
            Roughness = 0.9f,
        };

        AddChild(new MeshInstance3D { Name = "Floor", Mesh = floorMesh });
    }

    private void BuildLights()
    {
        var key = new DirectionalLight3D
        {
            Name = "Key",
            LightEnergy = 1.5f,
            RotationDegrees = new Vector3(-42f, -35f, 0f),
        };

        AddChild(key);

        var rim = new DirectionalLight3D
        {
            Name = "Rim",
            LightEnergy = 0.6f,
            LightColor = new Color(0.7f, 0.8f, 1f),
            RotationDegrees = new Vector3(-20f, 150f, 0f),
        };

        AddChild(rim);
    }

    /// <summary>按整体包围盒自动取景——道具会改变总高，写死机位会有一版拍不全。</summary>
    private void BuildCamera(Aabb whole)
    {
        Vector3 center = whole.Position + whole.Size * 0.5f;
        float radius = Mathf.Max(whole.Size.Y, Mathf.Max(whole.Size.X, whole.Size.Z)) * 0.5f;
        float distance = radius / Mathf.Tan(Mathf.DegToRad(CameraFov * 0.5f)) * CameraMargin;

        var camera = new Camera3D
        {
            Name = "Shot",
            Fov = CameraFov,
            Position = center + new Vector3(0f, whole.Size.Y * 0.10f, distance),
        };

        AddChild(camera);
        camera.LookAt(center, Vector3.Up);
        camera.Current = true;
    }
}
