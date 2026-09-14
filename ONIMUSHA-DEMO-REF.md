# 鬼武者：剑之道 试玩 demo — 参考笔记

> 来源：本地 `G:\SteamLibrary\steamapps\common\OnimushaWotS_Demo`（Steam appid **3974650**，RE Engine）。
> 本笔记**只做分析与命名学参考**：不复制、不导出、不反编译任何 Capcom 资产进本仓库。
> 生成时间：2026-09-15。

## 一句话

试玩的 12.6GB 资产全部封在 `re_chunk_000.pak`（`KPKA` v4，30,343 个文件，目录表加密），
**读不到模型/动画本体**；但有两处是明文的，且对本项目价值极高：

1. **`config.ini` → 整套画面配方**（可直接照搬到我们的 `WorldEnvironment`）。
2. **`OnimushaWotS_Demo.exe` 的字符串表 → 完整的战斗动作分类学**（约 18,542 条全大写常量）。
   这一条直接回答本项目 `07 §1.1`「玩家约 28 个动作」该长什么样、我们漏了什么。

---

## 一、画面配方（来自 `config.ini`）— **只作"他们做了什么"的记录**

下表是 Onimusha 自己的配方。**读它之前先问"他们为什么去饱和"**：它服务于**他们的题材**
（时代感、材质、血液、烟雾、暗部层次、和风建筑，以及一套偏电影化的镜头语言）。
**`0.64` 不是一个"优秀数字"，是一个服务于他们画面的解。**

> ★ **制作人 2026-09-15 裁定：不全盘复制，只借"精神"。**
> 我们的题材是**乡村 / 海岛 / 灯塔 / 春天 / 夜空**，核心审美是**自然、温暖、克制**。
> 直接套 `Sat=0.64 + 暗角 + 颗粒 + 色差 + 畸变 + 景深`，得到的只会是**"现代游戏后处理味"**，
> 而不是我们要的东西。**该借的是：整体略降饱和 ＋ 控住高光 ＋ 暗部压一点 ＋ 适度镜头语言**——
> 不是照抄参数表。

下表仅作对照记录：

| 项 | 值 | 备注 |
|---|---|---|
| `Fov` | **40** | 比我们截图机用的 45 更紧、更"贴人"。战斗动作游戏常用 38~42 |
| `Saturation` | **0.64** | ★ **刻意去饱和**。我们现在的画面如果偏艳，这就是差距所在 |
| `Gamma` | 1.05 | 略微提亮 |
| `VignetteOption` | **True** | 暗角 |
| `FilmGrainEnable` | **True** | 胶片颗粒 |
| `ChromaticAberration` | **True** | 色差 |
| `LensDistortionSetting` | **ON** | 镜头畸变 |
| `LensFlareEnable` / `GodRayEnable` | True / True | 镜头光/体积光 |
| `DepthOfFiledEnable` | True | 景深 |
| `MotionBlurEnable` | True（但 `[Graphics] MotionBlurOption=False`） | 两处冲突，以实际观感为准 |
| `ColorSpace` / `DisplayMaxNits` / `WhitePaperNits` | AUTO / 1100 / 50 | HDR 走自家 tonemap |
| 质量档 | AO STANDARD、SSR HIGH、Shadow HIGH、TAA、Texture HIGH | 中高配基线 |

**对 Godot 的映射**：`Camera3D.fov`、`Environment.adjustment_saturation`、tonemap 可直接对应；
**暗角 / 颗粒 / 色差 / 镜头畸变 Godot 核心没有内置**，需要一张屏幕空间后处理 shader
（本项目已有 `data/world/atmosphere_rainy_night.tres` 与 10 / 16 号文档的氛围层，可挂在同一处）。

> ⚠️ 这是**观感取向**的输入，不是必须照抄。要不要把我们的画面往"更去饱和 + 后处理堆叠"走，
> 属制作人裁定，我没有擅自改任何 `WorldEnvironment`。

---

## 二、动作分类学（来自 exe 字符串表）

`OnimushaWotS_Demo.exe` 里存在大量 `<X>k__BackingField`（C# 自动属性）与
`ACTION_* / ATTACK_* / *_STATE` 常量。**可执行文件中出现大量典型 .NET/C# 编译产物命名痕迹，
表明至少存在明显的托管/C# 代码层；具体玩法逻辑的实现边界不能仅凭字符串表确定。**
（2026-09-15 制作人降级措辞：原稿写成"说明这作的玩法层是 C# 写的"——证据链只到"存在托管代码层"，
"实现边界"不能由字符串表断言。留档文档按此口径。）
它对本项目的意义**只是命名与分层的借鉴**，不作为技术栈论断。

