# -*- coding: utf-8 -*-
"""主角 glb 蒙皮权重的**定位性体检**（纯读，不改任何文件）。

    python tools/weight_audit.py [glb 路径]
    默认 assets/models/model_player_congyun_03_textured.glb

它量什么、为什么这么量
----------------------
指标：**每个顶点到「主导它那根骨的线段」的最短距离**。

* 主导骨 = 权重最大的那根骨。
* 线段 = 关节头 → 子关节（叶子骨取父骨方向补 0.12 m）。

为什么不用别的：

* 「顶点到关节头的距离」有系统偏差——长骨的顶点云中心本来就在骨头中段，
  离关节头 ≈ 半根骨长，怎么算都大，看不出好坏。
* 「主导骨离顶点 >0.45 m 的有几个」（T49 用的）**只看主导骨**，
  一个被左腿主导、却挂着 0.35 右腿权重的顶点，在它的扫描里是"健康"的。
* 棱伸长/PivotDrift 是"结果"指标，受姿势幅度影响；
  这里是"原因"指标——权重糊不糊，跟动画无关。

判读基准：定位良好的蒙皮，中位应 < 0.05 m、p90 < 0.12 m。
超过 0.20 m 的顶点，在大幅度旋转下必然被拖飞。
"""
import json
import os
import struct
import sys

import numpy as np

DEFAULT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "assets", "models", "model_player_congyun_03_textured.glb")


def log(m):
    print("[waudit] " + str(m))


def load(path):
    b = open(path, "rb").read()
    off = 12
    j = bc = None
    while off < len(b):
        ln, ty = struct.unpack_from("<II", b, off)
        if ty == 0x4E4F534A:
            j = json.loads(b[off + 8:off + 8 + ln])
        elif ty == 0x004E4942:
            bc = b[off + 8:off + 8 + ln]
        off += 8 + ln
    if j is None or bc is None:
        raise RuntimeError("读不出 glb 的 JSON/BIN chunk")
    return j, bc


CT = {5126: ("f", 4), 5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4)}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def accessor(j, bc, ai):
    a = j["accessors"][ai]
    bv = j["bufferViews"][a["bufferView"]]
    o = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    c, s = CT[a["componentType"]]
    n = NC[a["type"]]
    stride = bv.get("byteStride") or n * s
    out = np.zeros((a["count"], n), dtype=np.float64)
    for i in range(a["count"]):
        out[i] = struct.unpack_from("<" + c * n, bc, o + i * stride)
    return out


def node_matrix(n):
    T = np.eye(4)
    if "translation" in n:
        T[:3, 3] = n["translation"]
    if "rotation" in n:
        x, y, z, w = n["rotation"]
        R = np.eye(4)
        R[:3, :3] = np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        T = T @ R
    if "scale" in n:
        S = np.eye(4)
        S[0, 0], S[1, 1], S[2, 2] = n["scale"]
        T = T @ S
    return T


def seg_dist(pts, a, ab):
    """点到线段 a→a+ab 的距离（向量化）。"""
    ab2 = float(ab @ ab)
    t = 0.0 if ab2 < 1e-12 else ((pts - a) @ ab) / ab2
    t = np.clip(t, 0.0, 1.0)
    proj = a + t[:, None] * ab
    return np.linalg.norm(pts - proj, axis=1)


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
    if not os.path.isfile(path):
        raise SystemExit("找不到 " + path)
    j, bc = load(path)
    nodes = j["nodes"]
    sk = j["skins"][0]
    names = [nodes[k].get("name", "?") for k in sk["joints"]]

    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            parent[c] = i

    def gmat(i):
        M = node_matrix(nodes[i])
        while i in parent:
            i = parent[i]
            M = node_matrix(nodes[i]) @ M
        return M

    head = np.array([gmat(k)[:3, 3] for k in sk["joints"]])
    joint_set = set(sk["joints"])
    tail = np.zeros_like(head)
    for idx, k in enumerate(sk["joints"]):
        kids = [c for c in nodes[k].get("children", []) if c in joint_set]
        if kids:
            tail[idx] = np.mean([gmat(c)[:3, 3] for c in kids], axis=0)
        else:
            pi = parent.get(k)
            d = head[idx] - (gmat(pi)[:3, 3] if pi is not None else head[idx] - np.array([0, 0.12, 0]))
            L = float(np.linalg.norm(d))
            tail[idx] = head[idx] + (d / L * 0.12 if L > 1e-6 else np.array([0.0, 0.12, 0.0]))

    POS, JO, WE = [], [], []
    nprim = 0
    for prim in j["meshes"][0]["primitives"]:
        a = prim["attributes"]
        POS.append(accessor(j, bc, a["POSITION"]))
        JO.append(accessor(j, bc, a["JOINTS_0"]).astype(int))
        WE.append(accessor(j, bc, a["WEIGHTS_0"]))
        nprim += 1
    P = np.vstack(POS)
    J = np.vstack(JO)
    W = np.vstack(WE)
    N = len(P)
    log("文件 %s" % os.path.basename(path))
    log("primitive=%d 顶点=%d 关节=%d" % (nprim, N, len(names)))

    # ★ argmax 给的是「第几个影响槽」（0..3），不是关节索引——
    #   必须经 JOINTS_0 映射一次，否则所有顶点都会被算成 0 号骨（第一版就错在这）。
    dom = J[np.arange(N), W.argmax(axis=1)]
    d = np.zeros(N)
    per_bone = {}
    for idx, nm in enumerate(names):
        m = dom == idx
        if not m.any():
            continue
        dd = seg_dist(P[m], head[idx], tail[idx] - head[idx])
        d[np.where(m)[0]] = dd
        per_bone[nm] = (int(m.sum()), float(np.median(dd)), float(np.percentile(dd, 90)))

    med = float(np.median(d))
    p90 = float(np.percentile(d, 90))
    log("全体：中位 %.3f m   p90 %.3f m   p99 %.3f m   最大 %.3f m"
        % (med, p90, float(np.percentile(d, 99)), float(d.max())))
    bad = int((d > 0.20).sum())
    for lo, hi in [(0, 0.05), (0.05, 0.10), (0.10, 0.20), (0.20, 99.0)]:
        c = int(((d > lo) & (d <= hi)).sum())
        log("  (%.2f, %s] : %6d (%.1f%%)" % (lo, "∞" if hi > 90 else "%.2f" % hi, c, c / N * 100))

    log("按骨（顶点数 / 中位 / p90），★ = 中位 > 0.15：")
    for nm, (n, md, p9) in sorted(per_bone.items(), key=lambda x: -x[1][1]):
        log("  %-11s n=%5d  中位 %.3f  p90 %.3f %s" % (nm, n, md, p9, "★" if md > 0.15 else ""))

    # 判定：定位良好 → 中位 <0.05 且 p90 <0.12
    ok = med < 0.05 and p90 < 0.12
    log("结论：%s（中位 %.3f / p90 %.3f；良好标准 <0.05 / <0.12）"
        % ("定位良好" if ok else "★ 权重弥散——大幅度旋转必然把顶点拖飞", med, p90))
    raise SystemExit(0 if ok else 3)


main()
