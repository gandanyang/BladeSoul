# T53 交接 · 弹开反馈按伤害类型分档

## 一句话

**这是"接线"，不是"造轮子"。** `DamageType`（四值）、`CombatSfx`、`AttackData.Type`、
`HitEvent` 通道、`SparkBurst`、`CombatVfxDirector`、`AudioDirector` **全部已经存在**。

本卡新增的只有三样：**一张四档查表** ＋ **三个音色文件** ＋ **事件上一个字段**。
没有新系统、没有新输入、没碰任何裁决逻辑。

---

## 一、核心判据：两条正交轴，不许压成一个数

制作人这轮的**关键更正**（`07 §1.1.3`）：

| 轴 | 回答什么 | 取值范围 | 本卡 |
|---|---|---|---|
| **强度档** `F1~F5` | 量级（多响、多亮、多震） | 一个数 | ❌ **不做** |
| **攻击性质** `DamageType` | 同一量级下的**质感** | `Slash / Thrust / Blunt / Dark` | ✅ **本卡只做这一层** |

压成一个数就会滑向"打击比斩击更响，所以所有通道一起加"的偷懒做法。
**本卡没有引入任何这种加总**——四档是各自独立的参数组。

---

## 二、改了什么（全部）

| 文件 | 改动 | 性质 |
|---|---|---|
| `src/Core/GameEvents.cs` | `HitEvent` 加 `AttackType`（默认 `Slash`） | ＋1 字段 |
| `src/Core/CombatArbiter.cs` | `RaiseHitResolved` 里填 `AttackType = attack?.Type ?? DamageType.Slash` | ＋1 行 |
| `src/Combat/Data/DeflectFeedbackProfile.cs` | **新**：一档的 SFX ＋ 火花参数 | 新 |
| `src/Combat/Data/DeflectFeedbackSet.cs` | **新**：四档集合，`Load()` / `For()` 是唯一查表入口 | 新 |
| `data/combat/deflect_feedback.tres` | **新**：1 个 set ＋ 4 个 `sub_resource` | 数据 |
| `src/Audio/CombatSfx.cs` | 追加 `DeflectBlunt` / `DeflectThrust` / `DeflectDark` | ＋3 枚举（**只能在末尾**） |
| `tools/gen_placeholder_sfx.ps1` | ＋3 个音色生成函数（程序化合成，不引入外部素材） | 工具 |
| `assets/audio/sfx/sfx_deflect_{blunt,thrust,dark}.wav` | **新**：生成产物（含 `.import`） | 资产 |
| `src/Vfx/SparkBurst.cs` | 参数化：粒数 / 散布 / 初速 / 尺寸 / 颜色 / 寿命 | 重构 |
| `src/Vfx/CombatVfxDirector.cs` | 弹开时按 `e.AttackType` 查表 → 传给 `SparkBurst` | 接线 |
| `src/Audio/AudioDirector.cs` | 弹开走新的 `PlayDeflect(type)` | 接线 |
| `src/Dev/CombatVfxTest.cs` | 自检升级为**四档硬断言** | 验收 |
| `src/Dev/DeflectTypeShot.cs` ＋ `scenes/tests/DeflectTypeShot.tscn` | **新**：真链路四档截图机（带窗口） | 验收 |
| `src/Dev/AudioLeakProbe.cs` ＋ `scenes/tests/AudioLeakProbe.tscn` | **新**：音频资源泄漏**归属**探针 | 诊断 |
| `assets/references/t53_deflect_*.png` / `t53_sheet.png` | 验收截图 ＋ 带数值的对照图 | 证据 |

**没有碰**：`CombatResolver.cs`（裁决优先级）、`HumanoidAnimator.cs`（归 T38）、
`InputMap`（一个新输入都没有）。

---

## 三、四档数值（权威 = `data/combat/deflect_feedback.tres`）

| 档 | SFX | 音高 × | 音量 dB | 粒数 | 散布 | 初速 × | 尺寸 × | 颜色 | 寿命 |
|---|---|---|---|---|---|---|---|---|---|
| Slash | `Deflect` | 1.00 | 0 | 14 | 40° | 1.00 | 1.00 | `#c7e0ff` | 8 |
| Thrust | `DeflectThrust` | 1.28 | −1 | 9 | **18°** | **1.55** | 0.72 | `#ebffdb` | 6 |
| Blunt | `DeflectBlunt` | **0.72** | **+2** | **20** | **70°** | 0.62 | **1.50** | `#ffd18c` | 8 |
| Dark | `DeflectDark` | **0.55** | 0 | 16 | 55° | **0.50** | 1.25 | `#946bc7` | 8 |

