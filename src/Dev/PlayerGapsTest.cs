using System.Collections.Generic;
using Godot;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 试玩缺口体检：**落下平台** 与 **人物动画覆盖**。
///
///     godot --headless --path . res://scenes/tests/PlayerGaps.tscn
///
/// 这两条的共同点是"用眼睛才看得出来、断言又不好写"，所以本测试的产出
/// **不是 PASS/FAIL，而是一张带数字的缺口表**：
///
/// 1. **落下平台**：把玩家挪到平台外，跑 <see cref="FallFrames"/> 帧，
///    报告 Y 从哪掉到哪、**有没有被重置**。
///    读代码已知：`CombatActor` 有重力（读 `physics/3d/default_gravity`），
///    但**全项目没有任何越界重置**——所以预期是"无限下坠"。
///    这条测试的作用就是把"预期"变成**实测数字**。
///
/// 2. **动画覆盖**：把 `HumanoidAnimator` 逐个动作驱动一遍，采样骨架姿势与 idle 比，
///    报告**每个动作与 idle 的最大骨角差**。
///    **差 0° = 这个动作根本没有专属动画**（现在只有 5 类动作，其余全走 idle 那一套）。
///
/// 退出码 0 ＝ 体检跑完（**不代表缺口已修**）。
/// 等缺口修完，这里要改成硬断言并接进 `tools\check.ps1`（否则它只是一个报告工具）。
/// </summary>
public partial class PlayerGapsTest : Node3D
{
    [Export] public string LevelPath { get; set; } = "res://scenes/levels/Dojo.tscn";

    /// <summary>主角外观模型——动画覆盖体检在它上面采样。</summary>
    [Export] public string ModelPath { get; set; } = "res://assets/models/model_player_congyun_01.glb";

    /// <summary>掉出平台后观察多少帧（180 帧 = 3 秒，足够掉出很远）。</summary>
    [Export] public int FallFrames { get; set; } = 180;

    /// <summary>
    /// 允许低于阈值的余量（米）。**不能设成 0**：确认窗口（12 帧）＋ 淡出（15 帧）
    /// 这段时间玩家还在往下掉，实测大约再掉 6~7 米。余量的作用是
    /// "抓住'根本没重置'（那样会掉到 -41 米）"，而不是要求毫秒级反应。
    /// </summary>
    [Export] public float FallMargin { get; set; } = 14f;

    /// <summary>每个动作采样多少帧（取逐帧与 idle 的最大差，避免"采错时机"）。</summary>
    [Export] public int PoseFrames { get; set; } = 40;

    /// <summary>
    /// 每个动作与 idle 的**最小**允许骨角差。低于它就说明这个动作没有专属动画
    /// （或者幅度小到在背后视角下读不出来）。
    ///
    /// ★ **为什么不是 0.1°**：卡片的原话是"≤0.1° 就是没有专属动画"，那是**探测**用的判据；
    /// 但断言不能定在 0.1°——差 0.2° 的"动作"照样等于没有。这里取 **25°**：
    /// 它是"轮廓一眼能看出不同"的下限，而现有动作（走 72° / 格挡 98° / 普攻 134°）
    /// 都远在它之上，所以这条阈值不会把已经做好的东西误判成坏的。
    /// </summary>
    [Export] public float MinActionAngle { get; set; } = 25f;

    /// <summary>idle 与它自己必须几乎没差——否则"基准"本身在动，后面所有比较都失效。</summary>
    [Export] public float MaxIdleDrift { get; set; } = 5f;

    /// <summary>采样下来的姿势（按动作名索引），用来做"两个姿势必须一眼可分"的成对断言。</summary>
    private readonly Dictionary<string, float[]> _poses = new();

    /// <summary>把玩家挪出去多远（平台是 24×16，Z 方向最远 8，所以 40 一定在外面）。</summary>
    [Export] public float TeleportAwayZ { get; set; } = 40f;