### 2.1 ★★ 一闪（Issen）不是一个动作，是**五个入口**

| Onimusha 常量 | 含义 | 我们现状（`07 §1.1`） |
|---|---|---|
| `NORMAL_BLOCK_ISSEN` / `BLOCK_ISSEN`(+`_00/01/02`) | 格挡后一闪 | ❌ 无 |
| `COUNTER_ISSEN`(+`_FRONT/_BACK/_START_B`) | 反击一闪（对出招） | 部分 = 我们的 `issen` + 弹开窗 |
| `BREAK_ISSEN`(+`_COMBO_1..7`) | 破防后一闪 | ❌ 无 |
| `CHAIN_ISSEN`(+`_COMBO_1..7`) | **连锁一闪** | 🟡 我们有 `issen_chain`(P1，标注"可复用 issen") |
| `RIKIDO_ISSEN` / `SLIDING_CHAIN_ISSEN` | 力动 / 滑行连锁一闪 | ❌ 无 |
| `GUARD_BREAK_ISSEN` / `GUI_BREAK_ISSEN` | 破韧一闪 | ❌ 无 |
| `MULTIPLE_BLOCK_ISSEN_ACTION` | 多重格挡一闪 | ❌ 无 |

**可借鉴**：他们把"一闪"按**触发来源**分类（格挡 / 反击 / 破防 / 连锁），
每类有自己的动作与判定参数。我们的 `IssenState` 是单入口——`02 号文档`若要扩，
这是最现成的分类骨架。

### 2.2 ★★ 连锁一闪 = **慢镜窗口 + 窗口内按继续 + 超时收尾**

```
CHAIN_ISSEN_SLOW_START          ← 进入慢镜（顿帧/时间缩放）
CHAIN_ISSEN_INPUT_JUST          ← 窗口内"精准输入"判定
CHAIN_ISSEN_SLOW_END_INPUT      ← 窗口内按了 → 继续连锁
CHAIN_ISSEN_SLOW_END_TIME_OUT   ← 窗口到点没按 → 收尾
CHAIN_ISSEN_COMBO_1..7          ← 最多连 7 段
CHAIN_ISSEN_FINISH_1 / _2 / _B / _FINISH_FAILURE
CHAIN_ISSEN_ONE_HAND_GUARD / TWO_HAND_GUARD  ← 连锁中还能格挡
CHAIN_ISSEN_FAILURE_EMPTY / ONEHAND / TWOHAND ← 失败细分
```

**对本项目的直接意义**：我们的连击数只有 `CombatActor.DeflectChain` + 90 帧窗口
（且实测单挑恒为 1 —— 假人出招间隔 118 帧 > 窗口 90 帧）。
Onimusha 的做法是**在连锁窗口内做时间缩放（慢镜）**，窗口开合由慢镜演出而不是纯倒计时——
这既解决了"窗口要多大"的尴尬，又给玩家"再来一刀"的兴奋感。
**要不要给我们的连锁窗口配慢镜，是可以拍板的一条**（也能顺带让弹开音高递增在多敌人外也被听到）。

### 2.3 ★★ 拼刀（Blade Lock）是一个**完整状态机**，我们还没有它

```
BLADE_LOCK_START → BLADE_LOCK_LOOP / BLADE_LOCK_GRIND（较劲）
                 → BLADE_LOCK_WIN  或  BLADE_LOCK_FAILURE
BLADELOCK_KATATE / RYOTE        ← 片手 / 両手 两套
BLADELOCK_RAPID / BLADE_LOCK_LOOP_RAPID
BLADE_LOCK_INFERIOR_START/LOOP/END   ← ★ 劣势分支
BLADE_GLOW + BLADE_GLOW_INPUT_JUST   ← 发光 + 精准输入
CAN_BLADE_LOCK / CHKPROG_CAN_BLADE_LOCK / MIDST_BLADE_LOCK
```

我们的 `07 §2.1` 只有一条 `clash` 音效，**没有拼刀状态机**。
它是"弹开"的兄弟机制，且它的"优势/劣势分支 + 精准输入发光"是很值得抄的结构。

