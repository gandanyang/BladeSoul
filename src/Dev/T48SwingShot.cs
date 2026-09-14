using Godot;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// T48 验收 4：**同一次挥砍取 6 帧**，看"刀到底动没动"。
///
///     godot --path . res://scenes/tests/T48SwingShot.tscn
///
/// ⚠️ **必须带窗口跑**（无头是 dummy renderer，抓到的是空帧）——同 StanceShot。
///
/// 为什么要单独拍这个：T48 之前，模型的蒙皮权重**全部指向 Root**，
/// 动画器把 R_Upperarm 抡到 133.7°，屏幕上一个顶点都不动（实测 0/6500）。
/// 所以"骨头角度对"这件事**完全不能证明玩家看得见东西**——
/// 只有连帧截图能证明。机位固定在角色**左前方**（刀挂左腰，要从刀这一侧看）。
/// </summary>
public partial class T48SwingShot : Node3D
{
    [Export] public int Width { get; set; } = 900;
    [Export] public int Height { get; set; } = 700;

    /// <summary>先跑几帧让模型/相机/光照稳定下来。</summary>
    [Export] public int WarmupFrames { get; set; } = 20;

    /// <summary>每多少帧抓一张（轻斩壹共 26 帧，6 张正好铺满一次挥砍）。</summary>
    [Export] public int FramesBetweenShots { get; set; } = 5;

    [Export] public string OutputPrefix { get; set; } = "res://assets/references/t48_swing";

    private PlayerActor _player = null!;
    private int _frames;
    private int _shots;
    private bool _attacking;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T48 · 挥砍连帧";

        BuildStage();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        if (!_attacking)
        {
            Input.ActionPress("attack");
            _attacking = true;
            return;
        }

        if (_frames % FramesBetweenShots != 0)
            return;

        Input.ActionRelease("attack");
        string path = $"{OutputPrefix}_{_shots + 1}.png";
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[T48连帧] 第 {_shots + 1} 张抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[T48连帧] 第 {_shots + 1}/6 帧 → {path}（第 {_frames} 逻辑帧）"
            : $"[T48连帧] 存图失败 {path}: {error}");

        _shots++;
        if (_shots >= 6)
        {
            GD.Print("[T48连帧] 6 张出齐");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
        }
    }

    /// <summary>地板 + 一盏平行光 + 一台固定相机（机位不动，动的只有角色）。</summary>
    private void BuildStage()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(12f, 0.2f, 12f) } };
        shape.Position = new Vector3(0f, -0.1f, 0f);
        floor.AddChild(shape);

        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(12f, 0.2f, 12f) },
            Position = new Vector3(0f, -0.1f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.3f) },
        };
        floor.AddChild(mesh);
        AddChild(floor);

        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -35f, 0f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        AddChild(light);
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.11f, 0.12f, 0.14f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.44f, 0.5f),
                AmbientLightEnergy = 0.7f,
            },
        });

        // 机位：角色**左前方**（刀在左腰，Godot 侧 +X 那一边）。固定不动。
        var camera = new Camera3D { Fov = 42f };
        AddChild(camera);
        // 必须在树上才能 LookAt（不在树里会抛 "Node not inside tree"）。
        camera.LookAtFromPosition(new Vector3(2.35f, 1.45f, 1.75f), new Vector3(0.05f, 0.95f, 0f));
        camera.Current = true;
    }
}
