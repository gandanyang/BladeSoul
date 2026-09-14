using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 资产品质对照截图：把原始 glb 和当前 glb **并排渲染**，一次出两张同名角度的图。
///
/// 为什么需要它：反复用文字描述"是不是头朝下""哪里畸形"已经证明不可靠——
/// 描述会随相机角度漂移，而**并排的同一机位两张图**不会。
/// 把两张图交给视觉工具做 diff，或者直接给人看，结论就只有一个。
///
///     godot --path . res://scenes/tests/AssetCompare.tscn
///
/// 左边（x = -1.6）是 A 组，右边（x = +1.6）是 B 组。默认两组都是当前项目里的模型，
/// 所以第一次跑是"自己和自己"，用来验证机位；把 --asset-a / --asset-b 指到两个文件
/// 就能做真正的对照。
/// </summary>
public partial class AssetCompareTest : Node3D
{
    [Export] public int Width { get; set; } = 1400;
    [Export] public int Height { get; set; } = 900;

    /// <summary>等几帧再抓——材质和贴图要到第一帧之后才生效。</summary>
    [Export] public int WarmupFrames { get; set; } = 14;

    /// <summary>两组模型之间的横向间距（米）。</summary>
    [Export] public float Separation { get; set; } = 1.7f;

    /// <summary>模型缩放，和 `Player.tscn` 里的 VisualModelScale 保持一致。</summary>
    [Export] public float ModelScale { get; set; } = 0.888f;

    private int _frames;
    private int _shots;
    private Camera3D _camera = null!;

    private static readonly (string Name, Vector3 Dir)[] Viewpoints =
    {
        ("front", new Vector3(0f, 0f, 1f)),
        ("side", new Vector3(1f, 0f, 0f)),
    };

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "资产对照 · 左=A 右=B";
        BuildStage();

        string a = ArgValue("--asset-a", "res://assets/models/model_player_congyun_03_textured.glb");
        string b = ArgValue("--asset-b", "res://assets/models/model_player_congyun_03_textured.glb");
        Spawn(a, -Separation * 0.5f, "A");
        Spawn(b, Separation * 0.5f, "B");

        GD.Print($"[资产对照] A = {a}");
        GD.Print($"[资产对照] B = {b}");
    }

    private static string ArgValue(string flag, string fallback)
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == flag)
                return args[i + 1];
        return fallback;
    }

    private void Spawn(string path, float x, string tag)
    {
        var scene = GD.Load<PackedScene>(path);
        if (scene is null)
        {
            GD.PrintErr($"[资产对照] {tag} 加载失败：{path}");
            return;
        }

        var node = scene.Instantiate<Node3D>();
        node.Name = tag;
        node.Scale = Vector3.One * ModelScale;
        node.Position = new Vector3(x, 0f, 0f);

        // 模型朝向统一：和 Player.tscn 的 VisualModelRotationDegrees 一致，
        // 这样两张图才是同一条件下拍的。
        node.RotationDegrees = new Vector3(0f, 180f, 0f);
        AddChild(node);

        GD.Print($"[资产对照] {tag} @ x={x:F2}");
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        if (_shots >= Viewpoints.Length)
        {
            GD.Print("[资产对照] 出齐");
            GetTree().Quit(0);
            return;
        }

        (string name, Vector3 dir) = Viewpoints[_shots];
        _camera.LookAt(new Vector3(0f, 0.02f, 0f));
        _camera.Position = dir * 2.6f + new Vector3(0f, 0.05f, 0f);

        // 换机位后等一帧，否则抓到的是上一个机位的画面。
        if (_frames % 2 != 0)
            return;

        string path = $"user://compare_{name}.png";
        Image image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() == 0)
        {
            GD.PrintErr("[资产对照] 抓到空帧——需要带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[资产对照] {name,-6} → {ProjectSettings.GlobalizePath(path)}"
            : $"[资产对照] 存图失败 {path}: {error}");

        _shots++;
        _frames++;
    }

    private void BuildStage()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(14f, 0.2f, 14f) },
            Position = new Vector3(0f, -0.88f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.32f) },
        });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-45f, -30f, 0f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, 150f, 0f),
            LightEnergy = 0.5f,
        });

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.13f, 0.15f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.45f, 0.47f, 0.52f),
                AmbientLightEnergy = 0.8f,
            },
        });

        _camera = new Camera3D { Fov = 38f };
        AddChild(_camera);
        _camera.Position = new Vector3(0f, 0.05f, 2.6f);
        _camera.LookAt(new Vector3(0f, 0.02f, 0f));
        _camera.Current = true;
    }
}
