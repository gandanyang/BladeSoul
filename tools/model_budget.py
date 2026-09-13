"""给模型做"性能预算体检"：面数 / 顶点 / 材质 / 贴图 / 骨骼 / 动画 / 文件大小。

    python tools/model_budget.py assets/models/model_player_congyun_01.glb [...]

为什么需要它：性能不是"跑起来卡了再查"的事——**面数与贴图是可以在入库前量出来的**。
本项目的预算是 10 §2.1（杂兵 MVP ≤12k / 最终 ≤20k）与 09 §7（主角 ≤25k）。

输出是按模型对齐的一张表，**好和预算直接比**。
"""

from __future__ import annotations

import json
import os
import struct
import sys


def read_glb(path: str) -> tuple[dict, int]:
    """读 glb 的 JSON chunk，并返回（json, 二进制 chunk 字节数）。"""
    with open(path, "rb") as f:
        magic, version, length = struct.unpack("<III", f.read(12))
        if magic != 0x46546C67:
            raise ValueError(f"{path} 不是 glb（magic={magic:08x}）")
        chunks = []
        while f.tell() < length:
            clen, ctype = struct.unpack("<II", f.read(8))
            chunks.append((ctype, f.read(clen)))
    data = None
    for ctype, payload in chunks:
        if ctype == 0x4E4F534A:      # JSON
            data = json.loads(payload.decode("utf-8"))
            break
    if data is None:
        raise ValueError(f"{path} 里没有 JSON chunk")
    bin_len = sum(len(p) for ctype, p in chunks if ctype == 0x004E4942)
    return data, bin_len


def triangles(js: dict) -> tuple[int, int]:
    """返回（三角面数, 顶点数）。索引数 / 3 就是面数；没有索引时用顶点数 / 3 估。"""
    tris = 0
    verts = 0
    for mesh in js.get("meshes", []):
        for prim in mesh.get("primitives", []):
            attrs = prim.get("attributes", {})
            pos = attrs.get("POSITION")
            if pos is not None:
                verts += js["accessors"][pos]["count"]
            if "indices" in prim:
                tris += js["accessors"][prim["indices"]]["count"] // 3
            elif pos is not None:
                tris += js["accessors"][pos]["count"] // 3
    return tris, verts


def image_bytes(js: dict, bin_len: int) -> tuple[int, list[str]]:
    """贴图总字节数（近似：bufferView 的长度）与格式列表。"""
    total = 0
    kinds = []
    for img in js.get("images", []):
        kinds.append(img.get("mimeType", "?"))
        view = img.get("bufferView")
        if view is not None:
            total += js["bufferViews"][view]["byteLength"]
        elif "uri" in img:
            kinds[-1] += "(uri)"
    return total, kinds


def report(path: str) -> dict:
    js, bin_len = read_glb(path)
    tris, verts = triangles(js)
    tex_bytes, tex_kinds = image_bytes(js, bin_len)

    return {
        "name": os.path.basename(path),
        "size_mb": os.path.getsize(path) / 1048576,
        "tris": tris,
        "verts": verts,
        "meshes": len(js.get("meshes", [])),
        "prims": sum(len(m.get("primitives", [])) for m in js.get("meshes", [])),
        "materials": len(js.get("materials", [])),
        "textures": len(js.get("textures", [])),
        "tex_mb": tex_bytes / 1048576,
        "tex_kinds": tex_kinds,
        "bones": len(js.get("skins", [{}])[0].get("joints", [])) if js.get("skins") else 0,
        "skins": len(js.get("skins", [])),
        "anims": len(js.get("animations", [])),
    }


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2

    rows = [report(p) for p in argv[1:]]

    print(f"{'模型':<34} {'文件MB':>7} {'三角面':>9} {'顶点':>8} {'网格':>5} "
          f"{'子网格':>6} {'材质':>5} {'贴图':>5} {'贴图MB':>7} {'骨骼':>5} {'动画':>5}")
    for r in rows:
        print(f"{r['name']:<34} {r['size_mb']:>7.2f} {r['tris']:>9,} {r['verts']:>8,} "
              f"{r['meshes']:>5} {r['prims']:>6} {r['materials']:>5} {r['textures']:>5} "
              f"{r['tex_mb']:>7.2f} {r['bones']:>5} {r['anims']:>5}")

    # 输出保持 ASCII：Windows 控制台默认 GBK，打对钩会直接 UnicodeEncodeError
    # （第一版就是这么崩的）。
    print()
    print("budget check (09 S7 player <=25k tris; 10 S2.1 mob MVP <=12k)")
    for r in rows:
        if r["tris"] <= 25000:
            verdict = "OK (%.0f%% of budget)" % (r["tris"] / 250.0)
        else:
            verdict = "OVER by %.1fx" % (r["tris"] / 25000.0)
        draw = "~%d draw / %d material(s) / %d texture(s)" % (
            r["prims"], r["materials"], r["textures"])
        print(f"  {r['name']:<34} {r['tris']:>9,} tris  {verdict:<22} {draw}")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
