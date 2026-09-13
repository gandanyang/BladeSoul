using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 蓄力斩端到端验收（T46）：
///     godot --headless --path . res://scenes/tests/ChargedAttack.tscn
///
/// 单测（<c>ChargedAttackSelectorTests</c>）只证明"按住 34/48/62 帧该选哪一段"，
/// **证明不了那一记真的打出去了、伤害真的等于 `.tres` 里的数**——中间隔着
/// 输入采集、状态切换、判定框、仲裁器和物理帧顺序。这个场景量的就是那一半。
///
/// | 组 | 输入 | 期望 |
/// |---|---|---|
/// | 对照 | 点一下就松（8 帧） | **只有**轻攻击（伤害 10），且**没有**打出蓄力斩 |
/// | 一 | 按住 36 帧再松 | **轻斩 10 ＋ 蓄力斩·一 45 = 55** |
/// | 三 | 按住 66 帧再松 | **轻斩 10 ＋ 蓄力斩·三 72 = 82** |
/// | 位移 | 再按一次第三段 | 前冲 ≈ `AdvanceDistance`（1.5m） |
///
/// ★ **为什么是"两段"**：按下攻击键的那一瞬间，轻攻击照旧立刻出（M1 的手感不能动）；
/// **继续按住**才会走进蓄力，松开时再补一记蓄力斩。这是《只狼》的做法——
/// 先挥一刀，按住转蓄力。**替代做法是"按住时不出轻攻击"**，但那会给每一次
/// 轻攻击都加上一个"等你松手"的延迟，等于拿 M1 的核心手感去换这一招，不值得。
/// 所以断言写成 `轻斩 + 蓄力斩`，两个数**都从 `.tres` 读**，不写死。
///
/// 退出码 0 ＝ 全过，1 ＝ 有错。
/// </summary>
public partial class ChargedAttackTest : Node3D
{
    [Export] public int TapFrames { get; set; } = 8;
    [Export] public int HoldFrames1 { get; set; } = 36;
    [Export] public int HoldFrames3 { get; set; } = 66;
    [Export] public int SettleFrames { get; set; } = 100;

    /// <summary>位移容忍（米）。位移是"权重分配 + 物理帧"算出来的，不可能精确到位。</summary>
    [Export] public float AdvanceTolerance { get; set; } = 0.45f;

    private readonly List<string> _failures = new();
    private PlayerActor _player = null!;
    private TrainingDummy _dummy = null!;
    private int _damageDealt;
    private int _hits;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        _dummy = Load<TrainingDummy>("res://scenes/actors/TrainingDummy.tscn");
        _dummy.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_dummy);

        await WaitFrames(10);

        if (EventBus.Instance is { } bus)
            bus.HitResolved += OnHitResolved;

        GD.Print($"[蓄力] 段位阈值来自招式数据：{Describe()}");

        // ── 对照组：点一下就松 → 必须是轻攻击 ───────────────────────
        int before = _hits;
        int chargedBefore = _player.ChargedCount;
        await Swing(TapFrames);
        Check(_player.ChargedCount == chargedBefore, "点一下就松**没有**打出蓄力斩");
        Check(_hits > before, "点一下就松打出了轻攻击（有命中）");

        // ── 第一段 ──────────────────────────────────────────────
        int light = _player.Attacks?.Light1?.Damage ?? -1;
        int c1 = _player.Attacks?.Charged1?.Damage ?? -1;
        int c3 = _player.Attacks?.Charged3?.Damage ?? -1;

        int d1 = await Swing(HoldFrames1);
        Check(_player.LastChargedLevel == 1, $"{HoldFrames1} 帧判成第 1 段（实际第 {_player.LastChargedLevel} 段）");
        Check(d1 == light + c1, $"第一段总伤害 ＝ 轻斩 {light} ＋ 蓄力斩·一 {c1} = {light + c1}（实际 {d1}）");

        // ── 第三段 ──────────────────────────────────────────────
        int d3 = await Swing(HoldFrames3);
        Check(_player.LastChargedLevel == 3, $"{HoldFrames3} 帧判成第 3 段（实际第 {_player.LastChargedLevel} 段）");
        Check(d3 == light + c3, $"第三段总伤害 ＝ 轻斩 {light} ＋ 蓄力斩·三 {c3} = {light + c3}（实际 {d3}）");

        // ── 位移：把假人挪开，免得它把玩家挡住（那样量到的是碰撞不是位移）──
        _dummy.Position = new Vector3(0f, 0.1f, -40f);
        await WaitFrames(20);

        Input.ActionPress("attack");
        await WaitFrames(HoldFrames3);
        Input.ActionRelease("attack");
        Vector3 from = await WaitForChargedStart();
        await WaitFrames(SettleFrames);
        float moved = new Vector2(_player.GlobalPosition.X - from.X, _player.GlobalPosition.Z - from.Z).Length();
        float expected = 1.5f;
        Check(Mathf.Abs(moved - expected) <= AdvanceTolerance,
            $"蓄力斩·三前冲 {expected:F1}m ± {AdvanceTolerance:F2}（实际 {moved:F2}m）");

        GD.Print("");
        GD.Print(_failures.Count == 0
            ? "[蓄力] ✓ 全部通过"
            : $"[蓄力] ✗ {_failures.Count} 条没过：");
        foreach (string f in _failures)
            GD.PrintErr($"[蓄力]   ✗ {f}");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>按一下、按住 <paramref name="holdFrames"/> 帧再松、等打完，返回假人挨的伤害。</summary>
    private async Task<int> Swing(int holdFrames)
    {
        int before = _damageDealt;
        Input.ActionPress("attack");
        await WaitFrames(holdFrames);
        Input.ActionRelease("attack");
        await WaitFrames(SettleFrames);
        return _damageDealt - before;
    }

    /// <summary>等"蓄力斩真的开始了"那一帧，返回玩家当时的位置（量位移的起点）。</summary>
    private async Task<Vector3> WaitForChargedStart()
    {
        int before = _player.ChargedCount;

        for (int i = 0; i < SettleFrames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_player.ChargedCount > before)
                return _player.GlobalPosition;
        }

        Check(false, "按下 66 帧后松开，**蓄力斩根本没开始**");
        return _player.GlobalPosition;
    }

    private async Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void OnHitResolved(HitEvent e)
    {
        if (e.Damage <= 0)
            return;

        _damageDealt += e.Damage;
        _hits++;
    }

    private string Describe()
    {
        var set = _player.Attacks;
        return set is null
            ? "（招式表为空！）"
            : $"一={set.Charged1?.StartupFrames} 二={set.Charged2?.StartupFrames} 三={set.Charged3?.StartupFrames}";
    }

    private void Check(bool ok, string what)
    {
        if (ok)
        {
            GD.Print($"[蓄力]   ✓ {what}");
            return;
        }

        _failures.Add(what);
    }

    private void AddFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        var shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(60f, 0.4f, 60f) },
        };
        floor.AddChild(shape);
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
    }

    private static T Load<T>(string path) where T : Node => GD.Load<PackedScene>(path).Instantiate<T>();
}
