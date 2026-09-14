using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;

namespace Oniblade.UI;

/// <summary>
/// 战斗 HUD（T31 / 11 文档）。**它只读**：订阅 <see cref="EventBus.HitResolved"/> 取结算，
/// 每帧从 <see cref="ICombatActorDebug"/> 轮询血与体干。绝不反向影响战斗逻辑。
///
/// 为什么轮询而不是订阅事件：实测 <c>EventBus.HealthChanged</c> / <c>PostureChanged</c>
/// **从来没有人发**（战斗侧只在裁决里改数值）。11 §6 允许走
/// "ICombatActorDebug ＋ Godot 组"这条路，所以这里直接轮询——
/// 少一层需要在战斗代码里插桩的耦合。
///
/// ★ 这套 UI 里最重要的是**敌方架势槽**（11 §4.3）：没有它，弹开只是"打中了"；
/// 有了它，弹开才是"我在推进"（01 §1 的北极星）。
/// </summary>
public partial class Hud : CanvasLayer
{
	public static Hud? Instance { get; private set; }

	public const int HudLayer = 50;      // 低于对话(80)与一闪全屏闪(90)
	public const string PlayerGroup = "player";
	public const string ActorGroup = "combat_actor";

	/// <summary>
	/// 处决参数（`data/combat/deathblow.tres`，T52）。
	///
	/// 这里挂它是为了让**处决标记的距离上限与处决判定同源**：
	/// UI 与战斗各写一份 2.2 的话，迟早会出现"标记亮着但按 F 没反应"（或反过来）。
	///
	/// 留空时会**自动加载**那个 .tres（见 <see cref="ResolveDeathblowProfile"/>）——
	/// Hud 是 Autoload，没有 .tscn 可以给它配导出值，所以不能只靠"让策划记得填"。
	/// </summary>
	[Export] public Combat.Data.DeathblowProfile? Deathblow { get; set; }

	/// <summary>处决参数资源路径（自动加载用）。</summary>
	public const string DeathblowProfilePath = "res://data/combat/deathblow.tres";

	[ExportGroup("玩家面板（左下）")]
	[Export] public Color PlayerHealthColor { get; set; } = new(0.851f, 0.788f, 0.659f);   // #D9C9A8 暖白
	[Export] public Color PlayerPostureColor { get; set; } = new(0.624f, 0.702f, 0.722f);  // #9FB3B8 浅青灰
	[Export] public Color TrackColor { get; set; } = new(0.10f, 0.11f, 0.12f, 0.85f);
	[Export] public Color HealDotColor { get; set; } = new(0.72f, 0.62f, 0.42f);
	[Export] public Color HealDotSpentColor { get; set; } = new(0.22f, 0.22f, 0.24f);
	[Export] public Vector2 PlayerPanelOffset { get; set; } = new(28f, 30f);
	[Export] public Vector2 PlayerBarSize { get; set; } = new(240f, 12f);
	[Export] public float PlayerPostureBarHeight { get; set; } = 8f;

	/// <summary>延迟回落速度（比例/秒）。让玩家看清"这一下扣了多少"（11 §3）。</summary>
	[Export] public float DrainSpeed { get; set; } = 0.55f;

	[ExportGroup("敌方面板（上方居中）")]
	[Export] public Color EnemyHealthColor { get; set; } = new(0.478f, 0.078f, 0.094f);    // #7A1418 暗红
	[Export] public Color EnemyPostureColor { get; set; } = new(0.784f, 0.196f, 0.227f);   // #C8323A 血红
	[Export] public Vector2 EnemyPanelSize { get; set; } = new(420f, 46f);
	[Export] public float EnemyPanelTop { get; set; } = 26f;
	[Export] public int FocusHoldFrames { get; set; } = 240;
	[Export] public float FocusRange { get; set; } = 14f;

	[ExportGroup("战斗反馈")]
	[Export] public Color DeflectPulseColor { get; set; } = new(0.749f, 0.890f, 1.0f);     // #BFE3FF 白蓝
	[Export] public Color IssenPulseColor { get; set; } = new(1f, 1f, 1f);
	[Export] public Color BlockPulseColor { get; set; } = new(0.55f, 0.60f, 0.62f);
	[Export] public Color PlayerHitPulseColor { get; set; } = new(0.44f, 0.06f, 0.08f);

