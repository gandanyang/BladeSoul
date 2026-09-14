#!/usr/bin/env python3
"""把一个（无骨架的）人形 glb 绑成 HumanoidAnimator 能驱动的骨架，并导出带样例动作的 glb。

用法（无头 Blender，不需要开界面）：
    blender.exe --background --factory-startup --python tools/rig_character.py

为什么是这套骨骼名：src/Player/HumanoidAnimator.cs 的 Tracked 列表写死了 12 个名字
（Hip / Spine01 / Spine02 / Head / L_Upperarm / R_Upperarm / L_Forearm / R_Forearm /
L_Thigh / R_Thigh / L_Calf / R_Calf），名字对不上动画器就什么都不做。

已知限制（产出是"能用的粗胚"，不是成品）：
  · 骨骼位置按身高比例算（脚底 -0.976 / 头顶 +0.975），没有贴合网格；
  · 自动权重在"四肢与躯干融成一坨"的 3D 生成网格上必然难看：袍摆会跟着腿摆、腋下会撕，
    要出货必须手绘权重（Blender weight paint）；
  · 只有 21 根骨，不是 09 §7 要求的 Mixamo 65 骨标准骨架；没加武器骨（T48）；
  · 产出的 Action（Walk/Attack）游戏目前读不到：HumanoidAnimator 是程序化写骨骼姿势，
    没有 AnimationPlayer 通路。这两个 clip 的定位是动作来源／参考，不是能直接播的动画。

glTF 导出会做 Z-up → Y-up 转换，Godot 侧不需要额外旋转。
"""
import bpy, math, os
from mathutils import Vector

SRC = r'G:\Game\assets\models\model_player_congyun_03.glb'
OUT = r'G:\Game\assets\models\model_player_congyun_03_rigged.glb'

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
print('STAGE import ok, mesh=%s verts=%d' % (mesh.name, len(mesh.data.vertices)))

FOOT, TOP = -0.976, 0.975          # 实测：模型原点在身体中部，脚底/头顶是这两个值
H = TOP - FOOT
def z(frac):
    return FOOT + H * frac

BONES = [
    ('Hip',        (0.0, 0.0, z(0.53)),    (0.0, 0.0, z(0.62)),    None),
    ('Spine01',    (0.0, 0.0, z(0.62)),    (0.0, 0.0, z(0.72)),    'Hip'),
    ('Spine02',    (0.0, 0.0, z(0.72)),    (0.0, 0.0, z(0.82)),    'Spine01'),
    ('Neck',       (0.0, 0.0, z(0.82)),    (0.0, 0.0, z(0.86)),    'Spine02'),
    ('Head',       (0.0, 0.0, z(0.86)),    (0.0, 0.0, z(1.00)),    'Neck'),
    ('L_Shoulder', (0.03, 0.0, z(0.80)),   (0.10, 0.0, z(0.795)),  'Spine02'),
    ('L_Upperarm', (0.10, 0.0, z(0.795)),  (0.135, 0.0, z(0.66)),  'L_Shoulder'),
    ('L_Forearm',  (0.135, 0.0, z(0.66)),  (0.155, 0.0, z(0.53)),  'L_Upperarm'),
    ('L_Hand',     (0.155, 0.0, z(0.53)),  (0.165, 0.0, z(0.47)),  'L_Forearm'),
    ('R_Shoulder', (-0.03, 0.0, z(0.80)),  (-0.10, 0.0, z(0.795)), 'Spine02'),
    ('R_Upperarm', (-0.10, 0.0, z(0.795)), (-0.135, 0.0, z(0.66)), 'R_Shoulder'),
    ('R_Forearm',  (-0.135, 0.0, z(0.66)), (-0.155, 0.0, z(0.53)), 'R_Upperarm'),
    ('R_Hand',     (-0.155, 0.0, z(0.53)), (-0.165, 0.0, z(0.47)), 'R_Forearm'),
    ('L_Thigh',    (0.055, 0.0, z(0.53)),  (0.06, 0.0, z(0.30)),   'Hip'),
    ('L_Calf',     (0.06, 0.0, z(0.30)),   (0.065, 0.0, z(0.06)),  'L_Thigh'),
    ('L_Foot',     (0.065, 0.0, z(0.06)),  (0.065, -0.08, FOOT),   'L_Calf'),
    ('L_Toe',      (0.065, -0.08, FOOT),   (0.065, -0.16, FOOT),   'L_Foot'),
    ('R_Thigh',    (-0.055, 0.0, z(0.53)), (-0.06, 0.0, z(0.30)),  'Hip'),
    ('R_Calf',     (-0.06, 0.0, z(0.30)),  (-0.065, 0.0, z(0.06)), 'R_Thigh'),
    ('R_Foot',     (-0.065, 0.0, z(0.06)), (-0.065, -0.08, FOOT),  'R_Calf'),
    ('R_Toe',      (-0.065, -0.08, FOOT),  (-0.065, -0.16, FOOT),  'R_Foot'),
]

