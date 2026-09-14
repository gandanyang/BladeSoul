#!/usr/bin/env python3
"""给一个（无骨架的）人形 glb 绑成 HumanoidAnimator 能驱动的骨架。

用法（无头 Blender）：
    blender.exe --background --factory-startup --python tools/rig_humanoid.py -- <profile>

    profile: player | ashigaru

与 tools/rig_character.py 的关系：那是玩家模型的**一次性脚本**（路径与 FOOT/TOP 硬编码）。
本脚本把它泛化成按 profile 选参数，因为 T49 要给魔骸足兵绑骨，而足兵的包围盒与姿态
跟玩家**不一样**（实测：玩家 y 跨度 1.88、手臂外伸 |x| 0.51；足兵 Z 跨度 1.70、外伸 0.680）。

为什么骨名不能改：src/Player/HumanoidAnimator.cs 的 Tracked 列表写死了 12 个名字
（Hip / Spine01 / Spine02 / Head / L_Upperarm / R_Upperarm / L_Forearm / R_Forearm /
L_Thigh / R_Thigh / L_Calf / R_Calf），名字对不上动画器就什么都不做。

三个已经踩过的坑（都写进了防御代码，别删）：
  1. 骨热扩散（ARMATURE_AUTO）在拓扑不干净的网格上会**静默失败**：顶点组建好了、
     权重却全空 → 导出器认为"没有顶点受骨影响"→ 干脆不写 skins。
     对策：数**权重总量**而不数组的个数；全空就回退 envelope。
  2. 「权重写了、skin 没写」：JOINTS_0/WEIGHTS_0 在、skins=0，Godot 侧退化成
     22 个普通节点、没有 Skeleton3D。对策：导出后**直接解析 glb 的 JSON chunk** 自检。
  3. 自检不能"重新导入再看修改器"——skins 缺失时重新导入本来就没有修改器，
     等于什么都没验到。
"""
import sys

import bpy
from mathutils import Vector

PROFILES = {
    # 玩家（T24/T25/T48 用的那套，参数与原 rig_character.py 完全一致，保持可复现）
    'player': {
        'src': r'G:\Game\assets\models\model_player_congyun_03.glb',
        'out': r'G:\Game\assets\models\model_player_congyun_03_rigged.glb',
        'rig_name': 'CongyunRig',
        'foot': -0.976,
        'top': 0.975,
        # 实测：手臂外伸 |x| ≈ 0.51
        'arm_x': (0.10, 0.135, 0.155),
        'leg_x': (0.055, 0.06, 0.065),
        'arm_z': (0.795, 0.66, 0.53),
        'leg_z': (0.53, 0.30, 0.06),
        'with_clips': True,
    },
    # 魔骸足兵（T49）：无骨静态网格，Blender 里 Z 是竖直轴，Z ∈ [-0.8660, 0.8338]
    'ashigaru': {
        'src': r'G:\Game\assets\models\ashigaru_v2d_colored.glb',
        'out': r'G:\Game\assets\models\ashigaru_rigged.glb',
        'rig_name': 'AshigaruRig',
        'foot': -0.8660,
        'top': 0.8338,
        # 高度分片实测（zfrac 从脚底起算）：
        #   肩线连着 0.72~0.93 → 肩 0.80；手臂外伸从 0.47 起
        #   沿 x 分箱的 z 中位：x0.30→0.813 / x0.42→0.776 / x0.50→0.692 /
        #                        x0.58→0.586 / x0.66→0.504  （手臂斜向下伸）
        'arm_x': (0.30, 0.46, 0.62),
        'leg_x': (0.10, 0.11, 0.115),
        'arm_z': (0.795, 0.63, 0.50),
        'leg_z': (0.53, 0.30, 0.06),
        # 足兵的"动作"由 Godot 侧的程序化动画器驱动，glb 里不需要样例 clip
        'with_clips': False,
    },
}

TRACKED = ['Hip', 'Spine01', 'Spine02', 'Head', 'L_Upperarm', 'R_Upperarm',
           'L_Forearm', 'R_Forearm', 'L_Thigh', 'R_Thigh', 'L_Calf', 'R_Calf']


