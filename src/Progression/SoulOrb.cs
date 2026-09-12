using Godot;

namespace Oniblade.Progression;

/// <summary>
/// 魄火（03 §6.1）。
///
/// **它不是"掉在地上的道具"，是"从尸体里飞出来、自己飞向笼手的东西"。**
/// 所以它先停滞很短一段（让玩家看清它是从哪来的），然后加速追向玩家；
/// 距离再远也会追过来。玩家的感受应该是"我杀掉它之后，这东西自然归我"，
/// 而不是"地上有个球要我去捡"。
/// </summary>
public partial class SoulOrb : Node3D
{
	/// <summary>追向笼手时的最高速度（m/s）。给上限，免得远处的魄瞬移过来。</summary>
	[Export] public float MaxFlySpeed { get; set; } = 11f;

	/// <summary>加速度。太小会飘得拖沓，太大就变成瞬移。</summary>
	[Export] public float Acceleration { get; set; } = 26f;

	/// <summary>吸附判定半径。</summary>
	[Export] public float AbsorbRadius { get; set; } = 0.55f;

	/// <summary>生成后原地停滞的帧数——这是玩家"看见它从尸体里出来"的时间。</summary>
	[Export] public int HoverFrames { get; set; } = 10;

	/// <summary>滞后（秒）。"连吸"时后出的魄靠它才不会挤成一坨。</summary>
	[Export] public float ChainDelaySeconds { get; set; }

	public SoulType Type { get; set; } = SoulType.Crimson;
	public int Amount { get; set; } = 10;

	/// <summary>精英 / BOSS 的魄：吸收它会推进侵蚀（03 §6.6）。</summary>
	public bool IsGreat { get; set; }

	/// <summary>被吸收时，吸收者是不是正在「深吸」。</summary>
	public bool ConsumedByDeepAbsorb { get; private set; }

	private OniGauntlet? _gauntlet;
	private Vector3 _velocity;
	private int _hoverFramesLeft;
	private float _chainDelayLeft;

	public override void _EnterTree() => AddToGroup("soul_orb");

	public override void _Ready()
	{
		_hoverFramesLeft = HoverFrames;
		_chainDelayLeft = ChainDelaySeconds;

		// 灰盒表现：一小团黑紫色自发光。真实资产在 M7 替换。
		var glow = new MeshInstance3D
		{
			Name = "Glow",
			Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 8, Rings = 4 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.35f, 0.18f, 0.45f),
				EmissionEnabled = true,
				Emission = new Color(0.75f, 0.35f, 0.95f),
				EmissionEnergyMultiplier = 2.2f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		};
		AddChild(glow);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		// 出生时向上浮一下，读作"从尸体里飞出来"。
		if (_hoverFramesLeft > 0)
		{
			_hoverFramesLeft--;
			GlobalPosition += new Vector3(0f, 0.9f * dt, 0f);
			return;
		}

		if (_chainDelayLeft > 0f)
		{
			_chainDelayLeft -= dt;
			return;
		}

		_gauntlet ??= FindGauntlet();

		if (_gauntlet is null || !IsInstanceValid(_gauntlet))
		{
			// 没有笼手（比如玩家已经没了）：原地飘着，不报错。
			GlobalPosition += new Vector3(0f, 0.35f * dt, 0f);
			return;
		}

		Vector3 toTarget = _gauntlet.AbsorbPoint - GlobalPosition;
		float distance = toTarget.Length();

		// 「深吸」时强行拉得更急：这是它比自动牵引强的地方（03 §6.1）。
		float accel = _gauntlet.IsDeepAbsorbing ? Acceleration * 3f : Acceleration;

		_velocity += toTarget.Normalized() * accel * dt;
		if (_velocity.Length() > MaxFlySpeed)
			_velocity = _velocity.Normalized() * MaxFlySpeed;

		GlobalPosition += _velocity * dt;

		if (distance <= AbsorbRadius)
		{
			ConsumedByDeepAbsorb = _gauntlet.IsDeepAbsorbing;
			_gauntlet.Absorb(this);
			QueueFree();
		}
	}

	private OniGauntlet? FindGauntlet()
	{
		foreach (Node node in GetTree().GetNodesInGroup(OniGauntlet.GroupName))
		{
			if (node is OniGauntlet gauntlet)
				return gauntlet;
		}

		return null;
	}
}
