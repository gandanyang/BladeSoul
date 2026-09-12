# 任务看板

> 由制作人维护。**执行 agent 只认这份文档里的任务号**，做完把状态改成"待验收"，
> 由制作人验收后再改"完成"。
>
> 铁律（所有任务适用）：
> 1. 动手前读 [00-制作人决策记录](00-制作人决策记录.md)（接口冻结）与
>    [08-顶层设计评审](08-顶层设计评审.md)（当前优先级）。
> 2. **数值不许硬编码进 C#**，进 `data/**/*.tres`。帧相关字段一律带 `Frames` 后缀。
> 3. 改完必须跑 `powershell -NoProfile -File tools\check.ps1` 并全绿。
> 4. **不要执行 `git commit`**（多 agent 并行会抢索引锁，由制作人统一提交）。
> 5. **不要修改 `docs/00-制作人决策记录.md`**；需要改接口时在完成报告里写明，由制作人登记。

---

## 状态总览

| 编号 | 任务 | 负责 | 状态 | 依赖 |
|---|---|---|---|---|
| **T1** | 可玩闭环：战斗管线 + 三连击 + 木桩 | ~~执行 agent~~ → **制作人** | ✅ 完成（已验收，用户确认手感 OK） | — |
| **T2** | 调试面板 F1~F8 | 执行 agent（唯一成功的） | ✅ 完成 | 接口已冻结 |
| **T3** | 音频层 + 程序化占位音效 + 总线 | ~~执行 agent~~ → **制作人** | ✅ 完成 | 接口已冻结 |
| T4 | 修订 06/README 的口径（M5 = 1.0） | 制作人 | ✅ 完成 | — |
| T5 | `CombatTuning`（弹开窗唯一计算入口，08 §3 P1-3 红线） | 制作人 | ✅ 完成 | T1 |
| **T6** | **玩家格挡与弹开**（M1 核心） | 执行 agent | 🟡 待验收 | 接口已冻结 |
| **T7** | **挥砍假人**（会还手的靶子） | 执行 agent | 🟡 待验收 | 接口已冻结 |
| T8 | 死亡与重开协议（≤3 秒原地重开，08 §3 P1-2） | 待派发 | ⚪ 未开始 | T6 |
| T9 | `BossPhaseProfile` 数据层（08 §3 P2-2） | 待派发 | ⚪ 未开始 | — |
| **T10** | **连打防御惩罚覆盖中立态**（02 §8 裁定） | 制作人（agent 消息三次丢失） | ✅ 完成 | T6 |
| T11 | **弹开/一闪的职责边界**（02 §3 裁定：一闪应对一切，弹开只应对一般攻击） | 制作人 | ✅ 完成 | — |
| T12 | **危攻击预警 + 危·突刺枪兵** | 执行 agent | ✅ 完成（已验收） | T11 ✅ |
| T13 | **闪避（`DodgeState` + 无敌帧）** | 执行 agent | ✅ 完成（已验收） | T10 ✅ |
| T14 | **死亡与重开协议（≤3 秒原地重开）** | 执行 agent | ✅ 完成（已验收，实测 **60 帧 = 1.00 秒**） | — |
| T15 | **世界观 / 主角 / 反派 / 成长系统设定补充**（03 §2.5·§2.6·§6.4~6.7） | 制作人 | ✅ 完成 | — |
| T16 | `UpgradeTree` 升级数据资源（三条线 10 级，03 §6.4） | 🟢 可领取 | ⚪ 未开始 | T15 ✅ |
| T17 | `SoulWallet` + 侵蚀度接 `GameState`（03 §6.4·§6.6） | 🟢 可领取 | ⚪ 未开始 | T19 ✅ |
| T18 | **喝血 `HealState`**（02 §2.4） | 执行 agent | ✅ 完成（已验收） | — |
| T19 | 自动吸魂（魄牵引 + 连续吸魂反馈 + 深吸） | 制作人 | ✅ 完成 | — |
| **T20** | **一闪（真一闪 + 安全窗 + 特殊音效与动作）** | 🟡 可领取（**须等 T18 合并**） | ⚪ 未开始 | T18 |
| T21 | **删除 `PerfectDodgeGraceFrames`**（02 §8 裁定：它物理上不成立） | 🟢 可领取 | ⚪ 未开始 | — |
| T22 | 复活（`ReviveCount` 目前是**死配置**，但 05 难度表依赖它） | 🟢 可领取 | ⚪ 未开始 | — |
| T23 | `ActorId` 改为自动分配（手工分配**已经撞号两次**） | 🟢 可领取 | ⚪ 未开始 | — |
| **T24** | **动画管线打通**（Mixamo → 60fps → AnimationPlayer → 状态机驱动） | 🔴 **最高优先** | ⚪ 未开始 | — |
| T25 | 主角本体模型 + 骨架（含附加骨骼与材质槽分组） | 🟢 可领取 | ⚪ 未开始 | 09 定稿 |
| T26 | 噬魂笼手（独立骨骼 / 材质 / 侵蚀三阶段） | 🟡 依赖 T25 的骨架 | ⚪ 未开始 | T25 |
| T27 | 敌人模型：魔骸足兵 + 魔骸枪兵 | 🟢 可领取 | ⚪ 未开始 | 10 定稿 |
| T28 | 战斗特效（弹开火花 / 拼刀 / 血雾 / 一闪黑白闪） | 🟢 可领取 | ⚪ 未开始 | 10 §4 |

> **领取方式**：把对应卡片整段复制给执行 agent 即可，卡片是自包含的
> （规格、约束、验收、禁止改动的文件都写在里面）。

### M1 的唯一验收标准

> **"弹开成功时会想再试一次。"**（08 文档 §5）
> 它只能由人试出来——T6/T7 完成后必须真的上手玩一次，别只看测试是绿的。

---

## T1 · 可玩闭环（战斗管线 + 三连击 + 木桩）🔴 最高优先级

> **执行情况**：派发的执行 agent 被中断、零产出；由**制作人亲自完成**（17:00–17:20）。
> 验证：`tools\check.ps1` 全绿 = 89 单测 + 19 资源 + 端到端冒烟（木桩 HP 100→90，体干峰值 8）。

