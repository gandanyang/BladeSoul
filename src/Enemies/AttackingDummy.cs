using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Dev;

namespace Oniblade.Enemies;

/// <summary>
/// 挥砍假人（05 文档 §4.4：道场第一个训练模块）。
///
/// 它存在的理由只有一个：**弹开没法对着不还手的木桩练**。
/// 所以在木桩的基础上加了"会按固定节奏砍过来"这一件事，别的什么都不加——
/// 不追人、不变招、不读玩家输入（02 §10 铁律）。它是一台节拍器，
/// 玩家对着它练的是"在刀落下的瞬间按下防御"。
///
/// 出招**复用 <see cref="AttackState"/>**：帧数据全部来自
/// <c>data/attacks/enemies/grunt_slash.tres</c>（前摇 24 / 判定 4 / 后摇 30），
/// 这里一行帧数都不写。
/// </summary>
public partial class AttackingDummy : CombatActor
{
	/// <summary>唯一的那一招。场景里指向 <c>grunt_slash.tres</c>。</summary>
	[Export] public AttackData? Attack { get; set; }

	/// <summary>两次攻击之间的最小间隔（帧）。02 §10 要求 ≥45，默认 60。</summary>
	[Export] public int AttackIntervalFrames { get; set; } = 60;

	/// <summary>发动攻击的水平距离（米）。</summary>
	[Export] public float AttackRange { get; set; } = 2.2f;

	/// <summary>训练靶不该被打死（T7 硬约束）。</summary>
	[Export] public bool Invincible { get; set; } = true;

	[Export] public Color BodyColor { get; set; } = new(0.38f, 0.22f, 0.2f);
	[Export] public Color AccentColor { get; set; } = new(0.2f, 0.14f, 0.12f);

	private readonly List<int> _attackFrames = new(32);
	private BlockoutRig _rig = null!;
	private Node3D? _target;
	private int _cooldownFrames;

	/// <summary>每次出招的物理帧号（按发动顺序）。无头训练测试靠它证明"它真的在按节奏出招"。</summary>
	public IReadOnlyList<int> AttackFrames => _attackFrames;

	public int AttackCount => _attackFrames.Count;

	/// <summary>冷却还剩几帧（调试用）。</summary>
	public int CooldownFramesLeft => _cooldownFrames;

	protected override void OnActorReady()
	{
		_rig = new BlockoutRig();
		_rig.Build(BodyColor, AccentColor, true);
		AddChild(_rig);

		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		// 复用现成的出招状态，不为"会还手的假人"再写一套。
		if (Attack is not null)
			Machine.Get<AttackState>().Configure(new[] { Attack });
	}

	/// <summary>
	/// 出招意图（<see cref="CombatActor.WantsToAttack"/> 钩子）。
	/// 只回答"冷却结束了吗、玩家在刀能砍到的距离里吗"，**不看玩家按了什么**。
	/// </summary>
	public override bool WantsToAttack() => _cooldownFrames == 0 && IsTargetInRange();

	public override void OnAttackStarted(AttackData data)
	{
		_attackFrames.Add((int)Engine.GetPhysicsFrames());
		_rig.PlayAttack();
	}

	/// <summary>攻击结束才起算冷却，这样"间隔"说的是两次**发动**之间的间隔。</summary>
	public override void OnAttackEnded() => _cooldownFrames = AttackIntervalFrames;

	protected override void OnTickVisual(float dt, float speed01)
	{
		if (_cooldownFrames > 0)
			_cooldownFrames--;

		FaceTarget(dt);

		// 站桩：不做走路动画，免得看起来像要追人。
		_rig.AnimateLocomotion(0f, dt);
		_rig.AnimateCombat(dt);
	}

	protected override void OnDamaged(int damage) => _rig.PlayHitReact(1f);

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		if (result.Verdict is Combat.Verdict.Block or Combat.Verdict.Deflect or Combat.Verdict.Clash)
			_rig.PlayHitReact(0.5f);
	}

	/// <summary>体干破裂后立刻回满：它是节拍器，被连段打乱节奏就没法练了。</summary>
	protected override void OnPostureBroken() => Posture.Reset();

	public override void Die()
	{
		if (Invincible)
		{
			Health.Heal(Health.Max);
			return;
		}

		base.Die();
	}

	/// <summary>始终转向玩家，但一步都不走（站桩）。</summary>
	private void FaceTarget(float dt)
	{
		Node3D? target = ResolveTarget();
		if (target is null)
			return;

		Vector3 toTarget = target.GlobalPosition - GlobalPosition;
		toTarget.Y = 0f;

		if (toTarget.LengthSquared() <= 0.0001f)
			return;

		float targetYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);
		float yaw = Mathf.LerpAngle(Rotation.Y, targetYaw, Mathf.Min(1f, TurnSpeed * dt));
		Rotation = new Vector3(0f, yaw, 0f);
	}

	private bool IsTargetInRange()
	{
		Node3D? target = ResolveTarget();
		if (target is null)
			return false;

		Vector3 delta = target.GlobalPosition - GlobalPosition;
		delta.Y = 0f;

		return delta.LengthSquared() <= AttackRange * AttackRange;
	}

	/// <summary>
	/// 玩家从 Godot 组 <c>player</c> 里找（与 <see cref="EnemyController"/> 同一个约定）。
	/// 找到就缓存，别每帧去问场景树。
	/// </summary>
	private Node3D? ResolveTarget()
	{
		if (_target is not null && IsInstanceValid(_target))
			return _target;

		_target = null;
		foreach (Node node in GetTree().GetNodesInGroup("player"))
		{
			if (node is Node3D player)
			{
				_target = player;
				break;
			}
		}

		return _target;
	}
}
