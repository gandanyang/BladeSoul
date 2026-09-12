using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;

namespace Oniblade.Audio;

/// <summary>
/// 音频总监（Autoload）。战斗代码只通过 <see cref="PlayCombat"/> 说话。
///
/// 本文件是**接口骨架**：公共 API、总线约定、弹开音高递增逻辑已经定死，
/// 具体音频资产与混音由音频任务填充（见 docs/TASKS.md 的 T3）。
/// </summary>
public partial class AudioDirector : Node
{
    /// <summary>同时可用的音效播放器数量（池化，避免每次命中都 new 节点）。</summary>
    private const int VoicePoolSize = 16;

    /// <summary>弹开连击每层升高 2 个半音，最多 +12 个半音（02 文档 §7）。</summary>
    private const float SemitoneRatio = 1.0594631f;
    private const int MaxChainPitchSemitones = 12;
    private const int SemitonesPerChain = 2;

    public static AudioDirector? Instance { get; private set; }

    /// <summary>当前弹开连击数（连续弹开不中断时递增，断连归零）。</summary>
    public int DeflectChain { get; private set; }

    private readonly List<AudioStreamPlayer> _voices = new(VoicePoolSize);
    private readonly Dictionary<CombatSfx, string> _paths = new();
    private readonly Dictionary<string, AudioStream?> _cache = new();

    /// <summary>战斗音效走这条总线（07 文档 §6），慢镜时音乐变调不会带上它。</summary>
    private const string CombatBus = "Combat";

    /// <summary>占位期是程序化生成的 .wav，正式资产替换后改这里即可。</summary>
    public static string PathFor(CombatSfx sfx) => $"res://assets/audio/sfx/sfx_{ToSnake(sfx)}.wav";

    public override void _EnterTree()
    {
        Instance = this;

        foreach (CombatSfx sfx in System.Enum.GetValues<CombatSfx>())
            _paths[sfx] = PathFor(sfx);

        // 总线只在 default_bus_layout.tres 真的加载成功时才用，否则退回 Master，
        // 避免"为了一个混音布局把整个游戏的声音搞没"。
        bool hasCombatBus = AudioServer.GetBusIndex(CombatBus) >= 0;

        for (int i = 0; i < VoicePoolSize; i++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"Voice{i}",
                Bus = hasCombatBus ? CombatBus : "Master",
            };
            AddChild(player);
            _voices.Add(player);
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance is { } bus)
            bus.HitResolved -= OnHitResolved;

        Instance = null;
    }

    public override void _Ready()
    {
        if (EventBus.Instance is { } bus)
            bus.HitResolved += OnHitResolved;
    }

    /// <summary>播放一个战斗音效。<paramref name="pitchScale"/> 为 1 表示原始音高。</summary>
    public void PlayCombat(CombatSfx sfx, float pitchScale = 1f, float volumeDb = 0f)
    {
        AudioStream? stream = Resolve(sfx);
        if (stream is null)
            return;

        AudioStreamPlayer? voice = TakeFreeVoice();
        if (voice is null)
            return;

        voice.Stream = stream;
        voice.PitchScale = pitchScale;
        voice.VolumeDb = volumeDb;
        voice.Play();
    }

    /// <summary>断连：把弹开音高重置回基准。</summary>
    public void ResetDeflectChain() => DeflectChain = 0;

    /// <summary>弹开时的音高倍率（连击越高音越尖）。</summary>
    public float DeflectPitchScale
    {
        get
        {
            int semitones = Mathf.Min(DeflectChain * SemitonesPerChain, MaxChainPitchSemitones);
            return Mathf.Pow(SemitoneRatio, semitones);
        }
    }

    private void OnHitResolved(HitEvent e)
    {
        switch (e.Verdict)
        {
            case Verdict.Deflect:
                DeflectChain++;
                PlayCombat(CombatSfx.Deflect, DeflectPitchScale);
                EventBus.Instance?.RaiseDeflectChain(new DeflectChainEvent { Chain = DeflectChain });
                break;

            case Verdict.Block:
                PlayCombat(CombatSfx.HitBlock);
                break;

            case Verdict.Clash:
                PlayCombat(CombatSfx.Clash);
                ResetDeflectChain();
                break;

            case Verdict.GuardBreak:
                PlayCombat(CombatSfx.GuardBreak);
                ResetDeflectChain();
                break;

            case Verdict.Issen:
                PlayIssenSequence();
                ResetDeflectChain();
                break;

            case Verdict.Hit:
                PlayCombat(CombatSfx.HitSlash);
                ResetDeflectChain();
                break;

            case Verdict.Miss:
            default:
                break;
        }
    }

    /// <summary>切割声与低频轰鸣之间的间隔（秒）。02 §7 给的是 0.2 秒。</summary>
    private const double IssenImpactDelaySeconds = 0.2;

    /// <summary>
    /// 一闪的音效序列（02 §7）："先 0.2 秒静音（世界抽真空）→ 切割声 → 低频轰鸣"。
    ///
    /// 严格照做做不到：事件是在**结算那一刻**才发出来的，没法倒回去先静音。
    /// 所以折中成"切割声立刻响、低频轰鸣在 0.2 秒后砸下来"，而"抽真空"那一下
    /// 交给玩家侧的慢镜（<c>TimeScale = 0.25</c>）承担——世界确实被抽慢了，
    /// 听感上就是这一击从战场里被单独拎出来。
    ///
    /// 两个音频文件在 T3 就已经生成好了，这里**只做接线**（T20 卡片明确要求不要重做音频）。
    /// </summary>
    private async void PlayIssenSequence()
    {
        IssenSequenceCount++;
        PlayCombat(CombatSfx.IssenSlash);

        SceneTree? tree = GetTree();
        if (tree is null)
            return;

        await ToSignal(tree.CreateTimer(IssenImpactDelaySeconds), SceneTreeTimer.SignalName.Timeout);

        PlayCombat(CombatSfx.IssenImpact);
    }

    /// <summary>
    /// 闪过几次一闪（端到端测试靠它直接断言"Yes，音效路径真的被走到了"，
    /// 而不必去猜音频有没有出声）。T20。
    /// </summary>
    public int IssenSequenceCount { get; private set; }

    private AudioStream? Resolve(CombatSfx sfx)
    {
        if (!_paths.TryGetValue(sfx, out string? path))
            return null;

        if (_cache.TryGetValue(path, out AudioStream? cached))
            return cached;

        // 占位期音频文件可能还不存在——静默返回，不要刷错误日志。
        AudioStream? stream = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        _cache[path] = stream;
        return stream;
    }

    private AudioStreamPlayer? TakeFreeVoice()
    {
        foreach (AudioStreamPlayer voice in _voices)
        {
            if (!voice.Playing)
                return voice;
        }

        return null;
    }

    private static string ToSnake(CombatSfx sfx)
    {
        string name = sfx.ToString();
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
