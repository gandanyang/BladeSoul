# T38 交接 · 动画覆盖补齐（五个缺口动作已落地）

## 一句话

`PlayerGaps` 探针体检出的 **5 个"没有专属动画"的玩家动作**（闪避 / 喝血 / 跳跃 / 死亡 / 体干破裂）
全部补上，并且**把探针从"打印"升级成"硬断言"**——现在任何一个动作悄悄退回 idle 都会让
`check.ps1` 第 29 步变红。

改前实测：闪避 / 喝血 / 跳跃 / 死亡 = **0.1°**（≈ 完全没有专属姿势），体干破裂 **没有姿势**（`PlayGuardBreak` 只存在于灰盒 rig）。
改后实测：12 个动作**全部 ≥ 25°**（最低 51.1°），三对"必须一眼分得开"的姿势**全部通过**。

**`check.ps1` 第 29 步（掉落保护 + 动画覆盖）绿。** 见第四节关于第 35 步（T52 的卡）的说明。

---

## 一、交付物

### 1.1 改动的文件

| 文件 | 改了什么 | 规模 |
|---|---|---|
| `src/Player/HumanoidAnimator.cs` | 5 个动作通道 + 5 条姿势方法 + 6 个公开入口；优先级链扩到 9 级 | +443 / −36 |
| `src/Player/PlayerActor.cs` | **6 个接线点**（见 1.3）。⚠️ 该文件同时含有 **T52 未提交的处决改动**，本卡的改动全部带 `// T38：` 前缀，可逐行辨认 | 本卡 6 处 |
| `src/Dev/PlayerGapsTest.cs` | 探针 → 硬断言（自漂移阈值 + 每动作 ≥25° + 三对可区分） | +95 |
| `src/Dev/T38ActionShot.cs` | **新增**：真机连帧截图机（真接线驱动，非直接调动画器） | 新增 |
| `scenes/tests/T38ActionShot.tscn` | **新增**：2 行场景，指向上面脚本 | 新增 |

### 1.2 新增的公开接口（`HumanoidAnimator`）

```csharp
public bool IsDodging          => _dodgeLeft > 0;
public bool IsPostureBroken    => _brokenLeft > 0;

public void PlayDodge(Vector3 worldDirection, int totalFrames);  // 世界方向 → 前/侧分解，左右扑与前后扑轮廓不同
public void TrackJump(int phase, int landRecoveryFrames);        // phase: 0=蹬地 1=腾空 2=落地缓冲 -1=不在跳
public void PlayHeal(int startupFrames, int drinkFrames, int recoveryFrames);
public void PlayDeath(int totalFrames);
public void PlayGuardBreak(int totalFrames);
```

**帧数一律由调用方从数据/状态里读**（如 `heal.StartupFrames`、`ReviveState` 的演出帧数），
动画器本身不写死任何数字——沿用本工程"一个数字一个来源"的纪律。

### 1.3 接线点（`PlayerActor.cs`，6 处）

| 位置 | 接法 |
|---|---|
| `TryEnterDodge()` | 切 `DodgeState` 后 → `PlayDodge(dodgeDirection, InvulnerableFrames + RecoveryFrames)` |
| `TryEnterHeal()` | 切 `HealState` 后 → `PlayHeal(startup, drink, recovery)`（三段帧数直接来自状态字段） |
| `EnterRevive()` | 原先是"复用受击姿势"的占位 → 换成 `PlayDeath(performance)` |
| `OnPostureBroken()` | 在原有 `_rig?.PlayGuardBreak()` 旁**追加** `_skinAnimator?.PlayGuardBreak(...)`（真模型也有专属破防姿势） |
| `JumpAnimPhase()` | **新增私有助手**：读 `Machine.Current is JumpState` + `IsOnFloor()` + `Velocity.Y` 反推跳跃段（`JumpState` 内部字段是私有的，不碰它） |
| `OnTickVisual()` | 每帧 `_skinAnimator.TrackJump(JumpAnimPhase(), JumpProfile?.LandRecoveryFrames ?? 0)` 后再 `AnimateLocomotion` |

> `JumpAnimPhase` 为什么在动画侧反推：`JumpState` 的三个阶段字段是私有的，
> 本卡不去改别的卡的状态机，改成"从可观测的外在量（离地与否 / 垂直速度）推导"。

