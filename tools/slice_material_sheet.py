# -*- coding: utf-8 -*-
"""
把 GPT-image 出的「3×2 无缝材质表」切成 6 张贴图，并做**真正的无缝化**。

为什么要自己接边：AI 生成器说的 "seamless" 基本都做不到四边严丝合缝，
贴到模型上会看到明显的网格状接缝。这里用「半幅平移 ＋ 缝上羽化」把它变成
**数学上真的无缝**（四边像素逐点相等），代价是纹样中间会有一道很淡的十字柔和带
——对布料/皮革/甲壳这类颗粒噪声纹理来说看不出来。

用法：
  blender --background --factory-startup --python tools/slice_material_sheet.py -- <输入图>
  不传输入图时，用 --self-test 跑一遍合成图，验证拼接逻辑本身没问题。

产出：assets/textures/congyun/tex_<槽位>.png（6 张 512²）
"""
import os
import sys

import bpy
import numpy as np

PROJ = r"G:\Game"
OUT_DIR = os.path.join(PROJ, "assets", "textures", "congyun")

COLS, ROWS = 3, 2
SIZE = 512          # 输出边长（2 的幂，Godot 友好）
BAND = 0.125        # 缝上羽化带宽（占边长比例）

# 阅读顺序 → 材质槽（与 assets/references/tex_prompt_congyun.md 的 ①~⑥ 一一对应）
SLOTS = ["cloth_in", "cloth_out", "leather", "metal", "gauntlet", "belt"]


def log(msg):
    print("[tex] " + str(msg))
    sys.stdout.flush()


