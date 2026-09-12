using System.Collections.Generic;
using Oniblade.Progression;

namespace Oniblade.Dialogue;

/// <summary>
/// 台词集合的类别。它是 03 §2.7「80/20 纪律」的载体——**引导必须是少数**，
/// 而且这个比例是能被断言的（见 <see cref="DialogueCatalogue.GuidanceRatio"/>），
/// 不是口头约定。
/// </summary>
public enum DialogueTone
{
	/// <summary>闲聊。占多数。</summary>
	Chitchat,

	/// <summary>引导。占少数，而且**是提示不是答案**。</summary>
	Guidance,
}

/// <summary>一句台词。说话人、文本、立绘路径全在数据里，代码里一个字都不写（04 §2）。</summary>
public sealed class DialogueLine
{
	public string Speaker { get; init; } = "";
	public string Text { get; init; } = "";

	/// <summary>可选立绘。空字符串 = 不显示立绘（只有文字）。</summary>
	public string PortraitPath { get; init; } = "";

	public bool HasPortrait => !string.IsNullOrEmpty(PortraitPath);

	/// <summary>没有说话人的行就是旁白。</summary>
	public bool IsNarration => string.IsNullOrEmpty(Speaker);
}

/// <summary>
/// 一组台词，带**侵蚀分档变体**：进入「共鸣」「同化」之后，关于「那个声音」
/// 的那一组会换一版（T30 的核心要求，也接上了 T19 已经算出来的侵蚀度）。
///
/// 分档变体挂在**集合**这一层而不是逐行，是因为 03 §2.7 说的是
/// "她关于那个声音的台词换一版"——换的是**整段对话**，不是某一句。
/// </summary>
public sealed class DialogueSet
{
	public string Id { get; init; } = "";

	/// <summary>这组台词在讲什么（给维护者看的，不进游戏）。</summary>
	public string Summary { get; init; } = "";

	public DialogueTone Tone { get; init; } = DialogueTone.Chitchat;

	/// <summary>基础版（初鸣阶段，或没有分档变体时始终用它）。</summary>
	public IReadOnlyList<DialogueLine> Lines { get; init; } = new List<DialogueLine>();

	/// <summary>共鸣阶段的替换版。空 = 沿用基础版。</summary>
	public IReadOnlyList<DialogueLine> ResonanceLines { get; init; } = new List<DialogueLine>();

	/// <summary>同化阶段的替换版。空 = 沿用基础版。</summary>
	public IReadOnlyList<DialogueLine> AssimilationLines { get; init; } = new List<DialogueLine>();

	/// <summary>按当前侵蚀阶段取该说的那一版。</summary>
	public IReadOnlyList<DialogueLine> LinesFor(ErosionStage stage) => stage switch
	{
		ErosionStage.Assimilation when AssimilationLines.Count > 0 => AssimilationLines,
		ErosionStage.Resonance when ResonanceLines.Count > 0 => ResonanceLines,
		_ => Lines,
	};

	/// <summary>三个阶段加起来一共多少行（报告与自检用）。</summary>
	public int TotalLineCount => Lines.Count + ResonanceLines.Count + AssimilationLines.Count;
}
