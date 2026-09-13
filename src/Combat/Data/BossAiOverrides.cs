using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 某个阶段对 BOSS AI 的**覆盖值**（T9）。全部是"倍率/加成"，不是绝对数——
/// 这样基础 AI 改了，各阶段不会跟着漂。
///
/// ⚠️ 这些旋钮要**真的被读**，否则就是又一个"定义了但没人用"的死配置
/// （本项目已经栽过五次，其中两个就在 `DifficultyProfile` 里）。
/// 所以 `SelfTest` 会把它们记进清单，接了 BOSS AI 之后要能指到调用点。
/// </summary>
[GlobalClass]
public partial class BossAiOverrides : Resource
{
    [ExportGroup("AI 覆盖（倍率）")]

    /// <summary>攻击间隔倍率。越小越凶。</summary>
    [Export] public float AttackIntervalScale { get; set; } = 1f;

    /// <summary>移动速度倍率。</summary>
    [Export] public float MoveSpeedScale { get; set; } = 1f;

    /// <summary>反应延迟加成（帧）。越大越迟钝，用来给玩家留读招时间。</summary>
    [Export] public int ReactionDelayBonusFrames { get; set; }
}
