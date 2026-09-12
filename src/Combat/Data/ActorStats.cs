using Godot;

namespace Oniblade.Combat.Data;

/// <summary>角色/敌人的基础属性。放在 data/actors/ 与 data/enemies/ 下。</summary>
[GlobalClass]
public partial class ActorStats : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    [ExportGroup("生存")]
    [Export] public int MaxHealth { get; set; } = 100;
    [Export] public int MaxPosture { get; set; } = 100;

    [ExportGroup("体干回复")]
    [Export] public float PostureRegenPerSecond { get; set; } = 22f;
    [Export] public int PostureRegenDelayFrames { get; set; } = 30;

    [ExportGroup("移动")]
    [Export] public float MoveSpeed { get; set; } = 4.2f;
    [Export] public float SprintSpeed { get; set; } = 7.0f;
    [Export] public float AttackMoveSpeed { get; set; } = 1.6f;

    /// <summary>
    /// 格挡姿态下的移动速度倍率（02 文档 §2.2：约 40%）。
    /// 默认 1.0 = 不变；只有会格挡的角色（玩家）在 data/actors/ 里调小它。
    /// </summary>
    [Export] public float GuardMoveScale { get; set; } = 1.0f;

    [Export] public float TurnSpeed { get; set; } = 12f;

    [ExportGroup("喝血")]
    /// <summary>每关可喝次数（02 §2.4：3~5 次，宽松，不是稀有资源）。</summary>
    [Export] public int HealCharges { get; set; } = 4;

    /// <summary>起手段帧数：掏壶。此段**可以被打断**，且被打断则不消耗次数。</summary>
    [Export] public int HealStartupFrames { get; set; } = 10;

    /// <summary>饮用段帧数：进入此段后受击**不再打断动作**，但**伤害照常结算**。</summary>
    [Export] public int HealDrinkFrames { get; set; } = 34;

    /// <summary>收招段帧数：可被闪避/防御取消。</summary>
    [Export] public int HealRecoveryFrames { get; set; } = 10;

    /// <summary>一次喝血回复的最大血量百分比。</summary>
    [Export] public float HealPercent { get; set; } = 0.4f;

    /// <summary>
    /// 死亡到"原地重开完成"的等待帧数（T14）。默认 60 帧 = 1 秒，
    /// 远低于 01 §0 规则 1 的 180 帧上限，留出足够的余量给死亡表现。
    /// </summary>
    [Export] public int DeathRestartDelayFrames { get; set; } = 60;

    [ExportGroup("复活")]
    /// <summary>起身演出的帧数（T22 / 卡片：约 1.5 秒）。</summary>
    [Export] public int RevivePerformanceFrames { get; set; } = 90;

    /// <summary>
    /// 起身演出**结束后**额外给的无敌帧数。
    /// 不给的话"刚站起来就被同一套连招带走"——复活次数会被白白吃掉，玩家只会觉得被耍了。
    /// </summary>
    [Export] public int ReviveInvulnerableFrames { get; set; } = 30;

    [ExportGroup("档次")]
    [Export] public EnemyTier Tier { get; set; } = EnemyTier.Grunt;
}
