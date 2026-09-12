using System.Collections.Generic;
using Godot;

namespace Oniblade.Dialogue;

/// <summary>
/// 台词库。从 <c>data/dialogue/*.json</c> 读——**文本不进代码**（04 §2）。
///
/// 用 JSON 而不是 <c>.tres</c>：卡片允许两者，而这份数据是**带分档变体的长文本**
/// （十几组、每组 2~6 行），做成 .tres 的 sub_resource 数组会有几百行样板，
/// 改一句台词要翻半天——对"编剧要反复改词"这件事是灾难。
/// 数值仍然是 .tres（00 §2.6 的纪律管的是数字，不是台词）。
/// </summary>
public sealed class DialogueCatalogue
{
	private readonly List<DialogueSet> _sets = new();
	private readonly Dictionary<string, DialogueSet> _byId = new();

	public IReadOnlyList<DialogueSet> Sets => _sets;

	public int Count => _sets.Count;

	public int ChitchatCount
	{
		get
		{
			int count = 0;
			foreach (DialogueSet set in _sets)
			{
				if (set.Tone == DialogueTone.Chitchat)
					count++;
			}

			return count;
		}
	}

	public int GuidanceCount => Count - ChitchatCount;

	/// <summary>引导占比。03 §2.7 的纪律是 **~20%**。</summary>
	public float GuidanceRatio => Count == 0 ? 0f : GuidanceCount / (float)Count;

	/// <summary>三次侵蚀阶段加起来一共多少行台词（完成报告里要报的那个数）。</summary>
	public int TotalLineCount
	{
		get
		{
			int total = 0;
			foreach (DialogueSet set in _sets)
				total += set.TotalLineCount;

			return total;
		}
	}

	/// <summary>读取失败时返回空库并报错——**不抛异常**：一句台词读不到不该让游戏崩掉。</summary>
	public static DialogueCatalogue Load(string resourcePath)
	{
		var catalogue = new DialogueCatalogue();

		using FileAccess? file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
		if (file is null)
		{
			GD.PushError($"[对话] 读不到台词文件：{resourcePath}（{FileAccess.GetOpenError()}）");
			return catalogue;
		}

		Variant parsed = Json.ParseString(file.GetAsText());
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"[对话] 台词文件不是合法的 JSON 对象：{resourcePath}");
			return catalogue;
		}

		if (!parsed.AsGodotDictionary().TryGetValue("sets", out Variant setsVariant)
			|| setsVariant.VariantType != Variant.Type.Array)
		{
			GD.PushError($"[对话] 台词文件里没有 sets 数组：{resourcePath}");
			return catalogue;
		}

		foreach (Variant entry in setsVariant.AsGodotArray())
		{
			if (entry.VariantType != Variant.Type.Dictionary)
				continue;

			DialogueSet set = ParseSet(entry.AsGodotDictionary());
			if (string.IsNullOrEmpty(set.Id))
				continue;

			catalogue._sets.Add(set);
			catalogue._byId[set.Id] = set;
		}

		return catalogue;
	}

	public DialogueSet? Get(string id) => _byId.TryGetValue(id, out DialogueSet? set) ? set : null;

	public DialogueSet? At(int index) => index >= 0 && index < _sets.Count ? _sets[index] : null;

	/// <summary>按 id 取；取不到就回退到第 0 组（调试热键靠它循环）。</summary>
	public DialogueSet? GetOrFirst(string id) => Get(id) ?? At(0);

	private static DialogueSet ParseSet(Godot.Collections.Dictionary source)
	{
		return new DialogueSet
		{
			Id = ReadString(source, "id"),
			Summary = ReadString(source, "summary"),
			Tone = ReadString(source, "tone") == "guidance" ? DialogueTone.Guidance : DialogueTone.Chitchat,
			Lines = ParseLines(source, "lines"),
			ResonanceLines = ParseLines(source, "resonanceLines"),
			AssimilationLines = ParseLines(source, "assimilationLines"),
		};
	}

	private static IReadOnlyList<DialogueLine> ParseLines(Godot.Collections.Dictionary source, string key)
	{
		var lines = new List<DialogueLine>();

		if (!source.TryGetValue(key, out Variant value) || value.VariantType != Variant.Type.Array)
			return lines;

		foreach (Variant entry in value.AsGodotArray())
		{
			if (entry.VariantType != Variant.Type.Dictionary)
				continue;

			Godot.Collections.Dictionary row = entry.AsGodotDictionary();
			lines.Add(new DialogueLine
			{
				Speaker = ReadString(row, "speaker"),
				Text = ReadString(row, "text"),
				PortraitPath = ReadString(row, "portrait"),
			});
		}

		return lines;
	}

	private static string ReadString(Godot.Collections.Dictionary source, string key) =>
		source.TryGetValue(key, out Variant value) ? value.AsString() : string.Empty;
}
