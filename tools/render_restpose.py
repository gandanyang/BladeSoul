"""用 Blender 渲染模型的绑定姿态，做**交叉验证**。

为什么需要它：连续几轮的判断都建立在 Godot 的渲染上，而"头朝下 / 被翻折"这类
结论已经被反复推翻。换一个完全独立的渲染器（Blender 有自己的 glTF 导入器和
骨架求值器）出同一张图，如果两边一致，才能排除"是 Godot 侧的导入或渲染出错"。

    blender --background --factory-startup --python tools/render_restpose.py -- <glb> <out.png>

用法约定见 docs/17：资产脚本统一 `--background --factory-startup`。
"""
import math
import os
import sys

import bpy
from mathutils import Vector


def argv_after_dashes():
    if '--' in sys.argv:
        return sys.argv[sys.argv.index('--') + 1:]
    return []


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def main():
    args = argv_after_dashes()
    if len(args) < 2:
        print('usage: -- <glb> <out.png>')
        return 1
    glb, out = args[0], args[1]

    clear_scene()
    imported = import_glb(glb)
    print('imported: %s' % [o.name for o in imported])

    # ── 量一下绑定姿态的实际范围（Blender 的 Z 才是"上"） ──
    meshes = [o for o in imported if o.type == 'MESH']
    if not meshes:
        print('FAILED: 没有网格')
        return 1

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    total = 0
    for o in meshes:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            total += 1
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    print('顶点 %d 个' % total)
    print('绑定姿态包围盒  X %.3f..%.3f   Y %.3f..%.3f   Z %.3f..%.3f'
          % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    print('（Blender 的 Z 是"上"：头顶应该在 Z 最大处）')

    # ── 用材质名判定上下：和 Godot 侧的材质探针互相印证 ──
    zs = []
    for o in meshes:
        for v in o.data.vertices:
            zs.append((o.matrix_world @ v.co).z)
    zs.sort()
    if zs:
        zmin, zmax = zs[0], zs[-1]
        span = zmax - zmin
        print('Z 分位：1%%=%.3f  25%%=%.3f  50%%=%.3f  75%%=%.3f  99%%=%.3f'
              % (zs[int(len(zs) * 0.01)], zs[len(zs) // 4], zs[len(zs) // 2],
                 zs[len(zs) * 3 // 4], zs[int(len(zs) * 0.99)]))
        print('Z 跨度 %.3f' % span)

    # ── 相机：正前方水平，看向模型中心 ──
    cx = (lo.x + hi.x) / 2.0
    cy = (lo.y + hi.y) / 2.0
    cz = (lo.z + hi.z) / 2.0
    size = max(hi.x - lo.x, hi.z - lo.z)

    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size * 1.25
    cam = bpy.data.objects.new('cam', cam_data)
    bpy.context.scene.collection.objects.link(cam)
    # Blender 的 -Y 是"前"（glTF 导入后模型面向 -Y 或 +Y 取决于朝向）；
    # 这里从 +Y 侧看，能同时看到正面或背面，足以判断上下。
    cam.location = (cx, cy + max(size * 3.0, 4.0), cz)
    cam.rotation_euler = (math.radians(90.0), 0.0, math.radians(180.0))
    bpy.context.scene.camera = cam

    sun = bpy.data.lights.new('sun', type='SUN')
    sun.energy = 4.0
    sun_obj = bpy.data.objects.new('sun', sun)
    sun_obj.rotation_euler = (math.radians(55.0), 0.0, math.radians(35.0))
    bpy.context.scene.collection.objects.link(sun_obj)

    world = bpy.data.worlds.new('w')
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.12, 0.13, 0.15, 1.0)
    world.node_tree.nodes['Background'].inputs[1].default_value = 0.7

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in \
        [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] \
        else 'BLENDER_EEVEE'
    scene.render.resolution_x = 720
    scene.render.resolution_y = 900
    scene.render.film_transparent = False
    scene.render.filepath = out
    scene.render.image_settings.file_format = 'PNG'

    bpy.ops.render.render(write_still=True)
    print('wrote %s' % out)
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
