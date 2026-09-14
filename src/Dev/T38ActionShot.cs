using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// T38 的人眼验收：五个新动作各一组**连帧** ＋「格挡 / 弹开 / 体干破裂」的**同机位对照**。
///
///     godot --path . res://scenes/tests/T38ActionShot.tscn
///
/// ⚠️ **必须带窗口跑**（无头是 dummy renderer，抓到的是空帧）——同 T48SwingShot / StanceShot / DeflectChainShot。
///
/// **为什么数字够了还要拍**：`PlayerGaps` 已经能把"缺不缺动作"和"两个姿势差多少度"断言下来，
/// 但它证明不了**看起来对不对**——腿弯了而人没矮、手举到嘴边却被头挡死、
/// 破防与格挡在真实机位下糊成一样，这些在角度数字上全都是"通过"。
/// 本项目被这件事骗过：T51 的格挡姿态骨角差全对，实际画面差异只有 0.47%。
///
/// ★ **每一张都由真链路触发**，没有一张是摆拍：
/// 闪避 / 跳跃 / 喝血 / 格挡走真实输入；弹开靠**会还手的假人**（判定帧前 6 帧按防御的机器人，
/// 与 <c>DeflectChainShot</c> / <c>HudTest</c> 同一手法）；破防走
/// <c>CombatActor.ApplyPosturePercent</c>；死亡走 <c>CombatActor.Die()</c>
/// —— 后两条都是它们**唯一**的生产入口，没有绕过裁决去直接调动画器。
/// </summary>
public partial class T38ActionShot : Node3D
{
    [Export] public int Width { get; set; } = 1100;
    [Export] public int Height { get; set; } = 760;

    /// <summary>先跑几帧让模型 / 相机 / 光照稳定下来。</summary>
    [Export] public int WarmupFrames { get; set; } = 24;

    [Export] public string OutputPrefix { get; set; } = "res://assets/references/t38";

    /// <summary>弹开机器人：与 DeflectChainShot / HudTest 同一个手法（判定帧前 6 帧按下）。</summary>
    private const int GuardLeadFrames = 6;

    /// <summary>等假人出招的上限。它出招间隔 ≈118 帧，900 帧足够等到七八次。</summary>
    private const int MaxDeflectWaitFrames = 900;

    private PlayerActor _player = null!;
    private AttackingDummy _dummy = null!;
    private bool _guardHeld;

    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T38 · 五个动作 + 姿势对照";

        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        BuildStage();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 会还手的假人 —— 弹开那一张必须**真的弹开一次**。
        _dummy = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
            .Instantiate<AttackingDummy>();
        _dummy.Position = new Vector3(0f, 0f, -1.7f);
        AddChild(_dummy);

        await WaitPhysicsFrames(WarmupFrames);

        var kids = new System.Collections.Generic.List<string>();
        foreach (Node child in _player.GetChildren())
            kids.Add(child.Name);

        GD.Print($"[T38图] 玩家视觉模型：{(_player.VisualModel is null ? "**缺失（退回灰盒）**" : _player.VisualModel.ResourcePath)}"
                 + $"　玩家位置 {_player.GlobalPosition}　假人位置 {_dummy.GlobalPosition}"
                 + $"　玩家子节点 [{string.Join(", ", kids)}]");

        await ShootIdle();
        await ShootDodge();
        await ShootJump();
        await ShootHeal();
        await ShootGuard();
        await ShootDeflect();
        await ShootGuardBreak();
        await ShootDeath();

