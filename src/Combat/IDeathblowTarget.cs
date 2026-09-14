using Godot;

namespace Oniblade.Combat;

/// <summary>
/// "可以被处决的目标"（T52）。
///
/// 为什么要这个接口：处决是**玩家发起、敌人承受**的跨层动作。
/// 如果 <c>PlayerActor</c> 直接写 `if (target is Ashigaru ashigaru)`，
/// 那么玩家层就依赖了敌人层——将来做第二种敌人（武士、BOSS）时，
/// 要么复制这段判断，要么把玩家层改成认识所有敌人类型。
///
/// 有了接口，玩家只问三件事：**能不能处决、在哪、开始被处决**。
/// </summary>
public interface IDeathblowTarget
{
    /// <summary>此刻能被处决吗（= 处于破韧态且窗口还开着）。</summary>
    bool CanBeExecuted { get; }

    /// <summary>世界坐标（用于距离判定）。</summary>
    Vector3 GlobalPosition { get; }

    /// <summary>
    /// 进入"被处决"状态。<paramref name="durationFrames"/> 由处决演出决定，
    /// 敌人据此把自己钉住这么久。
    /// </summary>
    void BeginBeingExecuted(int durationFrames);
}
