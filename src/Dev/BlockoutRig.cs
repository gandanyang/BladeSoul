using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 灰盒阶段用的程序化"火柴人"。
/// 存在的唯一目的：在真实模型/动画到位之前（M7 才换），
/// 让帧数据、判定框、手感能先跑起来。**不要往这里加任何玩法逻辑。**
/// </summary>
public partial class BlockoutRig : Node3D
{
	public Node3D HeadPivot { get; private set; } = null!;
	public Node3D ArmLeft { get; private set; } = null!;
	public Node3D ArmRight { get; private set; } = null!;
	public Node3D LegLeft { get; private set; } = null!;
	public Node3D LegRight { get; private set; } = null!;
	public Node3D WeaponPivot { get; private set; } = null!;

	private Node3D _body = null!;
	private float _locomotionPhase;
	private float _attackElapsed = -1f;
	private float _hitReactElapsed = -1f;
	private float _hitReactStrength;
	private const float AttackDuration = 0.5f;
	private const float HitReactDuration = 0.22f;

	public void Build(Color primary, Color accent, bool weapon)
	{
		_body = new Node3D { Name = "Body" };
		AddChild(_body);

		Material bodyMat = MakeMaterial(primary, 0.6f);
		Material accentMat = MakeMaterial(accent, 0.45f);
		Material skinMat = MakeMaterial(primary.Lightened(0.3f), 0.85f);

		AddBox(_body, "Torso", new Vector3(0.42f, 0.58f, 0.24f), new Vector3(0f, 1.12f, 0f), bodyMat);
		AddBox(_body, "ShoulderPlate", new Vector3(0.54f, 0.12f, 0.24f), new Vector3(0f, 1.38f, 0f), accentMat);
		AddBox(_body, "Hips", new Vector3(0.34f, 0.2f, 0.22f), new Vector3(0f, 0.84f, 0f), accentMat);

		HeadPivot = new Node3D { Name = "HeadPivot", Position = new Vector3(0f, 1.46f, 0f) };
		_body.AddChild(HeadPivot);
		AddCapsule(HeadPivot, "Head", 0.15f, 0.34f, new Vector3(0f, 0.18f, 0f), skinMat);

		ArmLeft = AddLimb("ArmLeft", new Vector3(-0.3f, 1.34f, 0f), 0.07f, 0.56f, bodyMat, skinMat, true);
		ArmRight = AddLimb("ArmRight", new Vector3(0.3f, 1.34f, 0f), 0.07f, 0.56f, bodyMat, skinMat, true);
		LegLeft = AddLimb("LegLeft", new Vector3(-0.12f, 0.8f, 0f), 0.1f, 0.82f, accentMat, skinMat, false);
		LegRight = AddLimb("LegRight", new Vector3(0.12f, 0.8f, 0f), 0.1f, 0.82f, accentMat, skinMat, false);

		if (weapon)
			BuildKatana();
	}

	public void AnimateLocomotion(float speed01, float delta)
	{
		speed01 = Mathf.Clamp(speed01, 0f, 1.5f);
		_locomotionPhase += delta * Mathf.Lerp(2.5f, 10f, Mathf.Min(speed01, 1f));
		float swing = Mathf.Sin(_locomotionPhase) * speed01;

		LegLeft.Rotation = new Vector3(swing * 0.8f, 0f, 0f);
		LegRight.Rotation = new Vector3(-swing * 0.8f, 0f, 0f);

		if (_attackElapsed < 0f)
		{
			ArmLeft.Rotation = new Vector3(-swing * 0.5f, 0f, -0.1f);
			ArmRight.Rotation = new Vector3(swing * 0.5f, 0f, 0.1f);
		}

		float bob = Mathf.Abs(Mathf.Sin(_locomotionPhase)) * 0.035f * speed01;
		_body.Position = new Vector3(0f, bob, 0f);
	}

	public void PlayAttack()
	{
		_attackElapsed = 0f;
	}

	/// <summary>
	/// 受击反馈：后仰 + 抖动。灰盒阶段唯一能让人"感觉到打中了"的东西，
	/// 所以它必须存在——没有它，命中就只是一个数字变化。
	/// </summary>
	public void PlayHitReact(float strength)
	{
		_hitReactElapsed = 0f;
		_hitReactStrength = Mathf.Clamp(strength, 0f, 2f);
	}

