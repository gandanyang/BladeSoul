using Godot;

namespace Oniblade.Vfx;

/// <summary>
/// 命中血雾（T28 / 10 §4 的 P0）。
///
/// 关键要求是**方向与刀路一致**，不是原地爆开：
/// 原地爆开看起来像"被打气了一下"，顺着刀路才像"被刀划开了"。
/// 所以这里用的是窄锥（<c>Direction</c> + 小 <c>Spread</c>）而不是球形发射。
/// </summary>
public partial class BloodMist : Node3D
{
	/// <summary>血雾的存在帧数。比火花长——雾本来就该散得慢一点。</summary>
	public const int LifetimeFrames = 24;

	public int LifetimeFramesValue => LifetimeFrames;

	private int _framesLeft;

	public static BloodMist Create(Vector3 position, Vector3 direction)
	{
		var mist = new BloodMist
		{
			Name = "BloodMist",
			Position = position,
		};

		mist.Build(direction);
		return mist;
	}

	private void Build(Vector3 direction)
	{
		_framesLeft = LifetimeFrames;

		Color color = new(0.34f, 0.03f, 0.05f);

		var process = new ParticleProcessMaterial
		{
			// 顺着刀路喷出去（+direction 是"从攻方指向防御方"，也就是刀前进的方向）。
			Direction = direction,
			Spread = 22f,
			InitialVelocityMin = 1.2f,
			InitialVelocityMax = 3.4f,
			Gravity = new Vector3(0f, -2.5f, 0f),
			ScaleMin = 1.6f,
			ScaleMax = 3.4f,
			Color = color,
		};

		var particles = new GpuParticles3D
		{
			Name = "Particles",
			Amount = 18,
			Lifetime = LifetimeFrames / (float)Utils.Frames.PerSecond,
			OneShot = true,
			Explosiveness = 0.85f,
			ProcessMaterial = process,
			DrawPass1 = VfxMesh.Glow(0.10f, color),
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