两个刻意的设计选择，**验收时别当成 bug**：

1. **`Slash` 档沿用既有 `CombatSfx.Deflect`，不另造音效**——那个"金属叮"本来就是斩该有的声音。
   命名不对称是**故意的**：`Deflect` 就是斩击档，不是"通用档"。
2. **档位的 `PitchScale` 与连击升调（`AudioDirector.DeflectPitchScale`）相乘，不是替换**——
   02 §7 的"连着弹开、音越来越高"是核心反馈，不能被档位吃掉。

**寿命只能更短**：`SparkLifetimeFrames` 的硬上限是 `SparkBurst.DeflectLifetimeFrames = 8`，
消费点会 `Clamp`。因为 10 §4 第一原则是"火花不能盖住判定"，写大了也不会真越界。

---

## 四、证据（全部现跑，可复现）

### 4.1 单测 ＋ headless 全量：全绿

```
dotnet test tests/Oniblade.Tests      → 239/239 通过，失败 0
33 个 headless 场景逐个跑            → 全部 exit=0
   其中 21/36 combat vfx (CombatVfx.tscn) → exit=0   ← 本卡受影响的那一步
```

> ⚠️ **本机现在没有 pwsh 7**，`tools/check.ps1` 直接跑不了（5.1 会把 Godot 的 stderr
> 警告当成终止性错误）。上面这份是用 `.workbuddy/tmp/run_check_py.py` 驱动
> **同一份场景清单**得到的等价结果——断言仍在 Godot 场景里，没有第二份实现。
> 详见 §五 的"环境缺口"。

### 4.2 自检输出（`21/36`，headless）

```
[特效] 弹开火花：8 帧内消失（要求 ≤8）
[特效] 弹开·Slash：Deflect / c7e0ff / 14 粒 / 8 帧
[特效] 弹开·Thrust：DeflectThrust / ebffdb / 9 粒 / 6 帧
[特效] 弹开·Blunt：DeflectBlunt / ffd18c / 20 粒 / 8 帧
[特效] 弹开·Dark：DeflectDark / 946bc7 / 16 粒 / 8 帧
[特效] 弹开分档：四档映射正确、方向正确、两两可区分（数据 res://data/combat/deflect_feedback.tres）
[特效] ✓ 四种特效、寿命上限、战斗边界、降级顺序、弹开按攻击性质分档 全部通过
```

三条断言都是**硬的**：

1. **映射**：`set.Slash.Sfx == Deflect`、`Thrust == DeflectThrust`、`Blunt == DeflectBlunt`、
   `Dark == DeflectDark`——这条专门用来抓"枚举插在中间导致 `.tres` 整体错位"（见 §六 坑 1）。
2. **方向**：`Thrust.PitchScale > Slash.PitchScale`、`Blunt.PitchScale < Slash.PitchScale`
   （重的更低沉）。
3. **两两可区分**：**读场上的真 `GpuParticles3D`**（颜色 / 粒数 / 寿命），不是读配置回来再比。

> 卡片要求的 `Blunt 震动幅度 > Slash 震动幅度` **没有落成数**——因为项目里
> **没有相机震动系统**（§五）。这里用"`Blunt` 音更低、粒更散更大"替代，**并没有假装做到了**。

### 4.3 真链路截图机（带窗口，4/4）

`godot --path . res://scenes/tests/DeflectTypeShot.tscn` → `exit=0`

```
[T53图] 火花世界坐标 (0, 0.00018162967, 0)，屏幕投影 (522, 459)，视口 (1100, 700)
[T53图] 链路：攻击数据 Type=Slash  → 事件 AttackType=Slash  → 命中档位 Slash（等了 112 帧）
[T53图] Slash  → res://assets/references/t53_deflect_slash.png（第 165 逻辑帧）
[T53图] 链路：攻击数据 Type=Thrust → 事件 AttackType=Thrust → 命中档位 Thrust（等了 112 帧）
[T53图] Thrust → res://assets/references/t53_deflect_thrust.png（第 290 逻辑帧）
[T53图] 链路：攻击数据 Type=Blunt  → 事件 AttackType=Blunt  → 命中档位 Blunt（等了 112 帧）
[T53图] Blunt  → res://assets/references/t53_deflect_blunt.png（第 416 逻辑帧）
[T53图] 链路：攻击数据 Type=Dark   → 事件 AttackType=Dark   → 命中档位 Dark（等了 112 帧）
[T53图] Dark   → res://assets/references/t53_deflect_dark.png（第 539 逻辑帧）
[T53图] 结束：抓到 4/4 张
```

