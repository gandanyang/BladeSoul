#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""场景模块套件生成器（T33 P0：地面 / 墙 / 门洞 / 灯笼）。

## 这个工具为什么存在

T32 把白盒做出来了，但白盒是 **CSGBox3D 灰块**——它能证明"尺寸合规"，
证明不了"这里读起来像城下町"。T33 的就是把这些灰块换成真正的模块。

**为什么走程序化通道（而不是 Hunyuan3D）**：见 `docs/13` §7。
一句话——模块件的第一需求是**尺寸精确**（4m 主格 / 3.2m 层高 / 1.2·2.4m 门洞），
而生成式通道恰好不保证尺寸；而且这些都是人造的规则形体，正落在程序化擅长的区间。

## 三个"数字只有一个来源"的约束（别绕过）

1. **尺寸**：`GRID` / `LAYER_HEIGHT` / `DOOR_*` / `WALL` 全部从
   `tools/gen_level_whitebox.py` **import**——那是 10 §3.4 冻结规格的唯一落点。
   这个文件里**没有一个重复的尺寸常量**。
2. **色彩空间**：`COLOR_0` 是**线性**通道（glTF 规范），
   所以调色板必须 sRGB→线性再写。转换函数也是 import 来的
   （`tools/paint_mesh.py` 的 `srgb_to_linear`）——那条缺陷（魔骸显示成灰+粉）
   已经在项目里发生过一次，不要再写第二份实现。
3. **白盒调色板**：`SHELL_COLOR` / `GROUND_COLOR` / `STONE_COLOR` 也是 import 的，
   模块只是把它们往下细分，不另立一套灰。

## ★ 一条几何上的硬规则：模块必须是白盒盒体的**外包络**

白盒盒体同时是**碰撞体**（`use_collision`）与**布局**。模块只做外观、不碰碰撞，
所以模块必须**完全包住**它替换的那个盒体，否则灰盒会从模块里透出来
（尤其在地面：盒体顶面是平的，任何"凹进去的缝隙"都会让灰面顶出来）。

→ 结论：**浮雕只许往外凸（+），不许往里凹（−）**。所有缝隙靠"把相邻块做凸"来表达，
  不是靠"把缝挖深"。`KIT_PAD` 与各 `*_PROUD` 都遵守这一条。

## 用法

    # 生成全部模块到 assets/models/kit/
    python tools/gen_kit.py --out-dir assets/models/kit
    python tools/gen_kit.py --what wall_plank --out-dir assets/models/kit
    python tools/gen_kit.py --list              # 只看清单与面数预算

⚠️ 依赖 trimesh，本机只有 ComfyUI 自带那个 python 有：
    F:/ComfyUI-aki-v3/python/python.exe tools/gen_kit.py ...
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)

# ── 10 §3.4 冻结规格：只在白盒生成器里定义，这里 import ────────────────
import gen_level_whitebox as W                                        # noqa: E402
from paint_mesh import hexof, srgb_to_linear                          # noqa: E402

GRID = W.GRID                     # 4.0 主格距
LAYER_HEIGHT = W.LAYER_HEIGHT     # 3.2 一层净高
DOOR_SINGLE = W.DOOR_SINGLE       # 1.2
DOOR_PASSAGE = W.DOOR_PASSAGE     # 2.4
WALL = W.WALL                     # 0.4 墙厚

KIT_PAD = 0.006      # 模块包络比白盒盒体每边各多这么多：防共面 z-fighting，
                     # 同时保证灰盒一定在模块**内部**（不被看穿）。
                     # 代价：地面顶面比碰撞面高 6mm，角色脚下视觉上沉 6mm——不可见。
RELIEF_STRUCT = 0.030   # 结构料（柱/贯/门楣）凸出量
RELIEF_FACE = 0.006     # 面板/板条 凸出量（细一档，明暗层次靠它）
RELIEF_STONE = 0.026    # 石垣的石块凸出量（石头糙，凸得多）

GROUND_MODULE = GRID * 2.0   # 地面模块 8m（= 2 主格）。铺装是低频的，格大省件数
GROUND_SLAB = GRID * 0.5     # 2m 石板。★ 不能再小：10 §3.2 第 3 条"地面不能有高频图案"
POST_SQUARE = 0.6            # 柱（与白盒 Pillar 一致）

# ══════════════════════════════════════════════════════════════════
#  配色（10 §3.6 颜色纪律：**低饱和青灰**；暖色只允许出现在灯笼上）
#
#  白盒那三个色（SHELL / GROUND / STONE）是这套色的基色——直接 import 过来用，
#  模块只在它们之下细分（板条、缝、石块、腰板…），不另立一套灰。
#  下面新增的条目 = T33 需要的细分色。**这里是场景模块配色的唯一出处。**
# ══════════════════════════════════════════════════════════════════
def _s(hex_str: str):
    return (int(hex_str[0:2], 16), int(hex_str[2:4], 16), int(hex_str[4:6], 16))


