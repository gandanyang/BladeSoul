using Godot;
using Oniblade.Combat;

namespace Oniblade.Core;

/// <summary>
/// 坠落保险（02 §2.2 的兜底）：任何战斗单位掉到 <see cref="KillY"/> 以下就触发原地重开。
///
/// **为什么需要它**：道场南墙留了门洞，门外没有地板。没有这个节点，
/// 玩家走出门后会一直下坠——既不受伤也不死亡，重开协议永远不会被触发。
///
/// 它只做"发现坠落 → 交给 <see cref="BattleReset"/>"，不自己复写位置，
/// 免得和 T14 的重开协议长出两套复位逻辑。
/// </summary>
public partial class FallReset : Node
{
	/// <summary>低于这个世界 Y 坐标即视为坠落。地板在 y≈0，留足余量。</summary>
	[Export] public float KillY { get; set; } = -8f;

	/// <summary>重开后静默若干帧，避免在复活演出期间重复触发。</summary>
	[Export] public int CooldownFrames { get; set; } = 30;

	private int _cooldown;

	public override void _PhysicsProcess(double delta)
	{
		if (_cooldown > 0)
		{
			_cooldown--;
			return;
		}

		bool anyFallen = false;
		foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
		{
			if (node is CombatActor actor && actor.GlobalPosition.Y < KillY)
			{
				anyFallen = true;
				break;
			}
		}

		if (!anyFallen)
			return;

		if (BattleReset.Instance is { } reset)
			reset.RestartBattle();
		else
			FallbackTeleport();

		_cooldown = CooldownFrames;
	}

	/// <summary>场景里没放 <see cref="BattleReset"/> 时的兜底：至少把人拉回出生点。</summary>
	private void FallbackTeleport()
	{
		foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
		{
			if (node is CombatActor actor && actor.GlobalPosition.Y < KillY)
				actor.ResetForBattle();
		}
	}
}
