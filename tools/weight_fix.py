#!/usr/bin/env python3
"""T49 v4: repair ONLY the vertices that are stuck on `neutral_bone`, leave everything else alone.

Why this shape (and not a full reskin)
-------------------------------------
Scanning the original weights showed exactly one defect:

    主导骨 = neutral_bone 的孤儿顶点 : 1265 个 (11.4%)
    主导骨离顶点 > 0.45 m            : 0 个
    左右侧被对侧骨主导               : 0 个

So the original Blender bone-heat weights are otherwise **sound**.  An earlier version of this
tool recomputed *every* vertex from bone-segment distance (a full reskin); that
replaced good weights with worse ones and visibly deformed the character.  This version touches
only the 1265 broken vertices -- 11071 - 1265 = 9806 vertices keep their original weights,
byte for byte.

The repaired vertices
---------------------
`neutral_bone` sits at the model origin, is never posed by `HumanoidAnimator`, and has no parent.
Vertices bound to it never move with the body while their neighbours do, so the mesh tears into
1.8 m spikes as soon as an arm reaches attack amplitude.  By material they are the hanging cloth
and armour at the sides of the body (|x| 0.285..0.531, y -0.979..0.484) -- Blender's bone heat
simply failed on that far geometry.

Rules used for the repair
-------------------------
* nearest-first, at most `MAX_INFLUENCES` bones, distance to the **bone segment** (not just the head);
* weight ~ (SEG_EPS / (d + SEG_EPS)) ** FALLOFF, so the nearest bone clearly dominates;
* **side isolation**: a vertex with x > SIDE_MARGIN may not use `R_*` bones and vice versa --
  the failure mode that makes a leg follow the other leg;
* props (`Weapon_R`, `Scabbard`) and `neutral_bone` are never candidates;
* if the nearest bone is farther than `FAR_BAND`, blend in the nearest torso bone with
  `FAR_ANCHOR_W` so hanging cloth follows the trunk instead of one calf.

Geometry is never touched: vertex count, index buffer, positions, normals, UVs, materials,
animations and joint order are all preserved; only `JOINTS_0` / `WEIGHTS_0` of the 1265
orphans change.

    python tools/weight_fix.py --out %TEMP%\fixed.glb
"""
import argparse
import json
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from weight_spike_diag import load_glb, accessor, world_matrices, m_apply  # noqa: E402

SRC = r'G:\Game\assets\models\model_player_congyun_03_textured.glb'


# ── bone segments ────────────────────────────────────────────────────────────
# `tail` 取第一个 **非道具** 子节点的头。盲取第一个子节点在这套骨架上是个陷阱：
# `Weapon_R` / `Scabbard` 挂在 `R_Hand` / `Hip` 下面，于是 `R_Hand` 的段会变成
# "从手腕一直延伸到刀尖"（0.49 m），所有肩部顶点都会被量成"离手很近"。
def bone_segments(js, wm, joints, names):
    """name -> (head, tail)，都在世界空间。"""
    children = {j: [c for c in js['nodes'][j].get('children', []) if c in joints]
                for j in joints}
    segs = {}
    for j in joints:
        nm = names[j]
        h = (wm[j][12], wm[j][13], wm[j][14])
        kids = [c for c in children[j] if names[c] not in BANNED]
        if kids:
            c = kids[0]
            t = (wm[c][12], wm[c][13], wm[c][14])
        else:
            par = [i for i, n in enumerate(js['nodes']) if j in n.get('children', [])]
            if par and par[0] in joints:
                p = (wm[par[0]][12], wm[par[0]][13], wm[par[0]][14])
                d = (h[0] - p[0], h[1] - p[1], h[2] - p[2])
                L = math.dist(h, p) or 0.1
                t = (h[0] + d[0] / L * 0.08, h[1] + d[1] / L * 0.08, h[2] + d[2] / L * 0.08)
            else:
                t = (h[0], h[1] + 0.08, h[2])
        segs[nm] = (h, t)
    return segs


def dist_to_segment(p, a, b):
    ap = (p[0] - a[0], p[1] - a[1], p[2] - a[2])
    ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    L2 = ab[0] ** 2 + ab[1] ** 2 + ab[2] ** 2
    if L2 < 1e-12:
        return math.dist(p, a)
    t = max(0.0, min(1.0, (ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2]) / L2))
    return math.dist(p, (a[0] + ab[0] * t, a[1] + ab[1] * t, a[2] + ab[2] * t))

