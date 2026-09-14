#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""程序化生成魔骸的小道具 —— T27 交付物里缺的那两件：**阵笠** 与 **打刀**。

T27 的交付物写的是「魔骸足兵（**阵笠** + 残破胴 + **打刀**）+ 魔骸枪兵」，
但足兵一直只有"人"：笠与刀从来没做过（T39 当时采纳的建议正是
"先只生成人，笠与刀单独生成——每件都简单，而且独立部件更容易复用"）。

## 为什么这两件**故意不走** Hunyuan3D

因为这两件的已知失败模式，docs 里全都记着，而它们都是**生成式通道的固有代价**：

| 已记录的失败 | 记在哪 | 程序化为什么能绕过 |
|---|---|---|
| **薄片/悬空结构会被做坏** | TASKS.md T39「阵笠是最容易被 clean topology 做坏的（retopo 常把薄片糊成一块或穿洞）」 | 车削一个**闭合的、有厚度的壳**——"薄片"在构造上不存在 |
| **细长结构伸出画面 → 深度估计失准、形状散掉** | 12 §6 第 4 条（打刀正是细长物） | 长宽厚按参数给定，不经过深度估计 |
| **产物没有 NORMAL → 引擎渲染成纯白块** | docs/13 §3（T35 验收第一版就是这么翻车的） | 导出前显式算一次法线（沿用 `cleanup_mesh.py` 的做法） |
| **必须先清碎渣再降面，否则降不动** | docs/13 §3（660k 面 / 21 万块，降到 81,016 面卡住） | 面数按参数生成，**没有碎渣可去** |

尺寸也不是"看着差不多"：**阵笠的落点由脚本对足兵本体求解**（`--fit`）——
从下往上扫，第一个"不与本体穿模"的高度就是它能落到的最低处。
歪掉的笠会先在一侧碰到头，这就是"歪"的物理来源，不是手填的一个角度。

## 这条通道的边界（别当成万能的）

程序化只适合**人造的、几何规则的**东西：笠、刀、枪、甲片、建筑构件。
**有机形体（人、魔骸的躯干、布料）仍然必须走生成式通道**——
这一点和 12 §6 的判断一致，不要因为本工具好用就把它推广到人身上。

## 用法

    # 生成几何（顶点色交给 paint_mesh.py，调色板与色彩空间只在那一个地方定义）
    python tools/gen_props.py --what both --out-dir assets/models
    python tools/gen_props.py --what jingasa --out-dir assets/models

    # 求阵笠落在足兵头上的实际高度（不写文件，只报数字）
    python tools/gen_props.py --what jingasa --fit assets/models/ashigaru_v2d_clean.glb

⚠️ `tools/*.py` 依赖 trimesh，本机**只有 ComfyUI 自带的那个 python 有**：
    F:/ComfyUI-aki-v3/python/python.exe tools/gen_props.py ...
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import numpy as np
import trimesh

