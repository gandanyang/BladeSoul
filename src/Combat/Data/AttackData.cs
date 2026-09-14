using Godot;
using Oniblade.Audio;

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

    /// <summary>
    /// 可被弹开。**「危」攻击必须为 false**（02 §3 裁定），自检会强制检查。
    /// </summary>
    [Export] public bool Parryable { get; set; } = true;

    /// <summary>
    /// 可被一闪。**一闪可以应对一切攻击**，所以敌方招式默认都是 true；
    /// 这条只留给"确实要让某个大招免疫一闪"的例外，且要写进 02 文档。
    /// </summary>
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

    [ExportGroup("音效")]

    /// <summary>
    /// 刀风用轻的还是重的。**由数据决定**，不按伤害在代码里猜——
    /// 突刺没挥砍声、大上段该用重风，这类判断只有招式表知道。
    /// </summary>
    [Export] public WhooshKind Whoosh { get; set; } = WhooshKind.Light;

    /// <summary>刀风音量（dB）。负值变小。</summary>
    [Export] public float WhooshVolumeDb { get; set; }

    /// <summary>
    /// 刀风音高倍率。同一套连段里三段用不同的音高，耳朵才听得出"这是第二刀"。
    /// </summary>
    [Export] public float WhooshPitchScale { get; set; } = 1f;

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
