using System.Collections.Generic;
using Godot;
using Oniblade.Combat.Data;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 动画覆盖验收（T38）。**把"一眼能分辨"变成能跑的断言**——
/// 卡片原话是"现在弹开与格挡无法区分"，那就不该靠"我看着像"来收工。
///
///     godot --headless --path . res://scenes/tests/AnimCoverage.tscn
///
/// 四件事：
/// 1. **三段斩是三个不同的动作**：同一拍上两两之间的最大骨角差要够大
///    （硬约束：不许用"同一个挥砍换幅度"假装三段）。
/// 2. **格挡有起手**：抬起中（第 2 帧）与维持（第 20 帧）必须不同，
///    否则还是"一个 bool 切一个静态姿势"。
/// 3. **弹开与格挡一眼可分**：弹开的顿挫峰值与格挡维持的差要够大。
/// 4. **动画与逻辑不会漂**：同一个逻辑帧号驱动两次，姿势必须完全一致
///    （这是"动画只被逻辑帧驱动"的直接证据，也是 04 §13 `ANIM SYNC` 的上游保证）。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class AnimCoverageTest : Node3D
{
    private readonly List<string> _failures = new();

    private const string ModelPath = "res://assets/models/model_player_congyun_01.glb";

    /// <summary>判"两个动作不一样"的阈值（度）。</summary>
    private const float DistinctDegrees = 25f;

    public override void _Ready()
    {
        if (!ResourceLoader.Exists(ModelPath))
        {
            GD.PrintErr($"[动画] 找不到模型：{ModelPath}");
            GetTree().Quit(1);
            return;
        }

        try
        {
            CheckAttackStepsAreDistinct();
            CheckGuardHasRaisePhase();
            CheckDeflectDiffersFromGuard();
            CheckFrameDrivenDoesNotDrift();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex}");
        }

        Report();
    }

    // ── 1. 三段是三个动作 ──────────────────────────────────────

    private void CheckAttackStepsAreDistinct()
    {
        // 帧数取自真实招式数据——**动画时长必须与帧数据一致**（T38 硬约束）
        (string Path, int Step, string Name)[] steps =
        {
            ("res://data/attacks/player/light_01.tres", 0, "轻斩壹"),
            ("res://data/attacks/player/light_02.tres", 1, "轻斩贰"),
            ("res://data/attacks/player/light_03.tres", 2, "轻斩叁"),
        };

        var poses = new float[steps.Length][];
        var totals = new int[steps.Length];

        for (int i = 0; i < steps.Length; i++)
        {
            AttackData data = GD.Load<AttackData>(steps[i].Path);
            Check(data is not null, $"读不到 {steps[i].Path}");

            totals[i] = data?.TotalFrames ?? 26;

            // 取"出刀刚结束"那一拍（t≈0.62）——那是三条刀路分得最开的时候
            int strikeFrame = Mathf.RoundToInt(totals[i] * 0.62f);
            poses[i] = PoseAtAttackFrame(steps[i].Step, totals[i], strikeFrame);
        }

        GD.Print($"[动画] 三段斩帧数据：壹 {totals[0]} / 贰 {totals[1]} / 叁 {totals[2]} 帧");

        // 抓"同一个动作换幅度"这种偷懒：任意两段都必须在同一拍上明显不同
        for (int a = 0; a < poses.Length; a++)
        {
            for (int b = a + 1; b < poses.Length; b++)
            {
                float diff = MaxBoneAngleDegrees(poses[a], poses[b]);

                Check(diff >= DistinctDegrees,
                    $"{steps[a].Name} 与 {steps[b].Name} 在第 {Mathf.RoundToInt(totals[a] * 0.62f)} 帧上只差 {diff:0.#}°" +
                    $"（判据 ≥{DistinctDegrees:0}°）——这是同一个动作换幅度，不是三段刀路");

                GD.Print($"[动画]   {steps[a].Name} vs {steps[b].Name}：最大骨角差 {diff:0.#}°");
            }
        }
    }

    /// <summary>把攻击驱动到指定逻辑帧，返回骨架姿势。</summary>
    private float[] PoseAtAttackFrame(int step, int totalFrames, int frame)
    {
        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        Skeleton3D skeleton = FindSkeleton(template)!;
        var animator = new HumanoidAnimator(template) { };

        animator.PlayAttack(step, totalFrames);

        for (int f = 0; f <= frame; f++)
            animator.AnimateCombat(1f / 60f, f);

        return Snapshot(skeleton);
    }

    // ── 2. 格挡三态 ────────────────────────────────────────────

    private void CheckGuardHasRaisePhase()
    {
        float[] raising = PoseWhileGuard(2);
        float[] held = PoseWhileGuard(20);

        float diff = MaxBoneAngleDegrees(raising, held);

        Check(diff >= 10f,
            $"格挡第 2 帧与第 20 帧只差 {diff:0.#}°——格挡没有起手，" +
            "还是'一个 bool 切一个静态姿势'（T38 要的就是三段：抬起/维持/放下）");

        GD.Print($"[动画]   格挡 抬起中 vs 维持：最大骨角差 {diff:0.#}°（有起手 ✓）");
    }

    private float[] PoseWhileGuard(int frame)
    {
        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        Skeleton3D skeleton = FindSkeleton(template)!;
        var animator = new HumanoidAnimator(template);

        for (int f = 0; f <= frame; f++)
        {
            animator.TrackGuard(f);
            animator.AnimateLocomotion(0f, 1f / 60f);
            animator.AnimateCombat(1f / 60f, -1);
        }

        return Snapshot(skeleton);
    }

    // ── 3. 弹开与格挡必须一眼可分 ──────────────────────────────

    private void CheckDeflectDiffersFromGuard()
    {
        float[] held = PoseWhileGuard(20);
        float[] deflect = PoseAtDeflectPeak();

        float diff = MaxBoneAngleDegrees(held, deflect);

        Check(diff >= DistinctDegrees,
            $"弹开姿势与格挡维持只差 {diff:0.#}°（判据 ≥{DistinctDegrees:0}°）——" +
            "玩家看不出'我接住了'，这正是这张卡存在的理由");

        GD.Print($"[动画]   弹开 vs 格挡维持：最大骨角差 {diff:0.#}°（一眼可分 ✓）");
    }

    private float[] PoseAtDeflectPeak()
    {
        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        Skeleton3D skeleton = FindSkeleton(template)!;
        var animator = new HumanoidAnimator(template);

        // 先站进防御，再弹开——弹开总是从防御姿态里发生的
        for (int f = 0; f <= 20; f++)
        {
            animator.TrackGuard(f);
            animator.AnimateLocomotion(0f, 1f / 60f);
            animator.AnimateCombat(1f / 60f, -1);
        }

        animator.TrackGuard(-1);
        animator.PlayDeflect(12);

        // 顿挫的峰值在 t≈0.35，也就是第 4 帧附近
        for (int f = 0; f < 4; f++)
            animator.AnimateCombat(1f / 60f, -1);

        Check(animator.IsDeflecting, "弹开姿势没在播（PlayDeflect 没生效）");

        return Snapshot(skeleton);
    }

    // ── 4. 帧驱动 ⇒ 不会漂 ─────────────────────────────────────

    /// <summary>
    /// 同一个逻辑帧号驱动两次必须得到**完全一样**的姿势。
    /// 这是"动画只被逻辑帧驱动"的直接证据：如果姿势是 delta 累加出来的，
    /// 中间多跑几帧就会漂，这条断言当场失败。
    /// </summary>
    private void CheckFrameDrivenDoesNotDrift()
    {
        Node3D template = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        Skeleton3D skeleton = FindSkeleton(template)!;
        var animator = new HumanoidAnimator(template);

        const int total = 44;
        const int probe = 20;

        animator.PlayAttack(2, total);

        for (int f = 0; f <= probe; f++)
            animator.AnimateCombat(1f / 60f, f);

        float[] first = Snapshot(skeleton);

        // 乱跑一段（模拟掉帧 / 顿帧 / 中间插入别的东西），再回到同一个帧号
        for (int f = probe + 1; f <= total; f++)
            animator.AnimateCombat(1f / 60f, f);

        animator.AnimateCombat(1f / 60f, probe);
        float[] second = Snapshot(skeleton);

        float drift = MaxBoneAngleDegrees(first, second);

        // 阈值 0.5°：实测残差约 0.07°（≈0.001 rad），是四元数写入/读回的浮点往返噪声。
        // 对照：如果姿势是 delta 累加出来的，这里会差**几十度**——所以这个阈值足够把漂移抓出来，
        // 同时不会被浮点噪声误报。
        Check(drift < 0.5f,
            $"同一个逻辑帧（第 {probe} 帧）驱动两次，姿势差了 {drift:0.###}°——" +
            "说明姿势不是由帧号算出来的（04 §12 要求逻辑帧是权威）");

        GD.Print($"[动画]   帧驱动无漂移：同帧两次驱动差 {drift:0.####}°（ANIM SYNC 结构上为 0 ✓）");
    }

    // ── 工具 ───────────────────────────────────────────────────

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

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[动画] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[动画] ✓ 通过（三段不同刀路 / 格挡有起手 / 弹开与格挡可分 / 帧驱动不漂）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
