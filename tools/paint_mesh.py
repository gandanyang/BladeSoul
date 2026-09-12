#!/usr/bin/env python3
"""按 10 §1 的魔骸配色给网格**程序化上色**（写顶点色，不需要 UV）。

为什么走顶点色：本机没有本地贴图管线（Hunyuan3D 的纹理/UV 节点全是腾讯云 API，
违反 T35"纯本地离线"），也没装 xatlas / Blender 做 UV 展开。
而**顶点色不需要 UV**——这个模型本来就有 6,260 个顶点的颜色通道（只是一片平灰），
按高度分层写进去即可，Godot 侧的 StandardMaterial3D 直接吃 COLOR_0。

配色全部来自 10 §1「魔骸通用设计语言」：
    主色   黑   #141014   （饱和度低、湿、无光泽）
    甲片   冷灰 #3A3E44   （"残破但仍在用"）
    皮肤   灰黑 #4A4850
    血红   #7A1418        （次要色）
    发光红 #C8323A        （眼窝与伤口，越强的敌人越亮）

⚠️ 这是 **MVP 的可读性方案**，不是最终资产：它给的是色块，不是花纹。
   要真正的"贴图"（绑绳、甲片纹理、破口），得先有 UV —— 那是另一张卡。

    python tools/paint_mesh.py <模型.glb> --out <输出.glb> [--glow 1.6]
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
import trimesh

# ── 10 §1 的配色（改这里就是改配色）──────────────────────────────
BLACK = (0x14, 0x10, 0x14)      # 主色
PLATE = (0x3A, 0x3E, 0x44)      # 甲片
SKIN = (0x4A, 0x48, 0x50)       # 皮肤（比甲片略暖、略亮）
BLOOD = (0x7A, 0x14, 0x18)      # 血红
GLOW = (0xC8, 0x32, 0x3A)       # 发光红（眼窝/伤口）


def log(m: str) -> None:
    print(m, flush=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--out", required=True)
    ap.add_argument("--glow", type=float, default=1.0,
                    help="发光红的强度倍率（10 §1：越强的敌人越亮）")
    args = ap.parse_args()

    if not os.path.isfile(args.model):
        log(f"✗ 找不到输入：{args.model}")
        return 2

    mesh = trimesh.load(args.model, force="mesh")
    v = mesh.vertices
    log(f"载入: 面 {len(mesh.faces):,}  顶点 {len(v):,}  包围盒 {np.round(mesh.extents, 3)}")

    # 归一化高度：0 = 脚底，1 = 最高点
    lo, hi = v[:, 1].min(), v[:, 1].max()
    t = (v[:, 1] - lo) / max(1e-6, hi - lo)

    colors = np.zeros((len(v), 4), dtype=np.uint8)
    colors[:, 3] = 255

    # 由下往上分层：腿 → 胴/甲 → 胸 → 头
    colors[t < 0.10] = (*BLACK, 255)                       # 脚
    colors[(t >= 0.10) & (t < 0.42)] = (*BLACK, 255)       # 腿 / 草摺
    colors[(t >= 0.42) & (t < 0.62)] = (*BLOOD, 255)       # 腰带一带：血红（次要色）
    colors[(t >= 0.62) & (t < 0.84)] = (*PLATE, 255)       # 胴甲
    colors[(t >= 0.84) & (t < 0.92)] = (*SKIN, 255)        # 颈肩
    colors[t >= 0.92] = (*SKIN, 255)                       # 头/兜

    # 眼窝/伤口：头部下方一圈窄带 → 发光红。
    # 做成**环带**而不是只涂正面，是为了从任何角度都能读出"里面透出红光"
    # （10 §1 的辨识线索），也免去猜模型朝向——没有 UV/部件信息时这是稳妥做法。
    glow_band = (t >= 0.90) & (t < 0.945)
    g = np.clip(np.array(GLOW, dtype=float) * args.glow, 0, 255).astype(np.uint8)
    colors[glow_band] = (*g, 255)

    mesh.visual.vertex_colors = colors

    used = np.unique(colors[:, :3], axis=0)
    log(f"上色: 写入 {len(v):,} 个顶点，用到 {len(used)} 种颜色")
    for name, c in [("黑 主色", BLACK), ("甲片", PLATE), ("皮肤", SKIN),
                    ("血红", BLOOD), ("发光红", GLOW)]:
        n = int(np.all(colors[:, :3] == np.array(c, dtype=np.uint8), axis=1).sum())
        if n:
            log(f"        {name:<8} {n:>6,} 个顶点  ({n/len(v)*100:4.1f}%)")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    mesh.export(args.out, file_type="glb")
    log(f"已写出: {args.out}  ({os.path.getsize(args.out):,} 字节)")
    log("提示：顶点色要生效，Godot 的材质必须开 vertex_color_use_as_albedo（glTF 的 COLOR_0 一般会自动接上）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
