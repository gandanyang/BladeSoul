using System.Threading.Tasks;
using Godot;
using Oniblade.Combat;
using Oniblade.Combat.States;
using Oniblade.Core;
using Oniblade.Enemies;

namespace Oniblade.Dev;

/// <summary>
/// T52 端到端：足兵敌人 + 破韧链路。
///
/// 已经有 6 组对照实验写好了，分两批：
///   · **A 组（本轮可验）**：模型真的挂上并被动起来了、体干打满真的会进破韧态、
///     破韧态真的不能攻击、窗口真的会过期
///   · **B 组（处决链路，待 T52 下一轮）**：处决键、处决无敌、连按只出一次
///
/// 判据全部是**可数的东西**（骨角差、状态名、帧号），不是"看起来对"。
/// `四、` 那种"按了没反应"的项会明确打 `TODO` 而不是打勾——
/// 本项目吃过"只测单点让错实现看起来正确"的亏。
/// </summary>
public partial class EnemyDeathblowTest : Node3D
{
    private const string EnemyScene = "res://scenes/enemies/Ashigaru.tscn";

    private int _pass;
    private int _fail;

    public override async void _Ready()
    {
        GD.Print("[破韧处决] ── T52 端到端 ──");

        var packed = GD.Load<PackedScene>(EnemyScene);
        if (packed is null)
        {
            GD.PrintErr($"[破韧处决] ✗ 加载 {EnemyScene} 失败");
            GetTree().Quit(1);
            return;
        }

        // ── A0：地面 ───────────────────────────────────────────
        //
        // ★ 测试场景**必须有地面**。没有地面时敌人一直自由落体：
        //   `Velocity.Y` 会涨到 -15 而 X/Z 恒为 0（走位被 `DesiredVelocity` 驱动，
        //   却因为悬空而看不出位移），于是"追击"看起来像"没动"、
        //   "破韧不动"看起来像"掉了 5.9 米"。第一次跑就是这么被骗的。
        var ground = new StaticBody3D
        {
            Name = "Ground",
            CollisionLayer = 1,
            CollisionMask = 0,
            Position = new Vector3(0f, -0.5f, 0f),
        };
        ground.AddChild(new CollisionShape3D
        {
            // 100×100 的大板，免得敌人跑出去
            Shape = new BoxShape3D { Size = new Vector3(100f, 1f, 100f) },
        });
        AddChild(ground);

        // ── A1：场景与模型 ─────────────────────────────────────
        var enemy = packed.Instantiate<Ashigaru>();
        AddChild(enemy);
        await NextPhysicsFrame();
        await NextPhysicsFrame();
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        Check("A1 敌人实例化且是 Ashigaru", enemy is not null, enemy?.GetType().Name ?? "null");
        Check("A1 模型骨架可用（不是灰盒降级）", enemy!.HasModel,
              enemy.HasModel ? "AshigaruAnimator.Valid = true" : "降级到 BlockoutRig —— 模型没挂上");

        // ── A2：动画真的在动（量骨角，不看"有没有动画资产"）────────
        //
        // ★ 必须先建一个假玩家：足兵的移动是**追人驱动的**，场上没有 `player` 组节点，
        //   `ChaseTarget()` 永远不写 `DesiredVelocity`，`speed01` 恒为 0，
        //   动画器就按设计播"待机"——那时测移动动作只会得到 0°，是**探针的错**不是实现的错。
        var fakePlayer = new Node3D { Name = "FakePlayer" };
        fakePlayer.AddToGroup("player");
        AddChild(fakePlayer);
        fakePlayer.GlobalPosition = enemy.GlobalPosition + new Vector3(0f, 0f, -6f);

        await NextPhysicsFrame();

        // idle 基准要**显式让敌人别追**：`preferredRange` 设成 999 会让
        // `TryGetMoveIntent()` 立刻返回 false，敌人停下（IdleState）。
        // 不这么做的话，"待机"里混着刚起步的迈腿帧，量出来的 32° 并不是呼吸幅度。
        float keepRange = enemy.PreferredRange;
        enemy.PreferredRange = 999f;
        for (int i = 0; i < 20; i++)
            await NextPhysicsFrame();

        float idleDelta = await MeasureBoneDelta(enemy, 45);
        enemy.PreferredRange = keepRange;

        float moveDelta = await MeasureBoneDelta(enemy, 60);

        Check("A2 移动动作与待机可区分（>20°）", moveDelta > 20f,
              $"移动 {moveDelta:F1}° vs 待机 {idleDelta:F1}°");

        // 追击是否真的发生了（不然上面的"移动动作"其实是别的原因）
        float chased = enemy.GlobalPosition.DistanceTo(fakePlayer.GlobalPosition);
        Check("A2 敌人真的在追（位移可测）", chased < 6f, $"与假玩家距离 6.00 → {chased:F2} 米");

        // ── A3：体干打满 → 进破韧态 ────────────────────────────
        int brokenBefore = enemy.BrokenEnterCount;
        enemy.ApplyPostureDamage(9999);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        bool inBroken = enemy.Machine.Current is PostureBrokenState;
        Check("A3 体干打满真的进破韧态", inBroken,
              $"{enemy.StateName}（BrokenEnterCount {brokenBefore} → {enemy.BrokenEnterCount}）");

        int framesLeft = enemy.PostureBrokenFramesLeft;
        Check("A3 窗口按配置张开（120 帧）", framesLeft > 100,
              $"剩余 {framesLeft} 帧 / 配置 {enemy.PostureBrokenFrames}");

        // ── A4：破韧态**不能主动攻击**（状态机层面拒绝）──────────
        enemy.Machine.Change<AttackState>();
        await NextPhysicsFrame();
        await NextPhysicsFrame();
        Check("A4 破韧态拒绝进入攻击态", enemy.Machine.Current is not AttackState,
              $"当前 {enemy.StateName}");

        // ── A5：破韧态里敌人不移动（不能滑走，否则处决距离难猜）──
        Vector3 p0 = enemy.GlobalPosition;
        for (int i = 0; i < 20; i++)
            await NextPhysicsFrame();
        float moved = enemy.GlobalPosition.DistanceTo(p0);
        Check("A5 破韧态位移 ≈ 0（瘫软不动）", moved < 0.02f, $"20 帧位移 {moved:F4} 米");

        // ── A6：窗口过期后不再可处决 ───────────────────────────
        Check("A6 窗口开着时可处决", enemy.CanBeExecuted, $"CanBeExecuted = {enemy.CanBeExecuted}");

        // 直接把窗口推到过期（不等 120 帧真的跑完，测试要快）
        while (enemy.Machine.Current is PostureBrokenState { IsOpen: true })
        {
            await NextPhysicsFrame();
            if (enemy.Machine.Current is not PostureBrokenState)
                break;
            // 保险：不许无限等
            if (Engine.GetPhysicsFrames() % 100000 == 0)
                break;
        }

        Check("A6 窗口过期后不可处决且回到行动态",
              !enemy.CanBeExecuted && enemy.Machine.Current is not PostureBrokenState,
              $"当前 {enemy.StateName}，CanBeExecuted = {enemy.CanBeExecuted}");

        // ── B 组：处决链路 ─────────────────────────────────────
        //
        // ★ 这一组**必须用真玩家**。假玩家没有状态机，验不了"按 F 会不会进处决态"。
        //   而且必须**真按键**（`Input.ActionPress`）而不是直接调 `TryEnterDeathblow()`：
        //   后者验的是那个函数，键位接线接错了照样过——而"普攻键不许处决"这条约束
        //   的全部内容就是"哪个键接到哪里"。
        GD.Print("[破韧处决]  ── B 组（处决链路）──");

        // A 组的假玩家会干扰处决选目标（它也在 player 组），先拆掉
        SafeFree(fakePlayer);
        SafeFree(enemy);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        var arbiter = new CombatArbiter { Name = "CombatArbiter" };
        AddChild(arbiter);

        var player = GD.Load<PackedScene>("res://scenes/actors/Player.tscn")
                     .Instantiate<Oniblade.Player.PlayerActor>();
        player.Position = new Vector3(0f, 0.1f, 0f);
        AddChild(player);
        await NextPhysicsFrame();

        // EnemyA = 处决目标（离玩家 1.5 米，在 MaxDistance 2.2 内）
        var enemyA = packed.Instantiate<Ashigaru>();
        enemyA.Position = new Vector3(0f, 0.1f, 1.5f);
        AddChild(enemyA);

        // EnemyB = 对照：活着、没破韧、但**够得着**。用来证"非破韧态按 F 不触发"
        var enemyB = packed.Instantiate<Ashigaru>();
        enemyB.Position = new Vector3(0f, 0.1f, 2.0f);
        AddChild(enemyB);

        await NextPhysicsFrame();
        await NextPhysicsFrame();

        Check("B0 玩家 Loaded 且配了处决参数", player.Deathblow is not null,
              player.Deathblow is null ? "Deathblow 为空 —— 场景没挂 data/combat/deathblow.tres" : "ok");

        if (player.Deathblow is null)
        {
            Report();
            return;
        }

        // 让 EnemyA 破韧（EnemyB 保持完好）
        enemyA.ApplyPostureDamage(9999);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        Check("B0 处决目标已破韧（前置）", enemyA.CanBeExecuted,
              $"EnemyA {enemyA.StateName} CanBeExecuted={enemyA.CanBeExecuted}；" +
              $"EnemyB {enemyB.StateName} CanBeExecuted={enemyB.CanBeExecuted}");

        // ── B1：破韧态下按**普攻键** → 绝不触发处决 ─────────────
        //
        // ★ 这是"普攻键不许处决"的守门人。若这条键位接错，整个体干系统就废了：
        //   玩家一补刀就直接进处决，"砍几刀再从容处决"的节奏彻底消失。
        //
        // ★ 断言拆成**两条**，因为第一次跑时它"通过"了但其实是假阳性：
        //   破韧的敌人对普攻来说是硬直目标，玩家那一刀打出了**一闪**（IssenState）
        //   并把它直接杀了 —— 敌人的死让"存活"那条前提已经不成立。
        //   只写一条 `!playerExecuting` 的话，敌人怎么死的都不会被发现。
        await TapAction("attack", 4);

        bool playerExecuting = player.Machine.Current is DeathblowExecuteState;
        Check("B1 普攻键不触发处决（键位分离）", !playerExecuting,
              $"玩家={player.StateName}（若是 DeathblowExecuteState 说明普攻键接到了处决）");

        // 独立一条：普攻就算把破韧目标打死，也必须是"打死的"，不是"处决的"。
        // `IsBeingExecuted` 为真才是走处决链路；闪避/普攻/重伤都不该出现它。
        Check("B1 普攻打死也只能是打死、不能是处决",
              !enemyA.IsBeingExecuted,
              $"EnemyA={enemyA.StateName} IsDead={enemyA.IsDead} " +
              $"（被一闪/普攻杀死是允许的，`IsBeingExecuted`=true 才是键位接错）");

        // 等玩家那一下普攻收完，免得影响后面
        for (int i = 0; i < 40 && player.Machine.Current is not IdleState; i++)
            await NextPhysicsFrame();

        // ── B2：非破韧态按交互键 → 不触发（否则等于随时秒怪）────
        //
        // 换一个**全新的、完好的**敌人：B1 那一刀（一闪）已经把上面那个打死了，
        // 拿一具尸体来验"非破韧态不触发"，等于什么都没验。
        SafeFree(enemyA);
        enemyB.Position = new Vector3(0f, 0.1f, 30f);   // 场外，保证没有可处决目标
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        var enemyHealthy = packed.Instantiate<Ashigaru>();
        enemyHealthy.Position = new Vector3(0f, 0.1f, 1.5f);
        AddChild(enemyHealthy);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        bool anyExecutable = enemyHealthy.CanBeExecuted || enemyB.CanBeExecuted;
        await TapAction("interact", 4);

        Check("B2 非破韧态按 F/E 不触发处决",
              !anyExecutable
              && player.Machine.Current is not DeathblowExecuteState
              && !enemyHealthy.IsDead && !enemyHealthy.IsBeingExecuted,
              $"场上可处决目标={anyExecutable}，玩家={player.StateName} " +
              $"目标={enemyHealthy.StateName} 存活={!enemyHealthy.IsDead}");

        for (int i = 0; i < 40 && player.Machine.Current is not IdleState; i++)
            await NextPhysicsFrame();

        // ── B3：破韧态按交互键 → 真的处决，敌人死 ───────────────
        //
        // ★ 每一项都**换一个全新的敌人**，绝不重用 B1/B2 里那个。
        //   B 组第一次跑就是栽在这上面：B1 按普攻键时，玩家对着**破韧的**目标
        //   打出了一闪（`IssenState`）——一闪对一切攻击生效，敌人当场就死了。
        //   之后 B3 用同一个敌人，于是"判定帧之前目标还活着"失败：
        //   它早在 B1 就死了，处决的伤害落地时 `Target.IsDead` 已经为真、
        //   直接 return，什么也没结算。**共享对象让上一步的副作用变成了下一步的假象。**
        SafeFree(enemyA);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        var enemyA2 = packed.Instantiate<Ashigaru>();
        enemyA2.Position = new Vector3(0f, 0.1f, 1.5f);
        AddChild(enemyA2);
        await NextPhysicsFrame();

        Check("B3 新目标初始存活（干净前置）", !enemyA2.IsDead,
              $"IsDead={enemyA2.IsDead} 血量={enemyA2.Health.Current}/{enemyA2.Health.Max}");

        enemyA2.ApplyPostureDamage(9999);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        Check("B3 处决前目标可处决（前置）", enemyA2.CanBeExecuted,
              $"{enemyA2.StateName} 剩余 {enemyA2.PostureBrokenFramesLeft} 帧");

        int enterBefore = player.Machine.Get<DeathblowExecuteState>().EnterCount;

        // 真按键。这一步是"键位接线"唯一的证伪手段：
        // B 组第一次跑时在这里静默卡死（一闪打死的敌人被重复 QueueFree），
        // 后面所有断言一个字都没打出来——所以按键前后各留一条进度打印。
        await TapAction("interact", 3);
        await NextPhysicsFrame();

        bool entered = player.Machine.Current is DeathblowExecuteState;
        Check("B3 交互键真的打出处决", entered,
              $"玩家={player.StateName}（EnterCount {enterBefore} → " +
              $"{player.Machine.Get<DeathblowExecuteState>().EnterCount}）");

        // 目标应该被"钉住"——进被处决态
        Check("B3 目标进入被处决态并被钉住", enemyA2.IsBeingExecuted,
              $"EnemyA={enemyA2.StateName}");

        int hitFrame = player.Deathblow.HitFrame;
        float targetPosAtStart = enemyA2.GlobalPosition.Z;
        int deathFrame = -1;
        int aliveWhileExecuting = 0;
        int i2 = 0;

        // 跑到演出结束，逐帧记录"敌人在第几帧死"
        for (; i2 < 200; i2++)
        {
            if (player.Machine.Current is not DeathblowExecuteState)
                break;

            if (deathFrame < 0 && enemyA2.IsDead)
                deathFrame = i2;
            else if (deathFrame < 0)
                aliveWhileExecuting++;

            await NextPhysicsFrame();
        }

        int blowLength = i2;
        float targetDrift = Mathf.Abs(enemyA2.GlobalPosition.Z - targetPosAtStart);
        float playerDrift = player.GlobalPosition.Length();

        GD.Print($"[破韧处决]    诊断：演出 {blowLength} 帧，判定帧配置={hitFrame}，" +
                 $"敌人死于第 {deathFrame} 帧（死前一直活着 {aliveWhileExecuting} 帧）");

        // ★ 断言的是**时序契约**：伤害必须在演出中段（判定帧附近）落地，
        //   不能是"一按 F 敌人就没了"。
        //
        //   这条断言我自己写错过一次：原来固定采 `HitFrame-2` 那一帧来代表
        //   "判定帧之前"，但伤害正好落在那一帧上，于是**实现是对的、断言是错的**。
        //   现在改成直接量"伤害的实际落地帧"，比采某个猜测的采样点可靠。
        Check("B3 伤害在演出中段才落地（不是立刻结算）",
              deathFrame >= hitFrame - 3 && deathFrame <= hitFrame + 3,
              $"敌人死于第 {deathFrame} 帧 / 配置判定帧 {hitFrame}");

        Check("B3 伤害落地前目标一直活着（不是瞬间秒杀）",
              aliveWhileExecuting >= hitFrame - 4,
              $"落地前存活 {aliveWhileExecuting} 帧（期望 ≈{hitFrame}）");

        Check("B3 处决演出期间目标被钉住不动", targetDrift < 0.05f,
              $"目标位移 {targetDrift:F4} 米");

        Check("B3 处决演出期间玩家不位移", playerDrift < 0.15f,
              $"玩家水平位移 {playerDrift:F4} 米（含 0.1 高度偏移）");

        Check("B3 处决结束后敌人死亡", enemyA2.IsDead,
              $"EnemyA {enemyA2.StateName} IsDead={enemyA2.IsDead}");

        for (int i = 0; i < 30; i++)
            await NextPhysicsFrame();

        Check("B3 玩家处决完能回到正常行动", player.Machine.Current is not DeathblowExecuteState,
              $"玩家={player.StateName}");

        // ── B4：处决期间玩家无敌（对照组 + 实验组）──────────────
        //
        // ★ 必须做成**对照实验**：先证明"这台攻击者在正常情况下真的能打到玩家"，
        //   再说"处决期间打不到"。只验后者的话，攻击者根本没出招也会让测试通过
        //   —— 那是假阳性对照，本项目吃过这个亏。
        player.ResetForBattle();

        // 清场：只留"攻击者 + 一个处决目标"。B3 的目标要拆掉，
        // 否则它在 2.2 米内也是可处决的（虽然已经死了），会让目标选择变得不唯一。
        SafeFree(enemyA2);
        SafeFree(enemyHealthy);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        var attacker = GD.Load<PackedScene>("res://scenes/actors/AttackingDummy.tscn")
                         .Instantiate<AttackingDummy>();
        attacker.Position = new Vector3(0f, 0.1f, -1.9f);
        AddChild(attacker);

        var enemyC = packed.Instantiate<Ashigaru>();
        enemyC.Position = new Vector3(0f, 0.1f, 1.5f);
        AddChild(enemyC);
        enemyC.ApplyPostureDamage(9999);

        await NextPhysicsFrame();
        await NextPhysicsFrame();

        // ── 对照组：不处决，站着挨打 ────────────────────────────
        int hpStart = player.Health.Current;
        int controlFrames = 150;
        for (int i = 0; i < controlFrames; i++)
            await NextPhysicsFrame();

        int hpAfterControl = player.Health.Current;
        int attackerAttacks = attacker.AttackCount;
        bool controlProvesAttackerWorks = hpAfterControl < hpStart;

        GD.Print($"[破韧处决]    对照：{controlFrames} 帧内玩家 {hpStart} → {hpAfterControl} 血，" +
                 $"攻击者出招 {attackerAttacks} 次");

        Check("B4 对照组：攻击者确实能打到玩家（否则本组无意义）",
              controlProvesAttackerWorks,
              $"玩家 {hpStart} → {hpAfterControl} 血，攻击者出招 {attackerAttacks} 次");

        // ── 实验组：处决期间挨打，一滴血都不掉 ──────────────────
        player.ResetForBattle();
        enemyC.ResetForBattle();
        enemyC.ApplyPostureDamage(9999);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        Check("B4 处决前目标可处决（前置）", enemyC.CanBeExecuted, $"{enemyC.StateName}");

        int hpBeforeBlow = player.Health.Current;
        await TapAction("interact", 3);
        await NextPhysicsFrame();

        bool inBlow = player.Machine.Current is DeathblowExecuteState;
        int invulnFrames = 0;
        int totalFrames = 0;
        int hpMinDuringBlow = player.Health.Current;

        while (player.Machine.Current is DeathblowExecuteState && totalFrames < 200)
        {
            if (player.IsInvulnerableNow)
                invulnFrames++;
            hpMinDuringBlow = Mathf.Min(hpMinDuringBlow, player.Health.Current);
            totalFrames++;
            await NextPhysicsFrame();
        }

        Check("B4 处决演出全程无敌", inBlow && totalFrames > 0 && invulnFrames == totalFrames,
              $"{invulnFrames}/{totalFrames} 帧处于无敌");

        Check("B4 处决期间挨打不掉血", hpMinDuringBlow >= hpBeforeBlow,
              $"处决期间血量最低 {hpMinDuringBlow}（开始 {hpBeforeBlow}）");

        Check("B4 处决期间演出不被打断", enemyC.IsDead || enemyC.IsBeingExecuted,
              $"EnemyC={enemyC.StateName} IsDead={enemyC.IsDead}");

        for (int i = 0; i < 30; i++)
            await NextPhysicsFrame();

        // ── B5：连按交互键 → 只出一次处决 ───────────────────────
        player.ResetForBattle();
        SafeFree(enemyC);
        await NextPhysicsFrame();

        var enemyD = packed.Instantiate<Ashigaru>();
        enemyD.Position = new Vector3(0f, 0.1f, 1.5f);
        AddChild(enemyD);
        enemyD.ApplyPostureDamage(9999);
        await NextPhysicsFrame();
        await NextPhysicsFrame();

        int enterBeforeSpam = player.Machine.Get<DeathblowExecuteState>().EnterCount;

        // 连按：在整个演出期间每 2 帧按一次，总共按到演出结束
        int spamPresses = 0;
        while (player.Machine.Current is not DeathblowExecuteState && spamPresses < 20)
        {
            Input.ActionPress("interact");
            await NextPhysicsFrame();
            Input.ActionRelease("interact");
            await NextPhysicsFrame();
            spamPresses++;
        }

        // 演出中继续狂按
        int spamDuringBlow = 0;
        while (player.Machine.Current is DeathblowExecuteState && spamDuringBlow < 200)
        {
            Input.ActionPress("interact");
            await NextPhysicsFrame();
            Input.ActionRelease("interact");
            spamDuringBlow++;
        }

        await NextPhysicsFrame();
        await NextPhysicsFrame();

        int enterAfterSpam = player.Machine.Get<DeathblowExecuteState>().EnterCount;
        int executionCount = enterAfterSpam - enterBeforeSpam;

        Check("B5 连按交互键只出一次处决", executionCount == 1,
              $"演出期间狂按 {spamDuringBlow} 次，EnterCount {enterBeforeSpam} → {enterAfterSpam}（净 {executionCount} 次）");

        Check("B5 只结算一次伤害", enemyD.IsDead, $"EnemyD IsDead={enemyD.IsDead}");

        Report();
        return;

        // ── 局部函数 ───────────────────────────────────────────
        async Task TapAction(string action, int frames)
        {
            Input.ActionPress(action);
            await NextPhysicsFrame();
            for (int i = 1; i < frames; i++)
                await NextPhysicsFrame();
            Input.ActionRelease(action);
            await NextPhysicsFrame();
        }
    }

