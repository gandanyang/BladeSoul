#!/usr/bin/env python3
"""按 10 §1 的魔骸配色给网格**程序化上色**（写顶点色，不需要 UV）。

为什么走顶点色：本机没有本地贴图管线（Hunyuan3D 的纹理/UV 节点全是腾讯云 API，
违反 T35"纯本地离线"），也没装 xatlas / Blender 做 UV 展开。
而**顶点色不需要 UV**——这个模型本来就有 6,260 个顶点的颜色通道（只是一片平灰），
按高度分层写进去即可，Godot 侧的 StandardMaterial3D 直接吃 COLOR_0。

配色全部来自 10 §1「魔骸通用设计语言」：
    主色   黑   #141014   （饱和度低、湿、无光泽）
    甲片   冷灰 #3A3E44   （"残破但仍在用"）
    皮肤   灰黑 #4A4850
    血红   #7A1418        （次要色）
    发光红 #C8323A        （眼窝与伤口，越强的敌人越亮）

⚠️ 这是 **MVP 的可读性方案**，不是最终资产：它给的是色块，不是花纹。
   要真正的"贴图"（绑绳、甲片纹理、破口），得先有 UV —— 那是另一张卡。

★ COLOR_0 是**线性**通道（glTF 规范），所以调色板的 sRGB 值必须转成线性再写。
   不转的后果是**整体被提亮成另一个颜色**，而且看起来"像美术风格问题"、不像 bug：

   | 调色板（sRGB，10 §1） | 不转直接写 → 引擎里显示成 | 转线性后写 → 显示成 |
   |---|---|---|
   | 血红 `#7A1418` | **`#B84D54` 粉红** | `#7A1418` ✅ |
   | 主色黑 `#141014` | `#3D373D` 灰 | `#141014` ✅ |

   实测证据（2026-09-14）：`t27_yari_showcase.png` 里魔骸腰带采样 RGB = **(185,92,100)**，
   而"按 sRGB 字节写、按线性读"的理论值正是 (184,77,84)（差值来自环境光）。
   道场现有的那尊魔骸（`ashigaru_v2d_colored.glb`）也一起受影响——
   `t40_after.png` 里它的腰带是 (163,85,90)。**默认已修正**；`--color-space raw` 只用于复现旧结果。

    python tools/paint_mesh.py <模型.glb> --out <输出.glb> [--glow 1.6] [--color-space raw] [--style polearm]
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
import trimesh

# ── 10 §1 的配色（改这里就是改配色）──────────────────────────────
BLACK = (0x14, 0x10, 0x14)      # 主色
PLATE = (0x3A, 0x3E, 0x44)      # 甲片
SKIN = (0x4A, 0x48, 0x50)       # 皮肤（比甲片略暖、略亮）
BLOOD = (0x7A, 0x14, 0x18)      # 血红
GLOW = (0xC8, 0x32, 0x3A)       # 发光红（眼窝/伤口）

# 人形分层用的两个几何比例（改这里就是改分层的形状）
TORSO_X_RATIO = 0.55            # 躯干半宽 / 全身半宽——超过这条线算手臂，不涂腰带血红
EYE_BAND = (0.925, 0.945)       # 眼窝环带（占身高比例）；原为 (0.90, 0.945)，太宽像眼罩

# 道具（tools/gen_props.py 的产物）用的分层门限
JINGASA_RIM_RATIO = 0.86        # 笠缘磨出铁色的**半径**门（相对最大半径）——不用高度：
                                # 笠是烘了倾斜的，"高度带"切出来的缘会一边宽一边窄
KATANA_HILT_TOP = 0.256         # 柄+鍔 与 刀身 的分界（占全长比例）
KATANA_TIP_GLOW = 0.965         # 切先的红光带（占全长比例）


def log(m: str) -> None:
    print(m, flush=True)


def verify_attributes(path: str) -> str:
    """把写出的 glb 读回来，确认 `COLOR_0` 与 `NORMAL` 真的在文件里。

    判据必须落在**文件的 JSON chunk** 上，不能落在观感上：法线丢了的时候
    Godot 会兜底生成，**渲染看起来是正常的**——这条缺陷因此藏了很久。
    （复用 `tools/model_budget.py` 的 `read_glb`，不再写第二套 glb 读取。）
    """
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from model_budget import read_glb                                   # noqa: PLC0415

    js, _ = read_glb(path)
    have: set[str] = set()
    for mesh_def in js.get("meshes", []):
        for prim in mesh_def.get("primitives", []):
            have |= set(prim.get("attributes", {}).keys())

    ok_color, ok_normal = "COLOR_0" in have, "NORMAL" in have
    return (f"写出的 attributes = {sorted(have)}  "
            + ("✅" if ok_color and ok_normal else "✗")
            + ("" if ok_color else "  ← 没有 COLOR_0，顶点色不会生效")
            + ("" if ok_normal else "  ← 没有 NORMAL，Godot 侧没有 FormatNormal（docs/13 §2.4）"))


def srgb_to_linear(c: int) -> int:
    """把单个 sRGB 通道值（0~255）转成线性值（0~255）——COLOR_0 必须是线性的。"""
    s = c / 255.0
    lin = s / 12.92 if s <= 0.04045 else ((s + 0.055) / 1.055) ** 2.4
    return int(round(lin * 255.0))


def hexof(c) -> str:
    return "%02X%02X%02X" % (c[0], c[1], c[2])


def linear_to_srgb(c: int) -> int:
    """线性值（0~255）→ sRGB 值（0~255）。引擎把线性 COLOR_0 显示出来的那一步。"""
    s = c / 255.0
    srgb = s * 12.92 if s <= 0.0031308 else 1.055 * (s ** (1 / 2.4)) - 0.055
    return int(round(srgb * 255.0))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("--out", required=True)
    ap.add_argument("--glow", type=float, default=1.0,
                    help="发光红的强度倍率（10 §1：越强的敌人越亮）")
    ap.add_argument("--color-space", choices=["srgb-to-linear", "raw"], default="srgb-to-linear",
                    help="srgb-to-linear（默认）= 按 glTF 规范把调色板转线性后写入 COLOR_0；"
                         "raw = 直接写 sRGB 字节（旧行为，血红会显示成粉红）")
    ap.add_argument("--style", choices=["humanoid", "polearm", "jingasa", "katana"],
                    default="humanoid",
                    help="humanoid（默认）按人形高度分层；polearm 按长杆兵器分层"
                         "（杆黑 / 枪刃冷铁灰 / 刃缘发光红）；jingasa 阵笠"
                         "（主色黑 + 笠缘磨出铁色，按**半径**分层）；katana 打刀"
                         "（柄鍔黑 / 刀身冷铁灰 / 切先红光，按高度分层）")
    args = ap.parse_args()

    if args.color_space == "srgb-to-linear":
        conv = lambda c: tuple(srgb_to_linear(v) for v in c)   # noqa: E731
    else:
        conv = lambda c: tuple(c)                              # noqa: E731

    black, plate, skin = conv(BLACK), conv(PLATE), conv(SKIN)
    blood, glow_rgb = conv(BLOOD), conv(GLOW)
    log(f"色彩空间: {args.color_space}"
        + (f"  （血红 sRGB #{hexof(BLOOD)} → 线性 #{hexof(blood)}）"
           if args.color_space == "srgb-to-linear" else "  ← 旧行为，会偏亮"))

    if not os.path.isfile(args.model):
        log(f"✗ 找不到输入：{args.model}")
        return 2

    mesh = trimesh.load(args.model, force="mesh")
    v = mesh.vertices
    log(f"载入: 面 {len(mesh.faces):,}  顶点 {len(v):,}  包围盒 {np.round(mesh.extents, 3)}")

    # 归一化高度：0 = 脚底，1 = 最高点
    lo, hi = v[:, 1].min(), v[:, 1].max()
    t = (v[:, 1] - lo) / max(1e-6, hi - lo)

    colors = np.zeros((len(v), 4), dtype=np.uint8)
    colors[:, 3] = 255

    # 分层方式按**物件形态**分派。同一套切法套到不同形状上会涂错地方：
    # 人形那套按"腿 → 腰带 → 胴甲 → 头"切，套到一杆竖枪上会把血红涂到杆子中间。
    if args.style == "polearm":
        # 长枪：杆 黑 → 枪刃 冷铁灰 → 刃缘 发光红（10 §2.2 的危险信号）
        colors[t < 0.88] = (*black, 255)                   # 杆（含握把）
        colors[(t >= 0.88) & (t < 0.965)] = (*plate, 255)  # 枪刃
        colors[t >= 0.965] = (*plate, 255)                 # 刃身
        glow_band = t >= 0.965                             # 刃缘的微弱红光
    elif args.style == "jingasa":
        # 阵笠（10 §1）：主色黑；唯一的变化是**笠缘磨出来的铁色**。
        # 判据用**半径**而不是高度——笠的倾斜是烘进网格的（tools/gen_props.py），
        # 用高度带会把笠缘切成一边宽一边窄，看起来像涂错。
        radius = np.hypot(v[:, 0], v[:, 2])
        r_max = float(radius.max())
        rim = radius >= JINGASA_RIM_RATIO * r_max
        colors[:] = (*black, 255)
        colors[rim] = (*plate, 255)
        glow_band = np.zeros(len(v), dtype=bool)           # 笠不发光：发光红只给眼窝与伤口
        log(f"半径分层: 最大半径 {r_max:.3f}m → 笠缘门 {JINGASA_RIM_RATIO * r_max:.3f}m；"
            f"笠缘 {int(rim.sum())} 个顶点用甲片色，其余 {int((~rim).sum())} 个用主色黑")
    elif args.style == "katana":
        # 打刀（10 §1）：柄与鍔一组涂主色黑（读起来是"一整根握把"），
        # 刀身冷铁灰（金属），切先一带红光——与长枪的"刃缘红光"同一种语言。
        hilt = t < KATANA_HILT_TOP
        colors[hilt] = (*black, 255)
        colors[~hilt] = (*plate, 255)
        glow_band = t >= KATANA_TIP_GLOW
        log(f"高度分层: 柄+鍔 黑 <{KATANA_HILT_TOP:.3f}（{int(hilt.sum())} 顶点）"
            f" → 刀身 冷铁灰（{int((~hilt).sum())} 顶点）；"
            f"切先红光 {int(glow_band.sum())} 顶点")
    else:
        # ★ 横向闸门：**甲/腰带只画在躯干上，不画到垂下来的手臂**。
        #   没有它时，0.42~0.62 这一段高度会把前臂和手一起涂成血红
        #   （实测：手臂自然下垂时手正好落在腰带的高度区间里）——
        #   于是敌人看起来像戴了红手套。判据用**相对**半宽，两个模型共用一套阈值
        #   （足兵宽 1.355m、枪兵瘦长 0.847m，绝对值不能共用）。
        half_width = float(np.abs(v[:, 0]).max())
        torso = np.abs(v[:, 0]) <= TORSO_X_RATIO * half_width

        colors[t < 0.10] = (*black, 255)                   # 脚
        colors[(t >= 0.10) & (t < 0.42)] = (*black, 255)   # 腿 / 草摺
        colors[(t >= 0.42) & (t < 0.62) & torso] = (*blood, 255)  # 腰带一带（仅躯干）
        colors[(t >= 0.62) & (t < 0.84)] = (*plate, 255)   # 胴甲
        colors[(t >= 0.84) & (t < 0.92)] = (*skin, 255)    # 颈肩
        colors[t >= 0.92] = (*skin, 255)                   # 头/兜
        # 眼窝/伤口：**窄环带**（10 §1 的辨识线索）。
        # 做成环带而不是只涂正面，是为了从任何角度都能读出"里面透出红光"，
        # 也免去猜模型朝向。但**不能太宽**：4.5% 身高 ≈ 7.6cm，比眼睛那一圈宽一倍，
        # 渲染出来像一条红色眼罩；2% ≈ 3.4cm 才是"眼窝在发光"。
        glow_band = (t >= EYE_BAND[0]) & (t < EYE_BAND[1])

        leak = (t >= 0.42) & (t < 0.62) & ~torso
        log(f"横向闸门: 全身半宽 {half_width:.3f}m → 躯干阈值 {TORSO_X_RATIO * half_width:.3f}m；"
            f"腰带高度区间内排除 {int(leak.sum())} 个手臂/手顶点（否则会变成\u201c红手套\u201d）")
        log(f"眼窝环带: 身高 {EYE_BAND[0]:.3f}~{EYE_BAND[1]:.3f}"
            f"（{int(glow_band.sum())} 个顶点，占总高 {(EYE_BAND[1]-EYE_BAND[0])*100:.1f}%）")

    # 倍率先作用在**显示色**上，再转线性——否则"更亮"会被 2.4 次幂吃掉一大截。
    glow_display = np.clip(np.array(GLOW, dtype=float) * args.glow, 0, 255).astype(np.uint8)
    g = (np.array([srgb_to_linear(int(x)) for x in glow_display], dtype=np.uint8)
         if args.color_space == "srgb-to-linear" else glow_display)
    colors[glow_band] = (*g, 255)

    mesh.visual.vertex_colors = colors

    used = np.unique(colors[:, :3], axis=0)
    log(f"上色: 写入 {len(v):,} 个顶点，用到 {len(used)} 种颜色")
    for name, c in [("黑 主色", black), ("甲片", plate), ("皮肤", skin),
                    ("血红", blood), ("发光红", glow_rgb)]:
        n = int(np.all(colors[:, :3] == np.array(c, dtype=np.uint8), axis=1).sum())
        if n:
            log(f"        {name:<8} {n:>6,} 个顶点  ({n/len(v)*100:4.1f}%)")

    # ── 自检：把写入的线性值按引擎的方式读回来（线性 → sRGB），必须回到 10 §1 的原色 ──
    #    判据带 ±2 容差：极暗色在线性空间被 8bit 量化时，回读会有 1~2 级的误差
    #    （#7A1418 → 线性 #320202 → 回读 #7A1616，G 通道差 2）。这不是选错色彩空间，
    #    而是 8bit 线性存储的下限；选错色彩空间时偏差是 **几十级**（#7A1418 → #B84D54）。
    back = tuple(linear_to_srgb(v) for v in blood)
    delta = max(abs(a - b) for a, b in zip(back, BLOOD))
    if args.color_space == "srgb-to-linear":
        mark = "  ✅" if delta <= 2 else f"  ✗ 应为 #{hexof(BLOOD)}，色彩空间选错了"
    else:
        mark = "  ⚠ raw 是旧行为——这一行就是「偏亮」那个缺陷本身，只用来复现历史截图"
    log(f"自检  : 血红 写入线性 #{hexof(blood)} → 引擎回读 #{hexof(back)}"
        f"（与 10 §1 的 #{hexof(BLOOD)} 最大差 {delta} 级）" + mark)

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)

    # ── ★ 必须显式带上 NORMAL，否则整条流水线的前一步白做了 ──────────────
    # `cleanup_mesh.py` 第 3 步专门补过法线（Hunyuan3D 的 SaveGLB 只写 POSITION），
    # 但 **trimesh 的 glb 导出是 `include_normals=None`：只在缓存里已有法线时才写**，
    # 而 `load()` 进来的法线不在 `vertex_normals` 缓存里 → 这里一导出就**又丢掉了**。
    # 后果与 docs/13 §2.4 记的完全一样：Godot 侧没有 FormatNormal。
    #
    # 实测（2026-09-14）：修之前 `jingasa_v1_colored.glb` 的
    # `primitive.attributes` 只有 `['COLOR_0','POSITION']`，而它的输入
    # `jingasa_v1_clean.glb` 明明是 `['NORMAL','POSITION']`。
    #
    # ⚠️ **不调 `fix_normals()`**：生成式产物**不水密**（surface net 留下的孔），
    # fix_normals 依赖闭合体积判内外的，在不水密网格上可能翻掉一整片的朝向。
    # 这里只"触发缓存"，让 trimesh 该算的算法线。
    mesh = mesh.copy()
    _ = mesh.vertex_normals

    mesh.export(args.out, file_type="glb", include_normals=True)
    log(f"已写出: {args.out}  ({os.path.getsize(args.out):,} 字节)")

    # ── 自检：把写出的文件读回来，确认 NORMAL 与 COLOR_0 真的在 ──────────
    #    2026-09-14 之前这条一直是坏的，而**渲染看起来正常**（Godot 会兜底生成法线），
    #    所以只能靠读 glb 的 JSON chunk 发现——判据必须落在文件上，不在观感上。
    log("自检  : " + verify_attributes(args.out))
    log("提示：顶点色要生效，Godot 的材质必须开 vertex_color_use_as_albedo（glTF 的 COLOR_0 一般会自动接上）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