### 2.4 ★ 弹开/防御被拆成**三套**，而且弹开特效**按伤害类型分四种**

| 常量族 | 含义 |
|---|---|
| `JUST_GUARD`(+`_BLOCK`, `GREAT_JUST_GUARD_NOW`, `JUST_GUARD_CHANCE_{LARGE,SMALL,VERY_SMALL}`) | **精准防御**，且成功率**分三档**（按攻击威胁度） |
| `ATTACK_AFTER_JUSTGUARD_SLOW_START/END` + `AfterParry_SlowFrame/SlowRate` | 精准防御/弹开后**进慢镜** |
| `PARRY`（759 命中）：`PARRY_ATTACK_START/NORMAL/STRONG`、`PARRY_LARGE`、`AfterParryRollingModule` | **弹开**是独立一整套，弹开后可接攻击 |
| `GUARD_S / GUARD_M / GUARD_L` + `GUARD_UKE` / `GUARD_YOROKE` | 格挡**按攻击轻重分三档受击反应**（受け / よろけ） |
| `DIRECTIONAL_GUARD_F/B/L/R` + `NONE_DIRECTION_GUARD` | **四向格挡** |
| `SLICE_DEFLECT` / `BLUNT_DEFLECT` / `PIERCE_DEFLECT` / `SHOT_DEFLECT` | ★ **弹开按伤害类型分四种**（斩/打/突/射） |

**可借鉴**：
- 弹开特效/音效**按伤害类型分**——本项目已有斩/打/突的伤害类型概念，这条几乎零成本可加，反馈更"对味"。
- 格挡**按攻击轻重分受击档**（小/中/大的身体反应不同）——我们的格挡三态是"抬起/维持/放下"，
  这是**时间轴**上的三态；他们补的是**力度轴**上的三档。两者正交，可叠加。

### 2.5 闪避：分**大小两档** + **消耗资源** + **精准闪避反击**

```
DODGE_LEFT_LARGE / _SMALL, DODGE_RIGHT_LARGE / _SMALL, DODGE_BACK_LARGE / _SMALL
DODGE_TURN / JUMP_DODGE / DODGE_SLOW / DODGE_NO_HIT / DODGE_WALL
DODGE_RESOURCE_CONSUME / DODGE_RESOURCE_RECOVER   ← ★ 闪避消耗资源
JUST_DODGE / JUST_DODGED / IsAttackJustDodged
JUST_DODGE_ATTACK_* + JustDodgeAttackGauge / Rate / Value / SlowType / Slow
JUST_DODGE_AIR_ATTACK_NORMAL / _STRONG / _START  ← ★ 空中精准闪避反击
```

我们的 `dodge_f/b/l/r`（四向，单一档）。他们的"大小两档"是**长按/轻按**语义；
`JUST_DODGE_ATTACK` 是"精准闪避 → 立刻反击"，比我们的"无敌帧躲开"多一层收益。
⚠️ 但注意本项目 `02 §8` 曾裁定删掉过 `PerfectDodgeGraceFrames`——**要不要引入"精准闪避"要过制作人**，
别再走回头路。

### 2.6 其余可对照项

| 主题 | Onimusha | 我们 | 差异要点 |
|---|---|---|---|
| 连段 | `ATK_COMBO_1..7` + `COMBO_CANCEL` + `COMBO_3RD` | 轻斩三连 | 他们 **7 段**；`COMBO_CANCEL` 是取消窗 |
| 蓄力 | `ATTACK_CHARGE_ONE_HAND` / `_TWO_HAND_START/END/DASH` | `attack_charge_1/2/3` | 他们按**持刀式**切，我们按**段**切 |
| 突刺 | `ATTACK_SLASH_REVERSE` / `THRUST_PARRY_ATTACK_*` | `attack_thrust` | 他们突刺能"被弹开后再接" |
| 处决 | `COUNTER_GRAB`(+`_FAILURE/_RELEASE`)、`GRAPPLE_COUNTER_SUCCESS_{UP,DOWN}` | `deathblow_execute` / `_receive` | 我们的忍杀 ≈ 他们的 grab counter |
| 死亡 | `DIE_CHAIN_ISSEN` / `DIE_BREAK_ISSEN` / `DIE_COUNTER_ISSEN` / `DIE_FULLY_*` | `death` | ★ 他们**按死因分死亡动画** |
| 吸魂 | `SOUL` 1904 条：`SOUL_ABSORB_S/M/L`、`BLACK_SOUL_ABSORB`、`SOUL_BOOST`、`SOUL_ENHANCE` | `soul_absorb`(P1) | 吸魂是他们的**核心系统**（含黑魂/魂强化），我们只是 P1 一个动作 |
| 换武器 | `IsKatana2Lwep` / `Katana4Lwep2Katana` / `HashPropName_WeaponOnOff_*` | 单刀 + 笼手 | 他们武器位多，动画要乘 |
| 锁敌 | `LOCK_ON` + `LOCK_ON_CHANGE_{UP,DOWN,LEFT,RIGHT}` | 我们有锁定 | 一致 |

