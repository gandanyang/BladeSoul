using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 腿部姿势体检（T52 试玩反馈："弹开时主角的腿反着往前弯曲"）。
///
/// # 为什么必须"真打一次"
/// 第一版探针自己 `new HumanoidAnimator(玩家)` 再 `PlayDeflect(...)`，
/// 结论是"所有姿势数值逐字节相同"。不是腿一直是直的，而是
/// **`PlayerActor` 持有自己的 `_skinAnimator`**（`PlayerActor.cs:90`），
/// 每帧 `AnimateLocomotion` 先 `ResetBonePoses` 再摆姿势——
/// 独立实例写的姿势同一帧就被覆盖。**探针自己把结论废掉了。**
///
/// 所以走真实链路：真敌人砍真玩家、真按键格挡、真触发弹开，再量骨骼位置。
///
/// # 输出**坐标**，不输出"前后"结论
/// 前两版都在纠结"哪边是前"：先用「髋→膝」推前方（等于拿被怀疑的骨头定义基准），
/// 后用 Hip 朝向（实测它的 +Z 是**身后**）。两次都得到自相矛盾的数
/// （"静止站姿也反折 22cm"）——那是判据坏了，不是腿坏了。
///
/// 现在只打印髋/膝/踝的实测坐标，判断交给**和静止站姿对比**：
/// 唯一可靠的方向基准是"正常姿势长什么样"，不是某个我猜的轴。
/// </summary>
public partial class LegPoseProbe : Node3D
{
    private const int GuardLeadFrames = 6;

    private static readonly string[] Sides = { "L", "R" };

    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;
    private Skeleton3D? _skel;

    /// <summary>反折帧记录（脚高过膝 = 小腿反向折叠，几何上不可能）。</summary>
    private readonly System.Collections.Generic.List<string> _violations = new();

    public override void _Ready() => _ = RunAsync();

    private async System.Threading.Tasks.Task RunAsync()
    {
        try { await RunCore(); }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[腿体检] ✗ 未捕获异常：{ex}");
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

        await Wait(5);

        _skel = FindSkeleton(_player);

        if (_skel is null)
        {
            GD.PrintErr("[腿体检] ✗ 玩家没有 Skeleton3D");
            GetTree().Quit(1);
            return;
        }

        // 打开写入者追踪：光看坐标无法判断"是谁写的"，这一层才能定位到函数。
        _player.SkinAnimatorForProbe!.Diagnostics = true;

        GD.Print("[腿体检] ── 腿部姿势实测（世界坐标，相对玩家原点，单位 cm）──");
        GD.Print("[腿体检] 参照：敌人在 z = -170 cm 一侧，也就是它砍的是玩家正面");

        // ① 基准：静止站姿
        await Wait(12);
        await Measure("静止", 6);

        // ② 对照组：走路（用户没报有问题）
        Input.ActionPress("move_forward");
        await Wait(20);
        await Measure("走路", 30);
        Input.ActionRelease("move_forward");
        await Wait(20);

        // ③ 目标：真按键格挡 → 真弹开
        if (!await DeflectOnce())
        {
            GetTree().Quit(1);
            return;
        }

        // ★ 接上弹开姿势的回放口：证明"姿势表真的被执行了"，
        //   而不是靠结果反推。没有这一行，看到异常数值时无法区分
        //   "姿势表没跑" 和 "姿势表跑了但数值错"。
        _player.SkinAnimatorForProbe!.DeflectPoseLog = (t, snap) =>
            GD.Print($"[腿体检]     ApplyDeflectPose t={t:F2} snap={snap:F2}");

        await Measure("弹开", 12);

        // ── 判定：正常姿势里脚**永远**不该高过膝 ──────────────────
        //
        // 这是**几何不变量**，不是审美偏好：膝是髋踝之间的关节，脚在膝上方
        // 意味着小腿反向折叠——人腿没有这个自由度。正因为它是硬约束，
        // 才能当作棘轮用（美术好不好看没法自动判，这个可以）。
        if (_violations.Count == 0)
        {
            GD.Print("[腿体检] ✓ 三个姿势全部合法（脚始终低于膝）");
            GetTree().Quit(0);
            return;
        }

        foreach (string v in _violations)
            GD.Print($"[腿体检] ✗ {v}");

        GD.Print($"[腿体检] ✗ 共 {_violations.Count} 帧腿部反折");
        GetTree().Quit(1);
    }

    /// <summary>真按键格挡直到弹开。时机用"敌人招式判定帧还剩几帧"卡（同 DeflectTrainingTest）。</summary>
    private async System.Threading.Tasks.Task<bool> DeflectOnce()
    {
        bool guardHeld = false;

        for (int i = 0; i < 480; i++)
        {
            if (!guardHeld && FramesUntilEnemyActive() <= GuardLeadFrames)
            {
                Input.ActionPress("guard");
                guardHeld = true;
            }
            else if (guardHeld && _attacker.Machine.Current is not AttackState)
            {
                Input.ActionRelease("guard");
                guardHeld = false;
            }

            await NextPhysicsFrame();

            if (_player.Machine.Current is DeflectState)
            {
                GD.Print($"[腿体检] （第 {i} 帧弹开成功）");
                return true;
            }
        }

        Input.ActionRelease("guard");
        GD.PrintErr("[腿体检] ✗ 480 帧内没触发弹开——本次测量无效（不能拿它下结论）");
        return false;
    }

