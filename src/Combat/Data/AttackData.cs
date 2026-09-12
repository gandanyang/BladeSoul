using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 一个招式的全部数值。**这是唯一允许写帧数据的地方**——
/// 调平衡时改的是 data/attacks/**/*.tres，不是 C# 代码，所以不用重新编译。
/// </summary>
[GlobalClass]
public partial class AttackData : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public DamageType Type { get; set; } = DamageType.Slash;

    [ExportGroup("帧数据 (60fps)")]
    [Export] public int StartupFrames { get; set; } = 8;
    [Export] public int ActiveFrames { get; set; } = 4;
    [Export] public int RecoveryFrames { get; set; } = 14;

    /// <summary>
    /// 进入后摇后，第几帧起可以被下一段动作取消。
    /// 注意这里量的是**后摇内的偏移**，不是整招的绝对帧号：
    /// 轻斩壹 后摇 14 帧、偏移 6 → 从整招第 8+4+6 = 18 帧起可以取消。
    /// 用 -1 表示整招不可取消（蓄力斩、一闪）。
    /// </summary>
    [Export] public int CancelFromRecoveryFrame { get; set; } = 6;

    [ExportGroup("数值")]
    [Export] public int Damage { get; set; } = 10;
    [Export] public int PostureDamage { get; set; } = 8;
    [Export] public float Knockback { get; set; } = 0.4f;

    /// <summary>出招时角色向前推进的距离（米）。后摇越长越需要它，否则攻击就是纯惩罚。</summary>
    [Export] public float AdvanceDistance { get; set; } = 0.8f;

    [ExportGroup("属性开关")]

    /// <summary>「危」：格挡与弹开都无效，只能闪避/看破。</summary>
    [Export] public bool Unblockable { get; set; }

    /// <summary>可被弹开。</summary>
    [Export] public bool Parryable { get; set; } = true;

    /// <summary>可被一闪。</summary>
    [Export] public bool IssenVulnerable { get; set; } = true;

    /// <summary>突刺：可被看破（识破）。</summary>
    [Export] public bool Thrust { get; set; }

    /// <summary>显示「危」预警。</summary>
    [Export] public bool Perilous { get; set; }

    [Export] public PerilousKind PerilousKind { get; set; } = PerilousKind.None;

    [ExportGroup("资源")]
    [Export] public Shape3D? HitShape { get; set; }
    [Export] public string AnimName { get; set; } = "";
    [Export] public PackedScene? HitFx { get; set; }

    // ── 派生属性：统一转成纯结构再算，保证与单测用的是同一套逻辑 ──
    public AttackTiming ToTiming() => new()
    {
        StartupFrames = StartupFrames,
        ActiveFrames = ActiveFrames,
        RecoveryFrames = RecoveryFrames,
        CancelFromRecoveryFrame = CancelFromRecoveryFrame,
    };

    public int TotalFrames => ToTiming().TotalFrames;
    public int ActiveStart => ToTiming().ActiveStart;
    public int ActiveEnd => ToTiming().ActiveEnd;
    public int RecoveryStart => ToTiming().RecoveryStart;
    public bool Cancelable => ToTiming().Cancelable;
    public int CancelOpenFrame => ToTiming().CancelOpenFrame;

    public bool IsActiveAt(int frame) => ToTiming().IsActiveAt(frame);
    public bool IsInRecoveryAt(int frame) => ToTiming().IsInRecoveryAt(frame);
    public bool CanCancelAt(int frame) => ToTiming().CanCancelAt(frame);

    /// <summary>转换为裁决器能吃的纯结构（不带 Godot 类型）。</summary>
    public AttackTraits ToTraits() => new()
    {
        Damage = Damage,
        PostureDamage = PostureDamage,
        Unblockable = Unblockable,
        Parryable = Parryable,
    };
}