**目标**：让"人按下攻击键 → 屏幕上发生战斗反馈"这件事**第一次发生**。
这是 08 文档 §4 的红线 1。不要求动画、不要求好看、不要求敌人 AI。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/CombatActor.cs` | 抽象基类 `CharacterBody3D`，实现 `ICombatActorDebug`；持有 `HealthMeter` / `PostureMeter` / 顿帧 / `StateMachine` / 弹开窗计时 / 一闪 buff / 弹开连击数 |
| `src/Combat/States/ActorState.cs` `StateMachine.cs` | POCO 状态机，**延迟切换**（帧末统一处理，避免 Frame 被同帧重置） |
| `src/Combat/States/IdleState.cs` `MoveState.cs` | 移动层 |
| `src/Combat/States/AttackState.cs` | 三连（`ComboIndex` 1→2→3），帧数据驱动，读 `data/attacks/player/light_0*.tres` |
| `src/Combat/States/HitLightState.cs` | 受击小硬直 |
| `src/Combat/Hitbox.cs` | `Node3D` + `Shape3D`，**同步 `IntersectShape` 查询**，不用 Area3D 信号 |
| `src/Combat/Hurtbox.cs` | `Area3D`，`Monitoring=false` / `Monitorable=true`，只作为可被查询的标记 |
| `src/Core/CombatArbiter.cs` | 每帧：收集重叠 → 按 `(优先级, ActorId)` 排序 → 调 `CombatResolver.Resolve` → 应用结果 |
| `src/Player/PlayerActor.cs` | 玩家：输入 → `PlayerInputBuffer` → 状态机；攻击三连可打出 |
| `src/Dev/TrainingDummy.cs` | 木桩：无限血、体干不清零、受击时顿帧 + 音效 + 抖动 |
| `scenes/actors/Player.tscn` | 挂 `PlayerActor`（原 `PlayerController.cs` 请改造/替换，**不要留两套玩家控制**） |
| `scenes/actors/TrainingDummy.tscn` | 木桩 |
| `scenes/levels/Dojo.tscn` | 灰盒道场：地板 + 玩家 + 木桩 + `CombatArbiter`；设为 `run/main_scene` |

### 约束

- 招式数值**全部**读 `data/attacks/player/*.tres`（已存在 7 个），不许在代码里写伤害/帧数。
- 顿帧用**逐 actor 冻结**（`_hitStopFrames`），**不许用 `Engine.TimeScale`**。
- `_PhysicsProcess` 里禁用 `new List` / `new` 任何托管对象，用复用数组。
- 命中时必须 `EventBus.Instance?.RaiseHitResolved(...)`，并调
  `AudioDirector.Instance?.PlayCombat(CombatSfx.HitSlash)`（音频文件可能还不存在，静默即可）。
- `project.godot`：只允许改 `run/main_scene`。**不要碰 `[input]` 与 `[autoload]`。**
- 三连规则：后摇中段（`AttackData.CancelOpenFrame`）之后按攻击才接下一段；
  **攻击后摇不能被攻击键提前取消**（防无脑连打）。

### 验收标准

- [ ] `tools\check.ps1` 全绿（build + 59 → 更多单测 + 资源自检）
- [ ] 新增 xUnit 测试：`AttackData` 帧窗边界（前摇/判定/后摇/取消窗）+ 三连推进逻辑（纯逻辑部分）
- [ ] `godot --headless` 启动 `Dojo.tscn` 无 ERROR
- [ ] 代码里 `grep -n "Damage\s*=\s*[0-9]"` 不应出现在 `src/` 的战斗逻辑中（数值只能来自 `.tres`）
- [ ] 完成报告里给出：**怎么手动跑起来、按什么键、应该看到什么**

---

## T2 · 调试面板 F1~F8

**目标**：04 文档 §13 的面板。**M0 就做完**，因为后面每一行调参都靠它。

### 交付物

- `src/UI/DebugOverlay.cs`（`CanvasLayer`）+ `scenes/ui/DebugOverlay.tscn`
- 注册为 Autoload（在 `project.godot` **追加** `DebugOverlay="*res://src/UI/DebugOverlay.cs"`，
  **不要动 `[input]`、不要动已有的两个 autoload**）

### 功能

| 键 | 功能 |
|---|---|
| F1 | 开关面板 |
| F2 | 慢放 `Engine.TimeScale = 0.25` |
| F3 | 单帧步进（暂停 + 手动 tick 一次） |
| F4 | 玩家无敌 |
| F5 | 绘制 hitbox / hurtbox 线框 |
| F6 | 显示敌人 AI 状态 |
| F7 | 重开当前战斗 |
| F8 | 输出本场战斗统计到控制台 |

面板内容照 04 §13 的样例：STATE / WINDOWS / VITALS / ENEMY / LAST 10 VERDICTS / ANIM SYNC。

### 约束

- **只通过 `ICombatActorDebug` 接口和 `EventBus` 取数据**，不许引用 `CombatActor` 具体类型
  （T1 正在写它，你们必须能并行）。用 `GetTree().GetNodesInGroup("combat_actor")` 发现自己。
- 找不到任何战斗单位时，面板要能正常显示"无目标"，不许报错。
- F8 统计口径见 02 §11：弹开成功率 / 战斗时长 / 一闪次数 / 格挡破防次数。

### 验收标准

- [ ] `tools\check.ps1` 全绿
- [ ] `godot --headless` 无 ERROR（面板在无战斗单位时也能活着）
- [ ] 完成报告里给出面板的**实际截图或控制台输出样例**

---

## T3 · 音频层 + 程序化占位音效 + 总线

> **执行情况**：派发的执行 agent 两次都没有收到任务正文（回"没有收到请求"），零产出；
> 由**制作人亲自完成**（17:15–17:25）。
> 交付：`tools/gen_placeholder_sfx.ps1` 程序化生成 14 个音效（617KB，固定种子可复现）、
> `default_bus_layout.tres`（Master→Music/SFX→Combat·Ambience/Voice/UI）、
> `docs/CREDITS.md`、自检新增音效检查。
> 验证：`tools\check.ps1` 全绿，自检报告"音效 14 个，错误 0 项"。

**目标**：把"音效是手感的 50%"从口号变成可听的东西。02 §7 / 07 §2。

### 交付物

1. `src/Audio/AudioDirector.cs` —— 骨架已在，补完实现（加载、总线、`Sfx*` 音量）。
2. **程序化生成 14 个占位音效**到 `assets/audio/sfx/`，命名严格为
   `sfx_<snake_case>.ogg`？→ **改用 `.wav`**：改 `AudioDirector` 的路径扩展名为 `.wav`（占位期用 WAV，正式资产再换）。
   覆盖 `CombatSfx` 全部 14 个：`hit_slash` `hit_block` `deflect` `clash` `guard_break`
   `issen_slash` `issen_impact` `deathblow` `whoosh_light` `whoosh_heavy` `dodge_whoosh`
   `perilous_thrust` `perilous_sweep` `perilous_grab`。
   - 用 C# 合成（正弦 / 噪声 + 指数包络）写 `AudioStreamWAV` 或直接写 WAV 字节。
   - 音色要求：`deflect` 必须是**金属高频尖鸣**（这是全项目最重要的一个音）；
     `clash` 低频锵；`hit_slash` 短促切割；危攻击三种音效**只靠音高区分**（高/中/低）。
3. `tools/gen_placeholder_sfx.ps1`（或 `.cs`）——**可重复生成**，不许手工做一次性的二进制。
4. `default_bus_layout.tres`（项目根目录）——总线：
   `Master → Music / SFX(→Combat / Ambience) / Voice / UI`（07 §6）。
   **不要改 `project.godot`。**

### 约束

- 生成的 WAV 必须进 git（它们是可重建的，但要让别人 clone 下来就能听）。
- 单文件时长 ≤ 1.5 秒，采样率 44100，16-bit mono。
- `AudioDirector` 的公共 API **不许改签名**（T1 正在调用它）：
  `PlayCombat(CombatSfx, float pitchScale = 1f, float volumeDb = 0f)` / `ResetDeflectChain()` / `DeflectPitchScale`。

### 验收标准

- [ ] `tools\check.ps1` 全绿
- [ ] 14 个 `.wav` 全部存在且能被 `Godot` 加载（写进 `SelfTest` 的资源检查）
- [ ] 完成报告里列出每个音效的**波形设计参数**（频率/包络/时长），便于后续替换

---

## 发布流程（制作人）

1. agent 完成 → 状态改「待验收」→ 报告交付物与验证结果
2. 制作人跑 `tools\check.ps1` + 读代码 + 对照验收标准
3. 通过 → 制作人 `git commit`；不通过 → 打回并写明原因

---

## T6 · 玩家格挡与弹开（M1 核心）

**背景**：弹开是本项目的立命之本（02 §2.2 / 08 §2.2）。
玩家侧现在只有 `CombatActor.IsGuarding` 与 `OpenDeflectWindow()` 两个空槽，没有任何状态驱动它们。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/States/GuardState.cs`（新） | 按住防御的持续状态 |
| `src/Combat/States/DeflectState.cs`（新） | 弹开成功后的 12 帧特殊状态 |
| `src/Player/PlayerActor.cs`（改） | 覆写 `RegisterStates` 注册上面两个状态；覆写 `OnVerdictReceived` 在弹开时切 `DeflectState`；防御时给移动意图降速 |

### 规则（全部来自 02 文档，不许自己发明）

1. **进入**：按住 `guard` 键 → 进入 `GuardState`。
2. **弹开窗**：进入防御的**那一帧**调 `Actor.OpenDeflectWindow(...)`；
   窗口帧数**必须**由 `CombatTuning.ResolveDeflectWindowFrames(Difficulty.DeflectWindowFrames)` 算出
   （已存在，纯函数 + 单测）。**不许直接读 `Difficulty.DeflectWindowFrames` 去用。**
3. **取消硬直**：从其他状态（攻击/受击）取消进入防御时，前
   `Difficulty.GuardCancelLockFrames` 帧**不打开弹开窗**——
   但这几帧**仍然算格挡**（`IsGuarding = true`）。
   这就是"惩罚只惩罚效率，不惩罚存活"，也是防"防御键连打"的手段。
4. **格挡姿态**：`IsGuarding = true`；移动速度降到约 40%；攻击键不生效。
5. **退出**：松开 `guard` → `IsGuarding = false` → 回 `IdleState`。
6. **弹开成功**：`CombatActor.ReceiveVerdict` 已经做完了顿帧、授予 `IssenKind.Deflect` buff、
   累加连击数。你要做的只是在 `PlayerActor.OnVerdictReceived` 里
   `Machine.ForceChange<DeflectState>()`。
7. **`DeflectState`**：12 帧，保持防御姿态、不接受移动输入；结束后回 `IdleState`
   （若仍按住 guard 则回 `GuardState`）。

### 硬约束

- 数值只允许来自 `data/difficulty/*.tres`（窗口宽度、取消硬直）和这一个常量（12 帧弹开状态）。
- 音效不用你管：`AudioDirector` 订阅了 `EventBus.HitResolved`，会自动播放并做音高递增。
- **不要改** `CombatActor.cs` / `CombatResolver.cs` / `CombatTuning.cs`（接口已冻结）。
  确实需要改就在报告里写明，由制作人改。
- **不要碰** `src/Enemies/`、`src/UI/`、`project.godot`。
- 不要执行 `git commit`。

### 验收标准

- [ ] `powershell -NoProfile -File tools\check.ps1` 全绿
- [ ] 新增 xUnit 测试：覆盖"取消硬直期内不弹开但仍格挡"与"窗口内弹开"（抽成纯逻辑再测）
- [ ] `godot --headless` 无 ERROR
- [ ] 完成报告：怎么手测（按什么键、该看到什么）、验证输出、偏离规格处

---

## T7 · 挥砍假人（会还手的靶子）

**背景**：现在的木桩不还手，**弹开根本没法测**——没有攻击可以弹。
05 文档 §4.4 把"攻击模式固定的假人"列为道场第一个训练模块，这就是它。

### 交付物

- `src/Enemies/AttackingDummy.cs`（新，继承 `CombatActor`）
- `scenes/actors/AttackingDummy.tscn`（新）
- `scenes/levels/Dojo.tscn`（改：把其中一个木桩换成挥砍假人）

### 行为

1. 站桩不追人，但**始终转向玩家**。
2. 玩家进入攻击距离（约 2.2m）且冷却结束后发动攻击。
3. 攻击**复用现成的 `AttackState`**：在 `OnActorReady` 里
   `Machine.Get<AttackState>().Configure(new[] { Attack })`，
   `Attack` 是 `[Export] AttackData?`，在场景里指向
   `res://data/attacks/enemies/grunt_slash.tres`（前摇 24 / 判定 4 / 后摇 30）。
4. 发动时机：覆写 `public override bool WantsToAttack()`
   （这个钩子已经给你加好了，见 `CombatActor`），返回
   `冷却结束 && 玩家在距离内`。冷却 = `[Export] int AttackIntervalFrames`，默认 **60 帧**
   （02 §10 要求两次攻击间隔 ≥45 帧）。
5. 攻击结束回 `IdleState`，冷却开始计时，如此循环。
6. **不得读玩家输入**（02 §10 铁律）：不能因为玩家按了攻击就取消自己的前摇。

### 硬约束

- 帧数据只来自 `.tres`，代码里不许写前摇/后摇秒数。
- **不要改** `CombatActor.cs` / `src/Combat/States/*` / `PlayerActor.cs` / `src/UI/` / `project.godot`。
- 假人**不该被打死**（`Invincible = true`），它是训练靶。
- 不要执行 `git commit`。

### 验收标准

- [ ] `tools\check.ps1` 全绿
- [ ] `godot --headless` 无 ERROR
- [ ] **可自证的证据**：无头跑 ≥300 帧，打印出招次数与每次出招的帧号
      （证明它确实在按节奏出招，而不是"编译过就算"）
- [ ] 完成报告：怎么手测、验证输出、偏离规格处

---

## T10 · 连打防御惩罚覆盖中立态（02 §8 裁定）

> **执行情况**：`followup_task` 到已完成的 agent **连续三次没能送达**（T3 两次、T10 一次），
> 且最后确认 T7 实际是"晚到"并**并行改写了同一批文件**（重复定义了 `GuardReentryLockFrames`、
> 各写了一套 helper）。最终由**制作人完成**，agent 的那份实现被收敛掉。
> **教训：激活一个已完成的 agent 是竞态，不是排队。**
> 确认它在动之前，不要自己动手；动手前先 `git status` + 看文件 mtime。
>
> 实测（`DeflectTraining.tscn` vs `GuardSpam.tscn`）：
>
> | 机器人 | 弹开 | 格挡 | 挨打 |
> |---|---|---|---|
> | 看时机按 | **4** | 0 | 0 |
> | 连打 | **0** | 3 | 1 |

**背景**：T6/T7 完成后，T7 用无头机器人实测发现：**从站立反复点按防御，每次都会重新开一次弹开窗**。
把机器人改成"完全不看时机、每 4 帧松一下再按一下"，结果仍然是 **弹开 4 / 挨打 0**。
也就是连打防御能零技术含量地弹掉一切——这会让核心机制退化。

制作人裁定见 `docs/02-战斗系统设计.md` §8 末尾那段（**先读它**）。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/Data/DifficultyProfile.cs` | 新增 `[Export] int GuardReentryLockFrames`（默认 8），放在"玩家判定窗"组里 |
| `data/difficulty/*.tres`（4 个） | 填上这个新字段。建议：修罗 6 / 武士 8 / 剑客 10 / 見習 12 |
| `src/Player/PlayerActor.cs` | `TryEnterGuard()` 里判断"距上次松开防御不足 N 帧"→ 把 `EntrySource` 判成 `Cancel` |
| `tests/Oniblade.Tests/` | 新增用例：该判定逻辑要能被单测（如果它有可纯逻辑化的部分） |

### 规则

1. 松开防御时记下帧号。
2. 再次按下防御时，若 `当前帧 - 上次松开帧 < GuardReentryLockFrames`，
   则 `EntrySource = GuardEntrySource.Cancel`（付 `GuardCancelLockFrames` 的硬直：
   这几帧**只格挡、不弹开**）。
3. 否则按原有逻辑（从站立进入 = Neutral）。
4. **不许**用"按住不放"或"松开后一直不按"来触发惩罚；只惩罚"快速松开再按"。
5. 数值只来自 `DifficultyProfile`，代码里不许写死。

### 这条改动的验收（必须拿数据说话，不许"感觉应该好了"）

复用 T7 已经建好的 `scenes/tests/DeflectTraining.tscn` 机器人，加一个**连打模式**：

- **模式 A（看时机按）**：在判定帧前 ~5 帧按下 → 期望 `弹开 4 / 挨打 0`
- **模式 B（连打）**：完全不看时机，每 4 帧松一下再按一下 → 期望**弹开次数明显低于模式 A**

把两个模式的实测数字都打进完成报告。**如果模式 B 的弹开次数没有下降，说明改错了。**

### 硬约束

- **不要改** `CombatActor.cs` / `CombatResolver.cs` / `CombatTuning.cs` / `CombatWindow.cs` 的既有语义。
- 不要执行 `git commit`。
- 不要停下来问我；有歧义按 02 §8 的裁定文本判断，并在报告里写明。

---

## T12 · 危攻击预警 + 危·突刺枪兵

> **验收记录（制作人，实测）**：`check.ps1` 新增两个端到端场景，都带**对照组**：
>
> | 场景 | 弹开 | 格挡 | 挨打 | 预警 |
> |---|---|---|---|---|
> | `PerilousGuard`（横斩，一般攻击基线） | **4** | 0 | 0 | 0 次 / 形态 None |
> | `PerilousThrust`（危·突刺） | **0** | **4** | **0** | 4 次 / 形态 Thrust |
>
> **这就是 02 §3 那条裁定的可执行证明**：弹开对一般攻击有效、对危完全无效，
> 但**格挡仍然保命**（挨打 0）——"惩罚只惩罚效率，不惩罚存活"在数据上成立。
> 预警按 `PerilousKind` 正确分型（Thrust）。

**背景**：02 §3 刚定的裁定是「**一闪应对一切攻击，弹开只应对一般攻击**」。
它有一个直接后果：**玩家如果分不清"一般"和"危"，就会拿弹开去应付危攻击，然后直接挨打。**
所以「危」预警从"可及性选项"升级为**机制必需**——它不是锦上添花，它是这条裁定能成立的前提。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/PerilousCue.cs`（新） | 可复用的预警标记：`Show(PerilousKind kind, int frames)` / `Hide()`。灰盒期用红色自发光球 + 闪烁即可 |
| `src/Combat/CombatActor.cs`（改） | `OnAttackStarted` 的**基类实现**里统一处理：`data.Perilous` 时播放分型音效 + 显示预警；判定帧结束（`OnAttackEnded`）时隐藏 |
| `scenes/actors/SpearDummy.tscn`（新）+ `data/actors/spear_dummy.tres`（新） | 复用现成的 `AttackingDummy` 脚本，只把 `Attack` 指向 `res://data/attacks/enemies/perilous_thrust.tres` |
| `scenes/levels/Dojo.tscn`（改） | 放一个枪兵假人（`ActorId` 必须唯一，别和 100/101 撞） |

### 为什么预警放在基类而不是每个敌人各写一遍

预警是**机制必需**，不是某个敌人的特色。放在 `CombatActor.OnAttackStarted` 的基类实现里，
将来任何新敌人（BOSS、精英）**自动获得**，不会有人忘记加。
子类覆写时必须调 `base.OnAttackStarted(data)`。

### 音效

`AudioDirector` 已经有了，直接调：
`AudioDirector.Instance?.PlayCombat(CombatSfx.PerilousThrust)`（横扫 / 抓取同理）。
07 §2.2 要求三种**只靠音高区分**——这三个音效在 T3 已经按高/中/低生成好了，不用你重新做。

### 验收（**必须拿数据对比，这是本任务的核心价值**）

复用 `scenes/tests/DeflectTraining.tscn` 的机器人思路，做一次对照实验并打印数字：

| 靶子 | 机器人行为 | 期望 |
|---|---|---|
| 横斩假人（一般攻击） | 每次都在窗口内按防御 | **弹开 4 / 挨打 0** |
| 枪兵假人（危·突刺） | 同样在窗口内按防御 | **弹开 0 / 格挡 N / 挨打 0** |

第二行正是 T11 那条裁定的可执行证明：**弹开对危攻击无效，但格挡仍然保命**。
如果枪兵那边弹出了 `弹开 > 0`，说明裁定没有真正落到数据上，回来报告。

另外断言一次：枪兵发起攻击时，`OnAttackStarted` 确实走了 `Perilous` 分支
（打印 `PerilousKind` 即可）。

### 硬约束

- **不要改** `CombatResolver.cs` / `CombatTuning.cs` / `src/Combat/Data/*` / `PlayerActor.cs` / `src/Combat/States/*`。
  另一个 agent 正在并行改 `PlayerActor.cs` 和 `data/difficulty/`。
- `tools\check.ps1` 必须全绿（现在 5 步）。
- 不要执行 `git commit`。不要停下来问我；有歧义按 02 §3 的裁定文本判断，并在报告里写明。

---

## T18 · 喝血（`HealState`）

**背景**：01 §0 规则 2「资源管理不阻碍战斗」的直接落地。
**一个固定动作，不做药草、不做回血道具、不做菜单管理。** 规格全文见 02 §2.4。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/States/HealState.cs`（新） | 三段：起手 10 帧（**可被打断**）→ 饮用 34 帧（**受击不打断，但伤害照常结算**）→ 收招 10 帧（可被防御/闪避取消） |
| `src/Combat/Data/ActorStats.cs` | 新增喝血相关字段（次数上限、三段帧数、回复百分比），**不许写死在状态里** |
| `data/actors/player_stats.tres` | 填上这些值 |
| `src/Player/PlayerActor.cs` | 覆写 `RegisterStates` 注册 `HealState`；`item_use` 键触发；把参数写进状态再 `Change<HealState>()` |

### 关键机制：受击不打断，但伤害不免疫

这是**为了避免《只狼》式的"背板喝血"**（02 §2.4 有完整理由）：

| 做法 | 后果 |
|---|---|
| 打断喝血 | 玩家必须记住"BOSS 这一招之后才安全" → **记忆惩罚**，与本作方向相反 |
| **不打断、但照常掉血** | 玩家**永远能喝完**，但喝血的价值可能被抵消 → 博弈变成"现在喝还是再撑三秒" |

**实现提示（用现成机制，不要新造）**：`StateMachine.Tick` 会调 `Current.CanTransitionTo(_pending)`。
所以 `HealState` 只要覆写 `CanTransitionTo(next) => next is not StaggerState`，
就能让受击不再把玩家打进硬直——**伤害照常结算，只是不打断动作**。
（注意这也会挡掉 `GuardBreak` 推出的硬直，这是可接受的：喝血只有 54 帧。）

### 次数与补给（对齐 01 §0 规则 1）

- 每关基础 **3~5 次**（宽松，不是稀有资源），次数耗尽后不能再喝。
- **BOSS 战前自动补满 / 死亡后自动补满**——本任务只需把"补满"做成一个公开方法，
  真正的死亡重开协议是 T14。

### 验收（必须拿数据说话）

1. `tools\check.ps1` 全绿。
2. 新增 xUnit：三段帧数的边界（起手段可被打断 / 饮用段不可）。
3. **无头端到端**（写进 `scenes/tests/` 或复用现有场景）：让挥砍假人在玩家**正在饮用时**打中玩家，断言：
   - 玩家血量确实**回复了**（喝血生效）
   - 玩家**没有被推进 `StaggerState`**（不打断）
   - 但**伤害照常扣了**（不免疫）

   这三条缺一不可——它们就是"不背板但要付代价"这个设计的可执行证明。
4. 完成报告：怎么手测（按什么键、该看到什么）、验证输出、偏离规格处。

### 硬约束

- **不要改** `CombatResolver.cs` / `CombatTuning.cs` / `CombatActor.cs` / `src/Combat/States/AttackState.cs` / `GuardState.cs` / `GuardWindow.cs`。
- **不要碰** `src/Enemies/`、`src/UI/`、`project.godot`、`docs/00`。
- `project.godot` 里的 `item_use` 键（R）已经存在，直接用。
- 不要执行 `git commit`。不要停下来问我。

---

## T13 · 闪避（`DodgeState` + 无敌帧）

> **验收记录（制作人，实测）**：`check.ps1` 新增两个端到端场景，都带**对照组**：
>
> | 场景 | 被打中 | 躲开(Miss) | 完美闪避 | 避一闪 buff |
> |---|---|---|---|---|
> | `DodgeTraining`（无敌帧内闪） | **0** | 16 | **4** | 观察到 14 帧（符合规格） |
> | `DodgeTooEarly`（闪早了，对照组） | **4** | 0 | 0 | 0 帧 |
>
> 帧参数确认从难度档读取：**武士 = 无敌 8 / 后摇 18 / 宽容 3**。
> 对照组证明无敌帧是**真的在起作用**，而不是"闪避永远无敌"。

**背景**：02 §2.2 定义了闪避，01 §3 的**路径 B**（"闪避 + 普攻能通关"）整条建立在它上面，
05 §6 把它写进了验收清单——**但它在里程碑任务表里一次都没出现过**（08 §5「M1 缺项修正」）。
在"弹开只应对一般攻击"这条裁定之后，闪避还是**危攻击的保底答案**。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/States/DodgeState.cs`（新） | 闪避：无敌帧 → 后摇；完美闪避授予避一闪 buff |
| `src/Player/PlayerActor.cs`（改） | 覆写 `RegisterStates` 注册 `DodgeState`；`dodge` 键触发；把方向交给闪避 |

### 规则（全部来自 02 §2.2，不许自己发明）

| # | 规则 |
|---|---|
| 1 | **输入**：`dodge` 键（空格，已存在于 `project.godot`） |
| 2 | **无敌帧**：`0 ~ Difficulty.DodgeIFrames` 帧内 `IsInvulnerableNow = true`。裁决器规则 1 会直接给 `Miss`，**连一闪都打不中**（02 §4）。数值只来自 `DifficultyProfile.DodgeIFrames`，不许写死 |
| 3 | **完美闪避**：无敌帧内**实际躲开了一次攻击**（收到 `Verdict.Miss`）→ `GrantIssen(IssenKind.Dodge, 14)`。这是"避一闪"的入口（一闪本体是 M3，本任务只给 buff） |
| 4 | **后摇**：`Difficulty.DodgeRecoveryFrames` 帧（武士 18），可被**防御**取消 |
| 5 | **方向**：有方向输入就朝那个方向闪；没有就后跳。注意相机臂是 `TopLevel = true`（与身体解耦），移动方向要从相机算——照抄 `PlayerActor.TryGetMoveIntent` 的做法 |
| 6 | **完美闪避宽容**：无敌帧结束后 `Difficulty.PerfectDodgeGraceFrames` 帧内仍算完美闪避（02 §8） |

### 硬约束

- **不要改** `CombatResolver.cs` / `CombatTuning.cs` / `CombatActor.cs` / `States/GuardState.cs` / `States/AttackState.cs` / `Combat/GuardWindow.cs`。
- **不要碰** `src/Enemies/` / `src/Progression/` / `src/UI/` / `project.godot` / `docs/00` / `data/actors/*`。
- 不要执行 `git commit`。不要停下来问我；有歧义按 02 §2.2 判断并在报告里写明。

### 验收

1. `powershell -NoProfile -File tools\check.ps1` 全绿（现在 6 步）。
2. 新增 xUnit：无敌帧边界（第 `DodgeIFrames` 帧内 → `Miss`；之后 → 正常结算）。
3. **无头端到端**（放 `scenes/tests/`）：让挥砍假人出招，机器人在**无敌帧内**闪避 → 断言 `Verdict.Miss` 且没掉血；对照组在**无敌帧外**闪避 → 断言掉血。
4. 完美闪避后断言 `IssenBuff == IssenKind.Dodge` 且剩余 14 帧。
5. 完成报告：怎么手测（按什么键、该看到什么）、验证输出、偏离规格处。

---

## T14 · 死亡与重开协议（≤3 秒原地重开）

**背景**：01 §0 **规则 1（死亡没有持久性惩罚）** + 08 §3 P1-2。
原设计有"死亡后魂可原地取回"，**已删除**——掉魂再回去捡本身就是跑图惩罚。
调试面板的 F7 目前是"重载场景"，改完之后它应该调用这套原地重开。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/States/DeadState.cs`（新） | 死亡状态：不再接受输入、播死亡表现 |
| `src/Core/BattleReset.cs`（新；位置你判断，但要放在 `src/Core/`） | **原地重开协议**：把所有战斗单位复位，**不重载场景** |
| `src/Combat/CombatActor.cs`（改） | 补一个可复位的接口或虚方法——**只加不改**，现有语义一律不许动 |
| `src/Player/PlayerActor.cs`（改） | 玩家死亡 → 触发重开流程 |
| `data/**/*.tres` | 重开相关的帧数放进数据，不许写死 |

### 规则

| # | 规则 |
|---|---|
| 1 | 玩家死亡 → **≤ 3 秒（180 帧）** 内回到可控状态，**原地**重开，不重载场景、不退回主菜单 |
| 2 | **不掉的**：升级、魄、喝血次数、侵蚀度。死亡不扣任何持久资源 |
| 3 | **要复位的**：所有战斗单位的位置、朝向、血量、体干、状态机、buff、顿帧 |
| 4 | 敌人侧的**重生计时也要重置**（`TrainingDummy` / `AttackingDummy` / `RespawnDummy`），否则会出现"重开后假人还卡在死亡等待里" |
| 5 | 调试面板 **F7 改成调用这套协议**（它现在是 `ReloadCurrentScene`，见 T2 完成报告） |

### 硬约束

- **不要改** `CombatResolver.cs` / `CombatTuning.cs` / `States/AttackState.cs` / `States/GuardState.cs` / `Combat/GuardWindow.cs` 的既有语义。
- **不要碰** `src/Progression/`（吸魂与侵蚀已经在工作）、`project.godot`、`docs/00`。
- 不要执行 `git commit`。不要停下来问我。

### 验收（时间必须实测，不许估）

1. `powershell -NoProfile -File tools\check.ps1` 全绿。
2. **无头端到端**：把玩家打死 → 逐帧数，断言：
   - 玩家在 **≤180 帧**内回到初始位置，状态为 `IdleState`
   - 血量/体干满、`IsDead == false`
   - **能吃输入**（例：重开后按 `move_right` 真的能走动）
   - 敌人也复位了（位置 + 血量）
3. 完成报告里给出**实测的重开帧数**——不是"应该小于 180"。

---

## T20 · 一闪（真一闪 + 安全窗 + 特殊音效与动作）

### ⚠️ 先说清楚：**格挡已经做完了，不要重做**

| 项 | 现状 |
|---|---|
| 格挡状态 | `src/Combat/States/GuardState.cs` **已存在并工作** |
| 按键 | **鼠标右键**（`guard` / `button_index=2`，另附 K 键）——已是你想要的那个键 |
| 弹开 | 同一套里也做完了（窗口内被命中 → `Deflect`，零体干 + 削敌体干） |
| 端到端验证 | `scenes/tests/DeflectTraining.tscn`：**格挡 4 次 / 挨打 0** |

**本卡只做一闪。**

### 背景

一闪是本项目的卖点之一，而且 02 §3 已裁定：**一闪可以应对一切攻击**（含「危」）。
现在只有**结算侧**存在——`IssenTable`（收益表）、`CombatResolver` 规则 2、`IssenKind` 枚举、
`CombatTuning.ResolveIssenWindowFrames`（窗口合成通道）——**但没有任何东西去驱动它**。

### 交付物

| 文件 | 内容 |
|---|---|
| `src/Combat/IssenWindow.cs`（新，纯逻辑可单测） | 真一闪判定：按攻击的那一帧，敌人距离命中还有几帧 |
| `src/Combat/States/IssenState.cs`（新） | 22 帧后摇，**不可取消** |
| `src/Player/PlayerActor.cs`（改） | 窗口内按攻击 → 授予 `IssenKind.Shin` buff；落空 → 30 帧无防御硬直 |
| `src/Dev/BlockoutRig.cs`（改） | 灰盒一闪动作（快速斜斩 + 收势停顿） |
| `src/Audio/AudioDirector.cs`（改） | 接线：`Verdict.Issen` → `IssenSlash` + `IssenImpact` |

### 规则（全部来自 02 文档，不许自己发明）

| # | 规则 |
|---|---|
| 1 | **真一闪**：敌人在"命中前 0~N 帧"时按下攻击 → 授予 `IssenKind.Shin`。N 由 `CombatTuning.ResolveIssenWindowFrames(Difficulty.IssenWindowFrames)` **合成**（已存在，武士 = 6）。⚠️ **不许直接读 `DifficultyProfile.IssenWindowFrames`**（08 §3 P1-3 红线） |
| 2 | **实现方式**（02 §4）：**"输入时捕获意图，判定帧结算结果"**——按下的那一瞬间扫描范围内敌人，把结果记成 buff |
| 3 | ★ **安全窗**（防劝退核心，02 §2.3）：按早了（N ~ N+`IssenSafeWindowFrames` 帧）→ **不算一闪，但自动转格挡姿态，不挨打**。**这一条比一闪本身更重要** |
| 4 | **弹一闪**：弹开成功后按攻击。`DeflectState` 已经会授予 `IssenKind.Deflect` buff（10 帧），你只要让"buff 存在时按攻击"能真正打出 `Verdict.Issen` |
| 5 | **结算**：`IssenTable.For(kind, tier)` 已实现（杂兵即死 / 精英 −60~70% 体干 / BOSS −25~30% + 硬直），**直接用** |
| 6 | **`IssenState`**：22 帧、**不可取消**；落空 → 30 帧无防御硬直（有代价，但不是即死） |
| 7 | **连锁一闪**：一闪命中后 12 帧内按攻击 → 瞬移到 ≤8m 内最近的敌人重复一闪，**上限 3 连** |

### ★ 表现（本卡的重点之一：准确触发后要有特殊音效与动作）

| 表现 | 要求 |
|---|---|
| **音效** | **先 0.2 秒静音（世界抽真空）→ 切割声 → 低频轰鸣**（02 §7）。音效**已经生成好了**（`CombatSfx.IssenSlash` / `IssenImpact`），**直接用，不要重做音频** |
| 顿帧 | 14 帧（`ResolveResult.HitStopFrames` 已返回） |
| 慢镜 | `Engine.TimeScale = 0.25` + **Tween 平滑恢复 0.3s**。⚠️ **绝对不能瞬间跳回 1.0**（04 §10） |
| 视觉 | 黑白闪（全屏 `ColorRect` / shader）+ 血雾粒子 + 镜头推近 |
| 动作 | 灰盒期只用 `BlockoutRig`：加一个"快速斜斩 + 收势停顿"，**不需要新模型** |

### 硬约束

- **不要改** `CombatResolver.cs` 的判定顺序 / `CombatTuning.cs` / `IssenTable.cs`（都已完成且有测试）。
- **不要碰** `src/Enemies/` 的敌人 AI、`src/Progression/`（吸魂已工作）、`project.godot`、`docs/00`。
- 不要执行 `git commit`。不要停下来问我。

### 验收

1. `tools\check.ps1` 全绿（现在 8 个端到端场景）。
2. **单测**：真一闪窗口边界——N 帧内 → `Shin`；安全窗内 → 转格挡且**不挨打**；窗口外 → 无事发生。
3. **端到端对照实验**（放 `scenes/tests/`，**必须有对照组**）：
   - 命中前 N 帧内按攻击 → `Verdict.Issen`（对杂兵即死）
   - **按早**（安全窗内）→ **不挨打**（转格挡）——这是防劝退核心，**必须单独断言**
   - 按太早 / 太晚 → 落空，进入 30 帧无防御硬直
4. 断言一闪时：`IssenSlash` 音效被播放；`Engine.TimeScale` 曾降到 0.25 **并恢复回 1.0**。
5. 完成报告：怎么手测（按什么键、该看到什么）、验证输出、偏离规格处。

### ⚠️ 领取前的依赖检查

**T18（喝血）正在改 `PlayerActor.cs` 与 `CombatActor.cs`。**
两张卡同时动这两个文件会撞车（本项目已经因为并发写同一文件出过一次重复定义）。
**请确认 T18 已经合并之后再领本卡。**

---

## T21 · 删除 `PerfectDodgeGraceFrames`（清理死配置）

**背景**：02 §8 的裁定 —— 这个参数**物理上不成立**：
无敌帧结束后不再产生 `Miss`，而"完美闪避"只能由 `Miss` 触发。
它现在**只放宽判定窗、不放宽无敌**，因而是**惰性的**。
真正在做"宽容"的是 `DodgeBufferFrames`（输入缓冲）。

> 理由与 08 §3 P1-3 同源：**一个数字只能有一个来源。**
> 留着一个不生效的旋钮，比没有这个旋钮更危险——它会让后来的人以为宽容是它给的。

### 交付物（删干净）

| 文件 | 动作 |
|---|---|
| `src/Combat/DodgeWindow.cs` | 删除 `PerfectDodgeGraceFrames` 属性与其注释 |
| `src/Player/PlayerActor.cs` | 删除给 `dodge.PerfectDodgeGraceFrames` 赋值的那一行 |
| `data/difficulty/{asura,samurai,kenshi,migoto}.tres` | 删除该字段的行 |
| `src/Dev/DodgeTrainingTest.cs` | 打印里去掉"宽容 X 帧" |
| `tests/Oniblade.Tests/DodgeStateTests.cs` | `Grace_Frames_Do_Not_Extend_Invulnerability` 改为断言**无敌帧严格等于 `DodgeIFrames`** |

### 验收

1. `tools\check.ps1` 全绿。
2. **`rg PerfectDodgeGraceFrames src data tests` 必须无结果**（这条就是验收本身）。
3. 闪避的两个端到端场景（`DodgeTraining` / `DodgeTooEarly`）数字不变：
   **躲开 16 次 / 完美 4 次** vs **闪早挨打 4 次**。
4. 不要改 `DodgeIFrames` / `DodgeBufferFrames` 的值。

---

## T22 · 复活（`ReviveCount` 目前是死配置）

**背景**：`DifficultyProfile.ReviveCount` 存在于代码与四档 `.tres` 里，
05 §2 的难度表也把它当**可及性杠杆**（修罗 1 / 武士 1 / 剑客 2 / 見習 3），
02 §1 的状态清单里也写着 `DeadState` / `ReviveState`——**但没有任何代码使用它**。

它和 T14 的"死亡即原地重开"不是一回事：

| | 复活 | 重开（T14，已完成） |
|---|---|---|
| 发生次数 | 每条命 **`ReviveCount` 次** | 复活用完之后 |
| 表现 | **当场站起来**（短演出 + 起身无敌帧） | 复位全场，回到可控 |
| 掉资源吗 | **不掉**（01 §0 规则 1） | 不掉 |

### 规则

1. 玩家血量归零 → **若还有复活次数**：进入 `ReviveState`，播约 1.5 秒的起身演出，**给一段无敌帧**，然后回到可控。
2. 复活次数用尽 → 交给 **T14 的 `BattleReset`**（原地重开）。
3. 重开后复活次数**补满**（01 §0 规则 1：死亡没有持久性惩罚）。
4. 次数进 `.tres`，**不许写死**；四档按 05 §2 的表（1 / 1 / 2 / 3）。

### 硬约束

- **不要改** `CombatResolver.cs` / `CombatTuning.cs` 的既有语义。
- **不要碰** `BattleReset.cs` 的既有协议（它是 T14 已验收的成果），只**调用**它。
- 不要执行 `git commit`。不要停下来问我。

### 验收

1. `tools\check.ps1` 全绿。
2. **无头对照实验**：把玩家打死 **两次** ——
   - 第一次 → **复活**（断言没有触发重开、血量恢复、能重新吃输入）
   - 第二次（次数用尽）→ **触发重开**
3. 断言四档难度的复活次数不同（用 `見習` 档跑一遍，应能死 3 次才重开）。

---

## T23 · `ActorId` 改为自动分配（**已经撞号两次**）

**背景**：`CombatActor.ActorId` 目前在 `.tscn` 里**手工填**。
而 `CombatArbiter` 的确定性排序（04 §15：按 `ActorId` 排序 → 同帧结果可复现）**依赖它唯一**。

**已经撞过两次，而且两次都是静默的**：

1. 道场里两个 `TrainingDummy` 都是 `100`（制作人写的）。
2. `SpearDummy` 用了 `102`，而 `RespawnDummy` 已经是 `102`（T12 写的）。

两次都不会报错、不会闪、不会崩——**只会让"同帧互击谁先结算"变得不可复现**。
这是最糟的一类 bug：它只在调参时表现为"怎么这次结果不一样了"。

### 做法

在 `CombatActor._Ready()` 里：

```csharp
if (ActorId == 0)
    ActorId = NextAutoActorId();   // 静态递增计数器
