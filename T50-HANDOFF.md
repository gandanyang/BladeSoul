# T50 交接 · 玩家模型"像纸被翻折"的真根因

## 一句话

`HumanoidAnimator.Rot()` 把 `SetBonePoseRotation` 当成"转这么多"用，
而它是"**把局部旋转整个设成这个值**"。本骨架里 `L_Thigh` / `R_Thigh` 的
rest 旋转是 **180°**、`L_Upperarm` / `R_Upperarm` 是 **101.4°**——
于是 `Rot("L_Thigh", 0)` 把两条大腿整个翻过去，`Rot("L_Upperarm", 0)` 把双臂抬成 T 型。

## 怎么定位到的（这条路径可复用）

前面 T49 连续几轮都在查权重，方向是错的。真正把问题钉死的是这四步：

### 1. 先用**材质**确定资产本身没问题

`scenes/tests/MaterialProbe.tscn`：按 y 分 10 层列出每层的材质名。

```
第 1层 y -0.98..-0.78  Leather                            ← 靴/绑腿
第 3层 y -0.59..-0.39  ClothOuter x2431                   ← 下摆
第 8层 y  0.40..0.59  Body / Leather / Gauntlet           ← 胸、护臂
第10层 y  0.79.. 0.99  Body x130  Hair x336               ← 头发 + 头皮
```

**头发在最顶层、靴子皮革在最底层 → 绑定姿势正立**。这一步把"资产坏了"排除掉，
后面才没有继续白查权重。

### 2. 用两个小球校准**相机方向**

`PoseShot` 现在在空间 y=+0.75（红）和 y=−0.75（蓝）各放一个球。
红球必须出现在画面上方。没有这个基准，"头朝下"到底是模型翻还是相机翻，永远说不清。

### 3. 从**真实游戏**录帧，而不是用自己搭的场景

```powershell
godot --path . --write-movie %TEMP%\gameplay\frame.png --quit-after 40
```

这一招绕开了所有自己写的探针——用游戏自己的主场景、自己的相机。
关键发现：**画面深处的挥砍假人（NPC）站姿完全正常，只有玩家角色是畸形的。**
NPC 和玩家共用同一套渲染，所以问题必然在玩家这条路径上，与资产无关。

### 4. 在**游戏的相机上**反向解出每根骨被施加了什么旋转

`scenes/tests/GameView.tscn`（`src/Dev/GameViewTest.cs`）：
实例化 `Dojo.tscn`，等 60 帧，然后用
`applied = (pose.Basis * rest.Basis⁻¹).Orthonormalized()` 取轴角。

```
Hip / Spine01 / Spine02 / Head   rest 旋转 =   0.0°   施加 0.0°    ✔ 恰好等价
L_Thigh / R_Thigh                rest 旋转 = 180.0°   施加 180.0°  ✘ 整个翻过来
L_Upperarm / R_Upperarm          rest 旋转 = 101.4°   施加 101.4°  ✘ 抬成 T 型
```

**"施加的旋转"等于"rest 的旋转"** —— 这个等式一眼就能看出是"被替换成了单位旋转"，
而不是"摆动"。修复后再跑，全部变成 `0.0°`。

## 修复

```csharp
Quaternion rest = _skel!.GetBoneRest(bone).Basis.GetRotationQuaternion();
_skel.SetBonePoseRotation(bone, rest * q);     // 原来是 rest 直接被 q 覆盖
```

修复后同一探针：

```
施加的旋转：Hip 0.0°  Spine01 0.0°  Spine02 0.0°  Head 0.0°
           L_Thigh 0.0°  R_Thigh 0.0°  L_Calf 0.0°  R_Calf 0.0°
           L_Upperarm 0.0°  R_Upperarm 0.0°
局部姿势偏离 rest > 25° 的骨：0 根
```

## 顺带修的两处

1. **`AnimateLocomotion` 的走路摆幅保底**：`Mathf.Max(0.35f, ...)` 对 `speed01 = 0`
   也返回 0.35。注释写的「只要在走（speed01 > 0.15）」从来没进过代码。
   加了 `MinWalkSpeed` 常量守着。（注：这一条的实际影响比一开始想的小——
   `swing` 已被 `speed01` 乘成 0，所以腿本来不会摆；但 `Rot` 的修复让这一步的
   语义变得重要了，因为现在 `Rot(..., 0)` 真的等于"不动"。）

2. **`src/Dev/PlayerMountShot.cs` 有一行编译错误**（`Node3D.GetAabb()` 不存在），
   让**整个项目构建失败**。改成从下面的 `MeshInstance3D` 取 `GetAabb()`。
   这个文件是另一个 agent 写的、正在处理同一个问题，改动仅限修那行 API 误用。

## 探针本身踩过的坑（别重犯）

| 坑 | 后果 | 对策 |
|---|---|---|
| 用 `\|q1·q2\|` 算旋转差 | **0° 和 180° 都算成 1** → 把"没动"报成"翻 180°" | 用欧拉角分量相减 + `PosMod` 归一 |
| 自己搭正面机位看畸形 | 同一畸形在不同机位下读成完全不同的东西 | 用游戏自己的相机（`GameView.tscn`） |
| 只信渲染描述 | 同一张图被描述成"头顶在上"又说"整体倒立" | 材质分层、像素剖面、轴角数据优先 |

## 还没做完

1. **`AnimProbe` / `MoveProbe` 的阈值要重新校准。** 它们的"最大偏转"以前混着这
   180°／101.4°，所以报出的 115.4°（武器骨）、131.8°（两腿夹角差）都偏大。
   修好 `Rot` 之后这些数字会小很多，旧阈值可能变得过松（不再能挡住真回归）。
2. **走路摆幅要重新看。** 以前"腿在摆"有相当一部分是那 180° 的错觉。
   现在 `step` 是否够大、要不要调 `MinWalkSpeed`，得实机看。
3. 攻击/防御/受击三套姿势（`ApplyAttackPose` / `ApplyGuardPose` /
   `ApplyDeflectPose`）全部经过 `Rot()`，这次一并修好了，但**没有逐套截图验收**。
