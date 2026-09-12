using Oniblade.Combat.Data;

namespace Oniblade.Combat;

/// <summary>
/// 裁决器需要的招式属性，刻意做成**不含任何 Godot 类型**的纯结构，
/// 这样裁决器与它的单元测试都不需要引擎。
/// <see cref="Data.AttackData"/> 通过 ToTraits() 转换成它。
/// </summary>
public readonly struct AttackTraits
{
    public int Damage { get; init; }
    public int PostureDamage { get; init; }

    /// <summary>「危」：格挡与弹开都无效，只能闪避/看破。</summary>
    public bool Unblockable { get; init; }

    /// <summary>
    /// 可被弹开。**只对一般攻击为真**——
    /// 「危」攻击一律不可弹开（02 §3 裁定：一闪应对一切，弹开只应对一般攻击）。
    /// </summary>
    public bool Parryable { get; init; }

    /// <summary>普通斩击的默认属性：可格挡、可弹开。</summary>
    public static AttackTraits Neutral => new() { Parryable = true };

    /// <summary>「危·横扫 / 危·抓取」：不可格挡、不可弹开。</summary>
    public static AttackTraits Perilous => new() { Unblockable = true, Parryable = false };
}

/// <summary>攻方在**本帧**的状态快照。</summary>
public readonly struct AttackerSnapshot
{
    public int ActorId { get; init; }
    public AttackTraits Traits { get; init; }

    /// <summary>本帧处于判定帧（刀身真的在扫人）。</summary>
    public bool IsActive { get; init; }

    /// <summary>本帧在出招动作中（含前摇/后摇），用于拼刀判定。</summary>
    public bool IsAttackAction { get; init; }

    /// <summary>本帧处于"可被一闪"窗口。</summary>
    public bool IssenVulnerable { get; init; }
}

/// <summary>防御方在**本帧**的状态快照。</summary>
public readonly struct DefenderSnapshot
{
    public int ActorId { get; init; }

    /// <summary>闪避无敌帧。</summary>
    public bool IsInvulnerable { get; init; }

    /// <summary>防御方本帧**也在判定帧**（刀也在扫）——拼刀判定需要它。</summary>
    public bool IsActive { get; init; }

    /// <summary>处于弹开窗（进入防御后的前 D 帧，D 由难度决定）。</summary>
    public bool InDeflectWindow { get; init; }

    public bool IsGuarding { get; init; }

    /// <summary>攻方相对防御方正面的夹角（度，取绝对值前）。</summary>
    public int GuardAngleDeg { get; init; }

    public int CurrentPosture { get; init; }
    public int MaxPosture { get; init; }

    /// <summary>持有的一闪 buff，None 表示没有。</summary>
    public IssenKind IssenKind { get; init; }
}

/// <summary>裁决结果。数值型收益在这里给出，百分比型收益（一闪）由 IssenTable 决定。</summary>
public readonly struct ResolveResult
{
    public Verdict Verdict { get; init; }
    public int Damage { get; init; }
    public int PostureDamage { get; init; }
    public int HitStopFrames { get; init; }
    public int SlowMoMs { get; init; }

    /// <summary>结算后授予的一闪 buff。</summary>
    public IssenKind GrantIssen { get; init; }
    public int GrantIssenFrames { get; init; }
}