# ══════════════════════════════════════════════════════════════════
#  阵笠（jingasa）规格
#
#  10 §1：足兵"戴着**歪掉的阵笠**"。这不是美术口味，是叙事要求——
#  docs/TASKS.md T27 最要紧的一条约束是"必须让玩家看得出它曾经是个人"，
#  而阵笠是足兵身上**唯一一件人造物**，是那一句"疼……"能不能成立的东西。
#
#  尺寸这么定的（不是拍的）：足兵本体实测 1.355 x 1.700 x 0.457，
#  头宽 0.623、肩宽 0.802（AGENTS.md §6：先量包围盒，再决定按尺寸做的判断）。
#  阵笠直径取 **0.86**（略宽于肩）——真实阵笠的宽度大致就是个肩宽的量级；
#  比肩窄会读成"小帽子"，比肩宽太多会读成"伞"。
# ══════════════════════════════════════════════════════════════════
JINGASA_R = 0.400          # 笠缘外半径（m）→ 直径 0.80
JINGASA_H = 0.185          # 笠顶相对笠缘平面的高度（浅圆锥）
JINGASA_SHELL = 0.020      # 缘厚。"有明确厚度、不是薄片"（12 §6 第 3 条）
JINGASA_TOP_R = 0.024      # 顶面平台半径（真实阵笠顶上有个小座）
JINGASA_CAVITY_DEPTH = 0.072
# ★ 内腔（笠底那个碟）的深度，**单位是"笠缘平面往上的高度"**。
#   这一条决定"笠戴到头上的哪个位置"，是整个笠最要紧的一个数：
#     内腔越深 → 头能进去越多 → 笠缘落到越低（罩住整颗头，甚至压到肩上）
#     内腔越浅 → 头刚进去就顶住 → 笠缘停在靠头顶的位置（眉眼露在笠缘下面）
#   ⚠️ 第一版这里写成 `H - 0.030 = 0.205`，也就是**内腔深 20.5cm**——
#   而足兵的"头"只有 0.21m 高（且横向被压扁成 0.62m 宽），
#   于是笠一路滑到肩膀上，渲染出来像披了件斗篷。**名字起错会一直误导自己**，
#   所以改名为"深度"，不再用"顶点比顶面低多少"这种反着说的写法。
JINGASA_SEG = 44           # 圆周分段

# "歪掉"：两轴倾斜 + 一个凹坑 + 笠缘的低频起伏（确定性，不用随机数）
JINGASA_TILT_X = 7.0       # 绕 x 轴（度）
JINGASA_TILT_Z = 5.0       # 绕 z 轴（度）
JINGASA_DENT = 0.045       # 凹坑深度（相对半径）——被砸过
JINGASA_DENT_AT = 2.35     # 凹坑方位角（rad）
JINGASA_DENT_W = 0.55      # 凹坑角宽（rad）
JINGASA_WOBBLE = (0.010, 0.006)   # 笠缘起伏的两个谐波幅值（m）

# 外轮廓 (r/R, h/H)，从**顶面边缘**到**笠缘**（r 单调增、h 单调减 → 单值，便于内腔求解）。
# 形状是"直线锥 + 靠近笠缘处略微下垂"——阵笠的笠缘本来就是微微外张下压的，
# **不要**做成凸起来的圆顶：那是斗笠/蘑菇，不是阵笠。第一版就是太凸，
# 渲染出来像一个平碟子扣在头上。
JINGASA_PROFILE = (
    (0.0605, 1.000),
    (0.300, 0.700),
    (0.600, 0.400),
    (0.850, 0.175),
    (0.950, 0.085),
    (1.000, 0.000),
)

# ══════════════════════════════════════════════════════════════════
#  打刀（uchigatana）规格
#
#  10 §1：足兵是"残破胴 + 打刀"。刀按 12 §6 第 4 条**单独生成**
#  （竖直持刀会比人还高，必然伸出画面）——这条已经在长枪上验证过。
#
#  模型沿 +y 建：**柄头在 y=-TSUKA，切先朝上到 y=+BLADE+鍔厚/2**。
#  这个朝向是为了让 tools/paint_mesh.py 的**高度分层**能正确落色
#  （柄黑 / 鍔铁色 / 刀身冷灰 / 切先红光）。
#  ★ 局部原点取【握把中心】，方便挂到手上；不是柄头，也不是刀尖。
# ══════════════════════════════════════════════════════════════════
KATANA_TSUKA = 0.255       # 柄长（含柄头）
KATANA_TSUBA_T = 0.007     # 鍔厚
KATANA_BLADE = 0.760       # 刀身长（鍔→切先）
KATANA_W0 = 0.033          # 元幅（刀根宽）
KATANA_W1 = 0.021          # 先幅（近切先宽）
KATANA_T0 = 0.0080         # 元重（厚）
KATANA_T1 = 0.0048         # 先重
KATANA_SORI = 0.022        # 反り（中段最鼓，抛物线）
KATANA_RINGS = 26          # 刀身环数
KATANA_TSUBA = (0.042, 0.035, 3.2)   # 鍔的（长半轴, 短半轴, 超椭圆指数）→ 略方，铁鍔
KATANA_GRIP = (0.026, 0.019)         # 柄截面（长半轴, 短半轴）
KATANA_GRIP_SEG = 14
KATANA_GRIP_RINGS = 15
KATANA_CHIPS = ((0.44, 0.0060, 0.035), (0.72, 0.0042, 0.020))   # (位置s, 深度m, 宽s) 刃こぼれ


