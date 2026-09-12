using Godot;
using Oniblade.Combat;

namespace Oniblade.UI;

/// <summary>
/// 未锁定敌人的头顶细条（T31 / 11 §4.2）。**它只读**：每帧从 <see cref="ICombatActorDebug"/>
/// 轮询血与架势，把它投影到屏幕上，绝不反向影响战斗逻辑。
///
/// ★ 为什么不能"有敌人就挂条"（11 §4.2 的原话）：
/// 同屏 8 个敌人会挂满条，画面立刻变成仪表盘。所以显示规则是
/// **受伤或靠近**二者之一——这条规则被抽成纯函数 <see cref="ShouldShow"/>，
/// 因为它是这张卡里唯一能被无头自检断言住的部分（投影结果没法断言）。
///
/// 为什么是 Hud 的子节点而不是新 autoload：T31 只允许往 project.godot
/// **追加一个** Hud autoload（"只追加，不许动 [input]"），所以这层跟着 Hud 走。
/// </summary>
public partial class EnemyBars : Control
{
    [ExportGroup("配色（延续 11 §2 颜色纪律）")]
    [Export] public Color HealthColor { get; set; } = new(0.478f, 0.078f, 0.094f);   // #7A1418 暗红
    [Export] public Color PostureColor { get; set; } = new(0.784f, 0.196f, 0.227f);  // #C8323A 血红
    [Export] public Color TrackColor { get; set; } = new(0.08f, 0.09f, 0.10f, 0.72f);

    [ExportGroup("尺寸")]
    [Export] public float BarWidth { get; set; } = 54f;
    [Export] public float HealthHeight { get; set; } = 5f;
    [Export] public float PostureHeight { get; set; } = 3f;
    [Export] public float RowGap { get; set; } = 1f;

    /// <summary>条挂在头顶多高（米）。太贴近头部会和模型糊在一起。</summary>
    [Export] public float HeadOffset { get; set; } = 2.05f;

    [ExportGroup("显示规则（11 §4.2：只在受伤或靠近时显示）")]
    [Export] public float NearDistance { get; set; } = 7f;
    [Export] public float MaxDistance { get; set; } = 26f;

    /// <summary>受伤后仍然显示多少帧（60fps 下 180 帧 = 3 秒）。</summary>
    [Export] public int HurtHoldFrames { get; set; } = 180;

    /// <summary>同屏最多几条。同屏敌人数上限是 8（07 §7），所以池子给 8。</summary>
    [Export] public int MaxBars { get; set; } = 8;

    [Export] public float FadeSpeed { get; set; } = 6f;
    [Export] public float OffscreenMargin { get; set; } = 48f;
    [Export] public bool Enable { get; set; } = true;

    // ── 只读观测值：无头自检靠这些断言，不靠看画面 ──

    /// <summary>按显示规则"应该显示"的条数（纯逻辑，与投影无关）。</summary>
    public int DecidedVisibleCount { get; private set; }

    /// <summary>实际画到屏幕上的条数（投影后仍在视口内）。</summary>
    public int VisibleBarCount { get; private set; }

    public int NearShownCount { get; private set; }
    public int HurtShownCount { get; private set; }

    /// <summary>本帧扫到的候选（组内、非玩家、非锁定目标、未死）数量。</summary>
    public int CandidateCount { get; private set; }

    /// <summary>
    /// 显示规则（11 §4.2）。纯函数，便于自检直接断言四个象限：
    /// 近处未受伤 → 显示；远处未受伤 → 不显示；**受伤即使很远 → 显示**；超出 MaxDistance → 一律不显示。
    /// </summary>
    public static bool ShouldShow(float distance, int hurtFramesLeft, float nearDistance, float maxDistance)
        => distance <= maxDistance && (distance <= nearDistance || hurtFramesLeft > 0);

    // 预分配池 + 复用缓冲：_Process 里禁止 new（11 §7 硬约束 2）
    private BarSlot[] _slots = System.Array.Empty<BarSlot>();
    private readonly int[] _selectedIds = new int[32];
    private readonly float[] _selectedDists = new float[32];
    private readonly ICombatActorDebug[] _selectedDebug = new ICombatActorDebug[32];
    private readonly Node3D[] _selectedNodes = new Node3D[32];

    // actorId → 还剩几帧算"刚受过伤"；actorId → 上次看到的血量（用来发现"掉了血"）
    private readonly System.Collections.Generic.Dictionary<int, int> _hurtFrames = new();
    private readonly System.Collections.Generic.Dictionary<int, int> _lastHealth = new();

    private Node3D? _playerNode;

