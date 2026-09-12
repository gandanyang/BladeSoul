using Godot;
using Oniblade.Common;

namespace Oniblade.Enemies;

public partial class EnemyController : CharacterBody3D
{
	[Export] public float MoveSpeed { get; set; } = 3.2f;
	[Export] public float Acceleration { get; set; } = 9f;
	[Export] public float TurnSpeed { get; set; } = 7f;
	[Export] public float DetectRange { get; set; } = 20f;
	[Export] public float AttackRange { get; set; } = 2.2f;
	[Export] public float AttackCooldown { get; set; } = 1.5f;
	[Export] public Color BodyColor { get; set; } = new Color(0.4f, 0.16f, 0.14f);
	[Export] public Color AccentColor { get; set; } = new Color(0.12f, 0.12f, 0.14f);

	private enum State { Idle, Chase, Attack }

	private State _state = State.Idle;
	private float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	private float _attackTimer;
	private BlockoutCharacter _rig;
	private Node3D _target;

	public override void _Ready()
	{
		AddToGroup("enemy");
		_rig = new BlockoutCharacter();
		_rig.Build(BodyColor, AccentColor, true);
		AddChild(_rig);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_attackTimer = Mathf.Max(0f, _attackTimer - dt);

		if (!IsInstanceValid(_target))
			_target = FindPlayer();

		Vector3 velocity = Velocity;
		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;

		Vector3 toTarget = Vector3.Zero;
		float distance = float.MaxValue;
		if (_target != null)
		{
			toTarget = _target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			distance = toTarget.Length();
		}

		_state = distance > DetectRange ? State.Idle
			: distance <= AttackRange ? State.Attack
			: State.Chase;

		Vector3 desiredVelocity = Vector3.Zero;
		if (_state == State.Chase && distance > 0.01f)
			desiredVelocity = toTarget.Normalized() * MoveSpeed;

		if (_state == State.Attack && _attackTimer <= 0f)
		{
			_attackTimer = AttackCooldown;
			_rig.PlayAttack();
		}

		velocity.X = Mathf.MoveToward(velocity.X, desiredVelocity.X, Acceleration * dt);
		velocity.Z = Mathf.MoveToward(velocity.Z, desiredVelocity.Z, Acceleration * dt);

		float yaw = Rotation.Y;
		if (_state != State.Idle && toTarget.LengthSquared() > 0.01f)
		{
			float targetYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);
			yaw = Mathf.LerpAngle(yaw, targetYaw, Mathf.Min(1f, TurnSpeed * dt));
		}

		Velocity = velocity;
		MoveAndSlide();
		Rotation = new Vector3(0f, yaw, 0f);

		float speed01 = new Vector2(velocity.X, velocity.Z).Length() / MoveSpeed;
		_rig.AnimateLocomotion(speed01, dt);
		_rig.AnimateCombat(dt);
	}

	private Node3D FindPlayer()
	{
		Godot.Collections.Array<Node> players = GetTree().GetNodesInGroup("player");
		foreach (Node node in players)
		{
			if (node is Node3D node3D)
				return node3D;
		}
		return null;
	}
}
