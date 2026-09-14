using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Dev;

namespace Oniblade.Enemies;

/// <summary>
/// 魔骸足兵（T52）：**第一个真的会被破韧、会被处决的敌人**。
///
/// 与 <see cref="AttackingDummy"/> 的分工：
///   · `AttackingDummy` = 道场里的**节拍器**，站桩不追人、打不死、体干一破就回满（T7 验收用）。
///   · `Ashigaru`       = 真敌人：会追、会砍、**体干破了会瘫软待处决**。
///
/// 破韧链路（机制由项目主人口述裁定，不许含糊）：
/// <code>
///   体干被打满 → PostureBrokenState（默认 120 帧）
///                  · 不能主动攻击（CanTransitionTo 拒绝 AttackState）
///                  · 只能被打
///                  · 可被处决
///                → 窗口过期 → 回 IdleState 重新行动
/// </code>
///
/// 动画由 <see cref="AshigaruAnimator"/> 程序化驱动。姿势数字全部按**足兵自己的 rest**
/// 写（与玩家模型不同，见该类的注释）。
/// </summary>
public partial class Ashigaru : CombatActor, IDeathblowTarget
{
	/// <summary>普通攻击招式。场景里指向 <c>data/attacks/enemies/grunt_slash.tres</c>。</summary>
	[Export] public AttackData? Attack { get; set; }

	/// <summary>两次攻击之间的最小间隔（帧）。02 §10 要求 ≥45。</summary>
	[Export] public int AttackIntervalFrames { get; set; } = 70;

	/// <summary>发动攻击的水平距离（米）。</summary>
	[Export] public float AttackRange { get; set; } = 2.4f;

	/// <summary>进入这个距离就停下来出招（免得贴着玩家推来推去）。</summary>
	[Export] public float PreferredRange { get; set; } = 1.9f;

	/// <summary>
	/// 破韧窗口长度（帧）。**来自数据**（`data/actors/enemy_grunt.tres` 的
	/// <c>PostureBrokenFrames</c>），代码里只留一个兜底默认值 —— 与
	/// <c>CombatActor.MoveSpeed</c> 的 <c>Stats?.MoveSpeed ?? 4.2f</c> 同一写法（铁律 1）。
	///
	/// 它曾经是本类的一个 `[Export]`（T52 过渡方案），现已搬进数据：
	/// 破韧时长是**敌人自己的属性**（杂兵 2 秒、精英该更短、BOSS 可能不给），
	/// 不是"这一只怪在场景里的特例"，所以不该挂在场景上。
	/// </summary>
	public int PostureBrokenFrames => Stats?.PostureBrokenFrames ?? 120;

	/// <summary>模型场景（`scenes/enemies/AshigaruModel.tscn`）。留空则降级到灰盒。</summary>
	[Export] public PackedScene? ModelScene { get; set; }

	[Export] public Color BodyColor { get; set; } = new(0.30f, 0.26f, 0.24f);
	[Export] public Color AccentColor { get; set; } = new(0.42f, 0.16f, 0.14f);

	private AshigaruAnimator? _modelAnim;
	private BlockoutRig? _rig;
	private Node3D? _target;
	private int _cooldownFrames;
	private float _hitStrength = 1f;
	private int _hitStrengthFrameLeft;
	private int _brokenEnterCount;

	/// <summary>进入过几次破韧（探针用它证明"体干打满真的会进破韧态"）。</summary>
	public int BrokenEnterCount => _brokenEnterCount;

	/// <summary>破韧窗口还剩几帧（0 = 不可处决）。</summary>
	public int PostureBrokenFramesLeft =>
		Machine.Current is PostureBrokenState s ? s.FramesLeft : 0;

	/// <summary>此刻能不能被处决。</summary>
	public bool CanBeExecuted => Machine.Current is PostureBrokenState { IsOpen: true } && !IsDead;

