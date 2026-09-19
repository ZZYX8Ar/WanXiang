// ============================================================================
//  被动（PassiveDef）—— 回合制 v2.1 P3b
//  ---------------------------------------------------------------------------
//  设计：被动 = 触发时机 + 效果 + 数值 的三元组，改成**表驱动**，
//  以后加新被动只加一行数据，不再改战斗代码。
//
//  6 种触发时机（按 v2.1 §4.1）：开场 / 回合末 / 受击 / 击破 / 友方阵亡 / 低血
//  当前实现：OnBattleStart（开场）、OnTurnEnd（回合末）—— 这两处有现成稳定的钩子；
//  其余 4 种需要"受击后/击破后/阵亡时/掉血后"的钩子，接口已留好（见 PassiveHooks）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>被动触发时机。</summary>
    public enum PassiveTrigger
    {
        OnBattleStart = 0,
        OnTurnEnd = 1,
        OnHit = 2,        // 受击时（待接钩子）
        OnKill = 3,       // 击破目标时（待接钩子）
        OnAllyDown = 4,   // 友方阵亡时（待接钩子）
        OnLowHp = 5,      // 生命低于阈值（待接钩子）
    }

    /// <summary>被动效果类型。只用现有字段能表达的效果（无需新 API）。</summary>
    public enum PassiveEffect
    {
        None = 0,
        AttackUp = 1,     // 永久攻击加成（PermanentAttackBonus）
        Heal = 2,         // 回复最大生命的百分比（Hp，clamp 到 MaxHp）
    }

    public struct PassiveDef
    {
        public PassiveTrigger Trigger;
        public PassiveEffect Effect;
        /// <summary>AttackUp = 攻击加成比例（0.08 = +8%）；Heal = 最大生命的比例（0.05 = 5%）。</summary>
        public float Value;
        /// <summary>展示文案（图鉴 / 悬浮提示用）。</summary>
        public string Note;
    }

    /// <summary>
    /// 被动配置表。按五行给"性格"—— 同一元素同一被动，规则简单可学；
    /// 后续接 ScriptableObject 时，把 For() 换成读资产即可，调用方不变。
    /// </summary>
    public static class PassiveCatalog
    {
        private static readonly PassiveDef[] Table =
        {
            //  索引 = (int)Element：0 木 / 1 火 / 2 土 / 3 金 / 4 水
            new PassiveDef { Trigger = PassiveTrigger.OnTurnEnd,    Effect = PassiveEffect.Heal,     Value = 0.05f, Note = "生生不息：每回合末回复 5% 生命" },
            new PassiveDef { Trigger = PassiveTrigger.OnBattleStart, Effect = PassiveEffect.AttackUp, Value = 0.08f, Note = "燎原：开场攻击 +8%" },
            new PassiveDef { Trigger = PassiveTrigger.OnBattleStart, Effect = PassiveEffect.Heal,     Value = 0.12f, Note = "厚德：开场回复 12% 生命" },
            new PassiveDef { Trigger = PassiveTrigger.OnBattleStart, Effect = PassiveEffect.AttackUp, Value = 0.10f, Note = "肃杀：开场攻击 +10%" },
            new PassiveDef { Trigger = PassiveTrigger.OnTurnEnd,     Effect = PassiveEffect.Heal,     Value = 0.03f, Note = "渊流：每回合末回复 3% 生命" },
        };

        /// <summary>取某只异兽的被动（按五行）。</summary>
        public static PassiveDef For(BeastDef def)
        {
            int i = (int)def.Element;
            if (i < 0 || i >= Table.Length) return default;
            return Table[i];
        }

        public static PassiveDef ByTrigger(BeastDef def, PassiveTrigger trigger)
        {
            var p = For(def);
            return p.Trigger == trigger ? p : default;
        }
    }

    /// <summary>
    /// 被动在战斗里的应用（只做"改字段"这一类，不产生事件 —— 保持可种子复现）。
    /// 待接的 4 种时机在这里补：OnHit / OnKill / OnAllyDown / OnLowHp。
    /// </summary>
    public static class PassiveHooks
    {
        /// <summary>开场：给全部我方单位应用 OnBattleStart 被动。</summary>
        public static void ApplyBattleStart(BattleState st)
        {
            var ours = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (!u.IsAlive) continue;
                var p = PassiveCatalog.ByTrigger(u.Def, PassiveTrigger.OnBattleStart);
                if (p.Effect == PassiveEffect.None) continue;
                if (p.Effect == PassiveEffect.AttackUp) u.PermanentAttackBonus += p.Value;
                else if (p.Effect == PassiveEffect.Heal)
                    u.Heal((int)(u.MaxHp * p.Value));
            }
        }

        /// <summary>回合末：给全部我方单位应用 OnTurnEnd 被动。</summary>
        public static void ApplyTurnEnd(BattleState st)
        {
            var ours = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (!u.IsAlive) continue;
                var p = PassiveCatalog.ByTrigger(u.Def, PassiveTrigger.OnTurnEnd);
                if (p.Effect == PassiveEffect.Heal)
                    u.Heal((int)(u.MaxHp * p.Value));
                else if (p.Effect == PassiveEffect.AttackUp)
                    u.PermanentAttackBonus += p.Value;
            }
        }
    }
}
