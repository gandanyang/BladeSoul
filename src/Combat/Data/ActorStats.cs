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

    [ExportGroup("档次")]
    [Export] public EnemyTier Tier { get; set; } = EnemyTier.Grunt;
}
