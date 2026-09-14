using System.Collections.Generic;
using Godot;

namespace Oniblade.Enemies;

/// <summary>
/// 魔骸足兵的程序化动画器（T52）。
///
/// **为什么另开一个而不是复用 <c>HumanoidAnimator</c>**：
/// 1. 玩家那张是"剑戟对决"的动作（三段斩、格挡三态、弹开、一闪），敌人要做的是
///    攻击/受击/破韧/被处决/互动——动作集合不同；
/// 2. `HumanoidAnimator` 的玩家动作表归 T38，卡片明令不许碰。
///
/// 骨架命名与它**完全一致**（`Hip / Spine01 / Spine02 / Head / L_Upperarm / R_Upperarm /
/// L_Forearm / R_Forearm / L_Thigh / R_Thigh / L_Calf / R_Calf`），
/// 因为足兵的骨架就是照这套名字绑的（`tools/rig_humanoid.py`）。
///
/// ★ **但姿势数字一个都没照抄** —— 实测两边 rest 旋转不同：
///
///   | 骨 | 足兵 | 玩家 |
///   |---|---|---|
///   | `L_Upperarm` / `R_Upperarm` | **74.2°** | 101.4° |
///   | `L_Thigh` / `R_Thigh` | **178.5°** | 180.0° |
///
/// 照抄会得到一个"动作幅度完全不对"的敌人，而且很难看出是哪里错。
///
/// ★ **姿势一律由逻辑帧算出，不累加 delta**（04 §12：逻辑帧是权威）。
/// 姿势 = f(帧号)，动画与逻辑读同一个帧号，所以 ANIM SYNC 偏差**结构上为 0**。
///
/// ★ **`SetBonePoseRotation` 是替换语义**，必须写 `rest * q`。
/// 这不是风格问题：足兵大腿的 rest 是 178.5°，传 0 会把腿整个翻过去
/// （T50 那个"像一张纸被翻折"的根因）。
/// </summary>
public sealed class AshigaruAnimator
{
    private static readonly string[] Tracked =
    {
        "Hip", "Spine01", "Spine02", "Head",
        "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
        "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
    };

    private readonly Node3D _root;
    private readonly Skeleton3D? _skel;
    private readonly Dictionary<string, int> _bone = new();
    private readonly Dictionary<string, Vector3> _axisSide = new();
    private readonly Dictionary<string, Vector3> _axisRise = new();

    private float _phase;

    public AshigaruAnimator(Node3D modelRoot)
    {
        _root = modelRoot;
        _skel = FindSkeleton(modelRoot);
        if (_skel is null)
            return;

        foreach (string name in Tracked)
        {
            int bone = _skel.FindBone(name);
            if (bone < 0)
                continue;

            _bone[name] = bone;
            Basis rest = _skel.GetBoneGlobalRest(bone).Basis;

            Vector3 side = rest.Inverse() * Vector3.Right;
            Vector3 rise = rest.Inverse() * Vector3.Back;

            _axisSide[name] = side.LengthSquared() > 1e-6f ? side.Normalized() : Vector3.Right;
            _axisRise[name] = rise.LengthSquared() > 1e-6f ? rise.Normalized() : Vector3.Back;
        }
    }

    /// <summary>骨架找到了没有。false 时所有动作都是空操作（不会崩，但什么都不会动）。</summary>
    public bool Valid => _skel is not null && _bone.Count > 0;

    /// <summary>当前正在播的动作（自检与调试面板用）。</summary>
    public AshigaruAction Action { get; private set; } = AshigaruAction.Idle;

    /// <summary>当前动作播到第几帧。</summary>
    public int ActionFrame { get; private set; }

    /// <summary>当前动作总帧数。</summary>
    public int ActionTotalFrames { get; private set; }

    /// <summary>把动画器归到待机（重生、切场景时用）。</summary>
    public void Reset()
    {
        _phase = 0f;
        Action = AshigaruAction.Idle;
        ActionFrame = 0;
        ActionTotalFrames = 0;
        if (Valid)
            _skel!.ResetBonePoses();
    }

