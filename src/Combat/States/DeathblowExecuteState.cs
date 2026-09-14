using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 玩家的处决演出（T52）。
///
/// # 机制（项目主人口述裁定）
/// · 触发键 = **交互键 F/E**，**普攻键绝不许处决**
/// · 演出期间玩家**全程无敌**
/// · 目标（被破韧的敌人）在整个演出里被钉住不动
///
/// # 无敌为什么在这里给出
/// 不新增一套无敌判定：<c>CombatActor.IsInvulnerableNow</c> 是现成的虚属性，
/// 裁决器规则 1 已经在读它（<c>CombatResolver</c> 里 <c>if (def.IsInvulnerable)</c> →
/// 直接判 <c>Miss</c>，连一闪都打不中）。
/// 所以只要玩家在处决期间把这个属性变 true，**所有敌人**的刀就都打不到他——
/// 这正是验收要的"处决期间另一个敌人来砍，玩家不掉血"。
/// 另造一条裁决分支会把这个已经有单测覆盖的规则撕成两半。
///
/// 帧参数全部来自 <c>data/combat/deathblow.tres</c>，本类不写死数字。
/// </summary>
public sealed class DeathblowExecuteState : ActorState
{
    private readonly DodgeWindow _window = new();

    /// <summary>切入前由输入侧写入：总帧数（<c>DeathblowProfile.TotalFrames</c>）。</summary>
    public int TotalFramesValue { get; set; } = 90;

    /// <summary>切入前由输入侧写入：无敌帧数。</summary>
    public int InvulnerableFrames { get; set; } = 90;

    /// <summary>切入前由输入侧写入：伤害落在第几帧。</summary>
    public int HitFrameValue { get; set; } = 50;

    /// <summary>切入前由输入侧写入：处决伤害。</summary>
    public int Damage { get; set; } = 9999;

    /// <summary>切入前由输入侧写入：被处决的目标。</summary>
    public CombatActor? Target { get; set; }

    /// <summary>伤害已经落地了吗（防止多帧重复结算）。</summary>
    public bool DamageApplied { get; private set; }

    /// <summary>
    /// 这一帧**实际**算出了多少伤害（探针用它证明"只结算了一次"）。
    /// 连按 F/E 时如果状态被反复切入，这个数会翻倍；靠它才抓得到。
    /// </summary>
    public int AppliedDamage { get; private set; }

    /// <summary>本次演出是不是从 Enter 走过来的（区别于"状态对象还在但没进去过"）。</summary>
    public int EnterCount { get; private set; }

    /// <summary>本次处决命中了没有（探针断言用）。</summary>
    public bool Landed => DamageApplied && Target is not null;

    /// <summary>本帧是否无敌。裁决器规则 1 直接读它。</summary>
    public bool IsInvulnerableNow => _window.IsInvulnerable;

    /// <summary>已经过到第几帧。</summary>
    public int FramesSinceStart => _window.FramesSinceStart;

    public override int TotalFrames => _window.TotalFrames;

    public override void Enter()
    {
        base.Enter();

        DamageApplied = false;
        AppliedDamage = 0;
        EnterCount++;
        _window.Begin(InvulnerableFrames, Mathf.Max(0, TotalFramesValue - InvulnerableFrames));

        // 处决是站桩演出：不接受移动意图（否则玩家推着方向键会把演出拖走）
        Actor.DesiredVelocity = Vector3.Zero;
    }

    public override void Tick()
    {
        base.Tick();

        // 全程不许移动。这不只是"看起来对"——目标被钉住、玩家也在原地，
        // 处决的距离关系在整个演出里保持不变，不会出现"演到一半滑开了"。
        Actor.DesiredVelocity = Vector3.Zero;

        int frame = _window.FramesSinceStart;

        // 伤害在 HitFrameValue 那一帧落地。用 `>=` 而不是 `==`：
        // 万一某一帧被顿帧跳过，伤害也不会永远不落（那会变成"处决了但敌人没死"）。
        if (!DamageApplied && frame >= HitFrameValue)
        {
            DamageApplied = true;
            ApplyDamage();
        }

        _window.Advance();

        if (_window.IsFinished)
            ChangeState<IdleState>();
    }

    public override void Exit()
    {
        // 用掉就清掉目标引用（同 GuardState / DodgeState 的"参数即用即复位"纪律）：
        // 忘了清的话，下次处决若没写 Target，就会对着上一个死掉的敌人播演出。
        Target = null;
    }

    private void ApplyDamage()
    {
        if (Target is null || !GodotObject.IsInstanceValid(Target) || Target.IsDead)
            return;

        Target.Health.Apply(Damage);
        AppliedDamage += Damage;
        Target.Die();
    }

    /// <summary>
    /// 处决演出只拒绝**受击硬直**打断。
    ///
    /// 它只持续约 1.5 秒、而且玩家全程无敌，被打断只会让演出看起来像 bug；
    /// 但**不能把其余状态全挡掉**——那样演出播完就回不到 Idle，
    /// 玩家会永久卡在处决态里（症状是"打完之后动不了"）。
    /// </summary>
    public override bool CanTransitionTo(ActorState next) => next is not StaggerState;
}