def log(msg: str) -> None:
    print(msg, flush=True)


# ── 小工具 ────────────────────────────────────────────────────────
def rot_matrix(tilt_x_deg: float, tilt_z_deg: float) -> np.ndarray:
    """Rz(tz) · Rx(tx)：对角色的"歪"用两轴复合，单轴看起来像整个模型斜了。"""
    tx, tz = math.radians(tilt_x_deg), math.radians(tilt_z_deg)
    cx, sx, cz, sz = math.cos(tx), math.sin(tx), math.cos(tz), math.sin(tz)
    rx = np.array([[1.0, 0.0, 0.0], [0.0, cx, -sx], [0.0, sx, cx]])
    rz = np.array([[cz, -sz, 0.0], [sz, cz, 0.0], [0.0, 0.0, 1.0]])
    return rz @ rx


def make_mesh(verts, faces, name: str) -> trimesh.Trimesh:
    """统一出口：合并重复顶点 → 去退化面 → 重算法线（法线是 docs/13 §3 那个坑）。"""
    mesh = trimesh.Trimesh(vertices=np.asarray(verts, dtype=float),
                           faces=np.asarray(faces, dtype=np.int64),
                           process=True)
    try:
        mesh.update_faces(mesh.nondegenerate_faces())
    except Exception:                                    # noqa: BLE001 - 版本差异，不致命
        pass
    mesh.metadata["name"] = name
    return mesh


def finish_normals(mesh: trimesh.Trimesh) -> trimesh.Trimesh:
    mesh = mesh.copy()
    mesh.fix_normals()          # 统一面朝向——朝向不统一时"法线"没有意义
    _ = mesh.vertex_normals     # 触发计算并进缓存，导出才带得上 NORMAL
    return mesh


def revolve(loop, segments: int, deform=None, closed: bool = True):
    """把剖面（半平面 (r,h)，r>=0）绕 y 轴车削成回转体。

    `closed=True`  —— 剖面首尾相接（真正的环，如"笠缘一圈的厚度"那种闭合壳）。
    `closed=False` —— 剖面是**开放折线**，两端各自落在 r=0 的轴上。
                      此时**不能**接最后一段：两个 r=0 的点绕一圈会退化成同一个顶点，
                      那条带子的面会退化成零面积，水密判据直接失效
                      （第一版就是这么让 `--fit` 报不了数字的）。
                      两端在轴上自然收口，拓扑上依旧是闭合曲面。

    `make_mesh` 的 merge_vertices 会把同一位置的顶点并起来——**不要**手工去重。
    """
    verts: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int]] = []
    n = len(loop)

    for j in range(segments):
        theta = 2.0 * math.pi * j / segments
        ct, st = math.cos(theta), math.sin(theta)
        for (r, h) in loop:
            rr, hh = deform(r, h, theta) if deform else (r, h)
            verts.append((rr * ct, hh, rr * st))

    span = n if closed else n - 1
    for j in range(segments):
        j2 = (j + 1) % segments
        for i in range(span):
            i2 = (i + 1) % n
            faces.append((j * n + i, j * n + i2, j2 * n + i2))
            faces.append((j * n + i, j2 * n + i2, j2 * n + i))

    return verts, faces


