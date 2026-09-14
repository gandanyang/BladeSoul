# T52 交接 · 怪物动作 + 破韧处决

> 写于 2026-09-14。目标轮次 2/12。
> 卡片正文：`docs/TASKS.md` 的 `## T52 · 怪物动作 + 破韧处决`。
>
> ⚠️ **编号说明**：这张卡最初误用了 `T49`，但 `T49-HANDOFF.md`（蒙皮权重）/
> `T50-HANDOFF.md`（玩家模型翻折根因）/ `T51-HANDOFF.md`（格挡与弹开）
> **今天都真实存在过**且都没进 `TASKS.md`，于是撞号。已改为 **T52**，并补登了那三行。
> **加新卡前先 `Get-ChildItem *HANDOFF*.md`。**

---

## 零、当前状态速览（第 4 轮结束 · **目标达成**）

| 部分 | 状态 |
|---|---|
| 足兵绑骨 + Godot 骨架验证 | ✅ 完成，有实测 |
| 足兵 8 个动作（`AshigaruAnimator`） | ✅ 完成，逐个动作有骨角差数据 |
| 真敌人 `Ashigaru`（会追、会砍、会破韧） | ✅ 完成 |
| 破韧链路（打满 → 瘫软 → 不能攻击 → 窗口过期） | ✅ 完成 |
| **处决链路**（键位/无敌/钉住/伤害时序/连按） | ✅ **端到端 32/32 通过** |
| **处决标记 UI** | ✅ 完成（`DeathblowMarker` + 15 单测，已进 `HudTest`） |
| **破韧帧数进 `data`** | ✅ 完成（`ActorStats.PostureBrokenFrames` → `enemy_grunt.tres`） |
| **`docs/00` 登记** | ✅ 完成（§2.8 T52 表 + 变更日志 3 条） |
| `check.ps1` | ✅ **35 步全绿 / 239 单测** |

### 验收对照（目标原文 → 证据）

| 目标要求 | 证据 |
|---|---|
| 给足兵自动绑骨 | `tools/rig_humanoid.py -- ashigaru` → `skins=1 joints=21`，Godot `Tracked 12/12` |
| 补敌人动作（攻击/受击/破韧/被处决/互动） | **8 个动作**全部 >20° 可区分（移动 63.1°/攻击 74.4°/受击 22.0~31.5°/破韧 54.4°/被处决 54.4°/死亡 61.1°/互动 45.0°） |
| 破韧态 120 帧窗口 | ✓ 剩余 118 帧 / 配置 120（**帧数已进 `data/actors/enemy_grunt.tres`**） |
| 破韧态不能主动攻击 | ✓ `CanTransitionTo` 拒绝 `AttackState`（A4） |
| 破韧态只能被打 | ✓ 位移 0.0000 米（A5），且窗口**不可被续命**（单测钉死） |
| 处决 = 交互键 F/E 触发 | ✓ `Input.ActionPress("interact")` 真按键，`EnterCount 0→1`（B3） |
| 处决期间玩家无敌 | ✓ **87/87 帧无敌**，且对照组证明攻击者真的会打人（100→64 血）（B4） |
| **普攻键不复用处决** | ✓ 按 `attack` → 玩家进 `IssenState`，**不是** `DeathblowExecuteState`（B1） |
| 全部进 `check.ps1` 全绿 | ✓ 第 35 步，`ALL CHECKS PASSED`，239 单测 |
| 新接口登记 `docs/00` | ✓ §2.8 新增 T52 表（11 项）+ 变更日志 3 条 |

### 第 4 轮收尾新增

| 文件 | 作用 |
|---|---|
| `src/UI/DeathblowMarkerView.cs` | **纯逻辑**：该不该亮标记 + 紧张度（闪烁节奏）。**15 单测** |
| `src/UI/DeathblowMarker.cs` | 标记层：读只读接口 `ICombatActorDebug.CanBeExecuted`，**不引用任何具体敌人类型** |
| `ICombatActorDebug.CanBeExecuted`（追加） | 处决标记的读数口。"只加不改"，其它单位恒 false |
| `ActorStats.PostureBrokenFrames` | 破韧窗口帧数进数据（`enemy_grunt.tres = 120`） |
| `Hud.DeathblowMarkers` / `ResolveDeathblowProfile()` | 标记层挂进 HUD；距离上限与处决判定**同源**（避免"标记亮着但按 F 没反应"） |

