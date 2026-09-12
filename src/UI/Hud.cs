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
		UpdateFocus();
		UpdatePulse();
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

	private void BuildUi()
	{
		// 未锁定敌人的头顶细条（T31 / 11 §4.2）。**先加**，这样它画在玩家面板、
		// 敌方面板与边缘脉冲的下层——它是最弱的一层信息，不该盖住结算提示。
		EnemyBars bars = new() { Name = "EnemyBars" };
		AddChild(bars);
		Bars = bars;

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

		// 伤害数字归 HUD 拥有（它自己也订阅 HitResolved）
		DamageNumbers numbers = new() { Name = "DamageNumbers" };
		AddChild(numbers);
		Numbers = numbers;
	}

	/// <summary>伤害数字层（自检与调试用）。</summary>
	public DamageNumbers Numbers { get; private set; } = null!;

	/// <summary>未锁定敌人的头顶细条（自检与调试用）。</summary>
	public EnemyBars Bars { get; private set; } = null!;

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