def extrude_ring(ring, y0: float, y1: float):
    """把 xz 平面上的闭合多边形沿 y 拉成柱体（两端封盖），用于鍔。"""
    n = len(ring)
    verts = [(x, y0, z) for (x, z) in ring] + [(x, y1, z) for (x, z) in ring]
    faces = []
    for i in range(n):
        i2 = (i + 1) % n
        faces.append((i, i2, n + i2))
        faces.append((i, n + i2, n + i))

    c0 = len(verts)
    verts.append((0.0, y0, 0.0))
    for i in range(n):
        faces.append((c0, (i + 1) % n, i))

    c1 = len(verts)
    verts.append((0.0, y1, 0.0))
    for i in range(n):
        faces.append((c1, n + i, n + (i + 1) % n))

    return verts, faces


def superellipse(a: float, b: float, pts: int, power: float):
    """|x/a|^p + |z/b|^p = 1。p=2 是椭圆，p>2 偏方（铁鍔的方肩）。"""
    out = []
    for i in range(pts):
        th = 2.0 * math.pi * i / pts
        c, s = math.cos(th), math.sin(th)
        out.append((a * math.copysign(abs(c) ** (2.0 / power), c),
                    b * math.copysign(abs(s) ** (2.0 / power), s)))
    return out


# ══════════════════════════════════════════════════════════════════
#  阵笠
# ══════════════════════════════════════════════════════════════════
def jingasa_loop():
    """剖面（**开放折线**，两端都落在 r=0 的轴上）：顶面心 → 顶面缘 → 外锥 → 笠缘 → 内腔 → 内腔顶。

    内腔是一块**浅碟**，深度 = JINGASA_CAVITY_DEPTH，与外锥的等厚壳**故意不一致**：
    真实阵笠的内腔就是浅的（头只进去一点点，笠就架在头顶上），
    等厚壳会让内腔跟外锥一样深，笠只能整个套到脖子上——那就不像戴帽子了。
    """
    loop = [(0.0, JINGASA_H)]                      # 顶面中心
    loop += [(r * JINGASA_R, h * JINGASA_H) for (r, h) in JINGASA_PROFILE]
    loop.append((JINGASA_R - JINGASA_SHELL, 0.0))  # 笠缘内侧 → 缘的厚度
    loop.append((0.0, JINGASA_CAVITY_DEPTH))        # 内腔顶点（浅碟的中心）
    return loop


def jingasa_deform(r: float, h: float, theta: float):
    """歪 + 凹 + 笠缘起伏。**全部是确定性函数**（可复现，不用 random）。

    ⚠️ 两个形变都必须**随半径收敛到 0**：剖面两端落在 r=0 的轴上，
    如果那里还带着 θ 相关的起伏，轴上的点就**不会并成一个顶点**，
    会留下一圈约 1.4cm 的洞——外壳不水密，`--fit` 的落点判据就整个不能用了
    （第一版 660 面、水密 False 就是这个原因）。
    """
    # 1) 一个方位被砸凹：半径压进去，那块的高度也顺带塌一点
    d = abs(math.atan2(math.sin(theta - JINGASA_DENT_AT),
                       math.cos(theta - JINGASA_DENT_AT)))
    dent = JINGASA_DENT * math.exp(-(d / JINGASA_DENT_W) ** 2)

    # 2) 笠缘起伏：低频谐波。只在**靠近笠缘**处起作用——
    #    高度上越靠顶点越小，半径上也要收敛（否则轴上收不了口）。
    near_rim = max(0.0, 1.0 - h / JINGASA_H)
    radial = min(1.0, r / (0.30 * JINGASA_R))
    a1, a2 = JINGASA_WOBBLE
    wobble = (near_rim * radial
              * (a1 * math.cos(3.0 * theta + 0.7) + a2 * math.sin(5.0 * theta + 2.1)))

    return r * (1.0 - dent), h + wobble - dent * near_rim * radial * 0.055


def build_jingasa() -> trimesh.Trimesh:
    verts, faces = revolve(jingasa_loop(), JINGASA_SEG, jingasa_deform, closed=False)

    mesh = make_mesh(verts, faces, "jingasa_v1")
    # "歪掉"是**烘进网格的**（不是场景里的一个旋转）：
    # 戴上它的只有足兵，歪是这只笠的设定（10 §1），烘进去场景端就不必再记一个角度。
    # 想改成"正戴"，改顶部 JINGASA_TILT_* 两个常量重生成即可。
    tilt = np.eye(4)
    tilt[:3, :3] = rot_matrix(JINGASA_TILT_X, JINGASA_TILT_Z)
    mesh.apply_transform(tilt)
    return finish_normals(mesh)