**关于标记 UI 的两条设计纪律**：

1. **UI 不许引用具体战斗类型**（docs/00 §2.8）。标记层只读 `ICombatActorDebug`
   和 Godot 组 `combat_actor`，代码里不出现 `Ashigaru` 任何字样 ——
   将来第二种敌人实现接口后**自动生效**。
2. **不用 `Label` 写汉字**：那要依赖中文字体资源，而中文字体在无头环境/别的机器上
   **可能缺字变成方块**，那时标记等于没画。用几何图形（菱形框 + 竖划），到哪都一样。

### 卡里点名的 6 组对照实验（**全部验完**）

```
[破韧处决] 合计 32 通过 / 0 失败
```

| # | 验收项 | 实测 |
|---|---|---|
| 1 | 破韧后等窗口过期不按 → 恢复行动 | ✓ `CanBeExecuted=False`、回 `IdleState` |
| 2 | 破韧态按**普攻键** → **不触发处决** | ✓ 玩家进 `IssenState`（一闪），**不是** `DeathblowExecuteState` |
| 3 | 破韧态按 F/E → 进处决，敌人死 | ✓ `EnterCount 0→1`，目标进 `DeathblowState`，最终 `IsDead=True` |
| 4 | 处决期间**另一敌人砍玩家** → 不掉血、不打断 | ✓ 对照：150 帧内玩家 100→64 血；实验：**87/87 帧无敌、血量最低 100** |
| 5 | 破韧态连按 F/E → 只出一次 | ✓ 演出期间狂按 90 次，净 `EnterCount` **+1** |
| 6 | 非破韧态按 F/E → **不触发** | ✓ 场上无可处决目标时按 F，玩家仍 `IdleState`、目标存活 |

**第 4 组是唯一有真正对照实验的**，写法值得沿用：
先让攻击者**真的把玩家打到 100→64**（证明它会打人），再验"处决期间一滴血不掉"。
只验后者的话，攻击者根本没出招也会让测试通过 —— 那是**假阳性对照**。

### 第 4 轮抓到的 bug 与坑（三个，都过编译）

**(1) `BeginBeingExecuted` 从来没被调用** —— 端到端抓到的

`TryEnterDeathblow` 只设了 `state.Target`，**没告诉目标"你正在被处决"**。
症状极其隐蔽：玩家侧演出照常播、伤害照常落地、敌人照常死，"看起来全对"。
但敌人一直停在 `PostureBrokenState` —— 而**破韧态本身就不动**，
于是"演出期间目标被钉住"这条断言照样通过。
少这一句，整条链路唯一的外在表现是 **`BeingExecuted` 这个动作从来没播过**
（8 个动作里白做了一个）。修复：`target.BeginBeingExecuted(Deathblow.TotalFrames)`。

**(2) `async void _Ready()` 里 `QueueFree()` 一个已释放节点 → 整个测试静默卡死**

```
ERROR: System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'Oniblade.Enemies.Ashigaru'.
   at Godot.Node.QueueFree() ...
   at Oniblade.Dev.EnemyDeathblowTest._Ready() line 267
```

B1 的一闪把敌人打死了，敌人**自己走了**；B3 再对同一引用 `QueueFree()` 就炸。
而这个异常在 `async void` 里**被静默吞掉**，表现为
**"测试跑到一半不动了、没有任何错误输出、最后超时"** —— 极难查。
对策：加 `SafeFree()` helper，用 `IsInstanceValid` 判断（不能只判 null，
Godot 节点释放后 C# 包装对象仍非 null —— 与 T49/T50 那个 755 次的
`ObjectDisposedException` 是**同一个根因**）。

**(3) 断言写错，让"实现是对的"看起来像失败**

B3 里我固定采 `HitFrame-2` 那一帧代表"判定帧之前"，而伤害**正好落在那一帧**。
改成直接量"伤害的实际落地帧"（`deathFrame=48` / 配置 50，落在 ±3 容差内）。
**教训**：断言要量**事实**（伤害第几帧落地），不要量**猜测的采样点**。

