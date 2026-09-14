# -*- coding: utf-8 -*-
"""按「最近骨段」重算整张网格的蒙皮权重，**写到新文件**，不覆盖任何现有模型。

    python tools/reskin_nearest.py <输入.glb> <输出.glb>

为什么这么做（与 T49「只修 1265 个孤儿顶点」的分歧）
------------------------------------------------------
`tools/weight_audit.py` 量出来的是：**全身 96.6% 的顶点离主导骨超过 0.10 米**，
连脊柱、手臂都是 0.17~0.22 米（良好标准：中位 <0.05）。
这是 `rig_character.py` 当年骨热失败后走 **envelope 回退**的签名——
包络权重按距离摊给一大圈骨头，本来就不定位。

所以「只动 1265 个孤儿」修不到根上；T49 自己的对照表也显示
修完棱伸长还有 450~506 mm，那不是修好了，是病还在。

做法：每个顶点取**最近的 4 根骨段**，按 inverse-distance^3 分权（锐利、贴身），
再归一。这样定位性是**构造保证**的，不依赖骨热成功。
其余字节一律不动：关节层级、绑定姿势、动画、材质、UV 全保留。

产出必须用 `tools/weight_audit.py` 复检——中位不降到 0.05 以下就别接进游戏。
"""
import json
import os
import struct
import sys

import numpy as np

CT = {5126: ("f", 4), 5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4)}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
POW = 3.0        # inverse-distance 的幂；越大越贴身
EPS = 0.02       # 防除零，同时给 0 距离一个饱和半径


def log(m):
    print("[reskin] " + str(m))


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
    return b, bytearray(bc), j


def accessor_info(j, ai):
    a = j["accessors"][ai]
    bv = j["bufferViews"][a["bufferView"]]
    return {
        "offset": bv.get("byteOffset", 0) + a.get("byteOffset", 0),
        "stride": bv.get("byteStride") or NC[a["type"]] * CT[a["componentType"]][1],
        "comp": CT[a["componentType"]],
        "n": NC[a["type"]],
        "count": a["count"],
        "compType": a["componentType"],
    }


def read_acc(bc, info):
    c, s = info["comp"]
    n = info["n"]
    out = np.zeros((info["count"], n), dtype=np.float64)
    for i in range(info["count"]):
        out[i] = struct.unpack_from("<" + c * n, bc, info["offset"] + i * info["stride"])
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
    ab2 = float(ab @ ab)
    t = 0.0 if ab2 < 1e-12 else ((pts - a) @ ab) / ab2
    t = np.clip(t, 0.0, 1.0)
    return np.linalg.norm(pts - (a + t[:, None] * ab), axis=1)


def main():
    if len(sys.argv) < 3:
        raise SystemExit("用法: python tools/reskin_nearest.py <输入.glb> <输出.glb>")
    src, dst = sys.argv[1], sys.argv[2]
    raw, bc, j = load(src)
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
    jset = set(sk["joints"])
    tail = np.zeros_like(head)
    for idx, k in enumerate(sk["joints"]):
        kids = [c for c in nodes[k].get("children", []) if c in jset]
        if kids:
            tail[idx] = np.mean([gmat(c)[:3, 3] for c in kids], axis=0)
        else:
            pi = parent.get(k)
            d = head[idx] - (gmat(pi)[:3, 3] if pi is not None else head[idx] - np.array([0, 0.12, 0]))
            L = float(np.linalg.norm(d))
            tail[idx] = head[idx] + (d / L * 0.12 if L > 1e-6 else np.array([0.0, 0.12, 0.0]))
    ab = tail - head

    POS, JI, WI = [], [], []
    infos = []
    for prim in j["meshes"][0]["primitives"]:
        a = prim["attributes"]
        i_j = accessor_info(j, a["JOINTS_0"])
        i_w = accessor_info(j, a["WEIGHTS_0"])
        POS.append(read_acc(bc, accessor_info(j, a["POSITION"])))
        JI.append(read_acc(bc, i_j).astype(int))
        WI.append(read_acc(bc, i_w))
        infos.append((i_j, i_w))
    P = np.vstack(POS)
    total = len(P)
    log("顶点 %d  关节 %d" % (total, len(names)))

    # 每个顶点对每根骨的距离 → 取最近 4 根
    D = np.zeros((total, len(names)))
    for idx in range(len(names)):
        D[:, idx] = seg_dist(P, head[idx], ab[idx])
    K = 4
    nearest = np.argsort(D, axis=1)[:, :K]
    dn = np.take_along_axis(D, nearest, axis=1)
    wgt = 1.0 / (dn + EPS) ** POW
    wgt /= wgt.sum(axis=1, keepdims=True)
    log("最近 4 根骨里，第 1 近的平均占比 %.1f%%，第 4 近 %.1f%%"
        % (wgt[:, 0].mean() * 100, wgt[:, 3].mean() * 100))

    # 写回（保持原 accessor 的类型/数量/偏移，只改值）
    start = 0
    for i_j, i_w in infos:
        n = i_j["count"]
        js = JI[start:start + n] * 0
        ws = wgt[start:start + n]
        # 关节索引：用真实关节索引
        js = nearest[start:start + n]
        c_j, s_j = i_j["comp"]
        c_w, s_w = i_w["comp"]
        for i in range(n):
            base_j = i_j["offset"] + i * i_j["stride"]
            base_w = i_w["offset"] + i * i_w["stride"]
            for k in range(4):
                struct.pack_into("<" + c_j, bc, base_j + k * s_j, int(js[i, k]))
                v = float(ws[i, k])
                if c_w == "B":                      # 归一化字节
                    struct.pack_into("<B", bc, base_w + k * s_w,
                                     int(round(np.clip(v, 0, 1) * 255)))
                elif c_w == "H":
                    struct.pack_into("<H", bc, base_w + k * s_w,
                                     int(round(np.clip(v, 0, 1) * 65535)))
                else:
                    struct.pack_into("<f", bc, base_w + k * s_w, v)
        start += n

    out = bytearray(raw)
    # 找到 BIN chunk 的绝对位置并替换
    off = 12
    while off < len(out):
        ln, ty = struct.unpack_from("<II", out, off)
        if ty == 0x004E4942:
            if ln != len(bc):
                raise RuntimeError("BIN chunk 长度对不上，拒绝写出")
            out[off + 8:off + 8 + ln] = bc
            break
        off += 8 + ln

    with open(dst, "wb") as f:
        f.write(out)
    log("写出 %s (%.2f MB)" % (dst, os.path.getsize(dst) / 1e6))
    log("★ 必须复检：python tools/weight_audit.py %s" % dst)


main()