def _from_whitebox(rgb: tuple[float, float, float]):
    """白盒的 albedo 浮点色（显示空间）→ 线性 0~255。用于把基色接进来。

    ⚠️ 白盒那几个常量是 **4 元组**（带 alpha）——只取前三位，
    否则会得到一个 4 通道的"颜色"，混进顶点色数组里直接崩
    （实测：`np.asarray(colors, dtype=np.uint8)` 报 inhomogeneous shape）。
    """
    return tuple(srgb_to_linear(int(round(c * 255.0))) for c in rgb[:3])


# —— 基色：来自白盒（不重复定义）——
BASE_SHELL = _from_whitebox(W.SHELL_COLOR)     # 墙基色
BASE_GROUND = _from_whitebox(W.GROUND_COLOR)   # 地面基色
BASE_STONE = _from_whitebox(W.STONE_COLOR)     # 石基色

# —— 木（桁/柱/板壁/木地板）——
#   刻意压饱和度：原木在冷光下不该是暖棕，否则会跟灯笼抢"唯一暖色"的位置
TIMBER_DARK = _s("2F3238")     # 结构料（柱/贯/门楣/窗框）
TIMBER_MID = _s("3B3E44")      # 板壁板条
WOOD_FLOOR = _s("4A423A")      # 木地板（板条色）
WOOD_FLOOR_JOINT = _s("3A342E")  # 木板缝

# —— 土 / 灰泥 ——
DOBE_FACE = _s("4C4B47")       # 土壁面
DOBE_PATCH = _s("3F3E3A")      # 剥落露出的下地
DOBE_SKIRT = _s("45443E")      # 腰板（下半段被雨水打湿）

# —— 石 ——
STONE_FACE = _from_whitebox(W.STONE_COLOR)
STONE_A = _s("3F444B")
STONE_B = _s("464B53")
STONE_C = _s("38404A")
STONE_JOINT = _s("2E333A")

# —— 地面 ——
SOIL_FACE = _s("48463F")       # 夯土（压到接近灰，避免暖）
SOIL_SPECK = _s("413F39")      # 土面的湿块（**大块低频**，不是碎石子）
WETSTONE_FACE = _s("3E4450")   # 湿石板
WETSTONE_HI = _s("4A5160")     # 被打湿反光的那几块
WETSTONE_JOINT = _s("333944")

# —— 灯笼：**全作唯一的暖色**（10 §3.1），别的地方一个都不许有 ——
PAPER_WARM = _s("C89A5A")      # 灯纸（暖）
PAPER_DIM = _s("9A7644")       # 灯纸的暗面
LANTERN_FRAME = _s("23262B")   # 竹骨与上下托
ROPE = _s("3A3B38")            # 挂绳


