using System.Collections.Generic;
using Godot;
using Oniblade.World;

namespace Oniblade.Dev;

/// <summary>
/// 氛围与灯光验证（T34）：
///     godot --headless --path . res://scenes/tests/Atmosphere.tscn
///
/// 卡片验收 3（截图）与 2（帧率实测）我出不了——无头没有渲染输出，帧率数字不可信。
/// 但卡片里**能自动化的部分**在这里全部断言了：
///
/// 1. 档位真的落到了 WorldEnvironment 上（雾开、环境光**偏冷**、饱和度被压低）；
/// 2. **雨跟着摄像机**（移动摄像机 → 雨盒跟着移动），而不是只在固定区域下；
/// 3. **灯笼是画面里唯一的暖色**，且数量受上限约束；
/// 4. 降级顺序是 **① 砍体积雾 → ② 砍粒子**，而且是同一个旋钮（QualityDirector）在管；
/// 5. 读招保底：雾在战斗区对角线距离上的透光率 ≥ 阈值（**这是代理量**，见下）。
///
/// ⚠️ 第 5 条是**代理量**：真正的"雾里读不读得出招"只能靠眼睛。
/// 这里能量化的是"雾有多浓"和"环境光够不够冷"，而魔骸伤口红光的可读性
/// 要等 T27 的模型到位后才能一起看。这一点我写进报告了，不假装测到了。
///
/// 退出码 0 = 全过，1 = 有错。
/// </summary>
public partial class AtmosphereTest : Node3D
{
    [Export] public string LevelPath { get; set; } = "res://scenes/levels/L01_Gifu.tscn";

    /// <summary>判"暖色"的阈值：红比蓝高这么多就算暖。白色（R==B）不算。</summary>
    private const float WarmThreshold = 0.1f;

    private readonly List<string> _failures = new();

    private Node3D _level = null!;
    private AtmosphereController _atmosphere = null!;
    private QualityDirector _quality = null!;
    private Camera3D _camera = null!;

    public override async void _Ready()
    {
        _level = GD.Load<PackedScene>(LevelPath).Instantiate<Node3D>();
        AddChild(_level);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        if (FindController(_level) is not { } controller)
        {
            GD.PrintErr("[氛围] ✗ 关卡里没有 AtmosphereController");
            GetTree().Quit(1);
            return;
        }

        _atmosphere = controller;

        // 先钉住等级 0 再做检查：headless 下 FPS 读数很低，QualityDirector 会
        // **每帧升一级**，两帧之后雾就已经被砍了——那样"档位落地"这条会莫名其妙地失败。
        // （T28 的第一版就是这个坑。）
        QualityDirector.Instance?.ForceLevel(0);
        _atmosphere.RefreshDegradation();

        try
        {
            CheckProfileApplied();
            await CheckRainFollowsCamera();
            CheckLanternsAreTheOnlyWarmth();
            CheckLanternCap();
            CheckDegradationOrder();
            CheckReadabilityProxy();
        }
        catch (System.Exception ex)
        {
            Check(false, $"检查过程中抛异常：{ex.Message}");
        }

        Report();
    }

    private static AtmosphereController? FindController(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is AtmosphereController controller)
                return controller;

