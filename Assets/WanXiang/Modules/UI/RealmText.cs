// ============================================================================
//  RealmText —— 场域文案的**唯一来源**（2026-10-16）
//  ---------------------------------------------------------------------------
//  为什么单独一个文件：场地效果是**结构化字段**（挂哪个状态 / 几层 / 几回合 / 多少护盾），
//  要拼成中文句子。秘境入口面板、秘境信息卡、调试工具三处都要显示它 ——
//  各写一份的结果是"入口说湿1层、信息卡说湿2层"这种**文案与实际生效规则不一致**，
//  而这恰恰是最难被发现的 bug（玩家按文案决策，系统按字段结算）。
//
//  ⛔ 加场域效果时只改这里；⛔ 不要在别处硬编码任何场域描述字符串。
// ============================================================================

namespace WanXiang.Modules.UI
{
    internal static class RealmText
    {
        /// <summary>场地效果一句话（按 <see cref="WanXiang.Campaign.RealmDef"/> 的实际字段生成）。</summary>
        public static string Hazard(WanXiang.Campaign.RealmDef r)
        {
            if (r == null) return "（无）";
            switch (r.Hazard)
            {
                case WanXiang.Campaign.RealmHazardKind.FoeStatusAll:
                    return "每回合敌方全体「" + StatusCn(r.HazardStatusId) + "」" +
                           r.HazardStacks + " 层";
                case WanXiang.Campaign.RealmHazardKind.FoeShieldAll:
                    return "敌方全体获得相当于施法者生命 " +
                           UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) + "% 的护盾";
                case WanXiang.Campaign.RealmHazardKind.SelfShieldAll:
                    return r.NoQte
                        ? "禁格挡 QTE；我方每回合获得 " +
                          UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) + "% 护盾（替代手段）"
                        : "我方每回合获得 " +
                          UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) + "% 护盾";
                default:
                    return string.IsNullOrEmpty(r.Concept) ? "（无）" : r.Concept;
            }
        }

        /// <summary>状态 id → 中文名。查不到就显示原 id（**不留空白**：空白会让人以为没效果）。</summary>
        public static string StatusCn(string id)
        {
            if (string.IsNullOrEmpty(id)) return "状态";
            var def = WanXiang.Battle.Core.StatusCatalog.Get(id);
            return !string.IsNullOrEmpty(def.Name) ? def.Name : id;
        }
    }
}