def build_bones(foot, top, arm_x, leg_x, arm_z, leg_z):
    """按身高比例生成骨架。x 取正负两侧，y=0 为身体中轴。"""
    ax_sh, ax_up, ax_fore = arm_x
    lx_th, lx_ca, lx_ft = leg_x
    az_sh, az_up, az_fore = arm_z
    lz_hip, lz_knee, lz_ankle = leg_z
    H = top - foot

    def z(frac):
        return foot + H * frac

    def arm_zf(frac):
        return foot + H * frac

    out = [
        ('Hip',        (0.0, 0.0, z(0.53)),       (0.0, 0.0, z(0.62)),      None),
        ('Spine01',    (0.0, 0.0, z(0.62)),       (0.0, 0.0, z(0.72)),      'Hip'),
        ('Spine02',    (0.0, 0.0, z(0.72)),       (0.0, 0.0, z(0.82)),      'Spine01'),
        ('Neck',       (0.0, 0.0, z(0.82)),       (0.0, 0.0, z(0.86)),      'Spine02'),
        ('Head',       (0.0, 0.0, z(0.86)),       (0.0, 0.0, z(1.00)),      'Neck'),
    ]
    for side, sgn in (('L', 1.0), ('R', -1.0)):
        out += [
            (f'{side}_Shoulder', (sgn * ax_sh * 0.3, 0.0, arm_zf(az_sh)),
                                 (sgn * ax_sh, 0.0, arm_zf(az_sh)),        'Spine02'),
            (f'{side}_Upperarm', (sgn * ax_sh, 0.0, arm_zf(az_sh)),
                                 (sgn * ax_up, 0.0, arm_zf(az_up)),       f'{side}_Shoulder'),
            (f'{side}_Forearm',  (sgn * ax_up, 0.0, arm_zf(az_up)),
                                 (sgn * ax_fore, 0.0, arm_zf(az_fore)),   f'{side}_Upperarm'),
            (f'{side}_Hand',     (sgn * ax_fore, 0.0, arm_zf(az_fore)),
                                 (sgn * ax_fore, 0.0, arm_zf(az_fore) - 0.06), f'{side}_Forearm'),
            (f'{side}_Thigh',    (sgn * lx_th, 0.0, z(lz_hip)),
                                 (sgn * lx_ca, 0.0, z(lz_knee)),          'Hip'),
            (f'{side}_Calf',     (sgn * lx_ca, 0.0, z(lz_knee)),
                                 (sgn * lx_ft, 0.0, z(lz_ankle)),         f'{side}_Thigh'),
            (f'{side}_Foot',     (sgn * lx_ft, 0.0, z(lz_ankle)),
                                 (sgn * lx_ft, -0.08, foot),              f'{side}_Calf'),
            (f'{side}_Toe',      (sgn * lx_ft, -0.08, foot),
                                 (sgn * lx_ft, -0.16, foot),              f'{side}_Foot'),
        ]
    return out


def weight_total(m):
    return sum(g.weight for v in m.data.vertices for g in v.groups)


