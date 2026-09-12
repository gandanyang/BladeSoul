using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Core;

namespace Oniblade.UI;

/// <summary>
/// 伤害数字（T31 / 11 §5.2）。从命中点飘出、0.6 秒淡出。
///
/// 三条纪律：
/// 1. **只显示玩家造成的伤害**。玩家自己挨打用"血条闪白 + 屏幕边缘暗红"表达——
///    自己身上飘红字会让画面变脏，而且是负反馈，我们不需要强化它。
/// 2. **一闪用更大字号与不同颜色**——那是"大数字"的时刻。
/// 3. **池化**：`_Process` 里禁止 `new`，所以 Label 一次建好、循环复用。
/// </summary>
public partial class DamageNumbers : Control
{
	public const string PlayerGroup = "player";

	/// <summary>每个数字的固定宽度：给了它才能把数字**居中落在命中点**上。</summary>
	private const float BadgeWidth = 160f;

	[Export] public int PoolSize { get; set; } = 24;
	[Export] public int LifetimeFrames { get; set; } = 36;     // 0.6s @60fps
	[Export] public int FontSize { get; set; } = 18;
	[Export] public int IssenFontSize { get; set; } = 30;
	[Export] public Color DamageColor { get; set; } = new(0.92f, 0.90f, 0.84f);
	[Export] public Color IssenColor { get; set; } = new(1f, 0.86f, 0.86f);

	/// <summary>飘出高度（像素）。</summary>
	[Export] public float RisePixels { get; set; } = 46f;

	/// <summary>新数字出现时向上错开的距离，避免同一位置叠在一起。</summary>
	[Export] public float StackOffsetPixels { get; set; } = 16f;

	// ── 只读观测值（无头自检用）──
	public int Spawned { get; private set; }
	public int IssenSpawned { get; private set; }
	public int ActiveCount { get; private set; }

	private Label[] _pool = System.Array.Empty<Label>();
	private int[] _framesLeft = System.Array.Empty<int>();
	private Vector2[] _origin = System.Array.Empty<Vector2>();
	private int _next;
	private int _stackIndex;
	private ICombatActorDebug? _player;
	private int _playerActorId;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);

		_pool = new Label[Mathf.Max(1, PoolSize)];
		_framesLeft = new int[_pool.Length];
		_origin = new Vector2[_pool.Length];

		for (int i = 0; i < _pool.Length; i++)
		{
			var label = new Label
			{
				Name = $"Damage{i}",
				Visible = false,
				MouseFilter = MouseFilterEnum.Ignore,
				HorizontalAlignment = HorizontalAlignment.Center,
				CustomMinimumSize = new Vector2(BadgeWidth, 0f),
			};
			label.AddThemeFontSizeOverride("font_size", FontSize);
			AddChild(label);
			_pool[i] = label;
		}

		if (EventBus.Instance is { } bus)
			bus.HitResolved += OnHitResolved;
	}

	public override void _ExitTree()
	{
		if (EventBus.Instance is { } bus)
			bus.HitResolved -= OnHitResolved;
	}

	public override void _Process(double delta)
	{
		int active = 0;

		for (int i = 0; i < _pool.Length; i++)
		{
			if (_framesLeft[i] <= 0)
				continue;

			_framesLeft[i]--;
			int elapsed = LifetimeFrames - _framesLeft[i];

			if (_framesLeft[i] == 0)
			{
				_pool[i].Visible = false;
				continue;
			}

			active++;

			// 向上飘 + 淡出
			float t = elapsed / (float)LifetimeFrames;
			_pool[i].Position = _origin[i] - new Vector2(0f, RisePixels * t);
			Color color = _pool[i].GetThemeColor("font_color");
			color.A = 1f - t * t;
			_pool[i].AddThemeColorOverride("font_color", color);
		}

		ActiveCount = active;
	}

	private void OnHitResolved(HitEvent e)
	{
		ResolvePlayer();

		// 只显示玩家造成的伤害（11 §5.2）
		if (_player is null || e.AttackerId != _playerActorId)
			return;

		bool issen = e.Verdict == Verdict.Issen;
		int amount = e.Damage;

		// 一闪即使伤害数字为 0（对 BOSS 只削体干）也要出字，那是"大数字的时刻"
		if (!issen && amount <= 0)
			return;

		Spawn(issen ? $"一闪 {amount}" : amount.ToString(), e.Position, issen);
	}

	private void Spawn(string text, Vector3 worldPosition, bool issen)
	{
		int index = _next;
		_next = (_next + 1) % _pool.Length;

		Label label = _pool[index];
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", issen ? IssenFontSize : FontSize);
		label.AddThemeColorOverride("font_color", issen ? IssenColor : DamageColor);
		label.Visible = true;

		_origin[index] = ProjectToScreen(worldPosition)
			- new Vector2(BadgeWidth / 2f, _stackIndex * StackOffsetPixels);
		label.Position = _origin[index];
		_framesLeft[index] = LifetimeFrames;

		_stackIndex = (_stackIndex + 1) % 4;
		Spawned++;

		if (issen)
			IssenSpawned++;
	}

	/// <summary>世界坐标 → 屏幕像素。没有相机（无头/未设主相机）时退到画面中心。</summary>
	private Vector2 ProjectToScreen(Vector3 worldPosition)
	{
		Camera3D? camera = GetViewport()?.GetCamera3D();

		if (camera is null)
			return GetViewportRect().Size / 2f;

		return camera.UnprojectPosition(worldPosition);
	}

	private void ResolvePlayer()
	{
		if (_player is not null)
			return;

		foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
		{
			if (node is ICombatActorDebug debug)
			{
				_player = debug;
				_playerActorId = debug.ActorId;
				return;
			}
		}
	}
}
