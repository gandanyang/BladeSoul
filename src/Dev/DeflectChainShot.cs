using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;
using Oniblade.UI;

namespace Oniblade.Dev;

/// <summary>
/// T51 遗留①（弹开连击接进 HUD）的**人眼验收**：连帧抓图，看「×n」到底有没有出现在屏幕上。
///
///     godot --path . res://scenes/tests/DeflectChainShot.tscn
///
/// ⚠️ **必须带窗口跑**（无头是 dummy renderer，抓到的是空帧）——同 T48SwingShot / StanceShot。
///
/// 为什么要单独拍：`HudTest` 只能证明"读数变了"（<c>DeflectChainShown</c> 从 0 变成 2），
/// 证明不了**玩家看得见**——标签可能被别的层级盖住、可能落到屏幕外、可能字号小到读不出。
/// 这个项目已经被"数值全对但屏幕上是空的"骗过至少两次（T48 的雕像模型、T51 的格挡姿态 0.47%）。
///
/// 抓图时机是**事件驱动**的，不是按固定帧数：等到连击数第一次真的变成 ×2 才按快门。
/// 固定帧数会在"这次没连上"的时候拍到一张空图，反而证明不了什么。
///
/// 三个靶子**相位错开**（约 40 帧一档）：一个靶子的出招间隔 ≈118 帧 > 连击保持窗口 90 帧，
/// 单靶子数学上连不上第 2 次。
/// </summary>
public partial class DeflectChainShot : Node3D
{
    [Export] public int Width { get; set; } = 1100;
    [Export] public int Height { get; set; } = 760;

    /// <summary>先跑几帧让模型/相机/光照稳定下来。</summary>
    [Export] public int WarmupFrames { get; set; } = 20;

    /// <summary>抓图之前先让这一连击**存在**几帧（否则拍到的可能是它出现的那一瞬）。</summary>
    [Export] public int HoldFrames { get; set; } = 8;

    /// <summary>抓完第 1 张后再抓第 2 张的间隔（用来证明它**不会一闪就没**）。</summary>
    [Export] public int SecondShotDelayFrames { get; set; } = 30;

    [Export] public string OutputPrefix { get; set; } = "res://assets/references/t51_chain";

    /// <summary>弹开机器人：与 HudTest / DeflectTrainingTest 同一个手法（判定帧前 6 帧按下）。</summary>
    private const int GuardLeadFrames = 6;

    private readonly List<AttackingDummy> _attackers = new();

    private PlayerActor _player = null!;
    private int _frames;
    private int _shots;
    private bool _guardHeld;
    private int _holdLeft;
    private int _secondShotLeft;

    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T51 · 弹开连击 ×n";

        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        BuildStage();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        await WaitPhysicsFrames(WarmupFrames);

        int chain = _player.DeflectChain;
        int window = _player.DeflectChainWindowFrames;

        if (Hud.Instance is null)
        {
            GD.PrintErr("[T51连击图] Hud 没有注册成 autoload——屏幕上不会有连击数");
            GetTree().Quit(1);
            return;
        }

        // 三个靶子相位错开（约 40 帧一档），站成一个扇面
        await SpawnAttacker(new Vector3(0f, 0.1f, -1.7f), 6);
        await SpawnAttacker(new Vector3(1.25f, 0.1f, -1.5f), 40);
        await SpawnAttacker(new Vector3(-1.25f, 0.1f, -1.5f), 40);

        await RunFrames(900, window);

        GD.Print($"[T51连击图] 结束：最终连击 ×{_player.DeflectChain}，抓到 {_shots} 张");
        GetTree().Quit(_shots > 0 ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        _frames++;

        if (_shots == 1 && _secondShotLeft > 0 && --_secondShotLeft == 0)
            Capture("后（约 30 帧后仍在）", 2);
    }

