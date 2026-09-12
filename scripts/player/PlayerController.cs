using Godot;
using Oniblade.Common;

namespace Oniblade.Player;

public partial class PlayerController : CharacterBody3D
{
	[Export] public float WalkSpeed { get; set; } = 3.6f;
	[Export] public float SprintSpeed { get; set; } = 6.4f;
	[Export] public float Acceleration { get; set; } = 14f;
	[Export] public float JumpVelocity { get; set; } = 5.2f;
	[Export] public float TurnSpeed { get; set; } = 12f;
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float MinPitch { get; set; } = -70f;
	[Export] public float MaxPitch { get; set; } = 40f;

	private float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	private Node3D _cameraPivot;
	private SpringArm3D _springArm;
	private BlockoutCharacter _rig;

	public override void _Ready()
	{
		AddToGroup("player");
		Input.MouseMode = Input.MouseModeEnum.Captured;

		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
		_cameraPivot.Position = new Vector3(0f, 1.45f, 0f);
		_springArm.RotationDegrees = new Vector3(-12f, 0f, 0f);

		_rig = new BlockoutCharacter();
		_rig.Build(new Color(0.22f, 0.26f, 0.34f), new Color(0.55f, 0.16f, 0.14f), true);
		AddChild(_rig);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_cameraPivot.RotateY(-motion.Relative.X * MouseSensitivity);
			_springArm.RotateX(-motion.Relative.Y * MouseSensitivity);

			Vector3 springRotation = _springArm.Rotation;
			springRotation.X = Mathf.Clamp(springRotation.X, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			_springArm.Rotation = springRotation;
		}

		if (@event.IsActionPressed("pause"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		Vector3 velocity = Velocity;

		if (!IsOnFloor())
			velocity.Y -= _gravity * dt;
		else if (Input.IsActionJustPressed("jump"))
			velocity.Y = JumpVelocity;

		Vector2 input = Input.GetVector("move_left", "move_right", "move_back", "move_forward");

		Vector3 forward = -_cameraPivot.GlobalBasis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();
		Vector3 right = _cameraPivot.GlobalBasis.X;
		right.Y = 0f;
		right = right.Normalized();

		Vector3 desiredDir = right * input.X + forward * input.Y;
		if (desiredDir.LengthSquared() > 1f)
			desiredDir = desiredDir.Normalized();

		bool sprinting = Input.IsActionPressed("sprint") && input.Y > 0.1f;
		float targetSpeed = desiredDir == Vector3.Zero ? 0f : (sprinting ? SprintSpeed : WalkSpeed);
		Vector3 targetVelocity = desiredDir * targetSpeed;

		velocity.X = Mathf.MoveToward(velocity.X, targetVelocity.X, Acceleration * dt);
		velocity.Z = Mathf.MoveToward(velocity.Z, targetVelocity.Z, Acceleration * dt);

		float yaw = Rotation.Y;
		if (desiredDir.LengthSquared() > 0.001f)
		{
			float targetYaw = Mathf.Atan2(-desiredDir.X, -desiredDir.Z);
			yaw = Mathf.LerpAngle(yaw, targetYaw, Mathf.Min(1f, TurnSpeed * dt));
		}

		Velocity = velocity;
		MoveAndSlide();
		Rotation = new Vector3(0f, yaw, 0f);

		float speed01 = new Vector2(velocity.X, velocity.Z).Length() / SprintSpeed;
		_rig.AnimateLocomotion(speed01, dt);
		_rig.AnimateCombat(dt);
	}
}
