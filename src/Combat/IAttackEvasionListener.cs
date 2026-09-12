namespace Oniblade.Combat;

/// <summary>
/// 攻方的判定被**完全躲开**（裁决器返回 <see cref="Verdict.Miss"/>）时的通知口。
///
/// 为什么需要它：<c>CombatActor.ReceiveVerdict</c> 在 <c>Miss</c> 时**直接返回**
/// （Miss 连顿帧都不给，否则玩家会以为打中了），所以 <c>OnVerdictReceived</c> 收不到它。
/// 而"完美闪避"（02 §2.3 / §4）恰恰只能从 Miss 得知——
/// 没有这个通知口，闪避的奖励闭环就是断的：玩家躲开了攻击，系统却不知道。
///
/// 为什么是接口而不是给 <see cref="CombatActor"/> 加虚方法：
/// T13 的硬约束把 <c>CombatActor.cs</c> 列为冻结（另一个 agent 正在改它），
/// 而且目前**只有玩家**需要这个通知（敌人的闪避是 M3 之后的事）。
/// 这与 <see cref="States.IGuardInput"/> 是同一个理由、同一个做法：
/// 能力定义在 Combat 侧，实现留给具体的战斗单位。
/// </summary>
public interface IAttackEvasionListener
{
	/// <summary>
	/// 本帧有一次攻击打到我身上、但裁决结果是 <c>Miss</c>。
	/// <paramref name="attacker"/> 是**被我躲开的那个人**，不会被置空。
	/// </summary>
	void OnAttackEvaded(CombatActor attacker);
}
