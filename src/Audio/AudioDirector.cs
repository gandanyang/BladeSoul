using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
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

    /// <summary>3D（带方位）音效的池子，刀风用。</summary>
    private const int VoicePoolSize3D = 8;

    /// <summary>刀风的最大可听距离（米）。超出就听不见——这是"这一刀在哪儿"的边界。</summary>
    private const float WhooshMaxDistance = 22f;

    /// <summary>弹开连击每层升高 2 个半音，最多 +12 个半音（02 文档 §7）。</summary>
    private const float SemitoneRatio = 1.0594631f;
    private const int MaxChainPitchSemitones = 12;
    private const int SemitonesPerChain = 2;

    public static AudioDirector? Instance { get; private set; }

    /// <summary>
    /// 当前弹开连击数（连续弹开不中断时递增，断连归零）。
    /// 它是**消费者**：战斗层写权威值，这里只跟着走（见 <see cref="OnDeflectChainChanged"/>）。
    /// </summary>
    public int DeflectChain { get; private set; }

    private readonly List<AudioStreamPlayer> _voices = new(VoicePoolSize);
    private readonly List<AudioStreamPlayer3D> _voices3D = new(VoicePoolSize3D);
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

        for (int i = 0; i < VoicePoolSize3D; i++)
        {
            var player3D = new AudioStreamPlayer3D
            {
                Name = $"Voice3D{i}",
                Bus = hasCombatBus ? CombatBus : "Master",
                MaxDistance = WhooshMaxDistance,
                UnitSize = 6f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            };
            AddChild(player3D);
            _voices3D.Add(player3D);
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance is { } bus)
        {
            bus.HitResolved -= OnHitResolved;
            bus.DeflectChainChanged -= OnDeflectChainChanged;
        }

        Instance = null;
    }

    public override void _Ready()
    {
        if (EventBus.Instance is { } bus)
        {
            bus.HitResolved += OnHitResolved;
            bus.DeflectChainChanged += OnDeflectChainChanged;
        }
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
        CountPlay(sfx);
    }

    /// <summary>
    /// 在**世界里的某个位置**播放音效（刀风这类要跟着角色走的音效）。
    ///
    /// 为什么另开一条路：`PlayCombat` 是纯 2D 的（音量不随距离衰减），
    /// 刀风放在角色身上才有"这一刀在哪儿挥的"的方向感；而弹开/受击的判定音
    /// 保持 2D，保证玩家在任何机位都能听清（那是必须听见的信息）。
    /// </summary>
    public void PlayCombatAt(CombatSfx sfx, Vector3 worldPosition, float pitchScale = 1f, float volumeDb = 0f)
    {
        AudioStream? stream = Resolve(sfx);
        if (stream is null)
            return;

        AudioStreamPlayer3D? voice = TakeFreeVoice3D();
        if (voice is null)
        {
            // 3D 池满了就退回 2D——宁可没有方向感，也不能没有声音。
            PlayCombat(sfx, pitchScale, volumeDb);
            return;
        }

        voice.GlobalPosition = worldPosition;
        voice.Stream = stream;
        voice.PitchScale = pitchScale;
        voice.VolumeDb = volumeDb;
        voice.Play();
        CountPlay(sfx);
    }

    // ── 播放计数：让自检能断言"音效路径真的被走到了 ──
    //
    // 端到端测试没法断言"有没有出声"，但可以断言"这条路径走到了"。
    // 没有这个计数器，"加了音效"就只能靠耳朵验收，而那是最不可复现的验收方式。

    private readonly Dictionary<CombatSfx, int> _playCounts = new();

    private void CountPlay(CombatSfx sfx)
    {
        _playCounts.TryGetValue(sfx, out int n);
        _playCounts[sfx] = n + 1;
    }

    /// <summary>某个音效一共播过几次（自检用）。</summary>
    public int PlayCount(CombatSfx sfx) => _playCounts.TryGetValue(sfx, out int n) ? n : 0;

    /// <summary>清空计数（每个自检场景开头调一次）。</summary>
    public void ResetPlayCounts() => _playCounts.Clear();

    /// <summary>断连：把弹开音高重置回基准。</summary>
    public void ResetDeflectChain() => DeflectChain = 0;

    /// <summary>
    /// 跟随战斗层的**权威弹开链**（<see cref="Combat.CombatActor.DeflectChain"/>）。
    ///
    /// 两个计数器的分工（刻意不合并）：
    ///   · 战斗链（CombatActor）——"连着弹开了几次"，有自己的保持窗口，
    ///     超时/复活即归零。**屏显的 ×n 读的是它**（HUD 按 11 §6 的约定轮询只读视图）。
    ///   · 音高链（本类）——"听感上还连不连着"。除了跟随上面那条，
    ///     还会在拼刀/破防/一闪/挨打时**立刻**回落到基准音高：
    ///     玩家挨了一刀就不该再听到"连击还在"的升调。
    ///
    /// T51 交接记的"事件零订阅者"就修在这里——断连那一半以前根本不发，
    /// 所以这条订阅拿不到 0，升调会一直挂着。
    /// </summary>
    private void OnDeflectChainChanged(DeflectChainEvent e) => DeflectChain = e.Chain;

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
                // 连击数**不在这里加**：它由 CombatActor 统一写并广播（而且必须在
                // RaiseHitResolved 之前就写好，所以到这一行时 DeflectPitchScale 已经是对的值）。
                // 这里若也加一次，音高就会领先屏幕一格，而且同一种事件会有两个生产者。
                PlayDeflect(e.AttackType);
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
                // 即发即忘：`_ =` 是**显式**丢弃，让人一眼看出"这里不等它"。
                // 不写 `_ =` 的话编译器会警告，而这个警告是好事——它逼你把"故意不等"
                // 和"忘了等"区分开。
                _ = PlayIssenSequence();
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

    /// <summary>
    /// 弹开音效（T53）：**按攻击性质分档**——斩＝金属叮 / 打＝沉闷冲击 /
    /// 突＝尖锐短促 / 暗＝低频闷响。
    ///
    /// ★ 档位的 PitchScale 与连击升调**相乘**，不是替换：
    /// "连着弹开、音越来越高"（02 §7）是核心反馈，不能被档位吃掉。
    /// 数据缺失时退回 T53 之前的单音效行为（照旧响，而不是没声音）。
    /// </summary>
    private void PlayDeflect(DamageType type)
    {
        DeflectFeedbackProfile? profile = DeflectFeedbackSet.Load()?.For(type);

        if (profile is null)
        {
            PlayCombat(CombatSfx.Deflect, DeflectPitchScale);
            return;
        }

        PlayCombat(profile.Sfx, DeflectPitchScale * profile.PitchScale, profile.VolumeDb);
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
    ///
    /// ★ 返回 <c>Task</c> 而不是 <c>async void</c>（T52 工程规则）：
    /// <c>async void</c> 里的异常**不会传播给任何人**，只会静默消失。在这里后果特别难看——
    /// 万一 <c>PlayCombat</c> 抛了，就变成"切割声已响、轰鸣永远不来、日志里什么都没有"，
    /// 而玩家听到的是**一闪那一击只剩半截音效**，没人能查出来。
    /// 调用方用 `_ = PlayIssenSequence();` 即发即忘，异常由方法内部兜住并打日志。
    /// </summary>
    private async Task PlayIssenSequence()
    {
        IssenSequenceCount++;
        PlayCombat(CombatSfx.IssenSlash);

        SceneTree? tree = GetTree();
        if (tree is null)
            return;

        try
        {
            await ToSignal(tree.CreateTimer(IssenImpactDelaySeconds), SceneTreeTimer.SignalName.Timeout);

            PlayCombat(CombatSfx.IssenImpact);
        }
        catch (System.Exception ex)
        {
            // 音效坏了不许影响战斗结算，但**必须留下痕迹**（否则就是"静默失效"）
            GD.PushWarning($"AudioDirector: 一闪轰鸣序列中断：{ex.Message}");
        }
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

    private AudioStreamPlayer3D? TakeFreeVoice3D()
    {
        foreach (AudioStreamPlayer3D voice in _voices3D)
        {
            if (!voice.Playing)
                return voice;
        }

        return null;
    }
}
