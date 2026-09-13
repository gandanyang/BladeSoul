using Godot;

namespace Oniblade.Player;

/// <summary>
/// 跳跃参数（T41）。**一个数都不许写进代码**（铁律 1）；帧字段带 `Frames` 后缀。
///
/// 滞空时长**不写死帧数**——它由 <see cref="TakeoffSpeed"/> 和引擎重力一起决定
/// （`CombatActor` 已经在读 `physics/3d/default_gravity`），所以它天然跟着物理走。
/// </summary>
[GlobalClass]
public partial class JumpProfile : Resource
{
    [ExportGroup("跳跃")]

    /// <summary>起跳初速（米/秒）。最大高度 = v²/(2g)：5.5 → 约 1.54 米。</summary>
    [Export] public float TakeoffSpeed { get; set; } = 5.5f;

    /// <summary>落地硬直帧数——落地后这一段不能动，**防止无脑连跳**。</summary>
    [Export] public int LandRecoveryFrames { get; set; } = 12;
}