# ══════════════════════════════════════════════════════════════════
#  打刀
# ══════════════════════════════════════════════════════════════════
def katana_centerline(s: float) -> float:
    """反り：中段最鼓的抛物线（0 在鍔、1 在切先）。"""
    return KATANA_SORI * 4.0 * s * (1.0 - s)


def katana_edge_chip(s: float) -> float:
    """刃こぼれ：几处崩口。只啃**刃侧**（+x），刀栋不动。"""
    total = 0.0
    for at, depth, width in KATANA_CHIPS:
        total += depth * math.exp(-((s - at) / width) ** 2)
    return total


def katana_cross_section(s: float):
    """刃侧的截面（x 相对中线, z）。7 点：刃 / 镐 / 栋。"""
    w = KATANA_W0 + (KATANA_W1 - KATANA_W0) * s
    t = KATANA_T0 + (KATANA_T1 - KATANA_T0) * s
    edge_x = w * 0.5 - katana_edge_chip(s)
    return (
        (edge_x, 0.0),
        (w * 0.20, t * 0.5),
        (-w * 0.40, t * 0.5),
        (-w * 0.50, t * 0.30),
        (-w * 0.50, -t * 0.30),
        (-w * 0.40, -t * 0.5),
        (w * 0.20, -t * 0.5),
    )


def build_katana_blade():
    sec = len(katana_cross_section(0.0))
    y0 = KATANA_TSUBA_T * 0.5
    verts, faces = [], []

    for i in range(KATANA_RINGS):
        s = i / KATANA_RINGS
        cx = katana_centerline(s)
        y = y0 + s * KATANA_BLADE
        for (dx, dz) in katana_cross_section(s):
            verts.append((cx + dx, y, dz))

    for ring in range(KATANA_RINGS - 1):
        for i in range(sec):
            i2 = (i + 1) % sec
            a = ring * sec + i
            b = ring * sec + i2
            c = (ring + 1) * sec + i2
            d = (ring + 1) * sec + i
            faces.append((a, b, c))
            faces.append((a, c, d))

    # 切先：最后一环收到一个点
    tip = len(verts)
    verts.append((katana_centerline(1.0), y0 + KATANA_BLADE, 0.0))
    last = (KATANA_RINGS - 1) * sec
    for i in range(sec):
        faces.append((last + i, last + (i + 1) % sec, tip))

    # 刀根封盖（会被鍔挡住，只为水密）
    root = len(verts)
    verts.append((katana_centerline(0.0), y0, 0.0))
    for i in range(sec):
        faces.append((root, (i + 1) % sec, i))

    return verts, faces


def build_katana_grip():
    """柄：椭圆截面 + 柄巻的横向起伏 + 柄头外扩。"""
    rings = []
    for i in range(KATANA_GRIP_RINGS):
        s = i / (KATANA_GRIP_RINGS - 1)          # 0 = 柄头(最下), 1 = 鍔侧
        y = -KATANA_TSUKA + s * KATANA_TSUKA
        # 柄头（最下面 6%）外扬一点，读得出"这里到底了"
        flare = 1.14 if s < 0.06 else 1.0
        # 柄巻：6 道缠绕的起伏
        wrap = 1.0 + 0.05 * max(0.0, math.sin(s * math.pi * 6.0))
        rx = KATANA_GRIP[0] * wrap * flare * (1.0 - 0.06 * s)
        rz = KATANA_GRIP[1] * wrap * flare * (1.0 - 0.06 * s)
        rings.append((y, superellipse(rx, rz, KATANA_GRIP_SEG, 2.4)))

    n = KATANA_GRIP_SEG
    verts, faces = [], []
    for (y, ring) in rings:
        for (x, z) in ring:
            verts.append((x, y, z))

    for j in range(len(rings) - 1):
        for i in range(n):
            i2 = (i + 1) % n
            a, b = j * n + i, j * n + i2
            c, d = (j + 1) * n + i2, (j + 1) * n + i
            faces.append((a, b, c))
            faces.append((a, c, d))

    for y, flip in ((rings[0][0], False), (rings[-1][0], True)):
        cap = len(verts)
        verts.append((0.0, y, 0.0))
        base = 0 if not flip else (len(rings) - 1) * n
        for i in range(n):
            i2 = (i + 1) % n
            faces.append((cap, base + i, base + i2) if flip else (cap, base + i2, base + i))

    return verts, faces


