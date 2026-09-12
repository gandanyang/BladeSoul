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
| **T1** | 可玩闭环：战斗管线 + 三连击 + 木桩 | ~~执行 agent~~ → **制作人** | ✅ 完成（待验收） | — |
| **T2** | 调试面板 F1~F8 | 执行 agent | 🔵 进行中 | 接口已冻结 |
| **T3** | 音频层 + 程序化占位音效 + 总线 | 执行 agent | 🔵 进行中 | 接口已冻结 |
| T4 | 修订 06/README 的口径（M5 = 1.0） | 制作人 | ✅ 完成 | — |
| T5 | `CombatTuning`（弹开窗唯一计算入口） | 待派发 | ⚪ 未开始 | T1 |
| T6 | `BossPhaseProfile` 数据层 | 待派发 | ⚪ 未开始 | T1 |

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
