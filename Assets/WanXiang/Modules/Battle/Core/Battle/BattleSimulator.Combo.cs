// ============================================================================
//  万相 · 战斗核心 · Combo（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域，成员可访问性与语义完全不变。
//  ⚠ 只挪位置，没改任何一行逻辑。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static partial class BattleSimulator
    {

        /// <summary>
        /// 【预览】连携会作用于哪些单位 —— **只读、不消费随机数、不改任何状态**
        /// （供战斗面板的九宫格高亮；用户报障"放连携没有格子高亮"）。
        /// 目标口径与 <see cref="ExecuteCombo"/> 一一对应（全体敌方 / 全体敌方+我方全体 / 最前排单体）；
        /// ⚠ 改结算目标时**这里要一起改** —— 别再让表现层自己推一份。
        /// </summary>
        public static void PreviewComboTargets(BattleState st, BattleUnit host, ComboDef combo,
                                               List<BattleUnit> into)
        {
            into.Clear();
            if (st == null || host == null || combo == null) return;

            var foeSide = BattleState.Opponent(host.Side);
            // ⚠ `CollectAlive` **会先清空列表** ⇒ 多池必须各自收进临时表再合并。
            //   （直接把两个池都收进 into 的话，后者会把前者冲掉 —— 实测：WoodPulse 只剩我方、敌方全丢。）
            var tmp = new List<BattleUnit>(8);
            switch (combo.Effect)
            {
                case ComboEffect.WoodPulse:      // 全体敌方木伤 + 我方全体回血
                    st.CollectAlive(foeSide, tmp);    MergeInto(into, tmp);
                    st.CollectAlive(host.Side, tmp);  MergeInto(into, tmp);
                    break;
                case ComboEffect.FireStorm:      // 全体敌方火伤
                    st.CollectAlive(foeSide, tmp);    MergeInto(into, tmp);
                    break;
                case ComboEffect.MetalBreak:     // 最前排单体重击（复用结算同一个 PickByRank）
                    PickByRank(st, foeSide, into, host, true);
                    break;
            }
        }
        /// <summary>把 src 合并进 into（去重）。</summary>
        private static void MergeInto(List<BattleUnit> into, List<BattleUnit> src)
        {
            for (int i = 0; i < src.Count; i++)
                if (!into.Contains(src[i])) into.Add(src[i]);
        }
        /// <summary>
        /// 执行连携技（v2.1 P4）：主兽 + 伙伴各扣 2 灵力，按 ComboEffect 结算。
        /// 返回 false = 条件不满足（每场限一次 / 伙伴不在 / 灵力不足），不产生任何事件。
        /// </summary>
        public static bool ExecuteCombo(BattleState st, string comboId, BattleUnit host)
        {
            var combo = ComboRules.For(comboId);
            if (combo == null || host == null || !host.IsAlive) return false;
            if (st.UsedCombos.Contains(comboId)) return false;
            var partner = ComboRules.FindPartner(st, host, combo);
            if (partner == null) return false;
            if (st.TeamMp < combo.MpCost * 2) return false;

            st.TeamMp -= combo.MpCost * 2;
            st.UsedCombos.Add(comboId);

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: host.RuntimeId,
                       skillName: combo.Name, element: Element.None,
                       note: host.DisplayName + "×" + partner.DisplayName + "·" + combo.Name,
                       skill: SkillType.Active);

            int power = CoreMath.RoundDamage(host.Attack * combo.Power);
            var foeSide = BattleState.Opponent(host.Side);

            switch (combo.Effect)
            {
                case ComboEffect.WoodPulse:
                {
                    var foes = st.UnitsOf(foeSide);
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].IsAlive) foes[i].TakeDamage(power);
                    var ours = st.UnitsOf(host.Side);
                    for (int i = 0; i < ours.Count; i++)
                        if (ours[i].IsAlive) ours[i].Heal((int)(ours[i].MaxHp * 0.15f));
                    break;
                }
                case ComboEffect.FireStorm:
                {
                    var foes = st.UnitsOf(foeSide);
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].IsAlive) foes[i].TakeDamage(power);
                    break;
                }
                case ComboEffect.MetalBreak:
                {
                    var list = new System.Collections.Generic.List<BattleUnit>();
                    PickByRank(st, foeSide, list, host, true);      // 最前排单体
                    if (list.Count > 0 && list[0].IsAlive) list[0].TakeDamage(power);
                    break;
                }
            }
            return true;
        }
    }
}
