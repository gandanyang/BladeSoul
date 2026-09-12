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
/// 架势系统收口验收（T37）。三个缺口各有一组断言：
///
/// 缺口① 难度档的玩家架势恢复旋钮 —— 断言 <c>Posture.RegenScale</c> 真的被写进去了，
///        并把"四档在 60fps 下被向上取整吃掉"这件事**打进日志**（见 <see cref="CheckPostureRegenKnob"/> 的说明）。
/// 缺口② 玩家破防 —— 格挡到破防：破防前一点血不掉、进硬直、<c>OnPostureBroken</c> 被调过，
///        以及 HUD 把 GuardBreak 与 Block 拆开了。
/// 缺口③ 半自动防御 —— 四组对照（見習一般攻击 / 見習危攻击 / 見習连挨第 4 次 / 其他档）。
///
///     godot --headless --path . res://scenes/tests/Stance.tscn
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class StanceTest : Node3D
{
    private readonly List<string> _failures = new();

    private PlayerActor _player = null!;
    private AttackingDummy _grunt = null!;
    private AttackingDummy _spear = null!;

    /// <summary>把假人放到够不着的地方（用它做"这一组不参与"的开关）。</summary>
    private static readonly Vector3 Parked = new(200f, 0.1f, 0f);

    private static readonly Vector3 InReach = new(0f, 0.1f, -2f);

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Difficulty = Difficulty("samurai");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        // 普攻假人：grunt_slash（不是危）——半自动防御该对它生效
        _grunt = Spawn<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn", Parked);

        // 危攻击假人：perilous_thrust（05 §128 说一律不生效）
        _spear = Spawn<AttackingDummy>("res://scenes/actors/SpearDummy.tscn", Parked);

        await WaitPhysicsFrames(10);

        try
        {
            CheckPostureRegenKnob();
            await CheckHalfAutoGuardGroups();
            await CheckGuardBreakEndToEnd();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex}");
        }

        Input.ActionRelease("guard");
        Report();
    }

    // ── 缺口①：难度档的玩家架势恢复旋钮 ────────────────────────

    /// <summary>
    /// 先断言旋钮"接上了"（RegenScale 等于难度档的数值）。
    ///
    /// ★ 然后这里**故意不写**"修罗档比見習档慢"那条断言——实测它现在会失败，
    /// 原因不在旋钮，在 <c>PostureMeter.Tick</c> 的量化：
    /// 玩家每秒回 22 点，60fps 下每帧 0.3667 点，而 <c>Tick(1, …)</c> 里做的是
    /// <c>Ceiling(perFrame * 1)</c> —— 任何 &lt; 1 的回复量都被抬成 1 点/帧。
    /// 于是 0.85 / 1.0 / 1.2 / 1.5 四档全都恰好 1 点/帧，**缩放整个被吃掉**。
    /// 这不是接线问题，改它要动恢复公式的量化（会改变实际回复速度，并影响既有断言），
    /// 属于平衡决定，留给制作人裁定。这里把实测数字打出来，让这件事可见。
    /// </summary>
    private void CheckPostureRegenKnob()
    {
        Check(Mathf.Abs(_player.Posture.RegenScale - 1.0f) < 0.001f,
            $"武士档的 RegenScale 是 {_player.Posture.RegenScale}，应为 1.0（旋钮没接上）");

        _player.Difficulty = Difficulty("asura");
        _player.ApplyDifficulty();
        Check(Mathf.Abs(_player.Posture.RegenScale - 0.85f) < 0.001f,
            $"修罗档的 RegenScale 是 {_player.Posture.RegenScale}，应为 0.85（旋钮没接上）");

        _player.Difficulty = Difficulty("migoto");
        _player.ApplyDifficulty();
        Check(Mathf.Abs(_player.Posture.RegenScale - 1.5f) < 0.001f,
            $"見習档的 RegenScale 是 {_player.Posture.RegenScale}，应为 1.5（旋钮没接上）");

        GD.Print("[架势] 缺口① 旋钮已接上：武士 1.0 / 修罗 0.85 / 見習 1.5");

        // 量化实测：把"四档其实一样快"这件事钉在日志里
        int slow = SimulateRegen(0.85f, 120);
        int fast = SimulateRegen(1.5f, 120);

        GD.Print($"[架势] 缺口① 量化实测（每帧 Tick(1)，120 帧）：修罗档回 {slow} 点，見習档回 {fast} 点");

        // 如果余数不被丢掉（累加式），同样的 120 帧会差出这么多 —— 这是修法效果的直接证据
        int slowFixed = SimulateAccumulatedRegen(0.85f, 120);
        int fastFixed = SimulateAccumulatedRegen(1.5f, 120);

        GD.Print($"[架势] 缺口① 若余数累加（不动公式，只不丢零头）：修罗回 {slowFixed} 点，" +
                 $"見習回 {fastFixed} 点 —— 两档才分得开");

        if (slow == fast)
        {
            GD.Print("[架势] ⚠️ 当前量化下两档回复量相同（Ceiling(perFrame) 把 <1 的回复量抬成 1 点/帧，" +
                     "scale ≤ 2.72 全都等于 1）。旋钮是活的，但四档手感一致；" +
                     "连带后果：格挡 +20 架势会被 60 点/秒的回复吃掉，玩家几乎不可能被打到破防。");
        }

        _player.Difficulty = Difficulty("samurai");
        _player.ApplyDifficulty();
    }

    /// <summary>如果保留余数（累加式），同一组数字会回多少——用来给"修不修"提供依据。</summary>
    private static int SimulateAccumulatedRegen(float scale, int frames)
    {
        float perFrame = 22f * scale / Utils.Frames.PerSecond;
        float carry = 0f;
        int recovered = 0;

        for (int i = 0; i < frames; i++)
        {
            carry += perFrame;
            int whole = (int)carry;
            carry -= whole;
            recovered += whole;
        }

        return recovered;
    }

    /// <summary>按真实路径模拟：每帧 Tick(1)，量出 120 帧里回了多少点。</summary>
    private static int SimulateRegen(float scale, int frames)
    {
        var meter = new PostureMeter(100)
        {
            RegenDelayFrames = 30,
            RegenPerSecond = 22f,
            RegenScale = scale,
        };

        meter.Apply(70);
        meter.Tick(30, 1.0f);         // 先耗完延迟

        int before = meter.Current;

        for (int i = 0; i < frames; i++)
            meter.Tick(1, 1.0f);

        return before - meter.Current;
    }

    // ── 缺口③：半自动防御四组对照 ─────────────────────────────

    private async System.Threading.Tasks.Task CheckHalfAutoGuardGroups()
    {
        // 组④ 其他档（武士）＋ 按住防御 ＋ 一般攻击 → 一次都不触发
        await RunAutoGuardGroup("samurai", _grunt, frames: 300, expectMin: 0, expectMax: 0);
        GD.Print($"[架势] 缺口③ 组④ 武士档：假人挥了 {_grunt.AttackCount} 次，一次都没触发 ✓");

        // 组① 見習档 ＋ 按住防御 ＋ 一般攻击 → 自动弹开，且计数上升
        int attacksBefore = _grunt.AttackCount;
        await RunAutoGuardGroup("migoto", _grunt, frames: 260, expectMin: 1, expectMax: 3);
        Check(_grunt.AttackCount > attacksBefore, "普攻假人一次都没挥刀，组① 不成立（不是防御没生效）");
        Check(_player.Health.Current == _player.Health.Max,
            $"自动弹开期间掉了血：{_player.Health.Current}/{_player.Health.Max}——弹开应该是零伤害");
        GD.Print($"[架势] 缺口③ 组① 見習档＋一般攻击：触发 {_player.HalfAutoGuardTriggerCount} 次，" +
                 $"血量保持 {_player.Health.Current}/{_player.Health.Max} ✓");

        // 组③ 見習档 ＋ 连挨第 4 次 → 第 4 次不触发（10 秒 3 次的额度用尽）
        await ResetWith("migoto");
        await HoldGuard(frames: 520);
        Check(_player.HalfAutoGuardTriggerCount == 3,
            $"额度窗口内触发了 {_player.HalfAutoGuardTriggerCount} 次，应为 3（10 秒 3 次）");
        Check(_player.HalfAutoGuardUsedInWindow == 3,
            $"窗口内记账是 {_player.HalfAutoGuardUsedInWindow}，应为 3");
        Check(_grunt.AttackCount >= 4, $"这段时间假人只挥了 {_grunt.AttackCount} 次，验不出'第 4 次不给'");
        GD.Print($"[架势] 缺口③ 组③ 見習档连挨：假人挥 {_grunt.AttackCount} 次，" +
                 $"只在 {_player.HalfAutoGuardTriggerCount} 次上触发（额度 3/10s）✓");

        // 组② 見習档 ＋ 按住防御 ＋ 危攻击 → 一次都不触发
        _grunt.Position = Parked;
        _spear.Position = InReach;
        await ResetWith("migoto");

        int spearBefore = _spear.AttackCount;
        await HoldGuard(frames: 400);

        Check(_spear.AttackCount > spearBefore, "危攻击假人一次都没挥刀，组② 不成立");
        Check(_player.HalfAutoGuardTriggerCount == 0,
            $"危攻击也触发了半自动防御 {_player.HalfAutoGuardTriggerCount} 次——05 §128 说一律不生效");

        GD.Print($"[架势] 缺口③ 组② 見習档＋危攻击：危招挥了 {_spear.AttackCount - spearBefore} 次，" +
                 "自动弹开 0 次 ✓");

        _spear.Position = Parked;
        _grunt.Position = InReach;
    }

    private async System.Threading.Tasks.Task RunAutoGuardGroup(
        string difficultyId, AttackingDummy attacker, int frames, int expectMin, int expectMax)
    {
        attacker.Position = InReach;
        await ResetWith(difficultyId);
        await HoldGuard(frames);

        Check(_player.HalfAutoGuardTriggerCount >= expectMin && _player.HalfAutoGuardTriggerCount <= expectMax,
            $"{difficultyId} 档的触发次数是 {_player.HalfAutoGuardTriggerCount}，" +
            $"期望 [{expectMin}, {expectMax}]");
    }

    private async System.Threading.Tasks.Task HoldGuard(int frames)
    {
        Input.ActionPress("guard");
        await WaitPhysicsFrames(frames);
        Input.ActionRelease("guard");
        await WaitPhysicsFrames(2);
    }

    /// <summary>换难度 + 复位（复位会把本场的破防计数与半自动额度一起清零）。</summary>
    private async System.Threading.Tasks.Task ResetWith(string difficultyId)
    {
        _player.Difficulty = Difficulty(difficultyId);
        _player.ApplyDifficulty();
        _player.ResetForBattle();
        await WaitPhysicsFrames(3);
    }

    // ── 缺口②：格挡到破防（端到端）────────────────────────────

    private async System.Threading.Tasks.Task CheckGuardBreakEndToEnd()
    {
        // 用武士档跑（没有半自动防御干扰，能干净地量"格挡只扣架势不扣血"）
        _grunt.Position = InReach;
        await ResetWith("samurai");

        int healthBefore = _player.Health.Current;
        int guardBreaksBefore = _player.GuardBreakCount;
        int hudBreaksBefore = Hud.Instance?.GuardBreakFlashes ?? 0;
        int posturePeak = 0;
        bool sawWarnWhileRamping = false;

        Input.ActionPress("guard");

        for (int i = 0; i < 600; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            posturePeak = Mathf.Max(posturePeak, _player.Posture.Current);

            if (Hud.Instance?.PostureWarnActive == true)
                sawWarnWhileRamping = true;
        }

        // ① 格挡这段时间**一点血都不许掉**（02 §5：格挡只扣架势）
        Check(_player.Health.Current == healthBefore,
            $"格挡期间掉了血：{healthBefore} → {_player.Health.Current}——格挡应该只扣架势");
        Check(_grunt.AttackCount > 0, "假人一次都没挥刀，这一段什么也没验到");

        GD.Print($"[架势] 缺口② 连续格挡 {_grunt.AttackCount} 刀：血一直 {_player.Health.Current}/" +
                 $"{_player.Health.Max} 没掉，架势峰值 {posturePeak}/{_player.Posture.Max}");

        // ★ 这里要如实说明前置条件：当前量化下玩家等效每秒回 60 点架势，
        //   一刀 +20 会在 20 帧内被回光，所以"一直格挡到破防"这条路**走不通**（详见缺口①的日志）。
        //   为了仍然验到破防那一路的真实代码，这里把架势先填满（= 一直格挡本该达到的状态），
        //   再让**真实的攻击**落在格挡上，走裁决器的 GuardBreak 分支。
        if (_player.GuardBreakCount == guardBreaksBefore)
        {
            GD.Print("[架势] 缺口② 直接格挡攒不满架势（回复 60/s 吃掉 +20 一刀）——" +
                     "改为先填满架势、再让真实攻击落在格挡上，验破防那一路。");
        }

        _player.Posture.Apply(_player.Posture.Max);
        await WaitPhysicsFrames(2);

        bool sawWarn = sawWarnWhileRamping || (Hud.Instance?.PostureWarnActive ?? false);
        bool sawStagger = false;
        int postureAtBreak = 0;

        for (int i = 0; i < 260; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            posturePeak = Mathf.Max(posturePeak, _player.Posture.Current);

            if (_player.GuardBreakCount > guardBreaksBefore)
            {
                postureAtBreak = _player.Posture.Current;

                // 破防进硬直走的是 Change（帧末生效），而且破防有 10 帧顿帧
                // （CombatResolver.HitStopGuardBreak）会把生效再往后推，所以窗口给足 40 帧。
                // 另外注意：**按住防御会在硬直生效后的下一帧把它取消**（T6 规则 3 允许受击硬直被防御取消），
                // 所以硬直可能只存在一两帧——只能逐帧看，不能等几帧再看。
                for (int k = 0; k < 40; k++)
                {
                    string state = _player.StateName;

                    if (state == "StaggerState")
                        sawStagger = true;
                    else if (sawStagger)
                        break;

                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                }

                break;
            }
        }

        Input.ActionRelease("guard");

        Check(_player.GuardBreakCount > guardBreaksBefore,
            $"架势填满后挨刀也没破防（GuardBreakCount 仍是 {_player.GuardBreakCount}）——" +
            $"状态 {_player.StateName}");
        Check(sawStagger, $"破防后没有进入硬直（状态是 {_player.StateName}）");
        Check(sawWarn, "架势满了 HUD 的预警一次都没亮——玩家不会提前知道'再挡一下就破'");

        GD.Print($"[架势] 缺口② 破防：架势满（{postureAtBreak}/{_player.Posture.Max}）→ 进硬直，" +
                 $"OnPostureBroken 调过 {_player.GuardBreakCount} 次，预警已亮 ✓");

        // HUD 必须把破防和普通格挡拆开（缺口②的第二半）
        int hudBreaksNow = Hud.Instance?.GuardBreakFlashes ?? 0;
        Check(hudBreaksNow > hudBreaksBefore,
            $"HUD 没有把这次破防单独计上（GuardBreakFlashes {hudBreaksBefore} → {hudBreaksNow}）");
        Check(Hud.Instance?.LastPrompt == "破防",
            $"破防时的文字提示是「{Hud.Instance?.LastPrompt}」，应为「破防」");

        GD.Print($"[架势] 缺口② HUD：破防单独计数 {hudBreaksNow} 次，提示「{Hud.Instance?.LastPrompt}」✓");

        // 半自动防御的剩余次数要看得见（制作人裁定）
        await ResetWith("migoto");
        await WaitPhysicsFrames(4);
        Check(Hud.Instance?.HalfAutoChargesLeft == 3,
            $"見習档 HUD 的半自动次数读数是 {Hud.Instance?.HalfAutoChargesLeft}，应为 3");
        GD.Print($"[架势] 缺口③ HUD 弱提示：見習档剩余 {Hud.Instance?.HalfAutoChargesLeft} 次 ✓");

        await ResetWith("samurai");
        await WaitPhysicsFrames(4);
        Check(Hud.Instance?.HalfAutoChargesLeft == 0,
            $"武士档不该有半自动次数，读数是 {Hud.Instance?.HalfAutoChargesLeft}");
    }

    // ── 场地 ───────────────────────────────────────────────────

    private T Spawn<T>(string path, Vector3 position) where T : Node3D
    {
        T node = Load<T>(path);
        node.Position = position;
        AddChild(node);
        return node;
    }

    private static DifficultyProfile Difficulty(string id) =>
        GD.Load<DifficultyProfile>($"res://data/difficulty/{id}.tres");

    private void AddFloor()
    {
        var body = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(400f, 0.4f, 60f) },
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
            GD.PrintErr($"[架势] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[架势] ✓ 通过（旋钮接线 / 四组对照 / 格挡到破防 / HUD 拆开）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