### 1.4 姿势各覆盖全部 12 根 Tracked 骨

**这是本卡最容易踩的坑**：`AnimateLocomotion` 每帧先 `ResetBonePoses()`，随后 `AnimateCombat`
用战斗姿势**覆盖**。所以任何新动作若不把 12 根骨**全部**写一遍，就会**漏出走路姿势**
（例如只动手臂、腿还保持上一帧的行走帧）。5 条 Pose 方法逐条写满 12 根。

---

## 二、验证（可复现）

### 2.1 骨角差实测（`check.ps1` 第 29 步 / `PlayerGaps.tscn` 的真实输出）

| 动作 | 与 idle 最大骨角差 | 判定 |
|---|---:|---|
| idle 基准（自身漂移） | 0.1° | ✓ ≤ 5° 阈值 |
| 走 / 跑 | 72.4° | ✓ |
| 格挡（按住） | 97.7° | ✓ |
| 弹开成功 | 84.7° | ✓ |
| **体干破裂** | **74.5°** | ✓（改前：无姿势） |
| 普攻（三连共用） | 133.7° | ✓ |
| 一闪 | 99.8° | ✓ |
| 受击 | 51.1° | ✓ |
| **闪避** | **88.8°** | ✓（改前 0.1°） |
| **喝血** | **115.0°** | ✓（改前 0.1°） |
| **跳跃** | **114.6°** | ✓（改前 0.1°） |
| **死亡** | **134.6°** | ✓（改前 0.1°） |

### 2.2 三对"必须一眼分得开"（防"两个动作其实长得一样"）

| 对照 | 骨角差 | 为什么必须分 |
|---|---:|---|
| 格挡 vs 弹开成功 | 54.2° | 弹开是"成功的那一下"，不能和"举着盾"同形 |
| 格挡 vs 体干破裂 | 144.0° | 体干破裂要**手臂下垂 + 上身后折**（惩罚感），与格挡的前倾完全不同 |
| 死亡 vs 受击 | 134.6° | 受击是**挨一下**，死亡是**倒下**——给玩家的信息完全不同 |

### 2.3 真机截图 + 像素级对照（防"骨角全对但画面上看不出"）

T51 的教训：骨角全绿也可能只有 0.47% 的像素差。所以本卡额外做了**侧视机位**连帧截图
（`T38ActionShot.tscn`，**必须带窗口跑**，headless 是 dummy renderer 抓空帧），
并逐张量化像素差（1100×760，采样步长 2）：

每个动作 vs idle：

| 动作 | 像素差 |
|---|---:|
| 格挡 | 16.72% |
| 弹开成功 | 40.33% |
| 体干破裂 | 24.39% |
| 闪避 | 25.48% |
| 跳跃（腾空段） | 16.69% |
| 喝血（饮用段） | 16.72% |
| 死亡（倒下段） | 21.89% |
| 死亡（伏着段） | 22.77% |

三对必分姿势（像素差）：

| 对照 | 像素差 |
|---|---:|
| 格挡 vs 弹开成功 | 37.26% |
| 格挡 vs 体干破裂 | 21.70% |
| 弹开成功 vs 体干破裂 | 27.07% |

> 机位从"身后斜俯"改成"正侧视"（`(3.05,1.42,0.55) → (0,0.92,-0.85)`，Fov 45）后，
> 像素差整体**翻倍**——原来的机位里很多姿势被身体自身遮挡，正是 T51 那种"数值对、看不出"的陷阱。

### 2.4 截图产物（`assets/references/`）

- 单帧 16 张：`t38_{idle,dodge×3,jump×3,heal×3,guard,deflect,guardbreak,death×3}_*.png`
- 三帧拼图 5 张：`t38_sheet_{dodge,jump,heal,death,three}.png`
  （标签依次为 起手/最低点/收势、蹬地/腾空/落地缓冲、掏壶/饮用/收招、倒下/伏着/撑起、格挡/弹开成功/体干破裂）

截图机是**真接线驱动**的（走 `TryEnterDodge()` / 喝血状态 / `Die()` / `ApplyPosturePercent(1f)` 这些生产入口，
不直接调动画器），所以它同时也在端到端验证接线本身。

---

## 三、与 T52 的边界（为什么两卡不冲突）