```

- **保留手动覆盖**（`ActorId != 0` 时用场景里的值）——调试场景有时需要固定 id。
- 分配顺序由**树序**决定，而树序是确定的，所以确定性不受影响。

### 验收

1. `tools\check.ps1` 全绿。
2. 把 `Dojo.tscn` 与各测试场景里的**手工 `ActorId` 全部删掉**后仍然全绿。
3. **新增一条自检**（`SelfTest` 或端到端场景里）：断言场上所有战斗单位的 `ActorId` **两两不同**。
   这条断言的价值在于——**它以后会替我们抓住第三次**。

---

# 美术任务（T24~T28）

> **前提（07 文档的铁律）**：**美术绝不阻塞玩法。**
> 所有美术任务都必须满足：`tools\check.ps1` 的 **12 步全绿**、战斗逻辑一行不改、
> 灰盒外观可随时切回来（`BlockoutRig` 保留成"调试外观"开关）。
>
> **美术任务的验收标准不是"做完了"**，而是：
> **① 在游戏里真跑起来 ② 截图 ③ 无 ERROR ④ 在性能预算内。**

---

## T24 · 动画管线打通 🔴 最高优先

**为什么这个排第一**：07 §1.3 自己写着"**M0 就该花一天把 Mixamo 流程跑通**"。
现在整条链路上**没有一个真实动画**，它同时阻塞着三件事：

1. 04 §13 要求的调试面板 **"动画帧 vs 逻辑帧偏差 ≤2 帧"**检查——没有动画就无法验证
2. 帧数据（`AttackData` 的前摇/判定/后摇）与动画**对不上**——现在是"数字好看但看不出来"
3. 所有后续美术资产（主角、敌人、BOSS）都要走这条管道

### 交付物

| 项 | 内容 |
|---|---|
| 流程 | 一条**可复现**的流程：Mixamo 下载 → 导入 Godot → **FPS 设 60** → 挂 `AnimationPlayer` → 用 `Seek()` 由状态机驱动 |
| 第一个动画 | 主角的 **`idle` + `attack_light_01`**（两个就够，先证明管道） |
| 驱动层 | 状态机 → 动画：**逻辑帧是权威**，动画只做 `Seek(logicFrame/60)`（04 §12 的"两层分离"） |
| 文档 | 把这套流程写进 `docs/`（哪一步设什么、踩了什么坑），**换个人也能照做** |

### 硬约束

- **不要改**战斗逻辑（`CombatResolver` / `CombatActor` 的帧语义 / `AttackData` 字段）。
- 动画只允许**被逻辑帧驱动**，不许反过来让动画决定判定时机。
- 导入动画**必须是 60fps**（04 §12：全项目基准，不允许 30）。
- **不要碰** `tools\check.ps1` 的场景列表。

### 验收

1. `tools\check.ps1` 12 步全绿。
2. **可视化自证**：连续帧截图（或短录屏转 GIF），显示攻击动画确实按帧推进。
3. 调试面板的 `ANIM SYNC` 区块能显示真实 anim/logic 帧号，**偏差 ≤2**。

---

## T25 · 主角本体模型 + 骨架

**规格全文**：[09-主角外观设定](09-主角外观设定.md)（配色板、剪影、部件、绑定规格都在那）。

### 交付物

| 项 | 内容 |
|---|---|
| 模型 | 按 09 §2/§3/§4：**偏瘦、175cm、头身比 7.2**；无兜无家纹无肩甲；背后酒壶 + 布袋 |
| 刀 + 鞘 | 09 §4.4。**鞘要能单独摆动**（一根独立骨） |
| 材质槽 | 必须分组：`Body` / `Cloth` / `Metal(刀)` / `Gauntlet` / `Eye`（09 §7） |
| **附加骨骼** | **笼手独立骨（手指 5 + 手腕 1）**；刀、鞘各一根——**在动画导入之前加好并蒙皮** |
| 接入 | 替换灰盒外观，但 **`BlockoutRig` 保留**（调试开关） |

### 验收

1. `tools\check.ps1` 12 步全绿（**外观换了，战斗数值一个都不许变**）。
2. **截图**：正面 / 侧面 / **背面**各一张（背面最重要——玩家 90% 时间看背面）。
3. **灰盒对照截图**：同场景下灰盒与正式外观各一张，证明"打起来仍然能读招"。
4. 三角面 ≤ 25k（09 §7）。

---

## T26 · 噬魂笼手（独立资产）

**为什么单独一张卡**：它是**本作第一视觉标识**（09 §5），而且是**玩法组件**——
侵蚀三阶段靠换材质实现，不是换模型。所以它的 UV、材质槽、骨骼必须独立规划。

### 交付物

- 笼手模型（**左手**，手背包到小臂中段）：**"湿的、有生长的甲壳"**，既不是金属也不是皮革
- **独立 UV 与材质槽**，能承载三套材质：**初鸣 / 共鸣 / 同化**（09 §6 的表）
- 沟槽里的**自发光**（`#A855F7`）——**全身唯一光源**
- 手指可动（抓握 / 吸魂手势）

