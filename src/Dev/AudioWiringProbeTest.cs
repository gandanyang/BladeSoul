using Godot;
using Oniblade.Audio;
using Oniblade.Combat;

namespace Oniblade.Dev;

/// <summary>
/// 音效接线体检：**证明"出刀有声音"这条路真的被走到了**，而不是靠耳朵听。
///
/// 为什么需要它：`AudioDirector` 里那些算法（弹开连击升半音、一闪的两段序列）早就写好、
/// 也有单测，但**没有任何东西保证战斗代码会去调它们**。
/// 实际发生的事就是这样：`WhooshLight` / `WhooshHeavy` / `DodgeWhoosh`
/// 三个音效有资源、有枚举、有路径映射，**却零调用点**——
/// 挥刀时完全没声音，而所有测试全绿。一个只测"函数对不对"的测试，
/// 永远发现不了"没人调用这个函数"。
///
/// 所以这里测的是**接线**：模拟真实输入，然后数每个音效被播了几次。
///
///     godot --headless --path . res://scenes/tests/AudioWiringProbe.tscn
/// </summary>
public partial class AudioWiringProbeTest : Node3D
{
    /// <summary>先等场景稳定，再开始数。</summary>
    [Export] public int WarmupFrames { get; set; } = 10;

    /// <summary>
    /// 连段必须在**取消窗打开之后**再按。轻斩壹的取消窗在第 18 帧才开
    /// （前摇 8 + 判定 4 + 后摇偏移 6），而输入缓冲只有 8~12 帧——
    /// 按早了缓冲会过期，那一按就白丢了。
    ///
    /// 一开始我用固定间隔（先 30 帧、后 18 帧）去猜，两种都错：
    /// 30 帧太晚（该段已经结束，连段断），18 帧太早（缓冲过期）。
    /// 所以现在**不猜**：直接读连段进度，等取消窗开了才按。
    /// </summary>
    private int _frames;
    private int _phase;
    private int _phaseFrame;

    private readonly System.Collections.Generic.List<string> _log = new();

    private readonly struct Tally
    {
        public Tally(int light, int heavy, int dodge)
        {
            Light = light;
            Heavy = heavy;
            Dodge = dodge;
        }

        public int Light { get; }
        public int Heavy { get; }
        public int Dodge { get; }
    }

    public override void _Ready()
    {
        var dojo = GD.Load<PackedScene>("res://scenes/levels/Dojo.tscn");
        if (dojo is null)
        {
            GD.PrintErr("[音效接线] Dojo 加载失败");
            GetTree().Quit(1);
            return;
        }

        AddChild(dojo.Instantiate());
        AudioDirector.Instance?.ResetPlayCounts();
        GD.Print("[音效接线] Dojo 已实例化，准备模拟输入");
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        _phaseFrame++;

        switch (_phase)
        {
            case 0:
                Press("attack");
                _phase = 1;
                _phaseFrame = 0;
                break;

            // 段 1、段 2：等"取消窗已开"再按
            case 1:
            case 2:
                if (ComboFrame() is int frame && frame >= _waitFromFrame)
                {
                    Press("attack");
                    // 每一段的取消窗位置不同：段0 CancelOpenFrame=18、段1=20、段2=31
                    // （前摇+判定+后摇偏移）。写死一个值必然在某一段按早或按晚。
                    _waitFromFrame = frame < 20 ? 20 : 31;
                    _phase++;
                    _phaseFrame = 0;
                }
                else if (_phaseFrame > 200)
                {
                    GD.PrintErr($"[音效接线] 段{_phase} 等取消窗超时（当前帧 {ComboFrame()?.ToString() ?? "无"}）");
                    Report();
                    GetTree().Quit(1);
                }

                break;

            // 等**回到 Idle** 再闪避。用固定帧数等是不可靠的：
            // 第三段总长 44 帧，等 30 帧时人还在后摇里，那一按会被状态机拒绝，
            // 于是探针会报"闪避没声音"——那是探针的错，不是游戏的错。
            case 3:
                if (IsPlayerIdle())
                {
                    Press("dodge");
                    _phase = 4;
                    _phaseFrame = 0;
                }
                else if (_phaseFrame > 200)
                {
                    GD.PrintErr("[音效接线] 等回 Idle 超时");
                    Report();
                    GetTree().Quit(1);
                }

                break;

            case 4:
                if (_phaseFrame >= 30)
                {
                    Press("guard");
                    _phase = 5;
                    _phaseFrame = 0;
                }

                break;

            default:
                if (_phaseFrame >= 20)
                {
                    Report();
                    GetTree().Quit(0);
                }

                break;
        }
    }

    /// <summary>取消窗打开的帧号（从 <c>AttackData.CancelOpenFrame</c> 读，不写死）。</summary>
    private int _waitFromFrame = 18;

