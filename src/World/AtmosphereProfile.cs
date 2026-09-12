using Godot;

namespace Oniblade.World;

/// <summary>
/// 氛围档（T34 / 10 §3.1）。**所有参数都在这里，代码里一个数都不写**（04 §2）。
///
/// 一档氛围 = 一套"黄昏雨夜"：冷色环境光 + 体积雾 + 跟摄像机的雨 + 若干灯笼。
/// 雾不只是氛围，它同时是**遮住远景低模**的省钱手段（07 §7）——
/// 所以别把它当装饰，它承担着"不用做远景细节"这个预算任务。
///
/// 颜色纪律（10 §1）：低饱和青灰；**暖色只允许出现在灯笼上**。
/// <see cref="AmbientColor"/> 必须是冷色——有断言盯着这一条。
/// </summary>
[GlobalClass]
public partial class AtmosphereProfile : Resource
{
	[Export] public string Id { get; set; } = "rainy_night";
	[Export] public string DisplayName { get; set; } = "黄昏雨夜";

	[ExportGroup("调色")]
	[Export] public Color BackgroundColor { get; set; } = new(0.055f, 0.070f, 0.095f);
	[Export] public Color AmbientColor { get; set; } = new(0.30f, 0.36f, 0.46f);
	[Export] public float AmbientEnergy { get; set; } = 0.30f;
	[Export] public float TonemapExposure { get; set; } = 0.95f;

	/// <summary>整体饱和度。雨夜要压得很低——低饱和青灰是这套美术的底色。</summary>
	[Export] public float Saturation { get; set; } = 0.85f;

	[ExportGroup("体积雾")]
	[Export] public bool FogEnabled { get; set; } = true;

	/// <summary>雾密度。**它是"能不能读招"的直接旋钮**——见 §读招保底。</summary>
	[Export] public float FogDensity { get; set; } = 0.022f;

	[Export] public Color FogAlbedoColor { get; set; } = new(0.36f, 0.42f, 0.50f);
	[Export] public float FogAnisotropy { get; set; } = 0.35f;

	/// <summary>雾的渲染距离。它同时也是"远景画多远"的预算——够盖住低模就行。</summary>
	[Export] public float FogLength { get; set; } = 64f;

	[ExportGroup("雨")]
	[Export] public bool RainEnabled { get; set; } = true;
	[Export] public int RainAmount { get; set; } = 1200;
	[Export] public float RainLifetime { get; set; } = 1.1f;

	/// <summary>雨盒尺寸。粒子系统每帧跟着摄像机走，所以只在摄像机周围下。</summary>
	[Export] public Vector3 RainBoxExtents { get; set; } = new(16f, 10f, 16f);

	[Export] public float RainFallSpeed { get; set; } = 22f;
	[Export] public Color RainColor { get; set; } = new(0.55f, 0.62f, 0.72f, 0.35f);

	[ExportGroup("灯笼（画面里唯一的暖色）")]
	[Export] public Color LanternColor { get; set; } = new(1.00f, 0.62f, 0.28f);
	[Export] public float LanternEnergy { get; set; } = 1.6f;
	[Export] public float LanternRange { get; set; } = 9.0f;

	/// <summary>
	/// 同屏灯笼上限。**暖色＝注意力**，多了会抢读招（10 §3.1 明文要求控制数量）。
	/// 超出的灯笼不点灯，只在日志里报出来。
	/// </summary>
	[Export] public int MaxLanterns { get; set; } = 6;

	/// <summary>灯罩自发光强度。灯笼得自己亮，否则在雨夜里读不出"这里有个灯"。</summary>
	[Export] public float LanternGlowEnergy { get; set; } = 2.0f;

	[ExportGroup("室内天光（T40：房子里得有光）")]
	/// <summary>
	/// 天光**不是一盏灯，是"屋顶开了口子、冷光落下来"**（T40）。
	///
	/// 为什么必须是冷色：10 §1 的纪律是**暖色只能来自灯笼**，所以室内补光只允许是冷色；
	/// 而它又不能是"凭空一盏冷色天花板灯"——那读起来是电灯，不是战国。
	/// 所以每个天光锚点上方**真的开着一个洞**（关卡里用 CSG 减出来），
	/// 光源就摆在洞口往下打，玩家看到的是光柱、不是灯具。
	/// </summary>
	[Export] public Color SkylightColor { get; set; } = new(0.52f, 0.60f, 0.72f);

	[Export] public float SkylightEnergy { get; set; } = 2.6f;

	/// <summary>天光的照射距离。它是一盏**柔光**（不是锥形聚光），所以要够盖住半个房间。</summary>
	[Export] public float SkylightRange { get; set; } = 14.0f;

	/// <summary>
	/// 天光开口上限。开多了画面会"漏成筛子"，而且每个都是一次投影开销。
	/// </summary>
	/// <remarks>
	/// 为什么是柔光而不是聚光：聚光的锥体和墙面相交会留下**硬边光斑**，
	/// 打在墙上一眼就是投影仪/电灯的光斑，正是 10 §1 要避免的"不像战国"。
	/// 柔光没有锥形边界，读起来是"天光从洞口漫下来"。
	/// </remarks>
	[Export] public int MaxSkylights { get; set; } = 4;

	[ExportGroup("室内外光照分层（T40：灯笼不投影，光会穿墙）")]
	/// <summary>
	/// 室内几何所在的渲染层。**室外灯笼的 cull mask 不含这一层**，于是照不进屋里——
	/// 这就是卡片说的"分层"，比给每盏灯笼开阴影便宜得多（灯笼仍然是零阴影的）。
	/// 室内几何要**只挂这一层**（不挂默认的第 1 层），否则室外灯照样照得到。
	/// </summary>
	[Export] public int InteriorLayer { get; set; } = 2;

	/// <summary>室外灯笼照哪些层（只照默认层）。</summary>
	[Export] public uint OutdoorLanternCullMask { get; set; } = 1;

	/// <summary>室内灯笼照哪些层。它在屋里、挡不住，所以室内外都照。</summary>
	[Export] public uint IndoorLanternCullMask { get; set; } = 1 | 2;

	[ExportGroup("读招保底（雾不许盖住判定）")]
	/// <summary>魔骸发光的能量下限（10 §1：伤口/眼窝透红光，越强的敌人越亮）。</summary>
	[Export] public float ReadabilityGlowMinEnergy { get; set; } = 1.8f;

	/// <summary>8×8 战斗区的对角线，也就是最远的读招距离（≈11.3m）。</summary>
	[Export] public float CombatReadDistance { get; set; } = 11.3f;

	/// <summary>
	/// 在读招距离上雾必须保留的透光率下限。
	/// 低于它，"敌人从雾里走出来"就变成"敌人凭空出现"。
	/// </summary>
	[Export] public float MinTransmittanceAtCombatDistance { get; set; } = 0.5f;

	/// <summary>
	/// 该距离上的透光率（指数雾的近似）。**这是本档最重要的自检量**——
	/// 雾浓到读不出招，战斗就没法打了，而这件事在空场景里看不出来。
	/// </summary>
	public float TransmittanceAt(float distance) =>
		FogEnabled ? Mathf.Exp(-FogDensity * distance) : 1f;
}