### 验收

1. `tools\check.ps1` 12 步全绿。
2. **三阶段对照图**：同角度、同光照下的初鸣 / 共鸣 / 同化 三张截图。
3. **暗场截图**：在没有其他光源的地下水道环境里，证明**笼手能当光源用**（10 §3.3）。
4. 与皮肤/袖口的交界处**看不出接缝**。

---

## T27 · 敌人模型：魔骸足兵 + 魔骸枪兵

**规格全文**：[10-敌人·场景·特效美术设定](10-敌人·场景·特效美术设定.md) §1 / §2.1 / §2.2。

**最要紧的一条约束**：**必须让玩家看得出"它曾经是个人"**（10 §0）。
足兵戴着歪掉的**阵笠**，枪兵**站得比别的魔骸直**——这不是美术口味，是叙事要求
（序章那句"疼……"、共鸣阶段的"……别抢……这是给孩子的……"都靠它成立）。

### 交付物

- 魔骸足兵（阵笠 + 残破胴 + 打刀）
- 魔骸枪兵（完整长枪 + 破具足）
- 材质：黑 + 血红（`#141014` / `#7A1418`），伤口与眼窝透红光（`#C8323A`）
- 与主角的**辨识对比**：低饱和 vs 高饱和、直立对称 vs 佝偻不对称（10 §1 的表）

