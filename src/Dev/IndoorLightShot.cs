using Godot;
using Oniblade.UI;

namespace Oniblade.Dev;

/// <summary>
/// T40 的修前 / 修后对照图。**必须带窗口跑**（无头是 dummy renderer，抓到空帧）：
///
///     godot --path . res://scenes/tests/IndoorLightShot.tscn -- --out=res://assets/references/t40_before.png
///
/// 机位固定在**房间最深处**（道场北端，`Makugai` 站的那片全黑区），
/// 所以两张图的差别只可能来自照明本身。
///
/// 先把 HUD 藏掉：这张图是用来判断"亮不亮"的，血量条和提示会干扰。
/// </summary>
public partial class IndoorLightShot : Node3D
{
    [Export] public int Width { get; set; } = 1280;
    [Export] public int Height { get; set; } = 720;
    [Export] public int WarmupFrames { get; set; } = 20;

    /// <summary>房间最深处的站位（道场 24×16，Makugai 在 (3.2, 0.87, -6.2)）。</summary>
    [Export] public Vector3 CameraStation { get; set; } = new(-1.5f, 0.2f, -6.5f);

    /// <summary>朝东看（Makugai 在 +X 侧），这样最深处与那个新模型同时在画面里。</summary>
    [Export] public float StationYawDegrees { get; set; } = -90f;

    [Export] public string OutputPath { get; set; } = "res://assets/references/t40_after.png";

    private int _frames;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "T40 · 室内照明";

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out="))
                OutputPath = arg["--out=".Length..];
        }

        var level = GD.Load<PackedScene>("res://scenes/levels/Dojo.tscn").Instantiate<Node3D>();
        level.Name = "Dojo";
        AddChild(level);

        // 把玩家挪到最深处的机位（关卡里的默认出生点在门口那头）
        foreach (Node node in GetTree().GetNodesInGroup("player"))
        {
            if (node is Node3D player)
            {
                player.Position = CameraStation;
                player.RotationDegrees = new Vector3(0f, StationYawDegrees, 0f);
            }
        }

        if (Hud.Instance is { } hud)
            hud.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (++_frames < WarmupFrames)
            return;

        Image image = GetViewport().GetTexture().GetImage();

        if (image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            GD.PrintErr("[室内光截图] 抓到空帧——无头模式是 dummy renderer，请带窗口跑");
            GetTree().Quit(1);
            return;
        }

        Error error = image.SavePng(OutputPath);

        if (error == Error.Ok)
            GD.Print($"[室内光截图] 已出图：{OutputPath}（{image.GetWidth()}×{image.GetHeight()}）");
        else
            GD.PrintErr($"[室内光截图] 写不出去：{OutputPath}（{error}）");

        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
