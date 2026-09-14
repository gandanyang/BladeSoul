using Godot;
using Oniblade.Audio;
using Oniblade.Combat.Data;

namespace Oniblade.Dev;

/// <summary>
/// 音频资源泄漏归属探针。
///
/// ★ 为什么需要它：Godot 退出时会报
/// <c>ERROR: N resources still in use at exit</c>，但**不告诉你是谁**。
/// 于是每加一个音效都会被怀疑"是不是我漏了"，而它其实是
/// <c>AudioDirector._cache</c>（<c>src/Audio/AudioDirector.cs</c> 的
/// <c>Dictionary&lt;string, AudioStream?&gt;</c>）把**播过的**音效流钉到了进程结束
/// ——<c>AudioDirector</c> 是 Autoload，退出时仍然存活，缓存里的东西就都算"在用"。
///
/// 本探针把这条账**量出来**：播放过的不同音效数 == 报出来的资源数。
///
/// 用法（无头）：
/// <code>
/// godot --headless --path . res://scenes/tests/AudioLeakProbe.tscn --            # 只载入数据集
/// godot --headless --path . res://scenes/tests/AudioLeakProbe.tscn -- Deflect    # 播 1 个
/// godot --headless --path . res://scenes/tests/AudioLeakProbe.tscn -- Clash,Deflect,DeflectThrust,DeflectBlunt,DeflectDark
/// </code>
/// 实测（2026-09-15）：<c>(无参数)→0</c>、<c>Deflect→1</c>、四档→4、拼刀+四档→5。
/// 数据集（1 set + 4 profile）在无参数模式下报 0 ⇒ **数据资源本身不泄漏**。
///
/// 这是**手动诊断工具**，不接 check.ps1（它永远退 0，做不了门禁）。
/// </summary>
public partial class AudioLeakProbe : Node
{
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();

        // ① 数据集本身是否泄漏（1 个 set + 4 个 profile）
        DeflectFeedbackSet? set = DeflectFeedbackSet.Load();
        GD.Print($"[泄漏探针] 数据集：set={(set is not null)} " +
                 $"Sfx={set?.Slash?.Sfx}/{set?.Thrust?.Sfx}/{set?.Blunt?.Sfx}/{set?.Dark?.Sfx}");

        // ② 按参数播放指定音效
        int played = 0;
        if (args.Length > 0)
        {
            AudioDirector? audio = GetNodeOrNull<AudioDirector>("/root/AudioDirector");
            if (audio is null)
            {
                GD.PrintErr("[泄漏探针] 取不到 /root/AudioDirector");
                GetTree().Quit(1);
                return;
            }

            foreach (string raw in string.Join(",", args).Split(',', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (!System.Enum.TryParse(name, out CombatSfx sfx))
                {
                    GD.PrintErr($"[泄漏探针] 不认识音效名：{name}");
                    GetTree().Quit(1);
                    return;
                }

                audio.PlayCombat(sfx);
                played++;
            }
        }

        GD.Print($"[泄漏探针] 已播 {played} 个音效 → 退出时 'resources still in use' " +
                 $"应当等于 {played}；大于它说明另有东西在钉资源");
        GetTree().Quit();
    }
}
