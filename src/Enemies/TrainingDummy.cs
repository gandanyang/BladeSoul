using Godot;
using Oniblade.Combat;
using Oniblade.Dev;

namespace Oniblade.Enemies;

/// <summary>
/// 木桩。M0.5 的靶子：**它的存在意义是让手感第一次变得可感知**，
/// 所以它必须有反应（顿帧、受击抖动、体干可见地涨），不能一动不动。
/// </summary>
public partial class TrainingDummy : CombatActor
{
	[Export] public Color BodyColor { get; set; } = new(0.42f, 0.33f, 0.2f);
	[Export] public Color AccentColor { get; set; } = new(0.24f, 0.19f, 0.12f);

	/// <summary>打不死（练连段用）。关掉它就是普通敌人。</summary>
	[Export] public bool Invincible { get; set; } = true;

	/// <summary>
	/// 死后自动原地重生。
	/// **它是"击杀流程"的试验台**：吸魂、忍杀、掉落这些东西都需要一个会死、
	/// 又能反复死的靶子，否则每验证一次都要重启场景。
	/// </summary>
	[Export] public bool AutoRespawn { get; set; }

	/// <summary>死亡到重生的等待帧数（默认 120 帧 = 2 秒）。</summary>
	[Export] public int RespawnDelayFrames { get; set; } = 120;

	private BlockoutRig _rig = null!;
	private Vector3 _spawnPosition;
	private Vector3 _spawnRotation;
	private int _respawnFramesLeft;

	/// <summary>已经重生过几次（调试与测试用）。</summary>
	public int RespawnCount { get; private set; }

	public bool IsWaitingToRespawn => IsDead && AutoRespawn;

	protected override void OnActorReady()
	{
		_rig = new BlockoutRig();
		_rig.Build(BodyColor, AccentColor, false);
		AddChild(_rig);

		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		_spawnPosition = GlobalPosition;
		_spawnRotation = Rotation;
	}

	protected override void OnTickVisual(float dt, float speed01)
	{
		_rig.AnimateLocomotion(0f, dt);
		_rig.AnimateCombat(dt);
	}

	protected override void OnDamaged(int damage) => _rig.PlayHitReact(1f, HitStunFrames);

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		if (result.Verdict is Combat.Verdict.Block or Combat.Verdict.Deflect or Combat.Verdict.Clash)
			_rig.PlayHitReact(0.5f, HitStunFrames);
	}

	/// <summary>体干破裂后立刻回满，这样它可以被无限次练（道场的基本要求）。</summary>
	protected override void OnPostureBroken() => Posture.Reset();

	public override void Die()
	{
		if (Invincible)
		{
			Health.Heal(Health.Max);
			return;
		}

		base.Die();
	}

	/// <summary>
	/// 死掉之后基类会直接 return（<see cref="CombatActor._PhysicsProcess"/> 在 IsDead 时早退），
	/// 所以重生计时必须在**基类跑完之后**自己做。
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		if (!IsWaitingToRespawn)
			return;

		_respawnFramesLeft--;
		if (_respawnFramesLeft <= 0)
			Respawn();
	}

	protected override void OnDeath()
	{
		if (!AutoRespawn)
			return;

		_respawnFramesLeft = Mathf.Max(1, RespawnDelayFrames);
		_rig.Visible = false;
	}

	/// <summary>
	/// T14：复位时**必须把重生计时也清掉**，否则重开后它会卡在"死亡等待"里——
	/// 玩家会看到一个已经死掉、却在原地等着重生的假人，而它永远不会重生
	/// （因为重开已经把它救活了，<c>IsWaitingToRespawn</c> 变回 false）。
	/// </summary>
	public override void ResetForBattle()
	{
		_respawnFramesLeft = 0;
		_rig.Visible = true;

		base.ResetForBattle();
	}

	private void Respawn()
	{
		GlobalPosition = _spawnPosition;
		Rotation = _spawnRotation;

		Health.Heal(Health.Max);
		Posture.Reset();

		IsDead = false;
		_rig.Visible = true;
		_respawnFramesLeft = 0;
		RespawnCount++;

		Machine.ForceChange<Combat.States.IdleState>();
	}
}
