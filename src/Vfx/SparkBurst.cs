using Godot;
using Oniblade.Combat.Data;

namespace Oniblade.Vfx;

/// <summary>
/// 弹开 / 拼刀的火花（T28 / 10 §4 的 P0）。
///
/// **第一原则是"不能盖住判定"**：这两处的火花必须短，否则玩家看不清下一刀什么时候来。
/// 华丽是二闪与忍杀的事，**不是防御反馈的事**。
///
/// 所以这里的寿命是**按逻辑帧写死的常量，并且被无头测试断言**——
/// 不是"感觉挺短"。这也是刻意不用 <c>Timer</c>（秒）的原因：
/// 10 §4 的要求是"8 帧内结束"，秒数给不了这个保证。
///
/// T53 加了第二条生成路径：弹开不再是"一种火花"，而是**按攻击性质分档**
/// （斩/打/突/暗），档位来自 <see cref="DeflectFeedbackProfile"/>。
/// 拼刀仍走原来的 <c>clash</c> 预设——它是另一套反馈，T53 没动它。
/// </summary>
public partial class SparkBurst : Node3D
{
	/// <summary>弹开火花的存在帧数**上限**（10 §4：白蓝、8 帧内结束）。</summary>
	public const int DeflectLifetimeFrames = 8;

	/// <summary>拼刀火花：更大、更慢、带拖尾，所以比弹开长一点。</summary>
	public const int ClashLifetimeFrames = 15;

	// 弹开的**基准**参数。T53 的档位是相对它做倍率——
	// 所以 Slash 档（倍率全 1）生成的火花与 T53 之前**逐项一致**，零观感回归。
	private const float BaseSpeedMin = 3.0f;
	private const float BaseSpeedMax = 7.0f;
	private const float BaseScaleMin = 0.4f;
	private const float BaseScaleMax = 1.1f;

	public int LifetimeFrames { get; private set; }
	public bool IsClash { get; private set; }

	/// <summary>T53：这一簇用的是哪一档（拼刀、或没有档位数据时为 null）。自检读它取证。</summary>
	public DeflectFeedbackProfile? Profile { get; private set; }

	private int _framesLeft;

	/// <summary>
	/// 拼刀火花，以及**没有档位数据时的弹开降级路径**。
	/// 降级要表现为"照旧有火花"，不是"什么都不发生"。
	/// </summary>
	public static SparkBurst Create(Vector3 position, Vector3 direction, bool clash)
	{
		if (clash)
		{
			return Create(position, direction, new SparkParams
			{
				LifetimeFrames = ClashLifetimeFrames,
				Amount = 26,
				SpreadDeg = 55f,
				SpeedMin = 2.2f,
				SpeedMax = 5.5f,
				SizeMin = 0.6f,
				SizeMax = 1.8f,
				Color = new Color(1.00f, 0.78f, 0.42f),
			}, clash: true, profile: null);
		}

		return Create(position, direction, DefaultDeflectParams, clash: false, profile: null);
	}

	/// <summary>T53：按攻击性质对应的档位生成弹开火花。</summary>
	public static SparkBurst Create(Vector3 position, Vector3 direction, DeflectFeedbackProfile? profile)
	{
		SparkParams p = profile is null
			? DefaultDeflectParams
			: new SparkParams
			{
				// 上限硬钉在 8 帧：10 §4 的第一原则是"不能盖住判定"，
				// 所以这一项只能**更短**（突刺＝短促），数据里写大了也不许越界。
				LifetimeFrames = Mathf.Clamp(profile.SparkLifetimeFrames, 1, DeflectLifetimeFrames),
				Amount = Mathf.Max(1, profile.SparkAmount),
				SpreadDeg = profile.SparkSpreadDeg,
				SpeedMin = BaseSpeedMin * profile.SparkSpeedScale,
				SpeedMax = BaseSpeedMax * profile.SparkSpeedScale,
				SizeMin = BaseScaleMin * profile.SparkScale,
				SizeMax = BaseScaleMax * profile.SparkScale,
				Color = profile.SparkColor,
			};

		return Create(position, direction, p, clash: false, profile);
	}

	private static readonly SparkParams DefaultDeflectParams = new()
	{
		LifetimeFrames = DeflectLifetimeFrames,
		Amount = 14,
		SpreadDeg = 40f,
		SpeedMin = BaseSpeedMin,
		SpeedMax = BaseSpeedMax,
		SizeMin = BaseScaleMin,
		SizeMax = BaseScaleMax,
		Color = new Color(0.78f, 0.88f, 1.00f),
	};

	private static SparkBurst Create(
		Vector3 position, Vector3 direction, in SparkParams p, bool clash, DeflectFeedbackProfile? profile)
	{
		var burst = new SparkBurst
		{
			Name = clash ? "ClashSpark" : "DeflectSpark",
			IsClash = clash,
			Profile = profile,
			LifetimeFrames = p.LifetimeFrames,
			Position = position,
		};

		burst.Build(direction, p);
		return burst;
	}

	private void Build(Vector3 direction, in SparkParams p)
	{
		_framesLeft = LifetimeFrames;

		float life = LifetimeFrames / (float)Utils.Frames.PerSecond;

		var process = new ParticleProcessMaterial
		{
			// 火花朝**攻方那一侧**溅回去（-direction），呈一个锥面。
			Direction = -direction,
			Spread = p.SpreadDeg,
			InitialVelocityMin = p.SpeedMin,
			InitialVelocityMax = p.SpeedMax,
			Gravity = new Vector3(0f, -9f, 0f),
			ScaleMin = p.SizeMin,
			ScaleMax = p.SizeMax,
			Color = p.Color,
		};

		var particles = new GpuParticles3D
		{
			Name = "Particles",
			Amount = p.Amount,
			Lifetime = life,
			OneShot = true,
			Explosiveness = 1f,
			ProcessMaterial = process,
			DrawPass1 = VfxMesh.Glow(IsClash ? 0.055f : 0.035f, p.Color),
		};

		AddChild(particles);
		particles.Emitting = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (--_framesLeft <= 0)
			QueueFree();
	}

	/// <summary>一簇火花的全部可调参数（把"是什么"与"谁生成它"分开）。</summary>
	private struct SparkParams
	{
		public int LifetimeFrames;
		public int Amount;
		public float SpreadDeg;
		public float SpeedMin;
		public float SpeedMax;
		public float SizeMin;
		public float SizeMax;
		public Color Color;
	}
}
