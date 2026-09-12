#!/usr/bin/env python3
"""清理 3D 生成结果并把面数降到预算内（T35 流程的第 2.5 步）。

为什么需要它：Hunyuan3D 的 VoxelToMesh（surface net）在本项目的参数下会产出
**大量浮空碎渣**——实测第一次生成：660,972 面分散在 **214,826 个连通块**里，
最大的那块只占 26.5%，其余 21 万多块平均 2 面。

好消息是**本体是好的**（三视图里能清楚看出一只足轻：兜、肩甲、胴、草摺、腿），
只是碎渣把包围盒撑到了 1.96×1.96，而 10 §2.1 的预算只有 ≤12k 面。
所以流程是：**取最大连通块 → 降面到预算内**。

    python tools/cleanup_mesh.py <输入.glb> --out <输出.glb> [--faces 11500]
                                [--min-ratio 0.02] [--no-decimate]

`--min-ratio` 是"保留面数 ≥ 最大块这个比例"的块（默认只留最大的那一块）。
留下 0.02 是为了不误杀"刀/手臂"这类被切开的合法部件——但**默认值故意保守**：
宁可只留一块干净的本体，也不要为了保全部件把碎渣又带回来。
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
import trimesh


def log(msg: str) -> None:
    print(msg, flush=True)


def stats(mesh: trimesh.Trimesh) -> str:
    return (f"面 {len(mesh.faces):>8,}  顶点 {len(mesh.vertices):>8,}  "
            f"包围盒 {np.round(mesh.extents, 3)}  水密 {mesh.is_watertight}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--out", required=True)
    ap.add_argument("--faces", type=int, default=11500,
                    help="目标面数（10 §2.1 的 MVP 预算是 12k，默认留一点余量）")
    ap.add_argument("--min-ratio", type=float, default=0.0,
                    help="保留面数 ≥ 最大块 × 该比例的连通块；0 = 只留最大的那一块")
    ap.add_argument("--no-decimate", action="store_true", help="只清理，不降面（用来分步验证）")
    args = ap.parse_args()

    if not os.path.isfile(args.model):
        log(f"✗ 找不到输入：{args.model}")
        return 2

    mesh = trimesh.load(args.model, force="mesh")
    log(f"输入  : {stats(mesh)}")

    # ── 1) 去碎渣 ────────────────────────────────────────────────
    parts = mesh.split(only_watertight=False)
    if len(parts) > 1:
        counts = np.array([len(p.faces) for p in parts])
        biggest = int(counts.max())
        threshold = max(1, int(biggest * args.min_ratio)) if args.min_ratio > 0 else biggest
        keep = [p for p, c in zip(parts, counts) if c >= threshold]

        log(f"连通块: {len(parts):,} 个，最大 {biggest:,} 面，"
            f"阈值 {threshold:,} 面 → 保留 {len(keep)} 块")

        if len(keep) > 1:
            mesh = trimesh.util.concatenate(keep)
        else:
            mesh = keep[0]

        log(f"去碎渣: {stats(mesh)}")

        dropped = len(counts) - len(keep)
        if dropped:
            log(f"        丢掉 {dropped:,} 块碎渣")

    # ── 2) 降面 ─────────────────────────────────────────────────
    if not args.no_decimate and len(mesh.faces) > args.faces:
        before = len(mesh.faces)
        # 顶点色要跟着走：本项目的生成结果只有顶点色、没有 UV/贴图。
        mesh = mesh.simplify_quadric_decimation(face_count=args.faces)
        log(f"降面  : {before:,} → {len(mesh.faces):,} 面（目标 {args.faces:,}）")

        if len(mesh.faces) > args.faces * 1.2:
            log(f"⚠️  降不到目标：还剩 {len(mesh.faces):,} 面。"
                f"通常是连通块仍然很碎——调小 --min-ratio 或先看三视图确认本体是否完整")

    # ── 3) 补法线（**必须做**）────────────────────────────────────
    # Hunyuan3D 的 SaveGLB **只写 POSITION**——没有 NORMAL、没有 UV、没有顶点色
    # （用 glb 的 JSON chunk 核对过：primitive.attributes 只有 ["POSITION"]）。
    #
    # 后果不是"不好看"，是**根本没法着色**：Godot 导入后顶点格式是
    # `FormatVertex, FormatIndex`——没有 FormatNormal，引擎只能渲染成
    # **没有明暗的纯白块**（T35 验收 2 的同屏截图就是这么翻车的）。
    #
    # 而 trimesh 的 glb 导出是 `include_normals=None`：**只在缓存里已有法线时才写**。
    # 所以这里必须显式算一次，否则导出的文件依旧没有 NORMAL。
    mesh = mesh.copy()
    mesh.fix_normals()          # 统一面朝向——朝向不统一时，"法线"没有意义
    _ = mesh.vertex_normals     # 触发计算并进缓存，导出才带得上 NORMAL
    log("法线  : 已重算并写入（生成物原本没有 NORMAL，引擎里会渲染成纯白）")

    log(f"输出  : {stats(mesh)}")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    mesh.export(args.out, file_type="glb", include_normals=True)
    log(f"已写出: {args.out}  ({os.path.getsize(args.out):,} 字节)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
