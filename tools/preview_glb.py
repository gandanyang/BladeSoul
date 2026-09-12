#!/usr/bin/env python3
"""把 .glb/.gltf/.obj/.stl 渲染成三视图 PNG，用于人工检查 3D 生成结果。

为什么需要它：3D 生成通道（T35 Hunyuan3D / T36 Tripo）的产物是网格，
不看一眼就不知道好坏。本机没有可离屏渲染的 GL 环境（无 pyrender/pyglet），
所以用 matplotlib 做画家算法（按深度排序三角面 + 法线平光）来出轮廓图。
它不追求美观，只求能判断：比例对不对、有没有破面、是不是一坨。

用法：
    python tools/preview_glb.py <模型路径> [--out 输出png] [--size 660] [--min-faces 200]

⚠️ 用 `--min-faces` 是因为 **Hunyuan3D 这类模型的产物常被碎屑撑爆**：
它会把参考图里的发丝、轮廓光、背景残留也预测成网格。实测 `ashigaru_v2d_00001_.glb`
共 660,136 个三角面，却被切成 **214,826 个连通块**——主体只有 1 个（175k 面），
剩下全是 1~600 面的碎片。整张渲染出来是一团毛刺，**看不出主体到底好不好**。

⚠️ --size 是**单视图**边长，三视图并排后总宽 = 3 × size，所以它有个上限：
**总宽必须 ≤2048px**，否则 Codex 会把图片缩放并在工具输出后面插一条
`<image_resize_notice>`——那条消息会劈开并行工具调用的 call/output 配对，
让 provider 以 `invalid_request_error: No tool output found` 拒掉整轮请求
（2026-09-13 连续打死了两个会话）。660 × 3 = 1980px，安全。
"""

from __future__ import annotations

import argparse
import os
import sys

import matplotlib
matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
import trimesh
from matplotlib.collections import PolyCollection


def normalize(mesh: trimesh.Trimesh) -> trimesh.Trimesh:
    """把网格平移到原点并把最大边长缩放到 1，便于出图对比。"""
    m = mesh.copy()
    m.apply_translation(-m.bounds.mean(axis=0))
    ext = float(np.max(m.extents))
    if ext > 0:
        m.apply_scale(1.0 / ext)
    return m


def shade(tris: np.ndarray, normals: np.ndarray, light: np.ndarray) -> np.ndarray:
    lam = np.clip(normals @ light, 0.0, 1.0)
    base = 0.32 + 0.68 * lam
    return np.repeat(base[:, None], 3, axis=1)


def draw(ax, mesh: trimesh.Trimesh, view: int, label: str) -> None:
    tris_all = mesh.triangles  # (n, 3, 3)
    a = tris_all[:, 1] - tris_all[:, 0]
    b = tris_all[:, 2] - tris_all[:, 0]
    normals = np.cross(a, b)
    nlen = np.linalg.norm(normals, axis=1, keepdims=True)
    nlen[nlen == 0] = 1e-9
    normals = normals / nlen

    # 三个视角：0 正面 1 侧面 2 四分之三
    if view == 0:
        plane = (0, 1)
        depth_axis = 2
        light = np.array([0.0, 0.25, 0.97])
    elif view == 1:
        plane = (2, 1)
        depth_axis = 0
        light = np.array([0.0, 0.25, 0.97])
    else:
        plane = (0, 2)
        depth_axis = 1
        light = np.array([0.0, 0.25, 0.97])

    order = np.argsort(tris_all[:, :, depth_axis].mean(axis=1))
    polys = tris_all[order][:, :, :][:, :, plane][:, :, :]
    colors = shade(tris_all[order], normals[order], light)

    ax.add_collection(PolyCollection(polys[:, :, [0, 1]], facecolors=colors,
                                     edgecolors="none", antialiased=False))
    ax.set_aspect("equal")
    ax.autoscale_view()
    ax.margins(0.05)
    ax.set_title(label, fontsize=9)
    ax.set_xticks([])
    ax.set_yticks([])
    for s in ax.spines.values():
        s.set_color("#cccccc")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--out", default=None)
    ap.add_argument("--size", type=int, default=660)   # 三视图总宽 = 3×size，必须 ≤2048
    ap.add_argument("--min-faces", type=int, default=0,
                    help="只保留三角面数 ≥ N 的连通块（滤掉生成模型的碎屑/薄片；0＝全保留）")
    args = ap.parse_args()

    scene = trimesh.load(args.model, force="scene")
    if isinstance(scene, trimesh.Scene):
        # 必须走 dump()，否则场景节点上的 Y-up/Z-up 变换不会套用，
        # 出来的形状和实际导入引擎后的朝向不一致。
        mesh = scene.dump(concatenate=True)
        if not isinstance(mesh, trimesh.Trimesh) or not len(mesh.faces):
            print("网格为空", file=sys.stderr)
            return 1
    else:
        mesh = scene

    if args.min_faces > 0:
        parts = mesh.split(only_watertight=False)
        keep = [p for p in parts if len(p.faces) >= args.min_faces]
        if not keep:
            print(f"--min-faces={args.min_faces} 把全部 {len(parts)} 个连通块都滤掉了",
                  file=sys.stderr)
            return 1
        print(f"连通块 {len(parts)} → 保留 {len(keep)}，丢弃 {len(parts) - len(keep)} 个碎块"
              f"（丢弃面数 {len(mesh.faces) - sum(len(p.faces) for p in keep)}）")
        mesh = trimesh.util.concatenate(keep)

    print(f"顶点 {len(mesh.vertices)}  三角面 {len(mesh.faces)}")
    print(f"包围盒尺寸 {np.round(mesh.extents, 4).tolist()}")
    print(f"水密(watertight) {mesh.is_watertight}  体绕数一致 {mesh.is_winding_consistent}")
    try:
        print(f"体积 {mesh.volume:.5f}")
    except Exception:
        pass

    m = normalize(mesh)
    fig, axes = plt.subplots(1, 3, figsize=(args.size / 100 * 3, args.size / 100), dpi=100)
    # 标签按"投影到哪个平面"直说，不用视角名：Y-up 模型里 XY 就是正面，
    # 把它叫 "top" 会让人把一张正常的正面图误读成"模型是平的"（已经误读过一次）。
    # ⚠️ 必须是 ASCII——DejaVu Sans 没有中文字形，中文标题会渲染成一串方框。
    for i, (ax, label) in enumerate(zip(axes, ["front  X-Y", "side  Z-Y", "top    X-Z"])):
        draw(ax, m, i, label)
    fig.tight_layout()

    out = args.out or os.path.splitext(args.model)[0] + "_preview.png"
    fig.savefig(out, facecolor="white")
    plt.close(fig)
    print("预览图:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
