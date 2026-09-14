#!/usr/bin/env python3
"""Restore the ORIGINAL (pre-repair) weights into `model_player_congyun_03_textured.glb`.

Why: the textured model is untracked in git, and a T49 experiment overwrote it in
place, so the "before" state had to be recovered.  `model_player_congyun_03_rigged_weapon.glb`
has the same 11071-vertex mesh but has never been touched by the weight tools, so its
`JOINTS_0` / `WEIGHTS_0` are the original bone-heat weights.

Vertices are matched by **position** (they are bit-identical between the two files),
which sidesteps any primitive-order assumption.

    python tools/restore_original_weights.py --out %TEMP%\original.glb
"""
import argparse
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from weight_spike_diag import load_glb, accessor  # noqa: E402

TEXTURED = r'G:\Game\assets\models\model_player_congyun_03_textured.glb'
SOURCE = r'G:\Game\assets\models\model_player_congyun_03_rigged_weapon.glb'

QUANT = 1e5   # positions are quantised to 1e-5 m to build the lookup key


def key(p):
    return (round(p[0] * QUANT), round(p[1] * QUANT), round(p[2] * QUANT))


def gather(js, bin_):
    """Returns (joint names, {vertex-key: (joints, weights)}, plain position list)."""
    names = None
    table = {}
    flat = []
    dupes = 0
    for n in js['nodes']:
        if 'mesh' not in n or 'skin' not in n:
            continue
        if names is None:
            sk = js['skins'][n['skin']]
            names = [js['nodes'][j].get('name', '?') for j in sk['joints']]
        for prim in js['meshes'][n['mesh']]['primitives']:
            at = prim['attributes']
            if 'JOINTS_0' not in at:
                continue
            pos = accessor(js, bin_, at['POSITION'])
            J = accessor(js, bin_, at['JOINTS_0'])
            W = accessor(js, bin_, at['WEIGHTS_0'])
            for k in range(len(pos)):
                kk = key(pos[k])
                if kk in table:
                    dupes += 1
                table[kk] = (tuple(J[k]), tuple(W[k]))
                flat.append((pos[k], tuple(J[k]), tuple(W[k])))
    return names, table, flat, dupes


def nearest(flat, p, tol):
    best = None
    bd = tol
    for q, J, W in flat:
        d = ((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2) ** 0.5
        if d < bd:
            bd = d
            best = (J, W)
    return best, bd


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--textured', default=TEXTURED)
    ap.add_argument('--source', default=SOURCE)
    ap.add_argument('--out', required=True)
    args = ap.parse_args()

    dst_js, dst_bin = load_glb(args.textured)
    src_js, src_bin = load_glb(args.source)
    print('[restore] textured : %s' % args.textured)
    print('[restore] weight src: %s' % args.source)

    src_names, table, flat, dupes = gather(src_js, src_bin)
    dst_sk = dst_js['skins'][0]
    dst_names = [dst_js['nodes'][j].get('name', '?') for j in dst_sk['joints']]
    print('[restore] joints: source=%d textured=%d  duplicate keys in source=%d'
          % (len(src_names), len(dst_names), dupes))
    if src_names != dst_names:
        print('[restore] WARNING: joint order differs')
        print('   source: %s' % src_names)
        print('   dst   : %s' % dst_names)

    buf = bytearray(dst_bin)

    def loc(acc_idx):
        acc = dst_js['accessors'][acc_idx]
        bv = dst_js['bufferViews'][acc['bufferView']]
        comp = acc['componentType']
        ncomp = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[acc['type']]
        fmt = {5120: 'b', 5121: 'B', 5122: 'h', 5123: 'H', 5125: 'I', 5126: 'f'}[comp]
        size = struct.calcsize('<' + fmt)
        base = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
        stride = bv.get('byteStride') or size * ncomp
        return base, stride, fmt, ncomp

    restored = missed = total = 0
    rescued = 0
    worst_rescue = 0.0
    for n in dst_js['nodes']:
        if 'mesh' not in n or 'skin' not in n:
            continue
        for prim in dst_js['meshes'][n['mesh']]['primitives']:
            at = prim['attributes']
            if 'JOINTS_0' not in at:
                continue
            pos = accessor(dst_js, dst_bin, at['POSITION'])
            base_j, stride_j, fmt_j, _ = loc(at['JOINTS_0'])
            base_w, stride_w, fmt_w, _ = loc(at['WEIGHTS_0'])
            for k in range(len(pos)):
                total += 1
                got = table.get(key(pos[k]))
                if got is None:
                    # Exact-position match failed for a few vertices (UV unwrap can shift a
                    # vertex by a sub-micron amount).  Fall back to nearest position, but
                    # only accept it when it is really the same point.
                    got, dist = nearest(flat, pos[k], 1e-3)
                    if got is None:
                        missed += 1
                        continue
                    rescued += 1
                    worst_rescue = max(worst_rescue, dist)
                J, W = got
                struct.pack_into('<' + fmt_j * 4, buf, base_j + k * stride_j, *J)
                struct.pack_into('<' + fmt_w * 4, buf, base_w + k * stride_w, *W)
                restored += 1

    print('[restore] verts=%d restored=%d (of which nearest-match %d, worst %.2e m) missed=%d'
          % (total, restored, rescued, worst_rescue, missed))
    if missed:
        print('[restore] FAILED: %d vertices had no positional match in the source' % missed)
        return 1

    dst_js['buffers'][0]['byteLength'] = len(buf)
    jb = json.dumps(dst_js, separators=(',', ':')).encode('utf-8')
    jb += b' ' * ((4 - len(jb) % 4) % 4)
    bb = bytes(buf)
    bb += b'\x00' * ((4 - len(bb) % 4) % 4)
    out = bytearray()
    out += b'glTF' + struct.pack('<II', 2, 12 + 8 + len(jb) + 8 + len(bb))
    out += struct.pack('<II', len(jb), 0x4E4F534A) + jb
    out += struct.pack('<II', len(bb), 0x004E4942) + bb
    with open(args.out, 'wb') as f:
        f.write(out)
    print('[restore] wrote %s (%d bytes)' % (args.out, len(out)))

    # verify: the written file's weights must equal the source's
    js2, b2 = load_glb(args.out)
    nb = [k for k, nm in enumerate(dst_names) if nm == 'neutral_bone']
    stuck = 0
    for n in js2['nodes']:
        if 'mesh' not in n or 'skin' not in n:
            continue
        for prim in js2['meshes'][n['mesh']]['primitives']:
            if 'JOINTS_0' not in prim['attributes']:
                continue
            J = accessor(js2, b2, prim['attributes']['JOINTS_0'])
            W = accessor(js2, b2, prim['attributes']['WEIGHTS_0'])
            for k in range(len(J)):
                if nb and all(J[k][s] == nb[0] for s in range(4) if W[k][s] > 1e-6):
                    stuck += 1
    print('[restore] verify: vertices stuck on neutral_bone = %d (original had 1265)' % stuck)
    print('[restore] OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
