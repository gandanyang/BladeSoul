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
| T14 | **死亡与重开协议（≤3 秒原地重开）** | 🟢 **可领取** | ⚪ 未开始 | — |
| T15 | **世界观 / 主角 / 反派 / 成长系统设定补充**（03 §2.5·§2.6·§6.4~6.7） | 制作人 | ✅ 完成 | — |
| T16 | `UpgradeTree` 升级数据资源（三条线 10 级，03 §6.4） | 🟢 可领取 | ⚪ 未开始 | T15 ✅ |
| T17 | `SoulWallet` + 侵蚀度接 `GameState`（03 §6.4·§6.6） | 🟢 可领取 | ⚪ 未开始 | T19 ✅ |
| T18 | **喝血 `HealState`**（02 §2.4） | 🟢 **可领取** | ⚪ 未开始 | — |
| T19 | 自动吸魂（魄牵引 + 连续吸魂反馈 + 深吸） | 制作人 | ✅ 完成 | — |

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
