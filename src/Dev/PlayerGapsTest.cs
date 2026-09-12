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
            ? "[缺口] ✓ 掉落保护全部通过"
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
    /// 逐个动作驱动 `HumanoidAnimator`，采样骨架姿势，报告与 idle 的**最大骨角差**。
    /// 差 0° 就说明这个动作**没有专属动画**——它和站着不动一模一样。
    /// </summary>
    private void ProbeAnimationCoverage()
    {
        GD.Print("[缺口] 动画覆盖体检：每个动作与 idle 的最大骨角差（0° = 没有专属动画）");

        if (!ResourceLoader.Exists(ModelPath))
        {
            GD.PrintErr($"[缺口]   找不到模型：{ModelPath}");
            return;
        }

        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        AddChild(template);

        Skeleton3D? skeleton = FindSkeleton(template);
        if (skeleton is null)
        {
            GD.PrintErr("[缺口]   模型里没有 Skeleton3D——动画体检做不了");
            return;
        }

        float[] idle = SamplePose(skeleton, new HumanoidAnimator(template), ActionKind.Idle, null);
        GD.Print($"[缺口]   （骨架 {skeleton.GetBoneCount()} 根骨，每个动作采样 {PoseFrames} 帧取最大差）");

        var kinds = new (ActionKind Kind, string Name)[]
        {
            (ActionKind.Idle, "idle（基准）"),
            (ActionKind.Walk, "走 / 跑"),
            (ActionKind.Guard, "格挡（按住）"),
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
            float diff = MaxBoneAngleDegrees(idle, pose);
            string verdict = diff < 0.5f ? "**没有专属动画**" : "有";
            GD.Print($"[缺口]   {name,-16} 与 idle 最大差 {diff,6:F1}°　{verdict}");
        }
    }

    private enum ActionKind { Idle, Walk, Guard, Attack, Issen, Hit, Dodge, Heal, Jump, Death }

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
            switch (kind)
            {
                case ActionKind.Guard:
                    animator.Guarding = true;
                    break;
                case ActionKind.Attack:
                    if (f == 0) animator.PlayAttack();
                    break;
                case ActionKind.Issen:
                    if (f == 0) animator.PlayIssen();
                    break;
                case ActionKind.Hit:
                    if (f == 0) animator.PlayHitReact(2f);
                    break;
                default:
                    // 闪避 / 喝血 / 跳跃 / 死亡：**animator 上根本没有对应接口**，
                    // 所以什么都不驱动——这正是我们要测出来的缺口。
                    break;
            }

            // ★ 必须**照玩家实际的每帧调用路径**驱动。`PlayerActor.OnTickVisual` 每帧都是
            //   先写 Guarding、再 `AnimateLocomotion(speed01)`、最后 `AnimateCombat`。
            //   少调一次 AnimateLocomotion，骨架会停在静止姿态，
            //   量出来的是**假差**（第一次跑就是这么被骗的：四个没接口的动作全报 91.7°）。
            animator.AnimateLocomotion(kind == ActionKind.Walk ? 1f : 0f, dt);
            animator.AnimateCombat(dt);

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