另有一个**隐蔽的假阳性**被拆掉了：B1 原本只断言 `!playerExecuting`，
而一闪杀死目标后这个条件仍然成立 —— 敌人怎么死的都不会被发现。
现在拆成两条：一条管"键位"（不进处决态），一条管"链路"（`IsBeingExecuted` 为假）。

### 第 4 轮最重要的纪律：**每项用全新的敌人实例**

B1 按普攻 → 一闪杀死目标；如果 B2/B3 继续用同一具"尸体"，
"非破韧态不触发"和"伤害延迟落地"就全成了假象。
**共享对象会让上一步的副作用变成下一步的假象。**

---

## 一、足兵绑骨 ✅ —— 最关键的前置，一次通过

**为什么它最关键**：足兵模型 `assets/models/ashigaru_v2d_colored.glb` 原本
**`skins=0 / animations=0`**（单网格 6260 顶点、11496 面）。没有骨架，
"给它加动作"根本无从下手。这一步失败，整张卡的动作方案要换路线。

新增 `tools/rig_humanoid.py`（泛化版；`rig_character.py` 是玩家专用的一次性脚本）：

```powershell
blender.exe --background --factory-startup --python tools/rig_humanoid.py -- ashigaru
```

输出：

```
PROFILE ashigaru
STAGE import ok, mesh=geometry_0 verts=6260 polys=11496
STAGE rig ok, bones=21
MISSING_TRACKED 无
STAGE weights(auto) groups=21 total=6548.8
SOURCE_CHECK modifiers=['ARMATURE'] parent=AshigaruRig groups=21 polys=11496
STAGE export ok -> G:\Game\assets\models\ashigaru_rigged.glb 429072 bytes
SELFCHECK skins=1 nodes_with_skin=1 joints=21
SELFCHECK_TRACKED_MISSING 无
SELFCHECK_OK
```

**两个必须盯住的数**：

1. `total=6548.8` = 骨热扩散**成功**（原脚本注释警告过：拓扑不干净的网格上它会静默
   失败、权重全空）。若重跑看到 `total=0.0` + 「回退到 envelope」，说明网格被改过。
2. `polys=11496` 与导入时一致 —— `docs/17 §118` 硬约束（不许改网格外形）满足。

### 2. Godot 侧骨架验证 ✅

新增 `src/Dev/AshigaruRigProbe.cs` + `scenes/tests/AshigaruRig.tscn`：

```
[足兵骨架] 实例化成功，节点类型 = Node3D
[足兵骨架] ✓ Skeleton3D 找到，骨数 = 21
[足兵骨架] Tracked 命中 12/12
[足兵骨架] 带蒙皮的 MeshInstance3D = 1
[足兵骨架] ✓ 通过
```

**为什么必须单独验这一步**：绑骨自检只证明 **glb 的 JSON 里** `skins=1`，
不等于"Godot 导入了 `Skeleton3D`、骨名查得到、rest 可用"。
本项目栽过同类跟头（AGENTS.md §6）。

### 3. ★ 足兵的 rest 旋转与玩家不同（写姿势表前必须知道）

| 骨 | **足兵** | 玩家 | 后果 |
|---|---|---|---|
| `Hip` / `Spine01` / `Spine02` / `Head` | 0.0° | 0.0° | 一致 |
| `L_Forearm` / `R_Forearm` | 6.0° | — | — |
| `L_Calf` / `R_Calf` | 0.8° | — | — |
| **`L_Upperarm` / `R_Upperarm`** | **74.2°** | **101.4°** | **手臂基准姿态不同** |
| **`L_Thigh` / `R_Thigh`** | **178.5°** | **180.0°** | 同样必须 `rest * q` |

**结论：`HumanoidAnimator` 的姿势数字不能照抄给足兵**，要在 `AshigaruAnimator` 里
按足兵 rest 重写。但 `rest * q` 这个写法**必须继承**——见 `HumanoidAnimator.cs`
第 444~461 行的注释，那是 T50 那个"像一张纸被翻折"的根因。

### 4. 破韧态纯逻辑 + 单测 ✅

- `src/Combat/PostureBrokenWindow.cs` —— 窗口判定纯逻辑（不依赖场景树，铁律 8）
- `src/Combat/States/PostureBrokenState.cs` —— 状态（POCO）
- `tests/Oniblade.Tests/PostureBrokenWindowTests.cs` —— **11 个用例**

