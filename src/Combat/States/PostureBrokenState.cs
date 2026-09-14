using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 破韧态（T52）：体干被打满之后进入的**待处决窗口**。
///
/// 机制由项目主人口述裁定（`docs/TASKS.md` T52）：
/// **怪被破韧之后不能主动攻击、只能被攻击，维持一定时间；玩家可以砍几刀然后处决。**
///
/// 与 <see cref="StaggerState"/> 的分工：
///   · `StaggerState`       = 普通受击硬直（十几帧，之后照样能还手）
///   · `PostureBrokenState` = **破韧**（上百帧的长窗口 + 禁止还手 + 亮处决标记）
/// 两者绝不能混用——混了就会出现"打完架势敌人立刻反手一刀"，
/// 那这个系统在玩家眼里就等于不存在。
///
/// 「不能主动攻击」怎么落地：<see cref="CanTransitionTo"/> 拒绝 <see cref="AttackState"/>。
/// `StateMachine.Change&lt;T&gt;()` 对被拒绝的切换是**静默丢弃**的，所以敌人 AI 即使
/// 每帧请求出招也进不去攻击态——这是状态机层面的硬约束，不靠 AI 自觉。
///
/// 窗口的帧数计算在纯逻辑类 <see cref="PostureBrokenWindow"/> 里（可单测）。
/// </summary>
public sealed class PostureBrokenState : ActorState
{
    private readonly PostureBrokenWindow _window = new();

    /// <summary>窗口长度（帧）。由施加者按 `data/**/*.tres` 设进来，不许硬编码。</summary>
    public int DurationFrames { get; set; } = 120;

    /// <summary>被打进破韧的次数，供探针/HUD 断言"窗口真的开过"。</summary>
    public int EnterCount { get; private set; }

    /// <summary>窗口还开着的帧数（0 = 已过期，不再可处决）。</summary>
    public int FramesLeft => _window.FramesLeft;

    /// <summary>此刻能不能处决。</summary>
    public bool IsOpen => _window.IsOpen;

    public override int TotalFrames => DurationFrames;

    public override void Enter()
    {
        base.Enter();
        EnterCount++;
        _window.Begin(DurationFrames);
        Actor.DesiredVelocity = Vector3.Zero;

        // 破韧时手上的攻击必须立刻失效，否则会出现"被破韧的敌人还挥了一刀"。
        Actor.PrimaryHitbox?.SetActive(false);
    }

    public override void Exit()
    {
        _window.Close();
        Actor.PrimaryHitbox?.SetActive(false);
    }

    public override void Tick()
    {
        base.Tick();
        _window.Tick();

        // 破韧态里**完全不动**：它是"待处决"而不是"被打退"，
        // 让敌人滑开反而会让处决的距离判定变得难猜。
        Actor.DesiredVelocity = Vector3.Zero;

        if (Frame >= DurationFrames)
            ChangeState<IdleState>();
    }

    /// <summary>
    /// 破韧态只允许被"更强制"的状态打断：
    /// 处决、死亡、以及再次受击（受击走 <c>ForceChange</c>，不经过这里）。
    /// **攻击一律拒绝** —— 这就是"不能主动攻击"。
    /// </summary>
    public override bool CanTransitionTo(ActorState next) => next switch
    {
        AttackState => false,
        ChargedAttackState => false,
        _ => true,
    };
}
