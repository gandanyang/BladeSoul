using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// T31 验收用的两张截图（11 §5.1 / 卡片验收 4）。**必须带窗口跑**——
/// 无头模式是 dummy renderer，抓出来是空帧（和 T35 同屏截图一个道理）：
///
///     godot --path . res://scenes/tests/HudShot.tscn
///
/// 出图：
///     assets/references/t31_hud_main.png     主界面：玩家条 ＋ 锁定目标（名字/血/架势槽）＋ 头顶条
///     assets/references/t31_hud_deflect.png  一次弹开成功的瞬间：边缘脉冲 ＋ 「弹开」＋ 架势槽跳动
///
/// 头顶条那一张刻意摆了"近处一个、远处一个"两个未锁定敌人——
/// 远处那个**不该有**条，这正是 11 §4.2 "只在受伤或靠近时显示"的可视证据。
/// </summary>
public partial class HudShot : Node3D
{
    [Export] public int Width { get; set; } = 1280;
    [Export] public int Height { get; set; } = 720;

    /// <summary>摆好之后等几帧再抓——要等渲染器真的画过，否则可能抓到空帧。</summary>
    [Export] public int WarmupFrames { get; set; } = 14;

    /// <summary>触发弹开后等几帧再抓：等脉冲和文字真的上了屏（但还没淡掉）。</summary>
    [Export] public int DeflectDelayFrames { get; set; } = 3;

    [Export] public string MainOutputPath { get; set; } = "res://assets/references/t31_hud_main.png";
    [Export] public string DeflectOutputPath { get; set; } = "res://assets/references/t31_hud_deflect.png";

    /// <summary>弹开这一下削的架势（要与 data 里的弹开量级一致，卡片要求"看得出比普攻多"）。</summary>
    [Export] public int DeflectPostureDamage { get; set; } = 18;

    private PlayerActor _player = null!;
    private TrainingDummy _focus = null!;
    private int _playerActorId;
    private int _frames;
    private bool _capturedMain;
    private bool _postedDeflect;
    private bool _done;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T31 · 战斗 HUD";

        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 锁定目标：正前方 4m（Hud 自己选的焦点就是最近的这个）
        _focus = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        _focus.Position = new Vector3(0f, 0.1f, -4f);
        AddChild(_focus);

        // 未锁定敌人甲：左前方 6m —— 靠近，头顶条**该显示**
        TrainingDummy near = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        near.Position = new Vector3(-4.5f, 0.1f, -3.5f);
        AddChild(near);

        // 未锁定敌人乙：右前方 18m —— 又远又没受伤，头顶条**不该显示**（11 §4.2）
        TrainingDummy far = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        far.Position = new Vector3(6f, 0.1f, -17f);
        AddChild(far);

        MakePlayerCameraCurrent();
        AddEnvironment();
        AddLights();

        _playerActorId = _player.ActorId;
    }

    /// <summary>
    /// 低饱和青灰（10 §3.1）。不加环境光的话人物几乎成剪影，
    /// 截图看不出"这是谁、条挂在谁头上"——这张图是用来给人看的。
    /// </summary>
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

    private void AddLights()
    {
        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            Rotation = new Vector3(-1f, -0.6f, 0f),
            ShadowEnabled = true,
        });
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;

        _frames++;

        if (_frames < WarmupFrames)
        {
            // 抓图前两帧再充架势：体干会自己回落，早了就抓到一个空槽。
            // 玩家条"向上生长"、敌人条"左右收缩"——这张图要能看出这个形状差异（05 §4.3）。
            if (_frames == WarmupFrames - 2)
                ChargeGauges();

            return;
        }

        if (!_capturedMain)
        {
            _capturedMain = true;
            Capture(MainOutputPath, "主界面");
            return;
        }

        if (!_postedDeflect)
        {
            _postedDeflect = true;

            // 弹开那一下：敌方架势槽要跳（18），并让 HUD 走"玩家防住"的分支
            _focus.Posture.Apply(DeflectPostureDamage);

            EventBus.Instance?.RaiseHitResolved(new HitEvent
            {
                AttackerId = _focus.ActorId,
                DefenderId = _playerActorId,
                Verdict = Verdict.Deflect,
                AttackId = "t31_shot_deflect",
                IssenKind = IssenKind.None,
                Damage = 0,
                PostureDamage = DeflectPostureDamage,
                HitStopFrames = 0,
                Frame = 0,
                Killed = false,
                Position = _player.GlobalPosition + Vector3.Up * 1.2f,
                Direction = Vector3.Forward,
            });

            return;
        }

        if (_frames < WarmupFrames + DeflectDelayFrames + 1)
            return;

        _done = true;
        int code = Capture(DeflectOutputPath, "弹开瞬间");
        GetTree().Quit(code);
    }

    /// <summary>给双方预充架势，让两个槽在图上都读得出来（数值只是为了让截图可读，不参与战斗裁决）。</summary>
    private void ChargeGauges()
    {
        _player.Posture.Apply(_player.Posture.Max / 2);
        _focus.Posture.Apply(_focus.Posture.Max / 3);
    }

    private int Capture(string path, string label)
    {
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[HUD截图] {label} 抓到空帧（{image.GetWidth()}×{image.GetHeight()}）——" +
                        "无头模式是 dummy renderer，请带窗口跑");
            return 1;
        }

        Error error = image.SavePng(path);

        if (error == Error.Ok)
            GD.Print($"[HUD截图] {label} 已出图：{path}（{image.GetWidth()}×{image.GetHeight()}）");
        else
            GD.PrintErr($"[HUD截图] {label} 写不出去：{path}（{error}）");

        return error == Error.Ok ? 0 : 1;
    }

    /// <summary>玩家的 SpringArm 相机默认就该是当前相机，这里兜一下底，否则抓到的是空视角。</summary>
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

        GD.PrintErr("[HUD截图] 玩家身上没有 Camera3D，抓出来会是空视角");
    }

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
