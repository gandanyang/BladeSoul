using Godot;

namespace Oniblade.Player;

/// <summary>
/// 弹开窗指示器的外观参数（`data/player/deflect_cue.tres`）。
///
/// **为什么需要这个指示器**（T51 试玩反馈的结论）：
/// 弹开窗只在**按下防御键那一刻**开一次，宽 <c>DeflectWindowFrames</c> 帧，
/// 之后整段防御再也不开。实测（`GuardWindowProbe`）：按住右键时窗口在第 9 帧
/// 就过期，而敌人前摇 24 帧——两者精确错开，于是"能挡但从来弹不开"。
///
/// 规则本身没问题（点按的有效区间 = 提前 0~8 帧，正好等于配置的 9 帧）。
/// 问题是**玩家看不见窗口**：他没有任何途径知道"现在这几帧能不能弹开"，
/// 所以学不会时机，只能感觉到"时灵时不灵"。
///
/// 这个指示器就是那条缺失的反馈：窗口开着的每一帧，脚下有一圈光。
/// 数值一律走本资源，代码里不写死（铁律 1）。
/// </summary>
[GlobalClass]
public partial class DeflectCueProfile : Resource
{
	[ExportGroup("尺寸")]

	/// <summary>光环半径（米）。放在脚下，别大到看不清角色。</summary>
	[Export] public float Radius { get; set; } = 0.62f;

	/// <summary>环的粗细（米）。太细在远景里会消失。</summary>
	[Export] public float Thickness { get; set; } = 0.045f;

	/// <summary>离地高度（米）。贴地会被地面 z-fighting 吃掉，抬高一点点。</summary>
	[Export] public float HeightOffset { get; set; } = 0.03f;

	[ExportGroup("颜色")]

	/// <summary>窗口正开着时的颜色。</summary>
	[Export] public Color OpenColor { get; set; } = new(0.55f, 0.85f, 1f, 0.75f);

	/// <summary>
	/// 加上自发光，让它在暗处也看得见——这是**必须看见的信息**，
	/// 和"氛围"不是一回事。
	/// </summary>
	[Export] public Color EmissionColor { get; set; } = new(0.35f, 0.7f, 1f);

	/// <summary>自发光强度。</summary>
	[Export] public float EmissionEnergy { get; set; } = 1.6f;
}
