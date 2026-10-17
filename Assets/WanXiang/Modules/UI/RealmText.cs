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
        /// <summary>
        /// 「场地」一行：**我方/敌方 + 机制 + 为什么疼**。
        /// <para>⭐ 2026-10-17 用户："把负面效果说清楚"。只写"我方全体每回合「湿」1 层"，
        ///   玩家读不出**这对我有多糟** —— 湿到底会怎样？所以每条都要带后果。</para>
        /// <para>⛔ 每条都必须能对上真实实现（<see cref="WanXiang.Campaign.RealmHazards"/>）。
        ///   不要在这里写"未实现的后果"，那只是把假文案换个地方放。</para>
        /// </summary>
        public static string Hazard(WanXiang.Campaign.RealmDef r)
        {
            if (r == null) return "（无）";
            switch (r.Hazard)
            {
                // ★ 场地效果一律打我方（2026-10-17 用户纠正）。
                case WanXiang.Campaign.RealmHazardKind.PlayerStatusAll:
                    return "我方全体每回合「" + StatusCn(r.HazardStatusId) + "」" +
                           r.HazardStacks + " 层 —— " + StatusWhy(r.HazardStatusId);

                case WanXiang.Campaign.RealmHazardKind.FoeStatusAll:
                    return "敌方全体每回合「" + StatusCn(r.HazardStatusId) + "」" +
                           r.HazardStacks + " 层";

                case WanXiang.Campaign.RealmHazardKind.FoeShieldAll:
                    return "敌方全体每回合获得其生命 " +
                           UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) +
                           "% 的护盾 —— 护盾先吃伤害，同样的输出打不穿";

                case WanXiang.Campaign.RealmHazardKind.SelfShieldAll:
                    return r.NoQte
                        ? "禁格挡 QTE；我方每回合获得 " +
                          UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) +
                          "% 护盾 —— 这是禁格挡的替代手段，不是白送"
                        : "我方每回合获得 " +
                          UnityEngine.Mathf.RoundToInt(r.HazardShieldPct * 100f) + "% 护盾";

                default:
                    // None：别留空，空着会让玩家以为"没看清"。
                    return "无场地危害 —— 这一域的危险全在赌注上（见代价）";
            }
        }

        /// <summary>
        /// 「代价」一行：**从真实字段推导**，不读手写字符串。
        /// <para>⭐ 这样它不可能与实现脱节。原来 `RealmDef.Cost` 是手写文案，
        ///   里面写着"不能回城补给"——而本工程里**根本没有回城补给这个系统**，
        ///   减速也没有任何代码读取，等于糊在界面上的假字（用户："回城补给是什么鬼"）。</para>
        /// </summary>
        public static string Cost(WanXiang.Campaign.RealmDef r)
        {
            if (r == null) return "（无）";

            var parts = new System.Collections.Generic.List<string>();

            // ① 我方减速：唯一真正生效的"全程"代价
            if (r.PlayerSpeedMul < 0.999f)
            {
                int pct = UnityEngine.Mathf.RoundToInt((1f - r.PlayerSpeedMul) * 100f);
                parts.Add("我方全体速度 -" + pct + "%（出手更靠后，容易被敌方抢先手集火）");
            }

            // ② 撤离代价
            if (WanXiang.Campaign.RealmCatalog.PenalizesOnQuit(r)
                && !string.IsNullOrEmpty(r.QuitPenaltyText))
                parts.Add("中途撤离：" + r.QuitPenaltyText);

            if (parts.Count == 0) return "无额外代价";
            return string.Join("；", parts);
        }

        /// <summary>状态挂在我方身上时"为什么疼"—— 一句人话，对着真实反应规则写。</summary>
        private static string StatusWhy(string statusId)
        {
            if (statusId == WanXiang.Battle.Core.StatusCatalog.Wet)
                return "被火属性命中会触发「蒸发」，我方额外受伤";
            if (statusId == WanXiang.Battle.Core.StatusCatalog.Burn)
                return "每回合持续掉血，血线会被一路压低";
            return "我方持续处于该状态，越拖越亏";
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
