using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 静止站姿截图：证明"人物到底朝哪边、头在上还是下"。
///
/// 为什么需要它：模型文件、骨架静止姿势、绑定顶点全都正确，不代表**看起来**正确——
/// 朝向是缩略图级的事实，只能靠眼睛。无头模式是 dummy renderer，抓不到画面，
/// 所以这个场景要**带窗口**跑：
///
///     godot --path . res://scenes/tests/PoseShot.tscn -- --pose-out=user://pose.png
///
/// 一次出 4 张：正面 / 背面 / 左侧 / 右侧，从四个方向各看一遍，翻转无处可藏。
/// </summary>
public partial class PoseShotTest : Node3D
{
    [Export] public int Width { get; set; } = 720;
    [Export] public int Height { get; set; } = 900;

    /// <summary>等几帧再抓——第一帧模型还没变换完（贴图、材质都在这一帧才生效）。</summary>
    [Export] public int WarmupFrames { get; set; } = 12;

    /// <summary>
    /// 在空间 y=+0.6 放一颗红球、y=-0.6 放一颗蓝球。
    ///
    /// 这是**相机方向的校验**：红球必须出现在画面的上方。任何一次渲染出的图里
    /// 红球在下面，就说明相机/视口被翻了，那么"头朝下"就是这个翻转造成的假象，
    /// 而不是资产问题。没有这个基准，关于上下的一切讨论都没有落脚点。
    /// </summary>
    [Export] public bool ShowLandmarks { get; set; } = true;

    private int _frames;
    private int _shots;
    private Camera3D _camera = null!;
    private Node3D _player = null!;

    private static readonly (string Name, Vector3 From)[] Viewpoints =
    {
        ("front", new Vector3(0f, 1.05f, 3.2f)),
        ("back", new Vector3(0f, 1.05f, -3.2f)),
        ("left", new Vector3(3.2f, 1.05f, 0f)),
        ("right", new Vector3(-3.2f, 1.05f, 0f)),
    };

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "朝向体检 · 静止站姿";
        BuildStage();