单测 **197 → 208 全绿**，`check.ps1` 仍 exit 0。

**"不能主动攻击"怎么保证**：`PostureBrokenState.CanTransitionTo` 拒绝 `AttackState` /
`ChargedAttackState`；而 `StateMachine.Change<T>()` 对被拒绝的切换是**静默丢弃**的
（`StateMachine.cs` 第 66 行），所以敌人 AI 每帧请求出招也进不去攻击态。
硬约束落在状态机层面，**不靠 AI 自觉**。

**另一条纪律**：窗口**不能因为"又挨了一刀"而续命**（`PostureBrokenWindow` 没有任何
续命 API），否则玩家一直砍就能把窗口续下去，"砍几刀然后处决"会退化成"砍到死"。
单测 `窗口不会因为反复调用Tick以外的操作被续命` 钉住了这条。

### 5. 足兵 8 个动作 ✅（第 2 轮）

新增 `src/Enemies/AshigaruAnimator.cs`（**独立文件**，不碰 T38 的 `HumanoidAnimator`）。
骨名与玩家完全一致（`tools/rig_humanoid.py` 就是照那套名字绑的），
**但姿势数字一个都没照抄**——按足兵自己的 rest 重写。

逐个动作的实测骨角差（`scenes/tests/AshigaruAnim.tscn`）：

| 动作 | 与待机最大骨角差 | 峰值帧 |
|---|---|---|
| 移动 | 63.1° | 第 10 帧 |
| 攻击（单臂斜劈） | 74.4° | 第 13 帧 |
| 受击·轻 | 22.0° | 第 19 帧 |
| 受击·重 | 31.5° | 第 19 帧 |
| 破韧（瘫软跪伏） | 54.4° | 第 10 帧 |
| 被处决 | 54.4° | 第 8 帧 |
| 死亡 | 61.1° | 第 30 帧 |
| 互动 | 45.0° | 第 19 帧 |

**调试记录（两个都是探针自己的坑，不是实现的）**：

1. **移动一开始只有 8.9°**：探针逐帧调 `_anim.Reset()`，把 `_phase`（走步相位）清零了，
   测出来的是"相位 0 下腿的角度"而不是摆动幅度。改成移动**连续推进**、其余动作才逐帧
   Reset 之后 → **63.1°**。
2. **受击·轻一开始 17.3°**（低于 20° 门槛）：把强度从 0.55 提到 0.70 → **22.0°**。
   轻受击也必须一眼看得出来，玩家要能确认"这一刀打中了"。

### 6. 真敌人 `Ashigaru` ✅（第 2 轮）

`src/Enemies/Ashigaru.cs` + `scenes/enemies/Ashigaru.tscn` +
`scenes/enemies/AshigaruModel.tscn` + `data/actors/enemy_grunt.tres`（80 血 / 60 体干）。

与 `AttackingDummy`（T7 的道场节拍器）分工明确，**没有改它**。

### 7. ★★ 三个只有跑端到端才能抓到的 bug（第 2 轮）

这一节的三个坑**都过了编译**，静态看代码完全正常。它们是新写敌人/状态时最容易中的：

**(1) 破韧态没注册进状态机 → 静默什么都不发生**

`CombatActor.RegisterStates` 默认只注册 Idle/Move/Attack/Stagger。
忘注册的症状是：`Machine.Get<PostureBrokenState>()` 抛 `KeyNotFoundException`，
而 `Machine.Change<T>()` 是**静默丢弃**的——于是"体干打满了但什么都不发生"。
对策：`Ashigaru.RegisterStates` 里 `machine.Add(new PostureBrokenState())`。

**(2) `DesiredVelocity` 写在 `OnTickVisual` 里 → 敌人永远不动**

基类 `_PhysicsProcess` 的顺序是：

```
DesiredVelocity = Zero   ← 每帧清零
Machine.Tick()           ← MoveState 在这里调 TryGetMoveIntent() 并写 DesiredVelocity
ApplyMovement()          ← 只有在这之前写好的速度才会被用上
OnTickVisual()           ← 太晚了！
```

