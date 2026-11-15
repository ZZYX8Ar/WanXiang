#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""万相 · 编队战力审计
校验 encounters.csv 每组的实际战力（只算能解析的敌人），按幕打印曲线。
用法：python WanXiang/Tools/encounter_audit.py [--strict]
  --strict：任一编队有无法解析的 id 或人数不足即非零退出（CI 用）
"""
import csv, io, os, statistics, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
DATA = os.path.join(ROOT, 'Assets', 'Resources', 'Data')

def load(name):
    p = os.path.join(DATA, name)
    if not os.path.exists(p): return []
    return list(csv.DictReader(io.open(p, encoding='utf-8-sig')))

def num(v, d=1.0):
    try: return float(v)
    except (TypeError, ValueError): return d

beasts = {r['id']: r for r in load('beasts.csv')}
bosses = {r['id']: r for r in load('boss.csv')}
enemies = {r['id']: r for r in load('enemies.csv')}

def power(uid):
    b = beasts.get(uid)
    if b: return int(num(b['hp'])) + int(num(b['atk']))*3 + int(num(b['def']))*2 + int(num(b['spd']))
    b = bosses.get(uid)
    if b: return int(num(b['hp'])) + int(num(b['atk']))*3 + int(num(b['def']))*2 + int(num(b['speed']))
    b = enemies.get(uid)
    if b: return int(num(b['hp'])) + int(num(b['atk']))*3 + int(num(b['def']))*2 + int(num(b['spd']))
    return None

def main():
    strict = '--strict' in sys.argv
    enc = load('encounters.csv')
    if not enc:
        print('encounters.csv 不存在'); return 0
    byact, problems = {}, []
    for e in enc:
        act = int(num(e['act']))
        scale = num(e['scale'], 1.0)
        units = [c.split('@')[0] for c in e['comp'].split(';') if c]
        ps = [(u, power(u)) for u in units]
        missing = [u for u, p in ps if p is None]
        ok = [p for _, p in ps if p is not None]
        if not ok: continue
        total = int(sum(ok) * scale)
        byact.setdefault(act, []).append((e['name'], len(units), len(ok), total, e['nodeKind']))
        if missing: problems.append('%s（第%s幕）缺 id: %s' % (e['name'], e['act'], ','.join(missing)))
        if len(ok) < 3: problems.append('%s（第%s幕）只解析出 %d 只' % (e['name'], e['act'], len(ok)))

    print('=== 编队战力曲线（按幕）===')
    for act in sorted(byact):
        rows = byact[act]
        nonboss = [r[3] for r in rows if r[4] != 'Boss']
        bossr = [r[3] for r in rows if r[4] == 'Boss']
        print('\n第 %d 幕（%d 组）' % (act, len(rows)))
        for n, cnt, okcnt, tot, kind in sorted(rows, key=lambda x: -x[3]):
            tag = ' ⚠人数不足' if okcnt < cnt else ''
            print('  %-10s %d/%d人 总%-6d %s%s' % (n, okcnt, cnt, tot, kind, tag))
        if nonboss:
            print('  → 非Boss 总战力 min/中位/max = %d / %d / %d'
                  % (min(nonboss), int(statistics.median(nonboss)), max(nonboss)))
        if bossr: print('  → Boss 总战力 %s' % bossr)

    if problems:
        print('\n!! 问题 %d 条：' % len(problems))
        for p in problems: print('  -', p)
    else:
        print('\n✅ 所有编队 id 均可解析，人数完整')
    return 1 if (strict and problems) else 0

if __name__ == '__main__':
    sys.exit(main())
