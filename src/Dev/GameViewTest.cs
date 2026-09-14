using Godot;

namespace Oniblade.Dev;

/// <summary>
/// 用**游戏自己的第三人称相机**渲染 Dojo 里的玩家，复现玩家实际看到的画面。
///
/// 为什么之前几张图都不可信：`PoseShot` 用了一个我自己摆的正面机位，
/// 而玩家看到的是「相机在角色背后、略微俯视」。同一个畸形在这两个机位下
/// 可以读成完全不同的东西。这个场景直接实例化 `Dojo.tscn`，让游戏自己
/// 建相机、自己摆玩家，然后在**游戏的相机上**截一帧。
///
///     godot --path . res://scenes/tests/GameView.tscn
///
/// 同时把玩家的骨骼全局姿势打出来（用 `GetBonePose` 的局部量自己合成，
/// 不碰 `GetBoneGlobalPose`——它在某些路径下不刷新）。
/// </summary>
public partial class GameViewTest : Node3D
{
    [Export] public int Width { get; set; } = 1152;
    [Export] public int Height { get; set; } = 648;

    /// <summary>等几帧再抓——相机要等玩家生成后才 Current。</summary>
    [Export] public int WarmupFrames { get; set; } = 60;

    /// <summary>
    /// 要不要在抓完站桩图之后再拍「格挡」和「出刀」两张。
    ///
    /// 为什么需要：试玩反馈"按右键进了格挡但不明显"——这句话本身没法验证。
    /// 把三个姿态用**同一台游戏相机**各拍一张摆在一起，才能回答
    /// "格挡到底比站桩差多少"，也才能判断改动有没有让它变明显。
    /// </summary>
    [Export] public bool ShootPoses { get; set; }

    private int _frames;
    private bool _done;

    /// <summary>抓第一张时窗口还剩几帧；第二张要等它掉到一半以下。</summary>
    private int _cueStartLeft = int.MaxValue;

    private int _cueLeft = int.MaxValue;

    /// <summary>问一下玩家侧"弹开窗还剩几帧"。没窗口时返回 0。</summary>
    private int DeflectWindowFramesLeftOfPlayer()
    {
        Node? player = FindByName(GetTree().Root, "Player");
        return player is Combat.ICombatActorDebug dbg ? dbg.DeflectWindowFramesLeft : 0;
    }
    private bool? _cueShotA;
    private bool? _cueShotB;
    private int _posePhase;
    private int _poseFrame;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(Width, Height);
        GetWindow().Title = "游戏视角复现";

        var dojo = GD.Load<PackedScene>("res://scenes/levels/Dojo.tscn");
        if (dojo is null)
        {
            GD.PrintErr("[游戏视角] Dojo 加载失败");
            GetTree().Quit(1);
            return;
        }

