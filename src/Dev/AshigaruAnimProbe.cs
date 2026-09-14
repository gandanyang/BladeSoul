using System.Collections.Generic;
using Godot;
using Oniblade.Enemies;

namespace Oniblade.Dev;

/// <summary>
/// 足兵动作体检（T52）：**逐个动作打印它与 idle 的最大骨角差**。
///
/// 判据（照 `PlayerGaps` 的做法）：任何动作若 ≤ 0.1° 就是**没有专属动画**，
/// 必须报错而不是打勾。一个只测单点的检查会让"导出了但没动"看起来完全正常。
///
/// 为什么不用 `quaternion |dot|`：它把 0° 和 180° 看成一样，
/// 而足兵大腿 rest 恰好是 178.5°——用点积会把"腿翻过去"判成"没动"。
/// 这里用**欧拉分量差的最大值**，并把角度差规约到 (-180, 180]。
/// </summary>
public partial class AshigaruAnimProbe : Node
{
    private const string ModelPath = "res://assets/models/ashigaru_rigged.glb";

    private static readonly string[] Tracked =
    {
        "Hip", "Spine01", "Spine02", "Head",
        "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
        "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
    };

    /// <summary>每个动作采样多少帧（覆盖完整动作长度）。</summary>
    [Export] public int SamplesPerAction { get; set; } = 40;

    private readonly Dictionary<string, Quaternion> _idleBaseline = new();
    private Skeleton3D _skel = null!;
    private AshigaruAnimator _anim = null!;

    public override void _Ready()
    {
        var packed = GD.Load<PackedScene>(ModelPath);
        if (packed is null)
        {
            GD.PrintErr($"[足兵动作] ✗ 加载 {ModelPath} 失败");
            GetTree().Quit(1);
            return;
        }

        Node model = packed.Instantiate();
        AddChild(model);

        _skel = FindSkeleton(model) ?? throw new System.InvalidOperationException("没有 Skeleton3D");
        _anim = new AshigaruAnimator((Node3D)model);

        if (!_anim.Valid)
        {
            GD.PrintErr("[足兵动作] ✗ 动画器无效（骨名对不上？）");
            GetTree().Quit(1);
            return;
        }

        GD.Print("[足兵动作] ── 逐动作体检 ──");
        GD.Print($"[足兵动作] 骨架 {_skel.GetBoneCount()} 骨，每个动作采样 {SamplesPerAction} 帧");

        // 基准：idle 的第一帧
        _anim.Reset();
        _anim.Animate(0f, 0f, AshigaruAction.Idle, 0, 0);
        CaptureBaseline();

        int dead = 0;
        var results = new List<(string Name, float MaxDeg, float AtFrame)>();

        foreach (AshigaruAction action in System.Enum.GetValues<AshigaruAction>())
        {
            if (action == AshigaruAction.Idle)
                continue;

            float maxDeg = 0f;
            int atFrame = 0;

            // ★ 移动**不能逐帧 Reset()**：
            //   `_phase` 是动画器自己的走步相位，Reset 会把它清零。
            //   清掉之后每一帧都停在"相位 0"，测出来的是"零相位下腿的角度"（8.9°），
            //   而不是"腿的摆动幅度"。所以移动要**连续推进**，其余动作才逐帧 Reset
            //   （它们的姿势是帧号的纯函数，Reset 无害）。
            bool continuous = action == AshigaruAction.Move;
            if (continuous)
                _anim.Reset();

            for (int f = 0; f < SamplesPerAction; f++)
            {
                if (!continuous)
                    _anim.Reset();

                // Move 要喂速度，否则腿按设计完全不摆（MinWalkSpeed 闸门）
                float speed = continuous ? 1f : 0f;
                _anim.Animate(1f / 60f, speed, action, f, SamplesPerAction);

                float d = MaxBoneDeltaDegrees();
                if (d > maxDeg)
                {
                    maxDeg = d;
                    atFrame = f;
                }
            }

            string verdictName = ActionName(action);
            results.Add((verdictName, maxDeg, atFrame));

            bool ok = maxDeg > 20f;
            bool empty = maxDeg <= 0.1f;
            if (empty)
                dead++;

            GD.Print($"[足兵动作]   {(ok ? "✓" : "✗")} {verdictName,-16} 与 idle 最大差 {maxDeg,7:F1}°" +
                     $"  (第 {atFrame} 帧){(empty ? "  ← **没有专属动画**" : "")}");
        }

        GD.Print($"[足兵动作] 动作数 {results.Count}，其中「没有专属动画」的 {dead} 个");

        if (dead > 0)
        {
            GD.PrintErr($"[足兵动作] ✗ 有 {dead} 个动作压根没动——不许打勾通过");
            GetTree().Quit(1);
            return;
        }

        GD.Print("[足兵动作] ✓ 通过（每个动作都与 idle 可区分）");
        GetTree().Quit(0);
    }

    private void CaptureBaseline()
    {
        _idleBaseline.Clear();
        foreach (string name in Tracked)
        {
            int bone = _skel.FindBone(name);
            if (bone >= 0)
                _idleBaseline[name] = _skel.GetBonePoseRotation(bone);
        }
    }

    /// <summary>与基准相比，**欧拉分量差的最大值**（规约到 (-180,180]）。</summary>
    private float MaxBoneDeltaDegrees()
    {
        float worst = 0f;

        foreach (string name in Tracked)
        {
            if (!_idleBaseline.TryGetValue(name, out Quaternion baseline))
                continue;

            int bone = _skel.FindBone(name);
            if (bone < 0)
                continue;

            Vector3 a = baseline.GetEuler();
            Vector3 b = _skel.GetBonePoseRotation(bone).GetEuler();

            for (int i = 0; i < 3; i++)
            {
                float ax = Mathf.RadToDeg(a[i]);
                float bx = Mathf.RadToDeg(b[i]);
                float diff = Mathf.Abs(PosMod(bx - ax + 180f, 360f) - 180f);
                if (diff > worst)
                    worst = diff;
            }
        }

        return worst;
    }

    private static float PosMod(float a, float b) => a - b * Mathf.Floor(a / b);

    private static string ActionName(AshigaruAction a) => a switch
    {
        AshigaruAction.Move => "移动",
        AshigaruAction.Attack => "攻击",
        AshigaruAction.HitLight => "受击·轻",
        AshigaruAction.HitHeavy => "受击·重",
        AshigaruAction.PostureBroken => "破韧",
        AshigaruAction.BeingExecuted => "被处决",
        AshigaruAction.Death => "死亡",
        AshigaruAction.Interact => "互动",
        _ => "待机",
    };

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton)
            return skeleton;

        foreach (Node child in node.GetChildren())
        {
            Skeleton3D? found = FindSkeleton(child);
            if (found is not null)
                return found;
        }

        return null;
    }
}
