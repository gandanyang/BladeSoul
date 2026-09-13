using System;
using Godot;
using Oniblade.Audio;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;

namespace Oniblade.Combat;

/// <summary>状态机读取的移动意图。玩家从输入读，敌人从 AI 读。</summary>
public struct MoveIntent
{
	/// <summary>世界空间方向，已归一化（或为零）。</summary>
	public Vector3 Direction;

	public bool Sprint;
}

/// <summary>
/// 一切会打架的东西的基类：玩家、敌人、木桩。
///
/// 它只做四件事：**持有血/体干、跑状态机、应用顿帧、把裁决结果落成数值**。
/// 它不裁决（那是 <see cref="CombatResolver"/>）、不查判定框（那是 <see cref="Hitbox"/>）、
/// 不决定谁先谁后（那是 <see cref="Core.CombatArbiter"/>）。
///
/// 顿帧用**逐 actor 冻结**（04 文档 §10）：命中瞬间只有参战双方静止，
/// 世界、粒子、音频继续走——冲击感就来自这个"局部时间停止"。
/// </summary>
public abstract partial class CombatActor : CharacterBody3D, ICombatActorDebug, IDebugCheatable
{
	[Export] public int ActorId { get; set; }
	[Export] public string DebugName { get; set; } = "Actor";
	[Export] public ActorStats? Stats { get; set; }
	[Export] public float TurnSpeed { get; set; } = 12f;

	/// <summary>被打中之后的硬直帧数。</summary>
	[Export] public int HitStunFrames { get; set; } = 18;

	/// <summary>体干破裂（破防）的硬直帧数（02 文档 §5：50 帧）。</summary>
	[Export] public int GuardBreakStunFrames { get; set; } = 50;

	public HealthMeter Health { get; private set; } = null!;
	public PostureMeter Posture { get; private set; } = null!;
	public StateMachine Machine { get; private set; } = null!;
	public Hitbox? PrimaryHitbox { get; protected set; }

	/// <summary>弹开连击数（连着弹开时递增，断连归零）。</summary>
	public int DeflectChain { get; protected set; }

	public IssenKind IssenBuff { get; protected set; } = IssenKind.None;
	public int IssenBuffFramesLeft { get; protected set; }
	public int DeflectWindowFramesLeft { get; protected set; }
	public int HitStopFramesLeft => _hitStopFrames;
	public bool IsDead { get; protected set; }

	/// <summary>F4 调试开关。签名由调试面板（T2）冻结，见 <see cref="IDebugCheatable"/>。</summary>
	public bool DebugInvulnerable { get; set; }

	/// <summary>调试面板的 WINDOWS 区块用；只有玩家会给出内容。</summary>
	public virtual string InputBufferDebug => string.Empty;

	/// <summary>被一闪时按哪个档次结算收益。玩家覆盖成 Boss（永不被一闪秒杀）。</summary>
	public virtual EnemyTier IssenTier => Stats?.Tier ?? EnemyTier.Grunt;

	/// <summary>
	/// 进场时的位置与朝向。T14 的原地重开靠它把单位放回原位。
	/// 在 <c>_Ready</c> 的末尾捕获——**在 <c>OnActorReady()</c> 之后**，
	/// 这样子类在 OnActorReady 里做的摆位也会被算进"原位"。
	/// </summary>
	public Transform3D SpawnTransform { get; private set; }

	/// <summary>
	/// 改写"战场起点"——**存档点（T42 鬼火）用它**：走到鬼火旁就等于把新的复活位置
	/// 记进这里，之后死亡重开（<see cref="ResetForBattle"/>）会回到这儿而不是关卡入口。
	/// 传入的是**当前时刻的完整变换**（位置 + 朝向）：朝向也要记，
	/// 否则复活后背对着敌人开局。
	/// </summary>
	public void SetSpawnTransform(Transform3D transform) => SpawnTransform = transform;

	/// <summary>死后是否掉落魄。玩家不掉（03 §6.1 说的是魔骸的魄）。</summary>
	public virtual bool DropsSoulOnDeath => true;

	// ── 状态机每帧写这些，基类负责变成物理 ──────────────────────
	public Vector3 DesiredVelocity { get; set; }
	public float DesiredYaw { get; set; }
	public bool HasDesiredYaw { get; set; }

	protected float Gravity { get; private set; }

	// 状态类不是 CombatActor 的子类，所以这两个必须是 public。
	public float MoveSpeed => Stats?.MoveSpeed ?? 4.2f;
	public float SprintSpeed => Stats?.SprintSpeed ?? 7.0f;

