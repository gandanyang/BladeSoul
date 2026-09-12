using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;
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
///
/// 截图（卡片验收 4）需要带窗口跑，无头出不了。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class HudTest : Node3D
{
    private readonly List<string> _failures = new();

    private PlayerActor _player = null!;
    private TrainingDummy _enemy = null!;
    private TrainingDummy _barsDummy = null!;
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

        try
        {
            await CheckPlayerBars();
            await CheckEnemyPostureAndMagnitude();
            CheckPrompts();
            await CheckReadOnlyBoundary();
            await CheckEnemyBars();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        Report();
    }

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
            GD.Print("[HUD] ✓ 通过（玩家条可读 / 架势槽有跳动 / 四种提示 / 只读边界）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
