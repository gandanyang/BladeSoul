using Oniblade.Combat.Data;

namespace Oniblade.Combat;

/// <summary>
/// 战斗单位对外暴露的只读状态，**只给调试面板和战斗统计用**。
///
/// 这么设计是为了让调试面板（UI 层）不必知道 <c>CombatActor</c> 的具体实现，
/// 从而可以和战斗管线并行开发。任何战斗逻辑都不许走这个接口。
/// </summary>
public interface ICombatActorDebug
{
    int ActorId { get; }

    /// <summary>显示用名字，如 "Player" / "木桩" / "魔骸足轻"。</summary>
    string DebugName { get; }

    string StateName { get; }
    int StateFrame { get; }
    int StateTotalFrames { get; }

    int Health { get; }
    int MaxHealth { get; }
    int Posture { get; }
    int MaxPosture { get; }

    bool IsGuarding { get; }
    bool IsInvulnerable { get; }

    /// <summary>弹开窗还剩几帧；0 表示窗口已关闭。</summary>
    int DeflectWindowFramesLeft { get; }

    IssenKind IssenBuff { get; }
    int IssenBuffFramesLeft { get; }

    /// <summary>顿帧剩余。</summary>
    int HitStopFramesLeft { get; }

    /// <summary>当前招式 Id，空闲时为空串。</summary>
    string CurrentAttackId { get; }

    /// <summary>当前弹开连击数。</summary>
    int DeflectChain { get; }

    /// <summary>
    /// 喝血剩余次数（T31 追加，HUD 的"血瓶圆点"用它）。
    /// **只加不改**：只有玩家有这个概念，其它单位恒为 0。
    /// </summary>
    int HealChargesLeft { get; }

    /// <summary>
    /// 输入缓冲的人类可读快照，例如 <c>"dodge(2), attack(0)"</c>；没有缓冲内容时为空串。
    /// 调试面板的 WINDOWS 区块用它。
    /// </summary>
    string InputBufferDebug { get; }
}
