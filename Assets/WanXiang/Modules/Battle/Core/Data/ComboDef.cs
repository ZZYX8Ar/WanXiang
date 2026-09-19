// ============================================================================
//  连携技（ComboDef / ComboRules）—— 回合制 v2.1 P4
//  ---------------------------------------------------------------------------
//  设计（v2.1 §5 表）：两只特定异兽同时在场上 → 解锁双人合击。
//  消耗：双方各出 2 点灵力（合计 4，"贵但值"的选项）；每场每条限用一次。
//
//  本轮实现前 3 条（纯伤害/治疗，不需要新机制）：
//    青阳共鸣  句芒 + 木系   → 全体敌方木伤 + 我方全体回血 15%
//    炎海焚天  祝融 + 火系   → 全体敌方火伤
//    金戈断流  蓐收 + 金系   → 最前排单体 220% 金伤
//  其余（玄冥封锁需冻结状态、四季轮回需新 buff 等）待状态系统接入后补。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public enum ComboEffect
    {
        WoodPulse = 0,    // 全体敌方木伤 + 我方全体回血
        FireStorm = 1,    // 全体敌方火伤
        MetalBreak = 2,   // 最前排单体重击
    }

    public sealed class ComboDef
    {
        public string Id;
        public string Name;
        public string Note;
        /// <summary>主兽的 BeastDef.Id（由他发动）。</summary>
        public string HostId;
        /// <summary>伙伴需要达到的元素（"任何木属性异兽"）。</summary>
        public Element PartnerElement;
        /// <summary>双方各出的灵力。</summary>
        public int MpCost = 2;
        public ComboEffect Effect;
        /// <summary>伤害系数（× 主兽攻击）。</summary>
        public float Power = 1.2f;
    }

    public static class ComboRules
    {
        private static readonly ComboDef[] Table =
        {
            new ComboDef { Id = "combo_qingyang", Name = "青阳共鸣",
                Note = "对全体敌方造成木伤并为我方全体回复 15% 生命",
                HostId = "jumang", PartnerElement = Element.Wood,
                Effect = ComboEffect.WoodPulse, Power = 1.2f },
            new ComboDef { Id = "combo_yanhai", Name = "炎海焚天",
                Note = "对全体敌方造成火伤",
                HostId = "zhurong", PartnerElement = Element.Fire,
                Effect = ComboEffect.FireStorm, Power = 1.1f },
            new ComboDef { Id = "combo_jinge", Name = "金戈断流",
                Note = "对最前排单体造成 220% 金属性伤害",
                HostId = "rushou", PartnerElement = Element.Metal,
                Effect = ComboEffect.MetalBreak, Power = 2.2f },
        };

        public static System.Collections.Generic.IReadOnlyList<ComboDef> All => Table;

        public static ComboDef For(string id)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        /// <summary>
        /// 该单位此刻可以发动的连携（他是主兽 + 场上有对应元素的友方 + 双方灵力够）。
        /// </summary>
        public static System.Collections.Generic.List<ComboDef> AvailableFor(BattleState st, BattleUnit actor)
        {
            var list = new System.Collections.Generic.List<ComboDef>();
            if (actor == null || !actor.IsAlive || !actor.CanCastSkills) return list;

            for (int i = 0; i < Table.Length; i++)
            {
                var c = Table[i];
                if (actor.Def == null || actor.Def.Id != c.HostId) continue;      // 必须主兽发动
                if (st.TeamMp < c.MpCost) continue;                               // 自己的灵力要够

                BattleUnit partner = FindPartner(st, actor, c);
                if (partner == null) continue;
                if (st.TeamMp < c.MpCost * 2) continue;                           // 伙伴的灵力也要够
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// 为什么不能发动（返回 null = 可以发动）。给界面显示"差什么"，让玩家不用猜。
        /// </summary>
        public static string WhyNot(BattleState st, BattleUnit actor)
        {
            if (actor == null || actor.Def == null) return "没有待令单位";
            if (!actor.IsAlive) return "单位已阵亡";

            ComboDef matched = null;
            for (int i = 0; i < Table.Length; i++)
                if (actor.Def.Id == Table[i].HostId) { matched = Table[i]; break; }
            if (matched == null)
                return actor.DisplayName + " 不是任何连携的主兽（连携由句芒/祝融/蓐收发动）";

            if (st.UsedCombos.Contains(matched.Id)) return "本场已发动过「" + matched.Name + "」";
            var partner = FindPartner(st, actor, matched);
            if (partner == null)
                return "需要一名" + ElementName(matched.PartnerElement) + "属性的伙伴在场";
            if (st.TeamMp < matched.MpCost * 2)
                return "全队灵力不足 " + (matched.MpCost * 2) + " 点（当前 " + st.TeamMp + "）";
            return null;      // 可以发动
        }

        private static string ElementName(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return e.ToString();
            }
        }

        /// <summary>找连携伙伴：其他友方中第一个元素匹配且活着的。</summary>
        public static BattleUnit FindPartner(BattleState st, BattleUnit actor, ComboDef c)
        {
            var ours = st.UnitsOf(actor.Side);
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (u == actor || !u.IsAlive || u.Def == null) continue;
                if (u.Def.Element == c.PartnerElement) return u;
            }
            return null;
        }
    }
}
