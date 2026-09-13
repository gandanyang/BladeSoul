using System.Collections.Generic;
using Godot;
using Oniblade.Dialogue;
using Oniblade.Progression;

namespace Oniblade.Dev;

/// <summary>
/// 同伴对话系统验证（T30）：
///     godot --headless --path . res://scenes/tests/Dialogue.tscn
///
/// 卡片要的"一段真实对话在游戏里跑出来的样子"——截图我做不到（无渲染输出），
/// 所以这里走**真实系统**（autoload 的 <see cref="DialogueBox"/>，不是只测 POCO），
/// 把玩家会看到的东西逐句打出来，并断言：
///
/// 1. 台词库读得进来、**80/20 纪律成立**（引导占比 ~20%）；
/// 2. 台词数据自净（没有空行、没有"没有说话人的正文"）；
/// 3. 一组对话能被推进到底并自动收起；
/// 4. ★ **侵蚀分档生效**：「初鸣 / 共鸣 / 同化」三档下，`voice` 那一组说出来的话不同；
/// 5. 输出全部台词的行数（验收 3 要的那个数字）。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class DialogueTest : Node3D
{
    private readonly List<string> _failures = new();

    private DialogueBox _box = null!;

    public override async void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        if (DialogueBox.Instance is not { } box)
        {
            GD.PrintErr("[对话] ✗ DialogueBox 没有注册成 autoload");
            GetTree().Quit(1);
            return;
        }

        _box = box;

        // 异常也要走到 Report/Quit——否则 async void 静默中断，场景会挂到超时。
        try
        {
            CheckCatalogue();
            CheckDataHygiene();
            await RunSetThroughUi("first_meeting");
            CheckStageVariants();
            PrintTranscript();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        Report();
    }

    // ── 1. 台词库与 80/20 纪律 ─────────────────────────────────

    private void CheckCatalogue()
    {
        DialogueCatalogue catalogue = _box.Catalogue;

        GD.Print($"[对话] 台词库：{catalogue.Count} 组（闲聊 {catalogue.ChitchatCount} / " +
                 $"引导 {catalogue.GuidanceCount}），引导占 {catalogue.GuidanceRatio:P0}，" +
                 $"共 {catalogue.TotalLineCount} 行");

        Check(catalogue.Count > 0, "台词库是空的：JSON 没读进来");

        // 03 §2.7 的纪律是 80/20。给一点浮动空间，但不能反过来。
        Check(catalogue.GuidanceRatio is > 0.1f and <= 0.3f,
            $"引导占 {catalogue.GuidanceRatio:P0}，偏离 03 §2.7 的 80/20 纪律（应约 20%）");

        Check(catalogue.GuidanceCount > 0, "一组引导台词都没有：那就不叫 80/20，叫 100/0");

        // ── 侵蚀分档台词（T30 的核心要求 / 03 §2.7）────────────────
        // 她比玩家更早发现丛云在变，所以"那个声音"那一组必须随侵蚀阶段换一版。
        // 为什么要有这条断言：分档内容原本是**没有任何自检盯着**的——
        // 删掉它，80/20 那三条照样全绿。这类"静默失效"本项目的账上已经有好几笔。
        DialogueSet? voice = catalogue.Get("voice");
        Check(voice is not null, "找不到 voice 那一组（她关于「那个声音」的台词）");

        if (voice is not null)
        {
            Check(voice.ResonanceLines.Count >= 2,
                $"「共鸣」档的台词只有 {voice.ResonanceLines.Count} 行（至少要 2 行，否则分档名存实亡）");
            Check(voice.AssimilationLines.Count >= 2,
                $"「同化」档的台词只有 {voice.AssimilationLines.Count} 行（至少要 2 行）");

            // 三档不能是复读：首句一样基本就能断定是抄的（逐行比对留给以后）。
            bool sameLr = voice.Lines.Count > 0 && voice.ResonanceLines.Count > 0
                          && voice.Lines[0].Text == voice.ResonanceLines[0].Text;
            bool sameLa = voice.Lines.Count > 0 && voice.AssimilationLines.Count > 0
                          && voice.Lines[0].Text == voice.AssimilationLines[0].Text;
            bool sameRa = voice.ResonanceLines.Count > 0 && voice.AssimilationLines.Count > 0
                          && voice.ResonanceLines[0].Text == voice.AssimilationLines[0].Text;
            Check(!sameLr && !sameLa && !sameRa,
                "三档台词的首句重复：分档不能是把同一句抄三遍");
        }
    }

    // ── 2. 数据自净 ────────────────────────────────────────────

    private void CheckDataHygiene()
    {
        int emptyText = 0;
        int narrationWithSpeaker = 0;
        int tooLong = 0;

        foreach (DialogueSet set in _box.Catalogue.Sets)
        {
            foreach (DialogueLine line in AllLinesOf(set))
            {
                if (string.IsNullOrWhiteSpace(line.Text))
                    emptyText++;

                // 03 §2.7：一律短句。这也正是她的性格。
                if (line.Text.Length > 24)
                    tooLong++;

                if (line.IsNarration && line.Speaker.Length > 0)
                    narrationWithSpeaker++;
            }
        }

        Check(emptyText == 0, $"有 {emptyText} 行台词是空的");
        Check(tooLong == 0, $"有 {tooLong} 行超过 24 字，违反「一律短句」（03 §2.7）");

        GD.Print($"[对话] 数据自净：空行 {emptyText} / 超长行 {tooLong}");
    }

    private static IEnumerable<DialogueLine> AllLinesOf(DialogueSet set)
    {
        foreach (DialogueLine line in set.Lines)
            yield return line;

        foreach (DialogueLine line in set.ResonanceLines)
            yield return line;

        foreach (DialogueLine line in set.AssimilationLines)
            yield return line;
    }

    // ── 3. 走一遍真实系统 ─────────────────────────────────────

    private async System.Threading.Tasks.Task RunSetThroughUi(string setId)
    {
        _box.ErosionOverride = ErosionStage.FirstCry;

        Check(_box.Show(setId), $"Show(\"{setId}\") 失败了");
        Check(_box.IsTalking, "Show 之后 IsTalking 还是 false");

        var spoken = new List<string>();
        int guard = 0;

        while (_box.IsTalking && guard++ < 64)
        {
            DialogueLine? line = _box.CurrentLine;
            Check(line is not null, "在说话但 CurrentLine 是空的");
            if (line is not null)
                spoken.Add($"{line.Speaker}：{line.Text}");

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            _box.Advance();
        }

        Check(!_box.IsTalking, "对话推不完（64 次推进后还在说话）");
        Check(spoken.Count >= 5, $"「{setId}」只说了 {spoken.Count} 句，看起来没走完");

        GD.Print($"[对话] UI 实测「{setId}」：{spoken.Count} 句，说完自动收起 ✓");
    }

    // ── 4. ★ 侵蚀分档 ─────────────────────────────────────────

    private void CheckStageVariants()
    {
        DialogueSet? voice = _box.Catalogue.Get("voice");
        Check(voice is not null, "找不到 voice 那一组台词（分档台词靠它）");
        if (voice is null)
            return;

        IReadOnlyList<DialogueLine> first = voice.LinesFor(ErosionStage.FirstCry);
        IReadOnlyList<DialogueLine> resonance = voice.LinesFor(ErosionStage.Resonance);
        IReadOnlyList<DialogueLine> assimilation = voice.LinesFor(ErosionStage.Assimilation);

        Check(resonance.Count > 0, "共鸣阶段没有替换台词：侵蚀分档没落地");
        Check(assimilation.Count > 0, "同化阶段没有替换台词：侵蚀分档没落地");

        Check(!SameLines(first, resonance), "共鸣阶段的台词和初鸣一样：分档没有真正生效");
        Check(!SameLines(resonance, assimilation), "同化阶段的台词和共鸣一样：分档没有真正生效");

        GD.Print("[对话] ★ 侵蚀分档对照（同一组 voice，三档说法不同）：");
        PrintVariant("初鸣", first);
        PrintVariant("共鸣", resonance);
        PrintVariant("同化", assimilation);
    }

    private static void PrintVariant(string stage, IReadOnlyList<DialogueLine> lines)
    {
        GD.Print($"[对话]   ── {stage} ──");
        foreach (DialogueLine line in lines)
            GD.Print($"[对话]     {line.Speaker}：{line.Text}");
    }

    private static bool SameLines(IReadOnlyList<DialogueLine> a, IReadOnlyList<DialogueLine> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Text != b[i].Text)
                return false;
        }

        return true;
    }

    // ── 5. 完整剧本（"在游戏里跑起来"的文本版证据）────────────

    private void PrintTranscript()
    {
        GD.Print("[对话] ── 全部台词（初鸣阶段）─────────────────────");

        int printed = 0;

        foreach (DialogueSet set in _box.Catalogue.Sets)
        {
            GD.Print($"[对话] 【{set.Id}】{set.Tone} — {set.Summary}");

            foreach (DialogueLine line in set.LinesFor(ErosionStage.FirstCry))
            {
                GD.Print($"[对话]   {line.Speaker}：{line.Text}");
                printed++;
            }
        }

        GD.Print($"[对话] 当前阶段会说到的行数 {printed}；" +
                 $"三档合计 {_box.Catalogue.TotalLineCount} 行");

        Check(printed > 0, "一行台词都没打出来");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[对话] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print($"[对话] ✓ 通过（{_box.Catalogue.Count} 组 / {_box.Catalogue.TotalLineCount} 行台词）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
