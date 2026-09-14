#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 T33 的模块套件铺进关卡——**只做加法，布局零改动**。

## 为什么是"读 .tscn 再追加"，而不是让白盒生成器直接产出成品关卡

`tools/gen_level_whitebox.py` 自己的头注写着：

> 生成之后 **.tscn 就是源文件**——在编辑器里挪墙、加房间都以它为准；
> 这个脚本只在"想重新铺一遍"时用，**不再往回同步**。

而两条关卡已经分别走到了不同的状态（实测 `tools/gen_level_whitebox.py` 的输出）：
**`L01_Gifu.tscn` 与生成器逐字节一致（375 行 / 0 差异）**，
但 **`Dojo.tscn` 已经脱离生成器**（210 → 258 行：T40 开了两个天窗洞、T47 挂了
教学 `TutorialDirector` 与 `DialogueBox`、若干 CSG 的 `layers` 被改过）。

→ 所以**重跑白盒生成器就是覆盖别人两轮的手改**。本工具换一条路：
**把现有 .tscn 当作唯一权威，读它的 `CSGBox3D`（transform + size）来定位模块**，
再把模块作为**新增节点**追加进去。原有行一行不动，因此：

- 布局不可能被改（位移与尺寸都来自原文件，不是本工具算的）
- 可重入（重复跑只替换自己那段带标记的输出）
- 可回退（删掉标记段即回到纯白盒）

## 白盒盒体保持不动，模块只是"包络"套在外面

`CSGBox3D` 同时是**碰撞体**（`use_collision`）与**布局**。模块只做外观、不碰碰撞——
所以每个模块必须是它替换那个盒体的**外包络**（见 `tools/gen_kit.py` 的规则）。
碰撞一个字节没改 → `LevelWhiteboxTest` 的可走性/净空自检继续有效，
而且它顺便就成了"这次铺装有没有动到布局"的**判据**。

## 已知的四类陷阱（都踩过，都已处理）

| 陷阱 | 后果 | 本工具怎么办 |
|---|---|---|
| **CSG 的减法子节点**（T40 的天窗洞：`operation = 2`） | 铺一块板盖住它就**把洞堵了**，室内照明当场失效 | 带减法子节点的盒体**整个跳过** |
| **天花板/楼板**（薄轴是 Y 的水平板） | 当成墙去铺，会得到一圈竖板条糊在天花上 | 薄轴是 Y 且材质不是地面 → 跳过（P0 不做天花，见报告） |
| **门柱/柱基是"故意伸出包络"的** | 按包围盒缩放会把门柱压没 | 模块自带包络尺寸（`gen_kit.MODULES`），伸出部分不算尺寸 |
| **立式灯笼整件缩放会拉长灯体** | 灯体离开锚点 → T34 挂在锚点的发光球露到灯外面 | 灯体与柱子拆成两件，柱子**只缩 y**（截面不变） |

## 用法

    python tools/gen_level_kit.py --level scenes/levels/L01_Gifu.tscn --dry-run   # 只看计划
    python tools/gen_level_kit.py --level scenes/levels/L01_Gifu.tscn
    python tools/gen_level_kit.py --level scenes/levels/Dojo.tscn --level scenes/levels/L01_Gifu.tscn

⚠️ 依赖 trimesh（读模块包围盒），本机只有 ComfyUI 自带那个 python 有：
    F:/ComfyUI-aki-v3/python/python.exe tools/gen_level_kit.py ...