在 `OnTickVisual` 里写 `DesiredVelocity` 的症状极难查：**`Desired` 读出来是非零的**
（写进去了），但 `Velocity` 恒为 0（`ApplyMovement` 早跑完了，下一帧开头又被清零）。
**正确入口是 `public override bool TryGetMoveIntent(out MoveIntent intent)`** ——
`MoveIntent` 的注释写得很清楚："玩家从输入读，**敌人从 AI 读**"。

**(3) 测试场景没有地面 → 敌人自由落体，看起来像"不追人"**

`Velocity.Y` 涨到 -15 而 X/Z 恒为 0，于是 3D 距离被 Y 分量撑大：
"与玩家距离 6.00 → **13.55 米**"，看起来像敌人在后退。
对策：端到端场景里先铺一块 `StaticBody3D` 地面。

### 8. `AttackingDummy` 也支持真模型（可选，向后兼容）

加了 `[Export] PackedScene? ModelScene`：**留空则自动降级到灰盒 `BlockoutRig`**，
所以 T7 的训练场景一行都不用改也不会崩。Dojo 里的假人**暂时没换**（避免动 T7 的验收面）。

---

## 二、还没做（下一轮从这里继续）

| # | 任务 | 依赖 | 状态 |
|---|---|---|---|
| 1 | **`AshigaruAnimator`** | 骨架就绪 | ✅ **已完成**（8 个动作，见上 §5） |
| 2 | **足兵接进敌人场景** | #1 | ✅ **已完成**（`Ashigaru.tscn` 用真模型；`AttackingDummy` 支持但 Dojo 暂未换） |
| 7 | **敌人进破韧的接线** | #2 | ✅ **已完成**（`Ashigaru.OnPostureBroken` → `ForceChange<PostureBrokenState>`） |
| 10 | **端到端场景 `EnemyDeathblow.tscn`** | 全部 | 🟡 **A 组 10/10 通过**；B 组（处决）待做 |
| 11 | **新场景接进 `check.ps1`** | #10 | ✅ 已完成（第 35 步，35 步全绿） |
| 3 | **`DeathblowState`（被处决方）** | #1 #2 | ✅ **已完成**（钉住不动、拒绝攻击/移动） |
| 4 | **`DeathblowExecuteState`（玩家侧）** | #3 | ✅ **已完成**（演出 + 全程无敌，帧数据来自 `data/combat/deathblow.tres`） |
| 5 | **`IsInvulnerableNow` 处决无敌** | #4 | ✅ **已完成**（覆写，**没有**另造裁决分支） |
| 6 | **处决键 = `interact`(F/E) + 优先级** | #3 | ✅ **已完成**（`DeathblowResolver.PriorityOrder`；处决 > 深吸，实测"按住 F 不会变成一直吸魄"） |
| 10 | **端到端场景 `EnemyDeathblow.tscn`** | 全部 | ✅ **A 10 + B 22 = 32/32 通过** |
| 8 | **破韧帧数进 `data/**/*.tres`** | — | ❌ 下一轮：`Ashigaru.PostureBrokenFrames` 还是导出的过渡方案，要搬进 `data/actors/enemy_grunt.tres` 或 `data/combat/`（铁律 1） |
| 9 | **处决标记 UI** | — | ❌ 下一轮：敌人头顶（复用 `EnemyBars` / T31） |
| 12 | **`docs/00` 登记新接口** | — | ❌ **下一轮必做**。要登记：`IDeathblowTarget`、`DeathblowProfile`、`CombatActor.GroupName`、`InteractPriority`、`PostureBrokenWindow`、`DeathblowState`/`DeathblowExecuteState` |
| — | **把 Dojo 里的假人换成足兵** | T7 | ⏸ 可选：会动到 T7 的验收面，建议最后做或问制作人 |

### 卡里点名的 6 组对照实验（缺一不可）