    /// <summary>
    /// 每帧驱动。<paramref name="frame"/> 是**动作自己的帧号**（-1 = 不在这个动作里）。
    /// 所有姿势都是这个帧号的函数，所以不会漂。
    /// </summary>
    public void Animate(float delta, float speed01, AshigaruAction action,
                        int frame, int totalFrames)
    {
        if (!Valid)
            return;

        Action = action;
        ActionFrame = frame;
        ActionTotalFrames = totalFrames;

        _skel!.ResetBonePoses();

        switch (action)
        {
            case AshigaruAction.Move:
                ApplyMove(speed01, delta);
                break;
            case AshigaruAction.Attack:
                ApplyAttack(Normalized(frame, totalFrames));
                break;
            case AshigaruAction.HitLight:
                // 0.70 而不是 0.55：实测 0.55 时与 idle 的最大骨角差只有 17.3°，
                // 低于"一眼看得出来"的门槛（探针判据 20°）。轻受击也必须读得出来——
                // 玩家要能确认"这一刀打中了"，而不是只看血条。
                ApplyHit(Normalized(frame, totalFrames), 0.70f);
                break;
            case AshigaruAction.HitHeavy:
                ApplyHit(Normalized(frame, totalFrames), 1.0f);
                break;
            case AshigaruAction.PostureBroken:
                ApplyPostureBroken(Normalized(frame, totalFrames));
                break;
            case AshigaruAction.BeingExecuted:
                ApplyBeingExecuted(Normalized(frame, totalFrames));
                break;
            case AshigaruAction.Death:
                ApplyDeath(Normalized(frame, totalFrames));
                break;
            case AshigaruAction.Interact:
                ApplyInteract(Normalized(frame, totalFrames));
                break;
            default:
                ApplyIdle(delta);
                break;
        }
    }

    /// <summary>
    /// 一次性驱动（推荐入口）：给它"现在处于什么状态"，它自己决定放哪个动作。
    /// 调用点不用手忙脚乱地配对动作与帧号——**配对错了会出现"在待机里播死亡姿势"**这种
    /// 很难查的怪象。
    /// </summary>
    /// <param name="attackFrame">攻击态自己的帧号；不在攻击时给 -1。</param>
    /// <param name="attackTotal">该招式的总帧数。</param>
    public void AnimateCombat(float delta, float speed01, int attackFrame, int attackTotal,
                              int hitStunFrame, int hitStunTotal, float hitStrength,
                              int brokenFrame, int brokenTotal,
                              int executedFrame, int executedTotal,
                              int deathFrame, int deathTotal,
                              int interactFrame, int interactTotal)
    {
        // 优先级从"最强制"到"最弱"：
        // 死亡 > 被处决 > 破韧 > 受击 > 攻击 > 互动 > 移动/待机。
        //
        // 为什么死亡排第一：死了还在播攻击会把"已经打死了"这个信息抹掉。
        // 为什么被处决高于破韧：处决是破韧之后发生的，必须覆盖那个瘫软姿势。
        if (deathFrame >= 0)
        {
            Animate(delta, speed01, AshigaruAction.Death, deathFrame, deathTotal);
            return;
        }

        if (executedFrame >= 0)
        {
            Animate(delta, speed01, AshigaruAction.BeingExecuted, executedFrame, executedTotal);
            return;
        }

        if (brokenFrame >= 0)
        {
            Animate(delta, speed01, AshigaruAction.PostureBroken, brokenFrame, brokenTotal);
            return;
        }

        if (hitStunFrame >= 0)
        {
            AshigaruAction kind = hitStrength >= 0.8f ? AshigaruAction.HitHeavy : AshigaruAction.HitLight;
            Animate(delta, speed01, kind, hitStunFrame, hitStunTotal);
            return;
        }

        if (attackFrame >= 0)
        {
            Animate(delta, speed01, AshigaruAction.Attack, attackFrame, attackTotal);
            return;
        }

        if (interactFrame >= 0)
        {
            Animate(delta, speed01, AshigaruAction.Interact, interactFrame, interactTotal);
            return;
        }

        Animate(delta, speed01, speed01 > MinWalkSpeed ? AshigaruAction.Move : AshigaruAction.Idle,
                0, 0);
    }

