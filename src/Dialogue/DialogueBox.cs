using Godot;
using Oniblade.Progression;

namespace Oniblade.Dialogue;

/// <summary>
/// 对话/旁白 UI 与推进器（T30）。极简：**文字 + 说话人 + 可选立绘**，
/// 不做分支树、不做好感度、不做语音、不做嘴部动画、不做镜头演出。
///
/// 台词全部来自 <c>data/dialogue/ayame.json</c>，本文件里一个字都不写（04 §2）。
///
/// **触发条件不是本卡的范围**（走到她身边、剧情节点、按键交互……都属于后续的关卡接入），
/// 所以这里留了一个调试热键：<b>F9 循环下一组、F10 收起</b>——
/// 这样"这段对话在游戏里真的跑起来了"这件事现在就能被看见，
/// 而不必等触发系统做完。
/// </summary>
public partial class DialogueBox : CanvasLayer
{
	public const string DefaultCataloguePath = "res://data/dialogue/ayame.json";

	/// <summary>低于一闪的全屏闪（90），高于 HUD。</summary>
	public const int BoxLayer = 80;

	public static DialogueBox? Instance { get; private set; }

	[Export] public string CataloguePath { get; set; } = DefaultCataloguePath;

	/// <summary>说话人那行字的字号。</summary>
	[Export] public int SpeakerFontSize { get; set; } = 20;

	/// <summary>正文的字号。</summary>
	[Export] public int TextFontSize { get; set; } = 22;

	private readonly DialogueRunner _runner = new();

	private PanelContainer _panel = null!;
	private Label _speakerLabel = null!;
	private Label _textLabel = null!;
	private Label _hintLabel = null!;
	private TextureRect _portrait = null!;

	private int _debugSetIndex = -1;

	/// <summary>覆盖侵蚀阶段（测试与调试用）。null = 去场上找笼手读真实值。</summary>
	public ErosionStage? ErosionOverride { get; set; }

	public DialogueCatalogue Catalogue { get; private set; } = new();

	public DialogueRunner Runner => _runner;

	public bool IsTalking => _runner.IsActive;

	public DialogueLine? CurrentLine => _runner.Current;