    private Node3D? _player;
    private Vector3 _spawn;
    private float _lowestY;
    private int _frame;
    private int _failures;
    private FallGuard? _guard;
    private int _recoveriesAtPhaseStart;
    private float _profileThresholdY = -6f;
    private int _profileConfirmFrames = 12;
    private Phase _phase = Phase.ControlOnPlatform;
    private int _phaseFrame;

    private enum Phase
    {
        /// <summary>对照组 A：正常站在平台上，一次都不该重置。</summary>
        ControlOnPlatform,

        /// <summary>对照组 B：站在边缘内侧 0.5m，同样不该重置（防误触发）。</summary>
        ControlNearEdge,

        /// <summary>正式：挪到平台外，该被送回安全点。</summary>
        FallOff,
        Done,
    }

    public override void _Ready()
    {
        ProbeAnimationCoverage();

        if (!ResourceLoader.Exists(LevelPath))
        {
            GD.PrintErr($"[缺口] 找不到关卡：{LevelPath}");
            GetTree().Quit(1);
            return;
        }

        var level = GD.Load<PackedScene>(LevelPath).Instantiate<Node3D>();
        AddChild(level);

        _player = FindPlayer(level);
        if (_player is null)
        {
            GD.PrintErr("[缺口] 关卡里没找到 PlayerActor");
            GetTree().Quit(1);
            return;
        }

        _spawn = _player.GlobalPosition;
        _lowestY = _spawn.Y;

        // 阈值直接从玩家身上那份**资源**读——这样体检断的就是真实生效的参数，
        // 而不是测试里另抄一份（抄一份的话，改了 .tres 测试还"通过"）。
        _guard = _player.GetNodeOrNull<FallGuard>("FallGuard");
        if (_guard?.Profile is { } profile)
        {
            _profileThresholdY = profile.ThresholdY;
            _profileConfirmFrames = profile.ConfirmFrames;
        }
        else
        {
            GD.PrintErr("[缺口] 玩家身上没有 FallGuard 或其 Profile——掉落保护没挂上");
            _failures++;
        }

        GD.Print("");
        GD.Print($"[缺口] 落下平台：安全点（出生点）{Fmt(_spawn)}，每组观察 {FallFrames} 帧");
        GD.Print($"[缺口]   阈值 {_profileThresholdY:F1}m ＋ 连续 {_profileConfirmFrames} 帧确认，"
                 + $"允许再低 {FallMargin:F0}m（确认窗与淡出期间还在掉）");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
            return;

        switch (_phase)
        {
            case Phase.ControlOnPlatform:
                StepPhase(
                    "对照组 A（站在平台上）",
                    () => { },
                    () => Assert(_guard!.RecoveryCount - _recoveriesAtPhaseStart == 0,
                        "全程一次都没重置",
                        "正常站着却被送回去了——会误伤正常游玩"));
                return;

            case Phase.ControlNearEdge:
                StepPhase(
                    "对照组 B（站在边缘内侧 0.5m）",
                    // 道场地板 Z 到 8，站到 7.5 就是"贴着边但还在上面"。
                    () => _player.GlobalPosition = new Vector3(_spawn.X, _spawn.Y, 7.5f),
                    () => Assert(_guard!.RecoveryCount - _recoveriesAtPhaseStart == 0,
                        "贴着边站着也没重置",
                        "贴着边就触发重置——阈值或余量太紧"));
                return;

            case Phase.FallOff:
                StepPhase(
                    "正式（挪到平台外）",
                    () => _player.GlobalPosition = _spawn + new Vector3(0f, 2f, TeleportAwayZ),
                    AssertFallRecovered);
                return;

            default:
                return;
        }
    }

