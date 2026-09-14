# -*- coding: utf-8 -*-
"""
主角模型 UV 展开 + 分区配色图生成器（headless Blender）。

用途：给 GPT-image 做贴图时，需要一张「参考图」告诉它哪块面积是哪个部位。
细线 UV layout 图 AI 认不出来，所以这里额外做一张**按部位填满颜色的 UV 分区图**
（每个部位一个纯色块，直接铺满 UV 岛），AI 一看就知道每个色块该画成什么。

产出：
  1. assets/references/uv_congyun_layout.png       —— 官方 UV layout（细线，给人看/备查）
  2. assets/references/uv_congyun_regionmap.png    —— 分区配色图 2048²（★给 AI 的参考图）
  3. assets/references/uv_congyun_regionmap_preview.png —— 1024² 预览（给 agent 自己看）
  4. assets/models/model_player_congyun_03_uv.glb  —— 带 UV 的模型副本（不覆盖 _rigged）

用法：
  blender --background --factory-startup --python tools/uv_protagonist.py
"""
import os
import sys
import math
import json

import bpy
import numpy as np

PROJ = r"G:\Game"
SRC = os.path.join(PROJ, "assets", "models", "model_player_congyun_03_rigged.glb")
REF_DIR = os.path.join(PROJ, "assets", "references")
OUT_LAYOUT = os.path.join(REF_DIR, "uv_congyun_layout.png")
OUT_ATLAS = os.path.join(REF_DIR, "uv_congyun_regionmap.png")
OUT_PREVIEW = os.path.join(REF_DIR, "uv_congyun_regionmap_preview.png")
OUT_GLB = os.path.join(PROJ, "assets", "models", "model_player_congyun_03_uv.glb")

ATLAS = 2048
MARGIN_PX = 3            # UV 岛之间的隔离带（像素），防止 AI 把两个部位画糊在一起
REGION_MARGIN = 0.004    # 每个部位单独展开时的缝边（UV 单位）

# ── 配色：直接抄 docs/09 §3 配色板，改这里前先去改文档 ──────────────────────
PALETTE = {
    "skin":     (0.725, 0.604, 0.502),   # #B99A80 皮肤
    "hair":     (0.106, 0.102, 0.110),   # #1B1A1C 头发
    "cloth_in": (0.788, 0.761, 0.706),   # #C9C2B4 内层中衣
    "cloth_out":(0.243, 0.290, 0.322),   # #3E4A52 外层长袍
    "leather":  (0.290, 0.243, 0.196),   # #4A3E32 皮革胸甲/护肩
    "pants":    (0.243, 0.216, 0.184),   # #3E372F 长裤
    "boots":    (0.200, 0.169, 0.141),   # #332B24 高筒皮靴
    "belt":     (0.541, 0.478, 0.306),   # #8A7A4E 腰带/布绦
    "metal":    (0.541, 0.522, 0.471),   # #8A8578 素铁扣环
    "steel":    (0.659, 0.690, 0.733),   # #A8B0BB 剑身剑脊
    "cape":     (0.184, 0.227, 0.251),   # #2F3A40 短斗篷
    "gauntlet": (0.141, 0.110, 0.180),   # #241C2E 笼手底（黑紫）
    "marker":   (1.000, 0.000, 1.000),   # 洋红 = 未分类（显眼，一眼能看出漏了什么）
}

# ── 分区规则：按身高比例分带（不写死绝对坐标，模型换尺寸也不用改） ──────────
# (名, z 下界比例, z 上界比例)   z=0 脚底, z=1 头顶
BANDS = [
    ("boots",     0.000, 0.145),
    ("pants",     0.145, 0.470),
    ("belt",      0.470, 0.545),
    ("cloth_out", 0.545, 0.700),
    ("leather",   0.700, 0.815),
    ("cloth_in",  0.815, 0.850),
    ("skin",      0.850, 0.905),
    ("hair",      0.905, 1.000),
]
# 手臂：z 落在这一段 且 横向偏离中轴超过阈值 → 手臂；再细分到笼手
ARM_Z = (0.500, 0.860)
ARM_X_RATIO = 0.42   # 相对半身宽
GAUNTLET_Z = (0.500, 0.700)   # 小臂段


def log(msg):
    print("[uv] " + str(msg))
    sys.stdout.flush()


def find_mesh():
    best = None
    for o in bpy.context.scene.objects:
        if o.type != 'MESH':
            continue
        if best is None or len(o.data.polygons) > len(best.data.polygons):
            best = o
    return best


