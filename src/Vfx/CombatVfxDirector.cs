using Godot;
using Oniblade.Combat;
using Oniblade.Core;

namespace Oniblade.Vfx;

/// <summary>
/// 战斗特效层（T28 / 10 §4）。
///
/// **它只订阅 <see cref="EventBus.HitResolved"/>，绝不反过来影响战斗逻辑**（04 §4 的订阅纪律）。
/// 这条界线是可验证的：测试会灌给它一个 <c>Damage = 999</c> 的 Hit 事件，
/// 然后断言场上没有任何单位掉血——特效层不许有"顺手结算一下"的权力。
///
/// 降级按 10 §5 的顺序：**① 体积雾 → ② 粒子数量**。
/// 降级里没有"降分辨率/降帧率"——60fps 优先于画面（01 §0 的精神）。
/// 全屏闪**不参与降级**：一闪是核心反馈，宁可砍别的。
/// </summary>
public partial class CombatVfxDirector : Node
{
	public static CombatVfxDirector? Instance { get; private set; }

	/// <summary>低于这个帧率就降一级。</summary>
	[Export] public int DegradeFpsThreshold { get; set; } = 55;

	/// <summary>回到这个帧率以上才恢复一级。与降级阈值留出间隙，避免在边界上抖动。</summary>
	[Export] public int RecoverFpsThreshold { get; set; } = 58;

	/// <summary>0 = 全开，1 = 砍雾，2 = 再砍粒子。</summary>
	public int DegradationLevel { get; private set; }

	public int DeflectSparkCount { get; private set; }
	public int ClashSparkCount { get; private set; }
	public int BloodMistCount { get; private set; }
	public int ScreenFlashCount { get; private set; }

	/// <summary>因为降级而没有生成的次数（调试面板与测试用）。</summary>
	public int SkippedByDegradationCount { get; private set; }

	public override void _EnterTree()
	{
		Instance = this;
		AddToGroup("combat_vfx");

		// 订阅放在 _EnterTree：autoload 之间只保证 _EnterTree 的先后顺序，
		// 放 _Ready 有可能晚于别人发出的第一条事件。
		if (EventBus.Instance is { } bus)
			bus.HitResolved += OnHitResolved;
	}

	public override void _ExitTree()
	{
		if (EventBus.Instance is { } bus)
			bus.HitResolved -= OnHitResolved;

		if (Instance == this)
			Instance = null;
	}

	public override void _PhysicsProcess(double delta) => UpdateDegradation();

	/// <summary>被 <see cref="ForceDegradationLevel"/> 钉住之后，自动降级让位。</summary>
	private bool _degradationPinned;

	private void UpdateDegradation()
	{
		if (_degradationPinned)
			return;

		double fps = Engine.GetFramesPerSecond();

		// headless 与首帧读不到有效值，别让 0 把画质一路降到最低。
		if (fps <= 0)
			return;

		if (fps < DegradeFpsThreshold && DegradationLevel < 2)
			DegradationLevel++;
		else if (fps > RecoverFpsThreshold && DegradationLevel > 0)
			DegradationLevel--;
	}

	/// <summary>
	/// 强制钉住降级等级，并**停掉自动降级**。给测试与调试面板用。
	///
	/// 必须真的"钉住"而不只是"设一次值"：headless 下 <c>Engine.GetFramesPerSecond()</c>
	/// 读数很低，自动降级会**每帧升一级**，十来帧就把画质压到最低——
	/// 于是"等级 0 该出粒子"这条断言会莫名其妙地失败（而且失败得很随机）。
	/// </summary>
	public void ForceDegradationLevel(int level)
	{
		DegradationLevel = Mathf.Clamp(level, 0, 2);
		_degradationPinned = true;
	}

	/// <summary>恢复自动降级。</summary>
	public void ResumeAutoDegradation() => _degradationPinned = false;

	private void OnHitResolved(HitEvent e)
	{
		switch (e.Verdict)
		{
			case Verdict.Deflect: SpawnSpark(e, clash: false); break;
			case Verdict.Clash: SpawnSpark(e, clash: true); break;
			case Verdict.Hit: SpawnMist(e); break;
			case Verdict.Issen: SpawnIssen(e); break;
		}
	}

	private void SpawnSpark(in HitEvent e, bool clash)
	{
		// 等级 2 才砍粒子——降级顺序是"先砍雾"。
		if (DegradationLevel >= 2)
		{
			SkippedByDegradationCount++;
			return;
		}

		AddChild(SparkBurst.Create(e.Position, e.Direction, clash));

		if (clash)
			ClashSparkCount++;
		else
			DeflectSparkCount++;
	}

	private void SpawnMist(in HitEvent e)
	{
		if (DegradationLevel >= 1)
		{
			SkippedByDegradationCount++;
			return;
		}

		AddChild(BloodMist.Create(e.Position, e.Direction));
		BloodMistCount++;
	}

	/// <summary>一闪 = 全屏高对比闪 + 血雾（10 §4）。全屏闪不参与降级。</summary>
	private void SpawnIssen(in HitEvent e)
	{
		AddChild(ScreenFlash.Create(0.85f));
		ScreenFlashCount++;

		SpawnMist(e);
	}
}