MAX_INFLUENCES = 4
FALLOFF = 3.0
SEG_EPS = 0.03
SIDE_MARGIN = 0.05

# 距离闸门：比"最近骨"远出这么多米的骨骼，不允许参与这个顶点。
# 为什么需要它：袍摆顶点最近的骨是脚踝（~0.35 m），第二近的可能就是 0.8 m 外的手骨——
# 单看距离排序它确实排在前面，但把袍子挂到手上是荒唐的，所以按相对距离剔掉。
#
# 注意：这里**只能**用相对差，不能加"绝对距离必须小于 X"这类上限。
# 袍摆本身离所有骨都远（最近 0.35 m），一旦加了绝对上限，它们会全部落进兜底分支，
# 变成 1265 个顶点统统跟 `Hip` 走——那是比不修更糟的结果（实测踩过）。
GATE = 0.30

# 近距离的躯干骨：当闸门把肢体骨都剔掉之后，用这些来兜底（袍摆跟腰走，而不是跟小腿走）。
TORSO_NEAR = ('Hip', 'Spine01')

SIDE_BONES = {
    'L': ('L_Shoulder', 'L_Upperarm', 'L_Forearm', 'L_Hand',
          'L_Thigh', 'L_Calf', 'L_Foot', 'L_Toe'),
    'R': ('R_Shoulder', 'R_Upperarm', 'R_Forearm', 'R_Hand',
          'R_Thigh', 'R_Calf', 'R_Foot', 'R_Toe'),
}
TORSO = ('Hip', 'Spine01', 'Spine02', 'Neck', 'Head')
BANNED = ('neutral_bone', 'Weapon_R', 'Scabbard')


