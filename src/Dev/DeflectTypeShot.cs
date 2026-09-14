using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;
using Oniblade.Vfx;

namespace Oniblade.Dev;

/// <summary>
/// T53 的人眼验收：**四种攻击性质各弹开一次**，在火花还活着的那几帧里抓图。
///
///     godot --path . res://scenes/tests/DeflectTypeShot.tscn
///
/// ⚠️ **必须带窗口跑**（无头是 dummy renderer，抓到的是空帧）——同 T38ActionShot / DeflectChainShot。
///
/// 为什么不直接灌事件拍图：那样只证明了"特效层会按类型画"，证明不了
/// **Arbiter 真的把攻击性质带出来了**。所以这里是**真打斗**：
/// 会还手的假人 → 玩家在判定帧前按防御 → 弹开 → 事件带上 AttackData.Type → 火花分档。
/// 每张图都顺带打印一次链路证据（攻击数据的 Type / 事件里的 AttackType / 实际命中的档位）。
///
/// 机位是**斜侧近景**而不是游戏视角：火花出现在防御方的胸口，
/// 从玩家背后看会被自己的身体整个挡住——那样拍出来的图讨论不了"火花形状"。
/// </summary>
public partial class DeflectTypeShot : Node3D
{
    [Export] public int Width { get; set; } = 1100;
    [Export] public int Height { get; set; } = 700;
    [Export] public int WarmupFrames { get; set; } = 20;

    /// <summary>等不到弹开时的放弃帧数（一次出招间隔约 118 帧，给足余量）。</summary>
    [Export] public int GiveUpFrames { get; set; } = 900;

    [Export] public string OutputPrefix { get; set; } = "res://assets/references/t53_deflect";

    /// <summary>与 HudTest / DeflectTrainingTest / DeflectChainShot 同一手法：判定帧前 6 帧按下。</summary>
    private const int GuardLeadFrames = 6;

    private static readonly DamageType[] Order =
    {
        DamageType.Slash, DamageType.Thrust, DamageType.Blunt, DamageType.Dark,
    };

    private readonly List<AttackingDummy> _attackers = new();

    private PlayerActor _player = null!;
    private Camera3D _camera = null!;

    /// <summary>玩家的视觉模型。截图期间隐藏——火花和它是重叠的（见交接里的发现）。</summary>
    private Node3D? _playerVisual;

    private int _frames;
    private int _shots;
    private bool _guardHeld;
    private bool _deflected;
    private DamageType _lastAttackType = DamageType.Slash;
    private DamageType _lastEventType = DamageType.Slash;
    private string _lastProfileId = "";

    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T53 · 弹开按攻击性质分档";

        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        BuildStage();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 火花与**角色模型是重叠的**——受击框节点在角色根节点处（y≈0），
        // 不是胸口。不隐藏模型就只能拍到一具身体，火花全被吞掉。
        _playerVisual = _player.GetNodeOrNull<Node3D>("VisualModel");
        _playerVisual?.Hide();

        if (EventBus.Instance is { } bus)
            bus.HitResolved += OnHit;

        await WaitPhysicsFrames(WarmupFrames);

        var dummy = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
            .Instantiate<AttackingDummy>();
        dummy.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(dummy);
        _attackers.Add(dummy);

        if (dummy.Attack is null)
        {
            GD.PrintErr("[T53图] 假人身上没有 Attack 数据（场景没挂）——拍不出分档");
            GetTree().Quit(1);
            return;
        }

        await WaitPhysicsFrames(20);

        // 先空跑一次（不拍照）：否则**第一张**拍到的是"假人刚 spawn、玩家还没转向"
        // 的开场状态，构图和后面三张对不上——实测第一张的亮像素是后三张的两倍，
        // 四张图就没法并排比较。
        await WaitForDeflect();

        foreach (DamageType type in Order)
            await ShootOne(dummy, type);

        Input.ActionRelease("guard");
        GD.Print($"[T53图] 结束：抓到 {_shots}/{Order.Length} 张");
        GetTree().Quit(_shots == Order.Length ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        _frames++;

        // 玩家自带相机，且它会在建立时抢占 current——这里每帧确保是**我们这台侧视机**在拍。
        if (_camera is not null && GetViewport().GetCamera3D() != _camera)
            _camera.MakeCurrent();
    }

    private void OnHit(HitEvent e)
    {
        if (e.Verdict != Verdict.Deflect)
            return;

        _deflected = true;
        _lastEventType = e.AttackType;
    }

    /// <summary>空跑一次弹开（不拍照），把双方推进到稳定状态。</summary>
    private async System.Threading.Tasks.Task WaitForDeflect()
    {
        _deflected = false;

        for (int i = 0; i < GiveUpFrames && !_deflected; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            DriveGuard();
        }

        Input.ActionRelease("guard");
        _guardHeld = false;

        await WaitPhysicsFrames(SparkBurst.DeflectLifetimeFrames + 6);
    }