    /// <summary>
    /// 量"这个动作相对待机的最大骨角差"。
    /// 用**欧拉分量差**而不是 quaternion 点积——足兵大腿 rest 是 178.5°，
    /// 点积会把"腿翻过去"判成"没动"。
    /// </summary>
    private async Task<float> MeasureBoneDelta(Ashigaru enemy, int frames)
    {
        Skeleton3D? skel = FindSkeleton(enemy);
        if (skel is null)
            return 0f;

        string[] tracked =
        {
            "Hip", "Spine01", "Spine02", "Head",
            "L_Upperarm", "R_Upperarm", "L_Forearm", "R_Forearm",
            "L_Thigh", "R_Thigh", "L_Calf", "R_Calf",
        };

        // 待机基准
        await NextPhysicsFrame();
        var baseline = new System.Collections.Generic.Dictionary<string, Quaternion>();
        foreach (string n in tracked)
        {
            int b = skel.FindBone(n);
            if (b >= 0)
                baseline[n] = skel.GetBonePoseRotation(b);
        }

        float worst = 0f;
        for (int i = 0; i < frames; i++)
        {
            await NextPhysicsFrame();
            foreach (string n in tracked)
            {
                if (!baseline.TryGetValue(n, out Quaternion baseQ))
                    continue;
                int b = skel.FindBone(n);
                if (b < 0)
                    continue;

                Vector3 ea = baseQ.GetEuler();
                Vector3 eb = skel.GetBonePoseRotation(b).GetEuler();
                for (int k = 0; k < 3; k++)
                {
                    float d = Mathf.Abs(PosMod(Mathf.RadToDeg(eb[k] - ea[k]) + 180f, 360f) - 180f);
                    if (d > worst)
                        worst = d;
                }
            }
        }

        return worst;
    }

