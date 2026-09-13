"""把主角模型渲成正交三视图，用来**用眼睛定位**刀与鞘在哪（T48 第二步）。

    & 'C:\\Users\\Gdy\\Blender\\blender-4.5.13-windows-x64\\blender.exe' `
        --background --factory-startup --python tools/blender_render_views.py

为什么需要：连通块法认不出刀（主网格被切成 1083 块，刀和身体的其他部分**是连在一起的**）。
看不见就切不准，所以先出图。

输出：assets/references/_t48_model_{front,side}.png
"""

import math
import os

import bpy
from mathutils import Vector

GLB = r"G:\Game\assets\models\model_player_congyun_01.glb"
OUT_DIR = r"G:\Game\assets\references"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 700
scene.render.resolution_y = 900
scene.render.film_transparent = False

# 只画主网格：那个 80 面的单位球会把取景撑满，先排除掉再看身体。
meshes = [o for o in bpy.data.objects if o.type == "MESH" and not o.name.lower().startswith("ico")]

points = []
for obj in meshes:
    points += [obj.matrix_world @ v.co for v in obj.data.vertices]

lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
center = (lo + hi) / 2.0
size = max((hi - lo))

print("[T48] 主网格范围", tuple(round(v, 3) for v in lo), "→", tuple(round(v, 3) for v in hi))

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = size * 1.25
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(name: str, location: Vector) -> None:
    cam.location = location
    direction = (center - location).normalized()
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT_DIR, f"_t48_model_{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[T48] 渲染完成：{scene.render.filepath}")


d = size * 3.0
shoot("front", center + Vector((0.0, -d, 0.0)))    # 从 -Y 看过去（人物正面）
shoot("side", center + Vector((d, 0.0, 0.0)))      # 从 +X 看（人物右侧）
