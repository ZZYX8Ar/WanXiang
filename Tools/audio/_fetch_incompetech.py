# -*- coding: utf-8 -*-
"""从 Incompetech 下载选定的曲目到工程 BGM 目录
作者：Kevin MacLeod（incompetech.com）  许可：CC BY 4.0（可商用，必须署名）
挑选依据：Incompetech 目录里 Far East Inspired 专辑 + Dark World 专辑 + 东方关键词命中曲，
          按《万相》五幕（春木/夏火/秋金/冬水/长夏土）与场景职能分配。
"""
import os, sys, urllib.request, urllib.parse, time

BASE = "https://incompetech.com/music/royalty-free/mp3-royaltyfree/"
DST = "D:/Unity_Project/MYRIAD/WanXiang/Assets/Resources/Audio/BGM"

PICKS = [
    # (原始文件名,            工程内 id,                     用途)
    ("Ripples.mp3",                    "bgm_km_ripples.mp3",          "古筝冥想 · 主界面/剧情/残卷"),
    ("Guzheng City.mp3",               "bgm_km_guzheng_city.mp3",     "古筝明亮 · 灵市/编队/图鉴"),
    ("Shenyang.mp3",                   "bgm_km_shenyang.mp3",         "二胡民俗 · 节点地图/旅途"),
    ("Senbazuru.mp3",                  "bgm_km_senbazuru.mp3",        "日本风温暖 · 剧情温情/同伴"),
    ("That Zen Moment.mp3",            "bgm_km_zen_moment.mp3",       "禅意 · 设置/档案/冥想"),
    ("Mountain Emperor.mp3",           "bgm_km_mountain_emperor.mp3", "太鼓 · 精英战/首领战"),
    ("The Snow Queen.mp3",             "bgm_km_snow_queen.mp3",       "神秘 · 第四幕冬/水"),
    ("Frost Waltz.mp3",                "bgm_km_frost_waltz.mp3",      "冰霜 · 第四幕冬/水"),
    ("Water Prelude.mp3",              "bgm_km_water_prelude.mp3",    "水 · 第四幕/玄溟溟鲲"),
    ("Temple of the Manes.mp3",        "bgm_km_temple_manes.mp3",     "暗黑神秘 · 首领/终局"),
    ("The Path of the Goblin King v2.mp3", "bgm_km_goblin_king.mp3",  "暗黑紧张 · 首领战"),
    ("Wizardtorium.mp3",               "bgm_km_wizardtorium.mp3",     "诡异神秘 · 天时/征兆/结局"),
    ("Clash Defiant.mp3",              "bgm_km_clash_defiant.mp3",    "战斗 · 普通战斗/精英"),
    ("Devastation and Revenge.mp3",    "bgm_km_devastation.mp3",      "史诗战斗 · 终局首领"),
]


def fetch(fn):
    url = BASE + urllib.parse.quote(fn)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (compatible; WanXiang audio pipeline)"})
    return urllib.request.urlopen(req, timeout=240).read()


def main():
    if not os.path.isdir(DST):
        os.makedirs(DST)
    ok = fail = 0
    for fn, out, desc in PICKS:
        p = os.path.join(DST, out)
        if os.path.exists(p) and os.path.getsize(p) > 10000:
            print("  [skip] %-28s 已存在" % out, flush=True)
            ok += 1
            continue
        t0 = time.time()
        try:
            data = fetch(fn)
            if len(data) < 10000:
                raise ValueError("文件过小，可能不是音频：%d bytes" % len(data))
            open(p, "wb").write(data)
            print("  [OK]   %-28s %7.2f MB  %4.1fs  %s" % (out, len(data) / 1048576.0, time.time() - t0, desc), flush=True)
            ok += 1
        except Exception as e:
            print("  [FAIL] %-28s %s" % (out, e), flush=True)
            fail += 1
    print("完成：成功 %d / 失败 %d" % (ok, fail), flush=True)


if __name__ == "__main__":
    main()
