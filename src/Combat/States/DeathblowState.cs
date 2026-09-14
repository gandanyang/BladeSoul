using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 被处决（T52）：敌人被破韧后，玩家按下交互键打出处决演出的那一方。
///
/// 它由 <c>DeathblowExecuteState</c> 通过 `ForceChange` 强制切入
/// ——**必须强制**，因为此时敌人正处在 <see cref="PostureBrokenState"/> 里，
/// 而那个状态是"待处决"，它自己不会主动换到"被处决"。
///
/// 这个状态里敌人：
///   · **完全不动**（位移必须是 0，否则演出会看到目标滑走）
///   · **不能攻击**（同 <see cref="PostureBrokenState"/> 的理由）
///   · 播"被处决"姿势（由 <c>AshigaruAnimator</c> 的 <c>BeingExecuted</c> 负责）
///
/// 持续时间由处决演出决定（`DeathblowProfile.TotalFrames`），
/// 伤害由演出那侧在 `HitFrame` 落地——本状态**不管伤害**，只管"被制住"。
/// </summary>
public sealed class DeathblowState : ActorState
{
    /// <summary>被制住多少帧。由发起处决的一方写入。</summary>
    public int DurationFrames { get; set; } = 90;

    /// <summary>是否仍在被处决中（探针断言用）。</summary>
    public bool IsBeingExecuted => true;

    public override int TotalFrames => DurationFrames;

    public override void Enter()
    {
        base.Enter();
        Actor.DesiredVelocity = Vector3.Zero;
        Actor.PrimaryHitbox?.SetActive(false);
    }

    public override void Exit()
    {
        Actor.PrimaryHitbox?.SetActive(false);
    }

    public override void Tick()
    {
        base.Tick();

        // 被钉住：整个演出期间一动不动
        Actor.DesiredVelocity = Vector3.Zero;

        if (Frame >= DurationFrames)
            ChangeState<IdleState>();
    }

    /// <summary>
    /// 被处决期间不接受任何主动行为。
    /// 注意 <c>IsDead</c> 之后 <c>CombatActor</c> 会走自己的死亡流程，
    /// 所以这里不需要给"死亡"开口子。
    /// </summary>
    public override bool CanTransitionTo(ActorState next) => next switch
    {
        AttackState => false,
        ChargedAttackState => false,
        MoveState => false,
        IdleState => false,     // 只有超时才会主动回 Idle（走 ChangeState，不经过这里）
        _ => true,
    };
}
