using System.Collections.Generic;
using Godot;
using Oniblade.Core;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 权重体检：量"挥刀时顶点被甩多远"。
///
///     godot --headless --path . res://scenes/tests/WeightProbe.tscn
///
/// 判据：角色身高约 1.75 m，顶点在**一帧之间**跳 >0.20 m 就是肉眼可见的撕裂。
/// 这就是"打击感"的地基——衣服在身上炸成尖刺的时候，再快的刀也读不出来。
///
/// ── 这份探针为什么长这样（都是踩过的坑）──────────────────────────
/// 1. `Skeleton3D.GetBoneGlobalPose()` 在无头场景里**不刷新**：姿势已经从起手转到出刀，
///    它逐帧返回一字不差的同一个值 → "顶点位移"永远算出 0.000 m。**假绿的探针比没有更糟。**
/// 2. 自己按 `rest · pose` 合成全局变换时误差 2.01 m —— `GetBonePose(i)` 返回的**已经是
///    完整局部变换**（静止时它逐位等于 `GetBoneRest`），再乘一次 rest 等于把骨骼链算两遍。
/// 3. 网格 `ARRAY_BONES` 里的下标语义在 Godot 侧一直对不上（`Skin.GetBindBone()` 全返回 -1，
///    按下标取到的顶点/骨骼逐项错位）。
///
/// 所以这里**不碰任何 Godot 的骨架空间 API**：只从 `HumanoidAnimator` 拿"每根骨转了多少"
/// （这是它的输出，也是游戏里真实发生的事），旋转矩阵自己构造、顶点位置自己算。
/// 结果与坐标系无关，只依赖一件事实：**顶点 p 被骨骼 B 转动 θ 时，位移 = |p - pivot| · 2sin(θ/2)**。
/// </summary>
public partial class WeightProbeTest : Node3D
{
    /// <summary>采样帧数。</summary>
    [Export] public int FrameCount { get; set; } = 40;

    /// <summary>单帧位移超过这个值算"撕裂"（米）。</summary>
    [Export] public float TearThreshold { get; set; } = 0.20f;

    /// <summary>顶点被非主导骨扯开超过这个值，说明该处权重粘错了骨头（米）。</summary>
    [Export] public float DriftThreshold { get; set; } = 0.15f;

    /// <summary>
    /// 边的伸长比例超过这个值算"撕开"。
    ///
    /// 为什么用**边长比**而不是帧间位移：快速挥腿时，腿部顶点一帧移动 0.4 m 是**物理正确**的
    /// （角速度 × 半径），拿绝对位移做判据会把正常动作也算成缺陷。
    /// 而相邻顶点的距离只由**权重的空间一致性**决定——刚性蒙皮下它恒定；
    /// 一处皮被撕开，正是"邻居走了、它没走"，边长比会立刻跳起来。它与动画速度无关。
    /// </summary>
    [Export] public float StretchThreshold { get; set; } = 0.005f;

    private sealed class Skinned
    {
        public Vector3[] Rest = System.Array.Empty<Vector3>();
        public int[] Bones = System.Array.Empty<int>();
        public float[] Weights = System.Array.Empty<float>();
        public int Stride;
        public string Label = "";

        /// <summary>三角形索引（3 个一组）。</summary>
        public int[] Tris = System.Array.Empty<int>();

        /// <summary>唯一边的两端点下标（2 个一组）。</summary>
        public int[] Edges = System.Array.Empty<int>();
        public float[] RestEdgeLen = System.Array.Empty<float>();

        /// <summary>当前姿势下的顶点位置，供边长比较用。</summary>
        public Vector3[] Now = System.Array.Empty<Vector3>();
    }

    /// <summary>短于这个长度的棱不参与撕裂统计（米）。</summary>
    private const float MinEdge = 0.010f;
    private const float MinEdgeLen = 0.010f;

    private readonly List<Skinned> _meshes = new();

    /// <summary>骨骼名 -> 该骨在"模型局部空间"的枢轴（= 静止时骨骼原点的位置）。</summary>
    private readonly Dictionary<string, Vector3> _pivot = new();

