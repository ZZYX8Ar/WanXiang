// ============================================================================
//  万相 · 首领战机制钩子框架（Battle.Core）
//  ---------------------------------------------------------------------------
//  设计文档《首领战设计文档》§9~§10 的结论：14 个首领只用了 10 个可复用积木，
//  所以这里**先做积木、再做首领**—— 一套通用的战斗生命周期钩子 + 一堆可组合的机制，
//  首领只是"哪几个积木按什么参数拼"的数据（见 BossCatalog.cs）。
//
//  钩子只在生命周期点分发，且**空列表短路**：普通战斗 st.Hooks 为空，
//  BattleSimulator 的调用点全都 `if (st.Hooks.Count > 0)` 跳过，逐位不变（可复现性红线）。
//
//  生命周期点（在 BattleSimulator 里调用）：
//    · RunStart      —— 开场（免疫克制 / 双子部署）
//    · TurnStart     —— 回合始（首击标记清零 / 属性轮转 / 召唤计时）
//    · TurnEnd       —— 回合末（硬性DPS灭团 / 双子复活 / 组装成长 / 破壳窗口结算）
//    · ModifyIncoming—— 一段伤害结算**前**（首击减伤改写 incoming）
//    · OnDamageDealt —— 结算**后**（伤害反弹 / 分摊 / 假死伤害累计）
//    · OnUnitKilled  —— 单位阵亡时（共享生命 / 双杀记录 / 击杀成长）
//
//  ⚠ 确定性红线：所有触发都按回合 / 血量阈值，不引入任何额外随机流；
//    召唤/分身的属性若需随机，复用 st.Random（在同一战斗随机流里，仍是确定性）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>首领机制钩子。所有方法默认空实现，子类只重写自己关心的。</summary>
    public static class BossHint
    {
        public static void Set(BattleState st, BattleUnit u, string text)
        {
            if (u == null || u.StateHint == text) return;
            u.StateHint = text;
            if (st != null) st.CaptureFrame();
        }

        /// <summary>给敌方当前存活的那只首领写提示（钩子手里没有 boss 引用时用）。</summary>
        public static void SetOnBoss(BattleState st, string text)
        {
            if (st == null) return;
            var foes = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < foes.Count; i++)
                if (foes[i] != null && foes[i].IsAlive) { Set(st, foes[i], text); return; }
        }

        /// <summary>分隔符（多段拼装用）。</summary>
        public const string PartSeparator = " · ";

        /// <summary>
        /// **分段**写提示：同一只首领的多个机制可以共存，最终显示成 "A · B · C"。
        /// <paramref name="key"/> 决定顺序（按 key 字典序排，， 显示顺序稳定、可预期）。
        /// 传空文本 = 移除该段。
        /// ⚠ 与 <see cref="Set"/> 混用会互相干扰：<c>Set</c> 是"整条覆盖"，会把已有分段清掉。
        ///   ， 一只首领要么全用 SetPart，要么全用 Set，别混。
        /// </summary>
        public static void SetPart(BattleState st, BattleUnit u, string key, string text)
        {
            if (u == null || string.IsNullOrEmpty(key)) return;
            if (u.HintParts == null) u.HintParts = new System.Collections.Generic.SortedDictionary<string, string>();
            bool empty = string.IsNullOrEmpty(text);
            bool had = u.HintParts.ContainsKey(key);
            if (empty)
            {
                if (!had) return;
                u.HintParts.Remove(key);
            }
            else
            {
                u.HintParts[key] = text;
            }

            // 重拼：SortedDictionary 的枚举顺序 = key 字典序 ， 稳定
            string composed = null;
            if (u.HintParts.Count > 0)
            {
                var parts = new System.Collections.Generic.List<string>(u.HintParts.Values);
                composed = string.Join(PartSeparator, parts);
            }
            if (u.StateHint == composed) return;
            u.StateHint = composed;
            if (st != null) st.CaptureFrame();
        }

        /// <summary>清掉某单位的所有分段（连带 StateHint）。</summary>
        public static void ClearParts(BattleState st, BattleUnit u)
        {
            if (u == null || u.HintParts == null || u.HintParts.Count == 0) return;
            u.HintParts.Clear();
            u.StateHint = null;
            if (st != null) st.CaptureFrame();
        }

        /// <summary>给敌方当前存活的那只首领写<paramref name="key"/>分段。</summary>
        public static void SetPartOnBoss(BattleState st, string key, string text)
        {
            if (st == null) return;
            var foes = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < foes.Count; i++)
                if (foes[i] != null && foes[i].IsAlive) { SetPart(st, foes[i], key, text); return; }
        }
    }

}
