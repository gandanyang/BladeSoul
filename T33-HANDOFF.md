# T33 交接 · 场景模块化套件（P0 已落地）

## 一句话

P0 的 **11 个模块**全部程序化生成（每个都在 10 §3.6 面数预算内），**已铺进两关**——
Dojo 60 件 / 13,272 面，L01_Gifu 117 件 / 28,332 面。

**布局一个字节都没动**：把铺装段剔除后与铺装前逐行比对，`257→257`（Dojo）、`374→374`（Gifu），
`ALL LAYOUTS INTACT`。对比图（同机位、同一次运行内开关 `Dressing`）证明空间读起来变了。

---

## 一、交付物

### 1.1 模块清单（`python tools/gen_kit.py --list` 的真实输出）

| 模块 | 面数 | 预算 | 包络（m） | 模式 | 说明 |
|---|---:|---:|---|---|---|
| `floor_soil` | 96 | 300 | 8.00×0.40×8.00 | tile | 地面·夯土（低频湿斑，无碎石子） |
| `floor_wetstone` | 204 | 300 | 8.00×0.40×8.00 | tile | 地面·湿石板（2m 石板 + 成组湿斑） |
| `floor_wood` | 276 | 300 | 8.00×0.40×8.00 | tile | 地面·木地板（通长板条） |
| `wall_plank` | 276 | 300 | 4.00×3.20×0.40 | tile | 墙·板壁（竖板条 + 两道贯 + 上下长押） |
| `wall_dobe` | 228 | 300 | 4.00×3.20×0.40 | tile | 墙·土壁（露明木框架 + 腰板 + 剥落下地） |
| `wall_ishigaki` | 300 | 300 | 4.00×3.20×0.40 | tile | 墙·石垣（错缝砌石） |
| `post` | 84 | 300 | 0.60×3.20×0.60 | fit | 柱（础石 + 柱头 + 四面收分） |
| `gate` | 108 | 300 | 2.40×3.20×0.40 | fit | 门洞（门楣墙 + 冠木 + 两侧门柱） |
| `lantern_hanging` | 408 | 1500 | 0.43×0.70×0.43 | anchor | 灯笼·挂式灯体（**全作唯一暖色**） |
| `lantern_standing` | 420 | 1500 | 0.53×0.81×0.53 | anchor | 灯笼·立式灯体 |
| `lantern_rope` | 36 | 1500 | 0.34×1.00×0.34 | stretch_y | 挂绳 + 挂杆（**原点在绳顶**） |
| `lantern_post` | 36 | 1500 | 0.42×2.00×0.42 | stretch_y | 立式柱子 + 石基（**原点在柱顶**） |

尺寸全部取自 10 §3.4 的冻结值——`GRID=4.0` / `LAYER_HEIGHT=3.2` / `DOOR_PASSAGE=2.4` / `WALL=0.4`，
**直接 import 自 `gen_level_whitebox.py`，没有第二份定义**。

### 1.2 铺装量

| 关卡 | 件数 | draw call | 三角面 | 原有行 → 铺装后 |
|---|---:|---:|---:|---|
| `Dojo.tscn` | 60 | 60 | 13,272 | 260 → 836（+576） |
| `L01_Gifu.tscn` | 117 | 117 | 28,332 | 377 → 1457（+1080） |

draw call 上限 800（07 §7）——两关都远在安全区。

**Dojo 明细**：`wall_dobe ×40`、`floor_wood ×7`、`lantern_hanging ×4` + `lantern_rope ×4`、`post ×4`、`gate ×1`
**Gifu 明细**：`floor_wetstone ×55`、`wall_plank ×38`、`wall_ishigaki ×16`、`lantern_standing ×4` + `lantern_post ×4`

### 1.3 共享材质

按 10 §3.6「**不要每块一个材质**」：全场只有 **2 套材质**，颜色全走顶点色。

| 材质 | 用途 | 关键开关 |
|---|---|---|
| `data/materials/kit_surface.tres` | 所有墙/地/柱/门/绳柱 | `vertex_color_use_as_albedo = true`，roughness 0.58 |
| `data/materials/kit_lantern.tres` | **只**给灯笼灯体 | `shading_mode = 0`（Unshaded，纸罩要自己发光） |

