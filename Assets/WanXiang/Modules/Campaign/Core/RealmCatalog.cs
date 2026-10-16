// ============================================================================
//  秘境（Realm）—— 2026-10-16《精英首领与秘境设计 v1.0》第 5~7 章
//  ---------------------------------------------------------------------------
//  定位：**主线途中的高风险支线**，不是独立的第二款游戏。三条硬约束：
//    ① 产出直接进本局背包，**不进图鉴**（这是它区别于独立肉鸽模式的关键）
//    ② 打完**必回到原路线图同一节点**，路线图状态完全保留
//    ③ 每局上限 2 次 —— 它是「选择」不是「日常」
//
//  ★ 为什么它便宜：秘境**不新增结算路径**，只是三处已有机制的参数组合：
//    · enemyScale → 乘在 DeployEntry.StatMul（既有的敌方倍率通道）
//    · 场地效果   → 仿WeatherTurnStartHooks 的回合始钩子
//    · 禁 QTE     → 已有 ParryEnabled 开关（关 QTE 是零副作用路径）
//
//  ⚠ 最容易漏的一步是**回滚**（见RollbackLines）：场域会改临时状态，
//    返回时必须逐条还原，否则玩家带着「速度 -10%」回主线且再也不会消失。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>场域 id（用稳定字符串，便于存档与自检）。</summary>
    public static class RealmIds
    {
        public const string Marsh = "realm_marsh";    // 苍泽
        public const string Ember = "realm_ember";    // 烬岭
        public const string Gear = "realm_gear";      // 机枢
        public const string Silent = "realm_silent";  // 寂域
        public const string Thorn = "realm_thorn";    // 荆墟
    }

    /// <summary>场地效果的落点（回合始给谁挂什么）。</summary>
    public enum RealmHazardKind
    {
        None = 0,
        /// <summary>给**敌方全体**挂某状态（苍泽：湿；烬岭：灼烧）。</summary>
        FoeStatusAll = 1,
        /// <summary>给**敌方全体**加一层护盾（机枢）。</summary>
        FoeShieldAll = 2,
        /// <summary>给**我方全体**每回合加护盾（寂域禁 QTE 的替代）。</summary>
        SelfShieldAll = 3,
    }

    public sealed class RealmDef
    {
        public string Id;
        public string Name;
        public string Element;              // 五行（仅用于展示与配色）
        public string Concept;              // 一句话

        // ---- 强度 ----
        public int Waves;                    // 波数
        public float EnemyScale;            // 敌方强度倍率（**不吃幕数加成**）
        public float StarRate;              // 产出倍率

        // ---- 场地效果 ----
        public RealmHazardKind Hazard;
        public string HazardStatusId;       // 场地挂的状态 id
        public int HazardStacks = 1;
        public int HazardTurns = 2;
        public float HazardShieldPct = 0f;  // 护盾按施法者最大生命 x%

        // ---- 规则 ----
        public bool NoQte;                  // 禁格挡 QTE（寂域）
        public bool BanHealRelics;          // 禁回复类遗物（烬岭）
        public int AppearFromAct = 1;       // 最早出现幕
        public int Weight = 20;             // 出现权重

        public string Boon;                 // 收益（展示）
        public string Cost;                 // 代价（展示）
        public string[] Rooms;              // 地图分区名（展示 / 后续做地图用）
        public string Palette;              // 主题色（#RRGGBB）

        public bool AvailableIn(int act)
        {
            return act >= AppearFromAct;
        }
    }

    public static class RealmCatalog
    {
        public const int MaxPerRun = 2;      // 每局上限（硬约束 ③）

        private static readonly RealmDef[] Table =
        {
            new RealmDef {
                Id = RealmIds.Marsh, Name = "苍泽", Element = "水",
                Concept = "积水腐草，减速 + 持续伤害，但掉落最好",
                Waves = 3, EnemyScale = 1.00f, StarRate = 1.5f,
                Hazard = RealmHazardKind.FoeStatusAll,
                HazardStatusId = WanXiang.Battle.Core.StatusCatalog.Wet,
                HazardStacks = 1, HazardTurns = 2,
                // ⚠ 设计文档 5.3 的表格写「1 幕起」，但同节的**规则**写「第 1 幕不出现」。
                //   这里取**规则**（更硬，且理由充分：玩家没学会格挡就进秘境是纯挫败）。
                AppearFromAct = 2, Weight = 30,
                Boon = "结算产出 1.5 倍",
                Cost = "全程速度 -10%，且不能回城补给",
                Rooms = new[] { "泥沼浅滩", "沉木林", "毒瘴洼地" },
                Palette = "#2F4A45",
            },
            new RealmDef {
                Id = RealmIds.Ember, Name = "烬岭", Element = "火",
                Concept = "地表开裂，灼烧常驻，逼你速战速决",
                Waves = 4, EnemyScale = 1.15f, StarRate = 1.2f,
                Hazard = RealmHazardKind.FoeStatusAll,
                HazardStatusId = WanXiang.Battle.Core.StatusCatalog.Burn,
                HazardStacks = 1, HazardTurns = 2,
                AppearFromAct = 2, Weight = 25,
                Boon = "每击败 1 波，全体回血 8% 最大生命",
                Cost = "不可使用任何回复类遗物",
                Rooms = new[] { "焦岩台", "熔痕谷", "灰烬海" },
                Palette = "#5A2A20",
            },
            new RealmDef {
                Id = RealmIds.Gear, Name = "机枢", Element = "金",
                Concept = "废弃机关城，敌人有护盾与机械减伤",
                Waves = 4, EnemyScale = 1.20f, StarRate = 1.3f,
                Hazard = RealmHazardKind.FoeShieldAll,
                HazardShieldPct = 0.15f, HazardTurns = 2,
                AppearFromAct = 3, Weight = 20,
                Boon = "每场战斗后可重铸 1 个技能（免费，1 次/秘境）",
                Cost = "敌方多 1 只单位，但玩家只打 3 场",
                Rooms = new[] { "齿轮回廊", "校准间", "核心舱" },
                Palette = "#3C4654",
            },
            new RealmDef {
                Id = RealmIds.Silent, Name = "寂域", Element = "无",
                Concept = "无声之地，禁止一切 QTE —— 纯数值硬仗",
                Waves = 3, EnemyScale = 1.35f, StarRate = 1.8f,
                Hazard = RealmHazardKind.SelfShieldAll,
                HazardShieldPct = 0.08f, HazardTurns = 3,
                NoQte = true,
                AppearFromAct = 4, Weight = 10,
                Boon = "通关额外给 2 个神品级灵魂",
                Cost = "不能格挡 ⇒ 只能靠站位、护盾与治疗扛伤害",
                Rooms = new[] { "无声回廊", "哑石庭", "终点" },
                Palette = "#2B2530",
            },
            new RealmDef {
                Id = RealmIds.Thorn, Name = "荆墟", Element = "木",
                Concept = "赌坊型秘境：结算可加倍，但失败代价极高",
                Waves = 2, EnemyScale = 1.60f, StarRate = 2.2f,
                Hazard = RealmHazardKind.None,
                AppearFromAct = 2, Weight = 15,
                Boon = "星币产出 2.2 倍；第 2 波结束可选择「再赌一次」",
                Cost = "自选惩罚不可拒绝；全队血量 <50% 时不会弹出",
                Rooms = new[] { "荆门", "赌桌", "收账处" },
                Palette = "#4A4330",
            },
        };

        public static IReadOnlyList<RealmDef> All => Table;

        public static RealmDef For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        /// <summary>
        /// 按幕数抽一个可用的场域（确定性：只吃 act + seed，同局可复盘）。
        /// ⛔ 排除本局已经进过的（同局去重），也排除没到出现幕的。
        /// </summary>
        public static RealmDef Roll(int act, ulong seed, List<string> alreadyUsed)
        {
            int total = 0;
            var pool = new List<RealmDef>(Table.Length);
            for (int i = 0; i < Table.Length; i++)
            {
                var r = Table[i];
                if (!r.AvailableIn(act)) continue;
                if (alreadyUsed != null && alreadyUsed.Contains(r.Id)) continue;
                // 荆墟不劝死：全队血量过低时由调用方决定不弹，这里只管出现幕
                pool.Add(r);
                total += r.Weight;
            }
            if (pool.Count == 0 || total <= 0) return null;

            int roll = new WanXiang.Battle.Core.DeterministicRandom(
                seed ^ WanXiang.Battle.Core.CoreMath.Fnv1a("realm:" + act)).NextInt(0, total);
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= pool[i].Weight;
                if (roll < 0) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}