- T38 的卡面写明**玩家姿势表归 T38**，T52 是**怪物动作 + 破韧处决**。
- 本卡只碰：`HumanoidAnimator.cs`（玩家动画器）、`PlayerActor.cs` 里**动画接线那 6 行**、
  以及两个 dev 探针。**一个字节都没碰** `src/Enemies/`、`AshigaruAnimator`、处决状态机、数据 `.tres`。
- `PlayerActor.cs` 是唯一与 T52 共用的文件——T52 在 22:41 往里加了处决键位优先级与
  `DeathblowExecuteState` 注册，本卡在 22:57 加动画接线。**两边都编译干净、无冲突**
  （本卡接线点与处决逻辑完全不相邻）。

---

## 四、`check.ps1` 现状（必读）

`check.ps1` 共 35 步。本卡修改的**第 29 步**（掉落保护 + 动画覆盖）**全绿**，且因为
我是把**已存在的步骤**变成硬断言，**没有改动 `check.ps1` 本身**（零行改动即进绿门）。

**但是：第 35 步（`EnemyDeathblow.tscn`，T52 的卡）在本次运行时红**，30 通过 / 1 失败。
失败断言是 T52 自己的：

```
✗ B3 判定帧之前目标还活着（伤害不是立即结算）
   (第 48 帧存活 0（1=活着），实际死于第 48 帧)
```

诊断行：`演出 87 帧，判定帧配置=50，敌人死于第 48 帧`。

**判定：与本卡无关。** 依据有三，逐条可复核：
1. 该断言量的是**敌人**的死亡帧 vs 处决 `HitFrame`（`DeathblowExecuteState.Tick()` 里
   `frame >= HitFrameValue` 才结算），与玩家骨头姿势无任何耦合；
2. 本卡新增的动画入口是**纯视觉状态**（`PlayDeath` 只写 `_deathTotal/_deathElapsed`，
   `PlayGuardBreak` 只写 `_brokenTotal/_brokenLeft`），不进任何逻辑；
3. `src/Dev/EnemyDeathblowTest.cs` 的 mtime（23:05）**晚于**本卡文件（22:57–22:59）——
   T52 在交完 handoff（声明"35 步全绿"）之后**仍在改它自己的测试**，属"别的 agent 写到一半"。
   重跑一次仍然是同样的红（死亡帧恒 48），说明这是 T52 探针的**帧对齐差 2 帧**（测试循环
   `i2` 从按键后才起算，落后状态机 `FramesSinceStart` 约 2 帧），不是随机抖动。

**给 T52 的线索**：把探针里 `i2 == hitFrame - 2` 的采样点改成与状态机 `FramesSinceStart`
同源（或在按键后先记一帧基准），红点即消。

---

## 五、坑 / 遗留

1. **12 根骨必须写满**：新动作漏写哪根，那根就漏出走路的上一帧（见 1.4）。
2. **世界方向要分解**：闪避左右扑与前后扑轮廓不同，`PlayDodge` 用
   `_root.GlobalTransform.Basis` 把世界方向拆成前/侧两个分量，不直接吃世界向量。
3. **跳跃段在动画侧反推**：`JumpState` 阶段字段私有，用 `IsOnFloor()` + `Velocity.Y` 推，别去改状态机。
4. **`T38ActionShot.tscn` 里的 `Health.Heal` 复位是必需的**：闪避是无方向输入的后撤跳，
   会把玩家位移到 ~4.15m 外，超出假人 2.2m 的攻击距离 → 假人永不出招 → 弹开那张图拍不到。
   拍摄前把玩家复位到原点、清零速度、补满血。
5. **截图机里有一条诊断 `GD.Print`**（打印玩家模型路径 / `VisualModel` 是否在子节点里）——
   用来排除"灰盒降级"的误判，留着无害，需要干净日志可删。
6. 真模型目前**没有武器挂点**（T27 遗留）——本卡不涉及，但死亡/喝血姿势里"手的位置"
   在武器挂上去之后可能要再微调。

---

## 六、复现命令

```powershell
# 只看本卡（第 29 步）
& 'G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' `
    --headless --path G:\Game res://scenes/tests/PlayerGaps.tscn

# 重新出截图（必须带窗口）
& 'G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' `
    --path G:\Game res://scenes/tests/T38ActionShot.tscn

# 全量门禁
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check.ps1
```
