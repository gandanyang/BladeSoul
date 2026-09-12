using Godot;

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
/// </summary>
public partial class SparkBurst : Node3D
{
	/// <summary>弹开火花的存在帧数（10 §4：白蓝、8 帧内结束）。</summary>
	public const int DeflectLifetimeFrames = 8;

	/// <summary>拼刀火花：更大、更慢、带拖尾，所以比弹开长一点。</summary>
	public const int ClashLifetimeFrames = 15;

	public int LifetimeFrames { get; private set; }
	public bool IsClash { get; private set; }

	private int _framesLeft;

	public static SparkBurst Create(Vector3 position, Vector3 direction, bool clash)
	{
		var burst = new SparkBurst
		{
			Name = clash ? "ClashSpark" : "DeflectSpark",
			IsClash = clash,
			LifetimeFrames = clash ? ClashLifetimeFrames : DeflectLifetimeFrames,
			Position = position,
		};

		burst.Build(direction);
		return burst;
	}

	private void Build(Vector3 direction)
	{
		_framesLeft = LifetimeFrames;

		float life = LifetimeFrames / (float)Utils.Frames.PerSecond;

		Color color = IsClash
			? new Color(1.00f, 0.78f, 0.42f)     // 拼刀：橙白
			: new Color(0.78f, 0.88f, 1.00f);    // 弹开：白蓝

		var process = new ParticleProcessMaterial
		{
			// 火花朝**攻方那一侧**溅回去（-direction），呈一个锥面。
			Direction = -direction,
			Spread = IsClash ? 55f : 40f,
			InitialVelocityMin = IsClash ? 2.2f : 3.0f,
			InitialVelocityMax = IsClash ? 5.5f : 7.0f,
			Gravity = new Vector3(0f, -9f, 0f),
			ScaleMin = IsClash ? 0.6f : 0.4f,
			ScaleMax = IsClash ? 1.8f : 1.1f,
			Color = color,
		};

		var particles = new GpuParticles3D
		{
			Name = "Particles",
			Amount = IsClash ? 26 : 14,
			Lifetime = life,
			OneShot = true,
			Explosiveness = 1f,
			ProcessMaterial = process,
			DrawPass1 = VfxMesh.Glow(IsClash ? 0.055f : 0.035f, color),
		};

		AddChild(particles);
		particles.Emitting = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (--_framesLeft <= 0)
			QueueFree();
	}
}
