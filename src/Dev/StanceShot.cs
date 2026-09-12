using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// T37 验收用的两张对照图（卡片验收 4）。**必须带窗口跑**（无头是 dummy renderer，抓到空帧）：
///
///     godot --path . res://scenes/tests/StanceShot.tscn
///
/// 出图（**同机位**，只有姿态与 HUD 不同，要求一眼能分辨）：
///     assets/references/t37_guard_block.png  普通格挡：格挡姿态、架势槽半满、屏幕安静
///     assets/references/t37_guard_break.png  破防：后仰姿态、「破防」提示、白脉冲、边缘预警、架势槽满
///
/// 破防那张是**真的走裁决器打出来的**（不是摆拍）：架势先填满，再让真实的攻击落在格挡上，
/// 于是 OnPostureBroken 被调、姿态与 HUD 一起反应。为什么不能"一直格挡到破防"——
/// 见 StanceTest 里缺口①的日志（回复被量化放大到 60/s，一刀 +20 会被回光）。
/// </summary>
public partial class StanceShot : Node3D
{
    [Export] public int Width { get; set; } = 1280;
    [Export] public int Height { get; set; } = 720;
    [Export] public int WarmupFrames { get; set; } = 14;
    [Export] public string BlockOutputPath { get; set; } = "res://assets/references/t37_guard_block.png";
    [Export] public string WarnOutputPath { get; set; } = "res://assets/references/t37_posture_warn.png";
    [Export] public string BreakOutputPath { get; set; } = "res://assets/references/t37_guard_break.png";

    private PlayerActor _player = null!;
    private AttackingDummy _grunt = null!;
    private int _frames;
    private int _phase;
    private int _guardBreakBaseline;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T37 · 普通格挡 vs 破防";

        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Difficulty = GD.Load<DifficultyProfile>("res://data/difficulty/samurai.tres");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 木桩正前方 2m：在它的攻击距离（2.2m）之内，会自己挥刀
        _grunt = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        _grunt.Position = new Vector3(0f, 0.1f, -2f);
        AddChild(_grunt);

        MakePlayerCameraCurrent();
        AddEnvironment();
        AddLights();
    }

    public override void _Process(double delta)
    {
        _frames++;

        if (_frames < WarmupFrames)
            return;

        switch (_phase)
        {
            case 0:
                // 普通格挡：按住防御、架势半满（低于 0.75 的预警线 → 屏幕安静）
                _guardBreakBaseline = _player.GuardBreakCount;
                Input.ActionPress("guard");
                _player.Posture.Apply(_player.Posture.Max / 2);
                _phase = 1;
                return;

            case 1:
                // 等一帧让 HUD 把新读数画上去，再抓
                _phase = 2;
                return;

            case 2:
                Capture(BlockOutputPath, "普通格挡");
                _phase = 3;
                return;

            case 3:
                // 破防前：把架势填满（= 一直格挡本该达到的状态），预警边缘应当亮起来
                _guardBreakBaseline = _player.GuardBreakCount;
                _player.Posture.Apply(_player.Posture.Max);
                _phase = 4;
                return;

            case 4:
                Capture(WarnOutputPath, "架势满·预警");
                _phase = 5;
                return;

            case 5:
                // 继续按住防御，等真实攻击落上来 → 走裁决器的 GuardBreak 分支
                if (_player.GuardBreakCount > _guardBreakBaseline)
                {
                    _phase = 6;      // 破防了，下一帧抓（脉冲与文字最亮的时候）
                    return;
                }

                if (_frames > WarmupFrames + 500)
                {
                    GD.PrintErr("[架势截图] 等了 500 帧也没有破防，破防那张抓不到");
                    Input.ActionRelease("guard");
                    GetTree().Quit(1);
                }

                return;

            case 6:
                Input.ActionRelease("guard");
                int code = Capture(BreakOutputPath, "破防");
                GetTree().Quit(code);
                return;
        }
    }

    private int Capture(string path, string label)
    {
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[架势截图] {label} 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            return 1;
        }

        Error error = image.SavePng(path);

        if (error == Error.Ok)
            GD.Print($"[架势截图] {label} 已出图：{path}（{image.GetWidth()}×{image.GetHeight()}）");
        else
            GD.PrintErr($"[架势截图] {label} 写不出去：{path}（{error}）");

        return error == Error.Ok ? 0 : 1;
    }

    private void MakePlayerCameraCurrent()
    {
        foreach (Node node in _player.FindChildren("*", "Camera3D", true, false))
        {
            if (node is Camera3D camera)
            {
                camera.Current = true;
                return;
            }
        }
    }

    private void AddEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.12f, 0.14f, 0.17f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.42f, 0.46f, 0.55f),
            AmbientLightEnergy = 0.55f,
        };

        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });
    }

    private void AddLights() => AddChild(new DirectionalLight3D
    {
        Name = "Sun",
        Rotation = new Vector3(-1f, -0.6f, 0f),
        ShadowEnabled = true,
    });

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(60f, 0.4f, 60f) },
            Position = new Vector3(0f, -0.2f, 0f),
        });
        AddChild(body);
    }

    private static T Load<T>(string path) where T : Node =>
        GD.Load<PackedScene>(path).Instantiate<T>();
}
