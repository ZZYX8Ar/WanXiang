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
        public int Value;             // 约略伤害绝对值（详情用）
        public int Percent;           // 相对我方平均血量的百分比（徽章主显示，玩家可判轻重）
        public bool IsAoe;            // 是否群攻（徽章加"全"标记）
        public string Label = "?";    // 徽章短文本 "剑 20%"
        public string Name = "";      // 技能名
        public string Detail = "";    // 悬停说明
        public bool IsUltimate;       // 终结技（UI 用高亮底强调）
        public string TargetLabel = "";   // 打谁（"句芒" / "随机"）；空 = 群攻或无目标
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
            info.IsAoe = IsAoeSkill(sk);

            // ★ 2026-11-16 优化：绝对值（剑495）玩家判不出轻重 → 改相对我方平均血量百分比。
            int avgHp = AveragePlayerMaxHp(st);
            info.Percent = avgHp > 0 && info.Value > 0
                ? MathfClampPercent(info.Value * 100 / avgHp)
                : 0;

            // ★ 2026-11-16：算出"这一下要打谁" —— 玩家知道核心输出要被集火，才会去保护/换位。
            //   口径与结算同源（PreviewTargets 是结算用的同一份规则，纯只读）。
            // ⛔ 防御/增益的目标是它自己或友方，显示"→ 白猿"会让玩家误以为要打白猿。
            //   只有 攻击 / 终结技 / 减益 才有"谁要挨打"的语义。
            bool threatening = info.Kind == IntentKind.Attack
                            || info.Kind == IntentKind.Ultimate
                            || info.Kind == IntentKind.Debuff;
            info.TargetLabel = threatening ? ResolveTargetLabel(st, u, sk, info.IsAoe) : "";

            string tag = Tag(info.Kind);
            info.Label = info.Percent > 0
                ? tag + " " + info.Percent + "%" + (info.IsAoe ? "·全" : "")
                : tag;
            info.Detail = info.Name
                + (info.Value > 0 ? " · 约 " + info.Value + " 点" : "")
                + (info.Percent > 0 ? "（我方均血 " + avgHp + "，约 " + info.Percent + "%）" : "")
                + Suffix(info.Kind);
            return info;
        }

        /// <summary>是否群攻（与实战 IsAoeTarget 同一批选择器）。</summary>
        private static bool IsAoeSkill(SkillDef sk)
        {
            if (sk == null || sk.Effects == null) return false;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var t = sk.Effects[i].Target;
                if (t == TargetSelector.AllEnemies || t == TargetSelector.AllAllies
                    || t == TargetSelector.AllOthers) return true;
            }
            return false;
        }

        /// <summary>我方存活单位的平均最大血量（百分比口径的分母）。</summary>
        private static int AveragePlayerMaxHp(BattleState st)
        {
            var allies = st.UnitsOf(TeamSide.Player);
            if (allies == null) return 0;
            long sum = 0; int n = 0;
            for (int i = 0; i < allies.Count; i++)
            {
                var a = allies[i];
                if (a == null || !a.IsAlive || a.MaxHp <= 0) continue;
                sum += a.MaxHp; n++;
            }
            return n == 0 ? 0 : (int)(sum / n);
        }

        private static int MathfClampPercent(int v)
        {
            if (v < 1) return 1;      // 至少 1%，不让"有伤害"显示成空
            if (v > 999) return 999;  // 徽章宽度上限
            return v;
        }

        private static readonly System.Collections.Generic.List<BattleUnit> _intentTargetBuf
            = new System.Collections.Generic.List<BattleUnit>(8);

        /// <summary>意图目标的一句话标签（群攻不显示具体人）。</summary>
        private static string ResolveTargetLabel(BattleState st, BattleUnit u, SkillDef sk, bool isAoe)
        {
            if (isAoe || st == null || u == null || sk == null) return "";
            bool isRandom;
            BattleSimulator.PreviewTargets(st, u, sk, _intentTargetBuf, out isRandom);
            if (_intentTargetBuf.Count == 0) return "";
            if (isRandom) return "随机";
            if (_intentTargetBuf.Count == 1)
                return _intentTargetBuf[0] != null ? _intentTargetBuf[0].DisplayName : "";
            return _intentTargetBuf.Count + " 人";
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
