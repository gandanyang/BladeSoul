"""按"身体轮廓之外的那条细长件"选出刀/鞘，染色后渲染出来给人确认（T48 第三步）。

    & 'C:\\Users\\Gdy\\Blender\\blender-4.5.13-windows-x64\\blender.exe' `
        --background --factory-startup --python tools/blender_select_weapon.py

为什么要"先染色再给人看"：切分 + 绑骨**错了比不绑更难查**
（典型故障是"刀跟着手飞出去"）。而这个模型碎得厉害（1083 个连通块），
按区域选顶点很容易把袖口/裤脚的毛刺一起框进来。

选区怎么来的（**不是猜的，是量出来的**）：
* 主网格范围 X ±0.2（前后，模型面朝 -X）、**Y ±0.241（左右）**、Z 0→0.998（高度）；
* 从侧视图看，刀是一条从腰前上方斜穿到膝后方的细长件，**挂在身体侧面之外**——
  也就是说它的横向（Y）超出了躯干在那个高度上的粗细。
所以判据 = **横向离中面够远 + 在腰到膝的高度带里**。

输出：assets/references/_t48_weapon_pick.png（被选中的顶点染成红色）
"""

import os

import bpy
from mathutils import Vector

GLB = r"G:\Game\assets\models\model_player_congyun_01.glb"
OUT = r"G:\Game\assets\references\_t48_weapon_pick.png"

# 选区参数（按上面的实测值给，留了余量）
SIDE_MIN = 0.10      # |Y| 超过这个算"在身体侧面之外"
Z_LOW = 0.18         # 高度带下沿（膝）
Z_HIGH = 0.78        # 高度带上沿（腰以上一点，容下刀柄）

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

body = bpy.data.objects.get("player_congyun")
if body is None:
    mesh_objs = [o for o in bpy.data.objects if o.type == "MESH"]
    body = max(mesh_objs, key=lambda o: len(o.data.vertices))
    print("[T48] 没找到 player_congyun，改用最大网格：", body.name)

mesh = body.data
picked = []

for v in mesh.vertices:
    p = body.matrix_world @ v.co
    if abs(p.y) >= SIDE_MIN and Z_LOW <= p.z <= Z_HIGH:
        picked.append(v.index)

print(f"[T48] 选区 |Y|>={SIDE_MIN} 且 Z∈[{Z_LOW},{Z_HIGH}] → 选中 {len(picked)} / {len(mesh.vertices)} 个顶点")

if picked:
    pts = [body.matrix_world @ mesh.vertices[i].co for i in picked]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    print("[T48] 选中顶点范围", tuple(round(v, 3) for v in lo), "→", tuple(round(v, 3) for v in hi))
    print("[T48] 尺寸（x=前后 / y=左右 / z=高）",
          tuple(round(v, 3) for v in (hi - lo)))

# 染色：给选中的顶点新建一个红色材质槽（只在**面**上生效，所以按面取多数）。
red = bpy.data.materials.new("PICK")
red.use_nodes = True
red.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.9, 0.1, 0.1, 1.0)
mesh.materials.append(red)
red_slot = len(mesh.materials) - 1

pick_set = set(picked)
tagged = 0
for poly in mesh.polygons:
    if all(i in pick_set for i in poly.vertices):
        poly.material_index = red_slot
        tagged += 1

print(f"[T48] 染红 {tagged} 个面")

# 出一条侧视图（相机在 +X，看 -X —— 刀挂在这一侧最清楚）。
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
# ★ 必须显式指定"按材质上色"：Workbench 默认可能是单色模式，
# 那样**染了红也看不见**——第一次跑就是这么被自己骗过去的（图上一片灰，
# 我还以为选中的是身体，其实是颜色压根没参与渲染）。
scene.display.shading.color_type = "MATERIAL"
scene.render.resolution_x = 700
scene.render.resolution_y = 900

allpts = [body.matrix_world @ v.co for v in mesh.vertices]
lo = Vector((min(p.x for p in allpts), min(p.y for p in allpts), min(p.z for p in allpts)))
hi = Vector((max(p.x for p in allpts), max(p.y for p in allpts), max(p.z for p in allpts)))
center = (lo + hi) / 2.0
size = max(hi - lo)

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = size * 1.25
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

cam.location = center + Vector((size * 3.0, 0.0, 0.0))
cam.rotation_euler = (center - cam.location).normalized().to_track_quat("-Z", "Y").to_euler()

scene.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("[T48] 渲染完成：", OUT)

# ── 已知结论（第一次跑出来的，别再犯）────────────────────────────
# 判据 |Y| >= 0.10 太粗：64 个顶点里选中 2384 个（37%），
# 把整个躯干侧面都框进来了——因为躯干在那个高度本身就横向铺到 ±0.24。
#
# **技术真相**：刀的一部分**必然藏在本体内部**（它从腰带里穿过去），
# 几何上分不开。所以"纯脚本按区域切刀"这条路不可靠，需要：
#   ① 人在 Blender GUI 里手选（最快最准），或
#   ② 开 GUI + MCP，让 AI 边看边调选区（路线 B 的用途），或
#   ③ 换思路：不切网格，只把刀那段顶点**重新刷权重**到 Weapon_R
#      （但前提还是先认出那段顶点——同一道坎）。