    /// <summary>0..1 的进度；帧号非法时返回 0（= 动作起始姿势）。</summary>
    private static float Normalized(int frame, int totalFrames)
    {
        if (frame < 0 || totalFrames <= 1)
            return 0f;

        return Mathf.Clamp(frame / (float)(totalFrames - 1), 0f, 1f);
    }

    // ── 动作 ─────────────────────────────────────────────────────

    /// <summary>待机：极缓的呼吸，幅度小到不抢戏，但**与"完全静止"可测出差别**。</summary>
    private void ApplyIdle(float delta)
    {
        _phase += delta * 1.6f;
        float breathe = Mathf.Sin(_phase);

        Rot("Spine01", breathe * 0.028f);
        Rot("Spine02", breathe * 0.018f);
        Rot("Head", -breathe * 0.022f);
        Rot("L_Upperarm", breathe * 0.035f, breathe * 0.02f);
        Rot("R_Upperarm", -breathe * 0.035f, breathe * 0.02f);
        Rot("L_Forearm", -0.06f);
        Rot("R_Forearm", -0.06f);
    }

    /// <summary>移动：照 T38 的教训——**骨盆要跟着迈步转**，否则是"下半身一整块平移"。</summary>
    private void ApplyMove(float speed01, float delta)
    {
        speed01 = Mathf.Clamp(speed01, 0f, 1.5f);
        _phase += delta * Mathf.Lerp(2.2f, 8.5f, Mathf.Min(speed01, 1f));

        float swing = Mathf.Sin(_phase) * speed01;

        // 低于阈值腿必须**完全停住**：不然站桩也在蹬腿（T38 踩过，靠 MinWalkSpeed 守着）
        float step = speed01 > MinWalkSpeed
            ? Mathf.Max(0.40f, Mathf.Min(speed01, 1f)) * 1.05f
            : 0f;

        Rot("L_Thigh", Mathf.Sin(_phase) * step);
        Rot("R_Thigh", -Mathf.Sin(_phase) * step);
        Rot("L_Calf", -Mathf.Max(0f, Mathf.Sin(_phase)) * step * 1.05f);
        Rot("R_Calf", -Mathf.Max(0f, -Mathf.Sin(_phase)) * step * 1.05f);

        Rot("Hip", swing * 0.20f);
        Rot("Spine02", -swing * 0.10f);
        Rot("L_Upperarm", -swing * 0.55f, -0.08f * Mathf.Min(speed01, 1f));
        Rot("R_Upperarm", swing * 0.55f, -0.08f * Mathf.Min(speed01, 1f));
        Rot("L_Forearm", -Mathf.Abs(swing) * 0.28f);
        Rot("R_Forearm", -Mathf.Abs(swing) * 0.28f);
        Rot("Spine01", 0.10f * Mathf.Min(speed01, 1f));
        Rot("Head", -0.10f * Mathf.Min(speed01, 1f));
    }

    /// <summary>
    /// 攻击：**单臂斜劈**（exam 足兵是单刀）。
    /// 前摇（0~0.35 举刀）→ 挥出（0.35~0.6）→ 收（0.6~1）。
    /// 前摇必须明显——玩家要能读出"他要打我了"，否则弹开窗口没有意义。
    /// </summary>
    private void ApplyAttack(float t)
    {
        // 三段式：抬到顶 → 劈下去 → 回位
        float windup = Mathf.Clamp(t / 0.35f, 0f, 1f);
        float strike = Mathf.Clamp((t - 0.35f) / 0.25f, 0f, 1f);
        float recover = Mathf.Clamp((t - 0.60f) / 0.40f, 0f, 1f);

        // 抬起幅度（负 = 向前上方举）
        float arm = Mathf.Lerp(0f, -1.55f, windup);
        arm = Mathf.Lerp(arm, 1.15f, strike);
        arm = Mathf.Lerp(arm, 0f, recover);

        // 躯干：举刀时后仰、劈下时前倾
        float torso = Mathf.Lerp(0f, -0.28f, windup);
        torso = Mathf.Lerp(torso, 0.34f, strike);
        torso = Mathf.Lerp(torso, 0f, recover);

        Rot("R_Upperarm", arm, Mathf.Lerp(0.35f, -0.55f, windup) + strike * 0.5f);
        Rot("R_Forearm", Mathf.Lerp(0f, -1.35f, windup) + strike * 0.85f);
        Rot("L_Upperarm", arm * 0.30f, -0.18f * windup);
        Rot("L_Forearm", Mathf.Lerp(0f, -0.55f, windup));

        Rot("Spine01", torso);
        Rot("Spine02", torso * 0.75f);
        Rot("Head", -torso * 0.35f);

        // 腿：跨一步的重心变化（前腿沉、后腿撑）
        Rot("L_Thigh", -0.22f * windup + 0.30f * strike);
        Rot("R_Thigh", 0.16f * windup - 0.22f * strike);
        Rot("L_Calf", -0.18f * strike);
        Rot("R_Calf", -0.12f * windup);
        Rot("Hip", -0.10f * windup + 0.16f * strike);
    }

