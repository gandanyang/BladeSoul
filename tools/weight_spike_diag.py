#!/usr/bin/env python3
"""Weight spike diagnostic -- pure python glb reader, no Blender required.

Why: T48 handed off an unresolved problem -- auto weights make the mesh explode
when R_Upperarm rotates ~130 deg (attack amplitude).  Measured 1.561 m max vertex
displacement, ~1000 verts moving >0.2 m.  Before repairing weights we need to know
*which bone* throws *which vertex*, and where those verts live.

This reads the glb's own JOINTS_0 / WEIGHTS_0 (the authoritative skinning data the
GPU will use), applies a world-space rotation to one bone at a time and reports the
resulting displacement field.

Usage:
    python tools/weight_spike_diag.py [model.glb]
"""
import json
import math
import struct
import sys

DEFAULT_GLB = r'G:\Game\assets\models\model_player_congyun_03_textured.glb'

COMP = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2),
        5125: ('I', 4), 5126: ('f', 4)}
NCOMP = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def load_glb(path):
    raw = open(path, 'rb').read()
    assert raw[:4] == b'glTF', 'not a glb'
    ver, total = struct.unpack_from('<II', raw, 4)
    off = 12
    js, bin_ = None, None
    while off < len(raw):
        ln, ty = struct.unpack_from('<II', raw, off)
        chunk = raw[off + 8: off + 8 + ln]
        if ty == 0x4E4F534A:
            js = json.loads(chunk)
        elif ty == 0x004E4942:
            bin_ = chunk
        off += 8 + ln
    return js, bin_


def accessor(js, bin_, idx):
    """Return a flat list of numbers for accessor `idx`."""
    a = js['accessors'][idx]
    fmt, size = COMP[a['componentType']]
    n = NCOMP[a['type']]
    count = a['count']
    bv = js['bufferViews'][a['bufferView']]
    base = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride') or (size * n)
    out = []
    for i in range(count):
        o = base + i * stride
        vals = struct.unpack_from('<' + fmt * n, bin_, o)
        out.append(vals[0] if n == 1 else vals)
    return out


# ---------- tiny matrix helpers (column-major like glTF) ----------

def m_id():
    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]


def m_mul(a, b):
    """a * b (both column-major 4x4 lists)."""
    r = [0.0] * 16
    for c in range(4):
        for row in range(4):
            s = 0.0
            for k in range(4):
                s += a[k * 4 + row] * b[c * 4 + k]
            r[c * 4 + row] = s
    return r


def m_from_trs(node):
    if 'matrix' in node:
        return list(node['matrix'])
    t = node.get('translation', [0, 0, 0])
    q = node.get('rotation', [0, 0, 0, 1])
    s = node.get('scale', [1, 1, 1])
    x, y, z, w = q
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    m = [
        (1 - 2 * (yy + zz)) * s[0], (2 * (xy + wz)) * s[0], (2 * (xz - wy)) * s[0], 0.0,
        (2 * (xy - wz)) * s[1], (1 - 2 * (xx + zz)) * s[1], (2 * (yz + wx)) * s[1], 0.0,
        (2 * (xz + wy)) * s[2], (2 * (yz - wx)) * s[2], (1 - 2 * (xx + yy)) * s[2], 0.0,
        t[0], t[1], t[2], 1.0,
    ]
    return m


def m_apply(m, p):
    x, y, z = p
    return (m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14])


