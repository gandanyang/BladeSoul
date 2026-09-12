using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 同屏辨识截图（T35 验收 2）：把主角与**本地生成的魔骸足兵**并排摆在同一光照下，
/// 渲染一帧存成 PNG，用来回答 10 §1 的"一眼分得清谁是人"。
///
/// ⚠️ **必须带窗口跑**——`--headless` 是 dummy 渲染器，存出来是全黑：
///     godot --path . res://scenes/tests/ModelShowcase.tscn
///
/// 两个已知的坑，脚本自己会打印出来：
/// 1. **身高不一致**：主角模型只有 0.998m，魔骸是 1.700m。不归一化的话，
///    这张图验的是"谁高"而不是"谁是人"。所以按**各自设定身高**归一
///    （<see cref="PlayerTargetHeight"/> / <see cref="EnemyTargetHeight"/> / <see cref="ThirdTargetHeight"/>）。
/// 2. **魔骸没有材质**，只有顶点色（Hunyuan3D 的产物没有 UV/贴图）。
///    Godot 的 glTF 导入不一定打开"顶点色当反照率"，所以这里显式补一个材质，
///    否则魔骸会渲染成纯白——那不是模型的样子，是导入设置的样子。
///
/// 退出码 0 = 已出图，1 = 出错。
/// </summary>
public partial class ModelShowcase : Node3D
{
    [ExportGroup("模型")]
    [Export] public string PlayerModelPath { get; set; } = "res://assets/models/model_player_congyun_01.glb";
    [Export] public string EnemyModelPath { get; set; } = "res://assets/models/ashigaru_v2d_clean.glb";

    /// <summary>第三个模型位（留空 = 不放）。T29 的"三人同框"用它放「绫」。</summary>
    [Export] public string ThirdModelPath { get; set; } = "";
    [Export] public string ThirdLabel { get; set; } = "绫";
    [Export] public string OutputPath { get; set; } = "res://assets/references/t35_model_showcase.png";

    /// <summary>
    /// 归一化身高（米）——**每个模型各自的设定身高**，不是统一值。
    /// 统一值会把"谁高"这件事抹掉（丛云 1.75 / 魔骸 1.70 / 绫 1.62 是设定）。
    /// </summary>
    [Export] public float PlayerTargetHeight { get; set; } = 1.70f;
    [Export] public float EnemyTargetHeight { get; set; } = 1.70f;
    [Export] public float ThirdTargetHeight { get; set; } = 1.70f;

    /// <summary>两人中心之间的距离（米）。</summary>
    [Export] public float Spacing { get; set; } = 1.6f;

    /// <summary>模型的朝向修正（度）。主角模型的正面不是 -Z，所以这里转回来。</summary>
    [Export] public float PlayerYawDegrees { get; set; } = 90f;
    [Export] public float EnemyYawDegrees { get; set; } = 0f;
    [Export] public float ThirdYawDegrees { get; set; } = 0f;

    [ExportGroup("相机")]
    [Export] public float CameraDistance { get; set; } = 3.4f;
    [Export] public float CameraHeight { get; set; } = 1.4f;
    [Export] public float LookAtHeight { get; set; } = 0.95f;

    [ExportGroup("画布")]
    [Export] public int Width { get; set; } = 1280;
    [Export] public int Height { get; set; } = 720;

    /// <summary>摆好之后等几帧再抓图——要等渲染器真的画过，否则可能抓到空帧。</summary>
    [Export] public int WarmupFrames { get; set; } = 8;

    private int _frames;
    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T35 · 主角 vs 本地生成的魔骸";

        BuildEnvironment();
        BuildFloor();
        BuildLights();

        (string Path, float Height, float Yaw, string Label)[] slots =
        {
            (PlayerModelPath, PlayerTargetHeight, PlayerYawDegrees, "玩家"),
            (EnemyModelPath, EnemyTargetHeight, EnemyYawDegrees, "魔骸"),
            (ThirdModelPath, ThirdTargetHeight, ThirdYawDegrees, ThirdLabel),
        };

        int count = 0;
        foreach ((string path, _, _, _) in slots)
        {
            if (!string.IsNullOrWhiteSpace(path))
                count++;
        }

