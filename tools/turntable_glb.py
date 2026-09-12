#!/usr/bin/env python3
"""把网格渲成一张**转台动图（GIF）**，用来看清 3D 模型。

为什么不用现成的查看器：Windows 自带的「3D 查看器」在新系统上默认不装，
Blender 本机也没有。而"我就想看一眼它长什么样"这件事，不该先花 300MB 去装软件。
所以这里直接出一张会转的 GIF——任何看图工具都能打开。

它和 preview_glb.py 的分工：
  preview_glb.py  三视图（正/侧/俯），用来判断比例、有没有破面
  turntable_glb.py 绕一圈，用来看清立体形状 —— 也就是"3D 浏览"想要的那种感觉

    python tools/turntable_glb.py <模型> --out 输出.gif [--frames 24] [--size 480]
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
import trimesh
from PIL import Image
from matplotlib.figure import Figure
from matplotlib.backends.backend_agg import FigureCanvasAgg
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

BASE_COLOR = np.array([0.80, 0.80, 0.83])   # 中性灰：判断形状时颜色只会干扰


def shade(mesh: trimesh.Trimesh, use_vertex_colors: bool = False) -> np.ndarray:
    """按法线做一次简单的平光，再用顶点色（本项目生成品只有顶点色，没有贴图）上色。"""
    normals = mesh.face_normals
    light = np.array([0.35, 0.55, 0.75])
    light = light / np.linalg.norm(light)
    lambert = np.clip(normals @ light, 0.0, 1.0)
    intensity = 0.30 + 0.70 * lambert

    base = None
    if use_vertex_colors:
        base = vertex_colors_or_none(mesh)

    if base is None:
        base = np.tile(BASE_COLOR, (len(mesh.faces), 1))

    return np.clip(base * intensity[:, None], 0.0, 1.0)


def vertex_colors_or_none(mesh: trimesh.Trimesh) -> np.ndarray | None:
    """顶点色——**默认不用**。

    Hunyuan3D 的产物实测是**一片平的 (102,102,102)**（std=0、唯一色 1 种），
    拿它上色只会得到"灰上加灰"。只有在确实有颜色信息时才值得开。
    """
    vc = getattr(mesh.visual, "vertex_colors", None)
    if vc is None:
        return None
    rgb = np.asarray(vc, dtype=float)[:, :3] / 255.0
    if float(np.unique(rgb, axis=0).shape[0]) <= 1:
        return None
    return rgb[mesh.faces].mean(axis=1)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--out", required=True)
    ap.add_argument("--frames", type=int, default=24, help="绕一圈的帧数")
    ap.add_argument("--size", type=int, default=480, help="画面边长（px）")
    ap.add_argument("--elev", type=float, default=12.0, help="相机俯角（度）")
    ap.add_argument("--vertex-colors", action="store_true",
                    help="用顶点色上色（本项目 Hunyuan3D 产物的顶点色是平的，默认不用）")
    args = ap.parse_args()

    if not os.path.isfile(args.model):
        print(f"✗ 找不到输入：{args.model}")
        return 2

    mesh = trimesh.load(args.model, force="mesh")
    print(f"载入: 面 {len(mesh.faces):,}  顶点 {len(mesh.vertices):,}  包围盒 {np.round(mesh.extents, 3)}")

    colors = shade(mesh, use_vertex_colors=args.vertex_colors)
    # ★ glTF/Godot 都是 Y-up，而 matplotlib 的 3D 视图是 Z-up。
    # 不换轴的话模型会**躺着**呈现（第一版就是这个毛病）。
    verts = mesh.vertices[:, [0, 2, 1]]
    triangles = verts[mesh.faces]

    center = verts.mean(axis=0)
    triangles = triangles - center                      # 绕自身中心转
    half = np.abs(verts - center).max(axis=0)           # 每根半轴的长度

    frames = []
    for i in range(args.frames):
        azimuth = 360.0 * i / args.frames
        fig = Figure(figsize=(args.size / 100, args.size / 100), dpi=100)
        fig.patch.set_facecolor("#1b1d21")
        canvas = FigureCanvasAgg(fig)
        ax = fig.add_subplot(111, projection="3d")
        ax.set_facecolor("#1b1d21")

        collection = Poly3DCollection(triangles, facecolors=colors, edgecolors="none")
        ax.add_collection3d(collection)

        # 按物体**真实半轴**设限，并按比例设 box aspect——
        # 塞进一个正方体（set_box_aspect((1,1,1))）会让瘦长的人形只占画面几个百分点。
        ax.set_xlim(-half[0] * 1.06, half[0] * 1.06)
        ax.set_ylim(-half[1] * 1.06, half[1] * 1.06)
        ax.set_zlim(-half[2] * 1.06, half[2] * 1.06)
        ax.set_box_aspect(tuple(half / half.max()))
        ax.set_proj_type("ortho")            # 正交：没有透近大远小，看比例更准
        ax.view_init(elev=args.elev, azim=azimuth)
        ax.set_axis_off()
        fig.tight_layout(pad=0)

        canvas.draw()
        buf = np.asarray(canvas.buffer_rgba())
        # ⚠️ 不要用 ADAPTIVE + 默认抖动：灰阶图会被抖成一片彩色噪点（踩过）。
        # 中位切分 + 关抖动对连续调的灰阶最稳。
        frame = Image.fromarray(buf).convert("RGB")
        frames.append(frame.quantize(colors=96, method=Image.MEDIANCUT, dither=Image.NONE))
        print(f"  帧 {i + 1}/{args.frames}  azim {azimuth:.0f}°", flush=True)

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    frames[0].save(args.out, save_all=True, append_images=frames[1:],
                   duration=int(1000 / 12), loop=0, optimize=True)
    print(f"已写出: {args.out}  ({os.path.getsize(args.out):,} 字节)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
