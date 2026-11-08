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
    public abstract class BattleHook
    {
        public virtual void OnBattleStart(BattleState st) { }
        public virtual void OnTurnStart(BattleState st) { }
        public virtual void OnTurnEnd(BattleState st) { }
        public virtual void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el) { }
        public virtual void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg) { }
        public virtual void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim) { }
    }

    public static class BattleHooks
    {
        public static void RunStart(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnBattleStart(st);
        }
        public static void TurnStart(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnTurnStart(st);
        }
        public static void TurnEnd(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnTurnEnd(st);
        }
        public static void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].ModifyIncoming(st, src, dst, ref dmg, el);
        }
        public static void DamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnDamageDealt(st, src, dst, dmg, dealt, trueDmg);
        }
        public static void UnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (victim != null)
            {
                if (victim.DeathReported) return;   // 一次死亡只报一次（防重复 Death 事件 / 重复钩子）
                victim.DeathReported = true;
            }
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnUnitKilled(st, killer, victim);
        }

        /// <summary>
        /// 死亡对账（每回合末调用）：扫描战斗名册里「已阵亡但从未上报」的单位，
        /// 补发 <see cref="BattleEventKind.Death"/> 事件并派发 <see cref="UnitKilled"/>。
        /// <para>
        /// 为什么必须要它：共鸣分摊（<c>DamageSplitHook</c>）/ 伤害反弹 / 连携 / 天时 / 棋盘结算等
        /// 路径**直接调 <c>TakeDamage</c> 扣血、不经过完整伤害管线** ， 若没人补报，KillLinkHook
        /// 等同命 / 假死 / 击杀成长钩子收不到阵亡信号（用户 2026-10-12 报：双子被共鸣分摊磨死，
        /// 不复活、也没"倒下"提示，只因分摊扣血那一步既不发 Death 也不调 UnitKilled）。
        /// </para>
        /// 幂等：靠 <see cref="BattleUnit.DeathReported"/> 去重；正常击杀路径（Damage.cs）已上报的
        /// 单位不会被重复补报。
        /// </summary>
        public static void SweepDeaths(BattleState st)
        {
            if (st == null || st.IsOver || st.Hooks.Count == 0) return;
            var all = st.AllUnits;   // 战斗名册（双子等预置单位都在这；_allEver 仅表现层）
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.IsAlive || u.DeathReported) continue;
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId,
                           note: $"{u.DisplayName} 阵亡");
                // B2 骨架：道痕统计——我方阵亡 / 击败首领
                if (u.Side == TeamSide.Player) st.StatDeaths++;
                else if (u.Def != null && !string.IsNullOrEmpty(u.Def.Id)
                         && u.Def.Id.StartsWith("b_")) st.StatBossKills++;
                UnitKilled(st, null, u);   // 内部会置 DeathReported
            }
        }
    }

    // ============================================================================
    //  机制积木（§9 的 10 个 + 几个数值型辅助）
    // ============================================================================

}
