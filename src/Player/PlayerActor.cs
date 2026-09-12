using System;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Dev;

namespace Oniblade.Player;

/// <summary>
/// 玩家。它只负责两件事：**把输入翻译成意图**，以及**把状态翻译成画面**。
/// 判定、伤害、体干全部交给 <see cref="CombatActor"/> 与裁决器。
/// </summary>
public partial class PlayerActor : CombatActor
{
	private static readonly PlayerAction[] WatchedActions = Enum.GetValues<PlayerAction>();

	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float MinPitch { get; set; } = -70f;
	[Export] public float MaxPitch { get; set; } = 40f;

	/// <summary>招式表（data/attacks/player/player_combo.tres）。</summary>
	[Export] public PlayerAttackSet? Attacks { get; set; }

	/// <summary>难度档。弹开窗、输入缓冲这些宽容参数都从它读，不许写死在代码里。</summary>
	[Export] public DifficultyProfile? Difficulty { get; set; }

	[Export] public Color BodyColor { get; set; } = new(0.22f, 0.26f, 0.34f);
	[Export] public Color AccentColor { get; set; } = new(0.55f, 0.16f, 0.14f);

	/// <summary>玩家不会被一闪秒杀（01 文档：任何机制都不该一击终结玩家）。</summary>
	public override EnemyTier IssenTier => EnemyTier.Boss;

	private Node3D _cameraPivot = null!;
	private SpringArm3D _springArm = null!;
	private BlockoutRig _rig = null!;
	private readonly PlayerInputBuffer _buffer = new();

	public int InputBufferFrames => Difficulty?.InputBufferFrames ?? 8;

	public override string InputBufferDebug
	{
		get
		{
			var sb = new System.Text.StringBuilder();
			foreach (PlayerAction action in WatchedActions)
			{
				int age = _buffer.Age(action);
				if (age < 0)
					continue;

				if (sb.Length > 0)
					sb.Append(", ");
				sb.Append(action.ToString().ToLowerInvariant()).Append('(').Append(age).Append(')');
			}

			return sb.ToString();
		}
	}

	protected override void OnActorReady()
	{
		_cameraPivot = GetNode<Node3D>("CameraPivot");
		_springArm = GetNode<SpringArm3D>("CameraPivot/SpringArm3D");
		_cameraPivot.Position = new Vector3(0f, 1.45f, 0f);
		_springArm.RotationDegrees = new Vector3(-12f, 0f, 0f);

		_rig = new BlockoutRig();
		_rig.Build(BodyColor, AccentColor, true);
		AddChild(_rig);

		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		if (Attacks is not null)
			Machine.Get<AttackState>().Configure(Attacks.BuildLightCombo());

		Input.MouseMode = Input.MouseModeEnum.Captured;
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

	/// <summary>
	/// 在顿帧判断**之前**采集输入（见 <see cref="CombatActor._PhysicsProcess"/>）：
	/// 顿帧期间玩家提前按下的攻击必须进缓冲，否则会被"卡帧"吞掉，
	/// 那正是玩家最容易觉得"我明明按了"的时刻。
	/// </summary>
	protected override void PollLocalInput()
	{
		_buffer.Tick();

		if (Input.IsActionJustPressed("attack"))
			_buffer.Push(PlayerAction.Attack);
		if (Input.IsActionJustPressed("guard"))
			_buffer.Push(PlayerAction.Guard);
		if (Input.IsActionJustPressed("dodge"))
			_buffer.Push(PlayerAction.Dodge);
	}

	public override bool ConsumeAttackInput() => _buffer.Consume(PlayerAction.Attack, InputBufferFrames);

	public override bool TryGetMoveIntent(out MoveIntent intent)
	{
		Vector2 raw = Input.GetVector("move_left", "move_right", "move_back", "move_forward");

		Vector3 forward = -_cameraPivot.GlobalTransform.Basis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();

		Vector3 right = _cameraPivot.GlobalTransform.Basis.X;
		right.Y = 0f;
		right = right.Normalized();

		Vector3 direction = right * raw.X + forward * raw.Y;
		if (direction.LengthSquared() > 1f)
			direction = direction.Normalized();

		intent = new MoveIntent
		{
			Direction = direction,
			Sprint = Input.IsActionPressed("sprint") && raw.Y > 0.1f,
		};

		return direction.LengthSquared() > 0.0001f;
	}

	public override void OnAttackStarted(AttackData data) => _rig.PlayAttack();

	protected override void OnTickVisual(float dt, float speed01)
	{
		_rig.AnimateLocomotion(speed01, dt);
		_rig.AnimateCombat(dt);
	}

	protected override void OnDamaged(int damage) => _rig.PlayHitReact(1f);

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		if (result.Verdict is Combat.Verdict.Block or Combat.Verdict.Deflect or Combat.Verdict.Clash)
			_rig.PlayHitReact(0.5f);
	}
}
