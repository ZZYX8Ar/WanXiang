#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
万相 · 战力审计（B0-5）
口径与 Battle.Core/BattlePower.Of 一致：Power = HP + ATK*3 + DEF*2 + SPD
校验：
  1. 同职业内 Power 偏离中位数 <= 8%
  2. 面板字段齐全、为正
  3. trait_kind 是否已注册（未注册只是 warning，B1 补处理器即可）
用法：python WanXiang/Tools/power_audit.py
"""
import csv
import io
import os
import statistics
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
CSV = os.path.join(ROOT, 'Assets', 'Resources', 'Data', 'beasts.csv')

# B0 阶段 TraitRuntime.ResetToDefaults() 注册的 4 个处理器
REGISTERED = {
    'AdjacentMpBonus', 'CritRateTeam', 'ExecuteLowHp', 'BurnCapUp',
    'RageDownOnCrit', 'DamageToGuardUp', 'ShieldOnHurt',
    'DodgeWhenAdjacentFoe', 'PoisonOnCrit',
}
NUM_FIELDS = ('hp', 'atk', 'def', 'spd')
REQUIRED = ('id', 'name', 'element', 'role', 'rarity', 'temperament',
            'trait_kind', 'ultimate') + NUM_FIELDS


def main():
    if not os.path.exists(CSV):
        print('beasts.csv 不存在：%s（保持内置定义，无错误）' % CSV)
        return 0
    rows = list(csv.DictReader(io.open(CSV, encoding='utf-8-sig')))
    if not rows:
        print('beasts.csv 为空：保持内置定义')
        return 0

    errors, warnings = [], []
    groups = {}
    for i, r in enumerate(rows, start=2):
        for f in REQUIRED:
            if not r.get(f):
                errors.append('第 %d 行缺字段 %s' % (i, f))
        nums = {}
        for f in NUM_FIELDS:
            try:
                nums[f] = int(r.get(f) or 0)
                if nums[f] <= 0:
                    errors.append('第 %d 行 %s <= 0' % (i, f))
            except ValueError:
                errors.append('第 %d 行 %s 非法：%s' % (i, f, r.get(f)))
                nums[f] = 0
        p = nums['hp'] + nums['atk'] * 3 + nums['def'] * 2 + nums['spd']
        groups.setdefault(r.get('role', '?'), []).append((r.get('id'), p))
        tk = r.get('trait_kind')
        if tk and tk != 'None' and tk not in REGISTERED:
            warnings.append('第 %d 行 %s 天性「%s」尚未注册（B1 补处理器）' % (i, r.get('id'), tk))

    print('=== 万相 · 战力审计（%d 只）===' % len(rows))
    for role in sorted(groups):
        vals = [p for _, p in groups[role]]
        med = statistics.median(vals)
        print('  %-8s 中位 %6.0f  min %6d  max %6d  只数 %d' %
              (role, med, min(vals), max(vals), len(vals)))
        for bid, p in groups[role]:
            dev = abs(p - med) / max(1.0, med)
            if dev > 0.08:
                errors.append('%s(%s) Power=%d 偏离中位 %.1f%% > 8%%' % (bid, role, p, dev * 100))

    for w in warnings:
        print('  [warn] ' + w)
    print('---')
    if errors:
        for e in errors:
            print('  [ERROR] ' + e)
        print('结论：❌ %d 处问题' % len(errors))
        return 1
    print('结论：✅ 通过（同职业内离散度 <= 8%%，%d 条 warning 为 B1 待办）' % len(warnings))
    return 0


if __name__ == '__main__':
    sys.exit(main())