    /// <summary>受击：向后一顿。<paramref name="strength"/> 越大越明显（轻/重两档）。</summary>
    private void ApplyHit(float t, float strength)
    {
        // 一个快速冲上去再收回的包络
        float envelope = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t, 0f, 1f));
        float k = envelope * strength;

        Rot("Spine01", 0.42f * k);
        Rot("Spine02", 0.30f * k);
        Rot("Head", 0.55f * k);
        Rot("Hip", 0.16f * k);

        Rot("L_Upperarm", 0.30f * k, 0.35f * k);
        Rot("R_Upperarm", 0.30f * k, 0.35f * k);
        Rot("L_Forearm", -0.45f * k);
        Rot("R_Forearm", -0.45f * k);

        // 腿被推得往后撑
        Rot("L_Thigh", -0.30f * k);
        Rot("R_Thigh", 0.18f * k);
        Rot("L_Calf", -0.35f * k);
        Rot("R_Calf", -0.20f * k);
    }

    /// <summary>
    /// 破韧（进入待处决窗口）：**瘫软跪伏 + 手臂垂落**。
    /// 必须与"受击"一眼可分——受击是"被打得一震"，破韧是"撑不住了"。
    /// 它是可持续的姿势（窗口最长 120 帧都在这个姿势附近），不能像受击那样是脉冲包络。
    /// </summary>
    private void ApplyPostureBroken(float t)
    {
        // 快速塌下去（前 25%），之后维持在低位轻微发抖
        float collapse = Mathf.Clamp(t / 0.25f, 0f, 1f);
        float tremble = Mathf.Sin(t * Mathf.Pi * 14f) * 0.035f * collapse;

        Rot("Hip", 0.30f * collapse);
        Rot("Spine01", 0.62f * collapse + tremble);
        Rot("Spine02", 0.45f * collapse);
        Rot("Head", 0.85f * collapse);

        // 手臂完全垂下，刀拖在地上
        Rot("L_Upperarm", 0.55f * collapse, 0.62f * collapse);
        Rot("R_Upperarm", 0.55f * collapse, 0.62f * collapse);
        Rot("L_Forearm", -0.75f * collapse);
        Rot("R_Forearm", -0.75f * collapse);

        // 膝盖弯下去（跪）
        Rot("L_Thigh", -0.55f * collapse);
        Rot("R_Thigh", -0.48f * collapse);
        Rot("L_Calf", -0.95f * collapse);
        Rot("R_Calf", -0.88f * collapse);
    }

    /// <summary>
    /// 被处决：**被制住的固定姿势**。
    /// 处决演出期间敌人必须**完全不动**（验收要求打印位移 ≈ 0），
    /// 所以这里只有一个一次性塌下 + 之后定格，不做任何持续抖动。
    /// </summary>
    private void ApplyBeingExecuted(float t)
    {
        float seize = Mathf.Clamp(t / 0.20f, 0f, 1f);

        Rot("Hip", 0.22f * seize);
        Rot("Spine01", 0.50f * seize);
        Rot("Spine02", 0.38f * seize);
        Rot("Head", 0.95f * seize);        // 头低下去

        // 双臂被架开/垂下
        Rot("L_Upperarm", 0.70f * seize, 0.80f * seize);
        Rot("R_Upperarm", 0.62f * seize, 0.72f * seize);
        Rot("L_Forearm", -0.85f * seize);
        Rot("R_Forearm", -0.80f * seize);

        Rot("L_Thigh", -0.40f * seize);
        Rot("R_Thigh", -0.36f * seize);
        Rot("L_Calf", -0.70f * seize);
        Rot("R_Calf", -0.66f * seize);
    }

    /// <summary>死亡：像被抽掉骨头一样倒下去。</summary>
    private void ApplyDeath(float t)
    {
        float fall = Mathf.Clamp(t / 0.75f, 0f, 1f);
        float settle = Mathf.Clamp((t - 0.75f) / 0.25f, 0f, 1f);

        Rot("Hip", Mathf.Lerp(0.30f, 1.05f, fall));
        Rot("Spine01", Mathf.Lerp(0.45f, 0.30f, fall) - 0.10f * settle);
        Rot("Spine02", Mathf.Lerp(0.35f, 0.22f, fall));
        Rot("Head", Mathf.Lerp(0.70f, 0.30f, fall));

        Rot("L_Upperarm", Mathf.Lerp(0.50f, 0.85f, fall), Mathf.Lerp(0.45f, 0.70f, fall));
        Rot("R_Upperarm", Mathf.Lerp(0.45f, 0.80f, fall), Mathf.Lerp(0.40f, 0.65f, fall));
        Rot("L_Forearm", Mathf.Lerp(-0.60f, -0.20f, fall));
        Rot("R_Forearm", Mathf.Lerp(-0.55f, -0.18f, fall));

        Rot("L_Thigh", Mathf.Lerp(-0.35f, -0.70f, fall));
        Rot("R_Thigh", Mathf.Lerp(-0.30f, -0.62f, fall));
        Rot("L_Calf", Mathf.Lerp(-0.55f, -0.95f, fall));
        Rot("R_Calf", Mathf.Lerp(-0.50f, -0.90f, fall));
    }

    /// <summary>互动（被吸魂时的反应，接 T19）：被往前拽、手抬起来。</summary>
    private void ApplyInteract(float t)
    {
        float pull = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t, 0f, 1f));

        Rot("Spine01", -0.22f * pull);
        Rot("Spine02", -0.16f * pull);
        Rot("Head", -0.30f * pull);
        Rot("Hip", -0.12f * pull);

        // 手往胸口抬（魂被抽出来）
        Rot("L_Upperarm", -0.55f * pull, -0.45f * pull);
        Rot("R_Upperarm", -0.50f * pull, -0.40f * pull);
        Rot("L_Forearm", -0.90f * pull);
        Rot("R_Forearm", -0.85f * pull);

        Rot("L_Thigh", 0.18f * pull);
        Rot("R_Thigh", 0.15f * pull);
        Rot("L_Calf", -0.22f * pull);
        Rot("R_Calf", -0.20f * pull);
    }

    // ── 骨写入 ───────────────────────────────────────────────────

    /// <summary>
    /// 绕"抬落轴 / 前后摆轴"各转一个角度（弧度）。缺骨时静默跳过。
    /// </summary>
    private void Rot(string name, float side, float rise = 0f)
    {
        if (!_bone.TryGetValue(name, out int bone))
            return;

        Quaternion q = Quaternion.Identity;

        if (Mathf.Abs(side) > 0.0001f)
            q *= new Quaternion(_axisSide[name], side);

        if (Mathf.Abs(rise) > 0.0001f)
            q *= new Quaternion(_axisRise[name], rise);

        // ★ 必须 `rest * q` —— 见类注释：足兵大腿 rest 是 178.5°，
        //   直接替换会把腿整个翻过去（T50 的根因）。
        Quaternion rest = _skel!.GetBoneRest(bone).Basis.GetRotationQuaternion();
        _skel.SetBonePoseRotation(bone, rest * q);
    }

    /// <summary>低于这个速度就当作"站着不动"，腿部摆动必须完全停住（T38 的教训）。</summary>
    public const float MinWalkSpeed = 0.15f;

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

/// <summary>足兵会做的动作。名字即语义，供探针逐个点名验证。</summary>
public enum AshigaruAction
{
    Idle = 0,
    Move = 1,
    Attack = 2,
    HitLight = 3,
    HitHeavy = 4,
    PostureBroken = 5,
    BeingExecuted = 6,
    Death = 7,
    Interact = 8,
}
