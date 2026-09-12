using Godot;

namespace Oniblade.Combat.States;

/// <summary>
/// 喝血状态需要宿主提供的东西：次数记账 + 实际回血。
///
/// 定义成接口而不是给 <see cref="CombatActor"/> 加方法：T18 的硬约束把
/// <c>CombatActor.cs</c> 列为冻结，而且"有喝血次数"只属于玩家（敌人不喝血）。
/// 与 <see cref="IGuardInput"/> / <see cref="IAttackEvasionListener"/> 同一个理由、同一个做法。
/// </summary>
public interface IHealHost
{
	/// <summary>还剩几次。</summary>
	int HealChargesLeft { get; }

	/// <summary>扣掉一次。只在**跨进饮用段**那一帧调用（起手被打断则不消耗）。</summary>
	void ConsumeHealCharge();

	/// <summary>回复血量，返回**实际**回复量（会被血量上限截断）。</summary>
	int ApplyHeal(int requested);
}

/// <summary>
/// 喝血（02 §2.4 / T18）。三段：起手 → 饮用 → 收招。
///
/// **这个机制的全部意义在于"不背板"**：
/// 只狼的喝血逼玩家记住"BOSS 这一招之后才有安全窗口"，那是**记忆惩罚**，
/// 与本作"让玩家理解规则、把规则玩漂亮"的方向相反。
/// 这里改成——**喝血不会被打断（进入饮用段之后），但来自身后的刀照常掉血**。
/// 于是博弈自然出现：**现在喝，还是再撑三秒？** 而这个判断只依赖"我面前有几个人、
/// 他们离我多近"，不依赖背招。
///
/// 三段边界由 <see cref="HealWindow"/> 负责（纯逻辑、可单测），
/// 因为机制的全部意图都压在"起手可打断 / 饮用不可"这一条线上。
///
/// 参数由输入侧在切入之前写入（与 GuardState / DodgeState 同一用法），
/// 所以本状态不知道 ActorStats 的存在。
/// </summary>
public sealed class HealState : ActorState
{
	private readonly HealWindow _window = new();

	/// <summary>切入前由输入侧写入：三段帧数（<c>ActorStats.Heal*Frames</c>）。</summary>
	public int StartupFrames { get; set; }
	public int DrinkFrames { get; set; }
	public int RecoveryFrames { get; set; }

	/// <summary>切入前由输入侧写入：本次要回复的血量（已由 HealPercent 换算成绝对值）。</summary>
	public int HealAmount { get; set; }

	/// <summary>本次是否已经扣过次数（只扣一次）。</summary>
	public bool ChargeConsumed { get; private set; }

	/// <summary>本次实际回复了多少血（调试面板与端到端测试用）。</summary>
	public int HealedAmount { get; private set; }

	/// <summary>本次是否走到了"喝完"。</summary>
	public bool Completed { get; private set; }

	/// <summary>本帧处于哪一段（端到端测试靠它判断"挨打时是不是在饮用段"）。</summary>
	public HealPhase Phase => _window.Phase;

	public override int TotalFrames => _window.TotalFrames;

	public override void Enter()
	{
		base.Enter();

		_window.Begin(StartupFrames, DrinkFrames, RecoveryFrames);
		ChargeConsumed = false;
		HealedAmount = 0;
		Completed = false;
	}

	/// <summary>
	/// 受击能不能打断这个动作（02 §2.4 的核心规则）。
	///
	/// 只有**起手段**可以被打断。这正是 <see cref="StateMachine.Tick"/> 里
	/// <c>Current.CanTransitionTo(_pending)</c> 那次询问的用途：
	/// <c>CombatActor.ReceiveVerdict</c> 命中时请求 <c>Change&lt;StaggerState&gt;</c>，
	/// 这里拒绝它——而**伤害在请求之前就已经结算完了**，所以是
	/// "不打断动作，但照常掉血"，不是"无敌"。
	///
	/// 注意这也会挡掉体干破裂推出的硬直。T18 卡片明确认可：
	/// 喝血只有 54 帧，而"喝到一半被破防就前功尽弃"会把玩家推回背招。
	/// </summary>
	public override bool CanTransitionTo(ActorState next) =>
		next is not StaggerState || _window.CanBeInterruptedNow;

	public override void Tick()
	{
		base.Tick();

		// 喝血期间站住不动，也不格挡——这是它的时间成本。
		Actor.DesiredVelocity = Vector3.Zero;

		// 跨进饮用段：这一刻才扣次数。
		// 02 §2.4 明文"被打断则本次不消耗次数"，所以扣点必须在起手**之后**。
		if (_window.EnteredDrinkThisFrame)
		{
			ChargeConsumed = true;

			if (Actor is IHealHost drinker)
				drinker.ConsumeHealCharge();
		}

		// 跨进收招段 = "喝完了"：回血在这一刻生效。
		if (_window.EnteredRecoveryThisFrame && !Completed)
		{
			Completed = true;

			if (Actor is IHealHost drinker)
				HealedAmount = drinker.ApplyHeal(HealAmount);
		}

		_window.Advance();

		if (_window.IsFinished)
			ChangeState<IdleState>();
	}
}
