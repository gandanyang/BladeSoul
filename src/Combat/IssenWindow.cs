namespace Oniblade.Combat;

/// <summary>按下攻击键、且附近确实有敌人正在挥刀时，这一次按键被解释成哪一种。</summary>
public enum IssenIntent
{
	/// <summary>不是一闪尝试（附近没有正在挥刀的敌人）→ 放行走普通攻击。</summary>
	NotAnAttempt = 0,

	/// <summary>真一闪成立：敌人距离命中还剩 0~N 帧。</summary>
	Issen = 1,

	/// <summary>★ 安全窗：按早了（N ~ N+安全帧）→ 不算一闪，但**自动转格挡姿态、不挨打**。</summary>
	SafeGuard = 2,

	/// <summary>太早：比安全窗还早。</summary>
	TooEarly = 3,

	/// <summary>太晚：敌人的判定帧已经开始，刀已经在身上了。</summary>
	TooLate = 4,
}

/// <summary>敌人此刻处于攻击动作的哪一段。</summary>
public enum ThreatPhase
{
	None = 0,
	Windup = 1,
	Active = 2,
	Recovery = 3,
}

/// <summary>
/// 真一闪的判定（纯逻辑，可单测）。02 §2.3 / §4。
///
/// 实现口径是 02 §4 定的「**输入时捕获意图，判定帧结算结果**」：
/// 按下的那一瞬间算一次，把结果记成 buff 或状态；真正的伤害由裁决器在
/// 敌人的刀落下来那一帧按规则 2 结算。所以这里只回答"这一次按键算什么"，
/// 不碰任何伤害或体干。
///
/// **为什么安全窗比一闪本身更重要**（T20 卡片原话）：
/// 它让"按早了"的后果从"被砍死"降级成"这一下白按，但仍然防住了"。
/// 手残玩家可以放心对敌人乱按攻击键，代价只是打不出一闪——而不是送命。
/// 这条边界如果错了（比如按早了直接挨打），整个防劝退设计就塌了，
/// 而它在试玩里的表现只是"怎么我老挨打"，极难定位，所以必须由单测钉住。
/// </summary>
public static class IssenWindow
{
	/// <summary>
	/// 判定这一次按键算哪一种。
	/// </summary>
	/// <param name="phase">敌人当前处于攻击动作的哪一段。</param>
	/// <param name="framesUntilActive">
	/// 敌人距离判定帧还有几帧（前摇时 ≥1；判定帧开始之后 ≤0）。
	/// </param>
	/// <param name="issenWindowFrames">
	/// 真一闪窗口 N。**必须由 <see cref="CombatTuning.ResolveIssenWindowFrames"/> 合成后传进来**
	/// （08 §3 P1-3 红线：不许直接读难度档字段）。
	/// </param>
	/// <param name="safeWindowFrames">安全窗长度（<c>DifficultyProfile.IssenSafeWindowFrames</c>）。</param>
	public static IssenIntent Evaluate(
		ThreatPhase phase,
		int framesUntilActive,
		int issenWindowFrames,
		int safeWindowFrames)
	{
		switch (phase)
		{
			case ThreatPhase.Windup:
				if (framesUntilActive <= issenWindowFrames)
					return IssenIntent.Issen;

				if (framesUntilActive <= issenWindowFrames + safeWindowFrames)
					return IssenIntent.SafeGuard;

				return IssenIntent.TooEarly;

			// 判定帧已经开始 → 太晚。这一下只能认了（有代价，但不是即死）。
			case ThreatPhase.Active:
				return IssenIntent.TooLate;

			// 收招段**不算尝试**，这是刻意的：
			// 02 §10 要求"敌人连段结束后必定有 ≥20 帧空隙给玩家反打"。
			// 如果在这个空隙里按攻击也被判成"落空吃 30 帧硬直"，
			// 就等于系统在惩罚设计上明确要奖励的行为。
			default:
				return IssenIntent.NotAnAttempt;
		}
	}

	/// <summary>
	/// 授予真一闪 buff 的时长（帧）。
	///
	/// 必须活到**敌人的刀真正落下来那一帧**：按下那一帧是 F，敌人在 F+f 进入判定帧，
	/// 而 <c>CombatActor.TickTimers</c> 在授予的**同一帧**就会先扣掉 1
	/// （<c>_PhysicsProcess</c> 里 PollLocalInput 在 TickTimers 之前），
	/// 所以到 F+f 时剩余帧数是 <c>n-1-f</c>。要让它 ≥1，就必须 <c>n ≥ f+2</c>。
	/// </summary>
	public static int BuffFramesFor(int framesUntilActive) => framesUntilActive + 2;
}
