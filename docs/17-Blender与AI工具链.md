# 17 · Blender 与 AI 工具链（让 agent 能干活）

> 用途：**任何 agent 拿到这份文档就能用本机的 Blender 做资产**，不用重新探一遍。
> 写于 2026-09-13，起因：T48 需要"把刀从身体网格里切出来 + 加武器骨"，
> 而这属于"美术活"——本项目的 agent 以前在这条路上是完全堵死的。

---

## 0. 本机现状（2026-09-13 实测）

| 项 | 状态 |
|---|---|
| **Blender** | ✅ **4.5.13 LTS**，免安装 zip 版：`C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe` |
| 无头跑 Python | ✅ 验证过（`BLENDER_OK / gltf_import True / gltf_export True / python 3.11.15`） |
| `uv` / `uvx` | ❌ 没装（blender-mcp 要做前置） |
| `pipx` / `docker` | ❌ 没装 |
| `python` / `pip` / `node` | ✅ 3.10 / 有 / 有 |
| 代理 | ✅ `http://127.0.0.1:7897`（**blender.org 与 GitHub 直连都不通，必须走它**） |

---

## 1. 三条路线，选哪条看你要干什么

| 路线 | 适合谁 | 需要什么 | 能不能自证 |
|---|---|---|---|
| **A. 无头脚本**（`--background --python`） | **agent（默认走这条）** | 只要 Blender | ✅ **能**：脚本自己出数字/出图，像 `check.ps1` 那样验 |
| B. **MCP（blender-mcp）** | 人机同屏、边看边改 | **Blender GUI 开着** ＋ addon 监听端口 ＋ MCP 客户端 | ⚠️ 靠肉眼，难自动验 |
| C. `bpy` 当 pip 模块 | 想在纯 Python 里跑，不想开 GUI | `pip install bpy`（版本绑定 Blender） | ✅ 能 |

> **给 agent 的建议：默认走 A。** 理由不是"A 更高级"，而是**A 能被验证**——
> 本项目的验收习惯是"能拿数据自证"，而 B 的每一次操作都要靠人看着屏幕确认。
> B 真正的价值在**人机协作的探索阶段**（人看着 Blender，AI 改参数），不是在 CI 里。

---

## 2. 路线 A：无头脚本（agent 的主力）

### 2.1 怎么跑

```powershell
C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe `
    --background --factory-startup --python tools\你的脚本.py
