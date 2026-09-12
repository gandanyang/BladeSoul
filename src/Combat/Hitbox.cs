using System.Collections.Generic;
using Godot;
using Oniblade.Combat.Data;
using Oniblade.Core;

namespace Oniblade.Combat;

/// <summary>
/// 攻击判定框。**它不参与物理**，只持有一个 <see cref="Shape3D"/>，
/// 在判定帧里主动发起一次同步查询。
///
/// 为什么不用 Area3D 信号（04 文档 §7）：
/// 信号回调顺序不确定、且 `GetOverlappingAreas()` 返回的是上一物理帧的状态。
/// 对帧数据驱动的动作游戏，1 帧延迟是致命的，顺序不确定更是灾难。
/// </summary>
public partial class Hitbox : Node3D
{
	[Export] public Shape3D? Shape { get; set; }

	/// <summary>要打的受击框所在的物理层（玩家打 EnemyHurtbox = 16）。</summary>
	[Export] public uint TargetMask { get; set; } = 16;

	[Export] public int MaxTargets { get; set; } = 8;

	public CombatActor Actor { get; private set; } = null!;
	public AttackData? Data { get; private set; }
	public bool IsActiveThisFrame { get; set; }

	private CombatArbiter? _arbiter;
	private bool _registered;

	/// <summary>本次挥砍已经打中过谁——防止同一次判定在多个判定帧里重复结算。</summary>
	private readonly List<CombatActor> _hitThisSwing = new(4);

	public override void _EnterTree() => AddToGroup("hitbox");

	public override void _Ready()
	{
		Actor = FindOwnerActor();
		TryRegister();
	}

	public override void _ExitTree()
	{
		if (_registered)
		{
			_arbiter?.Unregister(this);
			_registered = false;
		}
	}

	public void SetAttack(AttackData? data)
	{
		Data = data;

		// 招式自带判定形状时优先用它；否则用节点上配的（灰盒阶段走这条）。
		if (data?.HitShape is { } shape)
			Shape = shape;
	}

	public void SetActive(bool active)
	{
		if (active && !IsActiveThisFrame)
			_hitThisSwing.Clear();

		IsActiveThisFrame = active;
	}

	public bool AlreadyHit(CombatActor actor) => _hitThisSwing.Contains(actor);

	public void MarkHit(CombatActor actor)
	{
		if (!_hitThisSwing.Contains(actor))
			_hitThisSwing.Add(actor);
	}

	/// <summary>同步形状查询。结果写进 <paramref name="buffer"/>（会被清空后复用，避免每帧 GC）。</summary>
	public int Query(List<Hurtbox> buffer)
	{
		buffer.Clear();

		if (!_registered)
			TryRegister();

		if (Shape is null || Actor is null || !IsActiveThisFrame)
			return 0;

		PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

		var parameters = new PhysicsShapeQueryParameters3D
		{
			Shape = Shape,
			Transform = GlobalTransform,
			CollisionMask = TargetMask,
			CollideWithAreas = true,
			CollideWithBodies = false,
		};

		Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(parameters, MaxTargets);

		int count = 0;
		foreach (Godot.Collections.Dictionary hit in hits)
		{
			if (hit["collider"].As<GodotObject>() is not Hurtbox hurt)
				continue;
			if (hurt.OwnerActor is null || hurt.OwnerActor == Actor)
				continue;

			buffer.Add(hurt);
			count++;
		}

		return count;
	}

	private void TryRegister()
	{
		if (_registered)
			return;

		_arbiter ??= CombatArbiter.Instance ?? GetTree().GetFirstNodeInGroup("combat_arbiter") as CombatArbiter;
		if (_arbiter is null)
			return;

		_arbiter.Register(this);
		_registered = true;
	}

	private CombatActor FindOwnerActor()
	{
		Node? node = GetParent();
		while (node is not null)
		{
			if (node is CombatActor actor)
				return actor;
			node = node.GetParent();
		}

		GD.PushError($"[Hitbox] {GetPath()} 没有挂在 CombatActor 下面");
		return null!;
	}
}
