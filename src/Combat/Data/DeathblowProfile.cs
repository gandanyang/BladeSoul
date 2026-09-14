using Godot;

namespace Oniblade.Combat.Data;

/// <summary>
/// 处决参数（T52）。**数值全部在这里，代码里一个都不写死**（铁律 1）。
///
/// 为什么处决要单独一份数据而不是塞进 <c>DifficultyProfile</c>：
/// 它不是难度旋钮，而是**动作的帧构成**——四档难度下处决演出应当是同一段演出
/// （玩家不会因为选了"修罗"就看到不一样的动作）。这与 <c>DodgeState</c> 把
/// `PerfectDodgeIssenFrames` 留在状态类里是同一个判断。
/// </summary>
[GlobalClass]
public partial class DeathblowProfile : Resource
{
    /// <summary>处决的最大水平距离（米）。比普攻（2.4）略短：够得着才出，免得"隔空处决"。</summary>
    [Export] public float MaxDistance { get; set; } = 2.2f;

    /// <summary>处决演出的总帧数。</summary>
    [Export] public int TotalFrames { get; set; } = 90;

    /// <summary>
    /// 无敌到第几帧（含）。T52 口径是**全程无敌**，所以默认等于 <see cref="TotalFrames"/>。
    /// 单独留一个字段是为了将来"演出最后几帧可以被打断"这种调整不用改代码。
    /// </summary>
    [Export] public int InvulnerableFrames { get; set; } = 90;

    /// <summary>伤害落在第几帧（演出里"刀落下去"的那一刻）。</summary>
    [Export] public int HitFrame { get; set; } = 50;

    /// <summary>处决伤害。精英/BOSS 按"扣一格命"另行处理，这里是一次性致死量。</summary>
    [Export] public int Damage { get; set; } = 9999;

    // 这里原来还有一个 `TargetLockFrames`（"把目标钉住多少帧"），已删除。
    //
    // 删它的理由不是"没人用"，而是**它和真正生效的时长是两套控制**：
    // 目标被钉住的时长由 <c>DeathblowState.DurationFrames</c> 决定，而那个值来自
    // <see cref="TotalFrames"/>（演出总长）。留着 TargetLockFrames 就等于给了
    // 一个"看起来能调、实际不起作用"的旋钮——真去调它的人会以为改坏了引擎。
    // 死配置棘轮（check.ps1 第 2b 步）第一次跑就把它抓了出来。
}