**为什么这是"真链路"而不是灌事件拍照**：假人会**还手**，玩家在判定帧前 6 帧按防御，
真的弹开 → `CombatArbiter` 真的把 `AttackData.Type` 填进事件 → 特效层真的查到档位。
灌事件只能证明"特效层会按类型画"，证明不了 `Arbiter` 带出了攻击性质。

### 4.4 像素级差异（`t53_sheet.png` 里也画了）

四张 1100×700，**同一台相机、同一个火花位置**：

| 对 | 像素差（曼哈顿阈值 30） |
|---|---|
| slash vs thrust | 2.02% |
| slash vs blunt | 3.03% |
| slash vs dark | 2.62% |
| thrust vs blunt | 2.47% |
| thrust vs dark | 1.66% |
| blunt vs dark | 2.36% |

亮像素 10.4%~12.4%（四张都确实拍到了火花，不是空帧）。

> **这一步是必须的**：T51 的教训是骨角 / 参数全对，屏幕上也可能只差 0.47%。
> 所以"分档生效"必须由**像素差**证明，不能只看配置对不对。

---

## 五、缺口（**没做**的，别以为做了）

| 缺口 | 为什么没做 | 影响 |
|---|---|---|
| **`CameraShake`** | **项目里没有相机震动系统**（`src/Vfx/` 只有 5 个文件，没有任何 shake 消费者／发射器） | 卡片列的五通道里少一个。要做先得有系统，属另一张卡 |
| **`HitStop`** | 顿帧在 `CombatResolver` 里，**属裁决不属反馈**；本卡硬约束明令"不许改裁决优先级" | 四档的顿帧差异**不存在**。要做要么放宽约束、要么在裁决后加一层反馈侧顿帧 |
| **火花的生成位置** | **既有 bug，不属本卡**：`CombatArbiter` 用 `hurtbox.GlobalPosition`，实测是**角色根节点高度**（`y ≈ 0.0002`），**不是胸口** | 游戏视角下火花出现在脚边。本卡只是把截图机对准了它，**没有改 gameplay** |
| **音频资源泄漏** | **既有**（机制见下），本卡只让它变多 | 见下 |
| **`tools/check.ps1` 在本机跑不了** | 本机**只有 PowerShell 5.1，没有 pwsh 7** | 见下 |

### 5.1 音频资源泄漏：是一条**既有**的账，但本卡让它变大了

退出时报 `ERROR: N resources still in use at exit`。**先证明它不是本卡的数据资源**：

`src/Dev/AudioLeakProbe.cs`（新探针，手动诊断用，不进门禁）：

| 模式 | 播了几个音效 | 报出来的资源在用 |
|---|---|---|
| 无参数（只 `Load()` 数据集 = 1 set ＋ 4 profile） | 0 | **0** ← 数据资源**零泄漏** |
| `-- Deflect`（**T53 之前就有**的音效） | 1 | **1** |
| `-- Deflect,DeflectThrust,DeflectBlunt,DeflectDark` | 4 | **4** |
| `-- Clash,Deflect,DeflectThrust,DeflectBlunt,DeflectDark` | 5 | **5** |

**结论**：报数 **== 播过的不同音效数**，而且**播一个 T53 之前就存在的音效就已经漏 1**
⇒ 机制**先于本卡存在**。

**机制**：`AudioDirector._cache`（`src/Audio/AudioDirector.cs:43`，
`Dictionary<string, AudioStream?>`）把**每个播放过的音效流**钉到进程结束。
`AudioDirector` 是 **Autoload**，退出时仍然存活 → 缓存里的东西全算"在用"。

**本卡让数量变大了**（必须说清楚，别甩锅）：弹开路径从"1 个音效"变成"4 个音效"，
按上面量出来的线性律，`CombatVfx` 场景的泄漏**由 2（拼刀 ＋ 斩）涨到 5（拼刀 ＋ 四档）**
——`clash4` 那一行精确复现了这个 5。
> 诚实标注：**"2" 是由线性律推出的，不是直接实测**（没有 T53 之前的干净基线：
> 工作区混着其他卡未提交的改动，不能回滚验证）。"5" 是实测的。

