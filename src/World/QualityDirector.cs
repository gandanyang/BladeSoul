using Godot;

namespace Oniblade.World;

/// <summary>
/// 全局画质降级（T34）。**唯一的降级旋钮**——雾、粒子、同屏敌人数都看它。
///
/// 为什么必须只有一处：07 §7 定的降级顺序是
/// **① 体积雾 → ② 粒子 → ③ 同屏敌人数**，跨三个系统。
/// 如果每个系统各自按 FPS 判断，同一帧里会出现"雾砍了但粒子还在"的中间态，
/// 而且彼此震荡（一个降了帧率回升，另一个又升回去）。
/// 这与 08 §3 P1-3「一个数字只能有一个来源」是同一条纪律。
///
/// 场景里放一个实例即可；没有它的场景里 <see cref="Instance"/> 为 null，
/// 各系统退化为"不降级"——训练场景不需要降级。
/// </summary>
public partial class QualityDirector : Node
{
	public static QualityDirector? Instance { get; private set; }

	/// <summary>低于这个帧率就降一级。</summary>
	[Export] public int DegradeFpsThreshold { get; set; } = 55;

	/// <summary>回到这个帧率以上才升一级。与降级阈值留出间隙，避免在边界上抖动。</summary>
	[Export] public int RecoverFpsThreshold { get; set; } = 58;

	/// <summary>最高等级：0 全开 / 1 砍雾 / 2 再砍粒子 / 3 再砍同屏敌人数。</summary>
	public const int MaxLevel = 3;

	/// <summary>0 = 全开，1 = 砍雾，2 = 再砍粒子，3 = 再砍同屏敌人数。</summary>
	public int Level { get; private set; }

	private bool _pinned;

	public override void _EnterTree()
	{
		Instance = this;
		AddToGroup("quality_director");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_pinned)
			return;

		double fps = Engine.GetFramesPerSecond();

		// headless 与首帧读不到有效值。不挡的话 0 会把画质一路压到最低，
		// 而所有无头自检都会跑在"画质最低"的状态下——那等于没测。
		if (fps <= 0)
			return;

		if (fps < DegradeFpsThreshold && Level < MaxLevel)
			Level++;
		else if (fps > RecoverFpsThreshold && Level > 0)
			Level--;
	}

	/// <summary>
	/// 钉住等级并停掉自动降级。给测试与调试面板用。
	/// 必须真的"钉住"：headless 下 FPS 读数很低，自动降级会每帧升一级，
	/// 十来帧就把画质压到底（T28 的第一版就是这么翻车的）。
	/// </summary>
	public void ForceLevel(int level)
	{
		Level = Mathf.Clamp(level, 0, MaxLevel);
		_pinned = true;
	}

	public void ResumeAuto() => _pinned = false;

	/// <summary>雾是否还该开（等级 ≥1 就砍）。</summary>
	public bool FogAllowed => Level < 1;

	/// <summary>粒子是否还该开（等级 ≥2 就砍）。</summary>
	public bool ParticlesAllowed => Level < 2;

	/// <summary>同屏敌人倍率（等级 ≥3 才减半）。</summary>
	public float EnemyCountScale => Level >= MaxLevel ? 0.5f : 1f;
}