### 2.7 ★ 为什么会"28 个不够"——`KATATE / RYOTE`

`BLADELOCK_KATATE` / `BLADELOCK_RYOTE`、`GUARD_ONE_HAND` / `GUARD_TWO_HAND`、
`GUARD_BREAK_HEAVY_KATATE` / `_RYOTE`、`JUST_GUARD_ONE_HAND_IDLE` / `_TWO_HAND_IDLE`、
`CHAIN_ISSEN_ONE_HAND_GUARD` / `_TWO_HAND_GUARD`、`CONTACT_DODGE_ONE_HAND_*` / `_TWO_HAND_*` …

**片手（単手持ち）/ 両手（両手持ち）是一个"持刀式"维度，几乎每个动作都有两套变体。**
这解释了为什么同代作品的动画量远超 28。本项目 `07 §1.1` 是**单手持刀**，
若要扩到双持刀式，动作数要 ×2 —— 这是个**范围决策**，不是美术决策。

### 2.8 ★ 他们的动画系统是"分层 + 叠加 + IK"，正是我们程序化方案的"正规解"

```
MOTION_LAYER_BODY / MOTION_LAYER_BEND_FB / MOTION_LAYER_BEND_LR   ← 分层
MOTION_ADD / ADDITIONAL_HIT_ISSEN                                  ← 叠加层
USE_MOTION_SINGLE / DOUBLE / MULTI / STRAFE                        ← 单/双/多/侧移
MOTION_IK_LEG / MOTION_IK_LOOK_AT                                  ← IK
MOTION_ROOT_TRANS_OFF / MOTION_ROOT_ROTATION_OFF                   ← ★ 根位移/根旋转开关
ENABLE_MOTION_MOVE_INHERIT / MOTION_SPEED / MOTION_SYNC_POINT      ← 位移继承 / 同步点
MOTION_INTERPOLATION_STATE / GuardInterpolateBeforeFrame/AfterFrame ← 插值（=起手/收招帧）
```

两条对本项目**立刻有用**：

1. **`MOTION_ROOT_TRANS_OFF` / `MOTION_ROOT_ROTATION_OFF`**：
   我们的程序化动画器是直接写骨旋转，**根骨没有"关掉位移"的开关**——
   T38 的闪避/跳跃之所以要额外写 `_root.Position`，本质就是在手工做这件事。
   把它显式化成"根位移通道"，比每个动作各自处理更干净。
2. **`MOTION_SYNC_POINT`**：处决（`DeathblowExecuteState`）和拼刀都需要**双方动作对齐到同一节拍**。
   T52 的处决现在靠"目标被钉住 + 距离关系不变"来近似；同步点才是正解。

---

## 三、明确**没有**的东西（避免误抄）

- **他们没有 Sekiro 式"体干槽"**。`Posture` 一族（`CharacterPostureType` / `OverwritePosture` /
  `MotionMergeBasePosture` / `PostureJointHash`）指的是**姿势/架势（pose）**，不是我们的
  `PostureMeter`。**本项目的体干系统是自己的设计**（`02`/`T37`），别在文档里说成"参考鬼武者"。
- `stamina` 只有 5 条、`focus` 191 条（多为渲染聚焦）——**没有体力条**这类资源（闪避资源是唯一例外）。
- `execut` 846 条几乎全是渲染器的 `executeDraw*`，**不是**处决——别被关键字骗了。

---

## 四、制作人裁定（2026-09-15）

> 结论一句话：**不要搬它的动作数量，搬它的"分类方式"。**
> 我们真正的问题不是"28 个动作太少"，而是**其中一些动作混在了同一个维度里**。

### 4.1 采纳（按优先级）