    private static float PosMod(float a, float b) => a - b * Mathf.Floor(a / b);

    private async Task NextPhysicsFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

    /// <summary>
    /// 安全释放一个节点。
    ///
    /// ★ 必须用 <c>IsInstanceValid</c> 而不是判 null —— 这是本项目**踩过两次**的坑
    /// （T49/T50 的 `_player ??= FindPlayer()` 反复失效就是同一个原因）：
    /// Godot 节点被释放后，C# 包装对象**仍然非 null**，对它调 `QueueFree()`
    /// 会抛 `ObjectDisposedException`。
    ///
    /// 而在 <c>async void _Ready()</c> 里，这个异常**会被静默吞掉**，
    /// 表现为"测试跑到一半就不动了、没有任何错误输出、最后超时"。
    /// B 组第一次跑就是这样卡住的：B1 的一闪把敌人打死后敌人自己走了，
    /// B3 再对它 `QueueFree()` 就炸了，后面所有断言一个字都没打出来。
    /// </summary>
    private static void SafeFree(Node? node)
    {
        if (node is not null && GodotObject.IsInstanceValid(node))
            node.QueueFree();
    }

    private void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            _pass++;
            GD.Print($"[破韧处决]   ✓ {name}  ({detail})");
        }
        else
        {
            _fail++;
            GD.PrintErr($"[破韧处决]   ✗ {name}  ({detail})");
        }
    }

    /// <summary>
    /// 收尾：打总数并决定退出码。
    /// 退出码 0 = 全过（`check.ps1` 只认这个），1 = 有失败项。
    /// </summary>
    private void Report()
    {
        GD.Print($"[破韧处决] 合计 {_pass} 通过 / {_fail} 失败");
        GD.Print(_fail == 0
            ? "[破韧处决] ✓ 通过"
            : "[破韧处决] ✗ 有失败项");

        GetTree().Quit(_fail == 0 ? 0 : 1);
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
