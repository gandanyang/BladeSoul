using System.Collections.Generic;
using Oniblade.Progression;

namespace Oniblade.Dialogue;

/// <summary>
/// 推进一组台词（POCO，可单测）。
///
/// **不做分支、不做选项、不做好感度**——01 §7 的"不做清单"里写着"复杂对话系统"。
/// 它只有三个动作：开始、下一句、停。台词本身决定了它是闲聊还是引导。
/// </summary>
public sealed class DialogueRunner
{
	private IReadOnlyList<DialogueLine> _lines = new List<DialogueLine>();

	/// <summary>当前正在说的那一组；null = 没在说话。</summary>
	public DialogueSet? Set { get; private set; }

	/// <summary>当前行号（-1 = 还没开始）。</summary>
	public int Index { get; private set; } = -1;

	/// <summary>本次实际采用的那一版（受侵蚀阶段影响）。</summary>
	public IReadOnlyList<DialogueLine> Lines => _lines;

	public bool IsActive => Set is not null;

	public bool IsFinished => !IsActive || Index >= _lines.Count;

	public DialogueLine? Current =>
		IsActive && Index >= 0 && Index < _lines.Count ? _lines[Index] : null;

	/// <summary>开始说一组台词。<paramref name="stage"/> 决定用哪一版（侵蚀分档）。</summary>
	public void Start(DialogueSet set, ErosionStage stage)
	{
		Set = set;
		_lines = set.LinesFor(stage);
		Index = _lines.Count > 0 ? 0 : 0;
	}

	/// <summary>下一句。返回 false 表示这一组说完了（此时 <see cref="Set"/> 已清空）。</summary>
	public bool Advance()
	{
		if (!IsActive)
			return false;

		Index++;

		if (Index >= _lines.Count)
		{
			Stop();
			return false;
		}

		return true;
	}

	public void Stop()
	{
		Set = null;
		_lines = new List<DialogueLine>();
		Index = -1;
	}
}