	public override void _EnterTree()
	{
		Instance = this;
		Layer = BoxLayer;

		Catalogue = DialogueCatalogue.Load(CataloguePath);
		BuildUi();
		SetUiVisible(false);

		GD.Print($"[对话] 台词库：{Catalogue.Count} 组（闲聊 {Catalogue.ChitchatCount} / " +
				 $"引导 {Catalogue.GuidanceCount}，引导占 {Catalogue.GuidanceRatio:P0}），" +
				 $"共 {Catalogue.TotalLineCount} 行；F9 循环、F10 收起");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	// ── 对外接口 ───────────────────────────────────────────────

	/// <summary>说一组台词。取不到 id 就报错并返回 false（不崩）。</summary>
	public bool Show(string setId)
	{
		DialogueSet? set = Catalogue.Get(setId);
		if (set is null)
		{
			GD.PushError($"[对话] 没有这组台词：{setId}");
			return false;
		}

		StartSet(set);
		return true;
	}

	/// <summary>
	/// 用**外部台词表**说一组（T47：序章第一幕的师父台词走这条路）。
	///
	/// 为什么不直接换掉 <see cref="Catalogue"/>：那个是本盒子的常驻台词表（绫的那份），
	/// F9/F10 的调试循环、以及 80/20 那几条自检都盯着它。**临时借一张表来播**，
	/// 播完盒子还是原来那个盒子——这样加教学台词就不会连累已经通过的对话自检。
	/// </summary>
	public bool ShowExternal(DialogueCatalogue catalogue, string setId)
	{
		DialogueSet? set = catalogue.Get(setId);
		if (set is null)
		{
			GD.PushError($"[对话] 外部台词表里没有这组：{setId}");
			return false;
		}

		StartSet(set);
		return true;
	}

	/// <summary>推进一句。说完了自动收起。</summary>
	public void Advance()
	{
		if (!_runner.IsActive)
			return;

		_runner.Advance();

		if (!_runner.IsActive)
		{
			SetUiVisible(false);
			return;
		}

		Refresh();
	}

	public void Stop()
	{
		_runner.Stop();
		SetUiVisible(false);
	}

	/// <summary>按当前侵蚀阶段取该说的那一版（T30 的分档台词靠它生效）。</summary>
	public ErosionStage CurrentErosionStage()
	{
		if (ErosionOverride is { } forced)
			return forced;

		foreach (Node node in GetTree().GetNodesInGroup(OniGauntlet.GroupName))
		{
			if (node is OniGauntlet gauntlet)
				return gauntlet.Stage;
		}

		return ErosionStage.FirstCry;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsTalking && @event.IsActionPressed("interact"))
		{
			Advance();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;

		switch (key.Keycode)
		{
			case Key.F9:
				ShowNextDebugSet();
				GetViewport().SetInputAsHandled();
				break;

			case Key.F10:
				Stop();
				GetViewport().SetInputAsHandled();
				break;
		}
	}

	// ── 内部 ───────────────────────────────────────────────────

	private void StartSet(DialogueSet set)
	{
		ErosionStage stage = CurrentErosionStage();
		_runner.Start(set, stage);
		Refresh();
		SetUiVisible(true);

		GD.Print($"[对话] {set.Id} [{set.Tone}] 侵蚀阶段 {stage} → {_runner.Lines.Count} 行");
	}

	private void ShowNextDebugSet()
	{
		if (Catalogue.Count == 0)
			return;

		_debugSetIndex = (_debugSetIndex + 1) % Catalogue.Count;

		DialogueSet? set = Catalogue.At(_debugSetIndex);
		if (set is null)
			return;

		StartSet(set);
	}

	private void Refresh()
	{
		DialogueLine? line = _runner.Current;

		if (line is null)
		{
			SetUiVisible(false);
			return;
		}

		_speakerLabel.Text = line.Speaker;
		_textLabel.Text = line.Text;
		_hintLabel.Text = _runner.Index < _runner.Lines.Count - 1 ? "▼" : "■";
		_portrait.Visible = line.HasPortrait;

		if (line.HasPortrait && ResourceLoader.Exists(line.PortraitPath))
			_portrait.Texture = GD.Load<Texture2D>(line.PortraitPath);
	}

	private void SetUiVisible(bool visible)
	{
		_panel.Visible = visible;

		if (!visible)
			_portrait.Visible = false;
	}

	/// <summary>UI 全部用代码搭：这一层只有三个 Label，做成 .tscn 反而更难维护。</summary>
	private void BuildUi()
	{
		_portrait = new TextureRect
		{
			Name = "Portrait",
			// 立绘还没做（09 §10）：没有纹理时这里就是个空位，不影响文字。
			CustomMinimumSize = new Vector2(160f, 220f),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(_portrait);
		_portrait.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_portrait.Position = new Vector2(48f, -260f);

		_panel = new PanelContainer
		{
			Name = "Panel",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(_panel);
		_panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_panel.OffsetLeft = 48f;
		_panel.OffsetRight = -48f;
		_panel.OffsetTop = -172f;
		_panel.OffsetBottom = -40f;

		var column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
		_panel.AddChild(column);

		_speakerLabel = new Label
		{
			Name = "Speaker",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(0.92f, 0.78f, 0.55f),
		};
		_speakerLabel.AddThemeFontSizeOverride("font_size", SpeakerFontSize);
		column.AddChild(_speakerLabel);

		_textLabel = new Label
		{
			Name = "Text",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_textLabel.AddThemeFontSizeOverride("font_size", TextFontSize);
		column.AddChild(_textLabel);

		_hintLabel = new Label
		{
			Name = "Hint",
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(1f, 1f, 1f, 0.5f),
		};
		column.AddChild(_hintLabel);
	}
}