	/// <summary>
	/// T52：给只读接口 <see cref="ICombatActorDebug"/> 的读数口（处决标记 UI 用）。
	/// 覆写而不是另起一套判断 —— 判据只能有一个来源。
	/// </summary>
	public override bool CanBeExecutedNow => CanBeExecuted;

	/// <summary>此刻是否正在被处决演出（探针断言用）。</summary>
	public bool IsBeingExecuted => Machine.Current is DeathblowState;

	/// <summary>
	/// 进入"被处决"。由 <c>PlayerActor</c> 在按下交互键时调用
	/// （通过 <see cref="IDeathblowTarget"/> 而不是直接认 <c>Ashigaru</c> 类型）。
	///
	/// 用 `ForceChange`：此刻敌人在 <see cref="PostureBrokenState"/> 里等着，
	/// 那个状态自己不会换到"被处决"。
	/// </summary>
	public void BeginBeingExecuted(int durationFrames)
	{
		if (IsDead)
			return;

		Machine.Get<DeathblowState>().DurationFrames = durationFrames;
		Machine.ForceChange<DeathblowState>();
	}

	/// <summary>动画器是否可用（探针用：证明模型真的被驱动了，而不只是"导入了"）。</summary>
	public bool HasModel => _modelAnim is { Valid: true };

	/// <summary>
	/// ★ **必须把 <see cref="PostureBrokenState"/> 注册进来**。
	///
	/// 基类默认只注册 Idle/Move/Attack/Stagger 四个（`CombatActor.RegisterStates`），
	/// 破韧态不在其中。忘了注册的症状是：`Machine.Get&lt;PostureBrokenState&gt;()`
	/// 直接抛 `KeyNotFoundException`，而 `Machine.Change&lt;T&gt;()` 却是**静默丢弃**——
	/// 于是"体干打满了但什么都不发生"，一点报错都看不到。
	/// （首次跑端到端就是被这个抓出来的。）
	/// </summary>
	protected override void RegisterStates(StateMachine machine)
	{
		base.RegisterStates(machine);
		machine.Add(new PostureBrokenState());
		// T52：被处决态也要注册——理由同破韧态，忘了就是静默丢弃
		machine.Add(new DeathblowState());
	}

	protected override void OnActorReady()
	{
		if (ModelScene is not null)
		{
			Node model = ModelScene.Instantiate();
			AddChild(model);

			if (model is Node3D node3D)
			{
				_modelAnim = new AshigaruAnimator(node3D);
				if (!_modelAnim.Valid)
				{
					GD.PushWarning($"{Name}: 足兵骨架不可用，动画不会动。" +
								   "先跑 res://scenes/tests/AshigaruRig.tscn 确认");
					_modelAnim = null;
				}
			}
		}

		if (_modelAnim is null)
		{
			_rig = new BlockoutRig();
			_rig.Build(BodyColor, AccentColor, false);
			AddChild(_rig);
		}

		PrimaryHitbox = GetNodeOrNull<Hitbox>("Hitbox");

		if (Attack is not null)
			Machine.Get<AttackState>().Configure(new[] { Attack });
	}

