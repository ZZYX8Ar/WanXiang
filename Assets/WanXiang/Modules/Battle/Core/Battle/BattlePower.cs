// ============================================================================
//  万相 · 战斗核心 · 战力口径（**单一真源**）
//  ---------------------------------------------------------------------------
//  编队面板（敌我双方）、结算面板、历程记录**都用这一份**，别在各处再写一遍
//  （曾经编队面板的我方"总战力"是 `1100 + idx*37` 的占位假值，敌方却是另一套预算口径
//    ⇒ 两边不可比、玩家完全看不懂。2026-09-26 用户报障后统一到这里。）
//
//  口径：生命 + 攻击×3 + 防御×2 + 速度
//  ⚠ 与 `DeployEntry.StatMul` 的作用范围一致：倍率**只缩放生命/攻击/防御**，速度不缩放
//    （理由见 DeployEntry.StatMul 注释 —— 否则高幕数会出现"敌人永远先手"的单点崩坏）。
// ⚠ 改这里 = 改全局战力展示口径。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class BattlePower
    {
        /// <summary>单只异兽的战力。<paramref name="statMul"/> = 该条目的属性倍率（默认 1）。</summary>
        public static int Of(BeastDef def, float statMul = 1f)
        {
            if (def == null) return 0;
            var st = def.Clone();
            BattleConfig.Default.ApplyPlaceholderStats(st);
            return (int)((st.BaseHp + st.BaseAtk * 3f + st.BaseDef * 2f) * statMul + st.BaseSpeed);
        }

        /// <summary>一队的战力总和。<paramref name="muls"/> 为空或不够长时，缺的部分按 1 计。</summary>
        public static int Sum(System.Collections.Generic.IList<BeastDef> defs,
                              System.Collections.Generic.IList<float> muls = null)
        {
            if (defs == null) return 0;
            int total = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                float m = (muls != null && i < muls.Count) ? muls[i] : 1f;
                total += Of(defs[i], m);
            }
            return total;
        }
    }
}