            if (FindController(child) is { } nested)
                return nested;
        }

        return null;
    }

    // ── 1. 档位落到 WorldEnvironment 上了吗 ────────────────────

    private void CheckProfileApplied()
    {
        AtmosphereProfile? profile = _atmosphere.Profile;
        Check(profile is not null, "AtmosphereController 没有配档位");

        Environment? env = _atmosphere.AppliedEnvironment;
        Check(env is not null, "没有拿到 WorldEnvironment 的 Environment");

        if (profile is null || env is null)
            return;

        Check(env.VolumetricFogEnabled, "体积雾没开：雨夜氛围与遮挡远景都指望它");
        Check(Mathf.IsEqualApprox((float)env.VolumetricFogDensity, profile.FogDensity),
            $"雾密度没落到环境上：{env.VolumetricFogDensity} vs 档位 {profile.FogDensity}");

        // 颜色纪律：环境光必须偏冷（暖色只留给灯笼）。
        Color ambient = env.AmbientLightColor;
        bool cool = ambient.B > ambient.R;

        Check(cool, $"环境光不是冷色（R={ambient.R:0.##} B={ambient.B:0.##}）：暖色只允许出现在灯笼上");
        Check(!IsWarm(ambient), "环境光是暖色：这会让整个画面失去「雨夜」的青灰底，也会抢掉敌人红光的可读性");

        Check(env.AdjustmentEnabled, "没开色彩校正，压不住饱和度");
        Check(Mathf.IsEqualApprox((float)env.AdjustmentSaturation, profile.Saturation),
            "饱和度没落到环境上");

        GD.Print($"[氛围] 档位 {profile.DisplayName}：雾 {env.VolumetricFogDensity:0.###} / " +
                 $"环境光 ({ambient.R:0.##}, {ambient.G:0.##}, {ambient.B:0.##}) / 饱和度 {env.AdjustmentSaturation:0.##}");
    }

    // ── 2. 雨跟着摄像机 ───────────────────────────────────────

    private async System.Threading.Tasks.Task CheckRainFollowsCamera()
    {
        _camera = new Camera3D { Name = "TestCamera" };
        AddChild(_camera);
        _camera.GlobalPosition = new Vector3(10f, 3f, 0f);
        _camera.MakeCurrent();

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Vector3 first = _atmosphere.RainAnchor;

        Check(first.DistanceTo(new Vector3(10f, 7f, 0f)) < 0.5f,
            $"雨盒没跟到摄像机头上：期望约 (10, 7, 0)，实际 ({first.X:0.#}, {first.Y:0.#}, {first.Z:0.#})");

        // 把摄像机挪走 → 雨必须跟着走（固定区域的雨在玩家走开后就会露馅）
        _camera.GlobalPosition = new Vector3(40f, 3f, 6f);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Vector3 second = _atmosphere.RainAnchor;

        Check(second.DistanceTo(first) > 25f,
            $"摄像机移动了 30m，雨盒只跟了 {second.DistanceTo(first):0.#}m：雨没有跟着摄像机");

        GD.Print($"[氛围] 雨跟随摄像机：(10,7,0) → ({second.X:0.#},{second.Y:0.#},{second.Z:0.#})");

        _camera.QueueFree();
    }

    // ── 3. 灯笼是唯一的暖色 ───────────────────────────────────

    private void CheckLanternsAreTheOnlyWarmth()
    {
        AtmosphereProfile? profile = _atmosphere.Profile;
        if (profile is null)
            return;

        int warmLights = 0;
        int lanternLights = 0;

        foreach (Node node in _level.GetChildren())
            CountLights(node, profile, ref warmLights, ref lanternLights);

        Check(_atmosphere.LanternCount > 0, "一盏灯笼都没点亮");
        Check(warmLights == lanternLights,
            $"画面上有 {warmLights} 个暖色光源，但只有 {lanternLights} 个来自灯笼：" +
            "暖色必须**只能**来自灯笼（10 §3.1）");

        GD.Print($"[氛围] 灯笼 {_atmosphere.LanternCount} 盏，暖色光源 {warmLights} 个 —— 全部来自灯笼 ✓");
    }

    private static void CountLights(Node node, AtmosphereProfile profile,
        ref int warmLights, ref int lanternLights)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Light3D light && IsWarm(light.LightColor))
            {
                warmLights++;

                if (light.Name == "LanternLight")
                    lanternLights++;
            }

            CountLights(child, profile, ref warmLights, ref lanternLights);
        }
    }

    private static bool IsWarm(Color color) => color.R - color.B > WarmThreshold;

    // ── 4. 灯笼数量上限 ───────────────────────────────────────

    private void CheckLanternCap()
    {
        AtmosphereProfile? profile = _atmosphere.Profile;
        if (profile is null)
            return;

        // 往场上塞一堆灯笼，重建之后必须被上限挡住。
        var extra = new Node3D { Name = "ExtraLanterns" };
        AddChild(extra);

        for (int i = 0; i < 8; i++)
        {
            extra.AddChild(new Marker3D
            {
                Name = $"ExtraLantern{i}",
                Position = new Vector3(i * 2f, 3f, 0f),
            });
            extra.GetChild(i).AddToGroup(AtmosphereController.LanternGroup);
        }

        _atmosphere.RebuildLanterns();

        Check(_atmosphere.LanternCount == profile.MaxLanterns,
            $"灯笼上限没生效：点亮 {_atmosphere.LanternCount} 盏，上限是 {profile.MaxLanterns}");
        Check(_atmosphere.LanternsSkipped > 0, "超出的灯笼没有被拒绝");

        GD.Print($"[氛围] 灯笼上限：点亮 {_atmosphere.LanternCount} / 拒绝 {_atmosphere.LanternsSkipped}" +
                 $"（上限 {profile.MaxLanterns}，暖色＝注意力）");

        extra.QueueFree();
    }

    // ── 5. 降级顺序：① 雾 → ② 粒子 ──────────────────────────

    private void CheckDegradationOrder()
    {
        if (QualityDirector.Instance is not { } quality)
        {
            Check(false, "关卡里没有 QualityDirector：降级没有单一旋钮");
            return;
        }

        _quality = quality;

        // 0：全开
        _quality.ForceLevel(0);
        _atmosphere.RefreshDegradation();
        Check(_atmosphere.FogActive, "等级 0 雾没开");
        Check(_atmosphere.RainActive, "等级 0 雨没开");

        // 1：砍雾，但粒子还在
        _quality.ForceLevel(1);
        _atmosphere.RefreshDegradation();
        Check(!_atmosphere.FogActive, "等级 1 没有砍掉体积雾（07 §7 要求它排第一）");
        Check(_atmosphere.RainActive, "等级 1 把粒子也砍了：顺序错了，应该先砍雾");

        // 2：再砍粒子
        _quality.ForceLevel(2);
        _atmosphere.RefreshDegradation();
        Check(!_atmosphere.RainActive, "等级 2 没有砍掉粒子");

        GD.Print("[氛围] 降级顺序：等级1 砍雾（粒子保留）→ 等级2 砍粒子 ✓");

        _quality.ForceLevel(0);
        _atmosphere.RefreshDegradation();
        Check(_atmosphere.FogActive && _atmosphere.RainActive, "回到等级 0 之后没有恢复");
    }

    // ── 6. 读招保底（代理量）──────────────────────────────────

    private void CheckReadabilityProxy()
    {
        AtmosphereProfile? profile = _atmosphere.Profile;
        if (profile is null)
            return;

        float transmittance = profile.TransmittanceAt(profile.CombatReadDistance);

        GD.Print($"[氛围] 读招保底（代理量）：战斗区对角线 {profile.CombatReadDistance:0.#}m 处" +
                 $"透光率 {transmittance:P0}（下限 {profile.MinTransmittanceAtCombatDistance:P0}）");

        Check(transmittance >= profile.MinTransmittanceAtCombatDistance,
            $"雾太浓：{profile.CombatReadDistance:0.#}m 处只剩 {transmittance:P0} 可见，" +
            "敌人会变成「凭空出现」而不是「从雾里走出来」");

        Check(profile.ReadabilityGlowMinEnergy > 0f,
            "没有声明敌人发光的能量下限：雾里读招靠的就是伤口/眼窝的红光");

        Color ambient = profile.AmbientColor;
        Color glow = new(0.784f, 0.196f, 0.227f);   // 10 §1 的魔骸红光 #C8323A

        Check(glow.R - ambient.R > 0.25f,
            $"敌人红光的红分量只比环境光高 {glow.R - ambient.R:0.##}：雾里读不出轮廓");

        GD.Print("[氛围] ⚠️ 「雾里读不读得出招」最终只能靠眼睛——这一步是代理量，" +
                 "真正的验证要等 T27 的敌人模型到位后截图");
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
            _failures.Add(message);
    }

    private void Report()
    {
        foreach (string failure in _failures)
            GD.PrintErr($"[氛围] ✗ {failure}");

        if (_failures.Count == 0)
            GD.Print("[氛围] ✓ 通过（档位落地 / 雨跟摄像机 / 灯笼唯一暖色 / 降级顺序 / 读招代理量）");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
