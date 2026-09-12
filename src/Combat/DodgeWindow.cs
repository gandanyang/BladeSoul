using System;

namespace Oniblade.Combat;

/// <summary>
/// 闪避的"无敌帧什么时候结束"（纯逻辑，可单测）。
///
/// 为什么单独抽出来：无敌帧边界是**唯一**能决定"这一刀算不算躲开"的东西，
/// 而它同时要喂两处——<c>CombatActor.GetDefenderSnapshot.IsInvulnerable</c>（裁决器规则 1）
/// 和"完美闪避"的奖励判定。混在状态类里就只能靠"玩一玩感觉对不对"来验证，
/// 而这类边界错误的表现是"偶尔擦伤"，恰恰是手感问题里最难定位的一种。
///
/// 与 <see cref="GuardWindowState"/> 的差别（不是笔误，是刻意的）：
/// 防御窗**不在进入那一帧打开**（GuardState 注释里有理由），
/// 闪避的无敌帧**必须包含进入那一帧**——玩家在刀落下的同一帧按下闪避，
/// 那一下就该算躲开。所以本类的第 0 帧（<c>FramesSinceStart == 0</c>）就是无敌的。
/// </summary>
public sealed class DodgeWindow
{
	private int _framesSinceStart;

	/// <summary>难度档给的无敌帧数（武士 8）。这是**宣传口径**的无敌长度。</summary>
	public int InvulnerableFrames { get; private set; }

	/// <summary>完美闪避宽容帧数（02 §8：无敌帧结束后 3 帧内仍算完美闪避）。</summary>
	public int PerfectDodgeGraceFrames { get; private set; }

	/// <summary>后摇帧数（武士 18），可被防御取消。</summary>
	public int RecoveryFrames { get; private set; }

	/// <summary>进入闪避后已经过到第几帧（进入那一帧记为 0）。</summary>
	public int FramesSinceStart => _framesSinceStart;

	/// <summary>整段闪避的总帧数 = 无敌帧 + 后摇。</summary>
	public int TotalFrames => InvulnerableFrames + RecoveryFrames;

	public void Begin(int invulnerableFrames, int perfectDodgeGraceFrames, int recoveryFrames)
	{
		InvulnerableFrames = Math.Max(0, invulnerableFrames);
		PerfectDodgeGraceFrames = Math.Max(0, perfectDodgeGraceFrames);
		RecoveryFrames = Math.Max(0, recoveryFrames);
		_framesSinceStart = 0;
	}

	/// <summary>
	/// 本帧判定（在 <see cref="Advance"/> **之前**读）：处于无敌帧。
	/// 为真时裁决器规则 1 直接返回 <c>Miss</c>——**连一闪都打不中**（02 §4）。
	/// </summary>
	public bool IsInvulnerable => _framesSinceStart < InvulnerableFrames;

	/// <summary>
	/// 本帧判定（在 <see cref="Advance"/> **之前**读）：这一帧躲开攻击算不算"完美闪避"。
	///
	/// ⚠️ 规格歧义（已写进完成报告，等制作人裁定）：
	/// 02 §8 说"无敌帧结束后 3 帧内仍算完美闪避"，但 T13 验收 #2 要求
	/// "第 <c>DodgeIFrames</c> 帧之后 → 正常结算"。两条不能同时成立——
	/// 无敌帧之后不再是 <c>Miss</c>，而完美闪避只能由 <c>Miss</c> 触发。
	/// 本实现按验收 #2 执行（<see cref="IsInvulnerable"/> 严格等于 DodgeIFrames），
	/// 因此宽容帧**只放宽判定窗，不放宽无敌**，当前是惰性的。
	/// </summary>
	public bool IsInPerfectDodgeWindow => _framesSinceStart < InvulnerableFrames + PerfectDodgeGraceFrames;

	/// <summary>后摇中（无敌已结束，但整段还没走完）。</summary>
	public bool IsInRecovery =>
		_framesSinceStart >= InvulnerableFrames && _framesSinceStart < TotalFrames;

	/// <summary>整段闪避结束。</summary>
	public bool IsFinished => _framesSinceStart >= TotalFrames;

	/// <summary>这一帧结束了，进入下一帧。</summary>
	public void Advance() => _framesSinceStart++;
}
