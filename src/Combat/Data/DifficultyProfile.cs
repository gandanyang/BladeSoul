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

    /// <summary>
    /// 松开防御后多少帧内**再次按下**，这一段防御**直接不开窗**（仍然格挡，02 §8）。
    ///
    /// **四档难度都设成 8，它不是难度旋钮，是输入语义规则。**
    /// 它必须 ≤ 二连斩的重按间隔（约 9 帧），否则玩家连"接第二刀"都做不到——
    /// 那不是难度，那是坏掉的输入。反连打的真正主力是「危」攻击（02 §3）。
    /// </summary>
    [Export] public int GuardReentryLockFrames { get; set; } = 8;

    /// <summary>真一闪：敌人距离命中还有几帧以内按攻击算一闪。</summary>
    [Export] public int IssenWindowFrames { get; set; } = 6;

    /// <summary>一闪安全窗：按早了不算一闪，但自动转格挡姿态，**不挨打**。</summary>
    [Export] public int IssenSafeWindowFrames { get; set; } = 6;

    [Export] public int InputBufferFrames { get; set; } = 8;
    [Export] public int DodgeBufferFrames { get; set; } = 10;
    [Export] public int DodgeIFrames { get; set; } = 8;
    [Export] public int DodgeRecoveryFrames { get; set; } = 18;

    // T21 删除了一个"完美闪避宽容帧数"参数：02 §8 那条规则物理上不成立
    // （无敌帧之后不再产生 Miss，而完美闪避只能由 Miss 触发）。
    // 真正在做宽容的是上面的 DodgeBufferFrames（输入缓冲）。

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

    /// <summary>
    /// 半自动防御（T37 缺口③ / 05 §126）。**只有最简单档（見習）开**（05 §50）。
    ///
    /// 语义由 05 §126-128 定死，执行侧不许自己发明：
    /// 按住防御时，若没手动弹开，系统在**命中前 2 帧**自动判定弹开，
    /// **每 10 秒最多 3 次**，且**只对「一般攻击」生效，对「危」攻击一律不生效**。
    /// 它把"精准时机"换成"资源管理"，不是无敌。
    /// </summary>
    [Export] public bool HalfAutoGuard { get; set; }

    /// <summary>半自动防御提前几帧判定（05 §126：命中前 2 帧）。</summary>
    [Export] public int HalfAutoGuardLeadFrames { get; set; } = 2;

    /// <summary>半自动防御的额度窗口（帧）。05 §126 的"每 10 秒"= 600 帧。</summary>
    [Export] public int HalfAutoGuardWindowFrames { get; set; } = 600;

    /// <summary>一个额度窗口内最多触发几次（05 §126：3 次）。</summary>
    [Export] public int HalfAutoGuardMaxTriggers { get; set; } = 3;

    [Export] public bool ShowParryRhythm { get; set; }
}
