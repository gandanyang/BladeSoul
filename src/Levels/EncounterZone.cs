using System.Collections.Generic;
using Godot;
using Oniblade.Combat;

namespace Oniblade.Levels;

/// <summary>
/// 遭遇战（T42）：**走进来 → 敌人出现 → 打死 → 放行**。
///
/// 它挂在关卡里已有的 `combat_area` 标记旁；规则本身在 <see cref="EncounterLogic"/> 里
/// （纯逻辑、可单测），这里只做三件事：探玩家、生成敌人、放行。
///
/// **放行靠组标记**：关卡里把"门/障碍"放进 `encounter_gate` 组，
/// 清场时这个节点会把它们关掉（`CollisionObject3D` 清碰撞层 + `Node3D` 隐藏）。
/// 这样关卡作者不必为每一扇门写脚本——**加一个组名就行**。
/// </summary>
public partial class EncounterZone : Node3D
{
    /// <summary>进了这个组的节点 = 被这场遭遇战挡住的路。</summary>
    public const string GateGroup = "encounter_gate";

    /// <summary>玩家所在的组（与 HUD 用的是同一个）。</summary>
    public const string PlayerGroup = "player";

    [Export] public EncounterProfile? Profile { get; set; }

    /// <summary>清场时放出的信号（关卡脚本可以接它做剧情）。</summary>
    [Signal] public delegate void ClearedEventHandler();

    public EncounterPhase Phase => _logic.Phase;
    public int SpawnedCount { get; private set; }

    /// <summary>清场发生在第几帧（-1 ＝ 还没）。测试用。</summary>
    public int ClearFrame { get; private set; } = -1;

    /// <summary>是否已经放行。</summary>
    public bool GateOpened { get; private set; }

    private readonly EncounterLogic _logic = new();
    private readonly List<Node3D> _spawned = new();
    private PackedScene? _enemyScene;
    private Node3D? _player;
    private int _frame;

    public override void _Ready()
    {
        float activation = 9f;
        float spawnRadius = 5f;
        int count = 3;
        string scenePath = "res://scenes/actors/enemies/Enemy.tscn";

        if (Profile is not null)
        {
            activation = Profile.ActivationRadius;
            spawnRadius = Profile.SpawnRadius;
            count = Profile.EnemyCount;
            scenePath = Profile.EnemyScenePath;
            _logic.ClearHoldFrames = Profile.ClearHoldFrames;
        }
        else
        {
            GD.PrintErr($"[遭遇战] {Name} 没配 EncounterProfile，正在用默认值");
        }

        _activationRadius = activation;
        _spawnRadius = spawnRadius;
        _enemyCount = count;

        if (!ResourceLoader.Exists(scenePath))
        {
            GD.PrintErr($"[遭遇战] 找不到敌人场景：{scenePath}");
            return;
        }

        _enemyScene = GD.Load<PackedScene>(scenePath);
        GD.Print($"[遭遇战] {Name} 就位：进场半径 {activation:F1}m，刷 {count} 只，"
                 + $"清场确认 {_logic.ClearHoldFrames} 帧");
    }

    private float _activationRadius = 9f;
    private float _spawnRadius = 5f;
    private int _enemyCount = 3;

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        if (_enemyScene is null)
            return;

        // 重查条件**不能只判 null**：Godot 节点释放后 C# 包装对象仍非 null、指针失效，
        // 于是 `??=` 永不重查，下面读 GlobalPosition 会每帧抛 ObjectDisposedException。
        // 同款坑在 TutorialDirector 实测刷了 755 次（`IsInstanceValid` 才是正确问法）。
        if (!GodotObject.IsInstanceValid(_player))
            _player = FindPlayer();

        if (_player is null)
            return;

        bool inside = new Vector2(
            _player.GlobalPosition.X - GlobalPosition.X,
            _player.GlobalPosition.Z - GlobalPosition.Z).Length() <= _activationRadius;

        if (_logic.NotifyPlayerInside(inside))
            Spawn();

        if (_logic.Phase == EncounterPhase.Active && _logic.NotifyAliveCount(CountAlive()))
            Clear();
    }

    private void Spawn()
    {
        for (int i = 0; i < _enemyCount; i++)
        {
            float angle = Mathf.Tau * i / Mathf.Max(1, _enemyCount);
            Node3D enemy = _enemyScene!.Instantiate<Node3D>();
            enemy.Position = new Vector3(
                Mathf.Cos(angle) * _spawnRadius,
                0.05f,
                Mathf.Sin(angle) * _spawnRadius);

            AddChild(enemy);
            _spawned.Add(enemy);
        }

        SpawnedCount = _spawned.Count;
        _logic.NotifySpawned(SpawnedCount);
        GD.Print($"[遭遇战] {Name} 触发：刷了 {SpawnedCount} 只");
    }

    private int CountAlive()
    {
        int alive = 0;
        foreach (Node3D enemy in _spawned)
        {
            // 被回收的自由节点算死。还活着的要**没死**才算 alive。
            if (!IsInstanceValid(enemy))
                continue;

            // ★ "死了"要看**血量**，不能只看 `CombatActor.IsDead`——那个标志要到
            // 死亡流程（OnDeath / 状态机）跑完才置位。直接扣血致死的单位会有一段时间
            // `IsDead == false` 而 `Health.IsDead == true`，只看前者会让清场永远等不到。
            // （这一条是端到端测试抓出来的：打死了 3 只，Phase 一直停在 Active。）
            if (enemy is CombatActor actor && (actor.Health.IsDead || actor.IsDead))
                continue;

            alive++;
        }

        return alive;
    }

    private void Clear()
    {
        ClearFrame = _frame;
        OpenGates();
        EmitSignal(SignalName.Cleared);
        GD.Print($"[遭遇战] {Name} 清场（第 {_frame} 帧）→ 放行 {GateOpened}");
    }

    private void OpenGates()
    {
        GateOpened = true;

        foreach (Node node in GetTree().GetNodesInGroup(GateGroup))
        {
            if (node is CollisionObject3D body)
                body.CollisionLayer = 0;      // 不再挡路

            if (node is Node3D visual)
                visual.Visible = false;       // 灰盒阶段：门就是消失
        }
    }

    private Node3D? FindPlayer()
    {
        foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
        {
            if (node is Node3D player)
                return player;
        }

        return null;
    }
}