	/// <summary>
	/// 破防脉冲（T37 缺口②）：**高对比白**，与格挡的青灰必须一眼分开——
	/// 之前两者合并成同一路，所以"玩家破防"和"普通格挡"在屏幕上根本分不出来。
	/// </summary>
	[Export] public Color GuardBreakPulseColor { get; set; } = new(1f, 1f, 1f);

	[Export] public int PulseFrames { get; set; } = 18;
	[Export] public int PromptFrames { get; set; } = 34;
	[Export] public int PromptFontSize { get; set; } = 26;
	[Export] public Color PromptColor { get; set; } = new(0.90f, 0.95f, 1f);

	[ExportGroup("破防预警（T37 缺口②）")]

	/// <summary>
	/// 玩家体干到这条线就开始给持续预警——让玩家提前知道"再挡一下就破"（11 §4.3 的下半截）。
	/// 在那之前破防是"突然发生"的，玩家学不会。
	/// </summary>
	[Export] public float PostureWarnRatio { get; set; } = 0.75f;

	/// <summary>
	/// 预警色：**青白，不是暖色**——暖色只允许出现在灯笼上（07 §7 / 10 §1）。
	/// 危险的表达靠**亮度**，不靠色相：灰度下也读得出来（05 §4.3）。
	/// </summary>
	[Export] public Color PostureWarnColor { get; set; } = new(0.82f, 0.92f, 0.97f);

	/// <summary>预警边缘的最大不透明度。刻意很弱——它是持续状态，不能盖过结算脉冲。</summary>
	[Export] public float PostureWarnMaxAlpha { get; set; } = 0.22f;

	/// <summary>半自动防御剩余次数的弱提示（制作人裁定 2026-09-13：资源必须可见）。</summary>
	[Export] public int HalfAutoChargesFontSize { get; set; } = 12;

	/// <summary>文字提示（「弹开」「一闪」）。**它是脚手架**，手感验证通过后应默认关掉（11 §5.1）。</summary>
	[Export] public bool ShowTextPrompts { get; set; } = true;

	[ExportGroup("弹开连击（×n）")]

	/// <summary>
	/// 连击数颜色。**与弹开脉冲同色**——同一件事（你弹开了）只许有一种颜色语言，
	/// 换色等于让玩家重新学一遍。暖色仍然只属于灯笼（07 §7）。
	/// </summary>
	[Export] public Color DeflectChainColor { get; set; } = new(0.749f, 0.890f, 1.0f);   // #BFE3FF

	/// <summary>
	/// 连击数基准字号。**第一版给的是 34，实机截图发现它比「弹开」提示（26）还不起眼**——
	/// 而连击是比单次弹开更重要的信息（后者已经有脉冲/音效/光环在三处说同一句话）。
	/// 提到 40 并加大每连的步进，让"这一串有多长"从字号上就读得出来。
	/// </summary>
	[Export] public int DeflectChainFontSize { get; set; } = 40;

	/// <summary>
	/// 从第几连开始显示。默认 **2**：弹开一下已经有脉冲 + 提示 + 音高了，
	/// 再挂一个「×1」只是噪音；「×2」才是"我连上了"这件事第一次成立的信号。
	/// </summary>
	[Export] public int DeflectChainMinToShow { get; set; } = 2;

	/// <summary>每多一连加大几号字（上限 4 连，见 <see cref="DeflectChainShown"/> 那段的算式）。</summary>
	[Export] public int DeflectChainFontStep { get; set; } = 4;

	/// <summary>断连后的淡出帧数。**要淡出而不是瞬间消失**——否则玩家看不到"它没了"。</summary>
	[Export] public int DeflectChainFadeFrames { get; set; } = 36;

	/// <summary>
	/// 相对屏幕底部居中的位置（与 Prompt 同一条中轴，压在它上面）。
	/// **不能再低了**：实机截图里 -222 正好压住弹开光环的上沿，
	/// 「×2」被光环切掉一块——读数被压住等于没有读数。
	/// </summary>
	[Export] public Vector2 DeflectChainOffset { get; set; } = new(0f, -256f);

	[ExportGroup("暂存（测试用）")]
	[Export] public bool Enable { get; set; } = true;

	// ── 只读观测值：无头自检靠这些断言，不靠看画面 ──
	public float PlayerHealthRatio { get; private set; } = 1f;
	public float PlayerPostureRatio { get; private set; }
	public int HealDotsLit { get; private set; }
	public int FocusActorId { get; private set; }
	public float FocusHealthRatio { get; private set; }
	public float FocusPostureRatio { get; private set; }
	public string FocusName { get; private set; } = "";