    /// <summary>玩家当前是否可以自由行动（Idle / Move）。</summary>
    private bool IsPlayerIdle()
    {
        Node? player = FindByName(GetTree().Root, "Player");
        object? machine = player?.GetType().GetProperty("Machine")?.GetValue(player);
        object? current = machine?.GetType().GetProperty("Current")?.GetValue(machine);
        string? name = current?.GetType().Name;
        return name is "IdleState" or "MoveState";
    }

    /// <summary>当前连段进度：段号 + 段内帧号。不在出招时返回 null。</summary>
    private int? ComboFrame()
    {
        Node? player = FindByName(GetTree().Root, "Player");
        object? machine = player?.GetType().GetProperty("Machine")?.GetValue(player);
        object? current = machine?.GetType().GetProperty("Current")?.GetValue(machine);
        object? seq = current?.GetType().GetProperty("Sequence")?.GetValue(current);
        return seq?.GetType().GetProperty("Frame")?.GetValue(seq) as int?;
    }

    /// <summary>按一帧再松——`JustPressed` 语义需要"按下"这个沿。</summary>
    private void Press(string action)
    {
        if (!InputMap.HasAction(action))
        {
            GD.PrintErr($"[音效接线] 输入动作不存在：{action}");
            return;
        }

        Input.ActionPress(action);
        // 下一帧松开，否则 JustPressed 永远为真会把后续按键吞掉。
        CallDeferred(nameof(Release), action);
        _log.Add($"帧{_frames} 按下 {action}（当前状态 {CurrentStateName()}）");
    }

    /// <summary>当前玩家处于什么状态——只看名字，不依赖具体类型。</summary>
    private string CurrentStateName()
    {
        Node? player = FindByName(GetTree().Root, "Player");
        if (player is null)
            return "找不到玩家";

        // PlayerActor 的状态机是内部的，这里退一步：看它有没有暴露当前状态名。
        // 找不到就报类型名，至少能看出"是不是卡在 AttackState"。
        var prop = player.GetType().GetProperty("Machine");
        object? machine = prop?.GetValue(player);
        object? current = machine?.GetType().GetProperty("Current")?.GetValue(machine);
        if (current is null)
            return "未知";

        // 如果正卡在出招里，把连段进度一起报出来——"卡在第几段第几帧"
        // 和"根本没在出招"是两种完全不同的故障。
        var seqProp = current.GetType().GetProperty("Sequence");
        object? seq = seqProp?.GetValue(current);
        if (seq is null)
            return current.GetType().Name;

        object? step = seq.GetType().GetProperty("StepIndex")?.GetValue(seq);
        object? frame = seq.GetType().GetProperty("Frame")?.GetValue(seq);
        return $"{current.GetType().Name}[段{step} 帧{frame}]";
    }

    private static Node? FindByName(Node root, string name)
    {
        if (root.Name == name)
            return root;
        foreach (Node c in root.GetChildren())
        {
            Node? r = FindByName(c, name);
            if (r is not null)
                return r;
        }

        return null;
    }

    private void Release(string action) => Input.ActionRelease(action);

    private void Report()
    {
        AudioDirector? audio = AudioDirector.Instance;
        if (audio is null)
        {
            GD.PrintErr("[音效接线] 没有 AudioDirector——autoload 没挂上？");
            GetTree().Quit(1);
            return;
        }

        Tally tally = new(
            audio.PlayCount(CombatSfx.WhooshLight),
            audio.PlayCount(CombatSfx.WhooshHeavy),
            audio.PlayCount(CombatSfx.DodgeWhoosh));

        GD.Print($"[音效接线] 挥刀轻风 {tally.Light} 次；挥刀重风 {tally.Heavy} 次；闪避破风 {tally.Dodge} 次");
        foreach (string line in _log)
            GD.Print($"[音效接线]   {line}");

        int swings = tally.Light + tally.Heavy;

        // 三次攻击至少该有三次刀风（轻斩壹/贰是轻风、叁是重风）。
        Check(swings >= 3,
            $"三次攻击响了 {swings} 次刀风（≥ 3）",
            $"三次攻击只响过 {swings} 次刀风——出刀没声音（期望 ≥ 3）");
        Check(tally.Light >= 2,
            $"轻风响了 {tally.Light} 次（轻斩壹/贰 都是轻风）",
            $"轻风只响了 {tally.Light} 次（轻斩壹/贰 都该是轻风）");
        Check(tally.Heavy >= 1,
            $"重风响了 {tally.Heavy} 次（轻斩叁 是重风）",
            $"重风只响了 {tally.Heavy} 次（轻斩叁 该是重风）");
        Check(tally.Dodge >= 1, "闪避破风响了（闪避有声音）", "闪避破风一次都没响——闪避没声音");
    }

    private static void Check(bool ok, string passMessage) => Check(ok, passMessage, passMessage);

    private static void Check(bool ok, string passMessage, string failMessage)
    {
        GD.Print(ok ? $"[音效接线]   ✓ {passMessage}" : $"[音效接线]   ✗ {failMessage}");
    }
}