    public override void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });

        var player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(player);

        Node3D? visual = player.GetNodeOrNull<Node3D>("VisualModel");
        if (visual is null)
        {
            GD.PrintErr("[权重体检] 玩家身上没有 VisualModel");
            GetTree().Quit(1);
            return;
        }

        Skeleton3D? skel = FindSkeleton(visual);
        if (skel is null)
        {
            GD.PrintErr("[权重体检] 找不到 Skeleton3D");
            GetTree().Quit(1);
            return;
        }

        var animator = new HumanoidAnimator(visual);
        GD.Print($"[权重体检] 骨架 {skel.GetBoneCount()} 根骨，动画器 Valid = {animator.Valid}");

        Collect(visual, skel);
        if (_meshes.Count == 0)
        {
            GD.PrintErr("[权重体检] 没有找到带蒙皮权重的网格");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"[权重体检] 网格 {_meshes.Count} 个，总顶点 {TotalVerts()}，"
                 + $"唯一棱 {TotalEdges()}");

        // 静止棱长直接用**绑定姿势**的顶点坐标（`verts`），不经过任何变换——
        // 这是唯一不可能被污染的基准。
        {
            foreach (Skinned s in _meshes)
                for (int e = 0; e < s.RestEdgeLen.Length; e++)
                    s.RestEdgeLen[e] = s.Rest[s.Edges[e * 2]].DistanceTo(s.Rest[s.Edges[e * 2 + 1]]);
        }
        GD.Print($"[权重体检] 骨骼枢轴表：{_pivot.Count} 根");

        // ★ 自证：静止姿势下，顶点必须严格等于它的 rest 位置（蒙皮恒等式）。
        //   这一步过了，"位移"才有意义。
        {
            var identity = new Dictionary<string, Quaternion>();
            float worst = 0f;
            for (int m = 0; m < _meshes.Count; m++)
                for (int i = 0; i < _meshes[m].Rest.Length; i++)
                    worst = Mathf.Max(worst, Skin(_meshes[m], identity, i).DistanceTo(_meshes[m].Rest[i]));

            GD.Print(worst < 1e-4f
                ? $"[权重体检] ✓ 蒙皮恒等式成立（静止时顶点偏差 {worst:E1} m）"
                : $"[权重体检] ✗ 蒙皮恒等式不成立（偏差 {worst:F4} m）—— 后续数字不可信");
        }

        // ── 对照组：静止不动时，棱长比必须正好是 1.000 ──
        Measure(animator, skel, 0f, -1, 20, "静止 20 帧");

        // ── 三段攻击 ──
        for (int step = 0; step < 3; step++)
        {
            animator.PlayAttack(step, 26);
            Measure(animator, skel, 0f, step, FrameCount, step == 0 ? "轻斩·壹" : step == 1 ? "轻斩·贰" : "轻斩·叁");
        }

        // ── 走路（全速 2 秒）──
        animator.PlayAttack(0, 26);
        Measure(animator, skel, 1f, -1, 120, "走路 2 秒");
        GetTree().Quit(0);
    }

    /// <summary>把所有顶点算到当前姿势的位置，存进 `Now`。</summary>
    private void Deform(Skinned s, Dictionary<string, Quaternion> rot)
    {
        for (int i = 0; i < s.Rest.Length; i++)
            s.Now[i] = Skin(s, rot, i);
    }

    /// <summary>
    /// 当前姿势下**边长相对静止**的最大伸长比例。
    ///
    /// 这是"撕裂"的直接度量：刚性蒙皮下每条棱长恒定，棱长比恒为 1；
    /// 一处皮被撕开就是"邻居被骨头带走了、它没有"，棱长会立刻变大。
    /// 与动画快慢无关，也不需要基准帧。
    /// </summary>
    private float Stretch(Dictionary<string, Quaternion> rot, float[]? perEdge)
    {
        float worst = 0f;
        int baseIndex = 0;
        foreach (Skinned s in _meshes)
        {
            Deform(s, rot);
            for (int e = 0; e < s.RestEdgeLen.Length; e++)
            {
                float L0 = s.RestEdgeLen[e];
                // 只看够长的棱：0.009 m 的棱"伸长 951%"其实只有 9 mm，
                // 那是网格自己的短边噪声，不是撕裂。
                if (L0 < MinEdge)
                    continue;

                float L = s.Now[s.Edges[e * 2]].DistanceTo(s.Now[s.Edges[e * 2 + 1]]);
                float grow = L - L0;   // 绝对伸长量（米）。比值在短棱上会爆掉，绝对量才是"这只脚被拉开了几毫米"
                if (grow > worst)
                    worst = grow;

                if (perEdge is not null)
                {
                    int gi = baseIndex + e;
                    if (grow > perEdge[gi])
                        perEdge[gi] = grow;
                }
            }

            baseIndex += s.RestEdgeLen.Length;
        }

        return worst;
    }

    private int TotalEdges()
    {
        int n = 0;
        foreach (Skinned s in _meshes)
            n += s.RestEdgeLen.Length;
        return n;
    }

    private int TotalVerts()
    {
        int n = 0;
        foreach (Skinned s in _meshes)
            n += s.Rest.Length;
        return n;
    }

    private void Collect(Node node, Skeleton3D skel)
    {
        if (node is MeshInstance3D mi && mi.Mesh is not null && mi.Skin is not null)
            AddSurface(mi, skel);

        foreach (Node child in node.GetChildren())
            Collect(child, skel);
    }

    private void AddSurface(MeshInstance3D mi, Skeleton3D skel)
    {
        Mesh mesh = mi.Mesh;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            if (arrays.Count <= (int)Mesh.ArrayType.Weights)
                continue;

            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            if (verts.Length == 0 || bones.Length == 0 || weights.Length == 0)
                continue;

            var idx = arrays.Count > (int)Mesh.ArrayType.Index
                ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array()
                : new int[0];

            // 唯一边：三角形三条边去重（用 min*N+max 做键）
            var seen = new HashSet<long>();
            var edges = new List<int>();
            long n = verts.Length;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = idx[t + e];
                    int b = idx[t + (e + 1) % 3];
                    if (a == b)
                        continue;
                    long key = a < b ? a * n + b : b * n + a;
                    if (seen.Add(key))
                    {
                        edges.Add(a);
                        edges.Add(b);
                    }
                }
            }

            var arr = edges.ToArray();
            var restLen = new float[arr.Length / 2];
            for (int e = 0; e < restLen.Length; e++)
                restLen[e] = verts[arr[e * 2]].DistanceTo(verts[arr[e * 2 + 1]]);

            _meshes.Add(new Skinned
            {
                Rest = verts,
                Bones = bones,
                Weights = weights,
                Stride = bones.Length / verts.Length,
                Label = $"{mi.Name}#{s}",
                Tris = idx,
                Edges = arr,
                RestEdgeLen = restLen,
                Now = new Vector3[verts.Length],
            });
        }

        // 枢轴表 + 骨骼名表。
        // * 名字是唯一可靠的键（下标语义在 Godot 侧对不上）；
        // * 枢轴取 `GetBoneGlobalRest(b).Origin`，它在静止时一定是正确的，而且与网格顶点
        //   处在同一个父级坐标系里（都是 VisualModel 的子节点）。
        for (int b = 0; b < skel.GetBoneCount(); b++)
        {
            _boneNames[b] = skel.GetBoneName(b);
            _pivot[skel.GetBoneName(b)] = skel.GetBoneGlobalRest(b).Origin;
        }
    }

    /// <summary>
    /// 复位骨架、让动画器把姿势推到位，然后取基准帧。
    ///
    /// 两个都不能省：
    /// * **复位**：不复位的话基准帧会带着上一组测试留下的姿势，第一帧的"位移"里
    ///   混进了"从上一组姿势跳回本组起手"那一下（实测 0.698 m），真问题就被淹了；
    /// * **复位后再跑一帧**：`SetBonePoseRotation(b, Identity)` 并不等于"骨骼回到静止"——
    ///   有些骨骼的 rest 自带 180° 翻转（本模型 L_Toe 的 rest 基是 diag(-1, ·, ·)），
    ///   Godot 会在下一次骨架刷新时把"相对旋转"重新算回 180°。
    ///   跑一帧让这些骨稳定下来，基准帧才对得上。
    /// </summary>
    private Vector3[][] SettleAndSnapshot(Skeleton3D skel, HumanoidAnimator animator,
        float speed, int attackFrame)
    {
        for (int b = 0; b < skel.GetBoneCount(); b++)
            skel.SetBonePoseRotation(b, Quaternion.Identity);

        for (int f = 0; f < 2; f++)
        {
            animator.AnimateLocomotion(speed, 1f / 60f);
            animator.AnimateCombat(1f / 60f, attackFrame);
        }

        return Snapshot(ReadRotations(skel));
    }

    /// <summary>从骨架读当前姿势：骨骼名 -> 相对静止的旋转四元数。</summary>
    private static Dictionary<string, Quaternion> ReadRotations(Skeleton3D skel)
    {
        var d = new Dictionary<string, Quaternion>();
        for (int b = 0; b < skel.GetBoneCount(); b++)
            d[skel.GetBoneName(b)] = skel.GetBonePoseRotation(b);
        return d;
    }

    private Vector3[][] Snapshot(Dictionary<string, Quaternion> rot)
    {
        var snap = new Vector3[_meshes.Count][];
        for (int m = 0; m < _meshes.Count; m++)
        {
            Skinned s = _meshes[m];
            snap[m] = new Vector3[s.Rest.Length];
            for (int i = 0; i < s.Rest.Length; i++)
                snap[m][i] = Skin(s, rot, i);
        }

        return snap;
    }

    private float Move(Dictionary<string, Quaternion> rot, Vector3[][] prev,
        float[]? peaks, ref float peak)
    {
        float frame = 0f;
        for (int m = 0; m < _meshes.Count; m++)
        {
            Skinned s = _meshes[m];
            for (int i = 0; i < s.Rest.Length; i++)
            {
                Vector3 p = Skin(s, rot, i);
                float d = p.DistanceTo(prev[m][i]);
                prev[m][i] = p;   // ★ 必须推进基准，否则第一帧之后位移恒为 0
                if (d > frame)
                    frame = d;
                if (d > peak)
                    peak = d;

                if (peaks is not null)
                {
                    int gi = Index(m, i);
                    if (d > peaks[gi])
                        peaks[gi] = d;
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// 单顶点形变。**只做一件事**：把顶点绕每根影响它的骨骼的枢轴转过去，按权重加权。
    ///
    /// 与 GPU 蒙皮等价：绕枢轴 c 转 q 就是 `c + q·(p-c)`，而 `boneGlobal·boneRest⁻¹`
    /// 展开后正是这个式子。绕开 Godot 的骨架空间，就不必再猜它的约定。
    /// </summary>
    private Vector3 Skin(Skinned s, Dictionary<string, Quaternion> rot, int i)
    {
        Vector3 p = s.Rest[i];
        Vector3 acc = Vector3.Zero;
        float total = 0f;
        for (int k = 0; k < s.Stride; k++)
        {
            float w = s.Weights[i * s.Stride + k];
            if (w <= 0f)
                continue;

            string bone = BoneName(s.Bones[i * s.Stride + k]);
            if (bone.Length == 0 || !_pivot.TryGetValue(bone, out Vector3 c))
                continue;

            Quaternion q = rot.TryGetValue(bone, out Quaternion r) ? r : Quaternion.Identity;
            acc += (c + q * (p - c)) * w;
            total += w;
        }

        // 权重和应当为 1；万一不是，按实际和归一化（否则顶点会朝原点塌）
        return total > 0f ? acc / total : p;
    }

    /// <summary>
    /// 顶点偏离它**主导骨**刚性带动位置的距离。
    ///
    /// 为什么用这个量而不是帧间位移：它只取决于**当前姿势**，对"基准帧对不对齐"完全免疫——
    /// 帧间差分那条路太脆（基准帧偏一帧就假报 0.7 m）。
    ///
    /// 含义也直白：主导骨决定这个顶点"长在哪"，其余骨骼把它从那儿扯开。扯开得越多，
    /// 这一处皮就越像被撕开了。权重全是同一根骨的顶点，这个值恒为 0。
    /// </summary>
    private float PivotDrift(Skinned s, Dictionary<string, Quaternion> rot, int i)
    {
        int best = -1;
        float bw = 0f;
        for (int k = 0; k < s.Stride; k++)
        {
            float w = s.Weights[i * s.Stride + k];
            if (w > bw)
            {
                bw = w;
                best = s.Bones[i * s.Stride + k];
            }
        }

        if (bw < 0.5f)
            return 0f;

        string bone = BoneName(best);
        if (bone.Length == 0 || !_pivot.TryGetValue(bone, out Vector3 c))
            return 0f;

        Vector3 p = s.Rest[i];
        Quaternion q = rot.TryGetValue(bone, out Quaternion r) ? r : Quaternion.Identity;
        Vector3 rigid = c + q * (p - c);      // 只被主导骨带动的话，顶点应该在这里
        return Skin(s, rot, i).DistanceTo(rigid);
    }

    private readonly Dictionary<int, string> _boneNames = new();

    private string BoneName(int index)
    {
        return _boneNames.TryGetValue(index, out string? n) ? n : "";
    }

    /// <summary>
    /// 跑一段真实动作，同时量两把尺子：
    /// * **棱长比**（主判据）：相邻顶点被拉开多少。与动画快慢无关、不需要基准帧。
    /// * 单帧位移（辅）：只用于和之前的记录对照，快速挥腿时它天然偏大。
    /// </summary>
    private void Measure(HumanoidAnimator animator, Skeleton3D skel, float speed,
        int attackFrame, int frames, string label)
    {
        ResetBase(skel, animator, speed, attackFrame);
        var prev = Snapshot(ReadRotations(skel));

        int vTotal = TotalVerts();
        var vPeak = new float[vTotal];
        var dPeak = new float[vTotal];
        var ePeak = new float[TotalEdges()];
        float peak = 0f;
        float stretch = 0f;

        for (int f = 0; f < frames; f++)
        {
            animator.AnimateLocomotion(speed, 1f / 60f);
            animator.AnimateCombat(1f / 60f, attackFrame);
            Dictionary<string, Quaternion> r = ReadRotations(skel);
            Move(r, prev, vPeak, ref peak);
            stretch = Mathf.Max(stretch, Stretch(r, ePeak));

            for (int m = 0; m < _meshes.Count; m++)
                for (int i = 0; i < _meshes[m].Rest.Length; i++)
                {
                    float d = PivotDrift(_meshes[m], r, i);
                    int gi = Index(m, i);
                    if (d > dPeak[gi])
                        dPeak[gi] = d;
                }
        }

        float drift = 0f;
        int drifted = 0;
        int vTorn = 0;
        int eTorn = 0;
        for (int e = 0; e < ePeak.Length; e++)
            if (ePeak[e] > StretchThreshold)
                eTorn++;
        for (int i = 0; i < vTotal; i++)
        {
            drift = Mathf.Max(drift, dPeak[i]);
            if (dPeak[i] > DriftThreshold)
                drifted++;
            if (vPeak[i] > TearThreshold)
                vTorn++;
        }

        GD.Print($"[权重体检] {label,-10} 偏离主导骨峰值 {drift:F3} m"
                 + $"（> {DriftThreshold:F2} m 者 {drifted}）"
                 + $"；单帧位移峰值 {peak:F3} m（> {TearThreshold:F2} m 者 {vTorn}）"
                 + $"；棱伸长峰值 {stretch * 1000f:F0} mm（> {StretchThreshold * 1000f:F0} mm 者 {eTorn}）");
    }
    /// <summary>
    /// 把动画器推到目标姿势、连跑两帧让它稳定，然后取基准。
    ///
    /// **不要**在这里复位骨架。`SetBonePoseRotation(b, Identity)` 并不等于"骨骼回到静止"：
    /// 动画器不碰的那些骨（本模型 `L_Toe` 的 rest 基是 diag(-1, ·, ·)，自带 180° 翻转）
    /// 会在下一次骨架刷新时被 Godot 算回 180°，基准姿势整体偏掉 0.30 m
    /// （实测"基准姿势下形变≠rest 最大差 0.2965 m"）。
    /// 基准帧只用**动画器自己设过的姿势**，两边语义才一致。
    /// </summary>
    private void ResetBase(Skeleton3D skel, HumanoidAnimator animator, float speed, int attackFrame)
    {
        for (int f = 0; f < 2; f++)
        {
            animator.AnimateLocomotion(speed, 1f / 60f);
            animator.AnimateCombat(1f / 60f, attackFrame);
        }
    }

    private string Where(int globalIndex)
    {
        int n = globalIndex;
        for (int m = 0; m < _meshes.Count; m++)
        {
            if (n < _meshes[m].Rest.Length)
                return $"{_meshes[m].Label} v{n}";
            n -= _meshes[m].Rest.Length;
        }

        return "?";
    }

    private int Index(int mesh, int vertex)
    {
        int n = vertex;
        for (int m = 0; m < mesh; m++)
            n += _meshes[m].Rest.Length;
        return n;
    }

    private Vector3 RestOf(int globalIndex)
    {
        int n = globalIndex;
        foreach (Skinned s in _meshes)
        {
            if (n < s.Rest.Length)
                return s.Rest[n];
            n -= s.Rest.Length;
        }

        return Vector3.Zero;
    }

    private string DominantBone(int globalIndex)
    {
        int n = globalIndex;
        foreach (Skinned s in _meshes)
        {
            if (n < s.Rest.Length)
            {
                int best = -1;
                float bw = 0f;
                for (int k = 0; k < s.Stride; k++)
                {
                    float w = s.Weights[n * s.Stride + k];
                    if (w > bw)
                    {
                        bw = w;
                        best = s.Bones[n * s.Stride + k];
                    }
                }

                string nm = BoneName(best);
                return nm.Length > 0 ? nm : "?";
            }

            n -= s.Rest.Length;
        }

        return "?";
    }

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