def _lin(rgb_srgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """sRGB 调色板 → 线性（写进 COLOR_0 的值）。**唯一的一步转换。**"""
    return tuple(srgb_to_linear(c) for c in rgb_srgb)


def _warm() -> tuple[int, int, int]:
    return _lin(PAPER_WARM)


def _hash01(i: int, j: int, salt: float) -> float:
    """确定性伪随机（0~1）。**不用 random**——模块必须是可复现的字节。

    `random` 会让同一份脚本每次生成出不同的石块/板条排布，
    那"模块清单与面数"就不再是一个可对账的数了。
    """
    x = math.sin(i * 127.1 + j * 311.7 + salt * 74.7) * 43758.5453
    return x - math.floor(x)


# ══════════════════════════════════════════════════════════════════
#  构造器：一切几何都是**轴对齐盒子的并集**
#
#  为什么只做盒子：模块件要的是"读得出体量与分块"，不是细节。
#  盒子在构造成立上最稳（不会有自交/翻转法线），面数也最省——
#  而 10 §3.6 给的预算很紧（墙面/地面 ≤ 300 三角面）。
#
#  ★ 每个盒子 24 个顶点（每个面 4 个，不共用）：这样顶点法线 = 面法线，
#    得到硬边的方块感；共用顶点会被平均成圆角，反而不像木料/石头。
#    顺便也让每个盒子可以带自己的顶点色（共用顶点就没法分色了）。
# ══════════════════════════════════════════════════════════════════
class Builder:
    def __init__(self, name: str):
        self.name = name
        self.verts: list[tuple[float, float, float]] = []
        self.faces: list[tuple[int, int, int]] = []
        self.colors: list[tuple[int, int, int]] = []

    def box(self, center, size, color_srgb) -> None:
        """轴对齐盒子。center/size 是 (x, y, z)。"""
        cx, cy, cz = center
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        rgb = _lin(color_srgb)

        # 6 个面，每面一个外法线方向；顶点按逆时针（从外面看）给，法线自然朝外
        faces_def = (
            ((0, 0, 1), ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))),
            ((0, 0, -1), ((1, -1, -1), (-1, -1, -1), (-1, 1, -1), (1, 1, -1))),
            ((1, 0, 0), ((1, -1, 1), (1, -1, -1), (1, 1, -1), (1, 1, 1))),
            ((-1, 0, 0), ((-1, -1, -1), (-1, -1, 1), (-1, 1, 1), (-1, 1, -1))),
            ((0, 1, 0), ((-1, 1, 1), (1, 1, 1), (1, 1, -1), (-1, 1, -1))),
            ((0, -1, 0), ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1))),
        )

        for (_n, quad) in faces_def:
            base = len(self.verts)
            for (sx, sy, sz) in quad:
                self.verts.append((cx + sx * hx, cy + sy * hy, cz + sz * hz))
                self.colors.append(rgb)
            self.faces.append((base, base + 1, base + 2))
            self.faces.append((base, base + 2, base + 3))

    def add(self, verts, faces, color_srgb) -> None:
        """外部构造好的几何（如车削体）整体一个颜色。"""
        rgb = _lin(color_srgb)
        base = len(self.verts)
        for v in verts:
            self.verts.append(tuple(float(c) for c in v))
            self.colors.append(rgb)
        for f in faces:
            self.faces.append((f[0] + base, f[1] + base, f[2] + base))

    def add_colored(self, verts, faces, colors_srgb) -> None:
        """逐顶点给色的几何（灯笼的纸/骨条纹就是靠这个，不额外加几何）。"""
        base = len(self.verts)
        for v, c in zip(verts, colors_srgb):
            self.verts.append(tuple(float(c2) for c2 in v))
            self.colors.append(_lin(c))

        for f in faces:
            self.faces.append((f[0] + base, f[1] + base, f[2] + base))

    def triangles(self) -> int:
        return len(self.faces)

    def trimesh(self):
        import trimesh                                                # noqa: PLC0415

        mesh = trimesh.Trimesh(vertices=np.asarray(self.verts, dtype=float),
                               faces=np.asarray(self.faces, dtype=np.int64),
                               process=False)
        mesh.metadata["name"] = self.name
        used = np.asarray(self.colors, dtype=np.uint8)
        mesh.visual = trimesh.visual.ColorVisuals(
            mesh=mesh, vertex_colors=np.hstack([used, np.full((len(used), 1), 255,
                                                              dtype=np.uint8)]))
        return mesh


# ── 车削（灯笼是回转体：套件的其余件都是盒子，只有它是圆的）────────────
def revolve(loop, segments: int):
    """把剖面 (r, y) 绕 y 轴车削。剖面**开放折线**，两端落在轴上（r=0）时自然收口。"""
    verts, faces = [], []
    n = len(loop)
    on_axis = [abs(r) < 1e-9 for (r, _y) in loop]

    for j in range(segments):
        theta = 2.0 * math.pi * j / segments
        ct, st = math.cos(theta), math.sin(theta)
        for (r, y) in loop:
            verts.append((r * ct, y, r * st))

    for j in range(segments):
        j2 = (j + 1) % segments
        for i in range(n - 1):
            a, b = j * n + i, j * n + i + 1
            c, d = j2 * n + i + 1, j2 * n + i
            if on_axis[i] and on_axis[i + 1]:
                continue
            if on_axis[i]:                       # 上一个环退化成点：三角形扇
                faces.append((a, b, c))
            elif on_axis[i + 1]:                 # 下一个环退化成点
                faces.append((a, b, d))
            else:
                faces.append((a, b, c))
                faces.append((a, c, d))

    return verts, faces


class RevolveColors:
    """车削体的顶点色：按 (环, 段) 给色。用来做灯笼的竹骨条纹——

    ★ 条纹是**颜色**不是**几何**：加一圈凸出的骨会多出 24 段 × 4 面 = 上百个面，
      而 10 §3.6 给装饰件的预算是 1.5k。用颜色画骨，面数一分不多。
    """

    def __init__(self, ring_colors, segments: int):
        self.ring = ring_colors
        self.segments = segments

    def build(self, n_rings: int):
        out = []
        for _j in range(self.segments):
            for i in range(n_rings):
                out.append(self.ring[i])
        return out