	public int DeflectPrompts { get; private set; }
	public int IssenPrompts { get; private set; }
	public int BlockFlashes { get; private set; }
	public int PlayerHitFlashes { get; private set; }

	/// <summary>破防提示次数（T37 缺口②）。**与 <see cref="BlockFlashes"/> 分开计**——合并就分不出两者了。</summary>
	public int GuardBreakFlashes { get; private set; }

	/// <summary>体干已进入预警区（"再挡一下就破"），11 §4.3 的下半截。</summary>
	public bool PostureWarnActive { get; private set; }

	/// <summary>半自动防御剩余次数（制作人裁定：资源要可见）。</summary>
	public int HalfAutoChargesLeft { get; private set; }

	/// <summary>
	/// 当前屏显的弹开连击数（0 = 不挂）。**自检读这个数，不去读标签文本**——
	/// 文本是表现，数字才是语义。
	/// </summary>
	public int DeflectChainShown { get; private set; }

	/// <summary>连击数是否在屏幕上（含断连后的淡出尾巴）。</summary>
	public bool DeflectChainVisible { get; private set; }

	public string LastPrompt { get; private set; } = "";

	private ICombatActorDebug? _player;
	private Node3D? _playerNode;

	private Control _playerPanel = null!;
	private ColorRect _healthFill = null!;
	private ColorRect _healthLag = null!;
	private ColorRect _postureFill = null!;
	private ColorRect _postureTrack = null!;
	private ColorRect[] _healDots = System.Array.Empty<ColorRect>();

	private Control _enemyPanel = null!;
	private Label _focusNameLabel = null!;
	private ColorRect _enemyHealthFill = null!;
	private ColorRect _enemyPostureFill = null!;

	private ColorRect[] _edges = System.Array.Empty<ColorRect>();
	private Label _prompt = null!;

	private float _healthLagRatio = 1f;

	/// <summary>体干轨道的底边（相对玩家面板左上角）。填充从这条线往上长。</summary>
	private float _postureTrackBottom;
	private int _pulseFramesLeft;
	private Color _pulseColor = Colors.White;
	private int _promptFramesLeft;

	private int _focusActorId;
	private int _focusHoldFramesLeft;
	private ICombatActorDebug? _focus;
	private int _lastHealDots = -1;
	private Label _halfAutoLabel = null!;
	private Label _deflectChainLabel = null!;
	private int _deflectChainFadeLeft;

	/// <summary>见过非零的半自动次数（= 当前难度档有这个机制）。非見習档整行不显示。</summary>
	private bool _halfAutoSeen;

	public override void _EnterTree()
	{
		Instance = this;
		Layer = HudLayer;
		BuildUi();

		if (EventBus.Instance is { } bus)
			bus.HitResolved += OnHitResolved;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;

		if (EventBus.Instance is { } bus)
			bus.HitResolved -= OnHitResolved;
	}

	public override void _Process(double delta)
	{
		UpdatePlayerPanel(delta);
		UpdateDeflectChain();
		UpdateFocus();
		UpdatePulse();
	}

	// ── 弹开连击（T51 遗留①）───────────────────────────────────

	/// <summary>
	/// 连着弹开了几次。**这是 M1 那条验收标准（"弹开成功时会想再试一次"）里
	/// 唯一还没被表达出来的东西**：单次弹开的反馈其实早就齐了（脉冲、提示、音高、
	/// 火花、架势槽 +18），但"我连上了"没有任何读数——
	/// 玩家只能靠记，而记不住的进步等于没有进步。
	///
	/// 数据来源走**轮询**而不是订阅事件，与血/体干同一条路（见类注释）：
	/// <see cref="ICombatActorDebug.DeflectChain"/> 就是权威值，它自带保持窗口
	/// （<c>CombatActor.DeflectChainWindowFrames</c>），所以超时归零会自动反映到屏幕上，
	/// HUD 不需要自己再数一遍——**每多一处自己数的地方，就多一个会和战斗分家的数**。
	/// </summary>
	private void UpdateDeflectChain()
	{
		if (!Enable || _player is null)
		{
			ApplyDeflectChain(0);
			return;
		}

		ApplyDeflectChain(_player.DeflectChain);
	}

