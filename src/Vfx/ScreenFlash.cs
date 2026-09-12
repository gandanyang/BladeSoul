using Godot;

namespace Oniblade.Vfx;

/// <summary>
/// 一闪的全屏黑白闪（T28 / 10 §4 的 P0）。
///
/// 它是四件 P0 里唯一**不挂在世界坐标上**的特效：一闪的重点是"时间被抽出来了"，
/// 全屏闪是这句话最直接的表达。0.1 秒 ≈ 6 帧，和 <c>PlayerActor</c> 那边的慢镜同时发生。
///
/// 用 <see cref="CanvasLayer"/> + 全屏 <see cref="ColorRect"/>，**MouseFilter = Ignore**——
/// 它是覆盖层，绝不能吞掉输入（否则一闪期间玩家的按键会丢）。
/// </summary>
public partial class ScreenFlash : CanvasLayer
{
	/// <summary>闪光的帧数（10 §4：全屏 0.1s 高对比 ≈ 6 帧）。</summary>
	public const int FlashFrames = 6;

	/// <summary>层级：盖在 HUD 之上、调试面板之下。</summary>
	public const int FlashLayer = 90;

	public int LifetimeFrames => FlashFrames;

	private ColorRect _rect = null!;
	private int _framesLeft;

	/// <summary>闪光当前的不透明度（测试断言"确实在衰减"用）。</summary>
	public float CurrentAlpha => _rect?.Color.A ?? 0f;

	public static ScreenFlash Create(float strength)
	{
		var flash = new ScreenFlash
		{
			Name = "IssenFlash",
			Layer = FlashLayer,
		};

		flash.Build(strength);
		return flash;
	}

	private void Build(float strength)
	{
		_framesLeft = FlashFrames;

		_rect = new ColorRect
		{
			Name = "Flash",
			Color = new Color(1f, 1f, 1f, Mathf.Clamp(strength, 0f, 1f)),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};

		AddChild(_rect);

		// 铺满屏幕。必须在入树之后做，否则拿不到 viewport 尺寸。
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (--_framesLeft <= 0)
		{
			QueueFree();
			return;
		}

		// 先亮到底再快速衰减（平方衰减 = 前段留得住、后段掉得快），
		// 这样"高对比"那一下才够狠，又不会糊住画面太久。
		float t = _framesLeft / (float)FlashFrames;
		Color color = _rect.Color;
		color.A = t * t;
		_rect.Color = color;
	}
}