def poly_regions(obj):
    """每个面属于哪个部位。返回 (regions, world_coords, bmin, bmax)。"""
    me = obj.data
    mw = obj.matrix_world
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    world = co @ np.array(mw.to_3x3()).T + np.array(mw.translation)
    bmin = world.min(axis=0)
    bmax = world.max(axis=0)
    regions = []
    for p in me.polygons:
        c = world[list(p.vertices)].mean(axis=0)
        regions.append(classify(c, bmin, bmax)[0])
    return regions, world, bmin, bmax


def unwrap(obj):
    """★ 按部位分开展开。

    为什么不用 smart_project 一把梭：智能展开只按法线夹角切，
    **同一个部位会被打散成上百个小岛铺满全图**——做出来的"分区图"是一张碎片海，
    AI 根本认不出"哪块是左小臂"（第一版实测就是这样）。
    按部位分组展开之后，每个部位是一整块连续岛，分区图才有意义。
    顺带满足 docs/09 §6 的硬要求：**笼手与左臂必须单独一套 UV**。
    """
    regions, world, bmin, bmax = poly_regions(obj)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')

    if obj.data.uv_layers.active is None:
        obj.data.uv_layers.new(name="UVMap")

    me = obj.data
    order = []
    for r in regions:
        if r not in order:
            order.append(r)

    import bmesh
    bm = bmesh.from_edit_mesh(me)
    bm.faces.ensure_lookup_table()
    idx = {f.index: f for f in bm.faces}

    for r in order:
        bm.select_flush(False)
        for f in bm.faces:
            f.select = False
        n = 0
        for i in range(len(regions)):
            if regions[i] == r:
                idx[i].select = True
                n += 1
        bmesh.update_edit_mesh(me)
        if n == 0:
            continue
        try:
            bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=REGION_MARGIN)
            log("  展开 %-10s 面数 %d" % (r, n))
        except RuntimeError as e:
            log("  展开 %s 失败(%s)，跳过" % (r, e))

    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(margin=0.004, rotate=True, scale=True)
    bpy.ops.object.mode_set(mode='OBJECT')


def classify(centroid, bmin, bmax):
    """三角形重心 → 部位名。返回 (region, 是不是左侧)。"""
    span = bmax - bmin
    if span[2] <= 1e-9:
        return "marker", False
    zn = (centroid[2] - bmin[2]) / span[2]
    half_x = max(abs(bmin[0]), abs(bmax[0])) or 1.0
    xn = centroid[0] / half_x
    is_left = centroid[0] > 0.0     # 具体哪边是左，跑完看日志再定

    if ARM_Z[0] <= zn <= ARM_Z[1] and abs(xn) >= ARM_X_RATIO:
        if is_left and GAUNTLET_Z[0] <= zn <= GAUNTLET_Z[1]:
            return "gauntlet", True
        return "skin", is_left

    for name, lo, hi in BANDS:
        if lo <= zn < hi:
            return name, is_left
    return "marker", is_left


