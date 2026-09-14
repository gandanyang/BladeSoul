# T48 / T49 · 玩家模型骨骼与权重工具

T48 加了武器骨（`Weapon_R` / `Scabbard`），T49 修蒙皮权重。这里的脚本每一个都对应一次
**踩过的坑**，别当成可随手删的临时文件。

## 一句话结论（先读这个）

原始权重**只有一个缺陷**：1265 个顶点（11.4%）粘在 `neutral_bone` 上，
那根骨永不被任何动画驱动，所以这些皮在身体动时纹丝不动 → 被邻居扯开成尖刺。
其余 9806 个顶点的权重是**健全的**，不要动。

修法就是 `tools/weight_fix.py`：只重算那 1265 个。

## 体检（在引擎里，权威）

```powershell
godot --headless --path . res://scenes/tests/AnimProbe.tscn    # 骨头动没动
godot --headless --path . res://scenes/tests/WeightProbe.tscn  # 皮被扯开多少
godot --headless --path . res://scenes/tests/PoseProbe.tscn    # 静止姿势对不对
godot --path . res://scenes/tests/PoseShot.tscn                # ★ 出四方向截图（要窗口）
```

`PoseShot` 是**朝向问题的唯一可信判据**。它固定机位出正面/背面/左/右四张，
你直接用眼睛看。文字描述和"我觉得头朝下"都不算数——本轮就是在这一点上被绕了很久：
相机在 y=1.05 俯视一个 y=0.1 的角色，参考线又画在地上，很容易读成上下颠倒。

`WeightProbe` 是 T49 新加的。它**不用**任何 Godot 的骨架空间 API，只从
`HumanoidAnimator` 读"每根骨转了多少"，旋转矩阵自己构造、顶点位置自己算。
理由见 `src/Dev/WeightProbeTest.cs` 顶部注释——`GetBoneGlobalPose()` 在无头场景里
不刷新，直接拿它做判据会得到一个**永远报 0 的假绿探针**。

尺子与各自的盲区：

| 指标 | 看得见什么 | 看不见什么 |
|---|---|---|
| **偏离主导骨**（`PivotDrift`） | 顶点被非主导骨扯离"它该在的地方"多远 | **看不见孤儿顶点**——只被一根骨影响时没有非主导骨可偏离，两版都是 0.308 m |
| 棱伸长（绝对量，mm） | 相邻顶点被拉开多少 | 短棱上要过滤；对整体形变敏感 |
| 单帧位移 | 只用于和历史记录对照 | 快速挥腿时它天然偏大（角速度 × 半径），不能当判据 |

## 修权重

```powershell
# 必须从**未修复的**输入出发。修复是幂等的：再跑一次会报 0 repaired。
python tools/weight_fix.py --src %TEMP%\original.glb --out %TEMP%\fixed.glb
copy %TEMP%\fixed.glb assets\models\model_player_congyun_03_textured.glb
godot --headless --path . --import
godot --headless --path . res://scenes/tests/WeightProbe.tscn
```

几何一个字节都不动，只改那 1265 个顶点的 `JOINTS_0` / `WEIGHTS_0`。

规则要点：**距离闸门 + 左右隔离 + 反向关节兜底**。三个都已踩过坑：

1. **绝对距离上限是个陷阱。** 袍摆顶点离所有骨都远（最近 0.35 m），
   一旦加"距离必须小于 X"，它们会全部落进兜底分支、1265 个统统跟 `Hip` 走——
   比不修更糟。闸门只能用**相对**于最近骨的差值。
2. **左右隔离。** 顶点 x > 0（模型的左侧）不许用 `R_*` 骨，反之亦然。
   不做这条，一条腿会被另一条腿拖走。
3. **道具骨不许参与权重。** `Weapon_R` 在身体前方 `(0.159, 0.387, 0.186)`，
   纯按距离算会让胸口顶点 39% 归刀。
4. **骨骼段不要顺着第一个子节点拉。** `Weapon_R` 是 `R_Hand` 的子节点，
   顺着拉会让"手骨"从手腕一直延伸到刀尖（0.49 m），所有肩部顶点都算成"离手很近"。
   `bone_segments()` 里用 `BANNED` 过滤掉道具。

## 诊断 / 恢复

| 脚本 | 什么时候用 |
|---|---|
| `weight_space_check.py` | 权重复算出"跨身体"结果时的第一站：顶点和骨骼是不是在同一个空间 |
| `weight_spike_diag.py` | 纯 Python 解析 glb；量 `rest × IBM ≈ I`、逐骨位移探针 |
| `restore_original_weights.py` | **`textured.glb` 不在 git 里**（未跟踪）。它是唯一能把原始权重找回来的东西——从 `_rigged_weapon.glb` 按顶点位置搬回来 |

## 血的教训（两条，都很贵）

**一、`assets/models/model_player_congyun_03_textured.glb` 没有备份。**
T49 期间一次 `Copy-Item` 覆盖就把"修复前"的状态弄丢了，后来靠
`restore_original_weights.py` 从 `_rigged_weapon.glb` 按顶点位置搬回来才恢复
（11071/11071 匹配，最差位置误差 3.6e-07 m，`neutral_bone` 孤儿数 1265 对得上）。
**动这个文件之前先复制一份到 `%TEMP%`。**

**二、"指标变好"不等于"变好了"。**
中途曾把全部 11071 个顶点按几何重算，指标全面变漂亮（棱伸长 1754 mm → 124 mm），
于是覆盖了原始权重——**结果人物在游戏里明显变形**。
拿好的权重去换坏的，是最典型的自伤。
这个项目习惯"能拿数据自证"，但自证之前先要证明**数据指向的东西是对的**：
先扫描"到底哪些顶点真的坏了"（结论是只有 1265 个），再决定改动范围。
