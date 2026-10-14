# -*- coding: utf-8 -*-
"""从 Incompetech 目录里筛选契合《万相》的曲目（Kevin MacLeod, CC BY 4.0）
机器可读目录：https://incompetech.com/music/royalty-free/pieces.json
音频直链：    https://incompetech.com/music/royalty-free/mp3-royaltyfree/<filename>

授权：CC BY 4.0 —— 可商用，**必须署名 Kevin MacLeod**（可付费去除署名要求）。
用法：
  python _pick_incompetech.py list                  # 按预设三组关键词列出候选
  python _pick_incompetech.py search 关键词[,关键词]  # 自定义搜索
"""
import json, os, subprocess, sys

BASE = "https://incompetech.com/music/royalty-free/"
HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, "_inc_cache")

GROUPS = {
    "东方 / 中国风（场景、剧情、地图）": [
        "asian", "china", "chinese", "oriental", "japan", "japanese", "samurai", "ninja",
        "zen", "shaolin", "koto", "shamisen", "erhu", "guzheng", "taiko", "temple",
        "pagoda", "lotus", "dragon", "bamboo", "silk", "emperor", "dynasty",
        "kung fu", "wuxia", "martial", "geisha", "sakura",
    ],
    "战斗 / 史诗 / 首领": [
        "epic", "battle", "dark", "tension", "action", "conflict", "war", "hero",
        "power", "dramatic", "ominous", "suspense", "aggressive", "pursuit", "chase",
    ],
    "静谧 / 氛围（主界面、剧情、结局）": [
        "calm", "peace", "meditat", "ambient", "reflect", "sad", "melanch", "mystery",
        "gentle", "serene", "contemplat", "beautiful", "hope", "tender", "quiet",
    ],
}


def ensure(name):
    os.makedirs(CACHE, exist_ok=True)
    p = os.path.join(CACHE, name)
    if os.path.exists(p) and os.path.getsize(p) > 100:
        return p
    url = BASE + name
    print("  [下载] " + url)
    subprocess.run(["curl", "-sL", "--max-time", "120", "-A", "Mozilla/5.0", "-o", p, url], check=True)
    return p


def load(name):
    return json.load(open(ensure(name), encoding="utf-8"))


def haystack(p):
    parts = [str(p.get(k, "")) for k in ("title", "description", "instruments", "feel", "genre")]
    return " ".join(parts).lower()


def match(p, kws):
    h = haystack(p)
    return [k for k in kws if k in h]


def show(p, hits):
    print("  %-38s %-9s %-22s %4s %8s" % (
        p.get("title", "?")[:37], p.get("genre", "?"), p.get("feel", "?")[:21],
        p.get("bpm", "?"), p.get("length", "?")))
    print("      hits: %s" % ", ".join(hits[:6]))
    print("      file: %s" % p.get("filename", "?"))
    d = (p.get("description") or "").replace("\n", " ")
    if d:
        print("      %s" % d[:150])


def main():
    pieces = load("pieces.json")
    print("目录共 %d 首（CC BY 4.0，Kevin MacLeod）\n" % len(pieces))

    if len(sys.argv) > 2 and sys.argv[1] == "search":
        kws = [k.strip().lower() for k in sys.argv[2].split(",") if k.strip()]
        res = [(p, match(p, kws)) for p in pieces]
        res = [(p, h) for p, h in res if h]
        print("命中 %d 首：" % len(res))
        for p, h in res[:60]:
            show(p, h)
        return

    for gname, kws in GROUPS.items():
        res = [(p, match(p, kws)) for p in pieces]
        res = [(p, h) for p, h in res if h]
        res.sort(key=lambda x: -len(x[1]))
        print("=" * 90)
        print("【%s】命中 %d 首（按命中数排序，显示前 14）" % (gname, len(res)))
        print("=" * 90)
        for p, h in res[:14]:
            show(p, h)
        print()


if __name__ == "__main__":
    main()