        GD.Print("[T38图] 全部拍完（每个动作 3 拍：起手 / 中段 / 收势）");
        GetTree().Quit(0);
    }

    // ── 逐个动作 ────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task ShootIdle()
    {
        await BackToIdle();
        Capture("idle", 1, "idle（基准）");
    }

    /// <summary>
    /// 闪避。**抓帧位置按"动作自己的包络"选**：这个姿势的峰值在整段的 35% 处
    /// （`ApplyDodgePose` 的 env），所以"最低点"那一张必须在那个位置附近抓。
    /// </summary>
    private async System.Threading.Tasks.Task ShootDodge()
    {
        await BackToIdle();

        Input.ActionPress("dodge");
        await WaitPhysicsFrames(1);
        Input.ActionRelease("dodge");

        await WaitPhysicsFrames(2);
        Capture("dodge", 1, "闪避·起手");

        await WaitPhysicsFrames(5);
        Capture("dodge", 2, "闪避·最低点");

        await WaitPhysicsFrames(8);
        Capture("dodge", 3, "闪避·收势");

        await BackToIdle();
    }

    /// <summary>
    /// 跳跃。三段**不按固定帧数抓**——滞空多少帧是物理结果（起跳初速 ÷ 重力），
    /// 所以这里等的是**物理事实**（离没离地），不是"第几帧"。
    /// </summary>
    private async System.Threading.Tasks.Task ShootJump()
    {
        await BackToIdle();

        Input.ActionPress("jump");
        await WaitPhysicsFrames(2);
        Input.ActionRelease("jump");

        await WaitPhysicsFrames(2);
        Capture("jump", 1, "跳跃·蹬地");

        for (int i = 0; i < 60; i++)
        {
            await WaitPhysicsFrames(1);
            if (!_player.IsOnFloor())
                break;
        }

        await WaitPhysicsFrames(8);
        Capture("jump", 2, "跳跃·腾空（收腿）");

        for (int i = 0; i < 200; i++)
        {
            await WaitPhysicsFrames(1);
            if (_player.Machine.Current is JumpState && _player.IsOnFloor())
                break;
        }

        Capture("jump", 3, "跳跃·落地缓冲");

        await BackToIdle();
    }

    /// <summary>喝血。三拍直接对着**状态的三个相位**抓（起手 / 饮用 / 收招）。</summary>
    private async System.Threading.Tasks.Task ShootHeal()
    {
        await BackToIdle();

        Input.ActionPress("item_use");
        await WaitPhysicsFrames(2);
        Input.ActionRelease("item_use");

        await WaitPhysicsFrames(5);
        Capture("heal", 1, "喝血·掏壶（可被打断）");

        await WaitForHealPhase(HealPhase.Drink);
        await WaitPhysicsFrames(8);
        Capture("heal", 2, "喝血·饮用");

        await WaitForHealPhase(HealPhase.Recovery);
        await WaitPhysicsFrames(2);
        Capture("heal", 3, "喝血·收招");

        await BackToIdle();
    }

    /// <summary>格挡：按住到"维持"段（抬起只有 8 帧，拍它等于拍过渡）。</summary>
    private async System.Threading.Tasks.Task ShootGuard()
    {
        await BackToIdle();

        Input.ActionPress("guard");
        await WaitPhysicsFrames(20);
        Capture("guard", 1, "格挡（维持）");

        Input.ActionRelease("guard");
        await WaitPhysicsFrames(12);
    }

    /// <summary>
    /// 弹开成功。**等它真的发生**（假人的判定帧前 6 帧按防御），不是摆拍——
    /// 固定帧数会在"这次没弹上"的时候拍到一张普通格挡，而那张图恰好会被读成
    /// "弹开与格挡没区别"，正是这张卡片要证伪的那件事。
    /// </summary>
    private async System.Threading.Tasks.Task ShootDeflect()
    {
        // 前面几拍里玩家可能已经掉了血（格挡那张之前有不少帧是空手站着的），
        // 先补满——否则弹开还没等到，人就先倒了，而"没弹开"会被误读成"机器人坏了"。
        _player.Health.Heal(_player.Health.Max);

        // ★ 还必须先站回原点：**闪避那一拍是带位移的**（没有方向输入就是后跳），
        //   到这里玩家已经被带走 4 米多，而假人的射程只有 2.2m —— 距离不对，它一次都不出招。
        //   第一次跑就是这么栽的：日志显示 `距离 4.15`、假人恒 IdleState、冷却恒 0，
        //   整段表现为"等了 900 帧也没弹上"，看起来像机器人坏了。
        _player.GlobalPosition = new Vector3(0f, 0.1f, 0f);
        _player.Velocity = Vector3.Zero;
        await WaitPhysicsFrames(2);

        for (int frame = 0; frame < MaxDeflectWaitFrames; frame++)
        {
            await WaitPhysicsFrames(1);
            DriveGuard();

            // 每 120 帧报一次：这张图最怕的是"没弹上"，而没弹上的原因通常只有两个
            // （假人没出招 / 玩家没活到那一刻），日志里要能一眼分辨。
            if (frame % 120 == 0)
            {
                GD.Print($"[T38图]   等弹开… 第 {frame} 帧　假人 {_dummy.Machine.Current?.GetType().Name}　"
                         + $"冷却 {_dummy.CooldownFramesLeft}　玩家 {_player.Machine.Current?.GetType().Name}　"
                         + $"血 {_player.Health.Current}/{_player.Health.Max}　"
                         + $"距离 {_player.GlobalPosition.DistanceTo(_dummy.GlobalPosition):F2}");
            }

            if (_player.Machine.Current is DeflectState)
            {
                await WaitPhysicsFrames(2);
                Capture("deflect", 1, "弹开成功");
                Input.ActionRelease("guard");
                _guardHeld = false;
                await BackToIdle();
                return;
            }
        }

        Input.ActionRelease("guard");
        _guardHeld = false;
        GD.PrintErr($"[T38图] {MaxDeflectWaitFrames} 帧内没等到一次弹开——这张图缺了");
    }

    /// <summary>
    /// 体干破裂。入口是 <c>CombatActor.ApplyPosturePercent</c> —— 它就是"被打满架势"的
    /// 真实入口（裁决器命中时走的就是它），不是绕过系统去直接写姿势。
    /// </summary>
    private async System.Threading.Tasks.Task ShootGuardBreak()
    {
        await BackToIdle();

        _player.ApplyPosturePercent(1f);
        await WaitPhysicsFrames(5);
        Capture("guardbreak", 1, "体干破裂");

        await BackToIdle();
    }

    /// <summary>
    /// 死亡。入口是 <c>CombatActor.Die()</c>（血量归零时敌人/玩家的真实入口），
    /// 它会走到 <c>OnDeath → EnterRevive → PlayDeath</c>。
    /// </summary>
    private async System.Threading.Tasks.Task ShootDeath()
    {
        await BackToIdle();

        _player.Die();

        await WaitPhysicsFrames(12);
        Capture("death", 1, "死亡·倒下");

        await WaitPhysicsFrames(14);
        Capture("death", 2, "死亡·伏着");

        await WaitPhysicsFrames(24);
        Capture("death", 3, "死亡·撑起");
    }

    // ── 工具 ────────────────────────────────────────────────────────────

    /// <summary>弹开机器人：谁快进判定帧就按防御，没人出招就松手（照抄 DeflectChainShot）。</summary>
    private void DriveGuard()
    {
        int soonest = int.MaxValue;
        bool attacking = false;

        if (_dummy.Machine.Current is AttackState attack && attack.Sequence.IsRunning)
        {
            attacking = true;
            soonest = attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
        }

        if (!_guardHeld && soonest <= GuardLeadFrames)
        {
            Input.ActionPress("guard");
            _guardHeld = true;
        }
        else if (_guardHeld && !attacking)
        {
            Input.ActionRelease("guard");
            _guardHeld = false;
        }
    }

    private async System.Threading.Tasks.Task WaitForHealPhase(HealPhase phase)
    {
        for (int i = 0; i < 150; i++)
        {
            if (_player.Machine.Current is HealState heal && heal.Phase == phase)
                return;

            await WaitPhysicsFrames(1);
        }
    }

    private async System.Threading.Tasks.Task BackToIdle(int maxFrames = 300)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (!_player.IsDead && _player.Machine.Current is IdleState)
                return;

            await WaitPhysicsFrames(1);
        }
    }

    private void Capture(string action, int index, string label)
    {
        string path = $"{OutputPrefix}_{action}_{index}.png";
        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr($"[T38图] {label} 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[T38图] {label} → {path}"
            : $"[T38图] 存图失败 {path}: {error}");
    }

    private async System.Threading.Tasks.Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>
    /// 地板 ＋ 一盏平行光 ＋ 一台**侧后方**的相机。
    ///
    /// 机位与 DeflectChainShot 刻意不同：那张要读屏幕下方的 HUD，必须正对背后；
    /// 这张要读**四肢轮廓**——正后方会把抬臂、收腿、侧倾全挡在躯干后面
    /// （T51 的格挡姿态就是这么只剩 0.47% 差异的）。
    /// 偏 40° 左右既保留了"游戏机位"的观感，又能看见手和腿。
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

        var camera = new Camera3D { Fov = 45f };
        AddChild(camera);

        // 侧视（不是正后方）。理由有两层：
        // ① 正后方会把抬臂 / 收腿 / 侧倾全挡在躯干后面——T51 的格挡姿态就是这么只剩 0.47% 差异的；
        // ② 玩家在 z=0、假人在 z=-1.7，从侧面看**两个人都入画且不重叠**，
        //    弹开那张图能同时看到"刀被架住"的两边。
        camera.LookAtFromPosition(new Vector3(3.05f, 1.42f, 0.55f), new Vector3(0f, 0.92f, -0.85f));
        camera.Current = true;
    }
}
