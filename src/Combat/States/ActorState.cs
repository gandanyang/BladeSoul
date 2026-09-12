namespace Oniblade.Combat.States;

/// <summary>
/// 状态是 **POCO，不是 Node**（04 文档 §9）：
/// 不污染场景树、没有节点生命周期开销、可以被单元测试直接实例化。
/// </summary>
public abstract class ActorState
{
    protected CombatActor Actor { get; private set; } = null!;
    protected StateMachine Machine { get; private set; } = null!;

    /// <summary>已在本状态持续多少帧。</summary>
    public int Frame { get; private set; }

    /// <summary>本状态的总时长（帧）。调试面板显示 `frame/total` 用；未知时返回 0。</summary>
    public virtual int TotalFrames => 0;

    public virtual void Enter() => Frame = 0;

    public virtual void Exit() { }

    public virtual void Tick() => Frame++;

    /// <summary>允许哪些状态打断自己。默认全部允许。</summary>
    public virtual bool CanTransitionTo(ActorState next) => true;

    internal void Attach(CombatActor actor, StateMachine machine)
    {
        Actor = actor;
        Machine = machine;
    }

    protected void SetFrame(int frame) => Frame = frame;

    protected void ChangeState<T>() where T : ActorState => Machine.Change<T>();
}
