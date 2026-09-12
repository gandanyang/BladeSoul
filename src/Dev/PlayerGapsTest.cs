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

    /// <summary>每个动作采样多少帧（取逐帧与 idle 的最大差，避免"采错时机"）。</summary>
    [Export] public int PoseFrames { get; set; } = 40;

    /// <summary>把玩家挪出去多远（平台是 24×16，Z 方向最远 8，所以 40 一定在外面）。</summary>
    [Export] public float TeleportAwayZ { get; set; } = 40f;

    private Node3D? _player;
    private Vector3 _spawn;
    private float _lowestY;
    private bool _everReset;
    private int _frame;

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

        // 挪到平台外：Z 方向最远 8（+ 廊下到 12），40 一定在外面。
        _player.GlobalPosition = _spawn + new Vector3(0f, 2f, TeleportAwayZ);

        GD.Print("");
        GD.Print($"[缺口] 落下平台：把玩家从 {Fmt(_spawn)} 挪到 {Fmt(_player.GlobalPosition)}，观察 {FallFrames} 帧");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
            return;

        _frame++;
        Vector3 p = _player.GlobalPosition;
        _lowestY = Mathf.Min(_lowestY, p.Y);

        // "重置"的判据：回到出生点附近（1m 内）——现在项目里没有任何逻辑做这件事，
        // 所以这里预期**永远是 false**。一旦有人加了越界重置，这一行就会变 true。
        if (p.DistanceTo(_spawn) < 1f)
            _everReset = true;

        if (_frame % 60 == 0)
            GD.Print($"[缺口]   第 {_frame,3} 帧：Y = {p.Y,10:F2}（起点 {_spawn.Y:F2}）");

        if (_frame < FallFrames)
            return;

        Vector3 final = _player.GlobalPosition;
        GD.Print($"[缺口]   结束：Y = {final.Y:F2}，最低 Y = {_lowestY:F2}，"
                 + $"总下落 {_spawn.Y - _lowestY:F2}m");
        GD.Print($"[缺口]   → 越界重置：{(_everReset ? "有" : "**没有**")}"
                 + (_everReset ? "" : "　← 掉下去就再也回不来了，这是确认的缺口"));
        GD.Print("");
        GD.Print("[缺口] 体检完成（退出码 0 只代表跑完了，不代表缺口已修）");
        GetTree().Quit(0);
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
