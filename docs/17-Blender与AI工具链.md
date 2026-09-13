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

### 3.1 安装（三步，本机还没做）

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
