# AGENTS.md · ONIBLADE 鬼刃

> Godot 4.7.1 mono / C# 的 3D 和风剑戟动作游戏（弹开 / 拼刀 / 一闪）。
> **本仓库由多个 agent 并行开发。你是其中一个，默认只改任务卡指定的文件。**

---

## 1. 先读什么（按顺序，别跳）

| # | 文档 | 为什么 |
|---|---|---|
| 1 | `docs/TASKS.md` | **任务卡看板**。找你要做的那张卡——规格、硬约束、验收全在卡里 |
| 2 | `docs/00-制作人决策记录.md` | **已冻结的接口**。与任何其它文档冲突时，**以它为准** |
| 3 | `docs/08-顶层设计评审.md` §4 | 三条红线 |
| 4 | 卡片里点名要读的文档 | 各系统的完整规格 |

**没有对应的卡就不要动手。** 卡不存在说明这件事还没被决定。

---

## 2. 铁律（违反会被打回）

1. **数值不许硬编码进 C#**，一律进 `data/**/*.tres`；帧相关的字段必须带 `Frames` 后缀。
   理由：C# 没有热重载，而本项目 40% 的工时是调参。
2. **不许改冻结接口**（清单见 `docs/00` §2.8）。确实需要改，**在完成报告里提出来**，由制作人登记。
3. **不要 `git commit`。** 多 agent 并行会抢索引锁，由制作人统一提交。
4. **不许改别人卡片拥有的文件。** 每张卡的"硬约束"会写明不许碰什么。
5. 改完必须跑 `tools\check.ps1` 并**全绿**（见 §3）。
6. 所有 `Node` / `Resource` 派生类必须 `public partial`；每个 `[Export]` 用 `[ExportGroup]` 分组。
7. 命名空间用 file-scoped（`namespace X;`），命名空间为 `Oniblade.<Layer>`。
8. **纯逻辑与引擎分离**：判定、体干、窗口计算这类东西写成**不依赖场景树的类**，才能被单测。

---

## 3. 验证

```powershell
powershell -NoProfile -File tools\check.ps1
```

37 步：编译 → xUnit 单测 → **两道棘轮**（死配置 / `async void`）→ 资源自检 → 端到端场景 → **腿部几何不变量**。
**输出必须是 `ALL CHECKS PASSED`（退出码 0）。**

**开工前先跑一次**，确认当前是绿的——工作区里可能有别的 agent 的在制品。
单跑某一步：`godot --headless --path . res://scenes/tests/<场景>.tscn`

---

## 4. 目录（三层铁律）

```
scenes/  只放结构       data/   只放数值（改这里零编译）
src/     只放逻辑       tests/  纯逻辑单测（脱离引擎跑）
tools/   开发脚本       assets/ 原始素材
```

**`scenes/` 放结构，`data/` 放数值，`src/` 放逻辑。三者不许交叉。**

| 想看什么 | 去哪 |
|---|---|
| 战斗裁决（优先级） | `src/Combat/CombatResolver.cs` ★ 全游戏的心脏 |
| 判定窗合成 | `src/Combat/CombatTuning.cs` ← **每个数字只能有一个来源** |
| 招式帧数据 | `data/attacks/**/*.tres` |
| 难度宽容参数 | `data/difficulty/*.tres` |
| 角色状态机 | `src/Combat/States/*.cs`（POCO，不是 Node） |
| 敌人 AI | `src/Enemies/` |
| 调试面板 | `src/UI/DebugOverlay.cs`（F1~F8） |
| **3D 资产 / Blender** | **[`docs/17-Blender与AI工具链.md`](docs/17-Blender与AI工具链.md)** ★ 要用 Blender 先读它（装在哪 / 怎么无头跑 / 三个坑） |
| 动画体检（骨头到底动没动） | `scenes/tests/AnimProbe.tscn`（几秒出逐骨偏转表） |
| 走路体检（腿动没动） | `scenes/tests/MoveProbe.tscn` |

---

## 5. 开工前必做的并发检查

```powershell
git status --short          # 工作区有什么在动
Get-ChildItem -Recurse src -File | Sort LastWriteTime -Desc | Select -First 5
```

**如果出现你根本没动过的文件是 `M` 状态，说明另一个 agent 正在写它**——
要么避开，要么先问。本项目**已经因为并发写同一个文件出过两次重复定义**（编译直接挂）。