def build_uchigatana() -> trimesh.Trimesh:
    a, b, p = KATANA_TSUBA
    tsuba_v, tsuba_f = extrude_ring(superellipse(a, b, 20, p),
                                    -KATANA_TSUBA_T * 0.5, KATANA_TSUBA_T * 0.5)
    blade_v, blade_f = build_katana_blade()
    grip_v, grip_f = build_katana_grip()

    verts, faces = [], []
    for v, f in ((blade_v, blade_f), (tsuba_v, tsuba_f), (grip_v, grip_f)):
        off = len(verts)
        verts += list(v)
        faces += [(x + off, y + off, z + off) for (x, y, z) in f]

    return finish_normals(make_mesh(verts, faces, "uchigatana_v1"))


# ══════════════════════════════════════════════════════════════════
#  落点求解：把"笠该戴在哪"从手填变成一个算出来的数
# ══════════════════════════════════════════════════════════════════
def fit_jingasa(hat: trimesh.Trimesh, body_path: str,
                lo: float = 0.40, hi: float = 1.10, step: float = 0.0005):
    """从下往上扫，找**第一个不与本体穿模**的笠缘平面高度 = 笠能落到的最低处。

    这就是"歪掉的阵笠"的物理来源：歪的一侧会先碰到头，所以是**一侧先落**。
    判据用 `contains`（点在实体内），要求外壳水密——不水密就直接报错，不要给假数字。

    ★ 带回**对照组**（AGENTS.md §7：有对照实验的必须给出对照组数据）：
      - `clearance_mm`：落点上，本体上半身顶点到笠表面的最近距离。
        约等于 0 = 刚好搭住；明显大于 0 = 这个"落点"其实是浮空的。
      - `hits_below`：把笠再压低 2mm，必须**立刻**穿模。
        否则说明扫描判据在"扫空气"，那个落点没有意义。
    """
    if not hat.is_watertight:
        raise RuntimeError(f"阵笠外壳不水密（{len(hat.faces)} 面）——"
                           f"落点判据不可信，先修几何再求解")

    body = trimesh.load(body_path, force="mesh")
    upper = body.vertices[body.vertices[:, 1] > 0.50]      # 半身以下离笠很远，省时间

    def at(y: float) -> trimesh.Trimesh:
        probe = hat.copy()
        probe.apply_translation((0.0, y, 0.0))
        return probe

    y = lo
    while y <= hi:
        if int(at(y).contains(upper).sum()) == 0:
            placed = at(y)
            _, dist, _ = trimesh.proximity.closest_point(placed, upper)
            hits_below = int(at(y - 0.002).contains(upper).sum())
            return {
                "rim_plane_y": round(y, 4),
                "upper_vertices_tested": int(len(upper)),
                "clearance_mm": round(float(dist.min()) * 1000.0, 2),
                "hits_below_mm2": hits_below,
            }
        y += step

    return None


