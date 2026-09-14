# 战斗反馈诊断 · 弹开 / 被击败

**日期**：2026-09-15
**范围**：制作人点名「弹开和被击败的效果还是有问题」。
**状态**：①② **已修复并验证**（2026-09-15 深夜，制作人拍板「继续」）；③ 修复方案就绪，**等制作人给一个数**。

---

## 一句话

三条**独立**缺陷，其中两条会互相掩盖，所以看起来「时好时坏、说不清哪里不对」：

| # | 玩家看到的 | 病根 | 证据强度 |
|---|---|---|---|
| ① | 弹开/命中火花、血雾出现在**脚底** | 接触点取的是角色**根节点**，不是受击框 | 代码 + 实测坐标 |
| ② | 足兵**只有半截身子**（下半身埋在地下） | 模型没对齐，比碰撞体低了 **0.875 m** | 包围盒 + 对照截图 |
| ③ | 敌人被打死后**僵在原地不倒** | 死亡演出没接到真实链路上 | 姿态 0.00° + 逐像素相同 |
| ④ | 上面三条**全都没被自检发现** | 探针自己造帧号，绕过了真实调用路径 | 代码比对 |

---

## ① 火花/血雾落在脚底

`src/Core/CombatArbiter.cs:169`：

```csharp
// 接触点用受击框的位置：它就在胸口高度，正是火花该出现的地方。
Position = hurtbox.GlobalPosition,
```

**注释写的是「受击框」，代码取的是「Hurtbox 这个 Area3D 节点」**——而这两者在数据上是分开的：

`scenes/actors/Player.tscn:51-59`（敌人 `Ashigaru.tscn:38-46` 同构）：

```ini
[node name="Hurtbox" type="Area3D" parent="."]        # ← 没有 transform = 位置 (0,0,0) = 脚底
...
[node name="CollisionShape3D" type="CollisionShape3D" parent="Hurtbox"]
transform = Transform3D(1,0,0, 0,1,0, 0,0,1, 0, 0.9, 0)   # ← 真正的受击体积，在胸口
```

**受击体积在子节点上，代码取的是父节点。** 差 0.9 米（敌人 0.875）。

这也解释了 T53 那张四宫格为什么看着「火花在腿下面」：隔壁的假人是 `BlockoutRig`（方块人），人家是**正常站着**的，脚就在 y=0——而火花就生成在 y=0。

> 影响面：**不止弹开**。`Verdict.Deflect / Clash / Hit / Issen` 四条全走这个 `Position`，
> 所以血雾（`SpawnMist`）和一闪的血雾也一样贴地。

---

## ② 足兵模型下沉 0.875 米

`scenes/enemies/AshigaruModel.tscn` 全文只有一行实例，**没有任何 transform**：

```ini
[node name="Model" parent="." instance=ExtResource("1_model")]
```

而 `assets/models/ashigaru_rigged.glb` 的网格原点在**腰部**。实测（探针现跑）：

```
敌人在 (0, -0.0027, 0)，模型根 (0, -0.0027, 0)，缩放 (1, 1, 1)
模型世界包围盒：min=(-0.68, -0.87, -0.22)  size=(1.36, 1.70, 0.46)
```

- 高度 **1.70** ✓（设计身高 1.6998，对的）
- **`min.y = -0.87`** ✗ ——脚底在地下 0.87 米

同一个 prefab 里的碰撞胶囊却是按「脚底贴地」摆的（`Ashigaru.tscn:44`，`y = 0.875`，高 1.8）。
**视觉与碰撞体差了整整 0.875 米。**

对照截图（同机位、同一具模型）：

| 文件 | 内容 | 看到什么 |
|---|---|---|
| `t52_death_alive.png` | 原样 | 一团趴在地上的白色，只有上半身 |
| `t52_death_raised.png` | 手动抬 0.875 | **一个完整站立的足兵**，双脚和影子都贴着地 |

---

## ③ 敌人死后姿态冻结

**两条链路同时断了。**

**第一处**，`src/Combat/CombatActor.cs:178`：

```csharp
if (IsDead)
{
    Velocity = Vector3.Zero;
    return;                 // ← 死亡后直接 return
}
```

`OnTickVisual` 在这之后才被调用 → **死亡之后视觉一次都不再更新**。
连带后果：`src/Enemies/Ashigaru.cs:263-267` 那段死亡分支变成**死代码**，永远执行不到：

```csharp
if (IsDead)
{
    _modelAnim!.Animate(dt, speed01, AshigaruAction.Death, 0, 0);   // ← 就算走到也是 0, 0
    return;
}
```

**第二处**就是那个 `0, 0`。旁边三个分支全都从状态机取真实帧数据（`executed.Frame, executed.TotalFrames` / `broken.Frame, broken.TotalFrames` / `atk.Frame, atk.TotalFrames`），**只有死亡写死零**——而该函数自己在第 259 行写着「这里不写死任何数字」。

而且 `Normalized(0, 0)` 有保护、返回 `0f`（不是 NaN），所以它不会崩，只会**安静地**表达成 `ApplyDeath(t=0)` = 所有 `Lerp(起点, 终点, 0)` = **起点值** = 「微微低头站着」。

**实证**（探针 `scenes/tests/ActorDeathProbe.tscn` 现跑）：

```
对照组·离线写死亡姿势：与存活姿态最大差 61.0°   ← 姿势本身有内容
实验组·Die() 后 40 帧：与死亡瞬间最大差 0.00°   ← 真实链路一动不动
```

同一次运行还拍了两张：`t52_death_atdie.png` 与 `t52_death_after40.png` ——
**逐像素完全相同**（不同像素 `0 / 746496`，最大通道差 `0`，差异包围盒 `None`）。