	private void ApplyDeflectChain(int chain)
	{
		if (chain < DeflectChainMinToShow)
			chain = 0;                      // 「×1」不显示：见 DeflectChainMinToShow 的注释

		if (chain != DeflectChainShown)
		{
			DeflectChainShown = chain;

			if (chain > 0)
			{
				_deflectChainLabel.Text = $"×{chain}";

				// 连得越多越大：字号本身就是"这一串有多长"的读数
				int step = Mathf.Min(chain - DeflectChainMinToShow, 4);
				_deflectChainLabel.AddThemeFontSizeOverride("font_size",
					DeflectChainFontSize + step * DeflectChainFontStep);
			}
		}

		if (chain > 0)
			_deflectChainFadeLeft = DeflectChainFadeFrames;
		else if (_deflectChainFadeLeft > 0)
			_deflectChainFadeLeft--;

		DeflectChainVisible = chain > 0 || _deflectChainFadeLeft > 0;
		_deflectChainLabel.Visible = DeflectChainVisible;

		if (DeflectChainVisible)
		{
			Color color = DeflectChainColor;
			color.A = chain > 0 ? 1f : _deflectChainFadeLeft / (float)Mathf.Max(1, DeflectChainFadeFrames);
			_deflectChainLabel.AddThemeColorOverride("font_color", color);
		}
	}

	// ── 结算反馈（11 §5.1）─────────────────────────────────────

	private void OnHitResolved(HitEvent e)
	{
		ResolvePlayer();

		if (_player is null)
			return;

		// 玩家是攻方 → 记下被打的那个敌人作为焦点（架势槽要跟着它跳）
		if (e.AttackerId == _player.ActorId)
		{
			_focusActorId = e.DefenderId;
			_focusHoldFramesLeft = FocusHoldFrames;
			_focus = null;      // 下一帧按 id 重新解析
			return;
		}

		if (e.DefenderId != _player.ActorId)
			return;

		switch (e.Verdict)
		{
			case Verdict.Deflect:
				DeflectPrompts++;
				Pulse(DeflectPulseColor);
				Prompt("弹开");
				break;

			case Verdict.Issen:
				IssenPrompts++;
				Pulse(IssenPulseColor);
				Prompt("一闪");
				break;

			case Verdict.Block:
				BlockFlashes++;
				Pulse(BlockPulseColor);
				break;

			// T37 缺口②：破防必须与普通格挡**分开**。
			// 之前两者走同一路，玩家破防时只会看到和格挡一模一样的青灰脉冲——
			// 于是"我卡住了"这件事没有任何解释。
			case Verdict.GuardBreak:
				GuardBreakFlashes++;
				Pulse(GuardBreakPulseColor);
				Prompt("破防");
				break;

			case Verdict.Hit:
				PlayerHitFlashes++;
				Pulse(PlayerHitPulseColor);
				break;
		}
	}

	private void Pulse(Color color)
	{
		_pulseColor = color;
		_pulseFramesLeft = PulseFrames;
	}

	private void Prompt(string text)
	{
		LastPrompt = text;

		if (!ShowTextPrompts)
			return;

		_prompt.Text = text;
		_promptFramesLeft = PromptFrames;
	}

	// ── 轮询 ───────────────────────────────────────────────────

	private void ResolvePlayer()
	{
		if (_player is not null && GodotObject.IsInstanceValid((GodotObject)_player))
			return;

		_player = null;
		_playerNode = null;

		foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
		{
			if (node is ICombatActorDebug debug && node is Node3D node3D)
			{
				_player = debug;
				_playerNode = node3D;
				return;
			}
		}
	}