---

## 6. 已知的坑（全都踩过，别再踩）

| 坑 | 现象 | 对策 |
|---|---|---|
| **场景节点忘挂脚本** | **静默失效**——判定框查不到目标，还不报错 | 新建节点后立刻确认 `script` 字段 |
| `_PhysicsProcess` 在 `IsDead` 时早退 | 死亡之后的逻辑永远不跑 | 覆写 `_PhysicsProcess`，先调 `base` 再自己数帧 |
| **`ActorId` 手工分配** | **撞号后静默**破坏"同帧结果可复现"（已撞两次） | 新单位检查 id 唯一性 |
| 新资产没有 `.import` | `ResourceLoader.Exists` 返回 false，资源"不存在" | 跑 `godot --headless --path . --import` |
| **`.ps1` 没带 UTF-8 BOM** | PS 5.1 按 ANSI 读 → 中文变乱码，甚至**语法错**（生成器脚本栽过一次） | **文件必须带 BOM**：`[IO.File]::WriteAllText($p,$t,(New-Object Text.UTF8Encoding $true))`；新脚本建议纯 ASCII |
| **另一个 agent 正在写文件时构建** | 报一些**根本不存在**的编译错误（撞在两次写盘之间） | **重跑一次再下结论**，别急着报 bug |
| `project.godot` 里的注释 | 编辑器保存时会被清掉 | 权威内容写进 `docs/00` |
| 端到端测试等太久 | `check.ps1` 变慢 | 测试里缩短等待参数，**不许改机制** |
| 覆写虚方法忘了 `base` | 静默丢掉基类行为（如「危」预警） | 覆写时先想"基类有没有做事" |
| **用 Blender 前不读 docs/17** | 重新探"装没装 / 走不走代理 / 能不能无头"，纯浪费 | **先读 [docs/17](docs/17-Blender与AI工具链.md)**；资产脚本统一 `--background --factory-startup` |
| **模型里混着垃圾对象** | 主角模型里藏着 80 面的单位球（把包围盒撑到 ±1），会误导所有按尺寸做的判断 | 先量包围盒；不合理就怀疑有杂物 |
| **探针测错对象**（本轮连撞 5 次） | 工具给出**精确但错误**的答案，比不测更危险：① 直接写骨测到"离地 65cm"，真实链路是 33cm；② 用 `TrackGuard()` 想钉住格挡帧，被 `PlayerActor` 每帧覆盖 → 全 0；③ 把 `GetBoneGlobalPose`（**相对骨架空间**）当世界坐标 → 根骨骼下沉看不见；④ 基准没在每轮重置 → 数字累加 195→379→609；⑤ 判据用"首帧速度"判跳跃惯性，而起跳帧本就还在蹬地 | **改被测对象前先自证探针**：跑一组"已知答案"的对照（如静止站姿必须等于基准）。姿势定标一律**参数化进被测类**（`ForcedGuardFrame` / `GuardThighAngle`）再走真实链路，不许旁路写骨。**测出的数字若和上一轮对不上，先怀疑口径，不要怀疑实现** |
| **动画姿势的"符号反了"** | **逻辑全绿、但角色看起来不是人**：T52 试玩"弹开时腿反着往前弯曲"。根因是 `Rot()` 在腿部骨骼上的正负号与直觉相反（本模型 `L_Thigh` 的 rest 旋转是 180°），于是"屈膝"写成了"脚向前翘"。坐下 18 帧反折时 **check.ps1 的 36 步全是绿的** | 别靠肉眼判"哪边是前"（`Hip` 的 `+Z` 其实是**身后**）。用**几何硬事实**判：脚必须低于膝。已由 `check.ps1` 第 37 步把守；`HumanoidAnimator.Diagnostics` 可查"这根骨这一帧是谁写的" |
| **`async void` 承载可失败流程** | **异常静默消失**，现场表现千奇百怪：日志里堆 2 万行没人看见（T49/T50 的 755 次 `ObjectDisposedException`）；或者**测试跑到一半不动、零错误输出、最后超时**（T52）；或者一闪只剩半截音效（`AudioDirector`） | **工程红线**，已由 `tools/check_async_void.ps1` 棘轮把守（生产零容忍）。改法：`void _Ready() => _ = RunAsync();` + `RunAsync()` 里 try/catch，catch 中 `PrintErr` + `Quit(1)`。详见 §9 |
| **对已释放的 Godot 节点调 `QueueFree()`** | 同上：C# 包装对象**释放后仍非 null**，只判 `null` 会拿到失效引用 → `ObjectDisposedException` → 在 `async void` 里被吞 | 一律 `GodotObject.IsInstanceValid(node)` 判活；测试里有 `SafeFree()` helper 可抄 |

