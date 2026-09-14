using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 机制体检（1 轮试玩反馈四条）：跳跃惯性 / 闪避位移与无敌 / 闪避奖励 / 体干恢复时机。
///
/// # 为什么这些 bug 逃过了 37 步全绿
/// 已有测试断言的全是**逻辑结果**：
///   `DodgeTraining` 验的是「Miss 16 次、完美闪避 4 次、无敌 8 帧」——
///   全是裁决器的输出，**没有一个字测位移**。于是"闪避把人原地定住"这种
///   手感问题可以完全不被发现；而玩家感知到的"没有无敌帧"，
///   很可能就是"没闪出去、刀还是落在我身上"。
///   `Jump` 验的是「离地/最高 1.59m/滞空 68 帧/硬直 13 帧」——
///   **完全没测水平速度**，所以"跳跃没有惯性"也不可能被测出来。
///
/// 所以本探针只量**手感量**：位移多少、惯性留没留、奖励玩家看不看得见、体干哪一帧在涨。
/// </summary>
public partial class MechanicsProbe : Node3D
{
    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;
    private Ashigaru _enemy = null!;

    private readonly System.Collections.Generic.List<string> _fails = new();

    public override void _Ready() => _ = RunAsync();

    private async System.Threading.Tasks.Task RunAsync()
    {
        try { await RunCore(); }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[机制体检] ✗ 未捕获异常：{ex}");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task RunCore()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn")
                   .Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        _attacker = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
                      .Instantiate<AttackingDummy>();
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        _enemy = GD.Load<PackedScene>("res://scenes/enemies/Ashigaru.tscn")
                   .Instantiate<Ashigaru>();
        _enemy.Position = new Vector3(6f, 0.1f, 0f);   // 离远点，别来干扰
        AddChild(_enemy);

        await Wait(8);

        await CheckGuardCrouch();
        await CheckJumpInertia();
        await CheckDodgeDisplacement();
        await CheckPerfectDodgeReward();
        await CheckPostureRegen();

        GD.Print("");
        if (_fails.Count == 0)
        {
            GD.Print("[机制体检] ✓ 四条机制全部符合预期");
            GetTree().Quit(0);
            return;
        }

        foreach (string f in _fails)
            GD.Print($"[机制体检] ✗ {f}");