	private void UpdatePlayerPanel(double delta)
	{
		ResolvePlayer();

		if (!Enable || _player is null)
			return;

		PlayerHealthRatio = Ratio(_player.Health, _player.MaxHealth);
		PlayerPostureRatio = Ratio(_player.Posture, _player.MaxPosture);

		// 延迟回落：主条立刻降，滞留条慢慢追 —— "这一下扣了多少"要看得见
		if (_healthLagRatio < PlayerHealthRatio)
			_healthLagRatio = PlayerHealthRatio;
		else if (_healthLagRatio > PlayerHealthRatio)
			_healthLagRatio = Mathf.Max(PlayerHealthRatio, _healthLagRatio - DrainSpeed * (float)delta);

		_healthFill.OffsetRight = PlayerBarSize.X * PlayerHealthRatio;

		_healthLag.OffsetLeft = PlayerBarSize.X * PlayerHealthRatio;
		_healthLag.OffsetRight = PlayerBarSize.X * _healthLagRatio;
		_healthLag.Visible = _healthLag.OffsetRight > _healthLag.OffsetLeft + 0.5f;

		// 体干条**向上生长**（05 §4.3：形状区分优先于颜色，灰度下也要分得清）
		// ★ 必须绑到轨道自己的底边上：OffsetTop/OffsetBottom 是相对锚点的绝对偏移，
		// 只写 -height/0 会把它画到面板顶边之外（截图里才发现，数值断言查不出来）。
		float postureHeight = PlayerPostureBarHeight * PlayerPostureRatio;
		_postureFill.OffsetTop = _postureTrackBottom - postureHeight;
		_postureFill.OffsetBottom = _postureTrackBottom;

		if (_player.HealChargesLeft != _lastHealDots)
		{
			_lastHealDots = _player.HealChargesLeft;
			HealDotsLit = _lastHealDots;

			for (int i = 0; i < _healDots.Length; i++)
				_healDots[i].Color = i < _lastHealDots ? HealDotColor : HealDotSpentColor;
		}

		// T37 缺口②：体干进入预警区就持续给信号——破防不该是"突然发生"的。
		PostureWarnActive = PlayerPostureRatio >= PostureWarnRatio;

		// T37 缺口③：半自动防御剩余次数。资源不可见就没法管理（制作人裁定）。
		int charges = _player.HalfAutoGuardChargesLeft;

		if (charges != HalfAutoChargesLeft)
		{
			HalfAutoChargesLeft = charges;

			if (charges > 0)
				_halfAutoSeen = true;   // 只要见过非零，就说明这一档有这个机制

			UpdateHalfAutoLabel();
		}
	}

	/// <summary>半自动防御的弱提示：非見習档没有这个机制，整行不显示（不要挂一行 0 在屏幕上）。</summary>
	private void UpdateHalfAutoLabel()
	{
		_halfAutoLabel.Visible = _halfAutoSeen;

		if (_halfAutoSeen)
			_halfAutoLabel.Text = $"自动防御 {HalfAutoChargesLeft}";
	}

	private void UpdateFocus()
	{
		if (!Enable || _player is null || _playerNode is null)
		{
			_enemyPanel.Visible = false;
			return;
		}

		if (_focusHoldFramesLeft > 0)
			_focusHoldFramesLeft--;
		else
		{
			_focusActorId = 0;
			_focus = null;
		}

		// `_focus` 是**跨帧缓存**：敌人被打死后 Godot 节点先释放，字段里那个
		// C# 引用却仍非 null、指针已失效——后面的 `??=` 便永不重查，
		// 于是每帧抛 ObjectDisposedException（TutorialDirector 那个坑实测刷了 755 次）。
		// 失效就丢弃，让下面的 `??=` 去重选一个活着的目标。
		if (_focus is not null && !GodotObject.IsInstanceValid((GodotObject)_focus))
			_focus = null;

		ICombatActorDebug? target = _focus;

		if (target is null && _focusActorId != 0)
			target = _focus = FindByActorId(_focusActorId);

		target ??= PickNearestEnemy();

		if (target is null)
		{
			_enemyPanel.Visible = false;
			FocusActorId = 0;
			FocusName = "";
			return;
		}

		_enemyPanel.Visible = true;
		FocusActorId = target.ActorId;
		FocusHealthRatio = Ratio(target.Health, target.MaxHealth);
		FocusPostureRatio = Ratio(target.Posture, target.MaxPosture);

		if (FocusName != target.DebugName)
		{
			FocusName = target.DebugName;
			_focusNameLabel.Text = target.DebugName;
		}

		// 敌人条**左右收缩**（与玩家条的"向上生长"形成形状差异）
		LayoutCentered(_enemyHealthFill, EnemyPanelSize.X, FocusHealthRatio);
		LayoutCentered(_enemyPostureFill, EnemyPanelSize.X, FocusPostureRatio);
	}

	private ICombatActorDebug? FindByActorId(int actorId)
	{
		foreach (Node node in GetTree().GetNodesInGroup(ActorGroup))
		{
			if (node is ICombatActorDebug { ActorId: var id } debug && id == actorId)
				return debug;
		}

		return null;
	}