	private int _hitStopFrames;
	private int _deflectChainResetFrames;
	private PerilousCue _perilousCue = null!;

	/// <summary>自动分配 ActorId 用的游标。0 是"未分配"的哨兵，所以从 100 起递增。</summary>
	private static int _lastAutoActorId = 100;

	/// <summary>
	/// 下一个自动 ActorId。
	///
	/// 计数器是**静态**的：同一个进程里场景重载后 id 会继续增长。
	/// 这对"唯一性"没有任何影响（需要的只是**同一场景内**两两不同），
	/// 而且比"每次重载重置回 100"更安全——重置会让上一个场景漏掉的悬空引用
	/// 恰好撞上新场景的 id，那正是这个改动想消灭的情形。
	/// </summary>
	private static int NextAutoActorId() => ++_lastAutoActorId;

	public override void _Ready()
	{
		AddToGroup("combat_actor");

		// ActorId 的唯一性被 CombatArbiter 的确定性排序依赖（04 §15），
		// 而手工填 id 已经**静默撞号两次**（道场里两个 TrainingDummy 都是 100；
		// SpearDummy 与 RespawnDummy 都是 102）。撞号的后果不是崩溃、不是报错，
		// 而是"同帧互击谁先结算"变得不可复现——只在调参时表现为
		// "怎么这次结果不一样了"，是最难查的一类 bug。
		//
		// 所以默认在这里按**树序**分配；树序是确定的，确定性不受影响。
		// 显式填了非 0 值的仍然用手工值——调试场景有时需要固定 id。
		if (ActorId == 0)
			ActorId = NextAutoActorId();

		// 「危」预警是机制必需（T12），放在基类里 → 所有战斗单位自动拥有，
		// 将来新加的敌人不会有人忘记补。必须在 RegisterStates/Start 之前建好：
		// AttackState 退出时会立刻调 OnAttackEnded，那时它必须已经存在。
		_perilousCue = new PerilousCue { Name = "PerilousCue" };
		AddChild(_perilousCue);

		int maxHealth = Stats?.MaxHealth ?? 100;
		int maxPosture = Stats?.MaxPosture ?? 100;

		Health = new HealthMeter(maxHealth);
		Posture = new PostureMeter(maxPosture)
		{
			RegenPerSecond = Stats?.PostureRegenPerSecond ?? 22f,
			RegenDelayFrames = Stats?.PostureRegenDelayFrames ?? 30,
		};

		Gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
		TurnSpeed = Stats?.TurnSpeed ?? TurnSpeed;

		Machine = new StateMachine(this);
		RegisterStates(Machine);
		Machine.Start<IdleState>();

		OnActorReady();
		SpawnTransform = GlobalTransform;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		// 输入采集必须在顿帧判断**之前**：顿帧期间玩家提前按下的键不能被吞掉。
		PollLocalInput();

		if (IsDead)
		{
			Velocity = Vector3.Zero;
			return;
		}

		// 顿帧：只冻结自己，不冻结世界（也不用 Engine.TimeScale）。
		if (_hitStopFrames > 0)
		{
			_hitStopFrames--;
			Velocity = new Vector3(0f, Velocity.Y, 0f);
			MoveAndSlide();
			return;
		}

		TickTimers();

		DesiredVelocity = Vector3.Zero;
		HasDesiredYaw = false;
		Machine.Tick();
		ApplyMovement(dt);

		Vector3 v = Velocity;
		float speed01 = new Vector2(v.X, v.Z).Length() / Mathf.Max(0.01f, SprintSpeed);
		OnTickVisual(dt, speed01);
	}

	// ── 子类钩子 ────────────────────────────────────────────────
	protected virtual void RegisterStates(StateMachine machine)
	{
		machine.Add(new IdleState());
		machine.Add(new MoveState());
		machine.Add(new AttackState());
		machine.Add(new StaggerState());
	}

	protected virtual void OnActorReady() { }
	protected virtual void PollLocalInput() { }
	protected virtual void OnTickVisual(float dt, float speed01) { }
	/// <summary>由 <see cref="States.AttackState"/> 调用；子类用它驱动动画。</summary>
	public virtual void OnAttackStarted(AttackData data)
	{
		// 基类实现 = 「危」预警（T12）。它是**机制必需**而不是某个敌人的特色：
		// 02 §3 裁定"弹开只应对一般攻击"之后，玩家分不清一般/危就会直接挨打。
		// 放在基类里，任何新敌人（BOSS、精英）都自动获得。
		// **子类覆写时必须在开头调 `base.OnAttackStarted(data)`。**
		if (!data.Perilous)
			return;

		_perilousCue.Show(data.PerilousKind, data.TotalFrames);
		AudioDirector.Instance?.PlayCombat(SfxFor(data.PerilousKind));
	}