    public override void _EnterTree()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildSlots();
    }

    public override void _Process(double delta)
    {
        DecidedVisibleCount = 0;
        VisibleBarCount = 0;
        NearShownCount = 0;
        HurtShownCount = 0;
        CandidateCount = 0;

        if (!Enable || Hud.Instance is null)
        {
            FadeAllOut(delta);
            return;
        }

        ResolvePlayer();

        Camera3D? camera = GetViewport()?.GetCamera3D();

        if (_playerNode is null || camera is null)
        {
            FadeAllOut(delta);
            return;
        }

        int focusId = Hud.Instance.FocusActorId;
        Vector3 origin = _playerNode.GlobalPosition;
        int count = CollectVisible(origin, focusId);

        DecidedVisibleCount = count;

        // 近处/受伤各自计了几条 —— 自检用来说明"为什么显示"
        for (int i = 0; i < count; i++)
        {
            if (_selectedDists[i] <= NearDistance)
                NearShownCount++;
            else
                HurtShownCount++;   // 能在远处出现，只可能是受伤保持
        }

        Vector2 viewport = GetViewportRect().Size;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (i >= count)
            {
                FadeSlot(_slots[i], false, delta);
                continue;
            }

            Vector3 world = _selectedNodes[i].GlobalPosition + Vector3.Up * HeadOffset;

            if (camera.IsPositionBehind(world))
            {
                FadeSlot(_slots[i], false, delta);
                continue;
            }

            Vector2 point = camera.UnprojectPosition(world);

            if (point.X < -OffscreenMargin || point.Y < -OffscreenMargin ||
                point.X > viewport.X + OffscreenMargin || point.Y > viewport.Y + OffscreenMargin)
            {
                FadeSlot(_slots[i], false, delta);
                continue;
            }

            ICombatActorDebug actor = _selectedDebug[i];
            Layout(_slots[i], point, Ratio(actor.Health, actor.MaxHealth), Ratio(actor.Posture, actor.MaxPosture));
            FadeSlot(_slots[i], true, delta);
            VisibleBarCount++;
        }
    }

    /// <summary>
    /// 挑出该显示的敌人，按距离从近到远放进复用缓冲，最多 <see cref="MaxBars"/> 条。
    /// 用插入排序 + 定长缓冲，全程不分配。
    /// </summary>
    private int CollectVisible(Vector3 origin, int focusId)
    {
        int count = 0;
        int capacity = Mathf.Min(MaxBars, _selectedIds.Length);

        foreach (Node node in GetTree().GetNodesInGroup(Hud.ActorGroup))
        {
            if (node is not ICombatActorDebug debug || node is not Node3D actor)
                continue;

            if (ReferenceEquals(node, _playerNode))
                continue;

            if (debug.Health <= 0)          // 死了就不挂条
            {
                Forget(debug.ActorId);
                continue;
            }

            if (debug.ActorId == focusId)   // 锁定目标有屏幕上方的大面板，别重复挂
                continue;

            CandidateCount++;

            float distance = actor.GlobalPosition.DistanceTo(origin);
            int hurt = TrackHurt(debug);

            if (!ShouldShow(distance, hurt, NearDistance, MaxDistance))
                continue;

            // 插入排序（近的在前）；满了就挤掉最远的那条
            int at = count < capacity ? count : capacity - 1;

            if (count == capacity && distance >= _selectedDists[at])
                continue;

            while (at > 0 && _selectedDists[at - 1] > distance)
            {
                _selectedIds[at] = _selectedIds[at - 1];
                _selectedDists[at] = _selectedDists[at - 1];
                _selectedDebug[at] = _selectedDebug[at - 1];
                _selectedNodes[at] = _selectedNodes[at - 1];
                at--;
            }

            _selectedIds[at] = debug.ActorId;
            _selectedDists[at] = distance;
            _selectedDebug[at] = debug;
            _selectedNodes[at] = actor;

            if (count < capacity)
                count++;
        }

        return count;
    }

    /// <summary>
    /// 发现"血量掉了"就重置受伤计时。返回还剩几帧算刚受伤（0 = 没受伤）。
    ///
    /// ★ 这里有个坑：**血量基线必须每帧都留**。第一版在"没受伤"的路径上顺手把
    /// <c>_lastHealth</c> 也删了，结果下一帧没有基线可比，掉血永远测不出来
    /// （HudTest 的"22m 受伤要挂条"当场抓住）。所以只删计时键，基线留着。
    /// </summary>
    private int TrackHurt(ICombatActorDebug debug)
    {
        int id = debug.ActorId;
        int health = debug.Health;

        int left = _hurtFrames.TryGetValue(id, out int stored) ? stored : 0;
        bool dropped = _lastHealth.TryGetValue(id, out int previous) && health < previous;

        _lastHealth[id] = health;

        if (dropped)
        {
            _hurtFrames[id] = HurtHoldFrames;
            return HurtHoldFrames;
        }

        if (left > 1)
        {
            left--;
            _hurtFrames[id] = left;
            return left;
        }

        _hurtFrames.Remove(id);
        return 0;
    }

    /// <summary>单位死了就从两张表里划掉，避免字典随敌人增删一直长。</summary>
    private void Forget(int actorId)
    {
        _hurtFrames.Remove(actorId);
        _lastHealth.Remove(actorId);
    }

    private void ResolvePlayer()
    {
        if (_playerNode is not null && GodotObject.IsInstanceValid(_playerNode))
            return;

        _playerNode = null;

        foreach (Node node in GetTree().GetNodesInGroup(Hud.PlayerGroup))
        {
            if (node is Node3D node3D)
            {
                _playerNode = node3D;
                return;
            }
        }
    }

    // ── 布局与淡入淡出 ─────────────────────────────────────────

    private void Layout(BarSlot slot, Vector2 point, float healthRatio, float postureRatio)
    {
        float height = HealthHeight + RowGap + PostureHeight;

        slot.Root.OffsetLeft = point.X - BarWidth / 2f;
        slot.Root.OffsetRight = point.X + BarWidth / 2f;
        slot.Root.OffsetTop = point.Y - height;
        slot.Root.OffsetBottom = point.Y;

        // 敌人条**左右收缩**（与玩家条的"向上生长"形成形状差异，05 §4.3 灰度下也要分得清）
        LayoutCentered(slot.HealthFill, BarWidth, healthRatio);
        LayoutCentered(slot.PostureFill, BarWidth, postureRatio);
    }

    private static void LayoutCentered(ColorRect fill, float trackWidth, float ratio)
    {
        float width = trackWidth * ratio;
        fill.OffsetLeft = (trackWidth - width) / 2f;
        fill.OffsetRight = (trackWidth + width) / 2f;
    }

    private void FadeAllOut(double delta)
    {
        foreach (BarSlot slot in _slots)
            FadeSlot(slot, false, delta);
    }

    private void FadeSlot(BarSlot slot, bool visible, double delta)
    {
        float target = visible ? 1f : 0f;

        if (slot.Alpha < target)
            slot.Alpha = Mathf.Min(target, slot.Alpha + FadeSpeed * (float)delta);
        else if (slot.Alpha > target)
            slot.Alpha = Mathf.Max(target, slot.Alpha - FadeSpeed * (float)delta);

        slot.Root.Visible = slot.Alpha > 0.01f;

        if (slot.Root.Visible)
            slot.Root.Modulate = new Color(1f, 1f, 1f, slot.Alpha);
    }

    private static float Ratio(int value, int max) =>
        max <= 0 ? 0f : Mathf.Clamp(value / (float)max, 0f, 1f);

    // ── 建池（只在 _EnterTree 里 new）────────────────────────────

    private void BuildSlots()
    {
        _slots = new BarSlot[Mathf.Max(1, MaxBars)];

        for (int i = 0; i < _slots.Length; i++)
        {
            var root = new Control
            {
                Name = $"EnemyBar{i}",
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(root);

            ColorRect healthTrack = MakeRect(root, TrackColor, 0f, 0f, BarWidth, HealthHeight);
            ColorRect healthFill = MakeRect(root, HealthColor, 0f, 0f, BarWidth, HealthHeight);

            float postureTop = HealthHeight + RowGap;
            ColorRect postureTrack = MakeRect(root, TrackColor, 0f, postureTop, BarWidth, PostureHeight);
            ColorRect postureFill = MakeRect(root, PostureColor, 0f, postureTop, BarWidth, PostureHeight);

            _slots[i] = new BarSlot(root, healthTrack, healthFill, postureTrack, postureFill);
        }
    }

    private static ColorRect MakeRect(Control parent, Color color, float x, float y, float w, float h)
    {
        var rect = new ColorRect
        {
            Color = color,
            MouseFilter = MouseFilterEnum.Ignore,
            OffsetLeft = x,
            OffsetTop = y,
            OffsetRight = x + w,
            OffsetBottom = y + h,
        };
        parent.AddChild(rect);
        return rect;
    }

    /// <summary>一条头顶细条 = 血（上）＋ 架势（下），各有一条底色轨。</summary>
    private sealed class BarSlot
    {
        public readonly Control Root;
        public readonly ColorRect HealthFill;
        public readonly ColorRect PostureFill;
        public float Alpha;

        public BarSlot(Control root, ColorRect healthTrack, ColorRect healthFill,
            ColorRect postureTrack, ColorRect postureFill)
        {
            Root = root;
            _ = healthTrack;
            _ = postureTrack;
            HealthFill = healthFill;
            PostureFill = postureFill;
        }
    }
}