	/// <summary>
	/// ★★ **追击必须从这里出，不能从 `OnTickVisual` 出**。
	///
	/// 基类 `_PhysicsProcess` 的顺序是：
	/// <code>
	///   DesiredVelocity = Zero   ← 每帧清零
	///   Machine.Tick()           ← MoveState 在这里调 TryGetMoveIntent() 并写 DesiredVelocity
	///   ApplyMovement()          ← 只有在这里之前写好的速度才会被用上
	///   OnTickVisual()           ← 太晚了！
	/// </code>
	/// 在 `OnTickVisual` 里写 `DesiredVelocity` 的症状是：**敌人永远一动不动**，
	/// 而 `DesiredVelocity` 读出来却是非零的（写进去了，但 `ApplyMovement` 早跑完了，
	/// 下一帧开头又被清零）。第一次跑端到端就是被这个卡住的——`Velocity` 恒为 0
	/// 而 `Desired` 显示 (0,0,-3.2)，查了很久。
	///
	/// 移动意图的语义由基类定义：**玩家从输入读，敌人从 AI 读**（`MoveIntent` 的注释）。
	/// </summary>
	public override bool TryGetMoveIntent(out MoveIntent intent)
	{
		intent = default;

		// 死亡 / 攻击 / 破韧 / 被处决 / 受击硬直里一律不移动。
		// （破韧与被处决都要求"一动不动"：前者是待处决、后者是被钉住。）
		if (IsDead
			|| Machine.Current is AttackState
			|| Machine.Current is PostureBrokenState
			|| Machine.Current is DeathblowState
			|| Machine.Current is StaggerState)
			return false;

		Node3D? target = ResolveTarget();
		if (target is null)
			return false;

		Vector3 toTarget = target.GlobalPosition - GlobalPosition;
		toTarget.Y = 0f;
		float distance = toTarget.Length();

		// 进了出招距离就停住，免得贴着玩家推来推去
		if (distance <= PreferredRange || distance <= 0.0001f)
			return false;

		intent.Direction = toTarget.Normalized();
		intent.Sprint = false;
		return true;
	}

	/// <summary>出招意图：冷却结束 + 玩家在攻击距离内。**不看玩家按了什么**（02 §10）。</summary>
	public override bool WantsToAttack() => _cooldownFrames == 0 && IsTargetInRange();

	public override void OnAttackStarted(AttackData data)
	{
		base.OnAttackStarted(data);   // 基类负责「危」预警，不许丢
		_rig?.PlayAttack(data.TotalFrames);
	}

	public override void OnAttackEnded() => _cooldownFrames = AttackIntervalFrames;

	/// <summary>
	/// 体干被打满：进**破韧态**而不是普通硬直。
	/// 这是本卡的核心——原来这里（假人）是 `Posture.Reset()`，因为靶子不该被破韧；
	/// 真敌人必须真的瘫下来。
	/// </summary>
	protected override void OnPostureBroken()
	{
		_brokenEnterCount++;

		Machine.Get<PostureBrokenState>().DurationFrames = PostureBrokenFrames;
		Machine.ForceChange<PostureBrokenState>();   // 强制：受击硬直也可能正占着状态机
	}

	public override void ResetForBattle()
	{
		_cooldownFrames = 0;
		_brokenEnterCount = 0;
		_hitStrength = 1f;
		_hitStrengthFrameLeft = 0;
		_modelAnim?.Reset();
		base.ResetForBattle();
	}

	protected override void OnTickVisual(float dt, float speed01)
	{
		if (_cooldownFrames > 0)
			_cooldownFrames--;

		if (_hitStrengthFrameLeft > 0)
			_hitStrengthFrameLeft--;
		else
			_hitStrength = 1f;

		FaceTarget(dt);

		if (_modelAnim is not null)
		{
			DriveModel(dt, speed01);
			return;
		}

		_rig?.AnimateLocomotion(speed01, dt);
		_rig?.AnimateCombat(dt);
	}

	/// <summary>
	/// 从状态机驱动模型动作。优先级见 <see cref="AshigaruAnimator.AnimateCombat"/>。
	/// 帧号与总帧数全部来自状态（真实帧数据在 `data/**`），这里不写死任何数字。
	/// </summary>
	private void DriveModel(float dt, float speed01)
	{
		if (IsDead)
		{
			_modelAnim!.Animate(dt, speed01, AshigaruAction.Death, 0, 0);
			return;
		}

		// 被处决：优先级**高于**破韧（处决是破韧之后发生的，必须覆盖瘫软姿势）
		if (Machine.Current is DeathblowState executed)
		{
			_modelAnim!.Animate(dt, 0f, AshigaruAction.BeingExecuted,
								executed.Frame, executed.TotalFrames);
			return;
		}

		if (Machine.Current is PostureBrokenState broken)
		{
			_modelAnim!.Animate(dt, 0f, AshigaruAction.PostureBroken,
								broken.Frame, broken.TotalFrames);
			return;
		}

		if (Machine.Current is AttackState atk && atk.TotalFrames > 0)
		{
			_modelAnim!.Animate(dt, 0f, AshigaruAction.Attack, atk.Frame, atk.TotalFrames);
			return;
		}

		if (Machine.Current is StaggerState stg && stg.TotalFrames > 0)
		{
			AshigaruAction kind = _hitStrength >= 0.8f ? AshigaruAction.HitHeavy : AshigaruAction.HitLight;
			_modelAnim!.Animate(dt, 0f, kind, stg.Frame, stg.TotalFrames);
			return;
		}

		// 其余情况（Idle / Move）由动画器按速度自己选。
		// 注意 speed01 来自基类的实际速度，所以"意图移动但被挡住"时不会原地踏步。
		_modelAnim!.Animate(dt, speed01,
							speed01 > AshigaruAnimator.MinWalkSpeed
								? AshigaruAction.Move
								: AshigaruAction.Idle,
							0, 0);
	}