	/// <summary>攻击结束：收起「危」预警。子类覆写时请调 `base.OnAttackEnded()`。</summary>
	public virtual void OnAttackEnded() => _perilousCue.Hide();

	/// <summary>
	/// 「危」三种形态各有一个**音高不同**的音效（07 §2.2：高/中/低）。
	/// 音高是可分辨的维度，改音量不是——所以这三个音效在 T3 就是按音高生成的。
	/// </summary>
	private static CombatSfx SfxFor(PerilousKind kind) => kind switch
	{
		PerilousKind.Sweep => CombatSfx.PerilousSweep,
		PerilousKind.Grab => CombatSfx.PerilousGrab,
		_ => CombatSfx.PerilousThrust,
	};
	protected virtual void OnVerdictReceived(in ResolveResult result) { }
	protected virtual void OnDamaged(int damage) { }
	protected virtual void OnPostureBroken() { }
	protected virtual void OnDeath() { }

	/// <summary>玩家读输入缓冲，敌人读 AI。默认永不主动攻击。</summary>
	public virtual bool ConsumeAttackInput() => false;

	/// <summary>
	/// 敌人 AI 用：本帧是否想发动攻击。玩家永远返回 false（走输入缓冲）。
	/// 有这个钩子，敌人就能直接复用 <see cref="States.AttackState"/>，
	/// 不必为"会还手的假人"再写一套出招状态。
	/// </summary>
	public virtual bool WantsToAttack() => false;

	/// <summary>移动意图。默认站桩。</summary>
	public virtual bool TryGetMoveIntent(out MoveIntent intent)
	{
		intent = default;
		return false;
	}

	public virtual bool IsInvulnerableNow => DebugInvulnerable;

	// ── 判定用的快照 ─────────────────────────────────────────────

	public bool IsAttackActiveNow => PrimaryHitbox is { IsActiveThisFrame: true };

	/// <summary>顿帧中不许再造成伤害，否则一次判定会在冻结期被反复结算。</summary>
	public bool CanDealDamage => _hitStopFrames == 0 && !IsDead;

	public DefenderSnapshot GetDefenderSnapshot(Vector3 attackerPosition)
	{
		Vector3 toAttacker = attackerPosition - GlobalPosition;
		toAttacker.Y = 0f;

		int angleDeg = 0;
		if (toAttacker.LengthSquared() > 0.0001f)
		{
			Vector3 forward = -GlobalTransform.Basis.Z;
			forward.Y = 0f;
			forward = forward.Normalized();
			angleDeg = Mathf.RoundToInt(Mathf.RadToDeg(forward.SignedAngleTo(toAttacker.Normalized(), Vector3.Up)));
		}

		return new DefenderSnapshot
		{
			ActorId = ActorId,
			IsInvulnerable = IsInvulnerableNow,
			IsActive = IsAttackActiveNow,
			InDeflectWindow = DeflectWindowFramesLeft > 0,
			IsGuarding = IsGuarding,
			GuardAngleDeg = angleDeg,
			CurrentPosture = Posture.Current,
			MaxPosture = Posture.Max,
			IssenKind = IssenBuffFramesLeft > 0 ? IssenBuff : IssenKind.None,
		};
	}

	/// <summary>格挡姿态。M1 接上 GuardState 后由它驱动。</summary>
	public bool IsGuarding { get; set; }

	// ── 结算落地 ─────────────────────────────────────────────────