        GD.Print($"[机制体检] ✗ 共 {_fails.Count} 项不符合预期");
        GetTree().Quit(1);
    }

    // ── ⓪ 格挡下沉 ─────────────────────────────────────────────────
    //
    // T52 试玩："按住右键格挡的姿势也有问题"。
    // 之前的修法只改了腿部符号（不再反折），但**人没有变矮**——
    // 实测旋腿只会把膝盖抬起来，髋部不动就没有"压重心"这回事。
    //
    // 判据用**髋骨的世界高度**：格挡时必须明显低于静止站姿。
    // ★ 用 `BoneGlobalPose`（世界空间），不是 `GetBoneGlobalPose`（相对骨架空间）——
    //   后者看不见根骨骼下沉，是前几版探针量不出效果的根因。
    private async System.Threading.Tasks.Task CheckGuardCrouch()
    {
        GD.Print("[机制体检] ── ⓪ 格挡姿势（重心下沉）──");

        GD.Print($"[机制体检]   动画器 GuardCrouchDepth = {_player.SkinAnimatorForProbe!.GuardCrouchDepth:F3} m" +
                 $"（应为 player_stats.tres 的 0.12）");
        GD.Print($"[机制体检]   Stats?.GuardCrouchDepth = {_player.Stats?.GuardCrouchDepth:F3} m");

        _player.SkinAnimatorForProbe!.ForcedGuardFrame = -1;
        await Wait(20);
        float hipRest = WorldHipY();

        _player.SkinAnimatorForProbe.ForcedGuardFrame = 40;
        await Wait(3);
        // 诊断开着（定位根骨骼下沉为何不生效）
        await Wait(17);
        float hipGuard = WorldHipY();
        float footGuard = WorldFootY();

        _player.SkinAnimatorForProbe.ForcedGuardFrame = -1;

        float sunk = (hipRest - hipGuard) * 100f;
        GD.Print($"[机制体检]   髋骨世界高度：静止 {hipRest * 100f:F1} cm → 格挡 {hipGuard * 100f:F1} cm" +
                 $"（下沉 {sunk:F1} cm）");
        GD.Print($"[机制体检]   格挡时脚底世界高度 {footGuard * 100f:F1} cm（地面在 0 附近）");

        if (sunk < 5f)
        {
            _fails.Add($"格挡没有下沉重心：髋只降了 {sunk:F1} cm——" +
                       "腿弯了但人没变矮，看起来就是「腿翘起来」而不是「蹲下去」");
        }

        if (footGuard > 0.25f)
            _fails.Add($"格挡时脚离地 {footGuard * 100f:F1} cm（人在浮空）");
    }

    /// <summary>髋骨世界高度（米）。</summary>
    private float WorldHipY() => BoneWorldY("L_Thigh");

    /// <summary>左脚脚骨世界高度（米）。</summary>
    private float WorldFootY() => BoneWorldY("L_Foot");

    private float BoneWorldY(string bone)
    {
        Skeleton3D? skel = FindSkeleton(_player);

        if (skel is null)
            return 0f;

        int b = skel.FindBone(bone);

        // ★ 必须乘 `GlobalTransform` 才是**世界**坐标：`GetBoneGlobalPose` 是
        //   **骨架本地空间**，看不见 `VisualModel`（骨架的父节点）的位移——
        //   这正是本探针连续 7 次没量到"格挡下沉"的根因。
        return b < 0 ? 0f : (skel.GlobalTransform * skel.GetBoneGlobalPose(b)).Origin.Y;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D s)
            return s;

        foreach (Node c in node.GetChildren())
        {
            Skeleton3D? f = FindSkeleton(c);

            if (f is not null)
                return f;
        }

        return null;
    }

    // ── ① 跳跃惯性 ─────────────────────────────────────────────────
    private async System.Threading.Tasks.Task CheckJumpInertia()
    {
        GD.Print("[机制体检] ── ① 跳跃惯性 ──");

        // 助跑起来
        Input.ActionPress("sprint");
        Input.ActionPress("move_forward");
        await Wait(45);

        float runSpeed = Horizontal(_player.Velocity);
        GD.Print($"[机制体检]   助跑水平速度 = {runSpeed:F2} m/s");

        // 起跳，然后**松开所有移动键**——这一条才是"惯性"的定义：
        // 松开方向键之后，人应该继续沿原方向飞出去，而不是原地垂直掉下来。
        Input.ActionPress("jump");
        await NextPhysicsFrame();
        Input.ActionRelease("jump");
        Input.ActionRelease("move_forward");
        Input.ActionRelease("sprint");

        var speeds = new System.Collections.Generic.List<float>();
        bool airborne = false;

        for (int i = 0; i < 80; i++)
        {
            await NextPhysicsFrame();

            if (!_player.IsOnFloor())
                airborne = true;

            if (airborne)
            {
                speeds.Add(Horizontal(_player.Velocity));

                if (_player.IsOnFloor() && i > 4)
                    break;
            }
        }

        float first = speeds.Count > 0 ? speeds[0] : 0f;
        float last = speeds.Count > 0 ? speeds[speeds.Count - 1] : 0f;

        GD.Print($"[机制体检]   松开方向键后，滞空水平速度：首帧 {first:F2} → 末帧 {last:F2} m/s" +
                 $"（共 {speeds.Count} 帧）");

        float peak = 0f;
        foreach (float s in speeds)
            peak = Mathf.Max(peak, s);

        GD.Print($"[机制体检]   滞空水平速度：峰值 {peak:F2} m/s / 末帧 {last:F2} m/s");
        GD.Print("[机制体检]   判据说明：**首帧为 0 是正常的**——起跳那一帧人还在地面蹬地，");
        GD.Print("[机制体检]   惯性要看**离地之后**保住了多少，不能看首帧。");

        // 判据：离地后应保住起跳时的大部分速度。
        if (peak < runSpeed * 0.5f)
        {
            _fails.Add($"跳跃没有惯性：起跳前 {runSpeed:F2} m/s，离地后峰值只有 {peak:F2} m/s" +
                       $"（保留 {peak / Mathf.Max(0.01f, runSpeed) * 100f:F0}%）");
        }
    }

    // ── ② 闪避位移 + 无敌期间的实际效果 ─────────────────────────────
    private async System.Threading.Tasks.Task CheckDodgeDisplacement()
    {
        GD.Print("[机制体检] ── ② 闪避位移 / 无敌 ──");

        await Wait(30);

        Vector3 before = _player.GlobalPosition;
        float hpBefore = _player.Health.Current;

        Input.ActionPress("move_forward");
        await Wait(10);
        Input.ActionPress("dodge");
        await NextPhysicsFrame();
        Input.ActionRelease("dodge");

        int invulnFrames = 0;
        var trace = new System.Collections.Generic.List<string>();

        for (int i = 0; i < 40; i++)
        {
            await NextPhysicsFrame();

            if (_player.IsInvulnerableNow)
                invulnFrames++;

            if (i < 12)
            {
                trace.Add($"f{i}:{Horizontal(_player.Velocity):F1}");
            }
        }

        Input.ActionRelease("move_forward");

        float moved = new Vector2(_player.GlobalPosition.X - before.X,
                                  _player.GlobalPosition.Z - before.Z).Length();
        float hpLost = hpBefore - _player.Health.Current;

        GD.Print($"[机制体检]   闪避期间：无敌帧观测 {invulnFrames} 帧，总位移 {moved:F2} m");
        GD.Print($"[机制体检]   闪避速度曲线（每帧水平速度 m/s）：{string.Join(" ", trace)}");

        var dodge = _player.Machine.Get<DodgeState>();
        GD.Print($"[机制体检]   配置：无敌 {dodge.InvulnerableFrames} 帧 / 后摇 {dodge.RecoveryFrames} 帧，" +
                 $"速度 {dodge.Speed:F2} m/s");

        // 无敌帧期间应该滑出的距离（下界）：速度 × 无敌帧时长
        float expectedMin = dodge.Speed * (dodge.InvulnerableFrames / 60f);
        GD.Print($"[机制体检]   无敌帧期间理论最少滑出 {expectedMin:F2} m");

        if (moved < 0.5f)
        {
            _fails.Add($"闪避位移过小：整段只移动 {moved:F2} m（无敌期间理论 {expectedMin:F2} m）——" +
                       $"玩家感觉就是「没闪出去」，进而感觉「没有无敌帧」");
        }

        if (invulnFrames == 0)
            _fails.Add("闪避全程 IsInvulnerableNow 都为假（无敌帧真的没生效）");
        else if (invulnFrames < dodge.InvulnerableFrames - 2)
        {
            _fails.Add($"无敌帧数不符：配置 {dodge.InvulnerableFrames} 帧，实测只观测到 {invulnFrames} 帧");
        }

        if (hpLost > 0f)
            _fails.Add($"闪避中仍然掉血 {hpLost:F0}（无敌帧没有挡住伤害）");
    }

    // ── ③ 闪避成功的奖励 ───────────────────────────────────────────
    private async System.Threading.Tasks.Task CheckPerfectDodgeReward()
    {
        GD.Print("[机制体检] ── ③ 闪避奖励（完美闪避 → 一闪 buff）──");

        await Wait(40);

        int evadedBefore = _player.EvadedAttackCount;

        // 卡在敌人判定帧前闪避（与 DodgeTrainingTest 同一手法）
        bool granted = false;
        int grantedAt = -1;

        for (int i = 0; i < 420 && !granted; i++)
        {
            if (_player.Machine.Current is not DodgeState && FramesUntilEnemyActive() <= 2)
            {
                Input.ActionPress("dodge");
                await NextPhysicsFrame();
                Input.ActionRelease("dodge");
            }
            else
            {
                await NextPhysicsFrame();
            }

            if (_player.IssenBuff != IssenKind.None)
            {
                granted = true;
                grantedAt = i;
                GD.Print($"[机制体检]   完美闪避触发：第 {i} 帧拿到一闪 buff " +
                         $"类型={_player.IssenBuff}，剩余 {_player.IssenBuffFramesLeft} 帧");
            }
        }

        int evaded = _player.EvadedAttackCount - evadedBefore;
        GD.Print($"[机制体检]   躲开 {evaded} 次；奖励 {(granted ? "已发放" : "未发放")}" +
                 (granted ? $"（buff 剩余 {_player.IssenBuffFramesLeft} 帧）" : ""));

        if (evaded == 0)
        {
            _fails.Add("420 帧内一次都没躲开（探针本身没跑到，本轮结论无效）");
            return;
        }

        if (!granted)
            _fails.Add($"躲开了 {evaded} 次，但从来没有拿到过一闪 buff（奖励没有发）");
        else if (_player.IssenBuffFramesLeft < 5)
        {
            _fails.Add($"奖励窗口太短：只剩 {_player.IssenBuffFramesLeft} 帧，" +
                       $"玩家来不及反应（「发了但没感觉到」等于没发）");
        }

        GD.Print("[机制体检]   （提示：奖励是「一闪 buff」，表现为 HUD 一闪指示）");
    }

    // ── ④ 体干恢复时机 ─────────────────────────────────────────────
    private async System.Threading.Tasks.Task CheckPostureRegen()
    {
        GD.Print("[机制体检] ── ④ 体干恢复时机 ──");

        await Wait(20);

        int regenDelay = _enemy.Posture.RegenDelayFrames;
        float regenPerSec = _enemy.Posture.RegenPerSecond;

        GD.Print($"[机制体检]   敌人体干：上限 {_enemy.Posture.Max}，" +
                 $"回复延迟 {regenDelay} 帧，速度 {regenPerSec:F1}/s");

        // 打一刀制造体干，然后逐帧观察"应该等多久才开始回落"
        _enemy.Posture.Apply(20);
        int afterHit = _enemy.Posture.Current;
        GD.Print($"[机制体检]   施加体干 20 → 当前 {afterHit}");

        int firstRegenFrame = -1;

        for (int i = 0; i < regenDelay + 40; i++)
        {
            await NextPhysicsFrame();

            if (_enemy.Posture.Current < afterHit && firstRegenFrame < 0)
            {
                firstRegenFrame = i;
            }
        }

        GD.Print($"[机制体检]   首次回落发生在第 {firstRegenFrame} 帧（配置延迟 {regenDelay} 帧）");

        if (firstRegenFrame >= 0 && firstRegenFrame < regenDelay - 1)
        {
            _fails.Add($"体干在恢复延迟内就开始回落：第 {firstRegenFrame} 帧（配置要求 ≥ {regenDelay} 帧）" +
                       $"——这就是「被攻击后韧性在不该恢复的时候恢复了」");
        }
        else if (firstRegenFrame < 0)
        {
            GD.Print("[机制体检]   （延迟期内没回落 ✓，但 40 帧内也没等到回落——速度可能偏慢）");
        }

        // 破韧期间**绝对不许**回体干（否则破韧窗口会自己消失）
        _enemy.Posture.Apply(_enemy.Posture.Max);
        bool broke = _enemy.Posture.IsBroken;
        int brokenValue = _enemy.Posture.Current;

        GD.Print($"[机制体检]   打满体干：IsBroken={broke}，当前 {brokenValue}");

        for (int i = 0; i < 140; i++)
            await NextPhysicsFrame();

        GD.Print($"[机制体检]   140 帧后（破韧窗口期间）：" +
                 $"IsBroken={_enemy.Posture.IsBroken}，体干 {_enemy.Posture.Current}");

        if (broke && _enemy.Posture.Current < brokenValue && _enemy.Posture.IsBroken)
        {
            _fails.Add($"破韧期间体干回落了：{brokenValue} → {_enemy.Posture.Current}" +
                       $"（破韧窗口会被自己吃掉）");
        }
    }

    private int FramesUntilEnemyActive()
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
            return int.MaxValue;

        return attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
    }

    private static float Horizontal(Vector3 v) => new Vector2(v.X, v.Z).Length();

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor" };
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(80f, 0.2f, 80f) },
            Position = new Vector3(0f, -0.1f, 0f),
        };
        body.AddChild(shape);
        AddChild(body);
    }

    private async System.Threading.Tasks.Task Wait(int frames)
    {
        for (int i = 0; i < frames; i++)
            await NextPhysicsFrame();
    }

    private async System.Threading.Tasks.Task NextPhysicsFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
}
