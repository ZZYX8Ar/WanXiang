# -*- coding: utf-8 -*-
"""
万相 · 程序化素材生成（2026-10-16）

项目约定：**重复/噪声类素材程序化生成**，不占用 AI 出图额度，
且必须可复现（脚本落盘，随时重跑）。

本脚本产出：
  · Assets/Resources/UI/Realm/reveal_noise.png   —— 转场艺术字"洇开"用的噪声图
    要求：**无缝可平铺**（边缘不能有接缝，否则揭示边界会出现一条直线）、灰度、
    中等对比（太糊则边缘圆滑无墨感，太锐则像锯齿）。

用法：
  python tools/gen_realm_assets.py
"""

import os
import numpy as np
from PIL import Image

# 脚本在 <工程>/Tools/art/ ⇒ 上溯两层才是工程根（WanXiang/）
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "UI", "Realm")


def value_noise(size, cells, rng):
    """格点值噪声：在 cells×cells 的格点上取随机值，双三次插值到 size×size。"""
    g = rng.random((cells + 1, cells + 1)).astype(np.float32)
    # 无缝：把最后一列/行复制成第一列/行，保证平铺时左右上下相接
    g[-1, :] = g[0, :]
    g[:, -1] = g[:, 0]
    img = Image.fromarray((g * 255).astype(np.uint8), mode="L")
    img = img.resize((size, size), Image.BICUBIC)
    return np.asarray(img).astype(np.float32) / 255.0


def fbm(size, rng, octaves=5, base_cells=3):
    """分形叠加：每层格点数翻倍、振幅减半 —— 得到自然的墨迹粗细混合。"""
    out = np.zeros((size, size), np.float32)
    amp = 1.0
    total = 0.0
    cells = base_cells
    for _ in range(octaves):
        out += value_noise(size, cells, rng) * amp
        total += amp
        amp *= 0.5
        cells *= 2
    out /= total
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    # 固定种子 ⇒ 可复现（每次生成的噪声图完全一致，避免"改了别的东西噪声也变了"）
    rng = np.random.default_rng(20261016)

    size = 512
    n = fbm(size, rng, octaves=5, base_cells=3)

    # 对比增强：把中段拉开 —— 噪声 shader 里是 (noise-0.5)*strength，
    # 中段拉得越开，揭示边缘的"墨齿"越明显。0.5 处保持中性不动。
    n = np.clip((n - 0.5) * 1.35 + 0.5, 0.0, 1.0)

    arr = (n * 255).astype(np.uint8)
    path = os.path.join(OUT_DIR, "reveal_noise.png")
    Image.fromarray(arr, mode="L").save(path, optimize=True)
    print("wrote", path, arr.shape, "min=%.3f max=%.3f mean=%.3f" % (n.min(), n.max(), n.mean()))

    # ---- 抖动检查：平铺接缝 ----
    # 左右两列、上下两行的平均差应当很小，否则揭示边界会出现直线。
    lr = np.abs(arr[:, 0].astype(int) - arr[:, -1].astype(int)).mean()
    tb = np.abs(arr[0, :].astype(int) - arr[-1, :].astype(int)).mean()
    print("seam check  left/right=%.2f  top/bottom=%.2f  (越小越无缝)" % (lr, tb))

    gen_silent_dust(OUT_DIR)


def gen_silent_dust(out_dir):
    """寂域（尘埃）粒子：**程序化**画一颗柔边尘点。

    为什么不从 AI 图集里切：
      出图提示词要的是"几乎透明的灰紫色尘埃球"，抠图阈值（防纸底噪声）一卡，
      这颗几乎全被抹掉 —— 实测只剩 **3 个不透明像素**，等于没有。
      而它本身就是一个径向渐变圆，程序化画反而更干净、可复现、零成本。

    ⚠ 寂域的主题是"静止"，所以这颗要**很淡**（中心 alpha ≈ 0.55），
      不能画成实心白点 —— 那会在灰紫背景上跳成"雪花"。
    """
    size = 128
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    cx = cy = (size - 1) * 0.5
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (size * 0.5)

    # 柔边：中心 1 → 边缘 0，用 smoothstep 收口，避免硬圆盘
    a = np.clip(1.0 - d, 0.0, 1.0)
    a = a * a * (3.0 - 2.0 * a)          # smoothstep
    a *= 0.55                            # 整体压低（"几乎透明"）

    # 颜色：灰紫（比纯灰多一点冷调，跟寂域的 #2B2530 呼应）
    rgb = np.zeros((size, size, 3), np.float32)
    rgb[:, :, 0] = 0.62
    rgb[:, :, 1] = 0.60
    rgb[:, :, 2] = 0.68

    out = np.dstack([rgb * 255.0, a * 255.0]).astype(np.uint8)
    path = os.path.join(out_dir, "FX_realm_silent.png")
    Image.fromarray(out, mode="RGBA").save(path, optimize=True)
    print("wrote %s (procedural dust)  maxAlpha=%.2f" % (path, a.max()))


if __name__ == "__main__":
    main()
