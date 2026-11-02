#!/usr/bin/env python
# -*- coding: utf-8 -*-
# 把 ImageGen 生成的 4 张"圆形小世界"节点图裁成正方形+圆mask，输出为地图可用的节点 sprite。
#  - Resources/UI/Home/map_node_s1..s4.png（春/夏/秋/冬）
#  - ArtRes/UI/HomeMap/map_node_s1..s4.png
import os, glob
from PIL import Image, ImageDraw
import numpy as np

GEN = "D:/Unity_Project/MYRIAD/_美术工作区/generated-images"
# 按生成顺序对应 春/夏/秋/冬
order = [
    "游戏地图节点图标_一个圆形小世界_圆形轮廓内是一幅微缩的春日_2026-10-14T07-31-16.png",
    "游戏地图节点图标_一个圆形小世界_圆形轮廓内是夏日景致__浓_2026-10-14T07-31-55.png",
    "游戏地图节点图标_一个圆形小世界_圆形轮廓内是秋日景致__金_2026-10-14T07-32-23.png",
    "游戏地图节点图标_一个圆形小世界_圆形轮廓内是冬日景致__白_2026-10-14T07-32-50.png",
]

OUT_SIZE = 300

def make_node(src):
    im = Image.open(src).convert("RGBA")
    arr = np.array(im).astype(np.float32)
    h, w, _ = arr.shape
    # 背景色 = 四角均值；内容 = 与背景色距离超阈值
    corners = np.array([arr[0, 0, :3], arr[0, w - 1, :3], arr[h - 1, 0, :3], arr[h - 1, w - 1, :3]])
    bg = corners.mean(axis=0)
    dist = np.sqrt(((arr[:, :, :3] - bg) ** 2).sum(axis=2))
    m = dist > 20
    ys, xs = np.where(m)
    if len(xs) > 10:
        cx, cy = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
        rr = max(xs.max() - xs.min(), ys.max() - ys.min()) / 2
    else:
        cx, cy, rr = w / 2, h / 2, min(w, h) * 0.47
    x0, y0 = int(cx - rr), int(cy - rr)
    x1, y1 = int(cx + rr), int(cy + rr)
    im = im.crop((max(0, x0), max(0, y0), min(w, x1), min(h, y1))).resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS)
    # 圆形 alpha mask（柔边）
    yy, xx = np.mgrid[0:OUT_SIZE, 0:OUT_SIZE].astype(np.float32)
    r = OUT_SIZE * 0.5
    d = np.sqrt((xx - r) ** 2 + (yy - r) ** 2)
    a = np.clip((r - d) / 2.0, 0.0, 1.0)   # 2px 柔边
    o = np.array(im)
    o[:, :, 3] = (o[:, :, 3].astype(np.float32) * a).astype(np.uint8)
    return Image.fromarray(o, "RGBA")

ROOT = "D:/Unity_Project/MYRIAD/WanXiang"
d1 = os.path.join(ROOT, "Assets/ArtRes/UI/HomeMap")
d2 = os.path.join(ROOT, "Assets/Resources/UI/Home")
for dd in (d1, d2):
    os.makedirs(dd, exist_ok=True)

outs = []
for i, name in enumerate(order):
    src = os.path.join(GEN, name)
    if not os.path.exists(src):
        print("MISSING", src); continue
    node = make_node(src)
    node.save(os.path.join(d1, "map_node_s%d.png" % (i + 1)))
    node.save(os.path.join(d2, "map_node_s%d.png" % (i + 1)))
    outs.append(node)
    print("wrote map_node_s%d.png" % (i + 1))

# 预览表
if outs:
    sheet = Image.new("RGB", (len(outs) * 310 + 10, 320), (232, 226, 214))
    for i, im in enumerate(outs):
        sheet.paste(im, (10 + i * 310, 10), im)
    sheet.save(os.path.join(d2, "_nodes_preview.png"))
    print("preview written")