def load_rgb(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    a = buf.reshape(h, w, 4)
    if img.colorspace_settings.name != 'sRGB':
        pass
    bpy.data.images.remove(img)
    # Blender 的 pixels 是**行优先从下往上**，翻回来变成常规图像坐标
    return np.flipud(a[:, :, :3]).astype(np.float64)


def save_rgb(arr, path):
    h, w, _ = arr.shape
    img = bpy.data.images.new("tmp_tex", width=w, height=h, alpha=False)
    flat = np.concatenate([np.flipud(arr), np.ones((h, w, 1))], axis=2).astype(np.float32)
    img.pixels.foreach_set(flat.ravel())
    if img.colorspace_settings.name != 'sRGB':
        img.colorspace_settings.name = 'sRGB'
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def resize(a, size):
    """双线性缩放。

    ★ 别换成最近邻：放大时会产生大量**完全相同的相邻行/列**，
    把 seam_ratio 的"图内相邻差值"压小，于是比值虚高、真正无缝的图也判不合格
    （第一版就是这么误报的：基线 Y 比值 1.78，其实是缩放伪影不是缝）。
    """
    h, w, _ = a.shape
    ys = (np.arange(size) + 0.5) * (h / size) - 0.5
    xs = (np.arange(size) + 0.5) * (w / size) - 0.5
    y0 = np.clip(np.floor(ys).astype(np.int64), 0, h - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    x0 = np.clip(np.floor(xs).astype(np.int64), 0, w - 1)
    x1 = np.clip(x0 + 1, 0, w - 1)
    fy = np.clip(ys - y0, 0.0, 1.0)[:, None, None]
    fx = np.clip(xs - x0, 0.0, 1.0)[None, :, None]
    top = a[y0][:, x0] * (1.0 - fx) + a[y0][:, x1] * fx
    bot = a[y1][:, x0] * (1.0 - fx) + a[y1][:, x1] * fx
    return top * (1.0 - fy) + bot * fy


def _ramp(n, band):
    """缝上的双向羽化权重：缝处 0，离开缝 b 像素后 1。"""
    idx = np.arange(n, dtype=np.float64)
    d = np.abs(idx - n / 2.0)
    b = max(1.0, n * band)
    return np.clip(d / b, 0.0, 1.0)


def make_seamless(t, band=BAND):
    """半幅平移后把中间的十字缝羽化掉 → 四边逐点相等（真无缝）。

    原理：np.roll 半幅之后，原来的**外边界变成了内部**，所以上下边、左右边
    天然接得上；不连续的地方被搬到了正中间。此时用一个两侧都连续的副本
    按「缝处权重 0、离开缝权重 1」做混合，缝上的不连续项就被权重 0 干掉了。
    """
    h, w, c = t.shape
    r = np.roll(np.roll(t, h // 2, 0), w // 2, 1)
    b = max(1, int(min(h, w) * band))
    off = np.roll(np.roll(r, b, 0), b, 1)

    wy = _ramp(h, band)[:, None, None]
    wx = _ramp(w, band)[None, :, None]
    a = np.minimum(wy, wx)          # 靠近任一条缝 → 权重趋 0
    return a * r + (1.0 - a) * off


def seam_ratio(t):
    """接缝质量：跨接缝的相邻行/列差值 ÷ 图内相邻行/列的典型差值。

    ★ 判据是**比值**不是绝对差。第一版自检写成「t[0] 与 t[-1] 逐像素相等」，
    那是**错的**——无缝并不要求首尾两行长得一样（平滑渐变图首尾就该不同），
    只要求"从最后一行接回第一行"这件事，和不跨界时的相邻关系一样自然。
    比值 ≈1 = 看不出来；明显 >1 = 有缝。
    """
    dy_int = float(np.abs(np.diff(t, axis=0)).mean())
    dy_wrap = float(np.abs(t[0] - t[-1]).mean())
    dx_int = float(np.abs(np.diff(t, axis=1)).mean())
    dx_wrap = float(np.abs(t[:, 0] - t[:, -1]).mean())
    ry = dy_wrap / max(dy_int, 1e-9)
    rx = dx_wrap / max(dx_int, 1e-9)
    return ry, rx, dy_wrap, dx_wrap


def find_separators(a, thresh=0.90):
    """找出「整行/整列都是近白」的分隔缝，返回 (行区间列表, 列区间列表)。

    ★ 别用固定的百分比裁边：AI 的排版不保证居中，固定裁会把白缝烤进贴图边缘，
    一平铺就是一圈白框。按实际检测到的缝切才干净。
    """
    rows = np.where(a.min(axis=(1, 2)) > thresh)[0]
    cols = np.where(a.min(axis=(0, 2)) > thresh)[0]

    def runs(idx):
        if len(idx) == 0:
            return []
        out, s, p = [], idx[0], idx[0]
        for v in idx[1:]:
            if v == p + 1:
                p = v
            else:
                out.append((int(s), int(p)))
                s = p = v
        out.append((int(s), int(p)))
        return out

    return runs(rows), runs(cols)


def cell_ranges(n, seps, count, pad=3):
    """把长度 n 的一维按分隔缝切成 count 段，**把整条缝排除在格子之外**。

    ★ 踩过的坑：按缝的**中点**切，等于把半条白缝留在格子边上——
    量出来的结果是每格第 0 列 / 第 510 列亮度 0.98（近白），
    贴到模型上就是一圈白边。必须用「上一条缝的末尾 +1」到「下一条缝的开头 -1」。
    """
    bounds = []
    prev_end = -1
    for s, e in seps:
        bounds.append((prev_end + 1, s - 1))
        prev_end = e
    bounds.append((prev_end + 1, n - 1))
    if len(bounds) != count:          # 缝没找齐就等分兜底
        step = n / count
        bounds = [(int(i * step), int((i + 1) * step) - 1) for i in range(count)]
    return [(a + pad, b - pad) for a, b in bounds]


def square_center(tile, pad=2):
    """取每格中心的最大正方形再缩放。

    ★ 这次 AI 出的是 411×621 的**竖长条**，直接拉成 512² 会把纹理颗粒横向拉伸 1.5 倍，
    贴到模型上颗粒方向不对。先取中心正方形就保住了颗粒的真实比例。
    """
    h, w, _ = tile.shape
    s = min(h, w) - 2 * pad
    y0 = (h - s) // 2
    x0 = (w - s) // 2
    return tile[y0:y0 + s, x0:x0 + s]


def edge_whiteness(t):
    """贴图四边的平均亮度。近白 = 分缝没切干净（会变成一圈白边）。

    ★ 这条断言是补上来的：第一版按缝中点切，肉眼看不出来，
    是量了"每列平均亮度的最亮列"才发现在第 0 列 / 第 510 列，亮度 0.98。
    """
    return max(float(t[0].mean()), float(t[-1].mean()),
               float(t[:, 0].mean()), float(t[:, -1].mean()))


def slice_sheet(sheet):
    rows_sep, cols_sep = find_separators(sheet)
    h, w, _ = sheet.shape
    yb = cell_ranges(h, rows_sep, ROWS)
    xb = cell_ranges(w, cols_sep, COLS)
    log("分隔缝 行%s 列%s" % (rows_sep, cols_sep))
    log("格子 y%s x%s" % (yb, xb))
    tiles = []
    for ry in range(ROWS):
        for rx in range(COLS):
            y0, y1 = yb[ry]
            x0, x1 = xb[rx]
            tiles.append(sheet[y0:y1 + 1, x0:x1 + 1])
    return tiles


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    src = args[0] if args else None

    if src is None:
        log("没给输入图，跑自检：造一张 900×600 的合成图喂进去")
        rng = np.random.default_rng(7)
        h, w = 600, 900
        yy, xx = np.mgrid[0:h, 0:w]
        sheet = np.zeros((h, w, 3))
        for i in range(ROWS):
            for j in range(COLS):
                base = rng.random(3) * 0.5 + 0.2
                m = np.zeros((h, w), dtype=bool)
                m[i * h // ROWS:(i + 1) * h // ROWS, j * w // COLS:(j + 1) * w // COLS] = True
                noise = rng.random((h, w, 1)) * 0.15
                # 两个方向都要有结构，否则某个方向的接缝根本测不出来
                blobs = rng.random((20, 30, 1)).repeat(h // 20 + 1, 0).repeat(w // 30 + 1, 1)[:h, :w]
                stripe = 0.08 * np.sin(xx / (3 + 2 * (i * COLS + j)))
                wave = 0.08 * np.sin(yy / (4 + (i * COLS + j)))
                sheet += m[:, :, None] * (base + noise + 0.3 * blobs + stripe[:, :, None] + wave[:, :, None])
        src = os.path.join(PROJ, "tools", "_tex_selftest_input.png")
        save_rgb(np.clip(sheet, 0, 1), src)
        log("合成输入图 → " + src)

    sheet = load_rgb(src)
    log("输入 %dx%d" % (sheet.shape[1], sheet.shape[0]))

    os.makedirs(OUT_DIR, exist_ok=True)
    # ★ 自检产物不许写进正式贴图目录：合成噪声图混在真贴图里，
    #   下一个 agent 会以为主角已经有贴图了。
    out_dir = OUT_DIR if args else os.path.join(OUT_DIR, "_selftest")
    os.makedirs(out_dir, exist_ok=True)
    if not args:
        log("自检模式：产物写到 %s（不入库）" % out_dir)
    tiles = slice_sheet(sheet)
    if len(tiles) != len(SLOTS):
        raise RuntimeError("切出 %d 格，期望 %d 格" % (len(tiles), len(SLOTS)))

    fail = 0
    for name, tile in zip(SLOTS, tiles):
        t = resize(square_center(tile), SIZE)
        # 对照组：先量**没处理**的接缝，再量处理后的——没有对照组就不知道修好没有
        b_ry, b_rx, _, _ = seam_ratio(t)
        t = make_seamless(t)
        ry, rx, wy, wx = seam_ratio(t)
        ew = edge_whiteness(t)
        ok = ry < 1.5 and rx < 1.5 and ew < 0.85
        if not ok:
            fail += 1
        log("%-10s 接缝比 Y %.2f→%.2f  X %.2f→%.2f  边亮度 %.3f  %s"
            % (name, b_ry, ry, b_rx, rx, ew, "OK" if ok else "★不合格"))

        save_rgb(np.clip(t, 0, 1), os.path.join(out_dir, "tex_%s.png" % name))

    log("写出 %d 张 → %s" % (len(SLOTS), out_dir))
    if fail:
        log("★ 有 %d 张没通过无缝自检（阈值 1.5）" % fail)
        raise SystemExit(1)
    if not args:
        try:
            os.remove(src)
        except OSError:
            pass
    log("DONE")


main()