| 优先级 | 事项 | 落点 |
|---|---|---|
| **P0** | **弹开反馈按 `DamageType` 分档**（`Slash/Thrust/Blunt/Dark` → VFX / SFX / 震动 / 顿帧 / 音高） | `TASKS.md` **T53** |
| **P0** | **连锁一闪窗口逻辑重设计**（由"下一次有效攻击机会"驱动，**不是**先上慢镜） | `TASKS.md` **T54** |
| **P1** | **拼刀最小状态机**（`Start → 短暂僵持 → 快速输入 → 成功/失败`，**只做一版**） | `TASKS.md` **T55** |
| **P1** | **格挡受击力度档**（"抬起/维持/放下"的**时间轴**与"轻/中/重"的**力度轴**分开，不重建 Guard FSM） | `TASKS.md` **T56** |
| **P2** | **`CombatSyncPoint`**（**先只用于 `Deathblow`**，以后再复用到拼刀 / 特殊攻击） | 暂不建卡，写进 T54/T55 前置 |

**两条拍板依据**（记下来，免得日后反复）：
- 弹开分档：`DamageType` 与 `CombatSfx` **本来就在代码里**（`src/Combat/Data/CombatEnums.cs` /
  `src/Audio/CombatSfx.cs`），本质是**把已有数据接进反馈层** → 标准的数据驱动表现，成本极低。
- 格挡两轴正交：现有三态是**时间状态**，力度档是**结果状态**，**可叠加，不需要推翻 Guard FSM**。

### 4.2 明确驳回 / 暂缓（防范围蔓延）

| 事项 | 裁定 | 理由 |
|---|---|---|
| 连锁一闪**先上慢镜** | ❌ 暂缓（降为第二层表现） | 慢镜是表现，不是窗口设计；先修 T54 的逻辑 |
| **精准闪避反击**（`JUST_DODGE_ATTACK`） | ❌ 继续不做 | 我们删过 `PerfectDodgeGraceFrames`（T21）；这是**增强高手上限**的系统，不是让基础战斗成立的系统 |
| **片手/両手 双持刀式**（动作数 ×2） | ❌ **不做** | 第一版**固定一种持刀姿态**，并定义为项目自己的战斗美学：**偏单手、轻盈、近距离、精准**。程序化动画下真正的瓶颈是**维护成本**，不是 CPU |
| 全盘照抄**画面配方**（`Sat=0.64` 等） | ❌ 不抄 | 见 §一；只借"略降饱和 + 控高光 + 压暗部 + 适度镜头语言" |
| **大量新增动作**（28 → 40/50） | ❌ 不优先 | 见 4.3；先把"清单"升级成"组合规则" |

### 4.3 核心判断（本笔记最重要的一条）

> **"28 个动作是不是太少？" —— 不是。**
> 真正的问题是：**要不要把"动作数量"从一个列表，升级成一套组合规则。**
>
> 成熟动作系统的力量不在字符串数量，而在：
> ```
> 少数玩家输入 × 大量状态 × 触发来源 × 攻击属性 × 时序 × 同步 × 反馈
> ```
> 这才是真正适合我们这种**程序化动画 + Godot C# + 小规模独立项目**的乘法。
> **不要把 28 堆到 50，要把 28 用出 50 的表达力。**
>
> 落地：`07 §1.1` 已升级为「玩家动作 × 触发条件 × 结果状态 × 反馈」矩阵（§1.1.1~1.1.3）。
>
> ★ **"同一动作的多个语义出口"已有一半基础**：`IssenKind` 里现在就躺着
> `Shin / Deflect / Dodge / Clash / Chain` 五种——**不需要新增玩家输入，只需要接线**。
> （`Dodge` / `Clash` 两个出口随 §4.2 的"暂缓"一起先不接。）

---

## 五、复现

```python
# 读画面配方
G:\SteamLibrary\steamapps\common\OnimushaWotS_Demo\config.ini

# 抽取动作常量（用已有脚本，输出到 .workbuddy/tmp/）
F:/ComfyUI-aki-v3/python/python.exe G:/Game/.workbuddy/tmp/scan_actions.py
F:/ComfyUI-aki-v3/python/python.exe G:/Game/.workbuddy/tmp/scan_exe.py
#   → onimusha_actions.txt / onimusha_exe_strings.txt
```

**边界**：`.pak` 内的模型/动画/贴图**不解包、不复用**；本笔记只用到了
明文配置与可执行文件的**标识符**（用于分类学对照）。