    /// <summary>跑完一个阶段：<paramref name="setup"/> 只在第一帧执行，<paramref name="verify"/> 在最后一帧执行。</summary>
    private void StepPhase(string name, System.Action setup, System.Action verify)
    {
        if (_phaseFrame == 0)
        {
            _lowestY = _player!.GlobalPosition.Y;
            _recoveriesAtPhaseStart = _guard?.RecoveryCount ?? 0;
            setup();
            GD.Print($"[缺口] ▶ {name}");
        }

        _phaseFrame++;
        _lowestY = Mathf.Min(_lowestY, _player!.GlobalPosition.Y);

        if (_phaseFrame < FallFrames)
            return;

        verify();
        _phaseFrame = 0;
        _phase = _phase switch
        {
            Phase.ControlOnPlatform => Phase.ControlNearEdge,
            Phase.ControlNearEdge => Phase.FallOff,
            _ => Phase.Done,
        };

        if (_phase != Phase.Done)
            return;

        GD.Print("");
        GD.Print(_failures == 0
            ? "[缺口] ✓ 掉落保护 + 动画覆盖 全部通过"
            : $"[缺口] ✗ {_failures} 条没过（见上面）");
        GD.Print("[缺口] 体检完成");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void AssertFallRecovered()
    {
        float minAllowed = _profileThresholdY - FallMargin;
        int recoveries = (_guard?.RecoveryCount ?? 0) - _recoveriesAtPhaseStart;

        Assert(recoveries >= 1, "掉出去以后被送回了安全点",
            "一次都没送回来——这就是要修的缺口 T45");
        Assert(_lowestY >= minAllowed,
            "下坠深度在允许范围内",
            $"掉到了 {_lowestY:F2}m，低于允许下限 {minAllowed:F2}m——确认窗口或淡出太久");
        Assert(_player is not null && _player.GlobalPosition.DistanceTo(_spawn) < 1f,
            "最终位置就在安全点（1m 内）",
            $"最终停在 {Fmt(_player!.GlobalPosition)}，安全点是 {Fmt(_spawn)}");

        GD.Print($"[缺口]   最低 Y = {_lowestY:F2}（允许下限 {minAllowed:F2}），"
                 + $"送回 {recoveries} 次，最终 {Fmt(_player!.GlobalPosition)}");
    }

    /// <summary><paramref name="what"/> 是要断言的事实（正面表述）；<paramref name="detail"/> 失败时才打印。</summary>
    private void Assert(bool ok, string what, string detail = "")
    {
        if (ok)
        {
            GD.Print($"[缺口]   ✓ {what}");
            return;
        }

        _failures++;
        GD.PrintErr($"[缺口]   ✗ {what}　—— {detail}");
    }

    // ── 动画覆盖 ────────────────────────────────────────────────────────

    /// <summary>
    /// 逐个动作驱动 `HumanoidAnimator`，采样骨架姿势，把"缺少专属动画"变成**硬断言**。
    ///
    /// 这张表原来是**纯报告**（打印数字、退出码恒 0），于是"动画缺不缺"只存在于日志里，
    /// `check.ps1` 全绿并不能说明动画是齐的——T38 卡点名要把这一步接成断言，就是这个意思。
    ///
    /// 三类断言：
    /// ① idle 基准稳定（否则后面全部失效）；
    /// ② 每个动作与 idle 的差 ≥ <see cref="MinActionAngle"/>；
    /// ③ 三组"必须一眼可分"的姿势，两两之差 ≥ 同一个阈值。
    /// </summary>
    private void ProbeAnimationCoverage()
    {
        GD.Print("[缺口] 动画覆盖体检：每个动作与 idle 的最大骨角差（差得越少 = 越没有专属姿态）");

        if (!ResourceLoader.Exists(ModelPath))
        {
            GD.PrintErr($"[缺口]   找不到模型：{ModelPath}");
            _failures++;
            return;
        }

        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        AddChild(template);

        Skeleton3D? skeleton = FindSkeleton(template);
        if (skeleton is null)
        {
            GD.PrintErr("[缺口]   模型里没有 Skeleton3D——动画体检做不了");
            _failures++;
            return;
        }

        float[] idle = SamplePose(skeleton, new HumanoidAnimator(template), ActionKind.Idle, null);
        _poses[nameof(ActionKind.Idle)] = idle;
        GD.Print($"[缺口]   （骨架 {skeleton.GetBoneCount()} 根骨，每个动作采样 {PoseFrames} 帧取最大差）");

        var kinds = new (ActionKind Kind, string Name)[]
        {
            (ActionKind.Idle, "idle（基准）"),
            (ActionKind.Walk, "走 / 跑"),
            (ActionKind.Guard, "格挡（按住）"),
            (ActionKind.Deflect, "弹开成功"),
            (ActionKind.GuardBreak, "体干破裂"),
            (ActionKind.Attack, "普攻（三连共用）"),
            (ActionKind.Issen, "一闪"),
            (ActionKind.Hit, "受击"),
            (ActionKind.Dodge, "闪避"),
            (ActionKind.Heal, "喝血"),
            (ActionKind.Jump, "跳跃"),
            (ActionKind.Death, "死亡"),
        };

        foreach ((ActionKind kind, string name) in kinds)
        {
            float[] pose = SamplePose(skeleton, new HumanoidAnimator(template), kind, idle);
            _poses[kind.ToString()] = pose;

            float diff = MaxBoneAngleDegrees(idle, pose);
            if (kind == ActionKind.Idle)
            {
                Assert(diff <= MaxIdleDrift, $"idle 基准稳定（自身漂移 {diff:F1}°）",
                    $"基准自己就在动（{diff:F1}°）——后面所有比较都不可信");
                continue;
            }

            Assert(diff >= MinActionAngle, $"{name}：与 idle 差 {diff:F1}°，有专属姿态",
                $"只差 {diff:F1}°，低于 {MinActionAngle:F0}° —— 等于没有专属动画");
        }

        // 成对可分性。T38 卡验收第 4 条点名的就是这条：
        // 「格挡 / 弹开 / 体干破裂」必须**一眼能分辨**；死亡与受击也要分得清，
        // 否则"死了"在玩家眼里只是"抖了一下"。
        AssertPairDistinct("格挡", ActionKind.Guard, "弹开成功", ActionKind.Deflect);
        AssertPairDistinct("格挡", ActionKind.Guard, "体干破裂", ActionKind.GuardBreak);
        AssertPairDistinct("死亡", ActionKind.Death, "受击", ActionKind.Hit);
    }

    /// <summary>两个姿势之间的最大骨角差必须够大——"一眼可分"要能被量出来，不能靠嘴说。</summary>
    private void AssertPairDistinct(string nameA, ActionKind a, string nameB, ActionKind b)
    {
        if (!_poses.TryGetValue(a.ToString(), out float[]? poseA) ||
            !_poses.TryGetValue(b.ToString(), out float[]? poseB))
            return;

        float diff = MaxBoneAngleDegrees(poseA, poseB);
        Assert(diff >= MinActionAngle, $"{nameA} 与 {nameB} 可区分（差 {diff:F1}°）",
            $"两者只差 {diff:F1}°，玩家一眼分不出来");
    }

    private enum ActionKind { Idle, Walk, Guard, Deflect, GuardBreak, Attack, Issen, Hit, Dodge, Heal, Jump, Death }

    /// <summary>
    /// 把一个动作跑 <see cref="PoseFrames"/> 帧，返回**与 <paramref name="idleRef"/> 差得最远的那一帧**的姿势。
    /// 传 <c>null</c> 时（测 idle 本身）返回最后一帧——那时姿势已经完全稳定。
    /// 每个动作都用**新的 animator**，免得内部计时器互相污染。
    /// </summary>
    private float[] SamplePose(Skeleton3D skeleton, HumanoidAnimator animator, ActionKind kind, float[]? idleRef)
    {
        float[] best = Snapshot(skeleton);
        float bestDiff = -1f;
        float dt = 1f / 60f;

        for (int f = 0; f < PoseFrames; f++)
        {
            int attackFrame = -1;

            // T38 之后防御不再是一个 bool，而是"进入防御后第几帧"（抬起/维持/放下三态）。
            // 非防御动作要显式给 -1，否则会一直停在上一轮留下的防御姿态里。
            animator.TrackGuard(kind == ActionKind.Guard ? f : -1);

            switch (kind)
            {
                case ActionKind.Attack:
                    if (f == 0) animator.PlayAttack(0, 30);   // Dev 体检用固定帧数（真调用点都传招式数据）
                    attackFrame = f;                          // T38：攻击姿势由逻辑帧算出
                    break;
                case ActionKind.Issen:
                    if (f == 0) animator.PlayIssen(22);
                    break;
                case ActionKind.Hit:
                    if (f == 0) animator.PlayHitReact(2f, 18);
                    break;
                case ActionKind.Deflect:
                    if (f == 0) animator.PlayDeflect(16);
                    break;
                case ActionKind.GuardBreak:
                    if (f == 0) animator.PlayGuardBreak(24);
                    break;
                case ActionKind.Dodge:
                    // 右闪。侧向的轮廓与前后向不一样，而侧闪是实战里按得最多的一个。
                    if (f == 0) animator.PlayDodge(Vector3.Right, 26);
                    break;
                case ActionKind.Heal:
                    if (f == 0) animator.PlayHeal(18, 24, 12);   // 起手 / 饮用 / 收招（Dev 用固定帧数）
                    break;
                case ActionKind.Jump:
                    // 跳跃是**逐帧轮询**的（滞空多少帧是物理结果，数据里没有这个数），
                    // 所以这里按帧推进三段：前 1/3 蹬地、中 1/3 腾空、后 1/3 落地。
                    int third = Mathf.Max(1, PoseFrames / 3);
                    animator.TrackJump(f < third ? 0 : (f < third * 2 ? 1 : 2), 12);
                    break;
                case ActionKind.Death:
                    if (f == 0) animator.PlayDeath(40);
                    break;
            }

            // ★ 必须**照玩家实际的每帧调用路径**驱动。`PlayerActor.OnTickVisual` 每帧都是
            //   先写防御帧号、再 `AnimateLocomotion(speed01)`、最后 `AnimateCombat`。
            //   少调一次 AnimateLocomotion，骨架会停在静止姿态，
            //   量出来的是**假差**（第一次跑就是这么被骗的：四个没接口的动作全报 91.7°）。
            animator.AnimateLocomotion(kind == ActionKind.Walk ? 1f : 0f, dt);
            animator.AnimateCombat(dt, attackFrame);

            float[] current = Snapshot(skeleton);

            if (idleRef is null)
            {
                best = current;          // 测 idle 本身：取最后一帧（已稳定）
                continue;
            }

            float diff = MaxBoneAngleDegrees(idleRef, current);
            if (diff > bestDiff)
            {
                bestDiff = diff;
                best = current;          // **差得最远的那一帧**才是这个动作最有代表性的姿势
            }
        }

        return best;

        // 注：`best` 初值是第 0 帧前的静止姿势，所以哪怕循环一帧都没跑也不会返回 null。
    }

    private static float[] Snapshot(Skeleton3D skeleton)
    {
        var flat = new List<float>();
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            Quaternion q = skeleton.GetBonePoseRotation(i);
            flat.Add(q.X);
            flat.Add(q.Y);
            flat.Add(q.Z);
            flat.Add(q.W);
        }

        return flat.ToArray();
    }

    private static float MaxBoneAngleDegrees(float[] a, float[] b)
    {
        float max = 0f;
        for (int i = 0; i + 3 < a.Length && i + 3 < b.Length; i += 4)
        {
            var qa = new Quaternion(a[i], a[i + 1], a[i + 2], a[i + 3]);
            var qb = new Quaternion(b[i], b[i + 1], b[i + 2], b[i + 3]);
            max = Mathf.Max(max, Mathf.RadToDeg(qa.AngleTo(qb)));
        }

        return max;
    }

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

    private static PlayerActor? FindPlayer(Node node)
    {
        if (node is PlayerActor player)
            return player;

        foreach (Node child in node.GetChildren())
        {
            PlayerActor? found = FindPlayer(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string Fmt(Vector3 v) => $"({v.X:F1}, {v.Y:F2}, {v.Z:F1})";
}
