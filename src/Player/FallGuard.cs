using Godot;
using Oniblade.World;

namespace Oniblade.Player;

/// <summary>
/// 掉落保护（T45）：玩家掉出世界时 **淡出 → 回到安全点 → 淡入**。
///
/// 为什么挂在**玩家身上**而不是关卡里：
/// 1. 安全点就是玩家的出生位置，跟着玩家走最自然；
/// 2. 这样可以**完全不碰关卡场景**——道场正被另一张卡（T40）改着，
///    而 `AGENTS.md` §5 明写了并发写同一个文件会出重复定义。
///
/// 它只做三件事：喂位置给纯逻辑、播淡入淡出、把玩家挪回去。
/// **判定本身在 <see cref="FallRecovery"/> 里**，那部分可以脱离引擎单测。
/// </summary>
public partial class FallGuard : Node3D
{
    /// <summary>参数资源（必填；留空就用代码里的默认值，那种情况应该被视为配置漏了）。</summary>
    [Export] public FallRecoveryProfile? Profile { get; set; }

    /// <summary>安全点。默认取玩家进场时的位置。</summary>
    [Export] public bool UseSpawnAsSafePoint { get; set; } = true;

    /// <summary>显式指定安全点（<see cref="UseSpawnAsSafePoint"/> 为 false 时生效）。</summary>
    [Export] public Vector3 SafePoint { get; set; } = Vector3.Zero;

    /// <summary>一共被送回来过几次（体检与调试用）。</summary>
    public int RecoveryCount { get; private set; }

    /// <summary>正在播淡入淡出（体检要等它结束）。</summary>
    public bool IsRecovering => _fadeTotal > 0;

    private readonly FallRecovery _logic = new();
    private CharacterBody3D? _body;
    private CanvasLayer? _fadeLayer;
    private ColorRect? _fadeRect;
    private int _fadeFrame;
    private int _fadeTotal;
    private bool _fadingOut;

    public override void _Ready()
    {
        _body = GetParent() as CharacterBody3D;

        if (_body is null)
        {
            GD.PrintErr("[掉落] FallGuard 的父节点不是 CharacterBody3D，保护没有生效");
            return;
        }

        if (Profile is not null)
        {
            _logic.ThresholdY = Profile.ThresholdY;
            _logic.ConfirmFrames = Profile.ConfirmFrames;
            _logic.MaxHorizontalDistance = Profile.MaxHorizontalDistance;
        }
        else
        {
            GD.PrintErr("[掉落] 没有配 FallRecoveryProfile，正在用代码里的默认值");
        }

        if (!UseSpawnAsSafePoint)
            return;

        SafePoint = _body.GlobalPosition;
        GD.Print($"[掉落] 保护就位：Y < {_logic.ThresholdY:F1} 连续 {_logic.ConfirmFrames} 帧即重置，"
                 + $"安全点 {Fmt(SafePoint)}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body is null)
            return;

        // 演出期间不再判定：否则淡出还没走完就又触发一次。
        if (_fadeTotal > 0)
        {
            StepFade();
            return;
        }

        Vector3 p = _body.GlobalPosition;
        if (!_logic.Update(p.X, p.Y, p.Z))
            return;

        GD.Print($"[掉落] 越界确认（连续 {_logic.OutFrames} 帧，Y = {p.Y:F2}）→ 淡出后回安全点");
        BeginFade();
    }

    private void BeginFade()
    {
        _fadeLayer?.QueueFree();

        _fadeRect = new ColorRect
        {
            Color = new Color(Profile?.FadeColor ?? new Color(0f, 0f, 0f, 1f), 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _fadeLayer = new CanvasLayer { Name = "FallFade", Layer = 95 };
        _fadeLayer.AddChild(_fadeRect);
        AddChild(_fadeLayer);

        _fadingOut = true;
        _fadeFrame = 0;
        _fadeTotal = Mathf.Max(1, Profile?.FadeOutFrames ?? 15);
    }

    private void StepFade()
    {
        _fadeFrame++;
        float t = Mathf.Clamp((float)_fadeFrame / _fadeTotal, 0f, 1f);

        if (_fadingOut)
        {
            ApplyAlpha(t);

            if (t < 1f)
                return;

            // 全黑的这一帧才动位置——玩家看不到瞬移，只看到"黑了一下然后站在别处"。
            TeleportToSafePoint();
            _fadingOut = false;
            _fadeFrame = 0;
            _fadeTotal = Mathf.Max(1, Profile?.FadeInFrames ?? 24);
            return;
        }

        ApplyAlpha(1f - t);

        if (t < 1f)
            return;

        _fadeTotal = 0;
        _fadeLayer?.QueueFree();
        _fadeLayer = null;
        _fadeRect = null;
    }

    private void ApplyAlpha(float alpha)
    {
        if (_fadeRect is null)
            return;

        Color c = _fadeRect.Color;
        _fadeRect.Color = new Color(c.R, c.G, c.B, Mathf.Clamp(alpha, 0f, 1f));
    }

    private void TeleportToSafePoint()
    {
        if (_body is null)
            return;

        _body.GlobalPosition = SafePoint;

        // 不清速度的话，落地后会带着下坠速度直接穿过地板（实测出来的）。
        _body.Velocity = Vector3.Zero;

        _logic.Clear();
        RecoveryCount++;
        GD.Print($"[掉落] 已回到安全点 {Fmt(SafePoint)}（第 {RecoveryCount} 次）");
    }

    private static string Fmt(Vector3 v) => $"({v.X:F1}, {v.Y:F2}, {v.Z:F1})";
}