def rasterize(obj, atlas=ATLAS):
    me = obj.data
    me.calc_loop_triangles()
    uvl = me.uv_layers.active
    if uvl is None:
        raise RuntimeError("没有 UV 层，展开可能失败了")
    uvdata = uvl.data
    mw = obj.matrix_world

    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    world = co @ np.array(mw.to_3x3()).T + np.array(mw.translation)
    bmin = world.min(axis=0)
    bmax = world.max(axis=0)
    log("bbox min=%s max=%s" % (np.round(bmin, 4), np.round(bmax, 4)))
    log("身高 = %.4f  半身宽 = %.4f" % (bmax[2] - bmin[2], max(abs(bmin[0]), abs(bmax[0]))))

    n = len(me.loop_triangles)
    tri_uv = np.empty((n, 3, 2), dtype=np.float64)
    tri_ci = np.empty((n, 3), dtype=np.int64)
    for i, t in enumerate(me.loop_triangles):
        for k in range(3):
            tri_uv[i, k] = uvdata[t.loops[k]].uv
        tri_ci[i] = t.vertices

    ctr = world[tri_ci].mean(axis=1)

    # 分类 → 每三角形一个颜色
    cols = np.zeros((n, 3), dtype=np.float64)
    counts = {}
    for i in range(n):
        name, _ = classify(ctr[i], bmin, bmax)
        cols[i] = PALETTE[name]
        counts[name] = counts.get(name, 0) + 1
    log("分区统计: " + json.dumps(counts, ensure_ascii=False, sort_keys=True))

    # 光栅化：重心坐标填充
    px = np.rint(tri_uv * (atlas - 1)).astype(np.int64)
    img = np.zeros((atlas, atlas, 3), dtype=np.float64)
    img[:, :, :] = 0.06                      # 底色：近黑（不是纯黑，避免 AI 把背景当素材）
    margin = MARGIN_PX

    for i in range(n):
        p = px[i]
        x0 = max(int(p[:, 0].min()) - margin, 0)
        x1 = min(int(p[:, 0].max()) + margin, atlas - 1)
        y0 = max(int(p[:, 1].min()) - margin, 0)
        y1 = min(int(p[:, 1].max()) + margin, atlas - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs = np.arange(x0, x1 + 1)
        ys = np.arange(y0, y1 + 1)
        gx, gy = np.meshgrid(xs, ys)
        ax, ay = float(p[0, 0]), float(p[0, 1])
        bx, by = float(p[1, 0]), float(p[1, 1])
        cx, cy = float(p[2, 0]), float(p[2, 1])
        d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(d) < 1e-12:
            continue
        l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / d
        l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / d
        l3 = 1.0 - l1 - l2
        tol = -margin / float(atlas)          # 允许的外扩量（对应 MARGIN_PX）
        hit = (l1 >= tol) & (l2 >= tol) & (l3 >= tol)
        if not hit.any():
            continue
        img[y0:y1 + 1, x0:x1 + 1][hit] = cols[i]
    return img, px


def draw_lines(img, px, atlas, color=(0.0, 0.0, 0.0), width=1):
    """把 UV 岛的边界线画到图上（后台模式没有 GPU，官方 export_layout 用不了）。"""
    n = px.shape[0]
    for i in range(n):
        for k in range(3):
            a = px[i, k].astype(np.float64)
            b = px[i, (k + 1) % 3].astype(np.float64)
            steps = int(np.abs(b - a).max()) + 1
            if steps < 2:
                continue
            t = np.linspace(0.0, 1.0, steps)
            xs = np.rint(a[0] + (b[0] - a[0]) * t).astype(np.int64)
            ys = np.rint(a[1] + (b[1] - a[1]) * t).astype(np.int64)
            for dx in range(-width + 1, width):
                for dy in range(-width + 1, width):
                    xx = np.clip(xs + dx, 0, atlas - 1)
                    yy = np.clip(ys + dy, 0, atlas - 1)
                    img[yy, xx] = color
    return img


def save_png(arr, path, size=None):
    h, w, _ = arr.shape
    img = bpy.data.images.new("tmp_atlas", width=w, height=h, alpha=False)
    flat = np.concatenate([arr, np.ones((h, w, 1))], axis=2).astype(np.float32)
    img.pixels.foreach_set(flat.ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    if size is not None and size != w:
        img.scale(size, size)
        img.filepath_raw = OUT_PREVIEW
        img.file_format = 'PNG'
        img.save()
    bpy.data.images.remove(img)
    log("写出 " + path)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SRC)

    obj = find_mesh()
    if obj is None:
        raise RuntimeError("glb 里没找到 mesh")
    log("mesh = %s  面数 = %d  顶点 = %d" % (obj.name, len(obj.data.polygons), len(obj.data.vertices)))
    log("已有 UV 层: %s  顶点组: %d" % ([l.name for l in obj.data.uv_layers], len(obj.vertex_groups)))

    unwrap(obj)
    log("UV 展开完成: %s" % [l.name for l in obj.data.uv_layers])

    atlas_img, px = rasterize(obj)
    covered = float((atlas_img.sum(axis=2) > 0.3).mean())
    log("UV 覆盖率 = %.1f%%（低于 70%% 说明打包没吃满，调 REGION_MARGIN / pack margin）" % (covered * 100.0))
    save_png(atlas_img, OUT_ATLAS, size=1024)
    save_png(draw_lines(atlas_img.copy(), px, ATLAS, width=1), OUT_LAYOUT)

    # 导出时把骨架一路带上，否则蒙皮会在导出这一步断掉（skin 丢失）
    bpy.ops.object.select_all(action='DESELECT')
    node = obj
    while node is not None:
        node.select_set(True)
        node = node.parent
    for c in obj.children:
        c.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=OUT_GLB, export_format='GLB',
        use_selection=True, export_skins=True, export_yup=True)
    log("写出 " + OUT_GLB)
    log("DONE")


main()
