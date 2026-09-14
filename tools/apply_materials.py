# -*- coding: utf-8 -*-
"""
把 6 张无缝材质贴到主角模型上，并按 docs/09 §7 分好材质槽，导出 + 出预览图。

三个关键决定：

1. **用盒子投影（box mapping）现做 UV，不用之前那张展开图。**
   展开图带自相交，贴图会糊成一团；而这些贴图本来就是**无缝平铺**的，
   所以每个面按"主法线轴"投到对应平面即可——不用切缝、不会自相交、颗粒方向天然正确。

2. **左右手靠骨骼判，不靠 X 正负猜。**
   `rig_character.py` 建的骨架里有 L_Hand / L_Forearm，
   所以"笼手在哪只手"是**读出来的**，不是猜的（docs/09 §5：笼手在左手，必须准）。

3. **材质槽按 docs/09 §7 分组**：Body / Hair / ClothInner / ClothOuter /
   Leather / Metal / Gauntlet / Belt —— 侵蚀三阶段靠换材质，不靠重建模型。

用法：
  blender --background --factory-startup --python tools/apply_materials.py
产出：
  assets/models/model_player_congyun_03_textured.glb
  assets/references/_t_tex_preview.png   （正面 + 背面正交预览）
"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

PROJ = r"G:\Game"
# 默认吃 **T48 切完剑的那一版**：它多了 Weapon_R / Scabbard 两根骨骼，
# 正好能把 Metal 与剑鞘填上（切剑之前这两块都混在袍子里）。
DEFAULT_SRC = os.path.join(PROJ, "assets", "models", "model_player_congyun_03_rigged_weapon.glb")
TEX = os.path.join(PROJ, "assets", "textures", "congyun")
OUT_GLB = os.path.join(PROJ, "assets", "models", "model_player_congyun_03_textured.glb")
OUT_PREVIEW = os.path.join(PROJ, "assets", "references", "_t_tex_preview.png")

TILE = 0.42        # 一张贴图覆盖多少米（调这个 = 调纹理颗粒大小）

# 6 张贴图 → 材质名（顺序与 tex_prompt_congyun.md 的 ①~⑥ 一致）
TEX_SLOTS = [
    ("cloth_in",  "ClothInner", 0.62, 0.0, 0.0),
    ("cloth_out", "ClothOuter", 0.70, 0.0, 0.0),
    ("leather",   "Leather",    0.55, 0.0, 0.0),
    ("metal",     "Metal",      0.38, 0.85, 0.0),
    ("gauntlet",  "Gauntlet",   0.34, 0.0, 0.8),
    ("belt",      "Belt",       0.72, 0.0, 0.0),
]
# 没有贴图、只用 docs/09 §3 纯色的槽
# Scabbard = 剑鞘「黑漆（磨损）」#241F1B；Eye 目前没有几何（脸是个团），先占位不建。
FLAT_SLOTS = {
    "Body": (0.725, 0.604, 0.502, 1.0),      # 皮肤
    "Hair": (0.106, 0.102, 0.110, 1.0),      # 头发
    "Scabbard": (0.141, 0.122, 0.106, 1.0),  # 剑鞘黑漆 #241F1B
}
# 骨骼权重 → 材质（T48 切剑后新增的两根）
BONE_SLOTS = {"Weapon_R": "Metal", "Scabbard": "Scabbard"}

# 身高比例分带（z=0 脚底, z=1 头顶）→ 材质名
BANDS = [
    ("Leather",   0.000, 0.145),   # 高筒皮靴
    ("ClothOuter",0.145, 0.470),   # 长裤（用外袍布）
    ("Belt",      0.470, 0.545),   # 腰带 / 布绦
    ("ClothOuter",0.545, 0.700),   # 外层长袍
    ("Leather",   0.700, 0.815),   # 皮革胸甲 / 右肩护肩
    ("ClothInner",0.815, 0.850),   # 交领中衣
    ("Body",      0.850, 0.905),   # 颈 / 脸
    ("Hair",      0.905, 1.000),   # 头发
]
ARM_Z = (0.500, 0.860)
ARM_X_RATIO = 0.42


def log(m):
    print("[mat] " + str(m))
    import sys
    sys.stdout.flush()


def find_mesh():
    return max([o for o in bpy.context.scene.objects if o.type == 'MESH'],
               key=lambda o: len(o.data.polygons))


def left_arm_side(obj):
    """用骨骼权重判断"哪一侧是左手"。返回 (x 符号, 左臂顶点占比)。

    ★ 不靠 X 正负猜：模型的朝向在导出/缩放的往返里经常翻，
    猜错就是把笼手画到右手上，而 docs/09 §5 说笼手在左手是硬设定。
    """
    me = obj.data
    idx = {vg.name: vg.index for vg in obj.vertex_groups}
    targets = [idx[n] for n in ("L_Hand", "L_Forearm") if n in idx]
    if not targets:
        return None, 0.0
    xs = []
    for v in me.vertices:
        w = sum(g.weight for g in v.groups if g.group in targets)
        if w > 0.35:
            xs.append(v.co.x)
    if not xs:
        return None, 0.0
    arr = np.array(xs)
    sign = 1.0 if arr.mean() > 0 else -1.0
    return sign, len(xs) / max(1, len(me.vertices))


def bone_weights(obj, names):
    """逐顶点：在给定骨骼组里的权重和。缺骨骼就返回全 0（不报错，因为切剑前没有）。"""
    me = obj.data
    idx = {vg.name: vg.index for vg in obj.vertex_groups}
    targets = [idx[n] for n in names if n in idx]
    w = np.zeros(len(me.vertices))
    if not targets:
        return w
    for v in me.vertices:
        w[v.index] = sum(g.weight for g in v.groups if g.group in targets)
    return w


def gauntlet_mask(obj):
    return bone_weights(obj, ("L_Hand", "L_Forearm"))


def region_of(centroid, bmin, bmax, glove_w):
    zn = (centroid[2] - bmin[2]) / max(1e-9, bmax[2] - bmin[2])
    if glove_w > 0.5:
        return "Gauntlet"
    # 左臂的骨骼管到哪算哪；剩下的手臂（右手臂、以及左臂肩以上）
    # 是**裸手臂**——docs/09 §5 只给了左手笼手，右臂没有护具。
    # 少了这一条，右小臂会被高度分带吃成布料（实测过）。
    half_x = max(abs(bmin[0]), abs(bmax[0])) or 1.0
    if ARM_Z[0] <= zn <= ARM_Z[1] and abs(centroid[0] / half_x) >= ARM_X_RATIO:
        return "Body"
    for name, lo, hi in BANDS:
        if lo <= zn < hi:
            if name == "Hair" or name == "Body":
                # 头部再按**前后**分：脸朝前，头发在顶与后脑。
                # 前 = -Y（正面机位在 -Y 方向）。不这样分的话整个头都是发色。
                y = centroid[1]
                if zn < 0.945 and y < bmin[1] + (bmax[1] - bmin[1]) * 0.45:
                    return "Body"
            return name
    return "Body"


def make_materials(obj):
    mats = {}
    for key, name, rough, metal, emit in TEX_SLOTS:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        path = os.path.join(TEX, "tex_%s.png" % key)
        if not os.path.isfile(path):
            raise RuntimeError("缺贴图：%s（先跑 tools/slice_material_sheet.py）" % path)
        img = bpy.data.images.load(path, check_existing=True)
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = img
        tex.location = (-400, 200)
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if emit > 0.0:
            # 沟槽里的紫光：把同一张贴图当自发光用——暗底几乎不发光，亮纹路发光
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = emit
        mats[name] = m
    for name, col in FLAT_SLOTS.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = col
        bsdf.inputs["Roughness"].default_value = 0.68
        # ★ 必须同时设视口颜色：没有贴图的材质，Workbench 显示的是 diffuse_color
        #   而不是 Principled 的 Base Color。不设的话预览里脸和头发是**纯白**的，
        #   会让人以为导出失败（第一版预览就是这样）。
        m.diffuse_color = col
        mats[name] = m
    return mats


def box_uv(obj, tile=TILE):
    """盒子投影：每个面按主法线轴投到对应平面，按世界尺寸 / tile 平铺。

    为什么不用展开图：那张图**带自相交**，贴上去会糊。而这些贴图是**无缝**的，
    盒子投影不需要切缝、不会自相交，颗粒方向还天然跟着表面走。
    """
    me = obj.data
    if me.uv_layers.active is None:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    mw = obj.matrix_world
    m3 = np.array(mw.to_3x3())
    tr = np.array(mw.translation)

    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    world = co.reshape(-1, 3) @ m3.T + tr

    axes = []
    for p in me.polygons:
        n = np.abs(np.array(p.normal) @ m3.T)
        axes.append(int(np.argmax(n)))

    n_axes = {0: 0, 1: 0, 2: 0}
    for i, p in enumerate(me.polygons):
        a = axes[i]
        n_axes[a] += 1
        for li in p.loop_indices:
            vi = me.loops[li].vertex_index
            x, y, z = world[vi]
            if a == 0:        # 法线朝 X → 投 YZ
                u, v = y, z
            elif a == 1:      # 法线朝 Y → 投 XZ
                u, v = x, z
            else:             # 法线朝 Z → 投 XY
                u, v = x, y
            uv[li].uv = (u / tile, v / tile)
    log("盒子投影: 法线朝 X %d 面 / Y %d 面 / Z %d 面，1 张贴图 = %.2f 米"
        % (n_axes[0], n_axes[1], n_axes[2], tile))


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    src = args[0] if args else DEFAULT_SRC
    if not os.path.isfile(src):
        raise RuntimeError("找不到输入模型：%s" % src)
    log("输入模型 %s" % os.path.basename(src))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    obj = find_mesh()
    log("mesh=%s 面=%d 顶点=%d" % (obj.name, len(obj.data.polygons), len(obj.data.vertices)))

    left_sign, ratio = left_arm_side(obj)
    log("左臂判定: x 符号=%s，左臂顶点占 %.1f%%（骨骼权重读出来的，不是猜的）"
        % (left_sign, ratio * 100))
    if left_sign is None:
        raise RuntimeError("读不到 L_Hand/L_Forearm 权重，无法确定左右")

    world = np.array([list(obj.matrix_world @ v.co) for v in obj.data.vertices])
    bmin, bmax = world.min(axis=0), world.max(axis=0)
    log("包围盒 %.3f x %.3f x %.3f，身高 %.3f" % (*bmax - bmin, bmax[2] - bmin[2]))

    mats = make_materials(obj)
    order = list(mats.keys())
    obj.data.materials.clear()
    for name in order:
        obj.data.materials.append(mats[name])
    slot = {n: i for i, n in enumerate(order)}

    counts = {}
    glove_w = gauntlet_mask(obj)
    glove_n = int((glove_w > 0.5).sum())
    if glove_n == 0:
        raise RuntimeError("骨架里读不到 L_Hand / L_Forearm 权重")
    log("笼手（L_Hand + L_Forearm 加权）顶点 %d / %d" % (glove_n, len(glove_w)))

    bone_w = {}
    for bone, mat in BONE_SLOTS.items():
        w = bone_weights(obj, (bone,))
        n = int((w > 0.5).sum())
        if n == 0:
            raise RuntimeError("★ 骨骼 %s 权重全空——切剑没生效，%s 槽会是空的" % (bone, mat))
        bone_w[mat] = w
        log("骨骼 %-10s → %-9s 顶点 %d" % (bone, mat, n))

    for p in obj.data.polygons:
        vs = list(p.vertices)
        r = None
        for mat, w in bone_w.items():           # 骨骼归属优先于几何分带
            if float(w[vs].mean()) > 0.5:
                r = mat
                break
        if r is None:
            c = world[vs].mean(axis=0)
            r = region_of(c, bmin, bmax, float(glove_w[vs].mean()))
        p.material_index = slot[r]
        counts[r] = counts.get(r, 0) + 1
    log("材质分配: " + ", ".join("%s=%d" % (k, v) for k, v in sorted(counts.items())))

    box_uv(obj)

    bpy.ops.object.select_all(action='DESELECT')
    node = obj
    while node is not None:
        node.select_set(True)
        node = node.parent
    for c in obj.children:
        c.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # ★ 打包图片再导出：不打包时 Blender 会在 glb 旁边甩出一堆
    #   `model_player_congyun_03_textured_tex_*.png` 孤儿文件（实测脏了 assets/models/），
    #   而 glb 本身其实已经把图内嵌了（走 bufferView），那些文件没人引用。
    bpy.ops.file.pack_all()
    bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format='GLB',
                              use_selection=True, export_skins=True,
                              export_image_format='AUTO')
    log("写出 %s (%.1f MB)" % (OUT_GLB, os.path.getsize(OUT_GLB) / 1e6))

    verify_export(OUT_GLB)
    render_preview(obj)


def verify_export(path):
    """导出后自检：**直接解析 glb 的 JSON chunk**，确认材质真的带上了贴图/颜色。

    ★ 教训来自 T35：只看 Blender 场景里的材质会骗自己——
    导出漏了贴图、或者 baseColorFactor 没写进去，场景里一切正常而 glb 是坏的。
    所以这里读的是**导出产物本身**，不是内存里的状态。
    """
    import json
    import struct
    b = open(path, 'rb').read()
    off = 12
    j = None
    while off < len(b):
        ln, ty = struct.unpack_from('<II', b, off)
        if ty == 0x4E4F534A:
            j = json.loads(b[off + 8:off + 8 + ln])
            break
        off += 8 + ln
    if j is None:
        raise RuntimeError("读不出 glb 的 JSON chunk")

    skins = len(j.get('skins', []))
    with_skin = sum(1 for n in j.get('nodes', []) if 'skin' in n)
    log("自检 skins=%d 带 skin 的节点=%d 材质=%d 图片=%d"
        % (skins, with_skin, len(j.get('materials', [])), len(j.get('images', []))))

    missing = []
    for m in j.get('materials', []):
        name = m.get('name', '?')
        pbr = m.get('pbrMetallicRoughness', {})
        has_tex = 'baseColorTexture' in pbr
        has_fac = 'baseColorFactor' in pbr
        emis = m.get('emissiveFactor')
        log("  %-11s 贴图=%-5s 颜色=%-5s emissive=%s"
            % (name, has_tex, has_fac, emis if emis else '-'))
        if not has_tex and not has_fac:
            missing.append(name)

    if skins != 1 or with_skin != 1:
        raise RuntimeError("★ 蒙皮丢了：skins=%d with_skin=%d" % (skins, with_skin))
    if missing:
        raise RuntimeError("★ 这些材质既没贴图也没颜色，导出后是纯白：%s" % missing)
    log("导出自检通过")


def render_preview(obj):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 640
    scene.render.resolution_y = 900
    scene.render.film_transparent = False
    sh = scene.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'          # ★ Workbench 直接显示贴图，不用打灯
    sh.show_shadows = False

    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = (lo + hi) / 2.0
    size = max(hi - lo)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size * 1.15
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    scene.render.image_settings.file_format = 'PNG'
    shots = [("front", (0, -1, 0)), ("back", (0, 1, 0))]
    files = []
    for tag, d in shots:
        dv = Vector(d).normalized()
        cam.location = center + dv * size * 3.0
        cam.rotation_euler = (math.radians(90), 0, 0 if d[1] < 0 else math.pi)
        p = os.path.join(os.path.dirname(OUT_PREVIEW), "_t_tex_%s.png" % tag)
        scene.render.filepath = p
        bpy.ops.render.render(write_still=True)
        files.append(p)
        log("预览 %s → %s" % (tag, p))

    # 拼成左右并排
    imgs = []
    for p in files:
        im = bpy.data.images.load(p, check_existing=False)
        w, h = im.size
        buf = np.empty(w * h * 4, dtype=np.float32)
        im.pixels.foreach_get(buf)
        imgs.append(np.flipud(buf.reshape(h, w, 4)[:, :, :3]).astype(np.float64))
        bpy.data.images.remove(im)
    h, w, _ = imgs[0].shape
    out = np.concatenate([imgs[0], np.ones((h, 12, 3)) * 0.1, imgs[1]], axis=1)
    H, W, _ = out.shape
    res = bpy.data.images.new("pv", width=W, height=H, alpha=False)
    flat = np.concatenate([np.flipud(out), np.ones((H, W, 1))], axis=2).astype(np.float32)
    res.pixels.foreach_set(flat.ravel())
    res.filepath_raw = OUT_PREVIEW
    res.file_format = 'PNG'
    res.save()
    for p in files:
        try:
            os.remove(p)
        except OSError:
            pass
    log("拼图 → %s" % OUT_PREVIEW)


main()