# ══════════════════════════════════════════════════════════════════
def main() -> int:
    ap = argparse.ArgumentParser(description="程序化生成魔骸的小道具（阵笠 / 打刀）")
    ap.add_argument("--what", choices=["jingasa", "uchigatana", "both"], default="both")
    ap.add_argument("--out-dir", default="assets/models", help="输出目录（.glb）")
    ap.add_argument("--fit", metavar="本体.glb",
                    help="给定足兵本体的 .glb，求解阵笠的落点高度（只报数字、不写文件）")
    ap.add_argument("--report", help="把规格与落点写成 JSON")
    args = ap.parse_args()

    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    report: dict = {"jingasa_spec": {}, "uchigatana_spec": {}}

    if args.what in ("jingasa", "both"):
        hat = build_jingasa()
        log(f"阵笠  : 面 {len(hat.faces):,}  顶点 {len(hat.vertices):,}  "
            f"包围盒 {np.round(hat.extents, 3)}  水密 {hat.is_watertight}")
        log(f"        直径 {2 * JINGASA_R:.3f}m  高 {JINGASA_H:.3f}m  "
            f"缘厚 {JINGASA_SHELL:.3f}m  内腔深 {JINGASA_CAVITY_DEPTH:.3f}m  "
            f"倾角 ({JINGASA_TILT_X}°, {JINGASA_TILT_Z}°)")

        if args.fit:
            fit = fit_jingasa(hat, args.fit)
            if fit is None:
                log("落点  : ✗ 扫到上限仍在穿模——本体包围盒不对？")
            else:
                log(f"落点  : 笠缘平面 y = {fit['rim_plane_y']:.4f}   "
                    f"（判据：{fit['upper_vertices_tested']:,} 个上半身顶点全部落在笠外壳之外）")
                log(f"        接触距离 {fit['clearance_mm']:.2f}mm（≈0 才是「搭住」）；"
                    f"压低 2mm 立刻穿模 {fit['hits_below_mm2']} 处"
                    f"（对照组——这一项是 0 就说明判据在扫空气，落点不算数）")
                fit["body"] = os.path.basename(args.fit)
                report["jingasa_fit"] = fit

        out = os.path.join(out_dir, "jingasa_v1_clean.glb")
        hat.export(out, file_type="glb", include_normals=True)
        log(f"已写出: {out}  ({os.path.getsize(out):,} 字节)")
        report["jingasa_spec"] = {
            "radius": JINGASA_R, "height": JINGASA_H, "rim_thickness": JINGASA_SHELL,
            "cavity_depth": JINGASA_CAVITY_DEPTH,
            "faces": int(len(hat.faces)), "watertight": bool(hat.is_watertight),
            "tilt_deg": [JINGASA_TILT_X, JINGASA_TILT_Z],
        }

    if args.what in ("uchigatana", "both"):
        katana = build_uchigatana()
        ext = katana.extents
        log(f"打刀  : 面 {len(katana.faces):,}  顶点 {len(katana.vertices):,}  "
            f"包围盒 {np.round(ext, 3)}  水密 {katana.is_watertight}")
        log(f"        全长 {ext[1]:.3f}m（柄 {KATANA_TSUKA} + 鍔 {KATANA_TSUBA_T} "
            f"+ 刀身 {KATANA_BLADE}）反り {KATANA_SORI*1000:.0f}mm  "
            f"元幅 {KATANA_W0*1000:.0f}mm → 先幅 {KATANA_W1*1000:.0f}mm")

        out = os.path.join(out_dir, "uchigatana_v1_clean.glb")
        katana.export(out, file_type="glb", include_normals=True)
        log(f"已写出: {out}  ({os.path.getsize(out):,} 字节)")
        report["uchigatana_spec"] = {
            "total_length": float(ext[1]), "tsuka": KATANA_TSUKA,
            "blade": KATANA_BLADE, "faces": int(len(katana.faces)),
            "watertight": bool(katana.is_watertight),
        }

    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        log(f"规格已写出: {args.report}")

    log("下一步（顶点色）：")
    log("  python tools/paint_mesh.py assets/models/jingasa_v1_clean.glb "
        "--out assets/models/jingasa_v1_colored.glb --style jingasa")
    log("  python tools/paint_mesh.py assets/models/uchigatana_v1_clean.glb "
        "--out assets/models/uchigatana_v1_colored.glb --style katana --glow 1.4")
    return 0


if __name__ == "__main__":
    sys.exit(main())
