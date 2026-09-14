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

    /// <summary>
    /// 体干被打满之后的**破韧窗口**帧数（T52）。
    ///
    /// 这段时间里敌人不能主动攻击、只能挨打，并且可以被处决（交互键 F/E）；
    /// 窗口过期就恢复行动。它放在这里而不是塞进 `DifficultyProfile`：
    /// 破韧时长是**敌人自己的属性**（杂兵 2 秒、精英该更短、BOSS 可能不给），
    /// 不是难度旋钮——四档难度下同一个足兵的破韧窗口应当是同一个长度。
    ///
    /// 默认 120 帧 = 2.0 秒（项目主人裁定）。
    /// </summary>
    [Export] public int PostureBrokenFrames { get; set; } = 120;

    [ExportGroup("移动")]
    [Export] public float MoveSpeed { get; set; } = 4.2f;
    [Export] public float SprintSpeed { get; set; } = 7.0f;
    [Export] public float AttackMoveSpeed { get; set; } = 1.6f;

    /// <summary>
    /// 格挡姿态下的移动速度倍率（02 文档 §2.2：约 40%）。
    /// 默认 1.0 = 不变；只有会格挡的角色（玩家）在 data/actors/ 里调小它。
    /// </summary>
    [Export] public float GuardMoveScale { get; set; } = 1.0f;

    /// <summary>
    /// 格挡时**整体下沉**多少米（T52 试玩："按住右键格挡的姿势也有问题"）。
    ///
    /// 为什么需要它、而不是继续用腿部角度：本骨架髋部固定、只有大腿/小腿/脚三节，
    /// 实测"旋腿"只会把**膝盖抬起来**（大腿 1.50 rad → 膝抬高 22cm），
    /// 做不出下蹲。所以"压重心"必须靠根骨骼下沉，腿只做轻微弯曲配合。
    /// 0 = 不下沉（默认，给不会格挡的角色）。
    /// </summary>
    [Export] public float GuardCrouchDepth { get; set; } = 0f;

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

    [ExportGroup("死亡")]
    /// <summary>
    /// 敌人死亡演出的总帧数（D3，2026-09-15）。姿势曲线（<c>AshigaruAnimator.ApplyDeath</c>）
    /// 倒地占前 75%、余量收尾——60 帧即倒地 45 帧完成、60 帧完全静止。
    /// ⚠️ **60 是临时默认值，制作人还没拍最终数**：要调就改这里（或对应 .tres），不在 C# 里写死。
    /// </summary>
    [Export] public int DeathPerformanceFrames { get; set; } = 60;

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
