using Godot;
using Oniblade.Combat.Data;

namespace Oniblade.Combat;

/// <summary>
/// 「危」攻击预警标记（T12）。
///
/// 它从"可及性选项"升级成了**机制必需**：02 §3 的裁定是
/// "一闪应对一切攻击，弹开只应对一般攻击"。这条裁定的直接后果是——
/// 玩家如果分不清"一般"和"危"，就会拿弹开去应付危攻击，然后直接挨打。
/// 预警不是锦上添花，它是这条裁定能成立的前提。
///
/// 灰盒期表现：头顶一颗红色自发光球 + 闪烁。三种形态用**闪烁快慢 + 色相**区分，
/// 再叠加 07 §2.2 要求的三种音高（高/中/低）——这样色盲玩家和听声辨位的玩家
/// 都能分辨，不必依赖单一通道。
///
/// 挂载与生命周期由 <see cref="CombatActor"/> 的基类实现统一负责，
/// 敌人不需要自己关心预警（将来任何新敌人自动获得）。
/// </summary>
public partial class PerilousCue : Node3D
{
	/// <summary>预警球相对脚底的高度（米）。略高于灰盒胶囊体的头顶。</summary>
	[Export] public float HeightOffset { get; set; } = 2.1f;

	/// <summary>球的半径（米）。</summary>
	[Export] public float Radius { get; set; } = 0.22f;

	private MeshInstance3D _ball = null!;
	private StandardMaterial3D _material = null!;

	private int _framesLeft;
	private int _blinkFrame;
	private int _halfPeriodFrames = 4;

	/// <summary>最近一次预警的形态（端到端测试断言"确实走了 Perilous 分支"）。</summary>
	public PerilousKind LastKind { get; private set; } = PerilousKind.None;

	/// <summary>当前是否正在预警。</summary>
	public bool IsShowing => _framesLeft > 0;

	/// <summary>累计预警次数（测试与调试面板用）。</summary>
	public int ShowCount { get; private set; }

	public override void _Ready()
	{
		Position = new Vector3(0f, HeightOffset, 0f);

		_material = new StandardMaterial3D
		{
			// 自发光球：不吃场景光照，暗环境里也一眼看得见。
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = ColorFor(PerilousKind.Thrust),
		};

		_ball = new MeshInstance3D
		{
			Name = "Ball",
			Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2f },
			MaterialOverride = _material,
			// 预警是给玩家读的 UI 语义，不该投影、不该参与视觉噪音。
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_ball);

		SetShowing(false);
	}

	/// <summary>
	/// 开始预警。<paramref name="frames"/> 通常给 <c>AttackData.TotalFrames</c>，
	/// 于是它会自己过期、和招式同长；攻击提前中断时由 <see cref="Hide"/> 收掉。
	/// </summary>
	public void Show(PerilousKind kind, int frames)
	{
		if (kind == PerilousKind.None)
		{
			Hide();
			return;
		}

		LastKind = kind;
		ShowCount++;
		_framesLeft = Mathf.Max(1, frames);
		_blinkFrame = 0;

		_material.AlbedoColor = ColorFor(kind);
		_halfPeriodFrames = Mathf.Max(1, HalfPeriodFor(kind));

		SetShowing(true);
	}

	/// <summary>
	/// 收起预警（攻击结束、被打断、死亡时调用）。
	///
	/// 用 <c>new</c> 是刻意的：T12 的规格把接口定名为 <c>Show</c> / <c>Hide</c>，
	/// 而 <see cref="CanvasItem.Hide"/> 同名同签名。两者的效果一致（都让节点不可见），
	/// 区别只是本方法还会清掉倒计时，所以语义是兼容的、不会互相打架。
	/// （<c>Show(PerilousKind, int)</c> 是重载，没有这个问题。）
	/// </summary>
	public new void Hide()
	{
		_framesLeft = 0;
		SetShowing(false);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_framesLeft <= 0)
			return;

		_framesLeft--;
		if (_framesLeft <= 0)
		{
			SetShowing(false);
			return;
		}

		// 方波闪烁：比正弦更像"信号"，也更容易在余光里被注意到。
		_blinkFrame++;
		_ball.Visible = (_blinkFrame / _halfPeriodFrames) % 2 == 0;
	}

	private void SetShowing(bool showing)
	{
		Visible = showing;
		if (showing)
			_ball.Visible = true;
	}

	/// <summary>
	/// 三种形态的颜色。全部落在"红—橙—暗红"一段里：
	/// 主通道仍然是"危险"，色相只做辅助区分，避免变成"三颗不同颜色的球"那种 UI 噪音。
	/// </summary>
	private static Color ColorFor(PerilousKind kind) => kind switch
	{
		// 危·横扫（中音）：偏橙，最容易和"横着扫过来"的动作对上。
		PerilousKind.Sweep => new Color(1.0f, 0.45f, 0.05f),
		// 危·抓取（低音）：暗红，压迫感最强。
		PerilousKind.Grab => new Color(0.62f, 0.05f, 0.12f),
		// 危·突刺（高音）：纯红，最急促。
		_ => new Color(1.0f, 0.13f, 0.13f),
	};

	/// <summary>闪烁半周期（帧）：突刺最快、抓取最慢。音高之外的第二条冗余通道。</summary>
	private static int HalfPeriodFor(PerilousKind kind) => kind switch
	{
		PerilousKind.Sweep => 5,
		PerilousKind.Grab => 7,
		_ => 4,
	};
}