	/// <summary>
	/// 把裁决结果变成血/体干/顿帧/buff。
	/// <paramref name="attacker"/> 是**打我的那个人**；
	/// 一闪时方向反过来：我才是发动者，<paramref name="attacker"/> 是受害者（这正是 Resolver 的语义）。
	/// </summary>
	public void ReceiveVerdict(in ResolveResult result, CombatActor? attacker, AttackData? attack, IssenKind usedIssen)
	{
		if (IsDead)
			return;

		switch (result.Verdict)
		{
			case Verdict.Miss:
				// 完全无效：连顿帧都不给，否则玩家会以为打中了。
				return;

			case Verdict.Hit:
			{
				FreezeBoth(attacker, result.HitStopFrames);
				int damage = Health.Apply(result.Damage);
				Posture.Apply(result.PostureDamage);
				OnVerdictReceived(result);
				if (damage > 0)
					OnDamaged(damage);
				Machine.Get<StaggerState>().Duration = Health.IsDead ? 0 : HitStunFrames;
				Machine.Change<StaggerState>();
				if (Posture.IsBroken)
					OnPostureBroken();
				if (Health.IsDead)
					Die();
				return;
			}

			case Verdict.Block:
				FreezeBoth(attacker, result.HitStopFrames);
				Posture.Apply(result.PostureDamage);
				OnVerdictReceived(result);
				if (Posture.IsBroken)
					OnPostureBroken();
				return;

			case Verdict.Deflect:
				FreezeBoth(attacker, result.HitStopFrames);
				attacker?.ApplyPostureDamage(result.PostureDamage);   // 弹开削的是攻方体干
				GrantIssen(result.GrantIssen, result.GrantIssenFrames);
				DeflectChain++;
				_deflectChainResetFrames = 90;
				OnVerdictReceived(result);
				return;

			case Verdict.Clash:
				FreezeBoth(attacker, result.HitStopFrames);
				OnVerdictReceived(result);
				return;

			case Verdict.GuardBreak:
				FreezeBoth(attacker, result.HitStopFrames);
				Posture.Apply(Posture.Max);
				OnVerdictReceived(result);
				OnPostureBroken();
				Machine.Get<StaggerState>().Duration = GuardBreakStunFrames;
				Machine.Change<StaggerState>();
				return;

			case Verdict.Issen:
			{
				FreezeBoth(attacker, result.HitStopFrames);
				IssenBuff = IssenKind.None;
				IssenBuffFramesLeft = 0;

				if (result.GrantIssen != IssenKind.None)
					GrantIssen(result.GrantIssen, result.GrantIssenFrames);

				if (attacker is { } victim)
				{
					IssenEffect effect = IssenTable.For(usedIssen, victim.IssenTier);
					if (effect.InstantKill)
					{
						victim.Health.Apply(victim.Health.Max);
						victim.Die();
					}
					else
					{
						victim.ApplyPosturePercent(effect.PostureDamagePercent);
						if (effect.StunFrames > 0)
						{
							victim.Machine.Get<StaggerState>().Duration = effect.StunFrames;
							victim.Machine.Change<StaggerState>();
						}
					}
				}

				OnVerdictReceived(result);
				return;
			}
		}
	}

	public void ApplyHitStop(int frames)
	{
		if (frames > _hitStopFrames)
			_hitStopFrames = frames;
	}

	public void ApplyPostureDamage(int amount)
	{
		if (amount <= 0 || IsDead)
			return;

		if (Posture.Apply(amount))
			OnPostureBroken();
	}

	public void ApplyPosturePercent(float percent)
	{
		if (percent <= 0f || IsDead)
			return;

		if (Posture.ApplyPercent(percent))
			OnPostureBroken();
	}

	public void GrantIssen(IssenKind kind, int frames)
	{
		if (kind == IssenKind.None || frames <= 0)
			return;

		IssenBuff = kind;
		IssenBuffFramesLeft = frames;
	}

	public void OpenDeflectWindow(int frames)
	{
		if (frames > DeflectWindowFramesLeft)
			DeflectWindowFramesLeft = frames;
	}

	public virtual void Die()
	{
		if (IsDead)
			return;

		IsDead = true;
		Machine.Get<StaggerState>().Duration = 0;
		PrimaryHitbox?.SetActive(false);

		if (DropsSoulOnDeath)
		{
			EventBus.Instance?.RaiseActorDefeated(new ActorDefeatedEvent
			{
				ActorId = ActorId,
				Position = GlobalPosition,
				Tier = IssenTier,
			});
		}

		OnDeath();
	}

	/// <summary>
	/// 原地复位（T14 的重开协议调用）。**只加不改**：这个方法不改变任何既有语义，
	/// 只在 <see cref="Core.BattleReset"/> 主动调用时生效。
	///
	/// 复位内容按 T14 规则 3 逐项对齐：位置、朝向、血量、体干、状态机、buff、顿帧。
	/// 子类要补自己的东西（重生计时、表现层、冷却）时覆写并调 <c>base</c>。
	///
	/// 刻意**不碰**持久资源：升级、魄、侵蚀、喝血次数一律留在玩家身上
	/// （01 §0 规则 1：死亡没有持久性惩罚）。
	/// </summary>
	public virtual void ResetForBattle()
	{
		GlobalTransform = SpawnTransform;

		Velocity = Vector3.Zero;
		DesiredVelocity = Vector3.Zero;
		HasDesiredYaw = false;

		_hitStopFrames = 0;
		_deflectChainResetFrames = 0;

		IssenBuff = IssenKind.None;
		IssenBuffFramesLeft = 0;
		DeflectWindowFramesLeft = 0;
		DeflectChain = 0;

		IsGuarding = false;
		IsDead = false;

		Health.Heal(Health.Max);
		Posture.Reset();

		PrimaryHitbox?.SetActive(false);
		PrimaryHitbox?.SetAttack(null);
		SetCurrentAttack(string.Empty);

		Machine.ForceChange<IdleState>();
	}