### 验收

1. `tools\check.ps1` 12 步全绿。
2. **同屏对比截图**：主角 + 2 个魔骸站在一起，**一眼能分清谁是人**（07 §3.1 的要求）。
3. 三角面各 ≤ 12k（MVP）。
4. 替换后 `PerilousThrust` / `DeflectTraining` 两个端到端场景仍然全绿。

---

## T28 · 战斗特效（弹开 / 拼刀 / 血雾 / 一闪）

**规格全文**：[10 §4](10-敌人·场景·特效美术设定.md)。

**驱动方式**：全部由 **`EventBus.HitResolved`** 驱动——它已经带着
`Verdict` / `Damage` / `HitStopFrames`。**特效层只订阅事件，不许反过来影响战斗逻辑**（04 §4 的订阅纪律）。

### 交付物（4 个 P0）

| 特效 | 触发 | 关键要求 |
|---|---|---|
| 弹开火花 | `Verdict.Deflect` | 白蓝，**≤8 帧内结束** |
| 拼刀大颗粒火花 | `Verdict.Clash` | 更大更慢带拖尾 |
| 命中血雾 | `Verdict.Hit` | **方向与刀路一致**，不是爆开 |
| 一闪黑白闪 | `Verdict.Issen` | 全屏 0.1s 高对比 + 血雾 |

### ⚠️ 第一原则：**特效不许盖住判定**

弹开与拼刀的火花必须**短**（≤8 帧）。玩家需要看清下一刀什么时候来。
华丽是二闪 / 忍杀的事，**不是防御反馈的事**。

### 验收

1. `tools\check.ps1` 12 步全绿。
2. **截图**：四种特效各一张（在暗场里拍，才看得出对比）。
3. **逐帧截图**：证明弹开火花在 **8 帧内消失**——这是可验证的，不是"感觉挺短"。
4. 掉帧时按 10 §5 的顺序降级（**先砍雾 → 再砍粒子**），60fps 是硬指标。
