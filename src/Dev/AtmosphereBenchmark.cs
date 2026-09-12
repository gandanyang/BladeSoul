using Godot;
using Oniblade.Enemies;
using Oniblade.World;

namespace Oniblade.Dev;

/// <summary>
/// 氛围帧率基准（T34 验收 2 / 4）。
///
/// **这个场景要在有显示的环境里跑**（编辑器里按 F6，或直接
/// <c>godot --path . res://scenes/tests/AtmosphereBenchmark.tscn</c>，不要加 --headless）。
///
/// 原因：无头模式用的是 dummy renderer，**根本不渲染**——
/// 那里跑出来的帧率是"逻辑帧率"，跟雾/雨/灯笼的开销毫无关系。
/// 所以无头下它只打印一行警告并正常退出，数字不当证据。
///
/// 它做的事：摆 8 个敌人 + 让摄像机同时看见它们，然后在三个降级等级下各测一段，
/// 输出一张表。验收要的是"降级顺序有**实际帧率收益**（两组数字）"，
/// 这张表就是要给的那两组数字。
/// </summary>
public partial class AtmosphereBenchmark : Node3D
{
    [Export] public string LevelPath { get; set; } = "res://scenes/levels/L01_Gifu.tscn";

    /// <summary>同屏敌人数量（卡片要求 8）。</summary>
    [Export] public int EnemyCount { get; set; } = 8;

    /// <summary>热身帧数（着色器编译、显存分配都在这段里发生，不能算进成绩）。</summary>
    [Export] public int WarmupFrames { get; set; } = 90;

    /// <summary>每档测量帧数。</summary>
    [Export] public int MeasureFrames { get; set; } = 180;

    /// <summary>目标帧率（07 §7：60fps 是硬指标）。</summary>
    [Export] public double TargetFps { get; set; } = 60.0;

    private Node3D _level = null!;
    private AtmosphereController _atmosphere = null!;
    private Camera3D _camera = null!;

    public override async void _Ready()
    {
        _level = GD.Load<PackedScene>(LevelPath).Instantiate<Node3D>();
        AddChild(_level);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        _atmosphere = FindController(_level) ?? null!;

        SpawnEnemies();
        PlaceCamera();

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        bool headless = DisplayServer.GetName() == "headless";

        if (headless)
        {
            GD.Print("[基准] 无头模式：**帧率数字不可信**（dummy renderer 不渲染）。");
            GD.Print("[基准] 请在编辑器里跑这个场景（F6），或去掉 --headless 再跑一次。");
            GetTree().Quit(0);
            return;
        }

        if (QualityDirector.Instance is not { } quality)
        {
            GD.PrintErr("[基准] ✗ 关卡里没有 QualityDirector");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"[基准] 关卡 {System.IO.Path.GetFileName(LevelPath)}，同屏敌人 {EnemyCount}，" +
                 $"每档测 {MeasureFrames} 帧（热身 {WarmupFrames} 帧）");

        double level0 = await Measure(quality, 0);
        double level1 = await Measure(quality, 1);
        double level2 = await Measure(quality, 2);

        GD.Print("[基准] ── 结果 ─────────────────────────────");
        GD.Print($"[基准] 等级 0（全开）        ：{level0:0.0} fps");
        GD.Print($"[基准] 等级 1（砍体积雾）    ：{level1:0.0} fps   （收益 {level1 - level0:+0.0}）");
        GD.Print($"[基准] 等级 2（再砍粒子）    ：{level2:0.0} fps   （再收益 {level2 - level1:+0.0}）");
        GD.Print($"[基准] 目标 ≥{TargetFps:0} fps");

        Report(level0, level1, level2);
    }

    private async System.Threading.Tasks.Task<double> Measure(QualityDirector quality, int level)
    {
        quality.ForceLevel(level);
        _atmosphere.RefreshDegradation();

        for (int i = 0; i < WarmupFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        ulong start = Time.GetTicksMsec();

        for (int i = 0; i < MeasureFrames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        double seconds = (Time.GetTicksMsec() - start) / 1000.0;

        return seconds > 0.0 ? MeasureFrames / seconds : 0.0;
    }

    private void Report(double level0, double level1, double level2)
    {
        var failures = new System.Collections.Generic.List<string>();

        if (level0 < TargetFps)
            failures.Add($"等级 0 只有 {level0:0.0} fps，低于 {TargetFps:0}（同屏 {EnemyCount} 个敌人）");

        if (level1 <= level0)
            failures.Add($"砍体积雾没有帧率收益（{level0:0.0} → {level1:0.0}）：降级顺序第一档是白砍的");

        if (level2 <= level1)
            failures.Add($"砍粒子没有帧率收益（{level1:0.0} → {level2:0.0}）");

        foreach (string failure in failures)
            GD.PrintErr($"[基准] ✗ {failure}");

        if (failures.Count == 0)
            GD.Print("[基准] ✓ 通过（60fps 达标 + 两档降级都有实际收益）");

        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    private void SpawnEnemies()
    {
        // 沿街一字排开：既"同屏"，又都在 2.2m 攻击距离之外，
        // 这样测量期间不会有战斗结算来干扰帧率。
        for (int i = 0; i < EnemyCount; i++)
        {
            AttackingDummy enemy = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
                .Instantiate<AttackingDummy>();

            enemy.Position = new Vector3(6f + i * 2f, 0.1f, 0f);
            _level.AddChild(enemy);
        }
    }

    private void PlaceCamera()
    {
        _camera = new Camera3D { Name = "BenchmarkCamera" };
        AddChild(_camera);

        // 后退到能一次看见全部敌人的位置
        float centerX = 6f + (EnemyCount - 1) * 1f;

        _camera.GlobalPosition = new Vector3(centerX, 7f, -16f);
        _camera.LookAt(new Vector3(centerX, 1f, 0f));
        _camera.MakeCurrent();
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
}