---

## ④ 为什么自检全绿

`src/Dev/AshigaruAnimProbe.cs:93`：

```csharp
_anim.Animate(1f / 60f, speed, action, f, SamplesPerAction);   // ← 它自己传了合法帧号
```

这个探针是**离线**的：自己 `new` 一个动画器、自己喂合法帧号，**绕过了 `DriveModel` / `_PhysicsProcess` 这条真实路径**。
所以它能理直气壮地报「死亡动作与 idle 差 XX° ✓」，而游戏里那行代码压根不执行。

> **教训**：只验证「姿势函数写没写对」的探针，永远发现不了「姿势有没有被播出来」。
> 两者必须各有一条断言，缺一边就会出现「测试绿、游戏坏」。

---

## 两条缺陷互相掩盖（关键）

① 和 ② 不是简单叠加，而是**互相打掩护**：

| | 敌人（Ashigaru） | 玩家 / 假人（站姿正常） |
|---|---|---|
| 视觉身体中心 | ≈ y 0（因为下沉了） | ≈ y 0.9 |
| 火花生成点 | y 0 | y 0 |
| **结果** | 看着**凑合**，像落在腰腹 | **明显在脚底** |

所以：
- 只看敌人 → 觉得火花位置「还行」
- 只看玩家/假人 → 火花「明显不对」
- **一旦单独修好 ②（把敌人抬起来），①立刻在敌人身上也暴露成「脚底」**

**这两条必须一起修。**

---

## 修复记录

### ① 接触点改用受击形状的位置 —— ✅ 已修

`src/Combat/Hurtbox.cs` 新增只读属性（取子 `CollisionShape3D` 的全局位置，拿不到退回自身）：

```csharp
public Vector3 GlobalContactPoint { get { ... return shape.GlobalPosition; ... } }
```

`CombatArbiter.cs` 改为 `Position = hurtbox.GlobalContactPoint,`，那句骗人的注释也改成了实话。
**验证**（探针现跑）：`hurtbox.GlobalPosition=(0,-0.0027,0) → GlobalContactPoint=(0, 0.8723, 0)`（期望 y≈0.875 ✓）。
连带修正：伤害数字（`DamageNumbers`）与血雾/一闪全部上到胸口；魂球走的是
`ActorDefeatedEvent.Position`（= 角色根节点，语义本来就对），**不受影响、未改动**。
T53 四档截图机机位对准新位置重拍：火花世界 y=0.900、屏幕投影 (519,328) 正中视口，
两两像素差 3.02%~7.71%，对照图已重生成（`t53_sheet.png`）。

### ② 模型对齐脚底 —— ✅ 已修（采用了 tscn 方案，理由见下）

`AshigaruModel.tscn` 的 `Model` 节点补上 `transform ... y=0.875`。
**验证**（探针现跑）：包围盒 `min.y` 从 **-0.87 → +0.01**，身高 1.70，脚底贴地；
截图里足兵完整站立（肩甲/双臂/腰带/裤裙/双脚/影子齐全）。

> **方案取舍**：诊断时我推荐「运行时按包围盒自动算」。实际采用了 tscn 写 0.875，
> 因为 0.875 本来就是同一 prefab 里碰撞胶囊的摆高（`Ashigaru.tscn:45`）——
> 模型和胶囊是配对关系，**换模型时这两个数必须一起动**，写在同一处场景数据里反而好对照。
> 代价：它确实是第二份「0.875」（已在下面"缺口"里登记）。

### ③ 死亡演出接线 —— ⏳ 等制作人给一个数

**两处都要动，缺一不可**（方案不变）：

1. `CombatActor._PhysicsProcess`：`IsDead` 时**不要直接 return**，至少让视觉继续跑完倒地。
   稳妥做法是加一个钩子（`OnTickDeathVisual`），而不是让死人也走 `OnTickVisual`（那里有冷却递减、转朝向等副作用）。
2. `Ashigaru.DriveModel`：死亡分支要传**自己的帧号与总帧数**，不是 `0, 0`。

**缺的数字**：死亡演出播多少帧、播完停在哪一帧、尸体留多久。
按项目规矩落 `data/**`，**这个数是多少，请你拍。**

---

## 我没验证的 / 不确定的

1. ~~关卡内实际摆放~~ → 探针实测坐实（包围盒 min.y=-0.87，场景无补偿），已修。
2. **`AshigaruAnimProbe` 的门禁地位**：修好 ③ 之后，这个离线探针**仍然发现不了回归**。要不要顺手给它补一条在线断言，你定。
3. **相机震动 / 顿帧**：老缺口（项目里没有相机震动系统；顿帧属 `CombatResolver`），本轮没碰。
4. **第二份「0.875」**：②采用了 tscn 方案后，模型高度与碰撞胶囊高度是两个写着的数。
   换模型时必须一起动（见上"方案取舍"）；若你更想自动算，改回运行时方案即可，探针会兜底验证。

---

## 复现方式

```powershell
# 数值（无头，约 10 秒）
godot --headless --path G:\Game res://scenes/tests/ActorDeathProbe.tscn
# 退出码 1 = 确认缺陷（不是探针坏了）；2 = 探针自身失效、不下结论

# 四张对照图（必须带窗口；无头是 dummy renderer，抓空帧）
godot --path G:\Game res://scenes/tests/ActorDeathProbe.tscn -- shot
```

产物：`assets/references/t52_death_{alive,atdie,after40,raised}.png`
探针：`src/Dev/ActorDeathProbe.cs`（通用命名，不是一次性脚本）
