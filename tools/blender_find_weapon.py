"""在主角模型里找出「刀」与「鞘」对应的连通块（T48 第一步：先认出，再切）。

    & 'C:\\Users\\Gdy\\Blender\\blender-4.5.13-windows-x64\\blender.exe' `
        --background --factory-startup --python tools/blender_find_weapon.py

为什么先做这一步：模型只有一个材质、没有独立节点，刀是**烘在身体网格里**的。
盲目切分 + 绑骨，错了比不绑更难查（会出现"刀跟着手飞出去"这种诡异现象）。
所以先按**连通块**把几何拆开，用"细长比"（最长边 / 最短边）把长条形物件挑出来，
再结合它在躯干上的位置判断哪一簇是刀、哪一簇是鞘。

输出：一张表（顶点数 / 尺寸 / 中心 / 细长比），按细长比从大到小。
"""

import bmesh
import bpy
from mathutils import Vector

GLB = r"G:\Game\assets\models\model_player_congyun_01.glb"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
print("[T48] 网格对象：", [(o.name, len(o.data.vertices)) for o in meshes])

islands = []

for obj in meshes:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    seen = set()
    for start in bm.verts:
        if start.index in seen:
            continue

        stack = [start]
        seen.add(start.index)
        comp = []

        while stack:
            v = stack.pop()
            comp.append(v)
            for edge in v.link_edges:
                other = edge.other_vert(v)
                if other.index not in seen:
                    seen.add(other.index)
                    stack.append(other)

        world = [obj.matrix_world @ v.co for v in comp]
        mn = Vector((min(c.x for c in world), min(c.y for c in world), min(c.z for c in world)))
        mx = Vector((max(c.x for c in world), max(c.y for c in world), max(c.z for c in world)))
        dims = sorted((mx - mn)[:])
        elong = dims[2] / max(dims[0], 1e-6)
        islands.append((len(comp), dims, (mn + mx) / 2.0, elong, obj.name))

    bm.free()

islands.sort(key=lambda x: -x[3])

# ★ 第一版直接把细长比排序打出来——前 14 名全是**退化薄片**
# （尺寸像 0.000 × 0.025 × 0.129：某个轴为 0，是碎边/残面），不是刀。
# 所以先滤掉"太小"和"某个轴退化"的连通块，剩下的才可能是真的长条物件。
MIN_VERTS = 20
MIN_DIM = 0.005

solid = [i for i in islands if i[0] >= MIN_VERTS and min(i[1]) >= MIN_DIM]

print(f"[T48] 过滤后（顶点≥{MIN_VERTS} 且最薄的一条边≥{MIN_DIM}）：{len(solid)} 个连通块")
print(f"[T48] {'#':>2} {'顶点':>7} {'尺寸(x,y,z)':>26} {'中心':>26} {'细长比':>7}  对象")

for i, (count, dims, center, elong, name) in enumerate(solid[:12]):
    d = "(" + ", ".join(f"{v:.3f}" for v in dims) + ")"
    c = "(" + ", ".join(f"{v:.3f}" for v in center) + ")"
    print(f"[T48] {i:>2} {count:>7} {d:>26} {c:>26} {elong:>7.1f}  {name}")

print(f"[T48] （未过滤时一共 {len(islands)} 个连通块——绝大多数是退化薄片）")

# 顺带把整个模型的范围打出来，好判断"中心"在身体的哪个部位。
allv = []
for obj in meshes:
    allv += [obj.matrix_world @ v.co for v in obj.data.vertices]

mn = Vector((min(v.x for v in allv), min(v.y for v in allv), min(v.z for v in allv)))
mx = Vector((max(v.x for v in allv), max(v.y for v in allv), max(v.z for v in allv)))
print("[T48] 整体范围 min", tuple(round(v, 3) for v in mn),
      "max", tuple(round(v, 3) for v in mx))

# 那个 Icosphere 到底是什么：它的范围与面数。
for obj in meshes:
    if obj.name.lower().startswith("ico"):
        pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
        if not pts:
            continue
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        print(f"[T48] ★ {obj.name}：{len(obj.data.polygons)} 面，"
              f"范围 {tuple(round(v, 3) for v in lo)} → {tuple(round(v, 3) for v in hi)}"
              f"（半径看着是 1——它把整体包围盒撑到了 ±1）")
