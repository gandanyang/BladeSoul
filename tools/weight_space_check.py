#!/usr/bin/env python3
"""Sanity check: are the mesh vertices and the bone joints in the same space?

This is the check whose absence produced a garbage re-skin.  Any tool that assigns weights
compares vertex positions against bone segments, so if the mesh primitive and the
joints live in different spaces (or the mesh node transform was applied twice),
every weight comes out attached to the wrong bone -- and the failure looks like
"the algorithm is bad" instead of "the coordinates are wrong".

Run:  python tools/weight_space_check.py
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from weight_spike_diag import load_glb, accessor, world_matrices, m_apply  # noqa: E402

SRC = r'G:\Game\assets\models\model_player_congyun_03_textured.glb'


def bbox(points):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    zs = [p[2] for p in points]
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


def fmt(b):
    return 'x[%+.3f %+.3f] y[%+.3f %+.3f] z[%+.3f %+.3f]' % (b[0][0], b[0][1],
                                                             b[1][0], b[1][1],
                                                             b[2][0], b[2][1])


def main():
    js, bin_ = load_glb(SRC)
    wm = world_matrices(js)
    joints = js['skins'][0]['joints']
    names = {j: js['nodes'][j].get('name', 'node%d' % j) for j in joints}

    print('=== joints, in world space (what the weight tools use) ===')
    heads = [wm[j][12:15] for j in joints]
    print('  ' + fmt(bbox(heads)))

    print('=== mesh vertices ===')
    node_space = []
    world_space = []
    for ni, n in enumerate(js['nodes']):
        if 'mesh' not in n or 'skin' not in n:
            continue
        m = wm[ni]
        for prim in js['meshes'][n['mesh']]['primitives']:
            at = prim['attributes']
            pos = accessor(js, bin_, at['POSITION'])
            node_space += pos
            world_space += [m_apply(m, p) for p in pos]
    print('  raw (mesh local)      ' + fmt(bbox(node_space)))
    print('  transformed by node   ' + fmt(bbox(world_space)))

    print('=== per-bone distance from its own geometry (world space) ===')
    print('  bone              head                     nearest vertex   median')
    for j in joints:
        nm = names[j]
        h = wm[j][12:15]
        ds = sorted(math.dist(h, p) for p in world_space)
        which = node_space[0]
        del which
        print('  %-16s (%+.3f,%+.3f,%+.3f)   %8.3f   %8.3f'
              % (nm, h[0], h[1], h[2], ds[0], ds[len(ds) // 2]))

    print('=== the junk object check ===')
    for ni, n in enumerate(js['nodes']):
        if 'mesh' not in n:
            continue
        verts = 0
        for prim in js['meshes'][n['mesh']]['primitives']:
            verts += len(accessor(js, bin_, prim['attributes']['POSITION']))
        print('  node %-24s mesh=%-12s verts=%5d  skinned=%s'
              % (n.get('name', 'node%d' % ni), n['mesh'], verts, 'skin' in n))


if __name__ == '__main__':
    main()
