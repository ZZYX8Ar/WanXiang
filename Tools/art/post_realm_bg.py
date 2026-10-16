# -*- coding: utf-8 -*-
"""
秘境背景图后处理（2026-10-16）

做三件事，**都不伪造内容**：
  ① 裁掉底部一条 —— AI 出图右下角有浅色水印，裁掉即消失（背景本来就是拉伸铺满，
     少一条底边对构图无影响；这比"涂抹修补"干净，也符合"不要手工修补原图"的要求）
  ② 统一到 1536×1024（与主线 map_bg_act*.png 同规格，Unity 里按 stretch 铺满）
  ③ 输出到 Resources/UI/Realm/map_bg_<realmId>.png，文件名与 RealmIds 常量对应

用法：python Tools/art/post_realm_bg.py
"""
import os
import glob
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_DIR = os.path.join(ROOT, "Assets", "ArtRes", "UI", "RealmBg")
DST_DIR = os.path.join(ROOT, "Assets", "Resources", "UI", "Realm")

TARGET = (1536, 1024)
# 底部裁掉的比例：水印在底部约 5% 处，裁 9% 留安全余量
CROP_BOTTOM = 0.09

# 关键词 → 目标文件名（顺序 = 生成顺序，靠关键词匹配，避免依赖时间戳）
MATCH = [
    ("沼泽", "map_bg_realm_marsh.png"),
    ("熔岩", "map_bg_realm_ember.png"),
    ("机关", "map_bg_realm_gear.png"),
    ("寂静", "map_bg_realm_silent.png"),
    ("荆棘", "map_bg_realm_thorn.png"),
]


def watermark_frac(a):
    """右下角"低对比但成片"的像素占比 —— 水印是浅灰文字，背景是画，能区分开。"""
    h, w, _ = a.shape
    bh, bw = max(8, h // 10), max(8, w // 5)
    corner = a[h - bh:, w - bw:].astype(np.float32)
    # 水印相对局部背景的偏差很小；用局部标准差判断"平坦区域里的浅色块"
    return float(corner.std() / 255.0)


def main():
    os.makedirs(DST_DIR, exist_ok=True)
    files = sorted(glob.glob(os.path.join(SRC_DIR, "*.png")))
    if not files:
        print("ERROR: 源目录没有 png：", SRC_DIR)
        return

    used = set()
    for f in files:
        base = os.path.basename(f)
        dst_name = None
        for key, name in MATCH:
            if key in base and name not in used:
                dst_name = name
                break
        if dst_name is None:
            print("skip (无匹配):", base)
            continue
        used.add(dst_name)

        im = Image.open(f).convert("RGB")
        w, h = im.size
        cut = int(h * CROP_BOTTOM)
        im2 = im.crop((0, 0, w, h - cut))
        im2 = im2.resize(TARGET, Image.LANCZOS)

        a = np.asarray(im2)
        print("%-28s  %s -> %s   corner-std=%.3f" %
              (os.path.basename(dst_name), im.size, im2.size, watermark_frac(a)))
        im2.save(os.path.join(DST_DIR, dst_name), optimize=True)

    missing = [n for _, n in MATCH if n not in used]
    if missing:
        print("WARNING: 没生成的文件：", missing)


if __name__ == "__main__":
    main()