    /// <summary>
    /// 跑到"连击数真的挂上屏幕"为止，然后按快门。整个过程由**真链路**驱动
    /// （会还手的假人 + 在窗口内按防御的机器人），不是摆拍。
    /// </summary>
    private async System.Threading.Tasks.Task RunFrames(int maxFrames, int windowFrames)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            DriveGuard();

            int shown = Hud.Instance?.DeflectChainShown ?? 0;

            if (shown >= 2 && _shots == 0)
            {
                if (_holdLeft < HoldFrames)
                {
                    _holdLeft++;
                    continue;
                }

                Capture($"连上 ×{shown}", 1);
                _secondShotLeft = SecondShotDelayFrames;
                continue;
            }

            if (_shots >= 2)
                break;
        }

        Input.ActionRelease("guard");

        // 收尾再拍一张"断连之后"：连击数必须**不在屏幕上**（否则它就是个永久挂着的数字）。
        // 要等**淡出也走完**再拍——只等保持窗口的话，拍到的是它半透明的残影，
        // 那种图"看起来还在"，下一眼就会被误读成"没隐藏"。
        await WaitPhysicsFrames(windowFrames + (Hud.Instance?.DeflectChainFadeFrames ?? 36) + 8);
        Capture("断连+淡出后（应无 ×n）", 3);
    }

    private void Capture(string label, int index)
    {
        string path = $"{OutputPrefix}_{index}.png";
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[T51连击图] {label} 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);

        GD.Print(error == Error.Ok
            ? $"[T51连击图] {label} → {path}（屏显 ×{Hud.Instance?.DeflectChainShown}，" +
              $"战斗链 {_player.DeflectChain}，第 {_frames} 逻辑帧）"
            : $"[T51连击图] 存图失败 {path}: {error}");

        _shots++;
    }

    /// <summary>弹开机器人：谁快进入判定帧就按防御，没人出招就松手。</summary>
    private void DriveGuard()
    {
        int soonest = int.MaxValue;
        bool anyoneAttacking = false;

        foreach (AttackingDummy dummy in _attackers)
        {
            if (dummy.Machine.Current is AttackState attack && attack.Sequence.IsRunning)
            {
                anyoneAttacking = true;
                soonest = Mathf.Min(soonest, attack.Sequence.Current.ActiveStart - attack.Sequence.Frame);
            }
        }

        if (!_guardHeld && soonest <= GuardLeadFrames)
        {
            Input.ActionPress("guard");
            _guardHeld = true;
        }
        else if (_guardHeld && !anyoneAttacking)
        {
            Input.ActionRelease("guard");
            _guardHeld = false;
        }
    }

    private async System.Threading.Tasks.Task SpawnAttacker(Vector3 position, int delayFrames)
    {
        var dummy = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
            .Instantiate<AttackingDummy>();
        dummy.Position = position;
        AddChild(dummy);
        _attackers.Add(dummy);
        await WaitPhysicsFrames(delayFrames);
    }

    private async System.Threading.Tasks.Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>
    /// 地板 + 一盏平行光 + 一台**游戏视角**的相机（角色背后偏上）。
    /// 机位与 T48SwingShot 刻意不同：那张要看刀，这张要看**屏幕下方居中的 HUD**，
    /// 所以必须是从玩家背后看出去的那台机位——连击数是给玩家看的，不是给旁观者看的。
    /// </summary>
    private void BuildStage()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(16f, 0.2f, 16f) } };
        shape.Position = new Vector3(0f, -0.1f, 0f);
        floor.AddChild(shape);

        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(16f, 0.2f, 16f) },
            Position = new Vector3(0f, -0.1f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.29f, 0.28f) },
        };
        floor.AddChild(mesh);
        AddChild(floor);

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48f, -30f, 0f),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        });

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.10f, 0.11f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.44f, 0.46f, 0.52f),
                AmbientLightEnergy = 0.75f,
            },
        });

        var camera = new Camera3D { Fov = 55f };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0f, 1.75f, 3.1f), new Vector3(0f, 1.0f, -1.0f));
        camera.Current = true;
    }
}