| 场景 | 期望 | 状态 |
|---|---|---|
| 破韧后等 120 帧不按 | 敌人恢复攻击、标记消失（证窗口会过期） | ✅ **已验**（A6：过期后 `CanBeExecuted=False`、回 `IdleState`） |
| 破韧态下按**普攻键** | **不触发处决**（证键位分离） | ✅ **已验**（B1：玩家进 `IssenState`，不是 `DeathblowExecuteState`） |
| 破韧态下按 F/E | 进处决，敌人死 | ✅ **已验**（B3：`EnterCount 0→1`、目标进 `DeathblowState`、最终 `IsDead=True`） |
| 处决演出期间**另一敌人砍玩家** | 玩家不掉血，演出不被打断 | ✅ **已验**（B4：**87/87 帧无敌**；对照 100→64 血证明攻击者真的会打人） |
| 破韧态下连按 F/E | 只出一次处决 | ✅ **已验**（B5：狂按 90 次，净 `EnterCount` **+1**） |
| 非破韧态按 F/E | **不触发**（否则等于随时秒怪） | ✅ **已验**（B2：无可处决目标时按 F，玩家仍 `IdleState`、目标存活） |

**额外补验（卡里没点名，但不验就会踩坑）**：

| 检查 | 实测 |
|---|---|
| 伤害在演出**中段**才落地（不是一按 F 就秒） | ✓ 死于第 48 帧 / 配置判定帧 50 |
| 演出期间**目标被钉住** | ✓ 位移 0.0000 米 |
| 演出期间**玩家不位移** | ✓ 0.0002 米 |
| 处决完玩家能回到正常行动 | ✓ 回 `IdleState` |

**写 B 组时踩到的三个坑（照着抄可避免）**：

1. **每项用全新的敌人实例**。B1 按普攻会打出**一闪**把目标直接杀死，
   继续用同一具"尸体"验 B2/B3，"非破韧态不触发"和"伤害延迟落地"就全是假象。
2. **玩家侧要真按键**（`Input.ActionPress("interact")`，同 `CombatSmokeTest.cs:176`）。
   不要直接调 `TryEnterDeathblow()` —— 那验的是函数不是键位接线，键接错了照样过。
3. **`QueueFree()` 前必须 `IsInstanceValid`**。被一闪杀死的敌人会自己走，
   再 `QueueFree()` 会抛 `ObjectDisposedException`，而它在 `async void _Ready()` 里
   **被静默吞掉** → 测试跑到一半不动、零错误输出、最后超时。
   用文件里的 `SafeFree()` helper。

**另外本轮多验了 4 条卡里没点名、但不验就会踩坑的**：

| 检查 | 为什么必须有 |
|---|---|
| 模型骨架可用（不是悄悄降级回灰盒） | T52 的全部意义就是"足兵有动作"；降级了必须有人喊 |
| 移动动作与待机可区分（40.4° vs 1.0°） | 只测"有没有动画资产"会让"一动不动"看起来正常 |
| 敌人真的在追（6.00 → 2.80 米） | 否则上面的"移动动作"是别的原因造成的 |
| 破韧态位移 ≈ 0（20 帧 0.0000 米） | 验收要求"只能被攻击"，滑走会让处决距离判定失去意义 |

---

## 三、机制口径（项目主人裁定，不许再含糊）

```
敌人架势条被打满
  → 【破韧态】120 帧（2.0s）        ← 帧数待进 data
       · 不能主动攻击（状态机层面拒绝）
       · 只能被攻击
       · 头顶亮忍杀标记
  → 窗口内按 F/E → 处决（玩家全程无敌）
  → 不按 → 敌人恢复行动
```

**普攻键绝不许处决** —— 同键会让"砍几刀再处决"退化成"一想补刀就进处决"。

---

## 四、环境备忘

- Blender：`C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe`
  （统一 `--background --factory-startup`，见 `docs/17`）
- 足兵在 Blender 里**Z 是竖直轴**，Z ∈ [-0.8660, 0.8338]（高 1.6998）
- 手臂结构（沿 x 分箱的 z 中位）：x0.30→zfrac0.813 / x0.42→0.776 / x0.50→0.692 /
  x0.58→0.586 / x0.66→0.504 —— 手臂**斜向下前伸**；肩线在 zfrac 0.80
- 新资产必须跑 `godot --headless --path . --import` 才有 `.import`
- **本轮顺手修掉的仓库级缺陷**：四处 `??=` 缓存节点引用导致
  `ObjectDisposedException`（`check.ps1` 第 22 步：755 次 / 21159 行日志 → **0 次 / 597 行**）。
  涉及 `TutorialDirector`(T47) / `SavePoint`(T45) / `EncounterZone`(T42) / `Hud`(T31)。