	// ── ICombatActorDebug（只读视图）─────────────────────────────
	// Health / MaxHealth / Posture / MaxPosture 用显式实现，
	// 因为公开的 Health/Posture 是"表"（HealthMeter/PostureMeter），而接口要的是数字。
	int ICombatActorDebug.Health => Health.Current;
	int ICombatActorDebug.MaxHealth => Health.Max;
	int ICombatActorDebug.Posture => Posture.Current;
	int ICombatActorDebug.MaxPosture => Posture.Max;

	/// <summary>
	/// 喝血剩余次数。**只给 UI 读**（T31）——只有玩家有，其它单位恒 0。
	/// 单独开一个只读虚属性而不是直接实现接口成员，是因为 PlayerActor 已经有一个
	/// 同名的公开属性（HealChargesLeft），显式接口实现没法被子类覆盖。
	/// </summary>
	public virtual int HealChargesLeftForUi => 0;

	int ICombatActorDebug.HealChargesLeft => HealChargesLeftForUi;

	/// <summary>
	/// 半自动防御的剩余次数（T37 缺口③）。**只给 UI 读**——只有玩家有，其它单位恒 0。
	/// 与 <see cref="HealChargesLeftForUi"/> 同一个理由（显式接口实现没法被子类覆盖）。
	/// </summary>
	public virtual int HalfAutoGuardChargesLeftForUi => 0;

	int ICombatActorDebug.HalfAutoGuardChargesLeft => HalfAutoGuardChargesLeftForUi;

	/// <summary>
	/// 当前正在打出来的这一招是不是「危」。
	/// T37 的半自动防御只对一般攻击生效（05 §128），靠它把危排除掉。
	/// </summary>
	public bool IsIncomingAttackPerilous => _perilousCue.IsShowing;

	public string StateName => Machine?.Current?.GetType().Name ?? "None";
	public int StateFrame => Machine?.Current?.Frame ?? 0;
	public int StateTotalFrames => Machine?.Current?.TotalFrames ?? 0;
	public bool IsInvulnerable => IsInvulnerableNow;
	public string CurrentAttackId { get; private set; } = string.Empty;

	// ── IDebugCheatable（只对调试开放）───────────────────────────
	public void DebugRestoreToFull()
	{
		Health.Heal(Health.Max);
		Posture.Reset();
	}

	public virtual void DebugRestartBattle() => DebugRestoreToFull();

	// ── 内部 ─────────────────────────────────────────────────────

	internal void SetCurrentAttack(string attackId) => CurrentAttackId = attackId;

	private void TickTimers()
	{
		Posture.Tick(1, Health.Ratio);

		if (IssenBuffFramesLeft > 0)
		{
			IssenBuffFramesLeft--;
			if (IssenBuffFramesLeft == 0)
				IssenBuff = IssenKind.None;
		}

		if (DeflectWindowFramesLeft > 0)
			DeflectWindowFramesLeft--;

		if (_deflectChainResetFrames > 0)
		{
			_deflectChainResetFrames--;
			if (_deflectChainResetFrames == 0)
				DeflectChain = 0;
		}
	}

	private void FreezeBoth(CombatActor? attacker, int frames)
	{
		if (frames <= 0)
			return;

		ApplyHitStop(frames);
		attacker?.ApplyHitStop(frames);
	}

	private void ApplyMovement(float dt)
	{
		Vector3 velocity = Velocity;

		if (IsOnFloor())
		{
			if (velocity.Y < 0f)
				velocity.Y = 0f;
		}
		else
		{
			velocity.Y -= Gravity * dt;
		}

		velocity.X = DesiredVelocity.X;
		velocity.Z = DesiredVelocity.Z;

		Velocity = velocity;
		MoveAndSlide();

		if (HasDesiredYaw)
		{
			Vector3 rotation = Rotation;
			rotation.Y = Mathf.LerpAngle(rotation.Y, DesiredYaw, Mathf.Min(1f, TurnSpeed * dt));
			Rotation = rotation;
		}
	}
}
