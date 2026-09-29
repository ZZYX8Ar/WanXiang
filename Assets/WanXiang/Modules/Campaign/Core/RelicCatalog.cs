// ============================================================================
//  万相 · 遗物系统（杀戮尖塔式「变强」载体）
//  ---------------------------------------------------------------------------
//  设计缘起（用户 2026-09-29 实测反馈）：
//    局内零成长 —— 我方养成在出征前快照冻结，一局之内无论打多少节点属性都不再涨；
//    敌人又只随幕跳变（幕内恒定）。于是「没变强感 / 二幕打不过 / 5v5 先死即败」。
//  遗物是**局内可积累的被动增益**，正好补这块：每场胜利随机 3 选 1，精英/Boss 高掉率，
//  节点图也有专门的「遗物」节点。效果全部映射到已有战斗管线，不引入战斗核心改动：
//    · PlayerStatPct  → 折进 req.PlayerMul（全队 生命/攻击/防御 %）
//    · EnemyWeakPct   → 折进敌方 DeployEntry.StatMul
//    · ReviveOnce     → 复用 BattleFactory 的「复苏」虫卵机制（玩家单位阵亡 30% 复活 1 次）
//    · StartMana      → 战斗开局 TeamMp 直接 +N（更早放出战记/连携）
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>遗物折叠后的中立增益包（不依赖任何 UI / BattleRequest 类型）。
    /// 调用方（战斗请求工厂，处于 UI 程序集）负责把它映射到自己的 BattleRequest。</summary>
    public struct RelicMods
    {
        public float PlayerMul;        // 全队倍率，作用于 req.PlayerMul（默认 1）
        public float EnemyMul;         // 敌方倍率，作用于每个 DeployEntry.StatMul（默认 1）
        public bool ReviveOn;          // 复用「复苏」虫卵机制
        public float ReviveHpPercent;  // 复活后生命比例
        public int StartMana;          // 开局灵力增量

        public static RelicMods Identity()
            => new RelicMods { PlayerMul = 1f, EnemyMul = 1f, ReviveOn = false, ReviveHpPercent = 0.3f, StartMana = 0 };
    }

    /// <summary>遗物稀有度（决定掉落权重）。</summary>
    public enum RelicRarity
    {
        Common = 0,
        Rare = 1,
        Boss = 2,
    }

    /// <summary>遗物效果类型（决定挂到战斗管线的哪一处）。</summary>
    public enum RelicEffect
    {
        PlayerStatPct,   // 全队 生命/攻击/防御 +Value%
        EnemyWeakPct,    // 敌方属性 -Value%
        ReviveOnce,      // 我方全体阵亡以 30% 生命复活 1 次（复用「复苏」机制）
        StartMana,       // 战斗开局灵力 +Value
    }

    /// <summary>单条遗物定义（纯数据，可 JSON/序列化只读）。</summary>
    public sealed class RelicDef
    {
        public string Id;
        public string Name;
        public string Desc;
        public RelicRarity Rarity;
        public RelicEffect Effect;
        public float Value;        // 含义随 Effect：% 或 灵力点数

        public RelicDef(string id, string name, string desc, RelicRarity r, RelicEffect e, float v)
        {
            Id = id; Name = name; Desc = desc; Rarity = r; Effect = e; Value = v;
        }
    }

    public static class RelicCatalog
    {
        /// <summary>全部遗物（内容侧维护；新增遗物只改这里）。</summary>
        public static readonly RelicDef[] All = new[]
        {
            // ---- 普通（局内大部分节点掉）----
            new RelicDef("r_chiyu",   "赤羽符", "全队 生命/攻击/防御 +8%",        RelicRarity.Common, RelicEffect.PlayerStatPct, 8),
            new RelicDef("r_qingmu",  "青木符", "全队 生命/攻击/防御 +6%",        RelicRarity.Common, RelicEffect.PlayerStatPct, 6),
            new RelicDef("r_jifeng",  "疾风铃", "战斗开局灵力 +3（更早放战记）",  RelicRarity.Common, RelicEffect.StartMana, 3),
            new RelicDef("r_pojun",   "破军石", "敌方属性 -6%",                  RelicRarity.Common, RelicEffect.EnemyWeakPct, 6),
            new RelicDef("r_xuanwu",  "玄武甲", "全队 生命/防御 +10%",           RelicRarity.Common, RelicEffect.PlayerStatPct, 10),

            // ---- 稀有（精英战高掉率）----
            new RelicDef("r_zhenshan","镇岳印", "全队 生命/攻击/防御 +15%",       RelicRarity.Rare, RelicEffect.PlayerStatPct, 15),
            new RelicDef("r_hantie",  "寒铁魄", "敌方属性 -12%",                 RelicRarity.Rare, RelicEffect.EnemyWeakPct, 12),
            new RelicDef("r_jiuxiao", "九霄令", "战斗开局灵力 +6",              RelicRarity.Rare, RelicEffect.StartMana, 6),
            new RelicDef("r_changsheng","长生灯","我方全体阵亡以 30% 生命复活 1 次", RelicRarity.Rare, RelicEffect.ReviveOnce, 30),

            // ---- Boss（守关/天阙高掉率）----
            new RelicDef("r_taichu",  "太初斧", "全队 生命/攻击/防御 +20%",       RelicRarity.Boss, RelicEffect.PlayerStatPct, 20),
            new RelicDef("r_hundun",  "混沌珠", "敌方属性 -18%",                 RelicRarity.Boss, RelicEffect.EnemyWeakPct, 18),
            new RelicDef("r_lunhui",  "轮回镜", "复活 1 次 + 开局灵力 +4",       RelicRarity.Boss, RelicEffect.StartMana, 4) {
                // 备注：轮回镜的「复活」由 ReviveOnce 表达、灵力由 StartMana 表达，
                //        这里 Effect 取 StartMana 仅作主标签；真正复活在 ApplyRelics 里对 id 特判。
            },
        };

        private static readonly Dictionary<string, RelicDef> _byId = new Dictionary<string, RelicDef>();
        static RelicCatalog()
        {
            foreach (var d in All) _byId[d.Id] = d;
        }

        public static RelicDef Get(string id)
            => string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var d) ? null : d;

        /// <summary>
        /// 从池里随机抽 <paramref name="count"/> 个（按稀有度权重）。
        /// <paramref name="bossLike"/>=true（精英/Boss 节点）时提高 Rare/Boss 权重。
        /// 已拥有的遗物尽量不重复出现（池足够时）。
        /// </summary>
        public static List<RelicDef> Roll(int count, int act, bool bossLike, DeterministicRandom rng,
                                          ICollection<string> owned = null)
        {
            var pool = new List<RelicDef>();
            foreach (var d in All)
            {
                if (owned != null && owned.Contains(d.Id)) continue;   // 不重复给已持有
                pool.Add(d);
            }
            if (pool.Count == 0) pool.AddRange(All);   // 全收集完 → 允许重复

            int cCommon = bossLike ? 30 : 60;
            int cRare = bossLike ? 45 : 30;
            int cBoss = bossLike ? 25 : 10;

            var out_ = new List<RelicDef>();
            var used = new HashSet<string>();
            int guard = 0;
            while (out_.Count < count && guard++ < 200)
            {
                int roll = rng.NextInt(0, cCommon + cRare + cBoss);
                RelicRarity want = roll < cCommon ? RelicRarity.Common
                                 : roll < cCommon + cRare ? RelicRarity.Rare : RelicRarity.Boss;
                var cand = PickOfRarity(pool, want, rng, used);
                if (cand == null) cand = PickOfRarity(pool, RelicRarity.Common, rng, used); // 该稀有度抽空 → 降级
                if (cand == null) break;
                used.Add(cand.Id);
                out_.Add(cand);
            }
            return out_;
        }

        private static RelicDef PickOfRarity(List<RelicDef> pool, RelicRarity r,
                                             DeterministicRandom rng, HashSet<string> used)
        {
            int n = 0;
            foreach (var d in pool) if (d.Rarity == r && !used.Contains(d.Id)) n++;
            if (n == 0) return null;
            int k = rng.NextInt(0, n);
            foreach (var d in pool)
            {
                if (d.Rarity != r || used.Contains(d.Id)) continue;
                if (k-- == 0) return d;
            }
            return null;
        }

        /// <summary>
        /// 把一组已拥有遗物折叠成中立增益包（纯计算，不碰 BattleRequest / 存档）。
        /// 调用方负责把结果映射到自己的战斗请求结构：
        ///   m.PlayerMul   → req.PlayerMul（与既有局外加成相乘叠加）
        ///   m.EnemyMul    → 每个 req.EnemyEntries[i].StatMul
        ///   m.ReviveOn    → req.PlayerReviveOn（复用「复苏」虫卵机制）
        ///   m.StartMana   → req.PlayerStartMana
        /// </summary>
        public static RelicMods Accumulate(ICollection<string> ownedIds)
        {
            var m = RelicMods.Identity();
            if (ownedIds == null) return m;
            foreach (var id in ownedIds)
            {
                var d = Get(id);
                if (d == null) continue;
                switch (d.Effect)
                {
                    case RelicEffect.PlayerStatPct:
                        m.PlayerMul *= (1f + d.Value / 100f);
                        break;
                    case RelicEffect.EnemyWeakPct:
                        m.EnemyMul *= (1f - d.Value / 100f);
                        break;
                    case RelicEffect.ReviveOnce:
                        m.ReviveOn = true;
                        if (d.Value > 1) m.ReviveHpPercent = d.Value / 100f;
                        break;
                    case RelicEffect.StartMana:
                        m.StartMana += (int)System.Math.Round(d.Value);
                        break;
                }
                // 轮回镜特例：既是 StartMana 主标签，也附带一次复活
                if (id == "r_lunhui") m.ReviveOn = true;
            }
            return m;
        }
    }
}