---

## 7. 完成报告格式（交回来必须包含）

1. **改了哪些文件**
2. **怎么手动验证**——按什么键、屏幕上该看到什么
3. **验证命令的真实输出**（贴出来，不要复述"全绿了"）
4. **偏离任务卡的地方及原因**
5. **新增 / 改动的接口**（制作人要登记进 `docs/00`）
6. 有对照实验的（如"闪早了会不会挨打"），**必须给出对照组的数据**

> **"编译过"不等于"做完了"。**
> 本项目的验收习惯是：**能拿数据自证**。一个只测了单个点的测试，
> 会让错误的实现看起来完全正确（连打防御的第一版修法就是这么被骗过去的）。

---

## 8. 当前状态

- **能玩**：道场（`scenes/levels/Dojo.tscn`）—— WASD 移动 / 鼠标视角 / 左键或 J 三连 /
  **右键格挡与弹开** / 空格闪避 / R 喝血 / 长按 R 深吸 / **一闪** /
  **战斗 HUD**（血槽·架势槽·伤害数字·未锁定敌人头顶条）；场上已有上色版魔骸足兵
- **验证**：**37 步全绿，239 项单测**（`powershell -NoProfile -File tools\check.ps1`）。
  另有两道静态棘轮：死配置零引用、`async void`（见 §6 / §9）；
  以及**表现层几何不变量**（第 37 步：脚必须低于膝，见 §6）
- **当前里程碑**：**M5 途中**（垂直切片）——代码已**越过 M4**，
  卡在"**没有遭遇战编排、没有存档点**"（细分见 `docs/14-下一阶段排期.md`）
- **M1 唯一的验收标准**：**"弹开成功时会想再试一次。"** —— 这一条只能由真人试出来，
  而且**至今没做过正式验收**（见 T44）

任务卡与进度：`docs/TASKS.md`

> ⚠️ 排期与阻塞先看 `docs/14-下一阶段排期.md`——它会告诉你"什么卡着什么"，
> 免得只按最近一次试玩反馈来决定做什么。

---

## 9. 工程红线：禁止 `async void` 承载可失败流程

**为什么它值得单独一节**：同一个陷阱在本项目**撞了三次** ——

| 卡 | 现场表现 |
|---|---|
| T49 / T50 | 755 次 `ObjectDisposedException` 累积在日志里，**没有一条被看见** |
| T52 | `EnemyDeathblowTest` 跑到一半**不动了**：零错误输出、没有失败断言、最后超时 |
| T52 | `AudioDirector.PlayIssenSequence` 若异常 → 一闪"切割声已响、轰鸣永远不来"，**玩家只听到半截音效** |

三次都不是"某张卡的 bug"，而是同一个工程级陷阱 —— 所以它现在和死配置一样，
属于**代码审查红旗**：看到 `async void` 就该停下来问一句"它的异常谁接？"

```csharp
// X 禁止：异常不会传播给任何人
public override async void _Ready() { await SomethingAsync(); Report(); }

// OK 正确：生命周期方法本身仍是 void（Godot 要求），但异常被兜住
public override void _Ready() => _ = RunAsync();

private async Task RunAsync()
{
    try { await SomethingAsync(); Report(); }
    catch (Exception ex) { GD.PrintErr($"[xx] ✗ 未捕获异常：{ex}"); GetTree().Quit(1); }
}
```

**棘轮**：`tools/check_async_void.ps1`（`check.ps1` 第 2c 步）。
生产代码 `src/` **零容忍（基线 0）**；`src/Dev/` 与 `tests/` 基线 28，
**只许减少不许新增** —— 每迁移一个测试就把基线减一。

**配套规则**：对已释放的 Godot 节点调 `QueueFree()` 会抛 `ObjectDisposedException`——
C# 包装对象**释放后仍然非 null**，所以判活必须用 `GodotObject.IsInstanceValid(node)`，
判 `null` 是无效的（这就是 T49/T50 那 755 次的根因）。
