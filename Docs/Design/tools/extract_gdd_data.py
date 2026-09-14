#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从《万相 · 游戏设计文档 v1.0》单文件 HTML 里提取结构化数据，
产出 GDD 附录 A.3 承诺的五份数据文件：

    five_elements.json   五行定义 / 相生相克环 / 5x5 系数矩阵 / 相生羁绊 / 棋盘规则
    solar_terms.json     四幕结构 / 24 节气天时 / 三条季节规则 / 天气技
    beasts.json          30 只异兽（含技能数组与色区）
    palette.json         传统色板 / 描边规范 / 纹样库 / 色区定义 / 职业与稀有度

用法：
    python extract_gdd_data.py "<GDD html 路径>" "<输出目录>"

设计约定（与工程既有做法一致）：
  - 本脚本是**可重跑的**：GDD 更新后重跑即可覆盖产出，不做增量。
  - 脚本自带**自校验**：相生相克环与 5x5 系数矩阵必须自洽，
    不自洽就直接报错退出（宁可炸，也不要写出一份看起来没问题的错数据）。
  - 产出是**纯数据**，不含任何 Unity 依赖，可直接被 C# 侧的 JSON 读取器消费。
"""

import json
import os
import re
import sys

# ---------------------------------------------------------------- 基础工具


def strip_tags(s: str) -> str:
    """去掉 HTML 标签并归一化空白。"""
    s = re.sub(r'<br\s*/?>', '\n', s)
    s = re.sub(r'<[^>]+>', '', s)
    s = s.replace('&nbsp;', ' ').replace('&amp;', '&')
    s = s.replace('&lt;', '<').replace('&gt;', '>').replace('&quot;', '"')
    s = re.sub(r'[ \t]+', ' ', s)
    return s.strip()


def one(pattern: str, text: str, cast=str, default=None):
    """取第一个捕获组。找不到就返回 default（不抛错，方便容错）。"""
    m = re.search(pattern, text, re.S)
    if not m:
        return default
    return cast(m.group(1).strip())


def all_of(pattern: str, text: str):
    return re.findall(pattern, text, re.S)


def beast_head_preview(chunk: str) -> str:
    """报错时给一段可读的卡片头，方便定位是哪一只解析失败。"""
    nm = re.search(r'<span class="nm">(.*?)</span>', chunk)
    return strip_tags(nm.group(1)) if nm else strip_tags(chunk[:80])


# ---------------------------------------------------------------- 常量（GDD 第二章）

ELEMENTS = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
ELEMENT_CN = {'Wood': '木', 'Fire': '火', 'Earth': '土', 'Metal': '金', 'Water': '水'}

# 相克环：每一项克其下一项（木克土·土克水·水克火·火克金·金克木）
COUNTER_RING = ['Wood', 'Earth', 'Water', 'Fire', 'Metal']
# 相生环：每一项生其下一项（木生火·火生土·土生金·金生水·水生木）
GENERATE_RING = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']

# 5x5 伤害系数矩阵（行 = 攻击方，列 = 防守方），逐格抄自 GDD 2.3。
# 抄完之后由 verify_matrix() 用两个环反推校验，抄错会当场炸。
DAMAGE_MATRIX = {
    'Wood':  {'Wood': 0.90, 'Fire': 1.00, 'Earth': 1.50, 'Metal': 0.75, 'Water': 1.00},
    'Fire':  {'Wood': 1.00, 'Fire': 0.90, 'Earth': 1.00, 'Metal': 1.50, 'Water': 0.75},
    'Earth': {'Wood': 0.75, 'Fire': 1.00, 'Earth': 0.90, 'Metal': 1.00, 'Water': 1.50},
    'Metal': {'Wood': 1.50, 'Fire': 0.75, 'Earth': 1.00, 'Metal': 0.90, 'Water': 1.00},
    'Water': {'Wood': 1.00, 'Fire': 1.50, 'Earth': 0.75, 'Metal': 1.00, 'Water': 0.90},
}

COEF = {'counter': 1.50, 'countered': 0.75, 'same': 0.90, 'neutral': 1.00}


def verify_matrix():
    """用相克环 / 相生环反推矩阵，确认抄写无误。"""
    problems = []
    counter_pairs = set()
    for i, atk in enumerate(COUNTER_RING):
        counter_pairs.add((atk, COUNTER_RING[(i + 1) % 5]))
    generate_pairs = set()
    for i, src in enumerate(GENERATE_RING):
        generate_pairs.add((src, GENERATE_RING[(i + 1) % 5]))

    for atk in ELEMENTS:
        for dfn in ELEMENTS:
            v = DAMAGE_MATRIX[atk][dfn]
            if atk == dfn:
                want = COEF['same']
            elif (atk, dfn) in counter_pairs:
                want = COEF['counter']
            elif (dfn, atk) in counter_pairs:
                want = COEF['countered']
            else:
                want = COEF['neutral']
            if abs(v - want) > 1e-9:
                problems.append(
                    f'  矩阵[{atk}->{dfn}] = {v}，但按环应为 {want}')

    # 相生与相克不允许同时成立（否则规则本身矛盾）
    both = counter_pairs & generate_pairs
    if both:
        problems.append(f'  同时相生又相克的对：{both}')
    # 每个属性必须恰好克 1 个、生 1 个、被克 1 个、被生 1 个、同属 1 个
    for e in ELEMENTS:
        n_c = sum(1 for p in counter_pairs if p[0] == e)
        n_g = sum(1 for p in generate_pairs if p[0] == e)
        if n_c != 1 or n_g != 1:
            problems.append(f'  {e} 克 {n_c} 个、生 {n_g} 个（各应为 1）')

    if problems:
        raise SystemExit('❌ 五行环与系数矩阵不自洽，拒绝产出：\n' + '\n'.join(problems))
    print(f'  ✓ 自校验通过：5x5 矩阵与相生/相克环完全自洽'
          f'（相克 {len(counter_pairs)} 对、相生 {len(generate_pairs)} 对）')


# ---------------------------------------------------------------- 一、five_elements.json

BONDS = [
    {'from': 'Wood', 'to': 'Fire', 'name': '木火通明',
     'effect': '火属性单位暴击率 +12%，木属性单位技能 CD 推进 +1 每回合',
     'quote': '木生火，薪尽而火传。'},
    {'from': 'Fire', 'to': 'Earth', 'name': '火土相成',
     'effect': '土属性单位减伤 +15%，火属性单位造成的灼烧持续 +1 回合',
     'quote': '火焚而成灰，灰积而为土。'},
    {'from': 'Earth', 'to': 'Metal', 'name': '土金相生',
     'effect': '金属性单位破防穿透 +15%，土属性单位嘲讽时获得等量护盾',
     'quote': '金藏于矿，非土不生子。'},
    {'from': 'Metal', 'to': 'Water', 'name': '金水相涵',
     'effect': '水属性单位无视 15% 敌方抗性，金属性单位暴击伤害 +15%',
     'quote': '金气清凉，凝露为水。'},
    {'from': 'Water', 'to': 'Wood', 'name': '水木清华',
     'effect': '木属性单位治疗与护盾效果 +20%，水属性单位每回合回复 3% 生命',
     'quote': '水润其根，木乃向荣。'},
]

BOARD_RULES = [
    {'id': 'adjacencyGenerate', 'name': '相生相邻',
     'trigger': '任意两个相邻格（上下左右，不含对角）的单位五行相生',
     'effect': '双方每回合开始时回复 3% 最大生命，且各自获得 1 层「同气」（每层 +2% 技能效果，上限 5 层）',
     'regenPercentPerTurn': 0.03, 'qiPerTurn': 1, 'qiMax': 5, 'qiEffectPerStack': 0.02,
     'intent': '把「站位」从单纯的前后排，升级为属性层面的空间策略。'},
    {'id': 'adjacencyCounter', 'name': '相克相冲',
     'trigger': '任意两个相邻格的单位五行相克',
     'effect': '双方每回合开始时各受 2% 最大生命的真实伤害（无视护盾），并使双方怒气获取 -10%',
     'trueDamagePercentPerTurn': 0.02, 'rageGainDelta': -0.10,
     'intent': '这是「内耗」。它不强到毁掉阵容，但足以让一个随手摆的棋盘付出代价。'},
    {'id': 'centerEarth', 'name': '中宫土位',
     'trigger': '棋盘正中央格（第 2 行第 2 列，即「中宫」）',
     'effect': '站在中宫的单位额外获得「土德」：受到的所有伤害 -8%，且相邻四格的单位（无论什么属性）都不会触发相冲',
     'damageReduction': 0.08, 'suppressAdjacentCounter': True,
     'intent': '中宫是调停位。把矛盾的属性放在中宫旁边，冲突被平息；把肉盾放中宫，收益最大。'},
    {'id': 'sameElementResonance', 'name': '同属共鸣',
     'trigger': '同属性单位达到 2 / 4 / 5 只',
     'effect': '2 只：该属性单位 +8% 攻击；4 只：+16% 攻击并解锁该属性专属「共鸣技」；5 只：+25% 攻击，共鸣技 CD -2',
     'tiers': [
         {'count': 2, 'attackBonus': 0.08, 'unlockResonanceSkill': False, 'resonanceCdDelta': 0},
         {'count': 4, 'attackBonus': 0.16, 'unlockResonanceSkill': True, 'resonanceCdDelta': 0},
         {'count': 5, 'attackBonus': 0.25, 'unlockResonanceSkill': True, 'resonanceCdDelta': -2},
     ],
     'intent': '保留《闪烁之光》式羁绊的爽感，但把羁绊的骨架换成五行。'},
]

def build_five_elements() -> dict:
    counter_pairs = [{'attacker': COUNTER_RING[i], 'defender': COUNTER_RING[(i + 1) % 5]}
                     for i in range(5)]
    generate_pairs = [{'from': GENERATE_RING[i], 'to': GENERATE_RING[(i + 1) % 5]}
                      for i in range(5)]
    return {
        '$schema': 'wanxiang/five-elements@1',
        'source': '《万相 · 游戏设计文档 v1.0》第二章',
        'elements': ELEMENTS,
        'elementCn': ELEMENT_CN,
        'baseTable': [
            {'element': 'Wood', 'cn': '木', 'season': 'Spring', 'seasonCn': '春',
             'god': '句芒', 'godTitle': '青阳', 'direction': '东', 'color': '青',
             'theme': '生长 · 续航', 'means': '回复、护盾、复活、叠层成长'},
            {'element': 'Fire', 'cn': '火', 'season': 'Summer', 'seasonCn': '夏',
             'god': '祝融', 'godTitle': '朱明', 'direction': '南', 'color': '赤',
             'theme': '爆发 · 灼烧', 'means': '灼烧、暴击、连击、能量加速'},
            {'element': 'Earth', 'cn': '土', 'season': 'LongSummer', 'seasonCn': '长夏',
             'god': '后土', 'godTitle': '黄中', 'direction': '中', 'color': '黄',
             'theme': '守御 · 控制', 'means': '嘲讽、减伤、地形、驱散'},
            {'element': 'Metal', 'cn': '金', 'season': 'Autumn', 'seasonCn': '秋',
             'god': '蓐收', 'godTitle': '白藏', 'direction': '西', 'color': '白',
             'theme': '破防 · 暴击', 'means': '破甲、斩杀、标记、单体高伤'},
            {'element': 'Water', 'cn': '水', 'season': 'Winter', 'seasonCn': '冬',
             'god': '禺强', 'godTitle': '玄英', 'direction': '北', 'color': '黑',
             'theme': '控制 · 先手', 'means': '冻结、减速、穿透、先手改序'},
        ],
        'counterRing': COUNTER_RING,
        'generateRing': GENERATE_RING,
        'counterPairs': counter_pairs,
        'generatePairs': generate_pairs,
        'damageMatrix': DAMAGE_MATRIX,
        'damageMatrixCoefficients': COEF,
        'bonds': BONDS,
        'boardRules': BOARD_RULES,
        'board': {
            'columns': 3, 'rows': 3, 'maxDeployed': 5,
            'centerIndex': 4,
            'indexToPos': [{'index': i, 'row': i // 3, 'col': i % 3} for i in range(9)],
            'adjacencyIsOrthogonalOnly': True,
            'roleDefaultRow': {'Guard': 0, 'Caster': 1, 'Support': 1, 'Striker': 2, 'Swift': -1},
            'rowNames': ['前排', '中排', '后排'],
        },
        'designNotes': [
            '相生不参与伤害计算，只产出羁绊增益——避免同一体系既给伤害又给生存导致数值失控。',
            '同属性对轰给 0.90 而不是 1.00，避免「镜像阵容互相打不动」的拖沓局面。',
            '被克给 0.75 而不是 0.50，让属性克制成为「优势」而非「胜负手」。',
            '羁绊只认相生不认相克：刻意把相克属性凑一队不受惩罚，也不拿增益——配队是纯收益抉择。',
        ],
    }


# ---------------------------------------------------------------- 二、solar_terms.json

WEATHER_SKILLS = [
    {'id': 'prayRain', 'name': '祷雨', 'element': 'Water',
     'effect': '覆盖当前天时为「雨」：每回合全场回复 2% 生命，火属性伤害 -20%，冰蚀层数 +1/回合'},
    {'id': 'prayClear', 'name': '祈晴', 'element': 'Fire',
     'effect': '覆盖当前天时为「晴」：火属性伤害 +25%，全场每回合受 2% 灼烧，冰蚀层数 -2/回合'},
    {'id': 'summonWind', 'name': '召风', 'element': 'Metal',
     'effect': '覆盖当前天时为「风」：全场速度 +15%，命中率 -8%，远程单位伤害 +15%'},
    {'id': 'moveMountain', 'name': '移山', 'element': 'Earth',
     'effect': '覆盖当前天时为「尘」：减免所有 AOE 伤害 30%，我方受击时反弹 10% 伤害'},
]

SEASON_RULES = [
    {'id': 'rotation', 'name': '季节是「轮转」的，不是「静态摆放」的',
     'detail': '每一幕结束进入下一幕时，上一季的场地天时不会消失，而是以「余气」形式残留 2 个节点'
               '（强度减半），与新的季节天时叠加。',
     'carryOverNodes': 2, 'carryOverStrength': 0.5},
    {'id': 'override', 'name': '天时可被玩家的天气技「逆天改势」',
     'detail': '天气技覆盖当前场地天时，持续 3 回合后回归原天时。'
               '若覆盖引入的属性与当前节气本属性相克，则覆盖期间我方该属性单位受到额外 15% 伤害（「逆天时」）。',
     'overrideTurns': 3, 'adverseCostPercent': 0.15},
    {'id': 'pathLength', 'name': '每一幕的路径长度固定为 4 个节点',
     'detail': '每幕 6 个节气构成一张小型分叉图，玩家实际只经过 4 个节点即抵达守关。'
               '一局总战斗场次 16 场常规 + 5 场守关 = 21 场。',
     'nodesPerAct': 6, 'nodesVisitedPerAct': 4,
     'totalNormalBattles': 16, 'totalBossBattles': 5, 'totalBattles': 21},
]

SEASON_OF_ELEMENT = {'Wood': 'Spring', 'Fire': 'Summer', 'Metal': 'Autumn',
                     'Water': 'Winter', 'Earth': 'LongSummer'}


def parse_acts(html: str):
    """解析 §3.3 的逐幕逐节气。返回 (acts, terms)。"""
    # 每一幕从 `<h3 style="border-left-color:#XXX">第 N 幕 · 春 · 青阳</h3>` 开始
    act_heads = [(m.start(), strip_tags(m.group(1)))
                 for m in re.finditer(r'<h3 style="border-left-color:#\w+">(第 \d 幕[^<]*)</h3>', html)]
    if len(act_heads) != 5:
        raise SystemExit(f'❌ 期望解析到 5 幕，实际 {len(act_heads)} 幕')

    acts, terms = [], []
    for i, (start, title) in enumerate(act_heads):
        end = act_heads[i + 1][0] if i + 1 < len(act_heads) else html.find('3.4 天时覆盖')
        chunk = html[start:end]

        # 幕头表格里的字段
        fields = {}
        for a, b in re.findall(r'<td[^>]*>\s*<b>([^<]+)</b>\s*</td>\s*<td[^>]*>(.*?)</td>', chunk, re.S):
            fields[strip_tags(a)] = strip_tags(b)
        # 「幕章」是 colspan=3 的，单独抓
        mc = one(r'<td><b>幕章</b></td><td colspan="3">(.*?)</td>', chunk)
        act_meta = {
            'act': i + 1,
            'title': title,
            'element': next((e for e, cn in ELEMENT_CN.items() if cn == fields.get('五行', '')), None),
            'theme': fields.get('主题'),
            'difficulty': fields.get('难度定位'),
            'boss': fields.get('守关'),
            'bossSource': fields.get('典籍出处'),
            'chapter': strip_tags(mc) if mc else None,
            'declaredNodeCount': 6 if '6 个节气' in (fields.get('节点') or '') else 0,
        }
        acts.append(act_meta)

        for m in re.finditer(r'<div class="term( earth)?">(.*?)</div>\s*(?=<div class="term|</div>)',
                             chunk, re.S):
            is_earth, body = m.group(1), m.group(2)
            idx = one(r'<div class="idx">(\d+)</div>', body, int)
            name = one(r'<div class="tn">(.*?)</div>', body)
            ph = one(r'<div class="ph">.*?</span>(.*?)</div>', body)
            bf_name = one(r'<div class="bf"><b>(.*?)</b>', body)
            bf_desc = one(r'<div class="bf"><b>.*?</b><br>(.*?)</div>', body)
            hook = one(r'<div class="hk">▸ (.*?)</div>', body)
            ph = ph or ''
            parts = [p.strip() for p in ph.split('·', 1)]
            el_cn = parts[0]
            terms.append({
                'index': idx,
                'name': name,
                'act': i + 1,
                'isEarthNode': bool(is_earth),
                'element': next((e for e, cn in ELEMENT_CN.items() if cn == el_cn), None),
                'elementCn': el_cn,
                'phenology': parts[1] if len(parts) > 1 else None,
                'weatherName': strip_tags(bf_name or ''),
                'fieldBuff': strip_tags(bf_desc or ''),
                'hook': strip_tags(hook or ''),
            })

    if len(terms) != 24:
        raise SystemExit(f'❌ 期望 24 个节气，实际解析到 {len(terms)} 个')
    # 节气序号必须 1..24 且唯一
    if sorted(t['index'] for t in terms) != list(range(1, 25)):
        raise SystemExit('❌ 节气序号不是 1..24 的排列')
    return acts, terms


def build_solar_terms(html: str) -> dict:
    acts, terms = parse_acts(html)
    return {
        '$schema': 'wanxiang/solar-terms@1',
        'source': '《万相 · 游戏设计文档 v1.0》第三章',
        'acts': acts,
        'terms': terms,
        'seasonRules': SEASON_RULES,
        'weatherSkills': WEATHER_SKILLS,
    }


# ---------------------------------------------------------------- 三、beasts.json

RARITY_CN = {'灵品': 'Rare', '玄品': 'Epic', '神品': 'Legend'}
ROLE_CN = {'御': 'Guard', '攻': 'Striker', '术': 'Caster', '辅': 'Support', '疾': 'Swift'}
SKILLTYPE_CN = {'普攻': 'Basic', '战技': 'Active', '绝技': 'Ultimate'}
ROW_CN = {'前排': 0, '中排': 1, '后排': 2, '中后排': 1, '任意': -1}


def parse_beasts(html: str):
    """解析 §4.3 的异兽卡片。返回 (beasts, groups)。"""
    # 五行分组标题：<h3 style="border-left-color:#XXX"><span class="el" ...></span>五行 · 木
    #   <span class="small" ...>&nbsp;春 · 青阳 · 司掌句芒 · 共 6 种</span></h3>
    group_heads = [(m.start(), m) for m in re.finditer(
        r'<h3 style="border-left-color:#\w+"><span class="el"[^>]*></span>五行 · (.)', html)]
    if len(group_heads) != 5:
        raise SystemExit(f'❌ 期望 5 个五行分组，实际 {len(group_heads)} 个')

    cards = [(m.start(), m.group(0)) for m in re.finditer(r'<div class="beast" style="[^"]*">', html)]
    if len(cards) != 30:
        raise SystemExit(f'❌ 期望 30 只异兽，实际 {len(cards)} 只')

    beasts, groups = [], []
    # ⚠ 不能用 html.find('第五章') —— §4.3 引言里就有「详见第五章 5.4」，
    #   会把区间截到第一张卡片之前。用 section 锚点。
    beast_section_end = html.find('<section id="ch5">')
    if beast_section_end < 0:
        raise SystemExit('❌ 找不到 <section id="ch5">，无法界定异兽详表的范围')
    for gi, (gstart, gm) in enumerate(group_heads):
        gend = group_heads[gi + 1][0] if gi + 1 < len(group_heads) else beast_section_end
        groups.append({
            'elementCn': gm.group(1),
            'element': next((e for e, cn in ELEMENT_CN.items() if cn == gm.group(1)), None),
            'range': (gstart, gend),
        })

    for cstart, card_tag in cards:
        # 一只异兽的区块：到下一张卡片开头（最后一张到第五章开头）
        nxt = next((cs for cs, _ in cards if cs > cstart), None)
        cend = min(nxt or beast_section_end, beast_section_end)
        chunk = html[cstart:cend]

        grp = next((g for g in groups if g['range'][0] <= cstart < g['range'][1]), None)
        tags = all_of(r'<span class="tag"[^>]*>(.*?)</span>', chunk)
        tags = [strip_tags(t) for t in tags]
        if len(tags) < 4:
            raise SystemExit(f'❌ 卡片缺少标签：{chunk[:120]}')

        skills = []
        for sn, st, cd, desc in all_of(
                r'<div class="skill"><div class="sn">(.*?)</div><div class="st">(.*?)</div>'
                r'<div class="cd">(.*?)</div><div>(.*?)</div></div>', chunk):
            cd = strip_tags(cd)
            m_cd = re.search(r'(\d+)', cd)
            skills.append({
                'name': strip_tags(sn),
                'typeCn': strip_tags(st),
                'type': SKILLTYPE_CN.get(strip_tags(st)),
                'cd': int(m_cd.group(1)) if m_cd else 0,
                'description': strip_tags(desc),
            })

        # 色条是卡片最后一个元素。注意不能写 <div class="palrow">(.*?)</div>——
        # 每个色块自己是 <div style="background:#X">#X</div>，非贪婪会停在第一个内层 </div>。
        pi = chunk.find('class="palrow"')
        if pi < 0:
            raise SystemExit(f'❌ 找不到色条：{beast_head_preview(chunk)}')
        pal = re.findall(r'<div style="background:#([0-9A-Fa-f]{6})">', chunk[pi:pi + 800])
        if len(pal) != 5:
            raise SystemExit(f'❌ 色区不是 5 个：{pal} —— {beast_head_preview(chunk)}')

        quote = one(r'<div class="quote">(.*?)<span class="src">', chunk)
        src = one(r'<span class="src">(.*?)</span>', chunk)
        lore = one(r'<div class="quote">.*?</div>\s*<p class="small">(.*?)</p>', chunk)
        trait_name = one(r'<div class="trait"><span class="tn">特性 · (.*?)</span>', chunk)
        trait_desc = one(r'<div class="trait"><span class="tn">.*?</span><br>(.*?)</div>', chunk)

        beast = {
            'id': strip_tags(one(r'<span class="en">(.*?)</span>', chunk) or ''),
            'displayName': strip_tags(one(r'<span class="nm">(.*?)</span>', chunk) or ''),
            'element': grp['element'] if grp else None,
            'elementCn': grp['elementCn'] if grp else None,
            'rarityCn': tags[0], 'rarity': RARITY_CN.get(tags[0]),
            'roleCn': tags[2], 'role': ROLE_CN.get(tags[2]),
            'defaultRowCn': tags[3], 'defaultRow': ROW_CN.get(tags[3], -1),
            'source': strip_tags(src or ''),
            'quote': strip_tags(quote or ''),
            'lore': strip_tags(lore or ''),
            'trait': {'name': strip_tags(trait_name or ''), 'description': strip_tags(trait_desc or '')},
            'skills': skills,
            'codex': strip_tags(one(r'<div class="codex">(.*?)</div>', chunk) or ''),
            'palette': {
                'bodyMain': '#' + pal[0],
                'bodyAccent': '#' + pal[1],
                'energyGlow': '#' + pal[2],
                'eyeCore': '#' + pal[3],
                'outline': '#' + pal[4],
            },
        }
        beasts.append(beast)

    # 自校验：id 唯一、技能 3 个、五行各 6 只、稀有度各 10 只
    ids = [b['id'] for b in beasts]
    if len(set(ids)) != 30 or '' in ids:
        raise SystemExit('❌ 异兽 id 有重复或为空')
    bad = [b['displayName'] for b in beasts if len(b['skills']) != 3]
    if bad:
        raise SystemExit(f'❌ 以下异兽不是 3 个技能：{bad}')
    for e in ELEMENTS:
        n = sum(1 for b in beasts if b['element'] == e)
        if n != 6:
            raise SystemExit(f'❌ 五行 {e} 有 {n} 只（应为 6）')
    for r in RARITY_CN.values():
        n = sum(1 for b in beasts if b['rarity'] == r)
        if n != 10:
            raise SystemExit(f'❌ 稀有度 {r} 有 {n} 只（应为 10）')
    return beasts


def build_beasts(html: str, terms: list) -> dict:
    beasts = parse_beasts(html)
    return {
        '$schema': 'wanxiang/beasts@1',
        'source': '《万相 · 游戏设计文档 v1.0》第四章',
        'count': len(beasts),
        'elements': ELEMENTS,
        'rarities': ['Rare', 'Epic', 'Legend'],
        'roles': ['Guard', 'Striker', 'Caster', 'Support', 'Swift'],
        'skillTypes': ['Basic', 'Active', 'Ultimate'],
        'beasts': beasts,
    }


# ---------------------------------------------------------------- 四、palette.json

COLOR_REGION_DEF = [
    {'field': 'bodyMain', 'cn': '主体色', 'share': '45-55%',
     'description': '毛发 / 甲壳 / 鳞片的主色块', 'from': '宿主（Body）',
     'fusion': '直接采用宿主原色'},
    {'field': 'bodyAccent', 'cn': '纹样色', 'share': '20-30%',
     'description': '斑纹、羽尖、角饰、甲缝等次色块', 'from': '宿主（Body）',
     'fusion': '宿主原色，可被灵魂整体色相偏移 ±30°'},
    {'field': 'energyGlow', 'cn': '元气辉光', 'share': '10-15%',
     'description': '技能特效色、体表辉光、内描边色', 'from': '灵魂（Soul）',
     'fusion': '灵魂主色直接覆盖，权重最高（1.0）'},
    {'field': 'eyeCore', 'cn': '睛色', 'share': '1-3%',
     'description': '眼部发光核心', 'from': '灵魂（Soul）',
     'fusion': '灵魂高光色直接覆盖'},
    {'field': 'outline', 'cn': '描边色', 'share': '常量', 'description': '全局主描边',
     'from': '常量', 'fusion': '恒为 #2A2118，任何情况下不参与融合'},
]

ORNAMENTS = [
    {'name': '云雷纹', 'where': '顶层框架边框，幕间过场与章节标题底纹',
     'meaning': '商周青铜器底纹，最古老的中国纹样之一，营造「典籍感」'},
    {'name': '回纹', 'where': 'UI 分隔线、列表项前缀、加载动画进度条',
     'meaning': '连续不断的回旋，寓意轮回与四季轮转——呼应四幕循环结构'},
    {'name': '卷草纹', 'where': '卡片四角装饰、技能图标外框',
     'meaning': '唐代纹样，由忍冬纹演变，柔韧延绵，用于木属性的强势视觉区'},
    {'name': '缠枝莲', 'where': '品质光效底纹，玄品与神品卡面背景',
     'meaning': '绵延不断的藤蔓与莲花，表现「融合」这一核心动作的视觉隐喻'},
    {'name': '八角星', 'where': '神品单位脚下图腾光环、守关 Boss 出场特效',
     'meaning': '源自玉琮与太阳纹，最高稀有度专用标识，出现即意味着一场硬仗'},
    {'name': '篆书印章', 'where': '图鉴页落款，显示该异兽所属典籍卷次',
     'meaning': '方寸朱印，替代西式游戏的「等级徽章」——收集的是印章，不是星星'},
]

FORBIDDEN_COLORS = ['#000000', '#FFFFFF']


def parse_palette(html: str) -> dict:
    start = html.find('5.1 中国传统色板')
    end = html.find('5.4 索引化色区')
    chunk = html[start:end]

    groups, cur = [], None
    for m in re.finditer(r'<h4>(.*?)</h4>(.*?)(?=<h4>|$)', chunk, re.S):
        cur = {'name': strip_tags(m.group(1)), 'colors': []}
        for color, name, usage in all_of(
                r'<div class="chip" style="background:#([0-9A-Fa-f]{6})">.*?</div>\s*'
                r'<div class="info"><div class="n">(.*?)</div><div class="u">(.*?)</div></div>', m.group(2)):
            cur['colors'].append({'hex': '#' + color.upper(), 'name': strip_tags(name),
                                  'usage': strip_tags(usage)})
        if cur['colors']:
            groups.append(cur)

    total = sum(len(g['colors']) for g in groups)
    if total < 20:
        raise SystemExit(f'❌ 色板只解析到 {total} 个颜色，明显不对')

    # 描边规范（5.2 的表格）
    strokeTbl = html[html.find('5.2 描边规范'):html.find('5.3 纹样库')]
    strokes = []
    for a, b, c in all_of(r'<tr><td[^>]*>(.*?)</td><td[^>]*>(.*?)</td><td[^>]*>(.*?)</td></tr>', strokeTbl):
        strokes.append({'item': strip_tags(a), 'spec': strip_tags(b), 'note': strip_tags(c)})

    return {
        '$schema': 'wanxiang/palette@1',
        'source': '《万相 · 游戏设计文档 v1.0》第五章 + 美术资产生产方案 v1.0',
        'style': '扁平矢量绘本风（Flat Storybook） + 中国传统色',
        'styleRules': {
            'outline': '墨色 #2A2118，宽度 2px（以 512px 画布为基准），等宽，无抗锯齿模糊',
            'hardShadow': '偏移 (4px, 4px)，颜色为墨色 15% 透明度，无模糊',
            'forbiddenColors': FORBIDDEN_COLORS,
            'forbiddenEffects': ['渐变阴影', '写实材质反光', '锐化高光', '胶片颗粒'],
            'rationale': '全局禁用纯黑纯白：暗部走焦墨/玄色，高光走霜色/月白。'
                         '这是让截图一眼看去有「中国味」的最廉价手段。',
        },
        'colorGroups': groups,
        'strokeSpecs': strokes,
        'ornaments': ORNAMENTS,
        'colorRegions': COLOR_REGION_DEF,
        'fusionShaderSpec': {
            'step1': 'Art 阶段：每只异兽贴图导出为一张 RGB 遮罩图（R=bodyMain 区，G=bodyAccent 区，'
                     'B=energyGlow 区，A=不透明区），另加一张 eyeCore 小尺寸遮罩。禁止在贴图里烘焙颜色。',
            'step2': 'Shader 阶段：片元着色器按遮罩通道混合五个色区参数，'
                     '输出 = bodyMain*R + bodyAccent*G + energyGlow*B + eyeCore*eyeMask。',
            'step3': '融合阶段：宿主色区由 BeastConfig 直接提供；灵魂色区仅取 energyGlow 与 eyeCore，'
                     '覆写宿主对应参数，并按累加规则把 bodyAccent 做一次色相偏移。',
            'step4': '描边阶段：主描边用 A 通道膨胀算法绘制（等宽 2px），或由美术在贴图里画死——'
                     '前者省资源、后者更可控，原型期建议后者。',
            'value': '把「融合外观」从资产生产问题降级为数据问题：'
                     'N 张宿主遮罩图 + M 个灵魂色板 = N×M 种外观，资源量只有 N+M。',
        },
        'hueShiftDegrees': 30,
        'roles': [
            {'cn': '御', 'en': 'Guard', 'defaultRow': 0, 'rowName': '前排',
             'duty': '以承受伤害、嘲讽、保护后排为核心。通常血厚、攻低、具备一定减伤或反伤。'},
            {'cn': '攻', 'en': 'Striker', 'defaultRow': 2, 'rowName': '后排',
             'duty': '以单体高额爆发、斩杀、破防为核心。脆但输出高，需要御类保护。'},
            {'cn': '术', 'en': 'Caster', 'defaultRow': 1, 'rowName': '中排或后排',
             'duty': '以群体伤害、灼烧、冻结、场地交换为核心。技能覆盖面积大，单点弱。'},
            {'cn': '辅', 'en': 'Support', 'defaultRow': 1, 'rowName': '中排或后排',
             'duty': '以治疗、护盾、增益、复活、驱散为核心。输出极低但决定阵容上限。'},
            {'cn': '疾', 'en': 'Swift', 'defaultRow': -1, 'rowName': '任意一行',
             'duty': '以速度、连击、先手改序、多段攻击为核心。伤害单段不高，但出手频次极高。'},
        ],
        'rarities': [
            {'cn': '灵品', 'en': 'Rare', 'count': 10,
             'desc': '图鉴主体。易获得，是融合与技能草稿的消耗素材，也常有不可替代的工具性效果。'},
            {'cn': '玄品', 'en': 'Epic', 'count': 10,
             'desc': '流派核心。拥有一个明确的机制标签（灼烧 / 破甲 / 冻结 / 吞噬），围绕它构筑。'},
            {'cn': '神品', 'en': 'Legend', 'count': 10,
             'desc': '决定一局走向的答案。多数为五方神或四凶级存在，融合后会产生独一无二的组合。'},
        ],
    }


# ---------------------------------------------------------------- main

def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    src, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    html = open(src, encoding='utf-8').read()
    print(f'读入 GDD：{src}（{len(html)} 字符）')

    print('\n[1/4] five_elements.json')
    verify_matrix()
    five = build_five_elements()

    print('[2/4] solar_terms.json')
    terms = build_solar_terms(html)
    print(f'  ✓ {len(terms["acts"])} 幕 / {len(terms["terms"])} 节气 / '
          f'{len(terms["weatherSkills"])} 天气技')

    print('[3/4] beasts.json')
    beasts = build_beasts(html, terms['terms'])
    print(f'  ✓ {beasts["count"]} 只异兽（五行各 6、稀有度各 10、每只 3 技能）')

    print('[4/4] palette.json')
    palette = parse_palette(html)
    n_colors = sum(len(g['colors']) for g in palette['colorGroups'])
    print(f'  ✓ {len(palette["colorGroups"])} 个色组 / {n_colors} 个颜色 / '
          f'{len(palette["ornaments"])} 种纹样 / {len(palette["strokeSpecs"])} 条描边规范')

    for name, obj in [('five_elements', five), ('solar_terms', terms),
                      ('beasts', beasts), ('palette', palette)]:
        path = os.path.join(outdir, name + '.json')
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(obj, f, ensure_ascii=False, indent=2)
            f.write('\n')
        print(f'  → {path}（{os.path.getsize(path)} 字节）')

    print('\n✅ 完成。')


if __name__ == '__main__':
    main()
