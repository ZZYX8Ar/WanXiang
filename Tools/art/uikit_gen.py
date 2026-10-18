# -*- coding: utf-8 -*-
"""
万相 · UI 底版生成器（2026-10-18）

用户口径："简约风格、参考 MIO、有点数字科技的线条、看起来简约有高级"。

为什么**程序化画**而不是用 AI 出图：
  · 这类资产全是几何细线（圆角、1~2px 描边、角标），AI 出图会抖动、会歪、会带水印；
  · 9-slice 要求四边像素**逐行一致**，AI 图做不到（拉伸后接缝会跳）；
  · 程序画的线是像素级精确的，且可换配色一键重出。

输出：Assets/ArtRes/UI/Kit/<variant>/ 下的 png
  panel / card / button / divider / corner_ticks

用法：
  python uikit_gen.py            # 出两套配色
  python uikit_gen.py --preview  # 额外合成一张对比图
"""

import os
import sys
from PIL import Image, ImageDraw, ImageFilter

# ---------------------------------------------------------------- 配色
VARIANTS = {
    # 深色全息：MIO 那类冷色数字感
    "holo": {
        "fill":     (10, 18, 26, 214),
        "fill2":    (8, 14, 20, 150),
        "line":     (110, 226, 255, 200),
        "line_soft":(70, 150, 185, 90),
        "accent":   (110, 226, 255, 255),
        "glow":     (60, 170, 220, 60),
        "card_fill":(10, 18, 26, 46),
    },
    # 宣纸：与现有水墨立绘同框，但用细墨青线做出"仪器感"
    "paper": {
        "fill":     (238, 232, 218, 230),
        "fill2":    (246, 242, 232, 180),
        "line":     (44, 62, 62, 210),
        "line_soft":(44, 62, 62, 70),
        "accent":   (38, 92, 96, 235),
        "glow":     (38, 92, 96, 26),
        "card_fill":(255, 252, 244, 40),
    },
}

SS = 4          # 超采样倍数（先画大再缩小，得到抗锯齿细线）


def _canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def _down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)