	public void AnimateCombat(float delta)
	{
		if (_hitReactElapsed >= 0f)
		{
			_hitReactElapsed += delta;
			float reactT = Mathf.Clamp(_hitReactElapsed / HitReactDuration, 0f, 1f);
			float falloff = (1f - reactT) * (1f - reactT) * _hitReactStrength;

			_headPivotLean = -0.55f * falloff;
			_bodyShake = new Vector3(Mathf.Sin(_hitReactElapsed * 90f) * 0.05f * falloff, 0f, 0f);

			HeadPivot.Rotation = new Vector3(_headPivotLean, 0f, 0f);
			_body.Position = _bodyShake;
			_body.Rotation = new Vector3(0f, 0f, Mathf.Sin(_hitReactElapsed * 70f) * 0.12f * falloff);

			if (reactT >= 1f)
			{
				_hitReactElapsed = -1f;
				HeadPivot.Rotation = Vector3.Zero;
				_body.Rotation = Vector3.Zero;
			}
		}

		if (_attackElapsed < 0f)
			return;

		_attackElapsed += delta;
		float t = Mathf.Clamp(_attackElapsed / AttackDuration, 0f, 1f);
		float eased = 1f - Mathf.Pow(1f - t, 3f);

		ArmRight.Rotation = new Vector3(Mathf.Lerp(-2.4f, 1.5f, eased), 0f, 0.1f);
		ArmLeft.Rotation = new Vector3(Mathf.Lerp(0.2f, -0.4f, eased), 0f, -0.1f);

		if (t >= 1f)
			_attackElapsed = -1f;
	}

	private float _headPivotLean;
	private Vector3 _bodyShake;

	private Node3D AddLimb(string name, Vector3 origin, float radius, float length, Material limbMat, Material tipMat, bool isArm)
	{
		var pivot = new Node3D { Name = name, Position = origin };
		_body.AddChild(pivot);
		AddCapsule(pivot, "Limb", radius, length, new Vector3(0f, -length * 0.5f, 0f), limbMat);

		var tip = new Node3D { Name = isArm ? "Hand" : "Foot", Position = new Vector3(0f, -length, 0f) };
		pivot.AddChild(tip);
		AddSphere(tip, "TipMesh", radius * 1.25f, Vector3.Zero, tipMat);
		return pivot;
	}

	private void BuildKatana()
	{
		var hand = ArmRight.GetNode<Node3D>("Hand");
		WeaponPivot = new Node3D { Name = "WeaponPivot", RotationDegrees = new Vector3(-72f, 0f, 0f) };
		hand.AddChild(WeaponPivot);

		Material steel = MakeMaterial(new Color(0.78f, 0.8f, 0.85f), 0.25f);
		Material grip = MakeMaterial(new Color(0.08f, 0.08f, 0.09f), 0.7f);
		Material guard = MakeMaterial(new Color(0.45f, 0.35f, 0.12f), 0.4f);

		AddBox(WeaponPivot, "Handle", new Vector3(0.035f, 0.2f, 0.035f), new Vector3(0f, -0.06f, 0f), grip);
		AddBox(WeaponPivot, "Guard", new Vector3(0.14f, 0.025f, 0.06f), new Vector3(0f, 0.05f, 0f), guard);
		AddBox(WeaponPivot, "Blade", new Vector3(0.045f, 0.98f, 0.016f), new Vector3(0f, 0.55f, 0f), steel);
	}

	private MeshInstance3D AddCapsule(Node3D parent, string name, float radius, float height, Vector3 position, Material material)
	{
		var mesh = new CapsuleMesh { Radius = radius, Height = Mathf.Max(height, radius * 2.01f), RadialSegments = 12, Rings = 4 };
		return AddMesh(parent, name, mesh, position, material);
	}

	private MeshInstance3D AddBox(Node3D parent, string name, Vector3 size, Vector3 position, Material material)
	{
		return AddMesh(parent, name, new BoxMesh { Size = size }, position, material);
	}

	private MeshInstance3D AddSphere(Node3D parent, string name, float radius, Vector3 position, Material material)
	{
		return AddMesh(parent, name, new SphereMesh { Radius = radius, Height = radius * 2f }, position, material);
	}

	private MeshInstance3D AddMesh(Node3D parent, string name, Mesh mesh, Vector3 position, Material material)
	{
		var instance = new MeshInstance3D { Name = name, Mesh = mesh, Position = position, MaterialOverride = material };
		parent.AddChild(instance);
		return instance;
	}

	private static Material MakeMaterial(Color color, float roughness)
	{
		return new StandardMaterial3D { AlbedoColor = color, Roughness = roughness };
	}
}
