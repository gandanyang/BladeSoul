using Godot;

namespace Oniblade.World;

/// <summary>
/// 掉落保护的参数（T45）。**一个数都不许写进代码**（AGENTS.md 铁律 1 / 04 §2）。
///
/// 为什么是"确认式"而不是"一低于阈值就重置"：
/// 那样玩家在**刚好擦到阈值附近**时会被反复重置，而且会和 T41 的跳跃落地打架。
/// 所以判定是"连续 N 帧都在界外"，N 在下面。
/// </summary>
[GlobalClass]
public partial class FallRecoveryProfile : Resource
{
    [ExportGroup("越界判定")]

    /// <summary>低于这个高度（米）就算掉出世界。道场地面在 y=0。</summary>
    [Export] public float ThresholdY { get; set; } = -6f;

    /// <summary>连续多少帧都在界外才算数（12 帧 = 0.2 秒，够滤掉"擦边"和落地抖动）。</summary>
    [Export] public int ConfirmFrames { get; set; } = 12;

    /// <summary>水平方向离原点多远算越界（防止一路走出关卡而不掉下去）。</summary>
    [Export] public float MaxHorizontalDistance { get; set; } = 200f;

    [ExportGroup("表现")]

    /// <summary>淡出多少帧后把玩家挪回去（不许"啪一下瞬移"，否则玩家以为卡了）。</summary>
    [Export] public int FadeOutFrames { get; set; } = 15;

    [Export] public int FadeInFrames { get; set; } = 24;

    [Export] public Color FadeColor { get; set; } = new(0f, 0f, 0f, 1f);
}
