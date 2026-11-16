// ============================================================================
//  万相 · 敌人意图预告（2026-11-16 用户："抄杀戮尖塔的敌人意图"）
//  ---------------------------------------------------------------------------
//  设计纪律：
//  1. ⛔ 单一真相源：DecideSkillSlot 被实际出手(BattleSimulator)和预告共同调用。
//     改一处，预告与实战一起变 —— 杜绝"预告 A 实际打 B"。
//  2. Core 纯逻辑、零引擎依赖、零副作用（不消费指令、不扣灵力、不掷随机）。
//  3. 只预告"动作类型 + 约略数值"，具体目标由技能 TargetSelector 决定，不虚假承诺。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public enum IntentKind
    {
        None = 0,
        Attack,      // 攻击（剑）
        Defend,      // 护盾/治疗（盾）
        Debuff,      // 施加负面状态（↓）
        Buff,        // 施加增益（↑）
        Ultimate,    // 终结技（★）
        Unknown,     // 未知（?）
    }

    public sealed class IntentInfo
    {
        public IntentKind Kind = IntentKind.Unknown;
        public int Value;             // 约略伤害，0 = 不显示数字
        public string Label = "?";    // 徽章短文本 "剑 12"
        public string Name = "";      // 技能名
        public string Detail = "";    // 悬停说明
        public bool IsUltimate;       // 终结技（UI 用高亮底强调）
    }

    public static class EnemyIntentReader
    {
        /// <summary>
        /// 【单一真相源】判定该单位这次放哪个槽。从 BattleSimulator.DecideSlot 原样抽出，
        /// 供实际出手与意图预告共用。玩家指令分支仍留在 BattleSimulator（它有副作用）。
        /// </summary>
        public static SkillType DecideSkillSlot(BattleState st, BattleUnit u)
        {
            var cfg = st.Config;
            var ult = u.GetSkill(SkillType.Ultimate);
            var act = u.GetSkill(SkillType.Active);

            if (cfg.AiProfile == AiProfile.Aggressive)
            {
                if (ult != null && u.CanCast(SkillType.Ultimate, cfg)) return SkillType.Ultimate;
                if (act != null && u.CanCast(SkillType.Active, cfg)) return SkillType.Active;
            }
            else if (cfg.AiProfile == AiProfile.Cautious)
            {
                bool hurt = u.HpPercent < 0.5f;
                if (!hurt && act != null && u.CanCast(SkillType.Active, cfg)) return SkillType.Active;
            }
            else
            {
                if (ult != null && cfg.AutoCastUltimate && u.CanCast(SkillType.Ultimate, cfg))
                    return SkillType.Ultimate;
                if (act != null && u.CanCast(SkillType.Active, cfg)) return SkillType.Active;
            }
            return SkillType.Basic;
        }

        /// <summary>纯函数读取敌人下一步意图（不修改 st、不消耗状态）。</summary>
        public static IntentInfo Read(BattleState st, BattleUnit u)
        {
            var info = new IntentInfo();
            if (st == null || u == null || !u.IsAlive) return info;

            SkillType slot = DecideSkillSlot(st, u);
            SkillDef sk = u.GetSkill(slot);
            if (sk == null) { info.Kind = IntentKind.Attack; info.Label = "剑?"; return info; }

            info.Name = string.IsNullOrEmpty(sk.Name) ? SlotName(slot) : sk.Name;
            info.IsUltimate = slot == SkillType.Ultimate;
            info.Kind = Classify(slot, sk);
            info.Value = Estimate(u, sk);
            info.Label = info.Value > 0 ? Tag(info.Kind) + " " + info.Value : Tag(info.Kind);
            info.Detail = info.Name + (info.Value > 0 ? " · 约 " + info.Value + " 点" : "") + Suffix(info.Kind);
            return info;
        }

        private static IntentKind Classify(SkillType slot, SkillDef sk)
        {
            if (slot == SkillType.Ultimate) return IntentKind.Ultimate;
            if (sk.Effects == null) return IntentKind.Attack;

            bool hasDamage = false, hasHealShield = false, hasDebuff = false;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                switch (a.Kind)
                {
                    case EffectAtomKind.Damage: hasDamage = true; break;
                    case EffectAtomKind.Heal:
                    case EffectAtomKind.Shield:
                    case EffectAtomKind.Revive: hasHealShield = true; break;
                    case EffectAtomKind.ApplyStatus:
                        hasDebuff = true;   // 具体增益/减益由 StatusDef 决定，徽章只提示"状态"
                        break;
                }
            }
            if (hasHealShield && !hasDamage) return IntentKind.Defend;
            if (hasDebuff && !hasDamage) return IntentKind.Debuff;
            return IntentKind.Attack;
        }

        /// <summary>
        /// 约略数值 = 攻击力 × 伤害倍率之和（含段数），取整。
        /// ⚠ 这是**预告用的粗算**，不含防御/克制/暴击，故 Label 写"约"。
        ///   它的价值在趋势（这一下重不重），不在精确值 —— 精确值反而会随目标变化而"骗人"。
        /// </summary>
        private static int Estimate(BattleUnit u, SkillDef sk)
        {
            if (sk.Effects == null) return 0;
            float sum = 0f;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                if (a.Kind != EffectAtomKind.Damage) continue;
                int hits = a.Hits > 0 ? a.Hits : 1;
                sum += a.Power * hits;
            }
            return sum <= 0f ? 0 : CoreMath.RoundDamage(u.Attack * sum);
        }

        private static string Tag(IntentKind k)
        {
            switch (k)
            {
                case IntentKind.Attack: return "剑";
                case IntentKind.Defend: return "盾";
                case IntentKind.Debuff: return "↓";
                case IntentKind.Buff: return "↑";
                case IntentKind.Ultimate: return "★";
                default: return "?";
            }
        }

        private static string SlotName(SkillType s)
        {
            switch (s)
            {
                case SkillType.Basic: return "普攻";
                case SkillType.Active: return "战技";
                case SkillType.Ultimate: return "终结技";
                default: return "技能";
            }
        }

        private static string Suffix(IntentKind k)
        {
            switch (k)
            {
                case IntentKind.Ultimate: return "（终结技 · 需留意）";
                case IntentKind.Defend: return "（自保）";
                case IntentKind.Debuff: return "（会施加状态）";
                case IntentKind.Attack: return "（对位目标）";
                default: return "";
            }
        }
    }
}
