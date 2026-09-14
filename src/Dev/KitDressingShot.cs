using Godot;

namespace Oniblade.Dev;

/// <summary>
/// T33 验收：**同机位**的「灰盒 vs 贴模块」对比图。
///
/// 为什么必须"同机位"：T33 的验收标准是"贴模块后空间读起来像城下町了，
/// 而**布局一个都没变**"。两张图如果机位不同，观感差异里就混进了机位差异，
/// 那句话就没法验证了。
///
/// 做法：**在同一次运行里开关 `Dressing` 节点的可见性**——
/// 相机、光照、时间、随机种子全都一模一样，唯一变的就是那几个模块。
/// 这比"跑两次、人工对齐机位"可靠得多（也没有对齐这件事）。
///
///     godot --path . res://scenes/tests/KitDressingDojo.tscn
///     godot --path . res://scenes/tests/KitDressingGifu.tscn
///
/// ⚠️ 必须**带窗口**跑：无头模式下 `GetViewport().GetTexture()` 拿不到内容，
///    存出来是空图（`GameView` 那边已经记过这条）。
/// </summary>
public partial class KitDressingShot : Node3D
{
    [Export] public string LevelPath { get; set; } = "res://scenes/levels/Dojo.tscn";
    [Export] public string OutBefore { get; set; } = "res://assets/references/kit_before.png";
    [Export] public string OutAfter { get; set; } = "res://assets/references/kit_after.png";

    [Export] public Vector3 CameraPosition { get; set; } = new(0f, 2.6f, 6.0f);
    [Export] public Vector3 CameraTarget { get; set; } = new(0f, 2.0f, -7f);
    [Export] public float CameraFov { get; set; } = 68f;

    /// <summary>等几帧再抓——关卡里的相机/光照/氛围要先生成完。</summary>
    [Export] public int WarmupFrames { get; set; } = 70;

    /// <summary>切换可见性之后等几帧，让渲染真的换过来。</summary>
    [Export] public int SettleFrames { get; set; } = 6;

    private Camera3D? _cam;
    private Node3D? _dressing;
    private int _frames;
    private int _stage;

    public override void _Ready()
    {
        // 无头模式必须**当场**拒掉。dummy 渲染器的 viewport 纹理是 null，
        // `GetImage()` 会直接抛（不是返回 0×0），下面的空帧判断根本轮不到——
        // 实测会每帧抛一次、几百兆日志刷屏，而且进程永不退出。
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("[铺装对比] 无头模式抓不到画面（dummy 渲染器）。请带窗口跑：" +
                        $"godot --path . res://scenes/tests/{GetSceneFilePath()}");
            GetTree().Quit(3);
            return;
        }

        GetWindow().Size = new Vector2I(1152, 648);
        GetWindow().Title = $"T33 铺装对比 {LevelPath}";

        PackedScene? level = GD.Load<PackedScene>(LevelPath);
        if (level is null)
        {
            GD.PrintErr($"[铺装对比] 关卡加载失败：{LevelPath}");
            GetTree().Quit(1);
            return;
        }

        AddChild(level.Instantiate());

        _cam = new Camera3D
        {
            Name = "KitCompareCamera",
            Fov = CameraFov,
            Near = 0.05f,
            Far = 400f,
            Current = true,
        };

        AddChild(_cam);
        _cam.GlobalPosition = CameraPosition;
        _cam.LookAt(CameraTarget, Vector3.Up);

        if (HideHud)
        {
            foreach (Node child in GetTree().Root.GetChildren())
            {
                if (child is CanvasLayer layer)
                    layer.Visible = false;
            }
        }

        GD.Print($"[铺装对比] {LevelPath} 已实例化；相机 @ {CameraPosition} 看向 {CameraTarget}");
    }

    [Export] public bool HideHud { get; set; } = true;

    public override void _Process(double delta)
    {
        _frames++;

        // 关卡自己的 SpringArm 相机会抢 Current，每帧抢回来
        if (_cam is not null && GetViewport().GetCamera3D() != _cam)
            _cam.MakeCurrent();

        if (_frames < WarmupFrames)
            return;

        _dressing ??= FindByName(GetTree().Root, "Dressing") as Node3D;

        switch (_stage)
        {
            case 0:
                if (_dressing is null)
                {
                    // 没有 Dressing 说明这一关还没铺装——那"对比"就无从谈起，
                    // 但要如实报出来，不能出一张假图充数。
                    GD.PrintErr("[铺装对比] 场景里没有 Dressing 节点（没铺装？）");
                    GetTree().Quit(2);
                    return;
                }

                _dressing.Visible = false;
                _stage = 1;
                _frames = 0;
                break;

            case 1:
                if (_frames < SettleFrames)
                    return;
                Shoot(OutBefore, "灰盒（Dressing 隐藏）");
                _dressing!.Visible = true;
                _stage = 2;
                _frames = 0;
                break;

            case 2:
                if (_frames < SettleFrames)
                    return;
                Shoot(OutAfter, "贴模块（Dressing 显示）");
                GetTree().Quit(0);
                break;
        }
    }

    private void Shoot(string path, string label)
    {
        Image image;
        try
        {
            image = GetViewport().GetTexture().GetImage();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[铺装对比] 抓帧抛异常（多半是无头/dummy 渲染器）：{e.Message}");
            GetTree().Quit(3);
            return;
        }

        if (image is null || image.GetWidth() == 0)
        {
            GD.PrintErr("[铺装对比] 抓到空帧——必须带窗口跑");
            GetTree().Quit(3);
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[铺装对比] {label} → {path}"
            : $"[铺装对比] {label} 存图失败 {error}");
    }

    private static Node? FindByName(Node root, string name)
    {
        if (root.Name == name)
            return root;
        foreach (Node c in root.GetChildren())
        {
            Node? r = FindByName(c, name);
            if (r is not null)
                return r;
        }

        return null;
    }
}
