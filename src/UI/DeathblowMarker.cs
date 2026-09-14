using Godot;
using Oniblade.Combat;
using Oniblade.Combat.Data;

namespace Oniblade.UI;

/// <summary>
/// 敌人头顶的**处决标记**（T52）：敌人被破韧、且玩家够得着时，在它头上画一个忍杀符号。
///
/// # 为什么这个 UI 是必需的（不是"锦上添花"）
/// 破韧窗口只有 2 秒。没有标记的话，玩家看到敌人跪下只知道"它不动了"，
/// **不知道"现在按 F 可以处决"** —— 这个机制就等于不存在。
/// 11 号文档的立场是"反馈层不是内容，是核心机制的可读性"，处决标记是这句话的直接应用。
///
/// # 实现纪律
/// · UI **不许引用具体战斗类型**（docs/00 §2.8）。所以这里只读
///   <see cref="ICombatActorDebug"/>（只读视图）和 Godot 组 `combat_actor`，
///   不出现 `Ashigaru` 任何字样。将来第二种敌人实现接口后自动生效。
/// · 该不该亮、闪多快，全部走纯逻辑 <see cref="DeathblowMarkerView"/>（有单测）。
///   本类只负责"把判定结果画出来"。
/// · 距离上限与处决判定**同源**：默认读同一个 <see cref="DeathblowProfile.MaxDistance"/>，
///   免得出现"标记亮着但按 F 没反应"。
/// </summary>
public partial class DeathblowMarker : Control
{
    /// <summary>
    /// 与处决判定同源的距离上限。留空时用一个保守默认值——
    /// 但**建议在场景里挂上 `data/combat/deathblow.tres`**，否则两边可能不一致。
    /// </summary>
    [Export] public DeathblowProfile? Deathblow { get; set; }

    /// <summary>标记相对敌人头顶的高度（米）。</summary>
    [Export] public float HeadOffset { get; set; } = 2.35f;

    /// <summary>标记大小（像素）。</summary>
    [Export] public float MarkerSize { get; set; } = 13f;

    /// <summary>标记颜色（默认"忍杀红"）。</summary>
    [Export] public Color MarkerColor { get; set; } = new(0.92f, 0.16f, 0.18f);

    /// <summary>总开关。</summary>
    [Export] public bool Enable { get; set; } = true;

    /// <summary>
    /// 闪烁周期（帧）。窗口越接近关闭闪得越快——
    /// 这给了玩家一个**不用看数字**就能判断"还剩多久"的读数。
    /// </summary>
    [Export] public int BlinkPeriodFrames { get; set; } = 20;

    /// <summary>自检/探针用：本帧画了几个标记。</summary>
    public int VisibleMarkerCount { get; private set; }

    /// <summary>自检/探针用：本帧扫到几个候选（可处决的）。</summary>
    public int CandidateCount { get; private set; }

    private Node3D? _playerNode;
    private double _blinkPhase;

    public override void _EnterTree()
    {
        // 铺满屏幕，但不吃鼠标事件（不然会挡住 HUD 上的按钮）
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        VisibleMarkerCount = 0;
        CandidateCount = 0;
        _blinkPhase += delta;

        if (!Enable)
        {
            QueueRedraw();
            return;
        }

        ResolvePlayer();

        Camera3D? camera = GetViewport()?.GetCamera3D();

        if (_playerNode is null || camera is null)
        {
            QueueRedraw();
            return;
        }

        Vector3 origin = _playerNode.GlobalPosition;
        Vector2 viewport = GetViewportRect().Size;
        float maxDistance = Deathblow?.MaxDistance ?? 2.2f;

        foreach (Node node in GetTree().GetNodesInGroup(CombatActor.GroupName))
        {
            // 三层保险，缺一不可：
            //   1. IsInstanceValid —— 敌人被打死时节点会先释放，只判 null 会拿到失效引用
            //   2. 是 Node3D         —— 需要世界坐标才能投影
            //   3. 实现了只读接口     —— 木桩等没有这个读数的单位直接跳过
            if (!IsInstanceValid(node) || node is not Node3D node3D
                || node is not ICombatActorDebug debug)
                continue;

            if (!debug.CanBeExecuted)
                continue;

            CandidateCount++;

            // 距离判定用水平面（与 DeathblowResolver 一致）：处决是地面动作，不看高度差
            float dx = node3D.GlobalPosition.X - origin.X;
            float dz = node3D.GlobalPosition.Z - origin.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            if (!DeathblowMarkerView.ShouldShow(true, distance, maxDistance, Enable))
                continue;

            Vector3 world = node3D.GlobalPosition + Vector3.Up * HeadOffset;

            if (camera.IsPositionBehind(world))
                continue;

            Vector2 point = camera.UnprojectPosition(world);

            if (point.X < -64f || point.Y < -64f
                || point.X > viewport.X + 64f || point.Y > viewport.Y + 64f)
                continue;

            float urgency = DeathblowMarkerView.Urgency(debug.StateFrame, debug.StateTotalFrames);
            DrawMarker(point, urgency);
            VisibleMarkerCount++;
        }

        QueueRedraw();
    }

    /// <summary>
    /// 画一个忍杀符号：外圈菱形 + 中间的竖划（"斩"的意象）。
    ///
    /// 不用 `Label` 写汉字：那要依赖字体资源，而中文字体在无头环境/不同机器上
    /// **可能缺字变成方块**，那时标记就等于没画。几何图形到哪都一样。
    /// </summary>
    private void DrawMarker(Vector2 center, float urgency)
    {
        // 越紧迫闪得越快：周期从 BlinkPeriodFrames 缩到它的 1/3
        int period = Mathf.Max(4, BlinkPeriodFrames);
        float speed = Mathf.Lerp(1f, 3f, urgency);
        float alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)_blinkPhase * speed * Mathf.Pi * 2f / (period / 60f)));

        Color color = MarkerColor;
        color.A = alpha;

        float half = MarkerSize * 0.5f;

        // 外圈菱形（四条边，闭合成"忍杀"的菱形框）
        var diamond = new Vector2[]
        {
            center + new Vector2(0f, -half),
            center + new Vector2(half, 0f),
            center + new Vector2(0f, half),
            center + new Vector2(-half, 0f),
            center + new Vector2(0f, -half),
        };
        DrawPolyline(diamond, color, 2f, true);

        // 中间竖划
        DrawLine(center + new Vector2(0f, -half * 0.45f),
                 center + new Vector2(0f, half * 0.45f),
                 color, 2f, true);
    }

    /// <summary>
    /// 玩家从组 <c>player</c> 找。
    /// 用 `IsInstanceValid` 重查而不是只判 null —— 节点释放后 C# 包装对象仍非 null，
    /// 只判 null 会一直拿着失效引用（T49/T50 那个 755 次 `ObjectDisposedException` 的同一个根因）。
    /// </summary>
    private void ResolvePlayer()
    {
        if (_playerNode is not null && IsInstanceValid(_playerNode))
            return;

        _playerNode = null;

        foreach (Node node in GetTree().GetNodesInGroup(Hud.PlayerGroup))
        {
            if (node is Node3D player)
            {
                _playerNode = player;
                return;
            }
        }
    }
}