**根因不是本卡，但副作用是本卡的。**

**修法**（一处，不属 T53）：`AudioDirector._ExitTree()` 里 `_cache.Clear()`，或 `Resolve()` 干脆不缓存。

### 5.2 环境缺口：`check.ps1` 是 pwsh 7 专用的

`tools/check.ps1` 开头 `$ErrorActionPreference = 'Stop'`。**Windows PowerShell 5.1 会把
原生命令写到 stderr 的任何一行当成终止性错误**——Godot 退出时那句
`WARNING: N ObjectDB instances were leaked` 就让脚本在**第 5 步**抛 `NativeCommandError` 停住。
**这是 5.1 的行为差异，不是测试真的失败**（同一步单独跑 exit=0）。

本机 `C:\Program Files\PowerShell` 不存在、PATH 上没有 `pwsh`。

**临时办法**：`.workbuddy/tmp/run_check_py.py` —— 它**不实现任何断言**，
只是把 `check.ps1` 里的**同一份场景清单**抽出来逐个跑，保证只有一个来源。
**建议制作人定夺**：装 pwsh 7（改动最小），还是把 `check.ps1` 里对 native stderr 的处理改成
`$PSNativeCommandUseErrorActionPreference = $false` / 不用 `Stop`。

---

## 六、坑（每条都曾经给出过错误结论）

1. **枚举插在中间 = 静默错档。** `.tres` 里的枚举存的是**整数**（`Sfx = 2`）。
   往 `CombatSfx` 中间插一个值，所有既有数据整体错位，而且**不报错**。
   所以三个新值**只能追加在末尾**，且自检里有显式映射断言专门抓这个。
2. **`.tres` 不能有注释。** 第一版在 `.tres` 里写了 `; 说明`，Godot 的解析器不保证支持。
   改成纯数据，解释搬到 C# 的 `///`。
3. **自己加缓存 = 自己造泄漏。** `DeflectFeedbackSet` 最初存了个 `static _cached`，
   退出时报 "5 resources still in use"（1 set ＋ 4 profile 被钉住）→ 删掉缓存即好。
   `GD.Load` 对同一路径本来就返回同一实例，**不需要再存一份**。
4. **上一档的火花会被下一档捡走。** 自检里 `FirstLive<SparkBurst>()` 抓到的是**上一档还没消失**的那簇，
   于是"拼刀火花没标记成 clash"——一个和拼刀毫无关系的红灯。
   修法：每档检查后 `await WaitPhysicsFrames(DeflectLifetimeFrames + 2)` 清场。
5. **抓图必须等渲染帧，不是物理帧。** 粒子从 `Emitting = true` 到真画进帧缓冲要过渲染管线，
   渲染帧率低于物理帧（实测 163 渲染帧 ≈ 172 物理帧）。等 2 物理帧有时只换来 0~1 渲染帧
   → 抓到空帧 → 四张图两两像素差 **0.00%**，**看着像"分档没生效"，其实是根本没拍到火花**。
   改等 **3 个 `ProcessFrame`**。
6. **相机必须对准火花，不能对准"胸口"。** 火花实际在角色**根节点高度**（`y ≈ 0.0002`，
   不是注释里写的胸口）。按胸口架机位，投影落到视口外（实测屏幕 `(437, 1077)`，视口高只有 700）。
   改对准实测的 `y ≈ 0.12` 后投影 `(522, 459)`，进画面了。
7. **第一张图构图和后面三张不一样。** 第一张拍到的是"假人刚 spawn、玩家还没转向"的开场态
   （亮像素是后三张的两倍），四张并排没法比。修法：拍照前**先空跑一次弹开**。
8. **探针自己的打印会污染日志解析。** 探针那行 `...退出时的 'resources still in use'...` 里
   含同样的字样，按关键字 grep 会先命中它。
   解析引擎诊断必须锚 `^ERROR:` / `^WARNING:` 行首，且**别用 `\s`**（在多层引号里容易被吃掉，
   换成 `[ ]*` 立刻就对了——这个坑让我白跑了一轮全零）。

---

## 七、新增接口（后续卡直接用，别再造一份）