        // 走真实的游戏路径：Player.tscn（含 PlayerActor + HumanoidAnimator + 缩放和 180° 朝向）。
        // 只渲染裸 glb 会漏掉这一层——而"头朝下"正是只在这一层才会出现的症状。
        var scene = GD.Load<PackedScene>("res://scenes/actors/Player.tscn");
        _player = scene.Instantiate<Node3D>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);
    }

    private Skeleton3D? FindSkeleton(Node n)
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

    /// <summary>
    /// 在**和截图完全相同的那条路径上**量骨骼的世界位置。
    /// 这一步是为了把"看起来倒立"和"确实是倒立"分开——如果 Head 的世界 y 仍然最大，
    /// 那看到的就是渲染/相机/我自己的错觉，而不是资产问题。
    /// </summary>
    private void ReportBones()
    {
        Skeleton3D? skel = FindSkeleton(_player);
        if (skel is null)
        {
            GD.Print("[朝向截图] 报告：找不到骨架");
            return;
        }

        GD.Print($"[朝向截图] 报告：骨架 {skel.GetBoneCount()} 骨，父节点链上的全局变换：");
        Transform3D xf = skel.GlobalTransform;
        GD.Print($"[朝向截图]   骨架 GlobalTransform origin=({xf.Origin.X:F3},{xf.Origin.Y:F3},{xf.Origin.Z:F3})"
                 + $" basisX=({xf.Basis.X.X:F2},{xf.Basis.X.Y:F2},{xf.Basis.X.Z:F2})"
                 + $" basisY=({xf.Basis.Y.X:F2},{xf.Basis.Y.Y:F2},{xf.Basis.Y.Z:F2})");
        GD.Print($"[朝向截图]   角色节点 GlobalTransform origin=({_player.GlobalPosition.X:F3},"
                 + $"{_player.GlobalPosition.Y:F3},{_player.GlobalPosition.Z:F3})");

        string[] watch = { "Head", "Hip", "L_Toe", "R_Toe", "L_Hand" };
        foreach (string nm in watch)
        {
            int b = skel.FindBone(nm);
            if (b < 0)
            {
                GD.Print($"[朝向截图]   {nm,-8} 不存在");
                continue;
            }

            // 姿势链自己合成（GetBoneGlobalPose 在无头路径上不刷新，这里带窗口跑也照做以求一致）
            Transform3D g = Transform3D.Identity;
            var chain = new System.Collections.Generic.List<int>();
            for (int i = b; i >= 0; i = skel.GetBoneParent(i))
                chain.Insert(0, i);
            foreach (int i in chain)
                g *= skel.GetBonePose(i);

            Vector3 w = skel.GlobalTransform * g.Origin;
            GD.Print($"[朝向截图]   {nm,-8} 姿势合成局部=({g.Origin.X:F3},{g.Origin.Y:F3},{g.Origin.Z:F3})"
                     + $"  → 世界=({w.X:F3},{w.Y:F3},{w.Z:F3})");
        }
    }
    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        if (_shots >= Viewpoints.Length)
        {
            GD.Print($"[朝向截图] {Viewpoints.Length} 张出齐");
            GetTree().Quit(0);
            return;
        }

        if (_shots == 0)
            ReportBones();

        (string name, Vector3 from) = Viewpoints[_shots];
        _camera.LookAtFromPosition(from + new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.95f, 0f));

        // 换机位之后必须再等一帧，否则抓到的是上一个机位的画面。
        if (_frames % 2 != 0)
            return;

        string path = $"user://pose_{name}.png";
        Image image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr("[朝向截图] 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[朝向截图] {name,-6} → {ProjectSettings.GlobalizePath(path)}"
            : $"[朝向截图] 存图失败 {path}: {error}");

        _shots++;
        _frames++;
    }

    private void BuildStage()
    {
        var floor = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(12f, 0.2f, 12f) },
            Position = new Vector3(0f, -0.98f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.3f) },
        };
        AddChild(floor);

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -35f, 0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        });

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.11f, 0.12f, 0.14f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.44f, 0.5f),
                AmbientLightEnergy = 0.8f,
            },
        });

        _camera = new Camera3D { Fov = 40f };
        AddChild(_camera);
        _camera.LookAtFromPosition(new Vector3(0f, 1.05f, 3.2f), new Vector3(0f, 0.05f, 0f));
        _camera.Current = true;

        // 三条参考线：地面 / y=0.702（Head 静止高度）/ y=-0.976（L_Toe 静止高度）。
        // 图里"头顶"应该刚好压在上面的线附近，脚底压在下面的线附近。
        AddGuide(-0.98f, new Color(0.9f, 0.35f, 0.3f));
        AddGuide(0.702f, new Color(0.35f, 0.85f, 0.45f));
        AddGuide(-0.976f, new Color(0.35f, 0.6f, 0.95f));

        // 上下方向的基准球：红的在上（y=+0.6），蓝的在下（y=-0.6）。
        // 画面上红球必须在上面——否则是相机翻了，不是模型翻了。
        if (ShowLandmarks)
        {
            AddBall(new Vector3(-0.75f, 0.75f, 0f), new Color(1f, 0.15f, 0.15f), "红球 (应该是画面上方)");
            AddBall(new Vector3(-0.75f, -0.75f, 0f), new Color(0.15f, 0.35f, 1f), "蓝球 (应该是画面下方)");
        }
    }

    private void AddBall(Vector3 at, Color color, string note)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.34f },
            Position = at,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });
        GD.Print($"[朝向截图] 基准球 {note} @ y={at.Y:F2}");
    }

    private void AddGuide(float y, Color color)
    {
        var line = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.004f, 0.004f) },
            Position = new Vector3(0f, y * 0.888f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(line);
    }
}
