# -*- coding: utf-8 -*-
"""
秘境粒子图集 → 5 张独立 PNG（2026-10-16）

一张图集出 5 个粒子，比"每个粒子出一次图"省 4 次出图。
本脚本做：
  ① 白底 → 真透明（色距法，与 _battle_particle_matte.py 同一套口径）
  ② 按**内容列间隙**自动切成 5 个（不写死像素位置 —— 出图每次排布会有小偏移，
     写死就会切歪；靠"投影列空隙"切才对任何排布都稳）
  ③ 每个切片先裁到自身 bbox，再居中放进正方形画布（Unity 里 UGUI 粒子按像素尺寸缩放，
     非方形源图会被拉伸变形）

用法：python Tools/art/slice_realm_fx.py <图集> 
"""
import os
import sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DST_DIR = os.path.join(ROOT, "Assets", "Resources", "UI", "Realm")

# 顺序必须与出图提示词一致（左 → 右）
NAMES = [
    "FX_realm_marsh.png",    # ① 浮萍叶
    "FX_realm_ember.png",    # ② 余烬火星
    "FX_realm_gear.png",     # ③ 齿轮碎片
    "FX_realm_silent.png",   # ④ 尘埃球
    "FX_realm_thorn.png",    # ⑤ 枯叶屑
]

OUT_SIZE = 128       # 统一直径（正方形）
# 底部裁掉比例：水印在底部，粒子在画面中部 ⇒ 裁掉底边不影响内容
CROP_BOTTOM = 0.06


def main():
    src = sys.argv[1]
    im = Image.open(src).convert("RGB")
    w, h = im.size
    im = im.crop((0, 0, w, h - int(h * CROP_BOTTOM)))
    a = np.asarray(im).astype(np.float32)
    h, w, _ = a.shape
    print("src", src, "->", (w, h))

    # ---- 1. 色距 → alpha（白底透明）----
    # ⚠ 阈值必须先量再定：这张图集实测"纸底噪声 diff ≤ 20、内容 diff ≥ 66"，
    #   所以 lo 取 26（高于纸噪、远低于内容），hi 取 130。
    #   初版照抄了粒子图那套 lo=12/hi=90 —— 结果纸底的微弱纹理也被算成内容，
    #   切出来的"粒子"高度 900px（整张图的高度），是**先量阈值**才发现的。
    paper = np.array([247.0, 247.0, 247.0], np.float32)
    diff = np.abs(a - paper[None, None, :]).max(axis=2)
    lo, hi = 26.0, 130.0
    alpha = np.clip((diff - lo) / (hi - lo), 0.0, 1.0)
    print("alpha coverage = %.4f" % float((alpha > 0.08).mean()))

    mask = alpha > 0.08

    # ---- 2. 按列投影找 5 个连通块（列全空 = 分隔）----
    colsum = mask.sum(axis=0)
    runs = []
    inrun = False
    for x in range(w):
        if colsum[x] > 0 and not inrun:
            inrun = True; start = x
        elif colsum[x] == 0 and inrun:
            inrun = False; runs.append((start, x))
    if inrun:
        runs.append((start, w))

    # 合并/拆分到正好 5 段：过滤掉过窄的碎块（噪点），再按总宽取最多的 5 个
    runs = [r for r in runs if r[1] - r[0] >= max(4, w // 200)]
    print("column runs =", runs)
    if len(runs) < 5:
        print("ERROR: 只找到 %d 段（需要 5）—— 图集排布不合预期，请重出图或改切法" % len(runs))
        sys.exit(1)
    if len(runs) > 5:
        runs = sorted(runs, key=lambda r: r[1] - r[0], reverse=True)[:5]
        runs.sort(key=lambda r: r[0])
        print("取了最宽的 5 段:", runs)

    os.makedirs(DST_DIR, exist_ok=True)
    rgba = np.dstack([a, alpha * 255.0]).astype(np.uint8)

    for i, (x0, x1) in enumerate(runs):
        sub = rgba[:, x0:x1, :]
        sub_mask = mask[:, x0:x1]
        ys, xs = np.where(sub_mask)
        if len(xs) == 0:
            print("ERROR: 第 %d 段是空的" % i); sys.exit(1)
        # 裁到自身 bbox（留 2px 余量）
        pad = 2
        bx0 = max(0, xs.min() - pad); bx1 = min(sub.shape[1], xs.max() + pad + 1)
        by0 = max(0, ys.min() - pad); by1 = min(sub.shape[0], ys.max() + pad + 1)
        piece = Image.fromarray(sub[by0:by1, bx0:bx1, :], mode="RGBA")
        pw, ph = piece.size

        # ---- 3. 居中放进正方形画布（等比缩放到占 88%，四周留边）----
        scale = (OUT_SIZE * 0.88) / max(pw, ph)
        nw, nh = max(1, int(round(pw * scale))), max(1, int(round(ph * scale)))
        piece = piece.resize((nw, nh), Image.LANCZOS)
        canvas = Image.new("RGBA", (OUT_SIZE, OUT_SIZE), (0, 0, 0, 0))
        canvas.paste(piece, ((OUT_SIZE - nw) // 2, (OUT_SIZE - nh) // 2), piece)

        path = os.path.join(DST_DIR, NAMES[i])
        canvas.save(path, optimize=True)
        print("  -> %-22s  crop=%dx%d  scale=%.2f" % (NAMES[i], pw, ph, scale))


if __name__ == "__main__":
    main()
