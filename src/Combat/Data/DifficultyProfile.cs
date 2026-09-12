using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 一档难度。四档就是 data/difficulty/ 下的 4 个 .tres：
/// asura（修罗）/ samurai（武士，默认）/ kenshi（剑客）/ migoto（見習）。
/// </summary>
[GlobalClass]
public partial class DifficultyProfile : Resource
{
    [Export] public string Id { get; set; } = "samurai";
    [Export] public string DisplayName { get; set; } = "武士";

    [ExportGroup("玩家判定窗")]

    /// <summary>进入防御后多少帧内被命中算弹开。</summary>
    [Export] public int DeflectWindowFrames { get; set; } = 9;

    /// <summary>从其他动作取消进入防御后的硬直帧数，此期间无法弹开（但**仍然能格挡**）。</summary>
    [Export] public int GuardCancelLockFrames { get; set; } = 4;

    /// <summary>真一闪：敌人距离命中还有几帧以内按攻击算一闪。</summary>
    [Export] public int IssenWindowFrames { get; set; } = 6;

    /// <summary>一闪安全窗：按早了不算一闪，但自动转格挡姿态，**不挨打**。</summary>
    [Export] public int IssenSafeWindowFrames { get; set; } = 6;

    [Export] public int InputBufferFrames { get; set; } = 8;
    [Export] public int DodgeBufferFrames { get; set; } = 10;
    [Export] public int DodgeIFrames { get; set; } = 8;
    [Export] public int DodgeRecoveryFrames { get; set; } = 18;

    /// <summary>完美闪避宽容：无敌帧结束后多少帧内仍算完美闪避。</summary>
    [Export] public int PerfectDodgeGraceFrames { get; set; } = 3;

    /// <summary>离开地面后的宽容帧数。</summary>
    [Export] public int CoyoteFrames { get; set; } = 4;

    [ExportGroup("敌人调整")]
    [Export] public int EnemyStartupBonus { get; set; }
    [Export] public float EnemyHealthScale { get; set; } = 1.0f;
    [Export] public float EnemyAttackIntervalScale { get; set; } = 1.0f;
    [Export] public int EnemyReactionDelayBonus { get; set; }

    /// <summary>同时进攻的敌人上限。防止被围殴到无法操作。</summary>
    [Export] public int GankMaxAttackers { get; set; } = 2;

    [ExportGroup("玩家调整")]
    [Export] public float PlayerPostureRegenScale { get; set; } = 1.0f;
    [Export] public float PlayerDamageTakenScale { get; set; } = 1.0f;
    [Export] public int ReviveCount { get; set; } = 1;

    [ExportGroup("辅助（可及性）")]
    [Export] public bool ShowPerilousCue { get; set; } = true;
    [Export] public bool HalfAutoGuard { get; set; }
    [Export] public bool ShowParryRhythm { get; set; }
}
