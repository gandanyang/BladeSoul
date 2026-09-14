using Godot;
using Oniblade.Audio;
using Oniblade.Combat.Data;

namespace Oniblade.Combat.States;

/// <summary>
/// 垫步闪避（02 §2.2 / T13）：**无敌帧 → 后摇**。
///
/// 它是**路径 B 的地基**（01 §3："闪避 + 普攻能通关"整条建立在它上面），
/// 也是「危」攻击的保底答案——在"弹开只应对一般攻击"这条裁定（02 §3）之后，
/// 危攻击只剩闪避与看破两条路，而看破只对危·突刺有效。
///
/// 参数由输入侧在切入之前写入（与 <see cref="GuardState"/>、<c>StaggerState.Duration</c>
/// 同一用法），所以本状态不知道 <c>DifficultyProfile</c> 的存在。
/// 数值一律来自难度档，代码里不写死。
///
/// **帧号约定（重要）**：第 0 帧 = 玩家按下闪避的那一帧，且那一帧就已经无敌。
/// 这是刻意的——"在刀落下的同一帧按闪避"必须算躲开，否则玩家会觉得输入被吞了。
/// 因此整段状态存在的帧数 = <c>TotalFrames + 1</c>（多出的那一帧就是输入帧本身），
/// 与 <see cref="DeflectState"/> 的 <c>DurationFrames=12</c> 走 13 帧是同一个约定。
/// </summary>
public sealed class DodgeState : ActorState
{
	/// <summary>
	/// 完美闪避授予的「避一闪」buff 时长（帧）。02 §2.3 / §4 定的是 14 帧。
	///
	/// 它是**状态时长常量**，不是难度旋钮——四档难度共用同一个值。
	/// 按 T6 已立的先例（<see cref="DeflectState.DurationFrames"/> 的 12 帧），
	/// 这类常量留在状态类里，没有跟着 <c>DodgeIFrames</c> 一起进 DifficultyProfile。
	/// </summary>
	public const int PerfectDodgeIssenFrames = 14;

	/// <summary>闪避破风声音量（dB）。</summary>
	public const float DodgeWhooshVolumeDb = -3f;

	/// <summary>
	/// 闪避破风声音高。比原始略高——垫步是**轻快**的动作，
	/// 和"沉重的跳劈"用同一个音高会读成"这一下很重"，与动作不符。
	/// </summary>
	public const float DodgeWhooshPitchScale = 1.12f;

	private readonly DodgeWindow _window = new();

	/// <summary>切入时锁定下来的方向与速度（公开参数用掉即复位，避免下次忘写时沿用）。</summary>
	private Vector3 _direction;
	private float _speed;

	/// <summary>切入前由输入侧写入：无敌帧数（<c>Difficulty.DodgeIFrames</c>）。</summary>
	public int InvulnerableFrames { get; set; }

	/// <summary>切入前由输入侧写入：后摇帧数（<c>Difficulty.DodgeRecoveryFrames</c>）。</summary>
	public int RecoveryFrames { get; set; }

	/// <summary>切入前由输入侧写入：闪避速度（米/秒，已含难度/属性加成）。</summary>
	public float Speed { get; set; }

	/// <summary>切入前由输入侧写入：世界空间闪避方向（已归一化；零向量 = 原地不动）。</summary>
	public Vector3 Direction { get; set; }

	/// <summary>本次闪避是否已经发过「避一闪」奖励。一次闪避只算一次。</summary>
	public bool PerfectDodgeGranted { get; private set; }

	/// <summary>本帧是否处于无敌帧。裁决器规则 1 直接给 <c>Miss</c>（连一闪都打不中）。</summary>
	public bool IsInvulnerableNow => _window.IsInvulnerable;

	/// <summary>本帧是否还在无敌帧里（供输入侧决定"防御能不能取消这次闪避"）。</summary>
	public bool IsInRecovery => _window.IsInRecovery;

	public override int TotalFrames => _window.TotalFrames;

	/// <summary>已经过到第几帧（测试与调试面板用）。</summary>
	public int FramesSinceStart => _window.FramesSinceStart;

	public override void Enter()
	{
		base.Enter();

		_direction = Direction;
		_speed = Speed;
		_window.Begin(InvulnerableFrames, RecoveryFrames);
		PerfectDodgeGranted = false;

		// 闪避的破风声。放在 Enter 而不是"无敌帧结束"——
		// 玩家按下闪避的那一帧就该听到"我动了"，声音晚一帧就是输入被吞的感觉。
		//
		// 走 3D 位置音：闪避是有方向的位移，声音跟着角色走才有"我往哪边扑出去了"。
		AudioDirector.Instance?.PlayCombatAt(
			CombatSfx.DodgeWhoosh,
			Actor.GlobalPosition,
			DodgeWhooshPitchScale,
			DodgeWhooshVolumeDb);

		// 用掉就复位（理由同 GuardState）：下次忘了写参数时宁可退化成"原地闪"，
		// 也不要沿用上一次的方向和速度——那会变成"角色自己乱跑"。
		Direction = Vector3.Zero;
		Speed = 0f;
	}

	public override void Tick()
	{
		base.Tick();

		ApplyDodgeMovement();
		_window.Advance();

		if (_window.IsFinished)
			ChangeState<IdleState>();
	}

	/// <summary>
	/// 完美闪避：无敌帧内**实际躲开了一次攻击**（裁决器返回 <c>Miss</c>）。
	///
	/// 由 <c>PlayerActor.OnAttackEvaded</c>（<see cref="IAttackEvasionListener"/>）转发进来。
	/// 判定窗就是无敌帧本身——<c>Miss</c> 只可能发生在无敌帧里，
	/// 所以"完美闪避"与"无敌"天然是同一个窗口（T21 删掉了那个冗余的宽容参数）。
	///
	/// 一次闪避只发一次——假人的一刀有 4 个判定帧，不挡住会连发 4 次、
	/// 把 buff 时长刷成 14 帧的整数倍，玩家就能靠"站着不动挨着躲"白嫖时长。
	/// </summary>
	public void OnAttackEvaded()
	{
		if (PerfectDodgeGranted || !_window.IsInvulnerable)
			return;

		PerfectDodgeGranted = true;
		Actor.GrantIssen(IssenKind.Dodge, PerfectDodgeIssenFrames);
	}

	/// <summary>
	/// 位移：无敌帧期间全速滑出，后摇期间线性衰减到 0（"滑一下就站住"）。
	///
	/// 全程**不转向**（<c>HasDesiredYaw</c> 保持 false）：后跳时转身会变成"逃跑"，
	/// 锁定目标时更会把朝向甩掉，下一刀就打不着人了。
	/// </summary>
	private void ApplyDodgeMovement()
	{
		if (_direction.LengthSquared() <= 0.0001f || _speed <= 0f)
			return;

		int framesSinceStart = _window.FramesSinceStart;
		float scale;

		if (framesSinceStart < _window.InvulnerableFrames)
		{
			scale = 1f;
		}
		else
		{
			int recoveryFrame = framesSinceStart - _window.InvulnerableFrames;
			int recoveryTotal = Mathf.Max(1, _window.RecoveryFrames);
			scale = Mathf.Max(0f, 1f - recoveryFrame / (float)recoveryTotal);
		}

		Actor.DesiredVelocity = _direction * (_speed * scale);
	}
}
