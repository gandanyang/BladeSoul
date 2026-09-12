using System.Collections.Generic;
using Godot;
using Oniblade.World;

namespace Oniblade.Dev;

/// <summary>
/// 室内照明验收（T40）。**把"亮不亮"变成能跑的断言**——卡片明确不许"看着亮多了"就交差。
///
///     godot --headless --path . res://scenes/tests/IndoorLight.tscn
///
/// 三件事：
/// 1. **光覆盖**：每个室内 `combat_area` 的**四角 ＋ 中心**，要么离最近灯笼 ≤
///    `LanternRange × 0.8`，要么落进**天光开口的投影**里（沿真实太阳方向反推到屋顶，
///    看落点是否在洞口矩形内）。两条都不满足就是"这片地方没光"。
/// 2. **不漏光**：室外灯笼的 `LightCullMask` 必须**不含**室内层，室内灯笼必须含；
///    并且室内几何（`Shell` 的子节点、魔骸）真的挂在室内层上——掩码只有在
///    几何真的挂那一层时才起作用，两半都要验。
/// 3. **暖色纪律**：天光必须是冷色（蓝 ≥ 红），暖色仍然只属于灯笼（10 §1）。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class IndoorLightTest : Node3D
{
    private readonly List<string> _failures = new();

    private const string LevelPath = "res://scenes/levels/Dojo.tscn";
    private const string ProfilePath = "res://data/world/atmosphere_rainy_night.tres";

    /// <summary>采样平面：地面略上方（玩家站的那一层）。</summary>
    private const float SampleHeight = 0.1f;

    /// <summary>屋顶高度（道场天花板 y=5.8）。</summary>
    private const float RoofHeight = 5.8f;

    public override async void _Ready()
    {
        AddChild(GD.Load<PackedScene>(LevelPath).Instantiate<Node3D>());
        await WaitPhysicsFrames(10);

        try
        {
            CheckCoverage();
            CheckNoLightLeak();
            CheckWarmColorDiscipline();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex}");
        }

        Report();
    }

    // ── 1. 光覆盖 ──────────────────────────────────────────────

    private void CheckCoverage()
    {
        AtmosphereProfile? profile = GD.Load<AtmosphereProfile>(ProfilePath);
        Check(profile is not null, "读不到氛围档");

        if (profile is null)
            return;

        float lanternReach = profile.LanternRange * 0.8f;
        List<Vector3> lanterns = CollectAnchors(AtmosphereController.LanternGroup);
        lanterns.AddRange(CollectAnchors(AtmosphereController.IndoorLanternGroup));
        List<Vector3> skylights = CollectAnchors(AtmosphereController.SkylightGroup);

        GD.Print($"[室内光] 灯笼锚点 {lanterns.Count} 个（含室内），天光开口 {skylights.Count} 处，" +
                 $"判据：离灯笼 ≤ {lanternReach:0.#}m 或落在洞口投影内");

        Vector3? sunToLight = SunToLightDirection();

        int areas = 0;
        int samples = 0;
        int covered = 0;

        foreach (Node node in GetTree().GetNodesInGroup("combat_area"))
        {
            if (node is not Node3D area)
                continue;

            if (!TryGetFootprint(area, out Vector3 center, out Vector2 half))
                continue;

            areas++;

            foreach (Vector3 point in SamplePoints(center, half))
            {
                samples++;

                float nearest = float.MaxValue;

                foreach (Vector3 lantern in lanterns)
                    nearest = Mathf.Min(nearest, lantern.DistanceTo(point));

                if (nearest <= lanternReach || UnderAnyOpening(point, skylights, sunToLight, out _))
                {
                    covered++;
                    continue;
                }

                Check(false, $"战斗区 {area.Name} 的采样点 {point} 没有光：" +
                             $"最近的灯笼在 {nearest:0.#}m（判据 {lanternReach:0.#}m），也不在天光开口下");
            }
        }

        Check(areas > 0, "场景里一个 combat_area 都没有，这条验收等于没跑");
        Check(samples > 0, "一个采样点都没取到（战斗区没有 CollisionShape3D/BoxShape3D？）");

        GD.Print($"[室内光] 光覆盖：{areas} 个战斗区 / {samples} 个采样点，{covered} 个有光");
    }

    /// <summary>四角 ＋ 中心（卡片指定的五个采样点）。</summary>
    private static IEnumerable<Vector3> SamplePoints(Vector3 center, Vector2 half)
    {
        yield return new Vector3(center.X - half.X, SampleHeight, center.Z - half.Y);
        yield return new Vector3(center.X + half.X, SampleHeight, center.Z - half.Y);
        yield return new Vector3(center.X - half.X, SampleHeight, center.Z + half.Y);
        yield return new Vector3(center.X + half.X, SampleHeight, center.Z + half.Y);
        yield return new Vector3(center.X, SampleHeight, center.Z);
    }

    private List<Vector3> CollectAnchors(string group)
    {
        var found = new List<Vector3>();

        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is Node3D anchor)
                found.Add(anchor.GlobalPosition);
        }

        return found;
    }

    private static bool TryGetFootprint(Node3D area, out Vector3 center, out Vector2 half)
    {
        center = area.GlobalPosition;
        half = Vector2.Zero;

        foreach (Node child in area.GetChildren())
        {
            if (child is not CollisionShape3D shape || shape.Shape is not BoxShape3D box)
                continue;

            Vector3 size = box.Size;
            half = new Vector2(size.X / 2f, size.Z / 2f);
            center = shape.GlobalPosition;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从采样点沿**真实太阳方向**反推到屋顶，看落点是否在某个洞口矩形内。
    /// 这就是"天光真的照到这里"的几何判据。
    /// </summary>
    private bool UnderAnyOpening(Vector3 point, List<Vector3> openings,
        Vector3? sunToLight, out string hit)
    {
        hit = "";

        if (sunToLight is not { } toLight)
            return false;

        // 只处理"太阳在上方"的情况：往上走才能碰到屋顶
        if (toLight.Y <= 0.01f)
            return false;

        float t = (RoofHeight - point.Y) / toLight.Y;

        if (t <= 0f)
            return false;

        Vector3 onRoof = point + toLight * t;

        foreach (Vector3 opening in openings)
        {
            if (Mathf.Abs(onRoof.X - opening.X) <= OpeningHalfSize && Mathf.Abs(onRoof.Z - opening.Z) <= OpeningHalfSize)
            {
                hit = $"{opening}";
                return true;
            }
        }

        return false;
    }

    /// <summary>洞口半边长（与 Dojo.tscn 里的 SkylightHole 尺寸一致：3.2×3.2）。</summary>
    private const float OpeningHalfSize = 1.6f;

    /// <summary>太阳指向场景的方向取反 = 采样点朝太阳的方向（Godot 里光沿自己的 -Z 前进）。</summary>
    private Vector3? SunToLightDirection()
    {
        foreach (Node node in GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false))
        {
            if (node is DirectionalLight3D sun)
                return sun.GlobalTransform.Basis.Z.Normalized();
        }

        return null;
    }

    // ── 2. 不漏光 ──────────────────────────────────────────────

    private void CheckNoLightLeak()
    {
        AtmosphereProfile? profile = GD.Load<AtmosphereProfile>(ProfilePath);

        if (profile is null)
            return;

        int interiorLayer = profile.InteriorLayer;
        var controller = FindController(this);

        Check(controller is not null, "场景里没有 AtmosphereController，氛围根本没建");

        if (controller is null)
            return;

        int outdoor = 0;
        int indoor = 0;

        foreach (Node node in GetTree().Root.FindChildren("*", "OmniLight3D", true, false))
        {
            if (node is not OmniLight3D light)
                continue;

            bool isLantern = light.Name.ToString().Contains("LanternLight");
            bool isIndoorLantern = light.Name.ToString().Contains("IndoorLanternLight");

            if (!isLantern && !isIndoorLantern)
                continue;

            uint mask = light.LightCullMask;

            if (isIndoorLantern)
            {
                indoor++;

                Check((mask & (uint)interiorLayer) != 0,
                    $"室内灯笼的 cull mask 不含室内层（{mask}）——它照不进室内几何");
                continue;
            }

            outdoor++;

            Check((mask & (uint)interiorLayer) == 0,
                $"室外灯笼的 cull mask 含室内层（{mask}）——它会把屋里照成假亮（T40 卡片点名的穿墙问题）");
        }

        Check(outdoor > 0, "室外灯笼一盏都没建，'不漏光'这条验不到");
        Check(indoor > 0, "室内灯笼一盏都没建，屋里还是没灯");

        // 掩码只有在几何真的挂那一层时才起作用——两半都要验。
        // `Porch`（廊下）**故意留在室外层**：它在屋外，本就该由门口那两盏室外灯笼照。
        int onInteriorLayer = 0;
        int interiorNodes = 0;

        foreach (Node node in GetTree().Root.FindChildren("*", "CSGBox3D", true, false))
        {
            if (node is not CsgBox3D box || box.Operation == CsgShape3D.OperationEnum.Subtraction)
                continue;

            string name = box.Name.ToString();

            if (name.StartsWith("SkylightHole") || name == "Porch")
                continue;

            interiorNodes++;

            if (box.Layers == (uint)interiorLayer)
                onInteriorLayer++;
        }

        Check(interiorNodes > 0, "一个室内 CSG 几何都没扫到");
        Check(onInteriorLayer == interiorNodes,
            $"只有 {onInteriorLayer}/{interiorNodes} 个室内几何挂在室内层上——" +
            "剩下那些仍会被室外灯笼照到（掩码挡不住没挂层的物体）");

        GD.Print($"[室内光] 不漏光：室外灯笼 {outdoor} 盏（mask 不含室内层）/ " +
                 $"室内灯笼 {indoor} 盏（含室内层）/ 室内几何 {onInteriorLayer} 块挂层正确 ✓");

        Check(controller.SkylightCount > 0, "天光一处都没建——屋顶开了洞却没有光下来");
        GD.Print($"[室内光] 天光开口已点亮 {controller.SkylightCount} 处" +
                 $"（跳过 {controller.SkylightsSkipped} 处，上限 {profile.MaxSkylights}）");
    }

    private static AtmosphereController? FindController(Node root)
    {
        foreach (Node node in root.GetTree().Root.FindChildren("*", "", true, false))
        {
            if (node is AtmosphereController controller)
                return controller;
        }

        return null;
    }

    // ── 3. 暖色纪律 ────────────────────────────────────────────

    private void CheckWarmColorDiscipline()
    {
        AtmosphereProfile? profile = GD.Load<AtmosphereProfile>(ProfilePath);

        if (profile is null)
            return;

        Color sky = profile.SkylightColor;
        Check(sky.B >= sky.R,
            $"天光是暖的（R {sky.R:0.##} > B {sky.B:0.##}）——10 §1 规定暖色只能来自灯笼");

        Color ambient = profile.AmbientColor;
        Check(ambient.B >= ambient.R, "环境光偏暖，整套美术的底色应该是低饱和青灰");

        GD.Print($"[室内光] 暖色纪律：天光 (R {sky.R:0.##} / B {sky.B:0.##}) 冷色，" +
                 $"环境光 (R {ambient.R:0.##} / B {ambient.B:0.##}) 冷色 ✓——暖色只来自灯笼");
    }

    // ── 杂项 ───────────────────────────────────────────────────

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
            GD.PrintErr($"[室内光] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[室内光] ✓ 通过（战斗区四角+中心都有光 / 不漏光 / 暖色纪律）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