```

* `--background`：不开窗口（**没有 GUI 也能跑**）
* `--factory-startup`：忽略用户配置与插件——**保证可复现**，别省这个参数
* `--python`：跑完脚本就退出

### 2.2 项目里已有的脚本（照它们的模式写）

| 脚本 | 干什么 |
|---|---|
| `tools/blender_find_weapon.py` | 把模型按**连通块**拆开，按"细长比"排序找出长条物件（T48 用） |
| `tools/blender_render_views.py` | 出**正交三视图** PNG（用眼睛定位几何，比数字快） |
| `tools/rig_character.py` | 无头绑定：建骨架 → 权重（**带权重总量校验 + envelope 回退**）→ 导出后**直接解析 glb 的 JSON chunk 自检 `skins`** |
| `tools/uv_protagonist.py` | **按部位**展开主角 UV ＋ 出「分区配色图」（见 §2.5） |
| `tools/slice_material_sheet.py` | 把 AI 出的 3×2 材质表**按检测到的分隔缝**切开 ＋ 真无缝化（见 §2.6） |
| `tools/apply_materials.py` | 把贴图按 `docs/09 §7` 分槽贴到模型上（盒子投影 UV）＋ 导出后**解析 glb 自检材质** ＋ 出正/背面预览（见 §2.6） |

### 2.6 贴图落地：盒子投影，不用那张展开图

`uv_protagonist.py` 出的 UV **带自相交**，直接拿来贴图会糊。但我们的贴图本来就是**无缝平铺**的，
所以 `apply_materials.py` 用**盒子投影**现做 UV：每个面按主法线轴投到对应平面，
坐标除以 `TILE`（当前 0.42 米）平铺。不用切缝、不会自相交、颗粒方向天然跟着表面走。

材质槽按 `docs/09 §7` 分成 `Body` / `Hair` / `ClothInner` / `ClothOuter` / `Leather` /
`Metal` / `Gauntlet` / `Belt` —— 侵蚀三阶段靠换材质，不靠重建模型。

三条踩出来的坑：

| 坑 | 现象 | 对策 |
|---|---|---|
| **按分隔缝中点切材质表** | 每格第 0 / 510 列亮度 0.98（近白），贴上去一圈白边。肉眼看不出，是量"每列平均亮度最亮列"才发现的 | 切点用「上一条缝末尾 +1」到「下一条缝开头 −1」，**整条缝排除**；并断言四边亮度 < 0.85 |
| **无贴图的材质忘了设视口颜色** | 预览里脸和头发**纯白**，看着像导出失败 | `mat.diffuse_color` 和 Principled 的 `Base Color` **两个都要设**（Workbench 用的是前者） |
| **按高度分带走手臂** | 右小臂被吃成布料；笼手盖满整条左臂（09 §5 只到手背包→小臂中段） | 笼手边界**读骨骼权重**（`L_Hand` + `L_Forearm`），手臂靠 `ARM_Z` + 横向偏离比例判定，不用猜 |
| **忘了模型正面不是 -Z** | 游戏里角色**背朝前跑**。模型在 Blender 里正面朝 -Y，经 glTF 的 Y-up 转换后正面朝 **+Z**，而 Godot 角色的前方是 -Z | 在 `Player.tscn` 里设 `VisualModelRotationDegrees = Vector3(0, 180, 0)`。**验证方法**（别靠推理）：用 `ModelShowcase` 渲染 yaw=0 与 yaw=180 两张对照图——前者看到正面、后者看到背面，就证明正面朝 +Z |
| **★ 模型原点在身体中部，不在脚底** | `PlayerActor` 只设 Scale/Rotation、**不补偿原点**，于是角色**腰以下全埋进地板**（实测脚底 y=-0.869、头顶 y=+0.876）。dsh 曾以为「scale 0.888 已经处理了」——**缩放救不了原点偏移**，这是两件事 | 别动 `PlayerActor.cs`（冻结），也别重导 glb（会冲掉别人的权重修复）。用包装场景 [`scenes/actors/PlayerVisual.tscn`](../scenes/actors/PlayerVisual.tscn)：包装层原点=脚底，里面把 glb 实例抬 +0.979（=模型空间的脚底深度）。`FindSkeleton` 是递归的，多一层不影响动画器。**验证**：`scenes/tests/PlayerMountShot.tscn` 会打出「脚底 y / 头顶 y」，必须 ≈0 / ≈1.75 |
| **展示场景会替你补偿原点，游戏不会** | `ModelShowcase` 里有 `root.Position += (0, -box.Position.Y*scale, 0)` 主动把模型贴到脚底，所以**它在哪儿都好看**；同样的模型在游戏里却是埋的。只看展示图会漏掉这个 bug | 量「游戏挂载方式」必须用不复刻补偿的场景（`PlayerMountShot` 就是为此写的），并打印脚底/头顶的**世界 y** 数字，别只看图 |
| **`check_dead_config.ps1` 把「有引用」误判成「零引用」** | 只在**声明行以中文结尾**的字段上发生（实测 `EnemyPostureColor` / `PostureColor`）。rg 输出是 UTF-8，PS 5.1 按 ANSI 解码，行尾中文的末字节把**换行一起吃掉**，下一行（真正的那条引用）被并进来 | 脚本开头设 `[Console]::OutputEncoding = [Text.Encoding]::UTF8`；判据改用 `-replace '^.*?:\d+:', ''` 剥前缀，不再用 `-split ':'`（盘符冒号会让结果随 CWD 变）。修完 **24 → 22，与基线一致** |

### 2.5 UV 展开：**必须按部位分开做**

`uv_protagonist.py` 里有一条踩出来的结论，别再走回头路：

| 做法 | 结果 |
|---|---|
| `smart_project` 一把梭（66°） | 覆盖率 **43.8%**，且同一部位被打散成上千个小岛铺满全图 → 分区图像**碎片海**，人和 AI 都认不出「哪块是左小臂」 |
| `smart_project` 放宽到 89° | 覆盖率 54.8%，**仍然是碎片海**（根因不是参数，是智能展开只按法线夹角切） |
| ★ **按部位分组 `unwrap` ＋ 最后整体 `pack_islands`** | 覆盖率 **84.7%**，9 个部位 = 9 块大连通岛，分区图一眼可读 |

分组展开顺带满足 `docs/09 §6/§7` 的硬要求：**笼手与左臂必须单独一套 UV**（侵蚀三阶段靠换材质）。

```powershell
& 'C:\Users\Gdy\Blender\blender-4.5.13-windows-x64\blender.exe' --background --factory-startup --python tools\uv_protagonist.py
```

产出：`uv_congyun_regionmap.png`（2048，给 AI 的参考图）/ `uv_congyun_layout.png`（带岛边界）/
`model_player_congyun_03_uv.glb`（带 UV 的副本，**不覆盖** `_rigged`，T48 的在制品不受影响）。

> ⚠️ **这张 UV 还带自相交**：按部位 `unwrap` 不做分缝，压平后同一部位会自己叠起来。
> 当**参考图**够用，当**最终贴图布局**不够用——要落地贴图前得先给每个部位手工切缝。

**共同模式**：读一个写死的路径 → 干活 → **把结论打成 `[标签] ...` 的行** → 由调用方 `Select-String` 抓。
（因为 Blender 会把大量噪声打到 stdout，不打标签就没法筛。）

### 2.3 四个坑（都踩过）

| 坑 | 说明 |
|---|---|
| **单位与朝向** | glTF 是 **Y-up**，Blender 是 **Z-up**——导入后模型"站起来"了，但**坐标要按 Z 是高度**读。别拿导出前的数值直接比 |
| **模型可能含垃圾对象** | 主角模型里就藏了一个 **80 面的单位球 `Icosphere`**（半径 1，把包围盒撑到 ±1）。**先量包围盒**，不合理就先怀疑有杂物 |
| **网格可能碎得厉害** | 同一个模型被切成 **1083 个连通块**（退化薄片占绝大多数）。按"连通块"认零件会失败——**它和身体是连在一起的** |
| **导出要显式** | `bpy.ops.export_scene.gltf(filepath=..., export_format='GLB')`；导出后**必须**再跑 `godot --headless --path . --import`，否则 `ResourceLoader` 找不到新资产 |

### 2.4 硬约束（与本项目对齐）

* **动画一律 60fps**（04 §12：逻辑帧是权威，动画只做 `Seek(logicFrame/60)`）。
* **不许改网格外形**：切分/加骨/刷权重可以，面数与轮廓不能变（除非卡片允许）。
* **产物进仓库后必须过 `tools\check.ps1`**（34 步 ＋ 死配置棘轮）。
* 二进制资产（`.glb`）**跟着 `.import` 一起提交**，别只提一个。

---

## 3. 路线 B：blender-mcp（人机同屏时用）

**它是什么**：第三方（**MIT**）的 MCP 集成，PyPI 包 **`blender-mcp`**（当前 **1.9.1**）。
由两部分组成——
1. **`addon.py`**：装在 Blender 里的插件，在 Blender 内部起一个 socket 服务；
2. **MCP 服务端**：MCP 客户端通过它把命令送进那个 socket，**在 Blender 里执行 Python**。

> ⚠️ **它需要 Blender GUI 开着**，并且 addon 处于监听状态。
> 也就是说：**它不能在没有界面的自动化里用**——这正是它不能当 agent 主力的原因。

### 3.1 安装（**2026-09-13 已完成**，下面是实际用的路径）

> 本节原写"本机还没做"，现已装好，记录实际结果以便复现：
> - `blender-mcp 1.9.1`：`uv tool install blender-mcp` → `C:\Users\Gdy\.local\bin\blender-mcp.exe`
> - Blender 插件：把包里的 `bundled/addon.py` 复制成
>   `%APPDATA%\Blender Foundation\Blender\4.5\scripts\addons\blender_mcp_addon.py`，
>   并用无头方式启用 + `save_userpref()`（**只复制不启用是不会加载的**）
> - 已接入三个客户端：ZCode（`~/.zcode/cli/config.json` → 嵌套 `mcp.servers`）、
>   Trae CN（`%APPDATA%\Trae CN\User\settings.json` → `mcpServers`，**该文件带 UTF-8 BOM**）、
>   WorkBuddy（工作区级 `.workbuddy/mcp.json`；置信度中等，未实测）
> - **人还要做一步**：Blender 里按 `N` → BlenderMCP 面板 → `Connect`。
>   那是跑在 Blender 事件循环里的 socket 服务，无头模式起不来。
>   顺序：**先让 Blender 连上，再让客户端连**。
>
> 不熟悉 Blender 的人从 [18-Blender零基础第一步](18-Blender零基础第一步.md) 开始看。

#### ✅ 已经装好了（2026-09-13）

| 步 | 状态 |
|---|---|
| `uv` / `uvx` | ✅ **0.12.5**，pip 方式装的；可执行文件在 `C:\Users\Gdy\AppData\Roaming\Python\Python310\Scripts\uvx.exe`（**不在 PATH**，所以要写全路径） |
| Blender addon | ✅ 已装到 `%APPDATA%\Blender Foundation\Blender\4.5\scripts\addons\blender_mcp.py`（175KB，**端口 9876**） |
| 接进 Codex | ✅ `codex mcp add blender -- <uvx 全路径> blender-mcp`，`codex mcp list` 里状态 **enabled** |

#### ⚠️ 还差两步，其中一步只能由人做

1. **（人做）打开 Blender** → `Edit → Preferences → Add-ons` →
   启用 **"Interface: MCP for Blender"**（新装的话要 disable 再 enable，或重启 Blender）→
   在右侧边栏（N 面板）点 **Start MCP Server**。
   **没有这一步，MCP 那端连不上**（它连的是 Blender 里的 socket）。
2. **（要开新会话）新开一个 Codex 任务**。
   **MCP 工具是在会话启动时加载的**——当前这条会话里看不到 `blender` 的工具，
   哪怕它已经 enabled。这不是故障，是加载时机。

```powershell
# 1) 装 uv（官方安装器；文档特意提醒：别用 pip install uv，否则可能没有 uvx）
#    https://docs.astral.sh/uv/getting-started/installation/