---

## 二、怎么手动验证

### 2.1 全量（自动）

```powershell
powershell -NoProfile -File tools\check.ps1
```

### 2.2 对比图（**必须带窗口**）

```powershell
godot --path . res://scenes/tests/KitDressingDojo.tscn
godot --path . res://scenes/tests/KitDressingGifu.tscn
```

出图到 `assets/references/t33_{dojo,gifu}_{before,after}.png`。
**同一次运行里开关 `Dressing` 节点的可见性**——相机/光照/时间/种子完全一致，
两张图之间唯一的变量就是模块本身（比"跑两次人工对齐机位"可靠，而且没有对齐这件事）。

### 2.3 像素取证（不只看"变了多少"，看"变成什么颜色"）

```powershell
"F:/ComfyUI-aki-v3/python/python.exe" .workbuddy/tmp/shot_diff.py
```

---

## 三、验证命令的真实输出

### 3.1 `check.ps1` → **ALL CHECKS PASSED**（34 步，退出码 0）

```
已通过! - 失败: 0，通过: 197，总计: 197        ← 单测 197 项
[自检] 资源 29 个（招式 12 / 难度 4 / 角色数据 5 / 氛围 1 / 材质 3 / 玩家 3），音效 15 个，错误 0 项
[自检] ✓ 通过
[死配置] 扫描 431 个 [Export]，零引用 22 个（基线 22 个）
[死配置] OK 没有新增
--- 22/34 whitebox: dojo (walkability + clearance) ---
[白盒] ✓ Dojo.tscn 通过（可走性 + 战斗区净空）
--- 23/34 whitebox: gifu chapter 1 ---
[白盒] ✓ L01_Gifu.tscn 通过
...
ALL CHECKS PASSED
```

`LevelWhiteboxTest` 的可走性 + 战斗区净空自检**继续全绿**——它顺便就是"这次铺装有没有动到布局"的判据
（碰撞体是 `CSGBox3D` 自己，模块只做外观包络，一个字节没碰碰撞）。

### 3.2 布局纯度（`.workbuddy/tmp/verify_layout_pure.py`）

把「MARK 之后的铺装段 + kit 的 `ext_resource` + `load_steps` 行」全部剔除后与铺装前备份逐行比对：

```
== Dojo.tscn: 257 → 257 行  ✓ 布局零改动
== L01_Gifu.tscn: 374 → 374 行  ✓ 布局零改动
ALL LAYOUTS INTACT
```

### 3.3 顶点色真的上了（`.workbuddy/tmp/shot_diff.py`）

| 关卡 | 修复前变化像素 | **修复后** | 平均饱和度 | 色相桶 |
|---|---:|---:|---:|---:|
| Dojo | 0.69% | **61.18%** | 0.258 | 7 个 |
| Gifu | 29.64% | **94.13%** | 0.205 | 7 个 |

判据是按"**饱和度**"下的，不只看变化率：glb 里没有材质，没套材质就是一片默认灰，
灰的饱和度≈0。两关都 > 0.20 且跨越 7 个 30° 色相桶 → 顶点色确定生效。

---

## 四、三个坑（都是实测踩出来的，写进 `docs/10`）

### 坑 1 · `.glb` 在 Godot 里是 **PackedScene**，不是 Mesh

第一版写成：

```
[node name="Kit_X" type="MeshInstance3D" parent="Dressing"]
mesh = ExtResource("kit_1")          ← kit_1 是 PackedScene
```

类型不符 → **网格压根不出来**（Dojo 的对比图"前后一样"、只差 0.69% 就是这个）。
正确写法是**实例化 PackedScene，再把材质覆盖写进实例内部**（照抄 `gen_level_whitebox._showcase`，
魔骸用的就是这套，**零代码**）：

```
[node name="Kit_X" parent="Dressing" instance=ExtResource("kit_1")]
position = Vector3(...)
scale = Vector3(...)

[node name="world" index="0" parent="Dressing/Kit_X"]

[node name="kit_floor_wood" index="0" parent="Dressing/Kit_X/world"]
material_override = ExtResource("kit_7")
```

