using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.World;

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

	/// <summary>
	/// 降级等级**不再由这里持有**——统一听 <see cref="Oniblade.World.QualityDirector"/>
	/// （08 §3 P1-3：一个数字只能有一个来源）。
	/// 07 §7 的降级顺序跨三个系统（雾 / 粒子 / 同屏敌人数），
	/// 各系统各自按 FPS 判断会出现"雾砍了粒子还在"的中间态，而且彼此震荡。
	///
	/// 场景里没有 QualityDirector 时退化为 0（不降级）。
	/// </summary>
	public int DegradationLevel => QualityDirector.Instance?.Level ?? 0;

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

	/// <summary>强制钉住降级等级（转发给 QualityDirector）。测试与调试面板用。</summary>
	public void ForceDegradationLevel(int level) => QualityDirector.Instance?.ForceLevel(level);

	/// <summary>恢复自动降级（转发给 QualityDirector）。</summary>
	public void ResumeAutoDegradation() => QualityDirector.Instance?.ResumeAuto();

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
