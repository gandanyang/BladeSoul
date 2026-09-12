using Godot;
using Oniblade.Combat;

namespace Oniblade.Core;

/// <summary>
/// 原地重开协议（T14 / 01 §0 规则 1 / 08 §3 P1-2）。
///
/// **它存在的全部理由是"死亡没有持久性惩罚"**：
/// 重载场景会把升级、魄、侵蚀、喝血次数全部抹掉——那其实就是一种惩罚，
/// 只是藏得比较深（玩家感受到的是"我得重新走一遍"）。所以这里改成
/// 把所有战斗单位**原地复位**：位置、朝向、血量、体干、状态机、buff、顿帧，一个不漏；
/// 而玩家的持久资源一个都不动。
///
/// 场景里放一个实例即可（**不需要 Autoload**，因为 T14 不许动 project.godot；
/// 而且没有它的场景里玩家死亡不会自动重开——这对无头训练测试反而是好事，
/// 它们要自己控制死亡时机）。
/// </summary>
public partial class BattleReset : Node
{
	public static BattleReset? Instance { get; private set; }

	/// <summary>已经重开过几次（调试与测试用）。</summary>
	public int RestartCount { get; private set; }

	/// <summary>上一次重开复位了多少个单位。为 0 说明场景里没有战斗单位（多半是放错位置了）。</summary>
	public int LastResetActorCount { get; private set; }

	public override void _EnterTree()
	{
		Instance = this;
		AddToGroup("battle_reset");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	/// <summary>
	/// 把所有战斗单位复位，返回复位的单位数。
	/// **不重载场景、不碰持久资源、不动 <c>Engine.TimeScale</c>**——
	/// 后者是调试面板的职责，混进来会让"重开"和"慢放"互相打架。
	/// </summary>
	public int RestartBattle()
	{
		int count = 0;

		foreach (Node node in GetTree().GetNodesInGroup("combat_actor"))
		{
			if (node is CombatActor actor)
			{
				actor.ResetForBattle();
				count++;
			}
		}

		LastResetActorCount = count;
		RestartCount++;
		return count;
	}
}
