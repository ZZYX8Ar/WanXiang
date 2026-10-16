# -*- coding: utf-8 -*-
"""
秘境转场艺术字：白底 → 真透明 RGBA + 裁到内容 bbox（2026-10-16）

为什么这样做（而不是"修补/涂抹"）：
  · AI 出图带浅灰底 + 右下角水印。用户 2026-10-12 的要求是**不要手工修补原图**。
  · 所以这里只做两件**不伪造内容**的事：
      ① 色距法把浅底变透明（保留墨迹原本的灰度与飞白）
      ② 裁到墨迹的 bbox —— 水印在右下角，裁掉内容框之后自然不在图内
  · 若裁完仍残留水印像素，脚本会**报错退出**（不静默交付带水印的图）。

用法：python Tools/art/matte_realm_title.py <原图> <输出png>
"""
import os
import sys
import numpy as np
from PIL import Image


def main():
    src = sys.argv[1]
    dst = sys.argv[2]

    im = Image.open(src).convert("RGB")
    a = np.asarray(im).astype(np.float32)
    print("src", src, "size", im.size)

    # ---- 1. 取四边中位色作为"纸底"基准（比左上角单点稳，不会被角落阴影带偏）----
    h, w, _ = a.shape
    band = 6
    edges = np.concatenate([
        a[:band, :, :].reshape(-1, 3),
        a[-band:, :, :].reshape(-1, 3),
        a[:, :band, :].reshape(-1, 3),
        a[:, -band:, :].reshape(-1, 3),
    ], axis=0)
    paper = np.median(edges, axis=0)
    print("paper bg =", paper)

    # ---- 2. 色距 → alpha ----
    # 与纸底的"最大通道差"决定不透明度：墨越黑，差越大，alpha 越高。
    diff = np.abs(a - paper[None, None, :]).max(axis=2)

    # 软阈值：低于 lo 全透明（纸的噪点），高于 hi 全不透明（实墨），中间线性过渡。
    # ⚠ 毛笔字有大量**飞白/淡墨**，hi 不能太高，否则笔锋的丝会被切掉变成硬边。
    lo, hi = 10.0, 92.0
    alpha = np.clip((diff - lo) / (hi - lo), 0.0, 1.0)

    # ---- 3. 墨色保持原样，不做"重着色"（避免把淡墨强行拉黑，破坏墨韵）----
    rgb = a

    out = np.dstack([rgb, alpha * 255.0]).astype(np.uint8)

    # ---- 4. 裁到内容 bbox（含少量留白）----
    solid = alpha > 0.06
    ys, xs = np.where(solid)
    if len(xs) == 0:
        print("ERROR: 抠完什么都不剩（阈值不对？）")
        sys.exit(1)
    pad = 8
    x0 = max(0, xs.min() - pad); x1 = min(w, xs.max() + pad + 1)
    y0 = max(0, ys.min() - pad); y1 = min(h, ys.max() + pad + 1)
    print("content bbox =", (x0, y0, x1, y1), " -> ", (x1 - x0, y1 - y0))
    out = out[y0:y1, x0:x1]

    # ---- 5. 水印自检：右下角块若有"低不透明度但非零"的密集像素，说明水印被裁进来了 ----
    #    水印是浅灰文字 ⇒ 色距小 ⇒ alpha 低但成片。墨迹边缘也有低 alpha，但不会成块。
    oh, ow, _ = out.shape
    bh, bw = max(8, oh // 12), max(8, ow // 6)
    corner = out[oh - bh:, ow - bw:, 3].astype(np.float32) / 255.0
    frac = float((corner > 0.10).mean())
    print("bottom-right low-alpha coverage = %.4f" % frac)
    if frac > 0.35:
        print("ERROR: 右下角疑似残留水印（覆盖率 %.2f）—— 拒绝交付，请改 crop 或重出图" % frac)
        sys.exit(2)

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    Image.fromarray(out, mode="RGBA").save(dst, optimize=True)
    print("wrote", dst, out.shape)


if __name__ == "__main__":
    main()