    /// <summary>敌人当前招式的判定帧还剩几帧；不在出招则返回 int.MaxValue。</summary>
    private int FramesUntilEnemyActive()
    {
        if (_attacker.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
            return int.MaxValue;

        return attack.Sequence.Current.ActiveStart - attack.Sequence.Frame;
    }

    private async System.Threading.Tasks.Task Measure(string label, int frames)
    {
        Vector3 origin = _player.GlobalPosition;
        float minLowerY = float.MaxValue;
        float maxLowerY = float.MinValue;
        float minKneeZ = float.MaxValue;
        float maxKneeZ = float.MinValue;

        GD.Print($"[腿体检] ── {label} ──");

        for (int i = 0; i < frames; i++)
        {
            // ★ 每帧清空写入记录。不清的话读到的是**更早帧的残留**，
            //   "最后写入者"就变成了"最后一个写过的人"，跨帧累积后完全失真。
            _player.SkinAnimatorForProbe!.ClearWriters();

            foreach (string side in Sides)
            {
                Vector3 hip = BonePos($"{side}_Thigh") - origin;
                Vector3 knee = BonePos($"{side}_Calf") - origin;
                Vector3 ankle = BonePos($"{side}_Foot") - origin;

                Vector3 lower = ankle - knee;
                Vector3 upper = knee - hip;

                // 脚高过膝 2cm 以上算违规（留一点数值抖动余量）
                if (lower.Y > 0.02f)
                {
                    _violations.Add($"{label} 第{i + 1}帧 {side}：" +
                                    $"脚比膝高 {lower.Y * 100f:F1} cm（小腿反向折叠）");
                }

                minLowerY = Mathf.Min(minLowerY, lower.Y);
                maxLowerY = Mathf.Max(maxLowerY, lower.Y);
                minKneeZ = Mathf.Min(minKneeZ, knee.Z);
                maxKneeZ = Mathf.Max(maxKneeZ, knee.Z);

                // ★ 只看左腿，每帧都打 —— 定位"哪几帧反折"必须逐帧，
                //   只打首尾帧会把中间的异常帧漏掉（第一版就是这么漏掉的）。
                if (side == "L")
                {
                    GD.Print($"[腿体检]   {label,-4} 第{i + 1,2}帧 L：" +
                             $"踝膝高差 {lower.Y * 100f,7:F1} cm（正=脚比膝高 ✘）、" +
                             $"膝前移 {upper.Z * 100f,7:F1} cm、" +
                             $"膝高 {knee.Y * 100f,7:F1} cm");
                }

                // ★ 逐骨追"最后写入者"。坐标只能说明"结果不对"，
                //   这一行才能说明"**是哪个姿势函数写成了这个值**"。
                if (label == "弹开" && side == "L" && i < 12)
                {
                    GD.Print($"[腿体检]        状态机 {_player.Machine.Current?.GetType().Name}  " +
                             $"IsDeflecting={_player.SkinAnimatorForProbe!.IsDeflecting}");
                }

                if (label == "弹开" && side == "L")
                {
                    HumanoidAnimator a = _player.SkinAnimatorForProbe!;
                    GD.Print($"[腿体检]        写入者  " +
                             $"L_Thigh←{a.LastWriterOf("L_Thigh")}({a.LastValueOf("L_Thigh"):F2})  " +
                             $"L_Calf←{a.LastWriterOf("L_Calf")}({a.LastValueOf("L_Calf"):F2})  " +
                             $"R_Thigh←{a.LastWriterOf("R_Thigh")}({a.LastValueOf("R_Thigh"):F2})  " +
                             $"R_Calf←{a.LastWriterOf("R_Calf")}({a.LastValueOf("R_Calf"):F2})  " +
                             $"Hip←{a.LastWriterOf("Hip")}({a.LastValueOf("Hip"):F2})");
                }
            }

            await NextPhysicsFrame();
        }

        GD.Print($"[腿体检]   {label,-4} 小结：踝膝高差 {minLowerY * 100f:F1} ~ {maxLowerY * 100f:F1} cm" +
                 $"（正常应恒为负 = 脚在膝下）；膝 Z {minKneeZ * 100f:F1} ~ {maxKneeZ * 100f:F1} cm");
    }

    private Vector3 BonePos(string name)
    {
        int idx = _skel!.FindBone(name);

        return idx < 0 ? Vector3.Zero : _skel.GetBoneGlobalPose(idx).Origin;
    }

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor" };
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(60f, 0.2f, 60f) },
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

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
            return skeleton;

        foreach (Node child in node.GetChildren())
        {
            Skeleton3D? found = FindSkeleton(child);

            if (found is not null)
                return found;
        }

        return null;
    }
}
