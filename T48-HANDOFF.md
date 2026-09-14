# T48 任务交接上下文（来自 Codex 对话「用 blender MCP 做 T48」，2026-09-13）

> 用途：在其他 AI 工具/新会话中继续 T48 及后续工作。项目：`G:\Game`（Godot 4.7.1 + C#，动作游戏 Oniblade）。

## 一、任务定义

- **T48**：给主角模型加武器骨（`Weapon_R` / `Scabbard`），解决试玩反馈的"完全没有打击感"。
- 任务出处：`docs/TASKS.md`（T48 条目）；冻结决策：`docs/00-制作人决策记录.md` 第 2.8 节；工具链说明：`docs/17-Blender与AI工具链.md`；`AGENTS.md` 有**并发写警告**（zcode 会同时改模型）。
- 硬约束：**不许改 `PlayerActor.cs`**（制作人决策卡明令）。

## 二、模型演进史（重要）

| 模型 | 状态 | 结论 |
|---|---|---|
| `model_player_congyun_01.glb` | 6500 顶点权重全部在 Root 骨 | **是个雕像**：转 R_Upperarm 68.8°，0/6500 顶点移动。"没打击感"的根因。已废弃但**一个字节不删**（用户明令保留） |
| `model_player_congyun_02_rigged.glb` | 22 骨真蒙皮、Attack/Walk 剪辑 | 但**没有头**（颈部以上只有肩领） |
| `model_player_congyun_03.glb` | 几何底模，有头 | 6110 顶点/11496 面，无材质无 UV |
| `model_player_congyun_03_rigged.glb` | 22 骨 + 2 动作，skins 正常 | T48 的加工底模（曾一度 skins 为空，zcode 已修好） |
| `model_player_congyun_03_textured.glb` | 9 材质 + 6 贴图，**已含 24 关节（Weapon_R/Scabbard 已并入）** | zcode 做的，`Player.tscn` 已指向它（scale 0.888）。T48 骨骼成果已被并进管线 |

## 三、T48 已完成的工作（Codex 侧，全部有验证）

1. **刀选区**（最终判据，可直接复用）：左右不对称(>0.030) + 距刀轴(<0.16) + 腰以上排除 + 取最大连通分量 → 243 面，刀柄顶到刀尖完整干净。自动判据的硬极限：网格边距中位数 3.7cm vs 刀杆半径 ~2cm，刀与袍摆物理上难分，单判据无解。
2. **按刀镡切分**：刀柄 50 面/36 顶点 → `Weapon_R`（父级 `R_Hand`）；鞘 193 面/128 顶点 → `Scabbard`（父级 `Hip`）。骨架 22 → 24 骨，刀区权重和 = 1.0。
3. **产物**：`assets/models/model_player_congyun_03_rigged_weapon.glb`（+`.import`）。
4. **二进制自证通过**：skins=1、24 关节、父级正确、三角形数不变(11496)、静止包围盒与源逐位一致、动作保留。
5. **游戏内验收通过**：
   - `AnimProbe`：24 骨、12 个必需骨名缺 0、`Weapon_R` 全局偏转 **115.4° ≥ 100°** ✓、`Scabbard` 跟腰 ✓
   - `tools/check.ps1` 全量 **34 步 ALL CHECKS PASSED**
6. **新增文件**：`src/Dev/T48SwingShot.cs` + `scenes/tests/T48SwingShot.tscn`（挥砍连帧验收，产出 `assets/references/t48_swing_1..6.png`）；`tools/start_blender_mcp.py`（带 try/except 的 Blender+MCP 启动器，直接 `--python-expr` 一抛异常 Blender 会整个退出）。

## 四、遗留问题（接手者接下来要做的）

1. **【大头】自动权重炸尖刺**：`R_Upperarm` 转 130°（攻击实际幅度）时最大顶点位移 **1.561m**、>0.2m 位移顶点约 1000 个——**不是 T48 造成的**（原始 `_03_rigged` 对照同样炸：973 个），是 zcode `tools/rig_character.py` 自动权重的固有缺陷（影响半径覆盖体高 40%，`neutral_bone` 影响盒 1.463）。需要重刷身体权重（限制影响半径/手工权重），这才是"打击感"的真正大头。
2. **待清理**：`assets/references/_t48*.png` 共 **74 张**探查过程图（用户已同意"按你说的来"清理；`t48_swing_*.png` 是验收产物要保留）。
3. `AnimProbeTest.cs` 已改判定口径：`Weapon_R`/`Scabbard` 是被动子骨，局部姿态恒 0°，**必须量全局姿态**；"武器骨骼缺 1/3"缺的是 `Weapon_L`，卡里写明留给将来左手持械，属预期。

## 五、关键坐标与环境事实

- 模型轴向（Blender 本地）：**forward = −Y，right = +X，up = +Z**（旧模型测量时踩过坑：Y 是前后不是左右）。
- 03 模型**原点在身体中部**，脚底在 z≈−0.979；`PlayerActor.cs` 假定原点在脚底且只设 Scale/Rotation——所以导出侧必须保证原点在脚底（zcode textured 版用 scale 0.888 处理）。
- 刀轴线参考点：刀柄顶 `TOP(0.158,−0.185,0.390)`、刀镡 `GUARD(0.212,−0.192,0.135)`、刀尖 z≈−0.636。
- Blender 4.5.13 LTS：`C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe`；MCP 端口 **9876**；**不要调用 `read_factory_settings(use_empty=True)`**（会杀掉 MCP addon，教训已吃）。
- Godot：`G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`，`--headless --path . --import` 后跑 `res://scenes/tests/AnimProbe.tscn` 等。
- 教训：**别把 .blend 工作文件放 `assets/models/`**——Godot 会当场景导入并报错中断整个导入扫描（已移到 %TEMP%）。
- 并发：zcode 可能同时在改模型文件和 `Player.tscn`（Codex 期间 `Player.tscn` 就被并发改过一次），动手前先看文件时间戳/git status。

## 六、当前暂停点

- 会话最后一条用户消息是"按你说的来"（同意清理 74 张 `_t48*.png`），**清理尚未执行**，会话即结束。
- 骨骼层面 T48 已完成并进入管线（textured 版已带 24 关节）；下一个真正要攻的是**身体权重重刷（解决炸尖刺）**。