def main():
    argv = sys.argv
    name = argv[argv.index('--') + 1] if '--' in argv else 'ashigaru'
    if name not in PROFILES:
        print('PROFILE_UNKNOWN %s (可选: %s)' % (name, ', '.join(PROFILES)))
        sys.exit(2)
    cfg = PROFILES[name]
    SRC, OUT = cfg['src'], cfg['out']
    print('PROFILE %s' % name)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SRC)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if len(meshes) != 1:
        print('MESH_COUNT_WARN %d（预期 1；多网格要重新想权重策略）' % len(meshes))
    mesh = meshes[0]
    print('STAGE import ok, mesh=%s verts=%d polys=%d' % (
        mesh.name, len(mesh.data.vertices), len(mesh.data.polygons)))
    # docs/17 §118：不许改网格外形。记下进来时的面数，导出前再对一次。
    src_polys = len(mesh.data.polygons)

    BONES = build_bones(cfg['foot'], cfg['top'], cfg['arm_x'], cfg['leg_x'],
                        cfg['arm_z'], cfg['leg_z'])

    arm_data = bpy.data.armatures.new(cfg['rig_name'])
    rig = bpy.data.objects.new(cfg['rig_name'], arm_data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones
    for bname, h, t, parent in BONES:
        b = eb.new(bname)
        b.head, b.tail = Vector(h), Vector(t)
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    print('STAGE rig ok, bones=%d' % len(arm_data.bones))

    missing = [n for n in TRACKED if n not in arm_data.bones]
    print('MISSING_TRACKED', missing if missing else '无')
    if missing:
        print('SELFCHECK_FAIL')   # 名字对不上 = 动画器什么都不做，直接判失败
        sys.exit(3)

    def auto_weight(kind):
        bpy.ops.object.select_all(action='DESELECT')
        mesh.select_set(True)
        rig.select_set(True)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.parent_set(type=kind)

    try:
        auto_weight('ARMATURE_AUTO')
    except Exception as e:
        print('WEIGHTS_FAIL', e)
    tot = weight_total(mesh)
    print('STAGE weights(auto) groups=%d total=%.1f' % (len(mesh.vertex_groups), tot))
    # 坑 1：数权重总量，不数组的个数
    if tot < 1.0:
        print('STAGE 骨热失败：权重全空 -> 回退到 envelope')
        auto_weight('ARMATURE_ENVELOPE')
        print('STAGE weights(envelope) groups=%d total=%.1f' % (
            len(mesh.vertex_groups), weight_total(mesh)))

    # 坑 2：显式钉上 Armature 修改器与父子关系
    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        md = mesh.modifiers.new('Armature', 'ARMATURE')
        md.object = rig
        print('STAGE 补挂 Armature 修改器')
    if mesh.parent is not rig:
        mesh.parent = rig
        print('STAGE 修正父子关系')
    print('SOURCE_CHECK modifiers=%s parent=%s groups=%d polys=%d' % (
        [m.type for m in mesh.modifiers],
        mesh.parent.name if mesh.parent else None,
        len(mesh.vertex_groups), len(mesh.data.polygons)))
    if len(mesh.data.polygons) != src_polys:
        print('POLY_COUNT_CHANGED %d -> %d（docs/17 §118 不许改外形）' % (
            src_polys, len(mesh.data.polygons)))

    # 样例 clip：只在 profile 需要时生成（足兵的动作由 Godot 侧程序化驱动，不需要）
    if cfg['with_clips']:
        import math
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
            key('L_Thigh', f, (25 * s, 0, 0))
            key('R_Thigh', f, (-25 * s, 0, 0))
            key('L_Calf', f, (-30 * max(0, s), 0, 0))
            key('R_Calf', f, (-30 * max(0, -s), 0, 0))
            key('L_Upperarm', f, (-20 * s, 0, 0))
            key('R_Upperarm', f, (20 * s, 0, 0))
            key('Spine01', f, (2 * s, 0, 0))
        print('STAGE walk ok, keys=%d' % len(act.fcurves))

    for o in bpy.data.objects:
        o.hide_set(False)
        o.hide_viewport = False
        o.hide_render = False
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=False,
                              export_animations=cfg['with_clips'], export_skins=True,
                              export_apply=False)
    import os
    print('STAGE export ok ->', OUT, os.path.getsize(OUT), 'bytes')

    # ── 自检：直接解析导出后的 glb（权威判据，坑 3）──
    import json
    import struct
    b = open(OUT, 'rb').read()
    off, ok = 12, False
    while off < len(b):
        ln, ty = struct.unpack_from('<II', b, off)
        if ty == 0x4E4F534A:
            j = json.loads(b[off + 8:off + 8 + ln])
            sk = len(j.get('skins', []))
            with_skin = sum(1 for n in j.get('nodes', []) if 'skin' in n)
            joints = len(j['skins'][0]['joints']) if sk else 0
            names = [j['nodes'][n].get('name', '?') for n in (j['skins'][0]['joints'] if sk else [])]
            print('SELFCHECK skins=%d nodes_with_skin=%d joints=%d' % (sk, with_skin, joints))
            miss2 = [n for n in TRACKED if n not in names]
            print('SELFCHECK_TRACKED_MISSING', miss2 if miss2 else '无')
            ok = sk >= 1 and with_skin >= 1 and joints >= 12 and not miss2
            break
        off += 8 + ln
    print('SELFCHECK_OK' if ok else 'SELFCHECK_FAIL')
    sys.exit(0 if ok else 1)


main()
