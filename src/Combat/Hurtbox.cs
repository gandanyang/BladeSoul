using Godot;

namespace Oniblade.Combat;

/// <summary>
/// 受击框。它**只作为一个"可以被我查询到"的标记存在**：
/// `Monitoring = false`（自己不主动检测别人）、`Monitorable = true`（别人能查到我）。
///
/// 判定由攻方的 <see cref="Hitbox"/> 主动发起，方向是单向的、顺序是确定的。
/// </summary>
public partial class Hurtbox : Area3D
{
	[Export] public float DamageMultiplier { get; set; } = 1f;

	/// <summary>弱点（打中伤害更高）。M2 之后再接。</summary>
	[Export] public bool IsWeakPoint { get; set; }

	public CombatActor OwnerActor { get; private set; } = null!;

	public override void _EnterTree() => AddToGroup("hurtbox");

	public override void _Ready()
	{
		Monitoring = false;
		Monitorable = true;
		OwnerActor = FindOwnerActor();
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

		GD.PushError($"[Hurtbox] {GetPath()} 没有挂在 CombatActor 下面");
		return null!;
	}
}