def m_rot_axis(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    x, y, z = axis
    n = math.sqrt(x * x + y * y + z * z)
    x, y, z = x / n, y / n, z / n
    C = 1 - c
    return [
        c + x * x * C, y * x * C + z * s, z * x * C - y * s, 0.0,
        x * y * C - z * s, c + y * y * C, z * y * C + x * s, 0.0,
        x * z * C + y * s, y * z * C - x * s, c + z * z * C, 0.0,
        0.0, 0.0, 0.0, 1.0,
    ]


def m_inv_rigid(m):
    """Inverse of a rotation+translation matrix."""
    r = [m[0], m[4], m[8], 0.0,
         m[1], m[5], m[9], 0.0,
         m[2], m[6], m[10], 0.0,
         0.0, 0.0, 0.0, 1.0]
    t = (m[12], m[13], m[14])
    it = (-(r[0] * t[0] + r[4] * t[1] + r[8] * t[2]),
          -(r[1] * t[0] + r[5] * t[1] + r[9] * t[2]),
          -(r[2] * t[0] + r[6] * t[1] + r[10] * t[2]))
    r[12], r[13], r[14] = it
    return r


# ---------- scene walks ----------

def world_matrices(js):
    """node index -> world matrix."""
    out = {}
    parent = {}
    for i, n in enumerate(js['nodes']):
        for c in n.get('children', []):
            parent[c] = i

    def walk(i):
        if i in out:
            return out[i]
        local = m_from_trs(js['nodes'][i])
        p = parent.get(i)
        out[i] = m_mul(walk(p), local) if p is not None else local
        return out[i]

    for i in range(len(js['nodes'])):
        walk(i)
    return out


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_GLB
    js, bin_ = load_glb(path)
    print('[file] %s' % path)

    wm = world_matrices(js)

    # ── skin ──
    skins = js.get('skins', [])
    assert len(skins) == 1, 'expected exactly 1 skin, got %d' % len(skins)
    skin = skins[0]
    joints = skin['joints']
    ibm = accessor(js, bin_, skin['inverseBindMatrices'])
    bone_name = {j: js['nodes'][j].get('name', 'node%d' % j) for j in joints}
    name_to_joint = {bone_name[j]: j for j in joints}
    print('[skin] joints=%d' % len(joints))

    # skeleton root world (for absolute pose math)
    skel_root = skin.get('skeleton')
    if skel_root is None:
        skel_root = skin['joints'][0]
        while True:
            up = [i for i, n in enumerate(js['nodes']) if skel_root in n.get('children', [])]
            if not up:
                break
            skel_root = up[0]
    root_w = wm[skel_root]
    root_w_inv = m_inv_rigid(root_w)
    print('[root] skeleton node=%s (%s)' % (skel_root, js['nodes'][skel_root].get('name')))

    # rest world matrix of every bone, in skeleton-root local space
    rest = {}
    for j in joints:
        rest[j] = m_mul(root_w_inv, wm[j])

    # glTF inverse-bind must invert that exact matrix, so we can double check
    err = 0.0
    for k, j in enumerate(joints):
        ib = ibm[k]
        prod = m_mul(rest[j], ib)
        for r in range(4):
            for c in range(4):
                want = 1.0 if r == c else 0.0
                err = max(err, abs(prod[c * 4 + r] - want))
    print('[verify] max |rest*IBM - I| = %.2e  (0 means our rest pose math matches the asset)'
          % err)

    # ── gather skinned geometry ──
    verts = []      # skeleton-root local rest positions
    jidx = []       # 4 joint node indices per vertex
    jw = []         # 4 weights
    src_of = []
    for ni, n in enumerate(js['nodes']):
        if 'mesh' not in n or 'skin' not in n:
            continue
        mesh = js['meshes'][n['mesh']]
        for pi, prim in enumerate(mesh['primitives']):
            at = prim['attributes']
            if 'JOINTS_0' not in at:
                continue
            pos = accessor(js, bin_, at['POSITION'])
            J = accessor(js, bin_, at['JOINTS_0'])
            W = accessor(js, bin_, at['WEIGHTS_0'])
            m = wm[ni]
            for k, p in enumerate(pos):
                verts.append(m_apply(m, p))
                jidx.append(J[k])
                jw.append(W[k])
                src_of.append((ni, pi))
    nv = len(verts)
    print('[mesh] skinned verts=%d' % nv)

    zs = [v[2] for v in verts]
    xs = [v[0] for v in verts]
    ys = [v[1] for v in verts]
    print('[bbox] x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f'
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    print('[frame] glTF Y-up: y=up, z=front(+Z is model front per docs/17), x=right(+X)')

    # ── weight sanity ──
    sums = [sum(w) for w in jw]
    bad_sum = sum(1 for s in sums if abs(s - 1.0) > 1e-3)
    counts = [sum(1 for x in w if x > 1e-6) for w in jw]
    hist = {}
    for c in counts:
        hist[c] = hist.get(c, 0) + 1
    print('[weights] sum!=1: %d   influence-count histogram: %s'
          % (bad_sum, dict(sorted(hist.items()))))

    # ── per-bone spike probe ──
    ROT_DEG = 130.0
    AXIS = (1.0, 0.0, 0.0)   # glTF/Godot X = pitch (the swing axis used by the animator)
    SPIKES = 0.20

    print('')
    print('[probe] rotate each bone %g deg about local X, measure vertex displacement'
          % ROT_DEG)
    print('%-14s %8s %8s %8s  %s' % ('bone', 'maxD(m)', '>0.2m', '>0.5m', 'top spike verts (x,y,z)'))

    report = []
    for j in joints:
        nm = bone_name[j]
        # posed world (in skeleton-root local space) = rot about the bone's own head
        head = (rest[j][12], rest[j][13], rest[j][14])
        R = m_rot_axis(AXIS, ROT_DEG)
        # M' = T(head) * R * T(-head) * M
        to_origin = m_id()
        to_origin[12], to_origin[13], to_origin[14] = -head[0], -head[1], -head[2]
        back = m_id()
        back[12], back[13], back[14] = head
        posed = m_mul(m_mul(back, R), m_mul(to_origin, rest[j]))

        # delta matrix in the vertex space: M' * IBM   (verts already in root space)
        ib = ibm[joints.index(j)]
        delta = m_mul(posed, ib)

        mx = 0.0
        n02 = 0
        n05 = 0
        worst = None
        for k in range(nv):
            w = 0.0
            for s in range(4):
                if jidx[k][s] == joints.index(j):
                    w = jw[k][s]
                    break
            if w <= 1e-6:
                continue
            p = verts[k]
            q = m_apply(delta, p)
            d = math.dist(p, q) * w
            if d > mx:
                mx = d
                worst = p
            if d > SPIKES:
                n02 += 1
            if d > 0.5:
                n05 += 1
        report.append((nm, mx, n02, n05, worst))
        print('%-14s %8.3f %8d %8d  %s'
              % (nm, mx, n02, n05,
                 '(%.2f,%.2f,%.2f)' % worst if worst else '-'))

    report.sort(key=lambda r: -r[2])
    print('')
    print('[worst bones by >0.2m vertex count]')
    for nm, mx, n02, n05, worst in report[:6]:
        print('  %-14s >0.2m=%4d  maxD=%.3f m' % (nm, n02, mx))


if __name__ == '__main__':
    main()
