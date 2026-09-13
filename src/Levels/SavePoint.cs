using Godot;
using Oniblade.Combat;

namespace Oniblade.Levels;

/// <summary>
/// 鬼火存档点（T42 的后半张 / 03 §7）。
///
/// 它做的事只有一件：**玩家走到近处时，把"死亡后回到哪"改成这里**。
/// 具体是把玩家的 <see cref="CombatActor.SpawnTransform"/> 换成当前变换——
/// 而重开协议（`BattleReset` / T14）本来就会把人送回那个变换，
/// 所以**不需要新写一套复活逻辑**，也不会和"≤3 秒原地重开"那条打架。
///
/// 灰盒阶段的表现：点亮一盏灯笼（没有灯就自己加一盏）。
/// 颜色纪律：暖色只属于灯笼（10 §1），所以这里用暖色是**合规的**。
/// </summary>
public partial class SavePoint : Node3D
{
    /// <summary>玩家进到多近算"存档"（米）。</summary>
    [Export] public float ActivationRadius { get; set; } = 2.5f;

    /// <summary>点亮后的灯色（暖色 —— 在本作里暖色只允许出现在灯笼上）。</summary>
    [Export] public Color LightColor { get; set; } = new(1f, 0.62f, 0.28f);

    [Export] public float LightEnergy { get; set; } = 2.2f;
    [Export] public float LightRange { get; set; } = 8f;

    /// <summary>已经激活过。</summary>
    public bool IsActive { get; private set; }

    /// <summary>第几帧激活的（测试用）。</summary>
    public int ActivatedFrame { get; private set; } = -1;

    /// <summary>一共被激活过几次（重复走过不会重复计数）。</summary>
    public int ActivationCount { get; private set; }

    private Node3D? _player;
    private OmniLight3D? _light;
    private int _frame;

    public override void _Ready()
    {
        // 灰盒：没有灯就自己补一盏（关着，等激活时点亮）。
        _light = GetNodeOrNull<OmniLight3D>("Light");
        if (_light is null)
        {
            _light = new OmniLight3D { Name = "Light" };
            AddChild(_light);
        }

        _light.LightColor = LightColor;
        _light.LightEnergy = 0f;      // 未激活不发光
        _light.OmniRange = LightRange;
        _light.ShadowEnabled = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        _player ??= FindPlayer();
        if (_player is null)
            return;

        float distance = new Vector2(
            _player.GlobalPosition.X - GlobalPosition.X,
            _player.GlobalPosition.Z - GlobalPosition.Z).Length();

        if (distance > ActivationRadius)
            return;

        if (IsActive)
            return;

        Activate(_player);
    }

    private void Activate(Node3D player)
    {
        IsActive = true;
        ActivatedFrame = _frame;
        ActivationCount++;

        if (_light is not null)
            _light.LightEnergy = LightEnergy;

        // ★ 真正的那一步：把玩家的"战场起点"改到这里（位置 + 朝向一起记）。
        if (player is CombatActor actor)
            actor.SetSpawnTransform(player.GlobalTransform);

        GD.Print($"[鬼火] {Name} 已点亮（第 {_frame} 帧）→ 死亡后从这里重开");
    }

    private Node3D? FindPlayer()
    {
        foreach (Node node in GetTree().GetNodesInGroup("player"))
        {
            if (node is Node3D player)
                return player;
        }

        return null;
    }
}
