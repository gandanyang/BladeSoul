using System.Collections.Generic;
using Godot;
using Oniblade.Audio;
using Oniblade.Combat;
using Oniblade.Combat.Data;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;
using Oniblade.UI;

namespace Oniblade.Dev;

/// <summary>
/// 战斗 HUD 验收（T31）：
///     godot --headless --path . res://scenes/tests/Hud.tscn
///
/// 卡片要求"要能自动校验，不靠看着还行"，所以今天能自动化的部分全部断言：
///
/// 1. 玩家挨打 → 血条读数下降、体干读数上升；**体干会回落**（"在恢复"这件事要可读）
/// 2. 打中敌人 → 焦点敌人的架势槽读数增加；**弹开那一下的增量大于普攻**
///    （前者用 HUD 的读数验，后者用冻结的裁决器/数据验——量级本来就不归 HUD 管）
/// 3. 弹开 / 一闪 / 格挡 / 挨打 四种结算各触发对应的提示
/// 4. ★ **HUD 只读**：灌一个 Damage=999 的命中事件，战斗单位一个数值都不许变
///    （照抄 T28 的做法，这条最重要）
/// 5. 未锁定敌人的头顶细条**只在受伤或靠近时显示**（11 §4.2）：
///    远处没受伤不挂条 → 挨一下必须挂条 → 伤后计时走完自动收回去
/// 6. ★ **弹开连击「×n」**（T51 遗留①）：1 连不显示 → 2 连显示 → 窗口走完归零并淡出。
///    这一条**故意走真链路**（会还手的假人 + 在窗口内按防御的机器人，与 DeflectTraining 同一套）：
///    连击是 CombatActor 的属性，直接灌 HitEvent 是灌不出来的——
///    灌出来的绿只会证明"我把它写进去了"，证明不了"它接上了"。
///
/// 截图（卡片验收 4）需要带窗口跑，无头出不了。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class HudTest : Node3D
{
    /// <summary>弹开机器人：在假人的判定帧到来前几帧按下防御（与 DeflectTrainingTest 同值）。</summary>
    private const int GuardLeadFrames = 6;

    private readonly List<string> _failures = new();

    private PlayerActor _player = null!;
    private TrainingDummy _enemy = null!;
    private TrainingDummy _barsDummy = null!;
    private readonly List<AttackingDummy> _attackers = new();
    private Hud _hud = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 焦点目标要有名字与可改的体干；木桩不会还手，正好用来验"看板"
        _enemy = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        _enemy.Position = new Vector3(0f, 0.1f, -2f);
        AddChild(_enemy);

        await WaitPhysicsFrames(12);

        if (Hud.Instance is not { } hud)
        {
            GD.PrintErr("[HUD] ✗ Hud 没有注册成 autoload");
            GetTree().Quit(1);
            return;
        }

        _hud = hud;
        _playerActorId = _player.ActorId;

        if (EventBus.Instance is { } bus)
            bus.HitResolved += OnHitResolved;

        try
        {
            await CheckPlayerBars();
            await CheckEnemyPostureAndMagnitude();
            CheckPrompts();
            await CheckReadOnlyBoundary();
            await CheckEnemyBars();
        CheckDeathblowMarker();
            await CheckDeflectChain();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        Report();
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance is { } bus)
            bus.HitResolved -= OnHitResolved;
    }

    /// <summary>只数"弹开连击测试那一段"里玩家真的挨了几刀（音高链断连规则会因此生效）。</summary>
    private void OnHitResolved(HitEvent e)
    {
        if (_countDeflectHits && e.DefenderId == _playerActorId && e.Verdict == Verdict.Hit)
            _deflectHits++;
    }

    private bool _countDeflectHits;
    private int _deflectHits;

    // ── 1. 玩家条 ──────────────────────────────────────────────

    private async System.Threading.Tasks.Task CheckPlayerBars()
    {
        await WaitPhysicsFrames(3);

        float healthBefore = _hud.PlayerHealthRatio;
        Check(healthBefore > 0.9f, $"开局血条读数只有 {healthBefore:0.##}，应该是满的");

        _player.Health.Apply(25);
        _player.Posture.Apply(30);
        await WaitPhysicsFrames(3);

        Check(_hud.PlayerHealthRatio < healthBefore,
            $"挨打后血条读数没降：{healthBefore:0.##} → {_hud.PlayerHealthRatio:0.##}");
        Check(_hud.PlayerPostureRatio > 0.05f,
            $"体干条读数没升：{_hud.PlayerPostureRatio:0.##}");

        float peak = _hud.PlayerPostureRatio;
        GD.Print($"[HUD] 玩家：血 {healthBefore:0.##} → {_hud.PlayerHealthRatio:0.##}，" +
                 $"体干升到 {peak:0.##}");

        // 体干要**可见地回落**（11 §3："体干在恢复"这件事本身要可读）
        await WaitPhysicsFrames(90);

        Check(_hud.PlayerPostureRatio < peak,
            $"体干没有回落：{peak:0.##} → {_hud.PlayerPostureRatio:0.##}（玩家会觉得体干永远不恢复）");

        GD.Print($"[HUD] 体干回落：{peak:0.##} → {_hud.PlayerPostureRatio:0.##} ✓");

        Check(_hud.HealDotsLit > 0, $"喝血圆点读数是 {_hud.HealDotsLit}，应该 >0");
    }

    // ── 2. 敌方架势槽 + 量级 ───────────────────────────────────

    private async System.Threading.Tasks.Task CheckEnemyPostureAndMagnitude()
    {
        _enemy.Posture.Reset();
        await WaitPhysicsFrames(2);

        Check(_hud.FocusActorId == _enemy.ActorId,
            $"焦点目标不是那个敌人（焦点 {_hud.FocusActorId}，敌人 {_enemy.ActorId}）——" +
            "架势槽会读错对象");
        Check(_hud.FocusName.Length > 0, "焦点目标没有名字");

        float before = _hud.FocusPostureRatio;

        // 普攻量级
        _enemy.Posture.Apply(8);
        await WaitPhysicsFrames(2);
        float afterLight = _hud.FocusPostureRatio;

        Check(afterLight > before,
            $"打中敌人后架势槽读数没增加：{before:0.##} → {afterLight:0.##}");

        _enemy.Posture.Reset();
        await WaitPhysicsFrames(2);

        // 弹开量级
        _enemy.Posture.Apply(18);
        await WaitPhysicsFrames(2);
        float afterDeflect = _hud.FocusPostureRatio;

        Check(afterDeflect > afterLight,
            $"弹开的架势增量（{afterDeflect:0.##}）没有大于普攻（{afterLight:0.##}）：" +
            "玩家看不出'这一下削得更多'");

        GD.Print($"[HUD] 架势槽读数：普攻 +8 → {afterLight:0.##}，弹开 +18 → {afterDeflect:0.##}");

        // 量级本身归裁决器与数据管，这里顺手钉一下它们确实是 18 vs 8
        var light = GD.Load<AttackData>("res://data/attacks/player/light_01.tres");
        Check(light is not null, "读不到轻斩壹的数据");

        var attack = new AttackerSnapshot
        {
            ActorId = 1,
            Traits = light?.ToTraits() ?? AttackTraits.Neutral,
            IsActive = true,
        };
        var defender = new DefenderSnapshot
        {
            ActorId = 2,
            InDeflectWindow = true,
        };

        ResolveResult deflect = CombatResolver.Resolve(attack, defender);
        int lightPosture = light?.PostureDamage ?? 0;

        Check(deflect.Verdict == Verdict.Deflect, $"弹开窗没给出 Deflect（{deflect.Verdict}）");
        Check(deflect.PostureDamage > lightPosture,
            $"弹开削的体干 {deflect.PostureDamage} 没有大于普攻 {lightPosture}");

        GD.Print($"[HUD] 量级核对：弹开 {deflect.PostureDamage} vs 普攻 {lightPosture} ✓");
    }

    // ── 3. 四种结算的提示 ─────────────────────────────────────

    private void CheckPrompts()
    {
        int deflect = _hud.DeflectPrompts;
        int issen = _hud.IssenPrompts;
        int block = _hud.BlockFlashes;
        int hit = _hud.PlayerHitFlashes;

        Raise(Verdict.Deflect);
        Raise(Verdict.Issen);
        Raise(Verdict.Block);
        Raise(Verdict.Hit);

        Check(_hud.DeflectPrompts == deflect + 1, "弹开没有触发提示");
        Check(_hud.IssenPrompts == issen + 1, "一闪没有触发提示");
        Check(_hud.BlockFlashes == block + 1, "格挡没有触发提示");
        Check(_hud.PlayerHitFlashes == hit + 1, "挨打没有触发提示");

        Check(_hud.LastPrompt == "一闪", $"最后一次文字提示是「{_hud.LastPrompt}」，应为「一闪」");

        GD.Print($"[HUD] 四种结算提示：弹开 {_hud.DeflectPrompts} / 一闪 {_hud.IssenPrompts} / " +
                 $"格挡 {_hud.BlockFlashes} / 挨打 {_hud.PlayerHitFlashes} ✓");

        // 伤害数字：玩家打出的才记（一闪走大字号）
        int spawned = _hud.Numbers.Spawned;
        int issenSpawned = _hud.Numbers.IssenSpawned;
        Raise(Verdict.Deflect);   // 不是玩家造成的伤害，不该出数字

        Check(_hud.Numbers.Spawned == spawned, "非玩家造成的伤害也出了伤害数字");

        // 正向：**玩家打出的**伤害要出数字；一闪还要走大字号
        RaiseFromPlayer(Verdict.Hit, damage: 12);
        Check(_hud.Numbers.Spawned == spawned + 1,
            $"玩家打出的伤害没有出数字（{spawned} → {_hud.Numbers.Spawned}）");

        RaiseFromPlayer(Verdict.Issen, damage: 40);
        Check(_hud.Numbers.IssenSpawned == issenSpawned + 1,
            "一闪的伤害数字没有走大字号那一路");

        GD.Print($"[HUD] 伤害数字：累计 {_hud.Numbers.Spawned} 个（其中一闪 {_hud.Numbers.IssenSpawned} 个），" +
                 $"当前在场 {_hud.Numbers.ActiveCount} 个");
    }

    // ── 4. ★ 只读边界（照抄 T28）──────────────────────────────

    private async System.Threading.Tasks.Task CheckReadOnlyBoundary()
    {
        int playerHealth = _player.Health.Current;
        int playerPosture = _player.Posture.Current;
        int enemyHealth = _enemy.Health.Current;
        int enemyPosture = _enemy.Posture.Current;

        Raise(Verdict.Hit, damage: 999);

        await WaitPhysicsFrames(3);

        Check(_player.Health.Current == playerHealth,
            $"HUD 把伤害算到了玩家头上：{playerHealth} → {_player.Health.Current}");
        Check(_player.Posture.Current == playerPosture, "HUD 改了玩家体干");
        Check(_enemy.Health.Current == enemyHealth,
            $"HUD 把伤害算到了敌人头上：{enemyHealth} → {_enemy.Health.Current}");
        Check(_enemy.Posture.Current == enemyPosture, "HUD 改了敌人体干");

        GD.Print($"[HUD] 只读边界：灌入 Damage=999 后，玩家 {_player.Health.Current}/" +
                 $"{_player.Health.Max}、敌人 {_enemy.Health.Current}/{_enemy.Health.Max} 均未变 ✓");
    }

    // ── 5. 未锁定敌人的头顶细条（11 §4.2）─────────────────────

    /// <summary>
    /// 对照组的做法：**最近的那个永远是焦点**（Hud 自己选的），而焦点有屏幕上方的大面板，
    /// 头顶条要跳过它。所以这里另放一个假人当"未锁定敌人"，靠它验显示规则。
    /// </summary>
    /// <summary>
    /// T52：处决标记层。验三件事——
    ///   ① Hud 真的建出了这一层（不然玩家永远不知道能处决）；
    ///   ② 它读的是**只读接口**而不是具体敌人类型（docs/00 §2.8 的界线）；
    ///   ③ 距离判定与处决判定同源（否则会出现"标记亮着但按 F 没反应"）。
    /// </summary>
    private void CheckDeathblowMarker()
    {
        DeathblowMarker? marker = _hud.DeathblowMarkers;
        Check(marker is not null, "Hud 没有建出处决标记层（破韧了玩家也不知道能处决）");

        if (marker is null)
            return;

        // ① 距离上限与处决判定同源
        Check(_hud.Deathblow is not null,
            "Hud.Deathblow 没挂 data/combat/deathblow.tres —— " +
            "标记距离会与处决判定各写一份，迟早出现'标记亮着但按 F 没反应'");
        Check(!marker.Enable || marker.Deathblow is not null,
            "标记层没拿到处决参数，距离上限退回保守默认值");

        // ② 只读接口真的暴露了处决状态。
        //    这里**不建 Ashigaru**：标记层靠 `ICombatActorDebug.CanBeExecuted`，
        //    任何单位都不许让 UI 去认具体类型。
        Check(_enemy is ICombatActorDebug,
            "敌人没实现 ICombatActorDebug —— 处决标记读不到状态");
        Check(!((ICombatActorDebug)_enemy).CanBeExecuted,
            "没被破韧的敌人就报 CanBeExecuted=true —— 标记会一直亮，玩家会以为随时能处决");

        // ③ 距离边界与 DeathblowResolver 一致（2.2 边界含等号）
        float max = _hud.Deathblow?.MaxDistance ?? 2.2f;
        Check(DeathblowMarkerView.ShouldShow(true, max - 0.01f, max),
            $"处决距离内（{max - 0.01f:F2}m）不亮标记");
        Check(!DeathblowMarkerView.ShouldShow(true, max + 0.01f, max),
            $"超出处决距离（{max + 0.01f:F2}m）仍亮标记 —— 标记在骗人");

        GD.Print($"[HUD] 处决标记层：已建出，距离上限 {max:F1}m，未破韧时不亮 ✓");
    }

    private async System.Threading.Tasks.Task CheckEnemyBars()
    {
        EnemyBars? bars = _hud.Bars;
        Check(bars is not null, "Hud 没有建出 EnemyBars 层");

        if (bars is null)
            return;

        // 纯规则四个象限 —— 这是"同屏 8 个不会变仪表盘"的全部依据
        Check(EnemyBars.ShouldShow(3f, 0, 7f, 26f), "近处未受伤的敌人没挂条");
        Check(!EnemyBars.ShouldShow(30f, 0, 7f, 26f),
            "远处未受伤的敌人也挂了条——同屏 8 个立刻变仪表盘（11 §4.2）");
        Check(EnemyBars.ShouldShow(20f, 120, 7f, 26f), "受伤的敌人没挂条——受伤必须保持可见");
        Check(!EnemyBars.ShouldShow(40f, 120, 7f, 26f), "超出 MaxDistance 的敌人仍挂条");

        _barsDummy = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        _barsDummy.Position = new Vector3(0f, 0.1f, -5f);
        AddChild(_barsDummy);
        await WaitPhysicsFrames(6);

        Check(_hud.FocusActorId == _enemy.ActorId,
            $"焦点被第二个假人抢走了（焦点 {_hud.FocusActorId}）——对照组不成立");

        // ① 靠近但没受伤 → 显示（走"靠近"那一路）
        Check(bars.DecidedVisibleCount >= 1,
            $"近处（5m）的敌人没挂条（读数 {bars.DecidedVisibleCount}）");
        Check(bars.NearShownCount >= 1, "近处那条不是按'靠近'显示的");
        GD.Print($"[HUD] 头顶条：靠近 5m 未受伤 → 显示 {bars.DecidedVisibleCount} 条 ✓");

        // ② 拉到 22m、没受伤 → 一条都不许有
        _barsDummy.Position = new Vector3(0f, 0.1f, -22f);
        await WaitPhysicsFrames(6);
        Check(bars.DecidedVisibleCount == 0,
            $"远处（22m）未受伤的敌人仍挂条：{bars.DecidedVisibleCount} 条");

        // ③ 同样远，但挨了一下 → 必须重新挂上，且走"受伤"那一路
        _barsDummy.Health.Apply(Mathf.Max(1, _barsDummy.Health.Max / 10));
        await WaitPhysicsFrames(6);
        Check(bars.DecidedVisibleCount >= 1, "受伤的远处敌人没有挂条");
        Check(bars.HurtShownCount >= 1, "受伤那条没走'受伤保持'那一路");
        GD.Print($"[HUD] 头顶条：22m 受伤 → 显示 {bars.DecidedVisibleCount} 条（受伤保持）✓");

        // ④ 受伤计时走完 → 自己收回去（否则会永久挂满条）
        await WaitPhysicsFrames(bars.HurtHoldFrames + 12);
        Check(bars.DecidedVisibleCount == 0,
            $"伤后计时走完仍挂着条：{bars.DecidedVisibleCount} 条");

        GD.Print("[HUD] 头顶条：伤后计时走完自动隐藏 ✓");
    }

    // ── 6. ★ 弹开连击「×n」（T51 遗留①）────────────────────────

    private async System.Threading.Tasks.Task CheckDeflectChain()
    {
        // 前面的假人先挪出攻击线：它们会抢 HUD 焦点，也挡在挥砍假人的刀路上
        _enemy.Position = new Vector3(0f, 0.1f, -30f);
        _barsDummy.Position = new Vector3(8f, 0.1f, -30f);
        await WaitPhysicsFrames(4);

        Check(_hud.DeflectChainShown == 0 && !_hud.DeflectChainVisible,
            $"还没弹开过，屏幕上就挂着连击数（×{_hud.DeflectChainShown}）");

        // ★ **两个靶子，相位错开约 60 帧**，这不是凑数——是"连击"这个概念的前提：
        // 一个靶子的两次出招之间隔 ≈118 帧（58 招式 + 60 冷却，02 §10 的下限是 45），
        // 比连击保持窗口（DeflectChainWindowFrames = 90）**还长**，
        // 所以对着一个靶子连击永远只到 1：第二刀落下时窗口早关了。
        // 两个错相的敌人正是遭遇战（T42）里的常态。
        _attackers.Clear();
        await SpawnAttacker(new Vector3(0f, 0.1f, -1.7f), delayFrames: 10);
        await SpawnAttacker(new Vector3(1.35f, 0.1f, -1.65f), delayFrames: 60);

        foreach (AttackingDummy dummy in _attackers)
            Check(dummy.Attack is not null, "挥砍假人没有配招式（Attack 为空）——连击根本没法测");

        _countDeflectHits = true;
        int windowFrames = _player.DeflectChainWindowFrames;
        bool guardHeld = false;
        bool sawSingleChainHidden = false;
        int maxFrames = 900;      // 一次循环 ≈118 帧，900 帧足够连上 2 次

        for (int frame = 0; frame < maxFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            int untilActive = FramesUntilAnyAttackerActive();

            if (!guardHeld && untilActive <= GuardLeadFrames)
            {
                Input.ActionPress("guard");
                guardHeld = true;
            }
            else if (guardHeld && !AnyAttackerInAttackState())
            {
                Input.ActionRelease("guard");
                guardHeld = false;
            }

            // 对照组①：**只弹开 1 次时屏幕上不该有连击数**。
            // 没有这条对照，"×1 也显示"会被当成没问题——而弹开一下本来就
            // 已经有脉冲/提示/音效了，再挂个 ×1 只是噪音。
            if (_player.DeflectChain == 1 && !_hud.DeflectChainVisible)
                sawSingleChainHidden = true;

            if (_player.DeflectChain >= 2)
                break;
        }

        Input.ActionRelease("guard");
        _countDeflectHits = false;

        int chain = _player.DeflectChain;
        await WaitPhysicsFrames(3);       // 让 HUD 的 _Process 至少跑一轮

        Check(chain >= 2, $"{maxFrames} 帧内只连上 {chain} 次弹开：连击显示没东西可测");
        Check(sawSingleChainHidden, "第 1 次弹开时屏幕上就出现了连击数——「×1」是噪音（阈值没生效）");
        Check(_hud.DeflectChainShown == chain,
            $"屏显连击 ×{_hud.DeflectChainShown} 与战斗链 {chain} 不一致：屏幕上那个数不是权威值");
        Check(_hud.DeflectChainVisible, $"已经连上 {chain} 次，屏幕上却没有连击数（没有任何 UI 反馈）");

        GD.Print($"[HUD] 弹开连击：连上 ×{chain} → 屏显 ×{_hud.DeflectChainShown}（第 1 次时未显示 ✓）");

        // 音高链必须跟的是**同一条链**（T51 记的"事件零订阅"就是断在这：
        // 以前事件只在涨的时候发，音高链拿不到断连）。
        AudioDirector? audio = AudioDirector.Instance;

        if (_deflectHits == 0)
            Check(audio is not null && audio.DeflectChain == chain,
                $"音高链 {(audio is null ? "不存在" : audio.DeflectChain.ToString())} 没有跟上战斗链 {chain}：事件还是没被消费");
        else
            GD.Print($"[HUD] 本轮玩家挨了 {_deflectHits} 刀，音高链按断连规则当场复位——不参与同源断言");

        // 对照组②：**断连那一半**。以前的事件只会在涨的时候发，
        // 所以这一条正是"只增不减"那个缺陷的唯一照妖镜。
        foreach (AttackingDummy dummy in _attackers)
            dummy.Position = new Vector3(dummy.Position.X, 0.1f, -30f);   // 停手，不再有人挥刀

        await WaitPhysicsFrames(windowFrames + 6);

        Check(_player.DeflectChain == 0,
            $"静置 {windowFrames + 6} 帧后战斗链仍是 {_player.DeflectChain}（保持窗口没归零）");
        Check(_hud.DeflectChainShown == 0,
            $"战斗链已经断了，屏幕上还挂着 ×{_hud.DeflectChainShown}：连击只增不减");
        GD.Print($"[HUD] 断连：静置 {windowFrames + 6} 帧 → 战斗链 0 / 屏显 ×0 ✓");

        await WaitPhysicsFrames(_hud.DeflectChainFadeFrames + 8);

        Check(!_hud.DeflectChainVisible,
            $"断连 {_hud.DeflectChainFadeFrames} 帧后连击数还挂在屏幕上（淡出没生效）");
        GD.Print($"[HUD] 淡出：{_hud.DeflectChainFadeFrames} 帧后连击数已隐藏 ✓");
    }

    /// <summary>生出第 n 个挥砍假人；<paramref name="delayFrames"/> 决定它的出招相位。</summary>
    private async System.Threading.Tasks.Task SpawnAttacker(Vector3 position, int delayFrames)
    {
        var dummy = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        dummy.Position = position;
        AddChild(dummy);
        _attackers.Add(dummy);
        await WaitPhysicsFrames(delayFrames);
    }

    /// <summary>最近的那个假人还有几帧进入判定帧（没人在出招时返回一个很大的值）。与 DeflectTrainingTest 同式。</summary>
    private int FramesUntilAnyAttackerActive()
    {
        int soonest = int.MaxValue;

        foreach (AttackingDummy dummy in _attackers)
        {
            if (dummy.Machine.Current is not AttackState attack || !attack.Sequence.IsRunning)
                continue;

            soonest = Mathf.Min(soonest, attack.Sequence.Current.ActiveStart - attack.Sequence.Frame);
        }

        return soonest;
    }

    private bool AnyAttackerInAttackState()
    {
        foreach (AttackingDummy dummy in _attackers)
        {
            if (dummy.Machine.Current is AttackState)
                return true;
        }

        return false;
    }

    /// <summary>玩家打出的命中（AttackerId 是玩家 → 会出伤害数字）。</summary>
    private void RaiseFromPlayer(Verdict verdict, int damage)
    {
        EventBus.Instance?.RaiseHitResolved(new HitEvent
        {
            AttackerId = _playerActorId,
            DefenderId = _enemy.ActorId,
            Verdict = verdict,
            AttackId = "hud_test_player",
            Damage = damage,
            Position = _enemy.GlobalPosition + Vector3.Up * 1.2f,
            Direction = Vector3.Forward,
        });
    }

    private void Raise(Verdict verdict, int damage = 0)
    {
        EventBus.Instance?.RaiseHitResolved(new HitEvent
        {
            AttackerId = 999,          // 不是玩家 → 不会出伤害数字
            DefenderId = _playerActorId,
            Verdict = verdict,
            AttackId = "hud_test",
            IssenKind = IssenKind.None,
            Damage = damage,
            PostureDamage = 0,
            HitStopFrames = 0,
            Frame = 0,
            Killed = false,
            Position = new Vector3(0f, 1f, -2f),
            Direction = Vector3.Forward,
        });
    }

    private int _playerActorId = -1;

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(40f, 0.4f, 40f) },
            Position = new Vector3(0f, -0.2f, 0f),
        });
        AddChild(body);
    }

    private static T Load<T>(string path) where T : Node =>
        GD.Load<PackedScene>(path).Instantiate<T>();

    private async System.Threading.Tasks.Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[HUD] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[HUD] ✓ 通过（玩家条可读 / 架势槽有跳动 / 四种提示 / 只读边界 / 弹开连击显示）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
