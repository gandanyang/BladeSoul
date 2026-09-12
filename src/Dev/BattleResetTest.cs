using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 死亡与原地重开端到端测试（T14）：
///     godot --headless --path . res://scenes/tests/BattleReset.tscn
///
/// T14 的验收明确要求"时间必须实测，不许估"，所以本测试的核心输出是
/// **death → 回到可控 的实测帧数**，而不是"应该小于 180"。
///
/// 它同时验证"重开之后世界是干净的"：
///   · 玩家：位置回原位、满血、状态回 Idle、喝血次数补满、**真的能吃输入**
///   · 敌人：位置回原位、满血、**卡在死亡等待里的假人也被救活**
///
/// "能吃输入"那一条不是锦上添花：它是"回到可控"的**定义**。
/// 只把 IsDead 置回 false 而状态机停在硬直里，玩家看着是活的、实际动不了。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class BattleResetTest : Node3D
{
    /// <summary>01 §0 规则 1 的上限：180 帧 = 3 秒。</summary>
    private const int MaxRestartFrames = 180;

    /// <summary>先把世界弄"脏"的帧数（让假人砍几刀、让它前冲离开原位）。</summary>
    private const int SetupFrames = 260;

    private const int MoveProbeFrames = 14;

    private readonly List<string> _failures = new();

    private PlayerActor _player = null!;
    private AttackingDummy _attacker = null!;
    private TrainingDummy _respawnDummy = null!;

    private Vector3 _playerSpawn;
    private Vector3 _attackerSpawn;
    private int _chargesBefore;
    private int _deathFrame = -1;
    private int _restartFrame = -1;
    private bool _respawnDeadAtReset;

    // 复位那一刻的快照。必须在**位移探针之前**采样：
    // 探针会把玩家走出去，而复位后敌人冷却归零会立刻再前冲——
    // 拿探针之后的位置去断言"回到原位"，测的其实是"这 14 帧里它动了多少"。
    private Vector3 _playerPosAtReset;
    private Vector3 _attackerPosAtReset;
    private int _playerHpAtReset;
    private int _attackerHpAtReset;
    private bool _respawnDeadAfterReset;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddChild(new BattleReset { Name = "BattleReset" });
        AddFloor();

        _player = Load<PlayerActor>("res://scenes/actors/Player.tscn");
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        _attacker = Load<AttackingDummy>("res://scenes/actors/AttackingDummy.tscn");
        _attacker.Position = new Vector3(0f, 0.1f, -1.7f);
        AddChild(_attacker);

        _respawnDummy = Load<TrainingDummy>("res://scenes/actors/RespawnDummy.tscn");
        _respawnDummy.Position = new Vector3(2.5f, 0.1f, 0f);
        AddChild(_respawnDummy);

        // ⚠️ 原位必须**在这里**记，不能等几帧之后再记：
        // 挥砍假人从第 1 帧起就会前冲（冷却初始为 0、玩家又在攻击距离内），
        // 等 10 帧再记，记到的已经是"冲出去一段之后"的位置了，
        // 于是复位后反而会被判成"没回到原位"。
        _playerSpawn = _player.GlobalPosition;
        _attackerSpawn = _attacker.GlobalPosition;

        await WaitPhysicsFrames(10);

        Check(BattleReset.Instance is not null, "BattleReset 没有注册成单例（_EnterTree 没跑？）");

        _chargesBefore = _player.HealChargesLeft;

        Check(_chargesBefore > 0, "玩家进场时喝血次数为 0，后面的「补满」断言没有意义");

        // ── 阶段 1：把世界弄脏 ──────────────────────────────────
        for (int frame = 0; frame < SetupFrames; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Check(_player.Health.Current < _player.Health.Max,
            $"假人砍了 {SetupFrames} 帧玩家还是满血：世界没被弄脏，重开也就测不出东西");

        // 挥砍假人掉血（验证"满血复位"），并且它已经前冲离开了原位（验证"位置复位"）
        _attacker.Health.Apply(45);

        bool attackerMoved = _attacker.GlobalPosition.DistanceTo(_attackerSpawn) > 0.1f;
        Check(_attacker.Health.Current < _attacker.Health.Max, "挥砍假人没有掉血，满血复位断言无效");
        Check(attackerMoved,
            $"挥砍假人一步都没动（距原位 {_attacker.GlobalPosition.DistanceTo(_attackerSpawn):0.###} m）：位置复位断言无效");

        // ── 阶段 2：先把复活次数耗光，再杀死玩家，逐帧数到"回到可控" ──
        //
        // T22 之后"死亡"会**先走复活**（当场站起来，不触发重开），
        // 而 T14 的重开协议只在复活次数用尽后才接管。
        // 所以这里必须先把次数耗光才能测到真正的重开路径——
        // 耗光的过程本身也在测"复活确实会消耗次数"。
        int exhaustGuard = 0;
        while (_player.RevivesLeft > 0 && exhaustGuard++ < 8)
        {
            _player.Health.Apply(_player.Health.Max);
            _player.Die();
            await WaitForRecovery();
        }

        Check(_player.RevivesLeft == 0,
            $"复活次数没有耗尽（还剩 {_player.RevivesLeft}）：下面的重开断言测不到东西");

        // 打死重生假人，让它卡在"死亡等待"里——这正是 T14 规则 4 说的那个坑。
        //
        // ⚠️ 必须放在**耗光复活之后**：它的重生延迟只有 120 帧，
        // 而耗尽复活要花 90 帧 × N。放在前面的话，等我们真要测量时
        // 它已经自己活过来了，这条断言就永远测不到东西（而且会假装通过）。
        //
        // 注意：Health.Apply 只改数字，**不会触发 Die()**（那只发生在 ReceiveVerdict 里），
        // 所以这里必须显式调 Die()。
        _respawnDummy.Health.Apply(_respawnDummy.Health.Max);
        _respawnDummy.Die();

        Check(_respawnDummy.IsDead, "重生假人没有被打死，复位断言无效");
        Check(_respawnDummy.IsWaitingToRespawn, "重生假人没有进入死亡等待状态，复位断言无效");

        _deathFrame = (int)Engine.GetPhysicsFrames();
        _player.Die();

        for (int frame = 0; frame < MaxRestartFrames + 60; frame++)
        {
            // 在**这一帧的物理之前**读：复位就发生在这一帧里，
            // 所以这个值反映的正是"复位前"的世界，也就是我们要证明的那个状态。
            bool respawnDeadBefore = _respawnDummy.IsDead;

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (!_player.IsDead && _player.Machine.Current is IdleState)
            {
                _restartFrame = (int)Engine.GetPhysicsFrames();
                _respawnDeadAtReset = respawnDeadBefore;

                // 立刻快照：探针会把玩家走出去。
                _playerPosAtReset = _player.GlobalPosition;
                _attackerPosAtReset = _attacker.GlobalPosition;
                _playerHpAtReset = _player.Health.Current;
                _attackerHpAtReset = _attacker.Health.Current;
                _respawnDeadAfterReset = _respawnDummy.IsDead;

                break;
            }
        }

        // ── 阶段 3：断言 + 真的能动能走 ─────────────────────────
        await ProbeMovement();
        Report();
    }

    /// <summary>等到玩家回到可控（复活演出或重开都算），最多等 MaxRestartFrames + 60 帧。</summary>
    private async Task WaitForRecovery()
    {
        for (int frame = 0; frame < MaxRestartFrames + 60; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (!_player.IsDead && _player.Machine.Current is IdleState)
                return;
        }
    }

    /// <summary>重开后按 move_right，必须真的走起来（"回到可控"的定义）。</summary>
    private async Task ProbeMovement()
    {
        if (_restartFrame < 0)
            return;

        Vector3 before = _player.GlobalPosition;

        Input.ActionPress("move_right");

        for (int i = 0; i < MoveProbeFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Input.ActionRelease("move_right");

        float moved = _player.GlobalPosition.DistanceTo(before);
        Check(moved > 0.05f, $"重开后按 move_right 只移动了 {moved:0.###} m：没有真正回到可控状态");
    }

    private void Report()
    {
        if (_restartFrame < 0)
        {
            Check(false, $"{MaxRestartFrames + 60} 帧内玩家没有回到 Idle + 可动状态（重开协议没跑起来？）");
            Flush();
            return;
        }

        int restartFrames = _restartFrame - _deathFrame;

        GD.Print($"[重开] 实测：死亡 → 回到可控 = **{restartFrames} 帧**" +
                 $"（{restartFrames / 60.0:0.00} 秒，上限 {MaxRestartFrames} 帧）");
        GD.Print($"[重开] 玩家 HP {_playerHpAtReset}/{_player.Health.Max}，" +
                 $"次数 {_chargesBefore} → {_player.HealChargesLeft}，复位那一刻位置偏移 " +
                 $"{_playerPosAtReset.DistanceTo(_playerSpawn):0.###} m");
        GD.Print($"[重开] 敌人：挥砍假人复位那一刻偏移 {_attackerPosAtReset.DistanceTo(_attackerSpawn):0.###} m，" +
                 $"HP {_attackerHpAtReset}/{_attacker.Health.Max}；" +
                 $"重生假人复位前是否卡在死亡等待 = {_respawnDeadAtReset}，复位后 IsDead = {_respawnDeadAfterReset}");

        Check(restartFrames <= MaxRestartFrames,
            $"重开用了 {restartFrames} 帧，超过 01 §0 规则的 {MaxRestartFrames} 帧上限");

        // 玩家侧
        Check(!_player.IsDead, "重开后玩家仍然是死的");
        Check(_playerHpAtReset == _player.Health.Max, "重开后玩家没有满血");
        Check(_player.Posture.Current == 0, "重开后玩家体干没有清零");
        Check(_playerPosAtReset.DistanceTo(_playerSpawn) < 0.2f,
            $"重开后玩家没有回到原位（偏移 {_playerPosAtReset.DistanceTo(_playerSpawn):0.###} m）");
        Check(_player.HealChargesLeft == _chargesBefore,
            $"重开后喝血次数没有补满：{_player.HealChargesLeft}（进场是 {_chargesBefore}）");

        // 敌人侧
        Check(_attackerPosAtReset.DistanceTo(_attackerSpawn) < 0.2f,
            $"重开后挥砍假人没有回到原位（偏移 {_attackerPosAtReset.DistanceTo(_attackerSpawn):0.###} m）");
        Check(_attackerHpAtReset == _attacker.Health.Max, "重开后挥砍假人没有满血");

        Check(_respawnDeadAtReset, "复位前重生假人并不在死亡等待里：这条断言没测到东西");
        Check(!_respawnDeadAfterReset, "重开后重生假人仍然是死的：重生计时没有被复位（T14 规则 4）");
        Check(_respawnDummy.Health.Current == _respawnDummy.Health.Max, "重开后重生假人没有满血");

        Flush();
    }

    private void Flush()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[重开] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[重开] ✓ 原地重开测试通过");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

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

    private async Task WaitPhysicsFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }
}