    /// <summary>
    /// 等一次真弹开，然后在火花还没消失时按快门。
    /// 抓图**不用固定帧数**：火花寿命只有 6~8 帧，固定帧数会在"这次没接上"时
    /// 拍到一张没有火花的图，反而证明不了任何事。
    /// </summary>
    private async System.Threading.Tasks.Task ShootOne(AttackingDummy dummy, DamageType type)
    {
        // 序列里引用的是**同一个 AttackData 实例**（_Ready 里 Configure(new[] { Attack })），
        // 所以直接改 Type 立刻生效。截图机是独立进程，不会污染别人的资源。
        dummy.Attack!.Type = type;
        _lastAttackType = type;
        _deflected = false;

        int waited = 0;

        for (; waited < GiveUpFrames && !_deflected; waited++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            DriveGuard();
        }

        Input.ActionRelease("guard");
        _guardHeld = false;

        if (!_deflected)
        {
            GD.PrintErr($"[T53图] 等 {waited} 帧也没等到 {type} 的弹开");
            return;
        }

        // ⚠️ 这里等的是**渲染帧**，不是物理帧。粒子从 `Emitting = true` 到真的画进
        // 帧缓冲要过渲染管线，而渲染帧率通常低于物理帧率（实测 163 渲染帧 ≈ 172 物理帧）。
        // 等 2 物理帧有时只换来 0~1 个渲染帧，抓到的还是空帧——症状是四张图两两
        // 像素差 0.00%，看着像"分档没生效"，其实是根本没拍到火花。
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        SparkBurst? spark = FirstLiveSpark();
        _lastProfileId = spark?.Profile?.Id ?? "(无档位)";

        if (spark is not null)
        {
            Vector2 screen = _camera.UnprojectPosition(spark.GlobalPosition);
            Vector2 viewport = GetViewport().GetVisibleRect().Size;
            GD.Print($"[T53图] 火花世界坐标 {spark.GlobalPosition}，屏幕投影 " +
                     $"({screen.X:F0}, {screen.Y:F0})，视口 ({viewport.X:F0}, {viewport.Y:F0})");
        }

        GD.Print($"[T53图] 链路：攻击数据 Type={_lastAttackType} → 事件 AttackType={_lastEventType} " +
                 $"→ 命中档位 {_lastProfileId}（等了 {waited} 帧）");

        Capture(type.ToString());

        // 让这一簇火花自然结束，免得混进下一张。
        await WaitPhysicsFrames(SparkBurst.DeflectLifetimeFrames + 4);
    }

    private void Capture(string label)
    {
        string path = $"{OutputPrefix}_{label.ToLowerInvariant()}.png";
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[T53图] {label} 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);

        GD.Print(error == Error.Ok
            ? $"[T53图] {label} → {path}（第 {_frames} 逻辑帧）"
            : $"[T53图] 存图失败 {path}: {error}");

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

    private SparkBurst? FirstLiveSpark()
    {
        foreach (Node child in GetTree().Root.FindChildren("*", "Node3D", true, false))
        {
            if (child is SparkBurst spark && !spark.IsQueuedForDeletion())
                return spark;
        }

        return null;
    }

    private async System.Threading.Tasks.Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>地板 + 平行光 + 环境光 + 一台**斜侧近景**相机（火花在防御方胸口）。</summary>
    private void BuildStage()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(16f, 0.2f, 16f) },
            Position = new Vector3(0f, -0.1f, 0f),
        });
        floor.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(16f, 0.2f, 16f) },
            Position = new Vector3(0f, -0.1f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.29f, 0.28f) },
        });
        AddChild(floor);

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-45f, 35f, 0f),
            LightEnergy = 1.2f,
            ShadowEnabled = true,
        });

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.13f, 0.16f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.45f, 0.47f, 0.55f),
                AmbientLightEnergy = 0.6f,
            },
        });

        // 机位对准**火花的实际生成位置**。实测它不在胸口，而在角色的根节点高度
        // （世界坐标 y ≈ 0.0002，见交接里的发现）。按"胸口"架机位的话投影会落到
        // 视口外（实测屏幕 (437, 1077)，视口高只有 700），拍出来四张图两两像素差
        // 0.00%——看着像"分档没生效"，其实是根本没拍到火花。
        _camera = new Camera3D { Fov = 40f };
        AddChild(_camera);
        // D1 修复后（2026-09-15）：火花从脚底（y≈0）移到受击体积高度（y≈0.87，胸口），
        // 机位跟着对准新位置——这张图拍的就是玩家在游戏里看到的弹开。
        _camera.LookAtFromPosition(
            new Vector3(0.70f, 1.05f, 0.75f),
            new Vector3(0f, 0.87f, -0.05f));
        _camera.MakeCurrent();
    }
}