内层路径**是量出来的不是猜的**（`.workbuddy/tmp/glb_nodes.py` 直接读 glb 的 JSON chunk）：
trimesh 导出的结构恒为 `world`(容器) → `kit_<模块名>`(网格)，Godot 再在外面包一层同名根节点
（对照魔骸：`world/geometry_0`）。

⚠️ **属性顺序不能换**：`set_rotation` 是**整个替换** basis（不保留缩放），
所以 `scale` 必须写在 `rotation` 之后，否则模块会被转回 1:1 尺寸。

### 坑 2 · glTF 导入**不会**自动打开 `vertex_color_use_as_albedo`

实测我们导出的 glb 里 `materials: null`（trimesh 不带材质），而且即使带了，
Godot 导入后那个开关也是 `false`。模块和魔骸一样**没有 UV/贴图、全靠顶点色**，
不套材质就是一片灰——这就是必须写上面那个覆盖块的根本原因。

### 坑 3 · 铺装工具的**可重入**要清两处，不是一处

`gen_level_kit.py` 是"只做加法"的：读现有 `.tscn`、把模块作为新节点追加。
但它自己产出的东西有**两处**：

1. 尾部带 `MARK` 的节点段 → 切掉容易
2. **头部插进去的 kit `ext_resource`**（在第一个 `[node]` 之前，**不在 MARK 之后**）→ 容易漏

漏掉第 2 处的后果实测：重跑一次"原有行"从 258 涨到 267，id 一路漂到 `kit_10+`。
已按**路径**（`res://assets/models/kit/` 等）识别并摘除，不按 id（id 会随去重让位而漂移）。

---

## 五、偏离任务卡的地方

| # | 卡片要求 | 实际 | 为什么 |
|---|---|---|---|
| 1 | P0 = 地面/墙/门洞/灯笼 | 多做了一件 **`post`（柱）** | T32 的白盒里已经有 4 根 `Pillar`。不铺它们的话道场会是"贴了木地板和土壁、但柱子还是灰盒"——一眼假。柱本来就是 10 §3.5 的题中之义，只是被排在 P1 之外。 |
| 2 | 门洞 | 门洞**没有做"可开合的门扇"** | 卡片只要求"门洞"。白盒里的门是 `DoorLintel`（一道门楣），做成一整组带门柱的**通路门**，**不碰碰撞**（通行还是靠门楣下方的空档）。 |
| 3 | 地面/墙模块 ≤300 面 | 全部达标（最大 300，`wall_ishigaki`） | 无偏离 |
| 4 | "不要每块一个材质" | 2 套共享材质 + 顶点色 | 无偏离 |
| 5 | 颜色纪律：低饱和青灰，暖色只在灯笼 | 遵守 | 唯一的暖色是灯笼（`kit_lantern.tres` 是 Unshaded，两关的暖光都来自它） |
| 6 | — | **天花板不做** | P0 清单里没有天花。`Ceiling` 与两个 `SkylightHole` 被显式跳过，理由见下 |

**关于跳过的三类盒体**（`gen_level_kit.py` 的已知陷阱表）：

- `Ceiling` —— 薄轴是 **Y** 的水平板。当成墙去铺会在天花上糊一圈竖板条。
- `SkylightHoleA/B` —— **CSG 减法体**（`operation = 2`）。铺一块板盖上去**就把 T40 的天窗堵了**，
  室内照明当场失效。带减法子节点的盒体**整个跳过**。

---

## 六、**不在本次范围内、但请制作人过目**的一件事

`tools\check.ps1` 第 22 步（`LevelWhiteboxDojo`）会刷 **755 次** `ObjectDisposedException`，
约 1.1 万行日志。**这与 T33 无关**——做了对照实验：

| 关卡 | 行数 | PlayerActor 异常 |
|---|---:|---:|
| **未铺装的基线**（`levels-before-kit/Dojo.tscn`） | 11,343 | **755** |
| 已铺装的 `Dojo.tscn` | 11,343 | **755** |

**逐字节相同**。根因已定位到 `src/Levels/TutorialDirector.cs:114`（T47 的文件）：