	/// <summary>
	/// 挑焦点目标。**真正的锁定系统（LockOnSystem）还没做**（文件不存在），
	/// 所以这里自己选：刚被玩家打过的那个优先（带保持计时），否则取范围内最近的敌人。
	/// 锁定系统做出来之后，把这里换成读它即可——HUD 只是消费者。
	/// </summary>
	private ICombatActorDebug? PickNearestEnemy()
	{
		if (_playerNode is null || _player is null)
			return null;

		Vector3 origin = _playerNode.GlobalPosition;
		float best = FocusRange * FocusRange;
		ICombatActorDebug? found = null;

		foreach (Node node in GetTree().GetNodesInGroup(ActorGroup))
		{
			if (node is not ICombatActorDebug debug || node is not Node3D actor)
				continue;

			if (ReferenceEquals(node, (Node)_player))
				continue;

			float distance = actor.GlobalPosition.DistanceSquaredTo(origin);
			if (distance >= best)
				continue;

			best = distance;
			found = debug;
		}

		return found;
	}

	private static float Ratio(int value, int max) =>
		max <= 0 ? 0f : Mathf.Clamp(value / (float)max, 0f, 1f);

	private void UpdatePulse()
	{
		ApplyEdgeTint();

		if (_promptFramesLeft > 0 && --_promptFramesLeft == 0)
			_prompt.Text = "";
	}

	/// <summary>
	/// 屏幕边缘的颜色。两种来源共用这四条窄带，**瞬时事件优先于持续状态**：
	/// 结算脉冲（弹开/一闪/格挡/挨打/破防）盖过体干预警。
	/// </summary>
	private void ApplyEdgeTint()
	{
		if (_pulseFramesLeft > 0)
		{
			_pulseFramesLeft--;
			Color color = _pulseColor;
			color.A = _pulseFramesLeft / (float)Mathf.Max(1, PulseFrames) * 0.55f;

			foreach (ColorRect edge in _edges)
				edge.Color = color;

			return;
		}

		// T37 缺口②：没有脉冲时给"体干快满"让位。越接近满越亮，但**上限很低**
		// （它是持续状态，不能盖过结算提示，更不能变成第二种血条）。
		if (PostureWarnActive)
		{
			float span = Mathf.Max(0.01f, 1f - PostureWarnRatio);
			float strength = Mathf.Clamp((PlayerPostureRatio - PostureWarnRatio) / span, 0.15f, 1f);
			Color warn = PostureWarnColor;
			warn.A = PostureWarnMaxAlpha * strength;

			foreach (ColorRect edge in _edges)
				edge.Color = warn;

			return;
		}

		if (_edges.Length > 0 && _edges[0].Color.A > 0f)
		{
			foreach (ColorRect edge in _edges)
				edge.Color = new Color(0f, 0f, 0f, 0f);
		}
	}

	// ── 布局 ───────────────────────────────────────────────────

	/// <summary>
	/// 拿到处决参数：优先用导出的（策划可在编辑器覆盖），没配就自动加载那个 .tres。
	///
	/// 自动加载这一步是必要的，**不是偷懒**：<c>Hud</c> 在 `project.godot` 里是
	/// **Autoload**（没有 .tscn），所以没有"场景导出值"可填。只靠 `[Export]` 的话，
	/// 它永远是 null，标记就会退回保守默认值 —— 而这正是"两份距离不一致"的来源。
	///
	/// 加载失败也不抛：标记层退化成"什么都不显示"，游戏照常能玩（反馈层不该让游戏崩）。
	/// </summary>
	private Combat.Data.DeathblowProfile? ResolveDeathblowProfile()
	{
		if (Deathblow is not null)
			return Deathblow;

		Deathblow = GD.Load<Combat.Data.DeathblowProfile>(DeathblowProfilePath);

		if (Deathblow is null)
			GD.PushWarning($"Hud: 加载 {DeathblowProfilePath} 失败，处决标记将不显示任何目标");

		return Deathblow;
	}