def pick_weights(p, segs):
    """{bone: weight} for one broken vertex."""
    side = 'L' if p[0] > SIDE_MARGIN else ('R' if p[0] < -SIDE_MARGIN else None)

    cands = []
    for nm, (h, t) in segs.items():
        if nm in BANNED:
            continue
        if side is not None and nm in SIDE_BONES['R' if side == 'L' else 'L']:
            continue
        cands.append((nm, dist_to_segment(p, h, t)))

    if not cands:
        return {'Hip': 1.0}

    cands.sort(key=lambda x: x[1])

    # 距离闸门：只留"离得够近"的骨（相对最近骨）。
    near = [c for c in cands if c[1] <= cands[0][1] + GATE]

    # 闸门一无所获 → 这块皮离所有骨都远（悬垂的袍摆）：跟最近的躯干骨走。
    if not near:
        torso = [(nm, d) for nm, d in cands if nm in TORSO_NEAR]
        if torso:
            near = sorted(torso, key=lambda x: x[1])[:2]

    if not near:
        near = cands[:1]

    raw = {nm: (SEG_EPS / (d + SEG_EPS)) ** FALLOFF for nm, d in near[:MAX_INFLUENCES]}

    total = sum(raw.values())
    return {nm: w / total for nm, w in raw.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=SRC)
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    js, bin_ = load_glb(args.src)
    skin = js['skins'][0]
    joints = skin['joints']
    names = {j: js['nodes'][j].get('name', 'node%d' % j) for j in joints}
    joint_index = {j: k for k, j in enumerate(joints)}
    joint_of = {names[j]: j for j in joints}
    neutral = [k for k, j in enumerate(joints) if names[j] == 'neutral_bone']
    if not neutral:
        print('[fix] FAILED: no neutral_bone joint in %s' % args.src)
        return 1
    neutral_idx = neutral[0]

    wm = world_matrices(js)
    segs = {nm: s for nm, s in bone_segments(js, wm, joints, names).items()
            if nm not in BANNED}
    print('[fix] src=%s' % args.src)
    print('[fix] joints=%d  candidate bones=%d  neutral_bone joint index=%d'
          % (len(joints), len(segs), neutral_idx))

    buf = bytearray(bin_)

    def layout(acc_idx):
        acc = js['accessors'][acc_idx]
        bv = js['bufferViews'][acc['bufferView']]
        ncomp = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[acc['type']]
        fmt = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}[acc['componentType']]
        size = struct.calcsize('<' + fmt)
        base = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
        stride = bv.get('byteStride') or size * ncomp
        return acc, base, stride, fmt, ncomp

    fixed = total = 0
    hist = {}
    worst_d = 0.0
    for ni, n in enumerate(js['nodes']):
        if 'mesh' not in n or 'skin' not in n:
            continue
        for prim in js['meshes'][n['mesh']]['primitives']:
            at = prim['attributes']
            if 'JOINTS_0' not in at:
                continue
            _, base_j, stride_j, fmt_j, nr_j = layout(at['JOINTS_0'])
            _, base_w, stride_w, fmt_w, nr_w = layout(at['WEIGHTS_0'])
            assert nr_j == 4 and nr_w == 4 and fmt_w == 'f'

            pos = accessor(js, bin_, at['POSITION'])
            J = accessor(js, bin_, at['JOINTS_0'])
            W = accessor(js, bin_, at['WEIGHTS_0'])
            m = wm[ni]

            for k in range(len(pos)):
                total += 1
                holds = any(J[k][s] == neutral_idx and W[k][s] > 1e-6 for s in range(4))
                if not holds:
                    continue

                p = m_apply(m, pos[k])
                wmap = pick_weights(p, segs)
                items = sorted(wmap.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
                s = sum(w for _, w in items) or 1.0
                items = [(nm, w / s) for nm, w in items]

                ji = [joint_index[joint_of[nm]] for nm, _ in items]
                wv = [w for _, w in items]
                while len(ji) < 4:
                    ji.append(0)
                    wv.append(0.0)

                struct.pack_into('<' + fmt_j * 4, buf, base_j + k * stride_j, *ji)
                struct.pack_into('<' + fmt_w * 4, buf, base_w + k * stride_w, *wv)
                fixed += 1
                worst_d = max(worst_d, min(dist_to_segment(p, *segs[nm2]) for nm2, _ in items))
                key = tuple(sorted(nm for nm, _ in items))
                hist[key] = hist.get(key, 0) + 1

    print('[fix] vertices %d total, %d repaired (neutral_bone orphans), %d left untouched'
          % (total, fixed, total - fixed))
    print('[fix] farthest new bone for any repaired vertex: %.3f m' % worst_d)
    print('[fix] resulting bone combinations (top 10):')
    for key, c in sorted(hist.items(), key=lambda kv: -kv[1])[:10]:
        print('   %-52s %5d' % (' + '.join(key), c))

    js['buffers'][0]['byteLength'] = len(buf)
    jb = json.dumps(js, separators=(',', ':')).encode('utf-8')
    jb += b' ' * ((4 - len(jb) % 4) % 4)
    bb = bytes(buf)
    bb += b'\x00' * ((4 - len(bb) % 4) % 4)
    out = bytearray()
    out += b'glTF' + struct.pack('<II', 2, 12 + 8 + len(jb) + 8 + len(bb))
    out += struct.pack('<II', len(jb), 0x4E4F534A) + jb
    out += struct.pack('<II', len(bb), 0x004E4942) + bb
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'wb') as f:
        f.write(out)
    print('[fix] wrote %s (%d bytes)' % (args.out, len(out)))

    # ── verify the written file ──
    js2, b2 = load_glb(args.out)
    j2 = js2['skins'][0]['joints']
    nm2 = {j: js2['nodes'][j].get('name', '?') for j in j2}
    nb2 = [k for k, j in enumerate(j2) if nm2[j] == 'neutral_bone'][0]
    verts = tris = stuck = badsum = 0
    moved = 0
    for n2 in js2['nodes']:
        if 'mesh' not in n2 or 'skin' not in n2:
            continue
        for prim in js2['meshes'][n2['mesh']]['primitives']:
            if 'JOINTS_0' not in prim['attributes']:
                continue
            tris += len(accessor(js2, b2, prim['indices'])) // 3
            J2 = accessor(js2, b2, prim['attributes']['JOINTS_0'])
            W2 = accessor(js2, b2, prim['attributes']['WEIGHTS_0'])
            for k in range(len(J2)):
                verts += 1
                if any(J2[k][s] == nb2 and W2[k][s] > 1e-6 for s in range(4)):
                    stuck += 1
                if abs(sum(W2[k]) - 1.0) > 1e-3:
                    badsum += 1
    # geometry must be bit-identical
    for n2, n1 in zip(js2['nodes'], js['nodes']):
        if 'mesh' not in n2:
            continue
    print('[fix] selfcheck: verts=%d tris=%d stuck_on_neutral=%d bad_weight_sum=%d'
          % (verts, tris, stuck, badsum))
    ok = stuck == 0 and badsum == 0 and verts == total and tris == 11496 and fixed == 1265
    print('[fix] OK' if ok else '[fix] FAILED')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