```csharp
_player ??= FindPlayer();          // 第 97 行
if (_player is not PlayerActor actor)
    return;
...
Vector3 flat = actor.GlobalPosition - _origin;   // 第 114 行 ← 这里抛
```

`??=` 只在**引用为 null** 时重查。节点被释放后 C# 包装对象**非 null** 但指针已失效，
于是 cast 通过、每个物理帧抛一次。惯用修法是提前判失效：

```csharp
if (_player is not null && !GodotObject.IsInstanceValid(_player))
    _player = null;
_player ??= FindPlayer();
```

它**不影响测试结论**（测试最终仍 ✓ 通过），但会淹掉第 22 步的信号——
**我没有动它**：`TutorialDirector.cs` 属于 T47 的卡片，不在 T33 的范围内。请制作人拍板。

---

## 七、改了哪些文件

### 新增

| 文件 | 内容 |
|---|---|
| `tools/gen_kit.py` | 11 个模块的程序化生成器（axis-aligned 盒 + 旋转体；`--list` / `--report`） |
| `tools/gen_level_kit.py` | 铺装工具：读现有 `.tscn` → 分类 → 追加 `Dressing` 节点（**只做加法**） |
| `data/materials/kit_surface.tres` | 共享材质（顶色作 albedo） |
| `data/materials/kit_lantern.tres` | 灯笼材质（Unshaded） |
| `assets/models/kit/*.glb` ×11（+ `.import`） | 模块模型，全部带 `COLOR_0 / NORMAL / POSITION` |
| `src/Dev/KitDressingShot.cs` | 同机位前后对比出图；**无头模式当场拒跑** |
| `scenes/tests/KitDressingDojo.tscn` `KitDressingGifu.tscn` | 出图场景（**不进 `check.ps1`**，必须带窗口） |
| `assets/references/t33_{dojo,gifu}_{before,after}.png` | 对比图 |

### 修改

| 文件 | 改了什么 |
|---|---|
| `scenes/levels/Dojo.tscn` | 头部加 8 条 `ext_resource`；尾部追加 `Dressing` 段（+576 行）。**原有行一行未删** |
| `scenes/levels/L01_Gifu.tscn` | 同上（+1080 行） |
| `src/Dev/KitDressingShot.cs` | 补无头守卫（见下） |

**没有碰**：白盒生成器、任何碰撞、任何 `src/` 战斗/玩家逻辑、`data/` 里的数值。

> 顺手补的一个洞：`KitDressingShot.cs` 原本在无头模式下 `GetViewport().GetTexture().GetImage()`
> 会**直接抛**（dummy 渲染器返回 null 纹理，不是返回 0×0），绕过了原有的空帧判断，
> 于是每帧抛一次、日志刷到 40MB 且进程**永不退出**。现在 `_Ready` 里先判
> `DisplayServer.GetName() == "headless"` 当场退出，`Shoot()` 也包了 try/catch。

### 新增 / 改动的接口

**无**。没有新增任何 `[Export]`，没有改任何冻结接口，没有新增单测。
`gen_kit.py` 反向 import `gen_level_whitebox.py` 的 `GRID / LAYER_HEIGHT / DOOR_* / WALL`
与 `paint_mesh.py` 的 `srgb_to_linear`——**尺寸与色彩空间转换各自仍只有一个来源**。

---

## 八、怎么回退

删掉两关 `; ---- T33 KIT DRESSING` 标记之后的全部行、以及头部 8 条 kit `ext_resource`
即回到纯白盒（`gen_level_kit.py` 自己就是这么做的，重跑幂等）。
铺装前的完整备份在 `.workbuddy/tmp/levels-before-kit/`。

---

## 九、下一步（P1，未做）

町屋（茅·瓦屋顶、格子窗、门帘）、神社（鸟居、石阶、香炉）、自然（竹、松、草、石）。
P1 的模块**不改变**本轮的管线——加进 `MODULES` 表、跑一次 `gen_level_kit.py` 即可。
`stretch_y` 模式（绳/柱）已经证明了"截面不变、只缩长度"这条路可行，屋顶的檐口可以照此办理。
