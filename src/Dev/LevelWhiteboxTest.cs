using System.Collections.Generic;
using Godot;
using Oniblade.Combat;

namespace Oniblade.Dev;

/// <summary>
/// 关卡白盒验收（T32）：
///     godot --headless --path . res://scenes/tests/LevelWhiteboxDojo.tscn
///     godot --headless --path . res://scenes/tests/LevelWhiteboxGifu.tscn
///
/// 卡片要求"要能自动校验，不靠看着还行"，所以三件事全部由断言完成：
///
/// 1. **可走性**：让一个与玩家同尺寸的胶囊沿 <c>Path</c> 的路径点从入口走到 BOSS 房，
///    断言全程持续推进（连续卡住超过阈值就报出卡点坐标）。
///    它验的是 10 §3.4 那条最容易漏的硬约束：**可走落差 ≤0.2m**
///    （<c>CharacterBody3D</c> 没有自动步高，超过就会卡）。
/// 2. **战斗区净空**：读 <c>combat_area</c> 组里每个标记的 BoxShape3D，
///    断言水平 ≥8×8m；再从地面向上打射线，断言室内天花板 ≥5m（露天则射线无命中）。
/// 3. **战斗区里没有演员**：把"净空"验到底——没有单位站在 8×8 里面。
///
/// 顺带输出一张**俯视平面图 PNG**（从真实场景的几何算出来，不是截屏）：
/// 白盒是给关卡设计看的，平面图比透视截图更好用。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class LevelWhiteboxTest : Node3D
{
    /// <summary>要检查的关卡场景。两个测试场景各配一条。</summary>
    [Export] public string LevelPath { get; set; } = "res://scenes/levels/Dojo.tscn";

    /// <summary>俯视平面图的输出路径（空 = 不输出）。</summary>
    [Export] public string PlanOutputPath { get; set; } = "";

    /// <summary>与玩家一致的移动速度（1 格 4m ≈ 一秒）。</summary>
    [Export] public float WalkSpeed { get; set; } = 4.2f;

    /// <summary>单个路径段最多走多少帧（4m 一格，给足余量）。</summary>
    [Export] public int MaxFramesPerSegment { get; set; } = 300;

    /// <summary>连续多少帧几乎没有位移就算"卡住"（60 帧 = 1 秒）。</summary>
    [Export] public int StuckFramesThreshold { get; set; } = 60;

    /// <summary>战斗区净空下限（10 §3.4）。</summary>
    private const float RequiredClearance = 8.0f;

    /// <summary>室内战斗区净高下限（卡片 §硬约束 2：摄像机 SpringArm 长 4.2m）。</summary>
    private const float RequiredHeadroom = 5.0f;

    private readonly List<string> _failures = new();
    private readonly List<string> _log = new();

    private Node3D _level = null!;

    public override async void _Ready()
    {
        var packed = GD.Load<PackedScene>(LevelPath);
        if (packed is null)
        {
            GD.PrintErr($"[白盒] ✗ 读不到关卡：{LevelPath}");
            GetTree().Quit(1);
            return;
        }

        _level = packed.Instantiate<Node3D>();
        AddChild(_level);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        try
        {
            CheckCombatAreas();
            CheckNoActorsInsideCombatAreas();

            // 把演员清掉再走位：靶子会前冲，撞到自检胶囊会变成假"卡住"。
            // 战斗区与演员位置的关系上面已经验过了。
            _level.GetNodeOrNull("Actors")?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            await WalkThePath();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        WritePlanImage();
        Report();
    }

    // ── 战斗区净空 ─────────────────────────────────────────────

    private void CheckCombatAreas()
    {
        int count = 0;

        foreach (Node node in GetTree().GetNodesInGroup("combat_area"))
        {
            if (node is not Area3D area)
                continue;

            count++;

            CollisionShape3D? shape = area.GetNodeOrNull<CollisionShape3D>("Shape");
            if (shape?.Shape is not BoxShape3D box)
            {
                Check(false, $"{area.Name} 没有 BoxShape3D 子节点，读不到净空");
                continue;
            }

            float width = box.Size.X;
            float depth = box.Size.Z;
            float headroom = MeasureHeadroom(area);

            _log.Add($"{area.Name}：水平 {width:0.#}×{depth:0.#}m，垂直净空 " +
                     (float.IsInfinity(headroom) ? "露天（无遮挡）" : $"{headroom:0.##}m"));

            Check(width >= RequiredClearance && depth >= RequiredClearance,
                $"{area.Name} 水平净空只有 {width:0.#}×{depth:0.#}m，低于 {RequiredClearance:0.#}×{RequiredClearance:0.#}m");

            Check(headroom >= RequiredHeadroom,
                $"{area.Name} 室内净高只有 {headroom:0.##}m，低于 {RequiredHeadroom:0.#}m" +
                "（摄像机 SpringArm 长 4.2m，天花板太低会把镜头顶到贴脸）");
        }

        Check(count > 0, "一个 combat_area 都没有：战斗区标记没做");
    }

    /// <summary>从**地面**（不是区域中心）向上打射线，量天花板高度。露天则无命中。</summary>
    private float MeasureHeadroom(Area3D area)
    {
        Vector3 origin = area.GlobalPosition;
        Vector3 start = new(origin.X, 0.05f, origin.Z);
        Vector3 end = start + Vector3.Up * 40f;

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(start, end);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.CollisionMask = 1;   // 只打世界几何（CSG 碰撞默认在 layer 1）

        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (hit.Count == 0)
            return float.PositiveInfinity;

        // 量的是"离地多高"，所以减掉射线起点那一丁点高度（0.05m）。
        return ((Vector3)hit["position"]).Y - start.Y;
    }

    /// <summary>战斗区里不许站人——这是"净空"的完整含义。</summary>
    private void CheckNoActorsInsideCombatAreas()
    {
        foreach (Node node in GetTree().GetNodesInGroup("combat_area"))
        {
            if (node is not Area3D area)
                continue;

            if (area.GetNodeOrNull<CollisionShape3D>("Shape")?.Shape is not BoxShape3D box)
                continue;

            Vector3 center = area.GlobalPosition;
            float halfX = box.Size.X / 2f;
            float halfZ = box.Size.Z / 2f;

            foreach (Node actorNode in GetTree().GetNodesInGroup("combat_actor"))
            {
                if (actorNode is not Node3D actor)
                    continue;

                Vector3 local = actor.GlobalPosition - center;
                bool inside = Mathf.Abs(local.X) < halfX && Mathf.Abs(local.Z) < halfZ;

                Check(!inside,
                    $"{actor.Name} 站在 {area.Name} 里（局部坐标 {local.X:0.#}, {local.Z:0.#}）：战斗区不干净");
            }
        }
    }

    // ── 可走性 ─────────────────────────────────────────────────

    private async System.Threading.Tasks.Task WalkThePath()
    {
        Node? path = _level.GetNodeOrNull("Path");
        if (path is null)
        {
            Check(false, "关卡里没有 Path 节点：可走性自检没有路径可走");
            return;
        }

        var waypoints = new List<Vector3>();
        foreach (Node child in path.GetChildren())
        {
            if (child is Node3D marker)
                waypoints.Add(marker.GlobalPosition);
        }

        Check(waypoints.Count >= 2, $"Path 只有 {waypoints.Count} 个点，走不起来");

        var walker = new WhiteboxWalker { Name = "Walker" };
        AddChild(walker);
        walker.GlobalPosition = waypoints[0] + new Vector3(0f, 0.2f, 0f);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        float delta = 1f / 60f;
        int totalFrames = 0;

        for (int index = 1; index < waypoints.Count; index++)
        {
            Vector3 target = waypoints[index];
            int stuck = 0;
            int frames = 0;

            while (frames < MaxFramesPerSegment)
            {
                Vector3 before = walker.GlobalPosition;

                walker.StepToward(target, WalkSpeed, delta);

                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

                frames++;
                totalFrames++;

                float moved = new Vector2(
                    walker.GlobalPosition.X - before.X,
                    walker.GlobalPosition.Z - before.Z).Length();

                stuck = moved < 0.005f ? stuck + 1 : 0;

                if (stuck >= StuckFramesThreshold)
                    break;

                if (new Vector2(walker.GlobalPosition.X - target.X,
                        walker.GlobalPosition.Z - target.Z).Length() < 0.6f)
                    break;
            }

            float remaining = new Vector2(
                walker.GlobalPosition.X - target.X,
                walker.GlobalPosition.Z - target.Z).Length();

            Check(remaining < 1.2f,
                $"走到第 {index} 个路径点就过不去了：卡在 " +
                $"({walker.GlobalPosition.X:0.#}, {walker.GlobalPosition.Z:0.#})，距目标还有 {remaining:0.#}m" +
                "（多半是可走落差超过 0.2m，或门洞太窄）");

            _log.Add($"路径点 {index}/{waypoints.Count - 1}：{frames} 帧到达");
        }

        _log.Add($"全程 {totalFrames} 帧（{totalFrames / 60f:0.0} 秒）");

        walker.QueueFree();
    }

    // ── 俯视平面图 ─────────────────────────────────────────────

    private void WritePlanImage()
    {
        if (string.IsNullOrEmpty(PlanOutputPath))
            return;

        const int pixelsPerMeter = 8;
        const int margin = 12;

        var boxes = new List<(Vector3 Center, Vector3 Size)>();

        CollectBoxes(_level, boxes);

        if (boxes.Count == 0)
        {
            Check(false, "场景里一个 CsgBox3D 都没有：平面图画不出来");
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach ((Vector3 center, Vector3 size) in boxes)
        {
            minX = Mathf.Min(minX, center.X - size.X / 2f);
            maxX = Mathf.Max(maxX, center.X + size.X / 2f);
            minZ = Mathf.Min(minZ, center.Z - size.Z / 2f);
            maxZ = Mathf.Max(maxZ, center.Z + size.Z / 2f);
        }

        int width = (int)((maxX - minX) * pixelsPerMeter) + margin * 2;
        int height = (int)((maxZ - minZ) * pixelsPerMeter) + margin * 2;

        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0.10f, 0.11f, 0.12f));

        // 4m 主格线：一眼能看出尺寸对不对
        for (float x = Mathf.Ceil(minX / 4f) * 4f; x <= maxX; x += 4f)
            DrawLine(image, ToPixel(x, minZ, minX, minZ, pixelsPerMeter, margin, height),
                     ToPixel(x, maxZ, minX, minZ, pixelsPerMeter, margin, height),
                     new Color(0.20f, 0.22f, 0.24f));

        for (float z = Mathf.Ceil(minZ / 4f) * 4f; z <= maxZ; z += 4f)
            DrawLine(image, ToPixel(minX, z, minX, minZ, pixelsPerMeter, margin, height),
                     ToPixel(maxX, z, minX, minZ, pixelsPerMeter, margin, height),
                     new Color(0.20f, 0.22f, 0.24f));

        // 几何体
        foreach ((Vector3 center, Vector3 size) in boxes)
        {
            int x0 = (int)ToPixel(center.X - size.X / 2f, center.Z, minX, minZ, pixelsPerMeter, margin, height).X;
            int x1 = (int)ToPixel(center.X + size.X / 2f, center.Z, minX, minZ, pixelsPerMeter, margin, height).X;
            int y0 = (int)ToPixel(center.X, center.Z - size.Z / 2f, minX, minZ, pixelsPerMeter, margin, height).Y;
            int y1 = (int)ToPixel(center.X, center.Z + size.Z / 2f, minX, minZ, pixelsPerMeter, margin, height).Y;

            FillRect(image, x0, y0, x1, y1, new Color(0.34f, 0.37f, 0.40f, 0.9f));
        }

        // 战斗区：青绿高亮，一眼看出 8×8 是否达标
        foreach (Node node in GetTree().GetNodesInGroup("combat_area"))
        {
            if (node is not Area3D area ||
                area.GetNodeOrNull<CollisionShape3D>("Shape")?.Shape is not BoxShape3D box)
                continue;

            Vector3 center = area.GlobalPosition;
            Vector2 topLeft = ToPixel(center.X - box.Size.X / 2f, center.Z - box.Size.Z / 2f,
                minX, minZ, pixelsPerMeter, margin, height);
            Vector2 bottomRight = ToPixel(center.X + box.Size.X / 2f, center.Z + box.Size.Z / 2f,
                minX, minZ, pixelsPerMeter, margin, height);

            FillRect(image, (int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y,
                new Color(0.25f, 0.85f, 0.70f, 0.35f));
        }

        // 路径：黄线
        Node? path = _level.GetNodeOrNull("Path");
        if (path is not null)
        {
            Vector2? previous = null;

            foreach (Node child in path.GetChildren())
            {
                if (child is not Node3D marker)
                    continue;

                Vector2 pixel = ToPixel(marker.GlobalPosition.X, marker.GlobalPosition.Z,
                    minX, minZ, pixelsPerMeter, margin, height);

                if (previous is { } from)
                    DrawLine(image, from, pixel, new Color(0.95f, 0.85f, 0.35f));

                previous = pixel;
            }
        }

        Error error = image.SavePng(PlanOutputPath);
        Check(error == Error.Ok, $"平面图写不出去：{PlanOutputPath}（{error}）");

        if (error == Error.Ok)
            GD.Print($"[白盒] 俯视平面图：{PlanOutputPath}（{width}×{height}px，{pixelsPerMeter}px/m，青绿=战斗区，黄线=自检路径）");
    }

    private static void CollectBoxes(Node node, List<(Vector3, Vector3)> into)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is CsgBox3D box)
                into.Add((box.GlobalPosition, box.Size));

            CollectBoxes(child, into);
        }
    }

    private static Vector2 ToPixel(float x, float z, float minX, float minZ,
        int pixelsPerMeter, int margin, int height)
    {
        // +Z 朝画面下方（俯视图，从上方看下去）
        return new Vector2(
            margin + (x - minX) * pixelsPerMeter,
            height - margin - (z - minZ) * pixelsPerMeter);
    }

    private static void FillRect(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        int left = Mathf.Min(x0, x1);
        int right = Mathf.Max(x0, x1);
        int top = Mathf.Min(y0, y1);
        int bottom = Mathf.Max(y0, y1);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight())
                    continue;

                Color existing = image.GetPixel(x, y);
                image.SetPixel(x, y, existing.Lerp(color, color.A));
            }
        }
    }

    private static void DrawLine(Image image, Vector2 from, Vector2 to, Color color)
    {
        float length = from.DistanceTo(to);
        int steps = Mathf.Max(1, (int)length);

        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = from.Lerp(to, i / (float)steps);
            int x = (int)point.X;
            int y = (int)point.Y;

            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight())
                continue;

            image.SetPixel(x, y, color);
        }
    }

    // ── 报告 ───────────────────────────────────────────────────

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void Report()
    {
        foreach (string line in _log)
            GD.Print($"[白盒] {line}");

        foreach (string failure in _failures)
            GD.PrintErr($"[白盒] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print($"[白盒] ✓ {System.IO.Path.GetFileName(LevelPath)} 通过（可走性 + 战斗区净空）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