        int slot = 0;
        foreach ((string path, float height, float yaw, string label) in slots)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            float x = (slot - (count - 1) * 0.5f) * Spacing;
            Place(path, new Vector3(x, 0f, 0f), label, yaw, height);
            slot++;
        }

        BuildCamera();

        if (count == 0)
        {
            GD.PrintErr("[同屏] 一个模型都没配");
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(OutputPath);

        if (error == Error.Ok)
            GD.Print($"[同屏] 已出图：{OutputPath}（{image.GetWidth()}×{image.GetHeight()}）");
        else
            GD.PrintErr($"[同屏] 写不出去：{OutputPath}（{error}）");

        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private Node3D? Place(string path, Vector3 position, string label, float yawDegrees, float targetHeight)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr($"[同屏] 找不到模型：{path}");
            return null;
        }

        var scene = GD.Load<PackedScene>(path);
        Node3D root = scene.Instantiate<Node3D>();
        root.Name = label;
        root.Position = position;
        root.RotationDegrees += new Vector3(0f, yawDegrees, 0f);
        AddChild(root);

        // 量身高 → 归一化 → 让脚站到 y=0
        bool any = false;
        Aabb box = SubtreeAabb(root, Transform3D.Identity, ref any);

        if (!any)
        {
            GD.PrintErr($"[同屏] {label} 里没有网格");
            return root;
        }

        float scale = targetHeight > 0f && box.Size.Y > 0.001f ? targetHeight / box.Size.Y : 1f;
        root.Scale = Vector3.One * scale;
        root.Position = position + new Vector3(0f, -box.Position.Y * scale, 0f);

        GD.Print($"[同屏] {label}：原始高 {box.Size.Y:F3}m → 缩放 ×{scale:F3}（目标 {targetHeight:F2}m）");
        ReportMaterials(root, label);
        return root;
    }

    /// <summary>
    /// 魔骸只有顶点色、没有材质。glTF 导入不一定打开"顶点色当反照率"，
    /// 那就补一个——补的是**颜色通道的使用方式**，不是改颜色。
    /// </summary>
    private void ReportMaterials(Node node, string label)
    {
        if (node is MeshInstance3D mesh)
        {
            int surfaces = mesh.GetSurfaceOverrideMaterialCount();
            Material? material = mesh.Mesh is null || mesh.Mesh.GetSurfaceCount() == 0
                ? null
                : mesh.GetActiveMaterial(0);

            if (mesh.Mesh is ArrayMesh arrayMesh)
            {
                for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
                    GD.Print($"[同屏] {label} surface {i} 顶点格式: {arrayMesh.SurfaceGetFormat(i)}");
            }

            bool hasVertexColor = false;

            if (mesh.Mesh is ArrayMesh colored)
            {
                for (int i = 0; i < colored.GetSurfaceCount(); i++)
                {
                    if ((colored.SurfaceGetFormat(i) & Mesh.ArrayFormat.FormatColor) != 0)
                        hasVertexColor = true;
                }
            }

            bool usesVertexColor = material is StandardMaterial3D standard && standard.VertexColorUseAsAlbedo;
            bool textureless = material is StandardMaterial3D noTexture && noTexture.AlbedoTexture is null;

            // 判据：**没有贴图 ＋ 没启用顶点色** → 这个模型现在渲染出来是"一坨单色"，
            // 而它的颜色其实存在顶点色里。补的是**颜色通道的使用方式**，不是改颜色。
            // 有贴图的模型（主角）不会命中这里。
            if (mesh.Mesh is not null && hasVertexColor && (material is null || (textureless && !usesVertexColor)))
            {
                var fallback = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.85f,
                };

                for (int i = 0; i < mesh.Mesh!.GetSurfaceCount(); i++)
                    mesh.SetSurfaceOverrideMaterial(i, fallback);

                GD.Print($"[同屏] {label}：有顶点色但没启用 → 已补「顶点色当反照率」"
                         + $"（{mesh.Mesh.GetSurfaceCount()} 个 surface）");
            }
            else if (!hasVertexColor && textureless)
            {
                GD.Print($"[同屏] {label}：**没有贴图也没有顶点色**——引擎只能给它一个白色反照率。"
                         + "这是资产的真实状态（T27 补 UV/贴图之前就是这样）。");
            }
            else
            {
                GD.Print($"[同屏] {label}：材质 {material?.GetType().Name ?? "无"}，"
                         + $"顶点色当反照率={usesVertexColor}，覆盖槽={surfaces}");
            }
        }

        foreach (Node child in node.GetChildren())
            ReportMaterials(child, label);
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

    private void BuildCamera()
    {
        var camera = new Camera3D
        {
            Name = "Shot",
            Fov = 42f,
            Position = new Vector3(0f, CameraHeight, CameraDistance),
        };

        AddChild(camera);
        camera.LookAt(new Vector3(0f, LookAtHeight, 0f), Vector3.Up);
        camera.Current = true;
    }
}