        AddChild(dojo.Instantiate());
        GD.Print("[游戏视角] Dojo 已实例化");
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames < WarmupFrames)
            return;

        if (!_done)
        {
            _done = true;
            ReportAndShoot();
        }

        if (!ShootPoses)
        {
            if (_frames >= WarmupFrames + 3)
                GetTree().Quit(0);
            return;
        }

        // 姿态连拍：站桩 → 格挡（按住）→ 出刀
        _poseFrame++;
        switch (_posePhase)
        {
            case 0:
                if (_poseFrame >= 4)
                {
                    SaveShot("pose_idle.png");
                    Input.ActionPress("guard");
                    _posePhase = 1;
                    _poseFrame = 0;
                }

                break;

            case 1:
                // 按下防御键的**头几帧**才是弹开窗开着的时刻，指示器只在这时候亮。
                // 所以这里要立刻抓一张——等 20 帧之后窗口已经关了，
                // 拍到的就只是"格挡姿态"，看不到指示器。
                if (_poseFrame == 2)
                    SaveShot("pose_cue.png");

                // 恒定亮度的对照：两张都要在窗口内，且**跨越"刚按下"和"快关闭"**。
                //
                // 不能用"第 N 个 _Process 帧"当条件——`_Process` 与物理帧不同频，
                // 第一版就是这么栽的：以为抓在第 10 / 14 帧（都在窗口内），
                // 实际第二张抓到时窗口已归零，测出来的是"光环熄灭"，却会被读成"光环在闪"。
                // 所以要按**真实剩余帧数**决定抓拍时机。
                _cueLeft = DeflectWindowFramesLeftOfPlayer();

                if (_cueStartLeft == int.MaxValue && _cueLeft > 0)
                    _cueStartLeft = _cueLeft;

                // 两张都等**窗口后半段**再抓。
                //
                // 为什么不在刚开窗时抓：那一刻格挡姿态还在抬起过程中
                // （GuardRaiseFrames = 8），实测抓到的那一帧光环根本没进画面
                // （蓝色像素只剩 370 个、质心落在背景上），于是"两张对比"变成
                // "一张有环一张没环"，会被误读成光环在闪。
                if (_cueStartLeft != int.MaxValue
                    && _cueLeft > 0
                    && _cueLeft * 2 <= _cueStartLeft)
                {
                    if (_cueShotA is null)
                    {
                        SaveShot("pose_cue_late.png");
                        _cueShotA = true;
                    }
                    else if (_cueShotB is null)
                    {
                        SaveShot("pose_cue_late2.png");
                        _cueShotB = true;
                    }
                }

                if (_poseFrame >= 20)
                {
                    SaveShot("pose_guard.png");
                    Input.ActionRelease("guard");
                    _posePhase = 2;
                    _poseFrame = 0;
                }

                break;

            default:
                if (_posePhase == 2 && _poseFrame >= 20)
                {
                    Input.ActionPress("attack");
                    _posePhase = 3;
                    _poseFrame = 0;
                }
                else if (_posePhase == 3 && _poseFrame >= 14)
                {
                    SaveShot("pose_attack.png");
                    Input.ActionRelease("attack");
                    GD.Print("[游戏视角] 姿态连拍完成：pose_idle / pose_guard / pose_attack");
                    GetTree().Quit(0);
                }

                break;
        }
    }

    /// <summary>抓一帧存盘，并把"抓图那一刻弹开窗还剩几帧"作为返回值交出去。</summary>
    private int SaveShot(string file)
    {
        Image? img = GetViewport().GetTexture()?.GetImage();
        if (img is null)
        {
            GD.PrintErr($"[游戏视角] {file} 抓图失败（viewport 拿不到纹理）");
            return -1;
        }

        img.SavePng($"user://{file}");

        // 把**窗口实际还剩几帧**跟图一起打出来。
        // 不这么做就会像我第一次那样：以为抓在第 10 帧（窗口内），
        // 实际 _Process 与物理帧不同频，抓到的可能已经过期——
        // 于是"光环亮不亮"被测成了"抓早了还是抓晚了"。
        Node? player = FindByName(GetTree().Root, "Player");
        int left = -1;
        if (player is Combat.ICombatActorDebug dbg)
            left = dbg.DeflectWindowFramesLeft;

        GD.Print($"[游戏视角] 截图 → user://{file}   (弹开窗剩余 {left} 帧)");
        return left;
    }

    private void ReportAndShoot()
    {
        Node? player = FindByName(GetTree().Root, "Player");
        if (player is null)
        {
            GD.PrintErr("[游戏视角] 找不到 Player 节点");
            return;
        }

        GD.Print($"[游戏视角] Player 位置 = {((Node3D)player).GlobalPosition}");

        Skeleton3D? skel = FindSkeleton(player);
        if (skel is not null)
        {
            GD.Print($"[游戏视角] 骨架 {skel.GetBoneCount()} 骨");

            // 逐骨打印"局部姿势相对 rest 差了多少"，用**欧拉角之差**。
            //
            // 为什么不用四元数点积：`|q1·q2|` 对 0° 和 180° 都给出 1，
            // 于是"完全没动"和"整体翻过来"会被算成同一个数——上一版就是这么
            // 报出「L_Thigh 偏 180°」的假警报的。欧拉角分量相减没有这个歧义。
            var offenders = new System.Collections.Generic.List<(string Name, float Deg, Vector3 RestPos, Vector3 PosePos)>();
            for (int b = 0; b < skel.GetBoneCount(); b++)
            {
                string name = skel.GetBoneName(b);
                Transform3D rest = skel.GetBoneRest(b);
                Transform3D pose = skel.GetBonePose(b);

                Vector3 dr = rest.Basis.GetEuler() * Mathf.RadToDeg(1f);
                Vector3 dp = pose.Basis.GetEuler() * Mathf.RadToDeg(1f);
                float deg = Mathf.Max(
                    AngleGap(dr.X, dp.X),
                    Mathf.Max(AngleGap(dr.Y, dp.Y), AngleGap(dr.Z, dp.Z)));

                if (deg > 25f)
                    offenders.Add((name, deg, rest.Origin, pose.Origin));
            }

            // 反向解出"到底被施加了什么旋转"：pose = 施加量 * rest
            //   → 施加量 = pose.Basis * rest.Basis⁻¹，再取轴角。
            // 光看"偏了多少度"不够，必须知道绕的**轴**——同一个角度绕 X 是抬腿、
            // 绕 Z 是转身。把轴打出来才能对上代码里那一行 Rot() 的意图。
            GD.Print("[游戏视角] ── 每根骨的 rest 局部旋转（这就是 Rot() 会覆盖掉的东西）──");
            foreach (string nm in new[] { "Hip", "Spine01", "Spine02", "Head", "L_Thigh", "R_Thigh", "L_Calf", "L_Upperarm", "R_Upperarm", "L_Hand" })
            {
                int bi = skel.FindBone(nm);
                if (bi < 0)
                    continue;
                Basis rb = skel.GetBoneRest(bi).Basis;
                Quaternion rq = rb.GetRotationQuaternion();
                Vector3 ra = rq.GetAxis();
                float rAng = Mathf.RadToDeg(rq.GetAngle());
                GD.Print($"[游戏视角]   {nm,-12} rest 旋转 = {rAng,6:F1}°  绕 ({ra.X,5:F2},{ra.Y,5:F2},{ra.Z,5:F2})"
                         + $"    基底列 X=({rb.X.X:F2},{rb.X.Y:F2},{rb.X.Z:F2}) Y=({rb.Y.X:F2},{rb.Y.Y:F2},{rb.Y.Z:F2})");
            }

            GD.Print("[游戏视角] ── 施加的旋转（轴 + 角）──");
            foreach (string nm in new[] { "Hip", "Spine01", "Spine02", "Head", "L_Thigh", "R_Thigh", "L_Calf", "R_Calf", "L_Upperarm", "R_Upperarm" })
            {
                int bi = skel.FindBone(nm);
                if (bi < 0)
                    continue;
                Basis restB = skel.GetBoneRest(bi).Basis;
                Basis poseB = skel.GetBonePose(bi).Basis;
                Basis applied = (poseB * restB.Inverse()).Orthonormalized();
                Vector3 axis = applied.GetRotationQuaternion().GetAxis();
                float angle = Mathf.RadToDeg(applied.GetRotationQuaternion().GetAngle());
                string axisName = Mathf.Abs(axis.Y) > 0.9f ? "局部Y"
                    : Mathf.Abs(axis.X) > 0.9f ? "局部X"
                    : Mathf.Abs(axis.Z) > 0.9f ? "局部Z"
                    : "斜轴";
                GD.Print($"[游戏视角]   {nm,-12} 绕 {axisName} 转 {angle,6:F1}°  axis=({axis.X,5:F2},{axis.Y,5:F2},{axis.Z,5:F2})");
            }

            offenders.Sort((a, b) => b.Deg.CompareTo(a.Deg));
            GD.Print($"[游戏视角] 局部姿势偏离 rest > 25° 的骨：{offenders.Count} 根");
            foreach ((string name, float deg, Vector3 restPos, Vector3 posePos) in offenders)
                GD.Print($"[游戏视角]   {name,-12} 偏 {deg,6:F1}°  rest.Origin=({restPos.X:F3},{restPos.Y:F3},{restPos.Z:F3})"
                         + $" pose.Origin=({posePos.X:F3},{posePos.Y:F3},{posePos.Z:F3})");
        }

        Camera3D? cam = GetViewport().GetCamera3D();
        GD.Print(cam is null
            ? "[游戏视角] 当前没有 Current 相机"
            : $"[游戏视角] 相机 @ {cam.GlobalPosition}，看向 {(-cam.GlobalTransform.Basis.Z)}");

        string path = "user://gameview.png";
        Image image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() == 0)
        {
            GD.PrintErr("[游戏视角] 抓到空帧——需要带窗口跑");
            return;
        }

        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"[游戏视角] 截图 → {ProjectSettings.GlobalizePath(path)}"
            : $"[游戏视角] 存图失败 {error}");
    }

    /// <summary>两个角度（度）之间的最小夹角，0..180。</summary>
    private static float AngleGap(float a, float b)
    {
        float d = Mathf.PosMod(a - b + 180f, 360f) - 180f;
        return Mathf.Abs(d);
    }

    private static Node? FindByName(Node root, string name)    {
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

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s)
            return s;
        foreach (Node c in n.GetChildren())
        {
            Skeleton3D? r = FindSkeleton(c);
            if (r is not null)
                return r;
        }

        return null;
    }
}