	protected override void OnDamaged(int damage)
	{
		_hitStrength = 1f;
		_hitStrengthFrameLeft = HitStunFrames;
		_rig?.PlayHitReact(1f, HitStunFrames);
	}

	protected override void OnVerdictReceived(in ResolveResult result)
	{
		if (result.Verdict is Verdict.Block or Verdict.Deflect or Verdict.Clash)
		{
			_hitStrength = 0.5f;
			_hitStrengthFrameLeft = HitStunFrames;
			_rig?.PlayHitReact(0.5f, HitStunFrames);
		}
	}

	/// <summary>玩家是否在出招距离内（水平距离，不含高度差）。</summary>
	private bool IsTargetInRange()
	{
		Node3D? target = ResolveTarget();
		if (target is null)
			return false;

		Vector3 delta = target.GlobalPosition - GlobalPosition;
		delta.Y = 0f;

		return delta.LengthSquared() <= AttackRange * AttackRange;
	}

	/// <summary>
	/// 站定时自己转向玩家。
	///
	/// 追击中**不要**在这里改朝向：那时 `MoveState` 会设 `DesiredYaw`，
	/// 而 `ApplyMovement` 在 `OnTickVisual` **之前**就已经把它插值过了——
	/// 这里再直接写 `Rotation` 会把那次插值覆盖掉，看起来就是"转身一顿一顿的"。
	/// </summary>
	private void FaceTarget(float dt)
	{
		if (IsDead || Machine.Current is not (IdleState or MoveState))
			return;

		// 只在"不在移动"时才自己转（移动时的朝向归 MoveState 管）
		if (Machine.Current is MoveState
			&& TryGetMoveIntent(out MoveIntent intent)
			&& intent.Direction.LengthSquared() > 0.0001f)
			return;

		Node3D? target = ResolveTarget();
		if (target is null)
			return;

		Vector3 toTarget = target.GlobalPosition - GlobalPosition;
		toTarget.Y = 0f;

		if (toTarget.LengthSquared() <= 0.0001f)
			return;

		float targetYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);
		float yaw = Mathf.LerpAngle(Rotation.Y, targetYaw, Mathf.Min(1f, TurnSpeed * dt));
		Rotation = new Vector3(0f, yaw, 0f);
	}

	/// <summary>
	/// 玩家从 Godot 组 <c>player</c> 里找（与 <see cref="AttackingDummy"/> 同一个约定）。
	/// 找到就缓存，别每帧去问场景树。
	///
	/// 重查条件用 `IsInstanceValid` 而不是只判 null：Godot 节点释放后 C# 包装对象
	/// **仍然非 null**，只判 null 会一直拿到失效引用（T52 顺手修过同类缺陷）。
	/// </summary>
	private Node3D? ResolveTarget()
	{
		if (_target is not null && IsInstanceValid(_target))
			return _target;

		_target = null;
		foreach (Node node in GetTree().GetNodesInGroup("player"))
		{
			if (node is Node3D player)
			{
				_target = player;
				break;
			}
		}

		return _target;
	}
}