arm_data = bpy.data.armatures.new('CongyunRig')
rig = bpy.data.objects.new('CongyunRig', arm_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
for name, h, t, parent in BONES:
    b = eb.new(name)
    b.head, b.tail = Vector(h), Vector(t)
    if parent:
        b.parent = eb[parent]
        b.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
print('STAGE rig ok, bones=%d' % len(arm_data.bones))

TRACKED = ['Hip', 'Spine01', 'Spine02', 'Head', 'L_Upperarm', 'R_Upperarm',
           'L_Forearm', 'R_Forearm', 'L_Thigh', 'R_Thigh', 'L_Calf', 'R_Calf']
missing = [n for n in TRACKED if n not in arm_data.bones]
print('MISSING_TRACKED', missing if missing else '无')

bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
def _weight_total(m):
    return sum(g.weight for v in m.data.vertices for g in v.groups)

def _auto_weight(kind):
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type=kind)

try:
    _auto_weight('ARMATURE_AUTO')
except Exception as e:
    print('WEIGHTS_FAIL', e)
print('STAGE weights(auto) groups=%d total=%.1f' % (len(mesh.vertex_groups), _weight_total(mesh)))

# ★ 骨热扩散（AUTO）在拓扑不干净的网格上会**静默失败**：顶点组建好了，权重却全空。
#   此时导出器认为"没有任何顶点受骨影响"，于是干脆不写 skins —— 这正是 _03 那次的真凶
#   （我只数了组的个数＝21 就以为好了，没数权重）。
if _weight_total(mesh) < 1.0:
    print('STAGE 骨热失败：权重全空 -> 回退到 envelope')
    _auto_weight('ARMATURE_ENVELOPE')
    print('STAGE weights(envelope) groups=%d total=%.1f' % (len(mesh.vertex_groups), _weight_total(mesh)))

# ★ 显式把 Armature 修改器与父子关系钉上，不只依赖 parent_set。
#   血的教训：_03_rigged 那次「权重写了、skin 没写」（JOINTS_0/WEIGHTS_0 在，skins=0），
#   Godot 侧会退化成 22 个普通节点、没有 Skeleton3D。补齐并**在源场景里**验证。
if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
    md = mesh.modifiers.new('Armature', 'ARMATURE')
    md.object = rig
    print('STAGE 补挂 Armature 修改器')
if mesh.parent is not rig:
    mesh.parent = rig
    print('STAGE 修正父子关系')
print('SOURCE_CHECK modifiers=%s parent=%s groups=%d' % (
    [m.type for m in mesh.modifiers],
    mesh.parent.name if mesh.parent else None,
    len(mesh.vertex_groups)))

rig.animation_data_create()

def key(bone, frame, rot):
    pb = rig.pose.bones[bone]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = [math.radians(a) for a in rot]
    pb.keyframe_insert('rotation_euler', frame=frame)

bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 30
act = bpy.data.actions.new('Walk')
rig.animation_data.action = act
for f, s in ((1, 0), (8, 1), (15, 0), (23, -1), (30, 0)):
    key('L_Thigh', f, (25 * s, 0, 0)); key('R_Thigh', f, (-25 * s, 0, 0))
    key('L_Calf', f, (-30 * max(0, s), 0, 0)); key('R_Calf', f, (-30 * max(0, -s), 0, 0))
    key('L_Upperarm', f, (-20 * s, 0, 0)); key('R_Upperarm', f, (20 * s, 0, 0))
    key('Spine01', f, (2 * s, 0, 0))
print('STAGE walk ok, keys=%d' % len(act.fcurves))

act2 = bpy.data.actions.new('Attack')
rig.animation_data.action = act2
for f, a in ((1, 0), (9, -120), (16, 80), (26, 0)):   # 26 帧 = light_01.tres
    key('R_Upperarm', f, (a, 0, 0)); key('R_Forearm', f, (a * 0.3, 0, 0))
    key('Spine01', f, (a * 0.08, 0, 0))
print('STAGE attack ok, keys=%d' % len(act2.fcurves))

for o in bpy.data.objects:
    o.hide_set(False); o.hide_viewport = False; o.hide_render = False
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=False,
                          export_animations=True, export_skins=True, export_apply=False)
print('STAGE export ok ->', OUT, os.path.getsize(OUT), 'bytes')


# ── 自检：直接解析导出后的 glb（权威判据）──
#   上一版自检写错了：它"重新导入后再看修改器"，而 skins 缺失时重新导入本来就不会有修改器，
#   等于什么都没验到。这里直接读 glb 的 JSON chunk。
import json, struct
b = open(OUT, 'rb').read()
off, ok = 12, False
while off < len(b):
    ln, ty = struct.unpack_from('<II', b, off)
    if ty == 0x4E4F534A:
        j = json.loads(b[off + 8:off + 8 + ln])
        sk = len(j.get('skins', []))
        with_skin = sum(1 for n in j.get('nodes', []) if 'skin' in n)
        print('SELFCHECK skins=%d nodes_with_skin=%d joints=%s'
              % (sk, with_skin, len(j['skins'][0]['joints']) if sk else 0))
        ok = sk >= 1 and with_skin >= 1
        break
    off += 8 + ln
print('SELFCHECK_OK' if ok else 'SELFCHECK_FAIL')