| 接口 | 位置 | 说明 |
|---|---|---|
| `HitEvent.AttackType` | `src/Core/GameEvents.cs` | 这次攻击的伤害性质；默认 `Slash`（所有非攻击结算都落斩击档） |
| `DeflectFeedbackSet.Load()` | `src/Combat/Data/DeflectFeedbackSet.cs` | 缺数据返回 `null`，**不抛异常** |
| `DeflectFeedbackSet.For(DamageType)` | 同上 | **唯一查表入口**，调用点零 `if` |
| `DeflectFeedbackProfile` | `src/Combat/Data/DeflectFeedbackProfile.cs` | 一档（SFX ＋ 火花参数），`[GlobalClass]` 可被 `.tres` 引用 |
| `SparkBurst.Create(pos, dir, profile)` | `src/Vfx/SparkBurst.cs` | 旧的 `Create(pos, dir, clash)` 保留未改（T28 路径零改动） |
| `SparkBurst.Profile` | 同上 | 当前这簇用的是哪一档，自检靠它断言 |
| `scenes/tests/DeflectTypeShot.tscn` | 场景 | 四档弹开的真链路截图机（**必须带窗口**） |
| `scenes/tests/AudioLeakProbe.tscn` | 场景 | 音频资源泄漏归属探针（手动，不进门禁） |

**降级行为**：`deflect_feedback.tres` 缺失 / 某档为 `null` → 退回 T53 之前的行为
（照旧有音效、照旧有火花），**不崩、也不静默无反馈**。

---

## 八、对照实验数据（汇总，验收用）

| 实验 | 控制 | 结果 |
|---|---|---|
| 枚举错位 | 显式断言四档映射 | 4/4 正确 |
| 方向 | `Thrust.Pitch > Slash.Pitch`、`Blunt.Pitch < Slash.Pitch` | 均成立 |
| 两两可区分（配置＋场上节点） | 读真 `GpuParticles3D` 的颜色／粒数／寿命 | 四档互不相同 |
| 两两可区分（屏幕） | 同机位四张 1100×700 | 1.66%~3.03%，全 > 1% |
| 数据资源泄漏 | 只 `Load()` 后退出 | **0** 个资源在用 |
| 泄漏线性律 | 播 0/1/4/5 个音效 | 0/1/4/5 → **线性**，且播旧音效就已漏 1 |
| `Slash` 档零回归 | 四项倍率全 1 | 与 T53 之前**逐字节同参数** |

---

## 九、验收怎么复跑

```powershell
# 1) 编译 + 单测 + 全部 headless 场景（本机没有 pwsh 7 时的等价门禁）
C:\Users\Gdy\.workbuddy\binaries\python\versions\3.13.12\python.exe .workbuddy\tmp\run_check_py.py

# 2) 只看本卡那一步
C:\Users\Gdy\.workbuddy\binaries\python\versions\3.13.12\python.exe .workbuddy\tmp\run_check_py.py CombatVfx

# 3) 四档真链路截图（★ 必须带窗口，无头是 dummy renderer 抓空帧）
G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe --path . res://scenes/tests/DeflectTypeShot.tscn

# 4) 出对照图（带数值标注 ＋ 两两像素差）
F:\ComfyUI-aki-v3\python\python.exe .workbuddy\tmp\t53_sheet.py

# 5) 有 pwsh 7 的机器上，正式门禁
pwsh -NoProfile -File tools\check.ps1
```

---

## 十、还没做的（按价值）

1. **相机震动系统**——卡片要求的 `CameraShake` 通道缺它。四档"打击更沉"目前只落在音频与火花上，
   缺"手上一沉"这一下。这是**唯一一个本卡想做而做不了的通道**，因为它是新系统。
2. **修火花的生成位置**（既有 bug，脚边 vs 胸口）。改动在 `CombatArbiter` 喂 `Position` 的地方，
   属 gameplay，**不在本卡范围**，但它是"弹开手感"里肉眼最可见的一处。
3. **音频资源泄漏**（§5.1）。一处 `_cache.Clear()` 的事，但会让退出日志变干净。
4. **T54（连锁一闪窗口）**：本卡把"性质"这一层铺好了，T54 决定"窗口"那一层。
   两卡的落点不同，**别指望 T53 顺手解决连击窗口**（单挑连击恒为 1 是窗口宽度问题，不是分档问题）。
5. **真机试玩验收 T44**：四档是"听起来不一样"，但"玩家能不能听出来"没人试过。