	private void BuildUi()
	{
		// 未锁定敌人的头顶细条（T31 / 11 §4.2）。**先加**，这样它画在玩家面板、
		// 敌方面板与边缘脉冲的下层——它是最弱的一层信息，不该盖住结算提示。
		EnemyBars bars = new() { Name = "EnemyBars" };
		AddChild(bars);
		Bars = bars;

		// 处决标记（T52 / 卡片验收："破韧 → 头顶亮忍杀标记"）。
		//
		// 与 EnemyBars 同一手法（纯代码建、不放进 .tscn）：Hud 是 Autoload，
		// 它的 UI 全部由 BuildUi() 组装，半途插一个场景节点反而会让层级难查。
		//
		// **放在 EnemyBars 之后、玩家面板之前**：处决标记是"现在立刻要做的动作"，
		// 比敌人的血条更紧急，所以画在血条上层；但它仍不该盖住玩家的血/架势槽。
		DeathblowMarker marker = new()
		{
			Name = "DeathblowMarker",
			Deathblow = ResolveDeathblowProfile(),
		};
		AddChild(marker);
		DeathblowMarkers = marker;

		Vector2 size = PlayerBarSize;
		float panelWidth = size.X;
		float panelHeight = size.Y * 2f + PlayerPostureBarHeight + 24f;

		_playerPanel = MakeControl("PlayerPanel", 0f, 1f,
			PlayerPanelOffset.X, -(panelHeight + PlayerPanelOffset.Y), panelWidth, panelHeight);

		MakeRect(_playerPanel, TrackColor, 0f, 0f, size.X, size.Y);
		_healthLag = MakeRect(_playerPanel, new Color(1f, 1f, 1f, 0.35f), 0f, 0f, 0f, size.Y);
		_healthFill = MakeRect(_playerPanel, PlayerHealthColor, 0f, 0f, size.X, size.Y);

		// 体干：轨在上、填充从轨底往上长（OffsetTop 为负、OffsetBottom 为 0）
		float postureTrackTop = size.Y + 5f;
		_postureTrackBottom = postureTrackTop + PlayerPostureBarHeight;
		_postureTrack = MakeRect(_playerPanel, TrackColor, 0f, postureTrackTop, size.X, PlayerPostureBarHeight);
		_postureFill = MakeRect(_playerPanel, PlayerPostureColor,
			0f, postureTrackTop + PlayerPostureBarHeight, size.X, 0f);

		_healDots = new ColorRect[8];
		for (int i = 0; i < _healDots.Length; i++)
			_healDots[i] = MakeRect(_playerPanel, HealDotSpentColor,
				2f + i * 15f, postureTrackTop + PlayerPostureBarHeight + 7f, 10f, 10f);

		// 半自动防御剩余次数（T37 缺口③ / 制作人裁定）。刻意很弱：
		// 青灰小字、不抢眼——**暖色只属于灯笼**（07 §7），这里连暖色的边都不许沾。
		_halfAutoLabel = new Label
		{
			Name = "HalfAutoCharges",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_halfAutoLabel.AddThemeFontSizeOverride("font_size", HalfAutoChargesFontSize);
		_halfAutoLabel.AddThemeColorOverride("font_color", PlayerPostureColor);
		_halfAutoLabel.OffsetLeft = 2f;
		_halfAutoLabel.OffsetRight = panelWidth;
		_halfAutoLabel.OffsetTop = postureTrackTop + PlayerPostureBarHeight + 20f;
		_halfAutoLabel.OffsetBottom = _halfAutoLabel.OffsetTop + 14f;
		_playerPanel.AddChild(_halfAutoLabel);

		// 敌方面板（上方居中）：名字 + 血条 + 架势槽
		_enemyPanel = MakeControl("EnemyPanel", 0.5f, 0f,
			-EnemyPanelSize.X / 2f, EnemyPanelTop, EnemyPanelSize.X, EnemyPanelSize.Y);

		_focusNameLabel = new Label
		{
			Name = "FocusName",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_focusNameLabel.AddThemeFontSizeOverride("font_size", 15);
		_focusNameLabel.AnchorRight = 1f;
		_focusNameLabel.OffsetBottom = 18f;
		_enemyPanel.AddChild(_focusNameLabel);

		MakeRect(_enemyPanel, TrackColor, 0f, 20f, EnemyPanelSize.X, 10f);
		_enemyHealthFill = MakeRect(_enemyPanel, EnemyHealthColor, 0f, 20f, EnemyPanelSize.X, 10f);
		MakeRect(_enemyPanel, TrackColor, 0f, 33f, EnemyPanelSize.X, 6f);
		_enemyPostureFill = MakeRect(_enemyPanel, EnemyPostureColor, 0f, 33f, EnemyPanelSize.X, 6f);

		// 屏幕边缘脉冲（11 §5.1 第 2 层）：四条窄带
		_edges = new[]
		{
			MakeEdge("Top", 0f, 0f, 1f, 0f, 0f, 0f, 40f),
			MakeEdge("Bottom", 0f, 1f, 1f, 1f, 0f, -40f, 40f, vertical: true),
			MakeEdge("Left", 0f, 0f, 0f, 1f, 0f, 0f, 40f, vertical: true),
			MakeEdge("Right", 1f, 0f, 1f, 1f, -40f, 0f, 40f, vertical: true),
		};

		_prompt = new Label
		{
			Name = "Prompt",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_prompt.AddThemeFontSizeOverride("font_size", PromptFontSize);
		_prompt.AddThemeColorOverride("font_color", PromptColor);
		_prompt.AnchorRight = 1f;
		_prompt.AnchorTop = 1f;
		_prompt.AnchorBottom = 1f;
		_prompt.OffsetTop = -150f;
		_prompt.OffsetBottom = -118f;
		AddChild(_prompt);

		// 弹开连击「×n」（T51 遗留①）。与 Prompt 同一条中轴、压在它上面：
		// 提示说"这一次你弹开了"，连击数说"你连着弹开了几次"——同一位置的两句话，
		// 才不会让玩家的视线在两处之间跳。
		_deflectChainLabel = new Label
		{
			Name = "DeflectChain",
			Visible = false,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_deflectChainLabel.AddThemeFontSizeOverride("font_size", DeflectChainFontSize);
		_deflectChainLabel.AnchorRight = 1f;
		_deflectChainLabel.AnchorTop = 1f;
		_deflectChainLabel.AnchorBottom = 1f;
		_deflectChainLabel.OffsetTop = DeflectChainOffset.Y;
		_deflectChainLabel.OffsetBottom = DeflectChainOffset.Y + 46f;
		AddChild(_deflectChainLabel);

		// 伤害数字归 HUD 拥有（它自己也订阅 HitResolved）
		DamageNumbers numbers = new() { Name = "DamageNumbers" };
		AddChild(numbers);
		Numbers = numbers;
	}

	/// <summary>伤害数字层（自检与调试用）。</summary>
	public DamageNumbers Numbers { get; private set; } = null!;

	/// <summary>未锁定敌人的头顶细条（自检与调试用）。</summary>
	public EnemyBars Bars { get; private set; } = null!;

	/// <summary>
	/// 处决标记层（T52，自检与调试用）。
	///
	/// 它读的是 <see cref="Combat.ICombatActorDebug.CanBeExecuted"/> ——
	/// **只读接口，不是具体敌人类型**（docs/00 §2.8）。
	/// </summary>
	public DeathblowMarker DeathblowMarkers { get; private set; } = null!;

	/// <summary>建一个 Control 并**入树**（锚点相对父节点）。</summary>
	private Control MakeControl(string name, float anchorX, float anchorY,
		float left, float top, float width, float height)
	{
		var control = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
		control.AnchorLeft = anchorX;
		control.AnchorRight = anchorX;
		control.AnchorTop = anchorY;
		control.AnchorBottom = anchorY;
		control.OffsetLeft = left;
		control.OffsetTop = top;
		control.OffsetRight = left + width;
		control.OffsetBottom = top + height;
		AddChild(control);
		return control;
	}

	private static ColorRect MakeRect(Control parent, Color color, float x, float y, float w, float h)
	{
		var rect = new ColorRect
		{
			Color = color,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			OffsetLeft = x,
			OffsetTop = y,
			OffsetRight = x + w,
			OffsetBottom = y + h,
		};
		parent.AddChild(rect);
		return rect;
	}

	private ColorRect MakeEdge(string name, float left, float top, float right, float bottom,
		float offsetX, float offsetY, float thickness, bool vertical = false)
	{
		var edge = new ColorRect
		{
			Name = $"Edge{name}",
			Color = new Color(0f, 0f, 0f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		edge.AnchorLeft = left;
		edge.AnchorTop = top;
		edge.AnchorRight = right;
		edge.AnchorBottom = bottom;

		if (vertical)
		{
			edge.OffsetLeft = offsetX;
			edge.OffsetRight = offsetX + thickness;
			edge.OffsetTop = 0f;
			edge.OffsetBottom = 0f;
		}
		else
		{
			edge.OffsetLeft = 0f;
			edge.OffsetRight = 0f;
			edge.OffsetTop = offsetY;
			edge.OffsetBottom = offsetY + thickness;
		}

		AddChild(edge);
		return edge;
	}

	private static void LayoutCentered(ColorRect fill, float trackWidth, float ratio)
	{
		float width = trackWidth * ratio;
		fill.OffsetLeft = (trackWidth - width) / 2f;
		fill.OffsetRight = (trackWidth + width) / 2f;
	}
}