# ══════════════════════════════════════════════════════════════════
#  地面模块（8m × 8m，2m 石板）
# ══════════════════════════════════════════════════════════════════
def mod_floor(kind: str) -> Builder:
    """地面模块。局部原点在盒心；**顶面在外包络的 +y**。

    kind: soil（夯土）/ wetstone（湿石板）/ wood（木地板）
    """
    w = d = GROUND_MODULE
    t = WALL
    top = t * 0.5
    b = Builder(f"kit_floor_{kind}")

    def tile(cx, cz, sw, sd, color, lift):
        b.box((cx, top + lift * 0.5, cz), (sw, lift, sd), color)

    if kind == "wetstone":
        # 2m 石板满铺：**缝靠"把石板做凸"表达**，不挖沟（外包络规则）
        b.box((0.0, 0.0, 0.0), (w, t, d), WETSTONE_JOINT)
        n = max(1, int(round(w / GROUND_SLAB)))
        p = w / n
        for i in range(n):
            for k in range(n):
                # 湿斑成组（2×2 一组变亮），不做逐块散点——那是高频图案
                group = (i // 2) * 7 + (k // 2) * 13
                color = WETSTONE_HI if _hash01(group, 0, 3.1) > 0.62 else WETSTONE_FACE
                lift = 0.010 + 0.004 * _hash01(i, k, 1.7)
                tile((i + 0.5) * p - w * 0.5, (k + 0.5) * p - d * 0.5,
                     p * 0.965, p * 0.965, color, lift)

    elif kind == "wood":
        # 木地板：**板条**（沿 x 通长），不是格子。
        # 一版做成 22×22 的格子 = 5,820 面，超预算 19 倍——地板没有"横向的缝"。
        b.box((0.0, 0.0, 0.0), (w, t, d), WOOD_FLOOR_JOINT)
        pitch = 0.36
        n = max(1, int(round(d / pitch)))
        pd = d / n
        for k in range(n):
            color = WOOD_FLOOR if _hash01(0, k, 5.3) > 0.16 else WOOD_FLOOR_JOINT
            tile(0.0, (k + 0.5) * pd - d * 0.5, w * 0.998, pd * 0.88, color, 0.008)

    elif kind == "soil":
        # 夯土：**低频**。地是夯出来的，没有规则缝——
        # 所以不做铺装格，只在整片土面上压出几块大的湿斑（散点会变成高频图案，
        # 10 §3.2 第 3 条：地面高频图案会干扰玩家判断距离）。
        b.box((0.0, 0.0, 0.0), (w, t, d), SOIL_FACE)
        n = 3
        p = w / n
        for i in range(n):
            for k in range(n):
                if _hash01(i, k, 2.9) < 0.34:
                    continue
                lift = 0.004 + 0.003 * _hash01(i, k, 6.6)
                tile((i + 0.5) * p - w * 0.5 + p * 0.14 * (_hash01(i, k, 4.1) - 0.5),
                     (k + 0.5) * p - d * 0.5 + p * 0.14 * (_hash01(i, k, 7.3) - 0.5),
                     p * (0.55 + 0.30 * _hash01(i, k, 1.2)),
                     p * (0.55 + 0.30 * _hash01(i, k, 9.8)), SOIL_SPECK, lift)
    else:
        raise ValueError(kind)

    return b


# ══════════════════════════════════════════════════════════════════
#  墙模块（4m × 3.2m × 0.4m —— 就是 10 §3.4 的主格 × 层高 × 墙厚）
# ══════════════════════════════════════════════════════════════════
def mod_wall(kind: str) -> Builder:
    """墙模块。局部原点在盒心；**厚度轴 = z**（铺装时按白盒盒体的薄轴旋转）。"""
    l, h, t = GRID, LAYER_HEIGHT, WALL
    b = Builder(f"kit_wall_{kind}")
    hz = t * 0.5

    def panel(center, size, color):
        b.box(center, size, color)

    if kind == "plank":
        base = BASE_SHELL
        panel((0.0, 0.0, 0.0), (l, h, t), base)
        n = max(2, int(round(l / 0.60)))                 # 竖向板条
        pw = l / n
        for i in range(n):
            x = (i + 0.5) * pw - l * 0.5
            color = TIMBER_MID if _hash01(i, 0, 1.3) > 0.28 else TIMBER_DARK
            for sgn in (1.0, -1.0):                      # **两面都做**：室内外都看得见
                panel((x, 0.0, sgn * (hz + RELIEF_FACE * 0.5)),
                      (pw * 0.86, h * 0.995, RELIEF_FACE), color)
        for y in (-h * 0.28, h * 0.28):                  # 两道贯（横向压条）
            for sgn in (1.0, -1.0):
                panel((0.0, y, sgn * (hz + RELIEF_STRUCT * 0.5)),
                      (l * 0.995, 0.11, RELIEF_STRUCT), TIMBER_DARK)
        for y in (-h * 0.5 + 0.055, h * 0.5 - 0.055):    # 上下长押
            for sgn in (1.0, -1.0):
                panel((0.0, y, sgn * (hz + RELIEF_STRUCT * 0.45)),
                      (l * 0.995, 0.09, RELIEF_STRUCT * 0.9), TIMBER_DARK)

    elif kind == "dobe":
        panel((0.0, 0.0, 0.0), (l, h, t), DOBE_FACE)
        # 柱 + 贯：土壁是靠木框架撑住的（露明的骨架是"土壁"最好认的特征）
        for x in (-l * 0.5 + 0.075, l * 0.5 - 0.075):
            for sgn in (1.0, -1.0):
                panel((x, 0.0, sgn * (hz + RELIEF_STRUCT * 0.5)),
                      (0.15, h * 0.998, RELIEF_STRUCT), TIMBER_DARK)
        for sgn in (1.0, -1.0):
            panel((0.0, h * 0.30, sgn * (hz + RELIEF_STRUCT * 0.5)),
                  (l * 0.995, 0.13, RELIEF_STRUCT), TIMBER_DARK)
            panel((0.0, -h * 0.5 + 0.025, sgn * (hz + RELIEF_STRUCT * 0.4)),
                  (l * 0.995, 0.06, RELIEF_STRUCT * 0.8), TIMBER_DARK)
            # 腰板（下半段淋雨打湿）
            panel((0.0, -h * 0.5 + 0.42, sgn * (hz + RELIEF_FACE * 0.5)),
                  (l * 0.99, 0.74, RELIEF_FACE), DOBE_SKIRT)
        # 剥落的下地（成组的低频道子，不做散点——散点会变成"高频图案"）
        for (i, j) in ((0, 0), (1, 2), (2, 1), (3, 0)):
            cx = (i + 0.5) * (l / 4.0) - l * 0.5
            cy = (j * 0.42) - 0.30
            for sgn in (1.0, -1.0):
                panel((cx, cy, sgn * (hz + RELIEF_FACE * 0.5)),
                      (l / 4.0 * 0.42, 0.34, RELIEF_FACE), DOBE_PATCH)

    elif kind == "ishigaki":
        base = STONE_JOINT
        panel((0.0, 0.0, 0.0), (l, h, t), base)
        rows = max(2, int(round(h / 0.80)))
        cols = max(2, int(round(l / 1.30)))
        rh, cw = h / rows, l / cols
        shades = (STONE_A, STONE_B, STONE_C, STONE_FACE)
        for r in range(rows):
            for c in range(cols):
                # 错缝：奇数层横向偏移半块（真石垣的必要特征，不然读成砖）
                off = cw * 0.5 if (r % 2 == 1) else 0.0
                cx = (c + 0.5) * cw - l * 0.5 + off
                cx = max(-l * 0.5 + cw * 0.5, min(l * 0.5 - cw * 0.5, cx))
                cy = (r + 0.5) * rh - h * 0.5
                color = shades[int(_hash01(r, c, 4.7) * len(shades)) % len(shades)]
                lift = RELIEF_STONE * (0.55 + 0.45 * _hash01(r, c, 8.2))
                for sgn in (1.0, -1.0):
                    panel((cx, cy, sgn * (hz + lift * 0.5)),
                          (cw * (0.86 + 0.10 * _hash01(r, c, 6.1)),
                           rh * (0.80 + 0.14 * _hash01(r, c, 9.4)),
                           lift), color)
        return b
    else:
        raise ValueError(kind)

    return b


# ══════════════════════════════════════════════════════════════════
#  柱（白盒 Pillar 的对应件）
#
#  ⚠️ 这一件**超出 10 §3.5 的 P0 清单**。加它的理由很具体：
#     道场里有 4 根 stone 材质的柱子，如果只铺墙不铺柱，屋里会**留着四根灰柱子**，
#     铺装反而变得比不铺更刺眼。
# ══════════════════════════════════════════════════════════════════
def mod_post() -> Builder:
    b = Builder("kit_post")
    s = POST_SQUARE
    h = LAYER_HEIGHT
    b.box((0.0, 0.0, 0.0), (s, h, s), TIMBER_DARK)
    b.box((0.0, -h * 0.5 + 0.09, 0.0), (s * 1.30, 0.18, s * 1.30), BASE_STONE)   # 础石
    b.box((0.0, h * 0.5 - 0.055, 0.0), (s * 1.16, 0.11, s * 1.16), TIMBER_MID)   # 柱头
    for (dx, dz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):                          # 四面收分
        b.box((dx * s * 0.5, 0.0, dz * s * 0.5),
              (s * 0.98 if dx == 0 else RELIEF_FACE,
               h * 0.995,
               s * 0.98 if dz == 0 else RELIEF_FACE), TIMBER_MID)
    return b


# ══════════════════════════════════════════════════════════════════
#  门洞（2.4m 通路门：门楣那一块墙 + 露明的门框）
# ══════════════════════════════════════════════════════════════════
def mod_gate() -> Builder:
    """局部原点在**门楣盒体**的盒心（白盒里那只 `DoorLintel`）。

    门框（两侧的柱）会向门楣盒体**下面**伸出去——那部分正好藏在两侧墙模块里，
    所以看不到、也不需要有几何上的收口。
    """
    b = Builder("kit_gate")
    w = DOOR_PASSAGE
    h = LAYER_HEIGHT
    t = WALL
    hz = t * 0.5
    jamb = 0.14
    drop = 0.55          # 门框往下探出去的长度（藏进两侧墙里）

    b.box((0.0, 0.0, 0.0), (w, h, t), BASE_SHELL)                     # 门楣上方的墙

    for sgn in (1.0, -1.0):
        face_z = sgn * (hz + RELIEF_STRUCT * 0.5)
        b.box((0.0, -h * 0.5 + 0.085, face_z),                       # 门楣（冠木）
              (w + jamb * 2.0, 0.17, RELIEF_STRUCT), TIMBER_DARK)
        for sx in (-1.0, 1.0):                                        # 两侧门柱
            b.box((sx * (w * 0.5 + jamb * 0.5), -h * 0.5 - drop * 0.5, face_z),
                  (jamb, drop + 0.30, RELIEF_STRUCT), TIMBER_DARK)
        b.box((0.0, h * 0.5 - 0.06, face_z),                          # 上部长押
              (w * 0.995, 0.10, RELIEF_STRUCT * 0.9), TIMBER_DARK)
    return b


# ══════════════════════════════════════════════════════════════════
#  灯笼（**全作唯一的暖色**）
#
#  与 T34 的接口是硬的：`AtmosphereController.BuildLanterns()` 会在
#  `lantern` / `lantern_indoor` 组的锚点上挂一盏 `OmniLight3D` + 一个
#  **半径 0.16 / 高 0.32 的发光球**（挂在锚点、以锚点为中心）。
#  所以灯笼本体的**中心必须落在锚点上**、且内腔要容得下那个 0.32m 的球。
# ══════════════════════════════════════════════════════════════════
LANTERN_R = 0.215        # 灯体最大半径（> 0.16 的发光球，才罩得住）
LANTERN_H = 0.560        # 灯体高（> 0.32 的发光球）
LANTERN_SEG = 24


def _lantern_body(center_y: float) -> tuple[list, list, list]:
    """提灯（chochin）的灯体：车削 + **用颜色画竹骨**（不加几何）。"""
    rings = 9
    loop, colors = [], []
    for i in range(rings):
        s = i / (rings - 1.0)
        y = center_y - LANTERN_H * 0.5 + s * LANTERN_H
        # 上下收口、中间鼓——提灯的外形就是这个
        profile = math.sin(math.pi * s) ** 0.55
        r = LANTERN_R * (0.30 + 0.70 * profile)
        loop.append((r, y))
        # 竹骨：中间那几道环做深色；上下的托做骨架色
        if i in (0, rings - 1):
            colors.append(LANTERN_FRAME)
        elif i % 2 == 1:
            colors.append(LANTERN_FRAME)
        else:
            colors.append(PAPER_WARM if i != 4 else PAPER_DIM)

    verts, faces = revolve(loop, LANTERN_SEG)
    palette = RevolveColors(colors, LANTERN_SEG).build(rings)
    return verts, faces, palette


def mod_lantern(kind: str) -> Builder:
    """kind: hanging / standing —— **都只有灯体**（绳与柱是另外两件）。

    ★ 与 T34 的接口必须逐字成立：`AtmosphereController` 把一盏 `OmniLight3D`
      和一个 **半径 0.16 / 高 0.32 的发光球**挂在锚点上、**以锚点为中心**。
      所以灯体的中心必须**正好落在锚点**上——这决定了"挂/立"都不能做成整件缩放：
      绳长随天花板高度变、柱长随地面高度变，而灯体一根都不能被拉长。
      → 拆成 `lantern_hanging`/`lantern_standing`（灯体，原点 = 灯心）
        + `lantern_rope`/`lantern_post`（**原点在顶端、只缩 y**）。
      第一版做成整件缩放，结果灯体被拉到 3.6m 高、发光球从灯里露了出来。
    """
    b = Builder(f"kit_lantern_{kind}")
    verts, faces, colors = _lantern_body(0.0)
    b.add_colored(verts, faces, colors)

    top = LANTERN_H * 0.5
    bottom = -LANTERN_H * 0.5
    b.box((0.0, top + 0.035, 0.0), (0.20, 0.07, 0.20), LANTERN_FRAME)      # 上托
    b.box((0.0, bottom - 0.035, 0.0), (0.17, 0.07, 0.17), LANTERN_FRAME)   # 下托

    if kind == "standing":
        b.box((0.0, bottom - 0.095, 0.0), (0.24, 0.055, 0.24), LANTERN_FRAME)  # 柱顶法兰
    elif kind != "hanging":
        raise ValueError(kind)

    return b


LANTERN_TOP_DROP = 0.35      # 灯体上托顶面相对灯心的偏移（挂绳从这里往上接）
LANTERN_BOTTOM_DROP = 0.4025   # 灯体下托底面相对灯心的偏移（柱顶接到这里）
LANTERN_ROPE_LENGTH = 1.0    # 挂绳名义长度（从原点向下）——放置时按天花板高度拉伸
LANTERN_POST_LENGTH = 2.0    # 柱子名义长度（从原点向下）——放置时按地面高度拉伸


def mod_lantern_rope() -> Builder:
    """挂式灯笼的绳 + 挂杆。**原点在绳顶**（向下延伸到 LANTERN_ROPE_LENGTH）。

    道场实测：锚点 y=4.2、天花板底 5.6 —— 绳要有 1.4m，所以绳长**必须**可伸缩
    （固定 0.55 的绳会够不着天花板，整个挂式就不成立了）。
    """
    b = Builder("kit_lantern_rope")
    length = LANTERN_ROPE_LENGTH
    b.box((0.0, -length * 0.5, 0.0), (0.024, length, 0.024), ROPE)
    b.box((0.0, -0.022, 0.0), (0.34, 0.045, 0.06), TIMBER_DARK)   # 挂杆（吊在梁/檐下）
    b.box((0.0, -length + 0.03, 0.0), (0.085, 0.06, 0.085), LANTERN_FRAME)  # 下端挂钩
    return b


def mod_lantern_post() -> Builder:
    """立式灯笼的柱子 + 石基。**原点在柱顶**（向下延伸到 LANTERN_POST_LENGTH）。

    原点放在顶部是为了让放置器只改 y 缩放就能把它接到灯体上——
    石基与柱头法兰会轻微变厚，那是可接受的（它们本来就是方块）。
    """
    b = Builder("kit_lantern_post")
    length = LANTERN_POST_LENGTH
    t = 0.09
    b.box((0.0, -length * 0.5, 0.0), (t, length, t), TIMBER_DARK)
    b.box((0.0, -0.035, 0.0), (0.15, 0.07, 0.15), TIMBER_MID)              # 柱头垫木
    b.box((0.0, -length + 0.055, 0.0), (0.42, 0.11, 0.42), BASE_STONE)     # 石基
    return b


# ══════════════════════════════════════════════════════════════════
#  清单
#
#  每项 = (名字, 工厂, **包络**尺寸, 包络中心, 放置模式, 面数上限, 说明)
#
#  ★ 为什么要有"包络"这一栏（而不是用 glb 的包围盒）：
#    有些模块**故意有超出包络的部分**——门洞的两根门柱要往下探进两侧的墙里
#    （不然门框在门楣下沿就被切断了，看起来像浮着的），立式灯笼的柱子同理。
#    用包围盒去缩放的话，这些"伸出去的部分"会被算进尺寸，整个件反而被压扁。
#    所以包络 = "它替换掉的那个白盒盒体"，伸出部分不在内。
#
#  放置模式：
#    tile   —— 按主格在盒体上平铺（地面/墙），包络→盒体+包络余量
#    fit    —— 整件缩放到盒体上（柱/门洞），不铺
#    anchor —— 原点落在锚点上、**不缩放**（灯笼本体：T34 的发光球以锚点为中心）
#    post   —— 原点落给定点、**只缩放 y**（灯笼柱：长度随锚点高度变，截面不变）
# ══════════════════════════════════════════════════════════════════
Z3 = (0.0, 0.0, 0.0)

MODULES = (
    ("floor_soil", lambda: mod_floor("soil"), (GROUND_MODULE, WALL, GROUND_MODULE), Z3,
     "tile", 300, "地面·夯土（低频湿斑，无碎石子——10 §3.2 第 3 条）"),
    ("floor_wetstone", lambda: mod_floor("wetstone"), (GROUND_MODULE, WALL, GROUND_MODULE), Z3,
     "tile", 300, "地面·湿石板（2m 石板 + 成组湿斑；10 §3.1「湿→高光反射」）"),
    ("floor_wood", lambda: mod_floor("wood"), (GROUND_MODULE, WALL, GROUND_MODULE), Z3,
     "tile", 300, "地面·木地板（通长板条）"),
    ("wall_plank", lambda: mod_wall("plank"), (GRID, LAYER_HEIGHT, WALL), Z3,
     "tile", 300, "墙·板壁（竖板条 + 两道贯 + 上下长押）"),
    ("wall_dobe", lambda: mod_wall("dobe"), (GRID, LAYER_HEIGHT, WALL), Z3,
     "tile", 300, "墙·土壁（露明木框架 + 腰板 + 剥落下地）"),
    ("wall_ishigaki", lambda: mod_wall("ishigaki"), (GRID, LAYER_HEIGHT, WALL), Z3,
     "tile", 300, "墙·石垣（错缝砌石）"),
    ("post", mod_post, (POST_SQUARE, LAYER_HEIGHT, POST_SQUARE), Z3,
     "fit", 300, "柱（础石 + 柱头 + 四面收分）★ 超出 P0 清单，见函数注释"),
    ("gate", mod_gate, (DOOR_PASSAGE, LAYER_HEIGHT, WALL), Z3,
     "fit", 300, "门洞（通路门 2.4m：门楣墙 + 冠木 + 两侧门柱，门柱往下探进墙里）"),
    ("lantern_hanging", lambda: mod_lantern("hanging"), (0.43, 0.70, 0.43), Z3,
     "anchor", 1500, "灯笼·挂式灯体（**全作唯一暖色**；绳是下一件）"),
    ("lantern_standing", lambda: mod_lantern("standing"), (0.53, 0.81, 0.53), Z3,
     "anchor", 1500, "灯笼·立式灯体（柱是下一件）"),
    ("lantern_rope", mod_lantern_rope, (0.34, LANTERN_ROPE_LENGTH, 0.34), Z3,
     "stretch_y", 1500, "灯笼·挂绳 + 挂杆（**原点在绳顶**，按天花板高度拉伸）"),
    ("lantern_post", mod_lantern_post, (0.42, LANTERN_POST_LENGTH, 0.42), Z3,
     "stretch_y", 1500, "灯笼·立式柱子 + 石基（**原点在柱顶**，按地面高度拉伸）"),
)


def log(msg: str) -> None:
    print(msg, flush=True)


def main() -> int:
    ap = argparse.ArgumentParser(description="生成场景模块套件（T33）")
    ap.add_argument("--what", default="all", help="模块名（见 --list），或 all")
    ap.add_argument("--out-dir", default="assets/models/kit")
    ap.add_argument("--list", action="store_true", help="只列清单与预算")
    ap.add_argument("--report", help="把清单/面数写成 JSON")
    args = ap.parse_args()

    if args.list:
        log(f"{'模块':<18}{'面数':>7}{'预算':>7}  {'模式':<7}包络（m）")
        for (name, factory, env, _c, mode, budget, note) in MODULES:
            b = factory()
            ok = "✓" if b.triangles() <= budget else "✗ 超"
            log(f"{name:<18}{b.triangles():>7}{budget:>7}  {mode:<7}"
                f"{env[0]:.2f}×{env[1]:.2f}×{env[2]:.2f}  {ok}  {note}")
        return 0

    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    report: dict = {"grid": GRID, "layer_height": LAYER_HEIGHT,
                    "door_passage": DOOR_PASSAGE, "wall": WALL,
                    "kit_pad": KIT_PAD, "modules": {}}

    targets = [m for m in MODULES if args.what == "all" or m[0] == args.what]
    if not targets:
        log(f"没有这个模块：{args.what}（用 --list 看清单）")
        return 2

    over = []
    for (name, factory, env, env_center, mode, budget, note) in targets:
        b = factory()
        tris = b.triangles()
        if tris > budget:
            over.append((name, tris, budget))

        mesh = b.trimesh()
        out = os.path.join(out_dir, f"{name}.glb")
        # ★ include_normals=True 是必须的：法线不进缓存，导出就不写 NORMAL，
        #   而 Godot 会兜底生成法线 → 渲染"看起来正常"，缺陷因此藏得住（docs/13 §3）。
        mesh.export(out, file_type="glb", include_normals=True)

        ext = np.round(mesh.extents, 3)
        log(f"{name:<18} 面 {tris:>4}/{budget:<5} 顶点 {len(mesh.vertices):>4} "
            f"包围盒 {ext}  包络 {env[0]:.2f}×{env[1]:.2f}×{env[2]:.2f}  "
            f"({mode})  {os.path.getsize(out):,}B")
        report["modules"][name] = {
            "triangles": tris, "budget": budget, "vertices": int(len(mesh.vertices)),
            "envelope": list(env), "envelope_center": list(env_center), "mode": mode,
            "extents": [float(v) for v in ext], "file": f"assets/models/kit/{name}.glb",
            "note": note,
        }

    if over:
        log("")
        for (name, tris, budget) in over:
            log(f"✗ {name} 面数 {tris} 超过 10 §3.6 的 {budget}——模块件超标会直接吃 Draw Call")

    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        log(f"清单已写出: {args.report}")

    return 1 if over else 0


if __name__ == "__main__":
    sys.exit(main())