"""

from __future__ import annotations

import argparse
import difflib
import io
import math
import os
import re
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)

import gen_level_whitebox as W                                        # noqa: E402
import gen_kit                                                        # noqa: E402

KIT_PAD = gen_kit.KIT_PAD
GRID = gen_kit.GRID
LAYER_HEIGHT = gen_kit.LAYER_HEIGHT
WALL = gen_kit.WALL

MARK = "; ---- T33 KIT DRESSING (由 tools/gen_level_kit.py 生成，不要手改这一段) ----"

# trimesh 导出的 glb：scene 根节点恒为 `world`，网格挂它的子节点 `kit_<模块名>`。
# 实例化之后这两个都是**实例内部**的节点，材质覆盖必须写在最里面那个上。
KIT_INNER_ROOT = "world"

# 本工具"自己的" ext_resource 指纹——重跑时靠它把上一次插进头部的那几条摘掉。
# 这三个路径只可能由本工具写进关卡，按路径认比按 id 认稳（id 会随去重让位而漂移）。
KIT_EXT_MARKERS = ("res://assets/models/kit/",
                   "/kit_surface.tres", "/kit_lantern.tres")


def is_kit_ext(line: str) -> bool:
    s = line.strip()
    return s.startswith("[ext_resource") and any(k in s for k in KIT_EXT_MARKERS)

MAT_SURFACE = "res://data/materials/kit_surface.tres"
MAT_LANTERN = "res://data/materials/kit_lantern.tres"

# 每关的选型（10 §3.3：城下町 = 湿石板 + 木质町屋；道场 = 木地板 + 土壁）
LEVELS: dict[str, dict] = {
    "scenes/levels/Dojo.tscn": {
        "floor": "floor_wood",
        "wall": "wall_dobe",
        "stone": "wall_ishigaki",
    },
    "scenes/levels/L01_Gifu.tscn": {
        "floor": "floor_wetstone",
        "wall": "wall_plank",
        "stone": "wall_ishigaki",
    },
}

# 挂式 / 立式**不手配**，由几何算：挂式灯笼必须真的挂得住东西。
# 理由：L01 的街灯锚点下方什么都没有（町屋是 P1），配上挂式就是"吊在一根不存在的梁上"；
# 而道场的锚点上方就是天花板（锚点 4.2 / 天花板底 5.6 = 绳长 1.4m）。
# 这是关卡自身的事实，不该由人在这里猜。
SUPPORT_ABOVE_MIN = 0.20    # 支撑面离锚点至少这么高（太近会顶到灯顶）
SUPPORT_ABOVE_MAX = 3.00    # 挂绳够得到的最大距离（绳是拉伸件，上限比固定绳长宽得多）
ROPE_ROOM = 0.35            # 绳要占掉的高度 = 灯体上托顶面（挂点必须留出这么多）

# 模块名 → 包络 / 模式 / 文件（唯一的出处是 gen_kit.MODULES）
_BY_NAME = {m[0]: m for m in gen_kit.MODULES}


def envelope(name: str):
    return tuple(_BY_NAME[name][2])


def mode_of(name: str) -> str:
    return _BY_NAME[name][4]


_TRIS: dict[str, int] = {}


def tris_of(name: str) -> int:
    """模块的**实际**面数（现场构一次再数）。

    ⚠️ 不能拿 MODULES 里那一栏当面数——那一栏是 **10 §3.6 的预算**。
    第一版就这么写错了，于是报告里的"铺装三角面合计"是预算乘件数（虚高一个量级）。
    报告里的数必须是量出来的。
    """
    if name not in _TRIS:
        _TRIS[name] = _BY_NAME[name][1]().triangles()
    return _TRIS[name]


def log(msg: str) -> None:
    print(msg, flush=True)


# ── .tscn 解析 ────────────────────────────────────────────────────
_NODE_RE = re.compile(r'^\[node name="([^"]+)"(?:\s+type="([^"]*)")?(?:\s+parent="([^"]*)")?'
                      r'(?:\s+index="(\d+)")?(?:\s+instance=ExtResource\("([^"]+)"\))?'
                      r'(?:\s+groups=\[([^\]]*)\])?\]$')


class Node:
    __slots__ = ("name", "type", "parent", "groups", "props", "head", "start", "end")

    def __init__(self, head: str, start: int):
        self.head = head
        self.start = start
        self.end = start + 1
        self.name, self.type, self.parent, self.groups, self.props = "", "", "", [], {}
        m = _NODE_RE.match(head)
        if m:
            self.name = m.group(1)
            self.type = m.group(2) or ""
            self.parent = m.group(3) or ""
            if m.group(6):
                self.groups = [g.strip().strip('"') for g in m.group(6).split(",") if g.strip()]

    def vec3(self, key):
        raw = self.props.get(key, "")
        m = re.search(r"Vector3\(([^)]*)\)", raw)
        if not m:
            return None
        parts = [float(v) for v in m.group(1).split(",")]
        return tuple(parts[:3]) if len(parts) >= 3 else None

    def origin(self):
        raw = self.props.get("transform", "")
        m = re.search(r"Transform3D\(([^)]*)\)", raw)
        if not m:
            return (0.0, 0.0, 0.0)
        parts = [float(v) for v in m.group(1).split(",")]
        return tuple(parts[9:12]) if len(parts) >= 12 else (0.0, 0.0, 0.0)

    def basis_identity(self) -> bool:
        raw = self.props.get("transform", "")
        m = re.search(r"Transform3D\(([^)]*)\)", raw)
        if not m:
            return True
        parts = [float(v) for v in m.group(1).split(",")]
        if len(parts) < 12:
            return True
        want = [1, 0, 0, 0, 1, 0, 0, 0, 1]
        return all(abs(a - b) < 1e-6 for a, b in zip(parts[:9], want))


def parse(lines: list[str]):
    """返回 (nodes, header_end)。header_end = 第一个 [node 的下标。"""
    nodes: list[Node] = []
    head_end = len(lines)

    cur: Node | None = None
    for i, line in enumerate(lines):
        s = line.strip()
        if s.startswith("[node "):
            if head_end == len(lines):
                head_end = i
            cur = Node(s, i)
            nodes.append(cur)
            continue
        if s.startswith("[") and not s.startswith("[node "):
            cur = None
            continue
        if cur is not None and "=" in s and not s.startswith(";"):
            k, v = s.split("=", 1)
            cur.props[k.strip()] = v.strip()
            cur.end = i + 1
        elif cur is not None and not s:
            cur.end = i + 1
    return nodes, head_end


# ── 分类 ──────────────────────────────────────────────────────────
def thin_horizontal_axis(size) -> str | None:
    """薄轴（墙厚轴）。返回 'x' / 'z'；若薄轴是 y（水平板）则返回 None。"""
    if size[1] <= min(size[0], size[2]) + 1e-6:
        return None
    return "x" if size[0] <= size[2] else "z"


def classify(node: Node, nodes: list[Node], cfg: dict):
    """→ (模块名, 平铺轴, 说明) 或 (None, None, 跳过原因)。"""
    size = node.vec3("size")
    if size is None:
        return None, None, "没有 size（不是 CSGBox3D？）"

    # ⓪ CSG 的**减法体**本身不是几何（T40 的天窗洞就是一类）
    if node.props.get("operation") == "2":
        return None, None, "CSG 减法体（不是实体几何）"

    # ① CSG 减法子节点：铺上去就把洞堵了（T40 的天窗就是这样被挖出来的）
    for other in nodes:
        if other.parent == node.name and other.props.get("operation") == "2":
            return None, None, f"带 CSG 减法子节点（{other.name}）——铺上会把洞堵掉"

    mat = node.props.get("material", "")

    # ② 地面：材质是 ground 的水平板
    if "ground" in mat:
        return cfg["floor"], "xz", "地面"

    # ③ 薄轴是 Y 的水平板 = 天花板/楼板（P0 不做天花）
    axis = thin_horizontal_axis(size)
    if axis is None:
        return None, None, "薄轴是 Y 的水平板（天花/楼板）——P0 不做，见报告"

    # ④ 门楣：名字里带 Lintel/Gate，或者"宽度正好是通路门宽 + 整个悬在地面之上"
    lintel_like = abs(size[0] - W.DOOR_PASSAGE) < 0.01 or abs(size[2] - W.DOOR_PASSAGE) < 0.01
    bottom = node.origin()[1] - size[1] * 0.5
    if "Lintel" in node.name or "Gate" in node.name or (lintel_like and bottom > 1.0):
        return "gate", axis, "门洞"

    # ⑤ 柱：细高、截面接近正方
    horiz = (size[0], size[2])
    if node.name.startswith("Pillar") or (max(horiz) <= 0.9 and size[1] >= 2.0):
        return "post", axis, "柱"

    # ⑥ 其余是墙；石材质走石垣，其余走本关默认墙
    kind = cfg["stone"] if "stone" in mat else cfg["wall"]
    return kind, axis, "墙"


# ── 平铺 ──────────────────────────────────────────────────────────
def plan_box(node: Node, module: str, axis: str):
    """返回 [(局部单元偏移, 目标单元尺寸, 实例缩放, 绕 y 旋转(度))]。

    包络 → 目标（盒体 + 包络余量）：因为模块的几何是**以包络中心为原点**建的，
    在单元里再按 (i+0.5)/n - 0.5 铺开即可，不需要第二个原点修正。

    ★ 两个"轴"必须分清楚，第一版就在这里算错过一次：
      - 模块**自己**的轴：墙面模块的**长度永远在自己的 x**（4m）、厚度在自己的 z（0.4m）。
      - 盒体的轴：长墙面可能沿着世界 x（(4, 3.2, 0.4)），也可能沿着世界 z（(0.4, 3.2, 4)）。
      所以"沿长边铺"这一格的**步距必须用 env[0]**（模块长度），不是 env[2]。
      第一版对着 env[2]=0.4 算，把 16m 的墙铺成了 **80 件**（正确值是 8）。
    """
    size = node.vec3("size")
    target = tuple(size[i] + 2.0 * KIT_PAD for i in range(3))
    env = envelope(module)
    mode = mode_of(module)

    rotate = 0.0
    grid = [1, 1, 1]

    if axis == "xz":                       # 地面：模块 x→盒体 x、z→盒体 z，不旋转
        perm = (0, 1, 2)
        grid[0] = max(1, int(round(size[0] / env[0])))
        grid[2] = max(1, int(round(size[2] / env[2])))
    else:
        if size[0] >= size[2]:             # 盒体长轴 = 世界 x → 不旋转
            perm, long = (0, 1, 2), 0
        else:                              # 盒体长轴 = 世界 z → 绕 y 转 90°
            perm, long = (2, 1, 0), 2
            rotate = 90.0
        grid[long] = max(1, int(round(size[long] / env[0])))
        grid[1] = max(1, int(round(size[1] / env[1])))

    if mode == "fit":
        grid = [1, 1, 1]

    out = []
    for i in range(grid[0]):
        for j in range(grid[1]):
            for k in range(grid[2]):
                offset = tuple((n + 0.5) / g - 0.5 for n, g in ((i, grid[0]), (j, grid[1]),
                                                                (k, grid[2])))
                local = tuple(offset[a] * target[a] for a in range(3))
                cell = tuple(target[a] / grid[a] for a in range(3))
                # 缩放按"模块轴"给：scale[mod_axis] = cell[box_axis] / env[mod_axis]
                scale = [0.0, 0.0, 0.0]
                for mod_axis in range(3):
                    box_axis = perm[mod_axis]
                    scale[mod_axis] = cell[box_axis] / env[mod_axis]
                out.append((local, cell, tuple(scale), rotate))
    return out


def has_support_above(pos, nodes: list[Node]):
    """锚点正上方有没有"挂得住"的实体（天花板/梁/门楣）？→ (底面 y, 节点名) 或 None。

    判据是纯几何的：某个 CSGBox3D 的水平投影**盖住锚点**，且它的底面落在
    [锚点 + 0.20, 锚点 + 3.00] 之间。下限是别顶到灯顶，上限是绳能探到的距离。
    """
    best = None
    for node in nodes:
        if node.type != "CSGBox3D" or node.props.get("operation") == "2":
            continue
        size, center = node.vec3("size"), node.origin()
        if size is None:
            continue
        if abs(pos[0] - center[0]) > size[0] * 0.5 or abs(pos[2] - center[2]) > size[2] * 0.5:
            continue
        bottom = center[1] - size[1] * 0.5
        if not (SUPPORT_ABOVE_MIN <= (bottom - pos[1]) <= SUPPORT_ABOVE_MAX):
            continue
        # 取**最低**的那块——绳从最近的面挂起，不是从最高的天花板挂起
        if best is None or bottom < best[0]:
            best = (bottom, node.name)
    return best


# ── 生成 .tscn 片段 ───────────────────────────────────────────────
def fmt(v: float) -> str:
    t = f"{v:.5f}".rstrip("0").rstrip(".")
    return t if t not in ("", "-0") else "0"


def node_block(name: str, parent: str, position, scale, yaw_deg: float,
               module: str, mesh_id: str, mat_id: str, cast_shadow_off: bool) -> list[str]:
    """一件模块 = **三个**节点块：实例根 → 内部 `world` → 网格节点（挂材质）。

    ## 为什么不是 `[node type="MeshInstance3D"] ... mesh = ExtResource(glb)`

    这版写法实测**顶点色根本不显示**（对比图 Dojo 只变了 0.69%）。两个原因叠在一起：

    1. **`.glb` 在 Godot 里导入成 `PackedScene`，不是 `Mesh`。**
       把 PackedScene 赋给 `MeshInstance3D.mesh` 类型不符 → 网格压根不出来
       （Dojo 那张"前后一样"就是这么来的）。
    2. **就算渲染出来了也是灰的**：glTF 导入**不会**自动打开
       `vertex_color_use_as_albedo`（实测 glb 里 `materials: null`、开关是 false），
       而模块和魔骸一样**没有 UV/贴图、全靠顶点色** → 不套材质就是一片灰。

    ## 正确写法（照抄 `gen_level_whitebox._showcase`，魔骸用的就是这套）

    实例化 PackedScene，再把材质覆盖**写进实例内部**的网格节点上——
    Godot 的 .tscn 支持对 `[node instance=...]` 内部节点写覆盖块（`index=` 定序），
    所以"套材质"这件事**零代码**：

        [node name="Kit_X" parent="Dressing" instance=ExtResource("kit_1")]
        position = ... / rotation = ... / scale = ...

        [node name="world" index="0" parent="Dressing/Kit_X"]

        [node name="kit_floor_wood" index="0" parent="Dressing/Kit_X/world"]
        material_override = ExtResource("kit_5")

    ⚠️ 内层路径是量出来的，不是猜的：trimesh 导出的 glb 结构恒为
    `world`(容器) → `kit_<模块名>`(网格)，Godot 再在外面包一层同名根节点
    （`tools/read_glb` 可复核，对照魔骸是 `world/geometry_0`）。

    ⚠️ **属性顺序不能换**：`set_rotation` 是**整个替换** basis（不保留缩放），
    所以 `scale` 必须写在 `rotation` 之后，否则模块会被转回 1:1 尺寸。
    """
    path = f"{parent}/{name}"
    lines = [f'[node name="{name}" parent="{parent}" instance=ExtResource("{mesh_id}")]',
             f"position = Vector3({fmt(position[0])}, {fmt(position[1])}, {fmt(position[2])})"]
    if abs(yaw_deg) > 1e-9:
        lines.append(f"rotation = Vector3(0, {fmt(math.radians(yaw_deg))}, 0)")
    lines.append(f"scale = Vector3({fmt(scale[0])}, {fmt(scale[1])}, {fmt(scale[2])})")
    lines.append("")
    lines.append(f'[node name="{KIT_INNER_ROOT}" index="0" parent="{path}"]')
    lines.append("")
    lines.append(f'[node name="kit_{module}" index="0" parent="{path}/{KIT_INNER_ROOT}"]')
    lines.append(f'material_override = ExtResource("{mat_id}")')
    if cast_shadow_off:
        # 灯笼是光源，投影只会得到一个错误的黑影（而且 T34 的灯本身就是零阴影的）
        lines.append("cast_shadow = 0")
    lines.append("")
    return lines


def build_dressing(nodes: list[Node], cfg: dict, level_name: str):
    """→ (节点块行, 用到的模块集合, 统计)"""
    ext_ids: dict[str, str] = {}
    out: list[str] = []
    stats: dict = {"tile_modules": 0, "draw_calls": 0, "skipped": [], "by_module": {}}
    used: dict[str, str] = {}
    serial: dict[str, int] = {}

    def slug(node: Node) -> str:
        """给节点起一个在 Dressing 下**唯一**的名字。

        关卡里同名节点很多（L01 有 5 组 `WallLeft1`，分属不同父节点），
        而 Dressing 下所有件都是兄弟——不带上父路径就会撞名，
        撞名的节点在 Godot 里会变成 `@MeshInstance3D@2` 这种，排查时完全对不上号。
        """
        parent = node.parent.replace("/", "_").strip("_")
        base = f"Kit_{parent}_{node.name}" if parent and parent != "." else f"Kit_{node.name}"
        n = serial.get(base, 0)
        serial[base] = n + 1
        return base if n == 0 else f"{base}_{n}"

    def mesh_id(module: str) -> str:
        if module not in ext_ids:
            ext_ids[module] = f"kit_glb_{module}"
        used[module] = f"res://assets/models/kit/{module}.glb"
        return ext_ids[module]

    def bump(module: str, tris: int):
        stats["by_module"].setdefault(module, {"count": 0, "triangles": tris})
        stats["by_module"][module]["count"] += 1
        stats["draw_calls"] += 1

    for node in nodes:
        if node.type != "CSGBox3D":
            continue
        module, axis, why = classify(node, nodes, cfg)
        if module is None:
            stats["skipped"].append((node.name, why))
            continue

        size = node.vec3("size")
        center = node.origin()
        tris = tris_of(module)

        if mode_of(module) == "tile":
            cells = plan_box(node, module, axis)
            stats["tile_modules"] += 1
            for index, (local, _cell, scale, yaw) in enumerate(cells):
                pos = tuple(center[a] + local[a] for a in range(3))
                out += node_block(f"{slug(node)}_{index}", "Dressing", pos, scale,
                                  yaw, module, mesh_id(module), "kit_mat_surface", False)
                bump(module, tris)
            log(f"  {node.name:<18} → {module:<16} {len(cells):>3} 件  "
                f"{size[0]:.2f}×{size[1]:.2f}×{size[2]:.2f}  {why}")
        else:
            target = tuple(size[i] + 2.0 * KIT_PAD for i in range(3))
            env = envelope(module)
            if axis == "x":
                scale = (target[2] / env[0], target[1] / env[1], target[0] / env[2])
                yaw = 90.0
            else:
                scale = (target[0] / env[0], target[1] / env[1], target[2] / env[2])
                yaw = 0.0
            out += node_block(slug(node), "Dressing", center, scale, yaw,
                              module, mesh_id(module), "kit_mat_surface", False)
            bump(module, tris)
            log(f"  {node.name:<18} → {module:<16}   1 件  "
                f"{size[0]:.2f}×{size[1]:.2f}×{size[2]:.2f}  {why}")

    # ── 灯笼：按 lantern / lantern_indoor 两组的锚点落位 ──
    for node in nodes:
        if node.type != "Marker3D":
            continue
        if not ({"lantern", "lantern_indoor"} & set(node.groups)):
            continue
        pos = node.origin()
        indoor = "lantern_indoor" in node.groups
        tag = "（室内）" if indoor else ""
        label = slug(node)

        support = has_support_above(pos, nodes)
        if support is not None:
            gap = support[0] - (pos[1] + ROPE_ROOM)
            if not (0.10 <= gap <= SUPPORT_ABOVE_MAX):
                stats["skipped"].append(
                    (node.name, f"{support[1]} 离锚点 {support[0] - pos[1]:.2f}m，绳长算出来是 {gap:.2f}m"))
                log(f"  {node.name:<18} ⚠ 跳过：{support[1]} 的距离装不出一根合理的绳")
                continue
            chosen, extra, why = "lantern_hanging", ("lantern_rope", support[0], gap), \
                f"上方有 {support[1]}（底 {support[0]:.2f}）→ 挂式，绳长 {gap:.2f}m"
        elif pos[1] - gen_kit.LANTERN_BOTTOM_DROP - 0.11 >= 0.0:
            need = pos[1] - gen_kit.LANTERN_BOTTOM_DROP
            chosen, extra, why = "lantern_standing", ("lantern_post", need, need), \
                "上方没有可挂的东西 → 立式（自撑）"
        else:
            stats["skipped"].append((node.name, f"锚点 y={pos[1]:.2f} 太低，立式柱撑不到地"))
            log(f"  {node.name:<18} ⚠ 跳过：锚点太低，既挂不住也立不起来")
            continue

        out += node_block(label, "Dressing", pos, (1.0, 1.0, 1.0), 0.0,
                          chosen, mesh_id(chosen), "kit_mat_lantern", True)
        bump(chosen, tris_of(chosen))

        # 绳 / 柱：**原点在顶端、只缩 y**（截面不变——整件缩放会把灯体一起拉长）
        (part, top_y, length) = extra
        nominal = (gen_kit.LANTERN_ROPE_LENGTH if part == "lantern_rope"
                   else gen_kit.LANTERN_POST_LENGTH)
        origin_y = top_y if part == "lantern_rope" else pos[1] - gen_kit.LANTERN_BOTTOM_DROP
        out += node_block(f"{label}_Mount", "Dressing",
                          (pos[0], origin_y, pos[2]), (1.0, length / nominal, 1.0), 0.0,
                          part, mesh_id(part), "kit_mat_surface", False)
        bump(part, tris_of(part))
        log(f"  {node.name:<18} → {chosen:<16} 灯 y={pos[1]:.2f}{tag}"
            f" + {part} 长 {length:.2f}m  {why}")

    return out, used, stats


def apply(level_path: str, root: str, dry_run: bool) -> dict:
    cfg = LEVELS[level_path]
    full = os.path.join(root, level_path)
    with io.open(full, encoding="utf-8") as handle:
        original = handle.read().splitlines()

    # 可重入：先把自己上一次生成的东西切掉。它有**两处**，别只切尾部——
    #   ① 尾部带 MARK 的节点段；
    #   ② 头部插进去的 kit ext_resource：它们在第一个 [node] **之前**，不在 MARK 之后。
    # 只切 ① 的话每跑一次就积一层（实测重跑后"原有行"从 258 涨到 267，
    # 且 id 一路漂到 kit_10+，白盒那几条被挤到后面）。
    if MARK in original:
        original = original[:original.index(MARK)]

    nodes, head_end = parse(original)
    stale = [s for s in original[:head_end] if is_kit_ext(s)]
    if stale:
        original = ([s for s in original[:head_end] if not is_kit_ext(s)]
                    + original[head_end:])
        head_end -= len(stale)
        log(f"  重入：摘掉上次插的 {len(stale)} 条 kit ext_resource")
    log(f"=== {level_path}：解析到 {len(nodes)} 个节点（原有 {len(original)} 行）===")
    blocks, used, stats = build_dressing(nodes, cfg, level_path)

    # ── 组装：新 ext_resource 插在头部（第一个 [node 之前），原有行一行不动 ──
    head = original[:head_end]
    tail = original[head_end:]

    existing = {m.group(1) for m in
                (re.match(r'\[ext_resource type="[^"]*" path="[^"]*" id="([^"]+)"\]', s.strip())
                 for s in head) if m}

    new_ext: list[str] = []
    ids: dict[str, str] = {}
    n = 0
    for module, path in sorted(used.items()):
        n += 1
        rid = f"kit_{n}"
        while rid in existing:
            n += 1
            rid = f"kit_{n}"
        ids[f"kit_glb_{module}"] = rid
        new_ext.append(f'[ext_resource type="PackedScene" path="{path}" id="{rid}"]')

    mats = [("kit_mat_surface", MAT_SURFACE), ("kit_mat_lantern", MAT_LANTERN)]
    for (key, path) in mats:
        n += 1
        rid = f"kit_{n}"
        while rid in existing:
            n += 1
            rid = f"kit_{n}"
        ids[key] = rid
        new_ext.append(f'[ext_resource type="Material" path="{path}" id="{rid}"]')

    # 把占位 id 换成真 id
    def fix(line: str) -> str:
        for key, rid in ids.items():
            line = line.replace(f'"{key}"', f'"{rid}"')
        return line

    body = [fix(line) for line in blocks]

    assembled = ([f"[gd_scene load_steps=0 format=3]"] + new_ext + head[1:] + tail
                 + ["", MARK, '[node name="Dressing" type="Node3D" parent="."]', ""] + body)

    steps = sum(1 for s in assembled if s.startswith("[ext_resource")
                or s.startswith("[sub_resource")) + 1
    assembled[0] = f"[gd_scene load_steps={steps} format=3]"

    # ── 自检：原有行必须一行不少（只允许改 load_steps 那一行）──
    new_set = set(assembled)
    missing = [s for s in original
               if s not in new_set and not s.startswith("[gd_scene")]
    if missing:
        log("✗ 自检失败：有原有行在输出里不见了——这属于改到了布局，必须停下来查：")
        for s in missing[:12]:
            log(f"    {s}")
        raise SystemExit(3)

    diff = list(difflib.unified_diff(original, assembled, "白盒", "铺装后", lineterm="", n=0))
    stats["original_lines"] = len(original)
    stats["dressed_lines"] = len(assembled)
    stats["added_lines"] = len(assembled) - len(original)
    stats["missing_original_lines"] = 0

    log(f"  原有行 {len(original)} → 铺装后 {len(assembled)}（+{stats['added_lines']}，"
        f"diff 只应出现新增行与 load_steps 一行）")
    log(f"  模块件数 {stats['draw_calls']}（每件一个 draw call，07 §7 上限 800）")
    for (module, info) in sorted(stats["by_module"].items()):
        log(f"    {module:<20} × {info['count']:<4} 面 {info['triangles']:>4}  "
            f"合计 {info['count'] * info['triangles']:>7} 三角面")
    total = sum(i["count"] * i["triangles"] for i in stats["by_module"].values())
    stats["total_triangles"] = total
    log(f"  铺装三角面合计 {total:,}")
    if stats["skipped"]:
        log("  跳过：")
        for (name, why) in stats["skipped"]:
            log(f"    {name:<18} {why}")

    if dry_run:
        log("  （--dry-run：没有写文件）")
        return stats

    with io.open(full, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(assembled).rstrip() + "\n")
    log(f"  已写出 {level_path}")

    with io.open(os.path.join(root, ".workbuddy", "tmp",
                              f"kit_diff_{os.path.basename(level_path)}.txt"),
                 "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(diff))
    return stats


def main() -> int:
    ap = argparse.ArgumentParser(description="把 T33 模块铺进关卡（只做加法）")
    ap.add_argument("--level", action="append", default=[],
                    help="关卡 .tscn（可给多个；默认两关都铺）")
    ap.add_argument("--dry-run", action="store_true", help="只打印计划，不写文件")
    args = ap.parse_args()

    root = os.path.dirname(_HERE)
    levels = args.level or list(LEVELS.keys())

    for level in levels:
        if level not in LEVELS:
            log(f"✗ 没有 {level} 的选型配置（LEVELS 里加一条）")
            return 2
        apply(level, root, args.dry_run)
        log("")

    return 0


if __name__ == "__main__":
    sys.exit(main())
