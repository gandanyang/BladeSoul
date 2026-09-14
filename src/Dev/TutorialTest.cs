using System.Collections.Generic;
using Godot;
using Oniblade.Combat;
using Oniblade.Core;
using Oniblade.Levels;
using Oniblade.Player;

namespace Oniblade.Dev;

/// <summary>
/// 教学第一幕《收剑》端到端验收（T47）：
///     godot --headless --path . res://scenes/tests/Tutorial.tscn
///
/// 单测（<c>TutorialLogicTests</c>）只证明规则对，证明不了**它在真玩家身上真的会推进**。
/// 这个场景量的是那一整条链：走够 6 米 → 挥三刀 → 打一记蓄力斩 → 三段全过。
///
/// 另外验两条纪律（05 §5 铁律）：**提示不许在失败 2 次之前出现**、**一段只给一次**。
///
/// 退出码 0 ＝ 全过，1 ＝ 有错。
/// </summary>
public partial class TutorialTest : Node3D
{
    private readonly List<string> _failures = new();
    private PlayerActor _player = null!;
    private TutorialDirector _tutorial = null!;

    public override async void _Ready()
    {
        AddChild(new CombatArbiter { Name = "CombatArbiter" });
        AddFloor();

        _player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn").Instantiate<PlayerActor>();
        _player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(_player);

        _tutorial = new TutorialDirector
        {
            Name = "Tutorial",
            MoveDistance = 6f,
            LightAttackTarget = 3,
            ChargedAttackTarget = 1,
            StuckFramesPerFailure = 120,
            HintsAfterFailures = 2,
        };
        AddChild(_tutorial);

        await WaitFrames(10);

        Check(_tutorial.DialogueSetCount == 3,
            $"第一幕台词表读进来 3 组（实际 {_tutorial.DialogueSetCount}）");
        Check(_tutorial.Beat == TutorialBeat.Move, "开局教的是「移动」");

        // ── 第一段：走够 6 米 ─────────────────────────────────
        for (int i = 1; i <= 6; i++)
        {
            _player.Position = new Vector3(0f, 0.1f, i);
            await WaitFrames(3);
        }

        Check(_tutorial.Beat == TutorialBeat.LightAttack, $"走够 6 米 → 进入「轻攻击」（实际 {_tutorial.Beat}）");

        // ── 第二段：挥三刀 ────────────────────────────────────
        for (int i = 0; i < _tutorial.LightAttackTarget; i++)
        {
            Input.ActionPress("attack");
            await WaitFrames(3);
            Input.ActionRelease("attack");
            await WaitFrames(45);          // 等这一刀打完（轻斩 26 帧 + 余量）
        }

        Check(_tutorial.Beat == TutorialBeat.ChargedAttack,
            $"挥够三刀 → 进入「重攻击」（实际 {_tutorial.Beat}）");

        // ── 第三段：一记蓄力斩 ────────────────────────────────
        Input.ActionPress("attack");
        await WaitFrames(36);              // 按住过第一段阈值（34 帧）
        Input.ActionRelease("attack");
        await WaitFrames(120);

        Check(_tutorial.Beat == TutorialBeat.Done, $"打出蓄力斩 → 三段全过（实际 {_tutorial.Beat}）");
        Check(_tutorial.Completed, "Completed 标记为真");
        Check(_tutorial.BeatsPassed == 3, $"过了 3 段（实际 {_tutorial.BeatsPassed}）");
        Check(_tutorial.HintRequests == 0, $"一路顺畅时**一次提示都不该给**（实际 {_tutorial.HintRequests} 次）");

        // ── 对照组：卡住时，提示必须在失败 2 次之后 ─────────────
        var idle = new TutorialDirector
        {
            Name = "TutorialIdle",
            MoveDistance = 1000f,          // 永远走不到
            StuckFramesPerFailure = 30,
            HintsAfterFailures = 2,
        };
        AddChild(idle);

        await WaitFrames(35);              // 失败 1 次
        Check(idle.HintRequests == 0, $"失败 1 次时**不许**提示（实际 {idle.HintRequests} 次）");

        await WaitFrames(35);              // 失败 2 次
        Check(idle.HintRequests == 1, $"失败 2 次后给 1 次提示（实际 {idle.HintRequests} 次）");
        Check(idle.LastHintBeat == TutorialBeat.Move, "这条提示属于「移动」段");

        await WaitFrames(70);              // 再卡很久
        Check(idle.HintRequests == 1, $"同一段**只给一次**（实际 {idle.HintRequests} 次）");

        // ── 收刀（docs/15 第二子段「收刀（交互）」）──────────────
        // 按 interact → 记录事实 ＋ 镜头停半秒（TimeScale=0，恢复计时不受冻结影响）。
        _tutorial.SheathePauseSeconds = 0.1;   // 测试里把停顿缩短，别拖慢验收
        _tutorial.NotifySheathe();
        Check(_tutorial.Sheathed, "按 interact → 收刀被记录");
        Check(Engine.TimeScale == 0.0, "收刀瞬间镜头停（TimeScale = 0）");

        // 冻结期间物理帧不走，等恢复必须用不受 TimeScale 影响的计时器
        await ToSignal(GetTree().CreateTimer(0.4, processAlways: true,
            processInPhysics: false, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        Check(Engine.TimeScale == 1.0, "停顿结束后镜头恢复（TimeScale = 1）");
        Check(_tutorial.Sheathed, "重复判定：收刀只记第一次");

        GD.Print("");
        GD.Print(_failures.Count == 0 ? "[教学] ✓ 全部通过" : $"[教学] ✗ {_failures.Count} 条没过：");
        foreach (string f in _failures)
            GD.PrintErr($"[教学]   ✗ {f}");

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        if (ok)
        {
            GD.Print($"[教学]   ✓ {what}");
            return;
        }

        _failures.Add(what);
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void AddFloor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(80f, 0.4f, 80f) } });
        floor.Position = new Vector3(0f, -0.2f, 0f);
        AddChild(floor);
    }
}