def _rr(draw, box, r, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def panel(w, h, c, radius=10, border=1.4):
    """通用面板：半透明底 + 细描边 + 内缩一圈更淡的线 + 四角 L 形角标。"""
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    pad = 2 * SS
    box = [pad, pad, w * SS - pad, h * SS - pad]
    r = radius * SS

    _rr(d, box, r, fill=c["fill"], outline=c["line"], width=max(1, int(border * SS)))

    # 内缩淡线（离边框 7px）
    inset = 7 * SS
    b2 = [box[0] + inset, box[1] + inset, box[2] - inset, box[3] - inset]
    _rr(d, b2, max(2, r - inset), outline=c["line_soft"], width=max(1, int(1 * SS)))

    # 四角 L 形角标（贴边框外侧，长度 22px，线宽 2px）
    L = 22 * SS
    lw = max(1, int(2 * SS))
    for (x, y, dx, dy) in (
        (box[0], box[1], 1, 1), (box[2], box[1], -1, 1),
        (box[0], box[3], 1, -1), (box[2], box[3], -1, -1),
    ):
        d.line([(x + dx * 3 * SS, y), (x + dx * (L + 3 * SS), y)], fill=c["accent"], width=lw)
        d.line([(x, y + dy * 3 * SS), (x, y + dy * (L + 3 * SS))], fill=c["accent"], width=lw)

    # 顶部一条渐隐亮线（"通电"感）
    top = Image.new("RGBA", (w * SS, 3 * SS), (0, 0, 0, 0))
    td = ImageDraw.Draw(top)
    for i in range(w * SS):
        k = 1.0 - abs((i / (w * SS)) - 0.5) * 2.0
        a = int(120 * (k ** 1.6))
        td.line([(i, 0), (i, 1 * SS)], fill=(c["accent"][0], c["accent"][1], c["accent"][2], a))
    img.alpha_composite(top, (0, box[1] + 1 * SS))

    return _down(img, w, h)


def card(w, h, c):
    """小卡片（商品/列表项）：**近乎透明** —— 底下是立绘或文字，填色会把内容糊住
    （2026-10-18 实测：半透明底盖在商品立绘上像蒙了层灰）。只留细线框 + 角上小刻度。
    ⛔ 不要左侧竖色条：Sliced 拉伸后它会变成一条贯穿卡片的竖线（看起来像 bug）。"""
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    pad = 2 * SS
    box = [pad, pad, w * SS - pad, h * SS - pad]
    _rr(d, box, 8 * SS, fill=c["card_fill"], outline=c["line_soft"], width=max(1, int(1.2 * SS)))
    # 右上角小刻度
    for i in range(3):
        x = box[2] - (12 + i * 6) * SS
        d.line([(x, box[1] + 6 * SS), (x, box[1] + 12 * SS)], fill=c["line_soft"], width=max(1, SS))
    return _down(img, w, h)


def button(w, h, c, hover=False):
    """按钮：矩形 + 左右切角 + 细描边；悬停时描边更亮、底部有青色基线。"""
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    cut = 10 * SS
    pad = 2 * SS
    x0, y0, x1, y1 = pad, pad, w * SS - pad, h * SS - pad

    poly = [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
            (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    d.polygon(poly, fill=c["fill"])
    line = c["accent"] if hover else c["line"]
    d.line(poly + [poly[0]], fill=line, width=max(1, int(1.6 * SS)))
    # 底部基线
    d.line([(x0 + cut, y1 - 3 * SS), (x1 - cut, y1 - 3 * SS)],
           fill=(c["accent"][0], c["accent"][1], c["accent"][2], 120 if not hover else 220),
           width=max(1, int(2 * SS)))
    return _down(img, w, h)


def divider(w, h, c):
    """分隔线：一条细线 + 中央菱形节点，两端渐隐。"""
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    y = h * SS // 2
    cy = c["line_soft"]
    for i in range(w * SS):
        k = 1.0 - abs((i / (w * SS)) - 0.5) * 2.0
        a = int(200 * (k ** 1.2))
        d.line([(i, y), (i, y + max(1, SS))], fill=(c["line"][0], c["line"][1], c["line"][2], a))
    cx = w * SS // 2
    s = 5 * SS
    d.polygon([(cx, y - s), (cx + s, y), (cx, y + s), (cx - s, y)], fill=c["accent"])
    return _down(img, w, h)


def corner(w, h, c):
    """四角装饰（L 形 + 小点），给主界面/面板外用。"""
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    lw = max(1, int(2 * SS))
    L = (w - 6) * SS
    d.line([(3 * SS, 3 * SS), (3 * SS + L, 3 * SS)], fill=c["accent"], width=lw)
    d.line([(3 * SS, 3 * SS), (3 * SS, 3 * SS + (h - 6) * SS)], fill=c["accent"], width=lw)
    r = 3 * SS
    d.ellipse([3 * SS + L - r, 3 * SS - r, 3 * SS + L + r, 3 * SS + r], fill=c["accent"])
    return _down(img, w, h)


def build(out_root):
    made = []
    for name, c in VARIANTS.items():
        d = os.path.join(out_root, name)
        os.makedirs(d, exist_ok=True)
        jobs = [
            ("ui_panel",   512, 512, lambda w, h: panel(w, h, c)),
            ("ui_card",    256, 128, lambda w, h: card(w, h, c)),
            ("ui_button",  256,  72, lambda w, h: button(w, h, c, hover=False)),
            ("ui_button_hover", 256, 72, lambda w, h: button(w, h, c, hover=True)),
            ("ui_divider", 512,  20, lambda w, h: divider(w, h, c)),
            ("ui_corner",   96,  96, lambda w, h: corner(w, h, c)),
        ]
        for fn, w, h, f in jobs:
            p = os.path.join(d, fn + ".png")
            f(w, h).save(p)
            made.append(p)
    return made


def preview(paths, out):
    """把两套底版摆在深/浅两种面板底上，方便一眼比较。"""
    tiles = []
    for p in paths:
        im = Image.open(p).convert("RGBA")
        tiles.append((p, im))
    W = 360
    cols = 3
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (W * cols, W * rows), (60, 62, 68))
    for i, (p, im) in enumerate(tiles):
        s = min(1.0, W / max(im.size))
        t = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
        bg = Image.new("RGB", (W, W), (96, 98, 104))
        bg.paste(t, ((W - t.width) // 2, (W - t.height) // 2), t)
        sheet.paste(bg, ((i % cols) * W, (i // cols) * W))
    sheet.save(out)
    return out


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "out"
    made = build(root)
    print("生成 %d 张 -> %s" % (len(made), root))
    if "--preview" in sys.argv:
        print(preview(made, os.path.join(root, "_preview.png")))