# 2) 把 addon 装进 Blender
uvx blender-mcp install-addon

# 3) 在 Blender 里启用：侧边栏（N 面板）找到 BlenderMCP → Connect
```

### 3.2 接到 Codex

```powershell
codex mcp add blender -- uvx blender-mcp
```

### 3.3 Windows 专属的坑

**GUI 启动的客户端不继承终端的 PATH** → 裸写 `"command": "uvx"` 会报 `spawn uvx ENOENT`。
两种解法（README 给的）：
* 用 `where uvx` 查到全路径，把它填进 `command`；
* 或者包一层：`"command": "cmd", "args": ["/c", "uvx", "blender-mcp"]`。

装不了 uv 的环境还有两条替代：**`pipx`**（`pipx install blender-mcp`，然后填它的绝对路径）、
**Docker**（`docker run -i --rm blender-mcp`）。

---

## 4. 路线 C：`bpy` 当 pip 模块

```powershell
pip install bpy        # 注意：bpy 与 Blender 版本强绑定
```

好处是**不需要 Blender 可执行文件**，纯 Python 就能跑；代价是**版本绑死**，
而且和主 Blender 的版本可能对不上。本项目已经有正式 Blender，**没有理由再引一份**。

---

## 5. 结论：本项目怎么用

1. **agent 干活 → 路线 A**（无头脚本），产物用 `check.ps1` 与自检脚本验收；
2. **人机同屏、要看画面改东西 → 路线 B**（blender-mcp），但它**只在你打开 Blender 时可用**；
3. 路线 C 不用。

> 这份文档的存在本身就是一条纪律：**下次谁要用 Blender，先读它**。
> 之前每次都要重新探"装没装、走不走代理、能不能无头"，那是纯浪费。
