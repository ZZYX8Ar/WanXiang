// ============================================================================
//  敌方招式库 · 运行期（FoeKitRuntime）—— 2026-10-16
//  ---------------------------------------------------------------------------
//  把 FoeRules 表里的配置写进 BattleUnit，并在战斗里维护：
//    · 弱点 / 抗性倍率（改伤害，0.75~1.35）
//    · 破绽进度累计 + 触发失衡
//    · 失衡回合递减
//    · 起手预警事件的产出
//
//  ⚠ 纪律：
//   ① 没配 FoeKit 的单位**一行都不执行**（无 FoeKit 的 for 直接 return）⇒
//     天气自检的基准局指纹逐位不变。
//   ② 只对敌方生效（见 BattleFactory.Deploy 里的 Side 判定）。
//   ③ 破绽进度只在**玩家命中敌方**时累计 —— 敌方自己打自己不算。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class FoeKitRuntime
    {
        /// <summary>
        /// 把 FoeKit 配置写进单位。调用点在 <c>BattleFactory.Deploy</c>（建单位之后、上阵之前）。
        /// 没有对应配置 ⇒ 不动单位（保证零副作用）。
        /// </summary>
        public static void Attach(BattleUnit u)
        {
            if (u == null || u.Def == null) return;
            var kit = FoeRules.For(u.Def.Id);
            if (kit == null) return;                       // ⛔ 未配置 = 朴素敌人

            u.FoeWeakElement = kit.WeakElement;
            u.FoeResistElement = kit.ResistElement;
            u.FoeFamily = kit.Family;
            u.FoeFlawTrigger = kit.FlawTrigger;
            u.FoeFlawElement = kit.FlawElement;
            u.FoeFlawStatusId = kit.FlawStatusId;
            u.FoeFlawNeed = kit.FlawNeed;
            u.FoeFlawHpPercent = kit.FlawHpPercent;
            u.FoeUnbalancedMul = kit.UnbalancedMul;

            // ★ 把绝技**真写出来**（替换 BossSkills.Default 的占位技组）。
            //⛔ 只改 unit.Def（BattleFactory.Deploy 里已 def.Clone() 过，是每只单位独有的副本）
            //     ⇒ 不会污染图鉴共享的 BeastDef，也不会影响玩家自己养的那只同名兽。
            if (!string.IsNullOrEmpty(kit.UltName))
                u.Def.Ultimate = BuildUltimate(kit);
        }

        /// <summary>
        /// 按谱系生成绝技定义。
        /// ⛔ 段数在这里**不写进原子**（原子只管"每段几次判定"），段数由
        ///   <see cref="SegmentCountFor"/> 单独提供给格挡窗口 —— 两者分开的理由见那里的注释。
        /// </summary>
        public static SkillDef BuildUltimate(FoeKitDef kit)
        {
            if (kit == null) return null;
            int segs = FoeRules.ClampSegments(kit.Segments, kit.Family);
            int hits = kit.HitsPerSegment < 1 ? 1 : kit.HitsPerSegment;

            // 谱系 → 选目标：贯穿打单点（串一列），其余打全体（守关单 Boss 场景必须有 AOE 压力）
            var tgt = kit.Family == FoeFamily.Pierce
                ? TargetSelector.SingleHighestHp
                : TargetSelector.AllEnemies;
            float power = kit.Family == FoeFamily.Strike ? 0.55f
                        : kit.Family == FoeFamily.Sweep ? 0.42f
                        : kit.Family == FoeFamily.Quake ? 0.46f
                        : kit.Family == FoeFamily.Pierce ? 0.40f
                        : 0.38f;

            int n = 2;
            var eff = new EffectAtom[n];
            eff[0] = EffectAtom.Damage(tgt, power, hits);
            if (!string.IsNullOrEmpty(kit.UltStatusId))
                eff[1] = EffectAtom.Status(TargetSelector.SingleLowestHp, kit.UltStatusId,
                                           kit.UltStatusStacks < 1 ? 1 : kit.UltStatusStacks,
                                           kit.UltStatusTurns < 1 ? 2 : kit.UltStatusTurns);

            return new SkillDef
            {
                Id = "foe_" + kit.Id + "_ult",
                Name = kit.UltName,
                Type = SkillType.Ultimate,
                Cd = 3,
                Element = Element.None,
                PrimaryTarget = tgt,
                Description = kit.UltNote,
                Effects = eff,
            };
        }

        /// <summary>
        /// 这只敌人这次行动要分几段格挡。⭐ 段数由 **FoeKit 配置**决定（不是原子 Hits）——
        /// 因为"劈斩 3 段"是招式谱系的一部分（每段一次判定），而"每段 2 次伤害"才是原子的事。
        /// ⛔ 没配 FoeKit ⇒ 返回 -1，调用方沿用旧的"看原子 Hits"口径。
        /// </summary>
        public static int SegmentCountFor(BattleUnit u, int fallback)
        {
            if (u == null || u.Side != TeamSide.Enemy) return fallback;
            var kit = FoeRules.For(u.Def != null ? u.Def.Id : null);
            if (kit == null || kit.Family == FoeFamily.None) return fallback;
            if (u.Def == null || u.Def.Ultimate == null) return fallback;
            // 只有"这次放的是绝技"才用谱系段数；普攻/战技沿用旧口径
            if (u.Def.Ultimate.Id != null && !u.Def.Ultimate.Id.StartsWith("foe_")) return fallback;
            return FoeRules.ClampSegments(kit.Segments, kit.Family);
        }

        // =====================================================================
        //  伤害倍率（走 BattleHooks.ModifyIncoming / DealDamage 收口）
        // =====================================================================

        /// <summary>
        /// 弱点 / 抗性倍率。<b>只读不改</b>，可以安全地在预览里调用（面板显示"弱点 ×1.35"）。
        /// </summary>
        public static float WeaknessMul(BattleUnit src, BattleUnit dst)
        {
            if (dst == null || dst.Side != TeamSide.Enemy) return 1f;
            if (src == null) return 1f;
            // 无属性攻击（含真实伤害）不吃弱点 —— 否则"无属性万能"变成"无属性最强"。
            if (src.Element == Element.None) return 1f;
            if (dst.FoeWeakElement != Element.None && src.Element == dst.FoeWeakElement)
                return FoeRules.WeakMul;
            if (dst.FoeResistElement != Element.None && src.Element == dst.FoeResistElement)
                return FoeRules.ResistMul;
            return 1f;
        }

        /// <summary>
        /// 合乘后的「这只怪现在吃多少伤害」：弱点/抗性 × 失衡倍率。
        /// 无 FoeKit 配置 ⇒ 1（逐位不变）。
        /// </summary>
        public static float IncomingMul(BattleUnit src, BattleUnit dst)
        {
            if (dst == null || dst.Side != TeamSide.Enemy) return 1f;
            return dst.FoeIncomingMul * WeaknessMul(src, dst);
        }

        // =====================================================================
        //  破绽进度
        // =====================================================================

        /// <summary>
        /// 记录一次「玩家命中敌方」，累计破绽进度。触发则进入失衡并返回 true。
        /// ⛔ 只在**玩家打敌方**时调用；敌方互殴 / 环境伤害不计。
        /// </summary>
        public static bool OnPlayerHitEnemy(BattleState st, BattleUnit src, BattleUnit dst, Element el)
        {
            if (st == null || dst == null || dst.Side != TeamSide.Enemy) return false;
            if (src == null || src.Side != TeamSide.Player) return false;
            if (dst.FoeFlawTrigger == FoeFlawTrigger.None) return false;
            if (dst.FoeUnbalancedTurns > 0) return false;   // 已经失衡了，别重复计
            if (!IsParryableFlaw(dst)) return false;

            switch (dst.FoeFlawTrigger)
            {
                case FoeFlawTrigger.HitByElement:
                    if (dst.FoeFlawElement != Element.None && el != dst.FoeFlawElement) return false;
                    break;
                case FoeFlawTrigger.StatusStacks:
                    if (StatusStacks(dst, dst.FoeFlawStatusId) >= dst.FoeFlawNeed)
                        return EnterUnbalanced(st, dst);
                    return false;
                case FoeFlawTrigger.HpBelow:
                    return false;      // 血量型在 CheckFlawHp 里判
                case FoeFlawTrigger.ParryCount:
                case FoeFlawTrigger.BreakParts:
                    return false;      // 分别在格挡成功 / 部件破坏时计
            }

            dst.FoeFlawProgress++;
            if (dst.FoeFlawProgress >= dst.FoeFlawNeed) return EnterUnbalanced(st, dst);
            return false;
        }

        /// <summary>成功格挡它一次（白刃「刃口」这类"防御即输出"的破绽）。</summary>
        public static bool OnParriedBy(BattleState st, BattleUnit foe)
        {
            if (st == null || foe == null || foe.Side != TeamSide.Enemy) return false;
            if (foe.FoeFlawTrigger != FoeFlawTrigger.ParryCount) return false;
            if (foe.FoeUnbalancedTurns > 0) return false;
            foe.FoeFlawProgress++;
            if (foe.FoeFlawProgress >= foe.FoeFlawNeed) return EnterUnbalanced(st, foe);
            return false;
        }

        /// <summary>打碎一个部件（玄溟冰晶那类）。</summary>
        public static bool OnPartBroken(BattleState st, BattleUnit foe, int count = 1)
        {
            if (st == null || foe == null || foe.Side != TeamSide.Enemy) return false;
            if (foe.FoeFlawTrigger != FoeFlawTrigger.BreakParts) return false;
            if (foe.FoeUnbalancedTurns > 0) return false;
            foe.FoeFlawProgress += count < 1 ? 1 : count;
            if (foe.FoeFlawProgress >= foe.FoeFlawNeed) return EnterUnbalanced(st, foe);
            return false;
        }

        /// <summary>血量型破绽（归墟「地脉」）：血量跌破阈值直接失衡。</summary>
        private static void CheckFlawHp(BattleState st, BattleUnit foe)
        {
            if (foe == null || foe.Side != TeamSide.Enemy) return;
            if (foe.FoeFlawTrigger != FoeFlawTrigger.HpBelow) return;
            if (foe.FoeUnbalancedTurns > 0) return;
            if (foe.MaxHp <= 0) return;
            float pct = foe.Hp * 100f / foe.MaxHp;
            if (pct > foe.FoeFlawHpPercent) return;
            EnterUnbalanced(st, foe);
        }

        private static bool IsParryableFlaw(BattleUnit u)
        {
            return u.FoeFlawTrigger == FoeFlawTrigger.HitByElement
                || u.FoeFlawTrigger == FoeFlawTrigger.StatusStacks;
        }

        private static int StatusStacks(BattleUnit u, string statusId)
        {
            if (string.IsNullOrEmpty(statusId)) return 0;
            for (int i = 0; i < u.Statuses.Count; i++)
                if (u.Statuses[i].Def.Id == statusId)
                    return u.Statuses[i].Stacks;
            return 0;
        }

        private static bool EnterUnbalanced(BattleState st, BattleUnit foe)
        {
            if (foe == null) return false;
            foe.FoeUnbalancedTurns = FoeRules.For(foe.Def != null ? foe.Def.Id : null) != null
                ? _unbalancedTurnsOf(foe)
                : 2;
            foe.FoeFlawProgress = 0;      // 破绽重置
            st.Log.Add(st.Turn, BattleEventKind.StatChange, actorId: foe.RuntimeId,
                       targetId: foe.RuntimeId,
                       note: "破绽·" + FlawNameOf(foe) + " → 失衡 " + foe.FoeUnbalancedTurns
                             + " 回合（受伤 ×" + foe.FoeUnbalancedMul.ToString("0.0") + "）");
            return true;
        }

        private static int _unbalancedTurnsOf(BattleUnit u)
        {
            var kit = FoeRules.For(u.Def != null ? u.Def.Id : null);
            return kit != null && kit.UnbalancedTurns > 0 ? kit.UnbalancedTurns : 2;
        }

        private static string FlawNameOf(BattleUnit u)
        {
            var kit = FoeRules.For(u.Def != null ? u.Def.Id : null);
            return kit != null && !string.IsNullOrEmpty(kit.FlawName) ? kit.FlawName : "破绽";
        }

        // =====================================================================
        //  回合钩子：失衡递减 + 血量型破绽检查
        // =====================================================================

        /// <summary>回合末递减失衡回合（每个敌方单位都判）。</summary>
        public static void TickUnbalanced(BattleState st)
        {
            if (st == null) return;
            var foes = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < foes.Count; i++)
            {
                var u = foes[i];
                if (u == null || !u.IsAlive) continue;
                CheckFlawHp(st, u);
                if (u.FoeUnbalancedTurns > 0) u.FoeUnbalancedTurns--;
            }
        }

        // =====================================================================
        //  起手预警（Windup）
        // =====================================================================

        /// <summary>
        /// 产出起手预警事件。<b>只有配了 FoeKit 的敌人才有</b>（朴素敌人无事件 ⇒ 指纹不变）。
        /// </summary>
        public static void EmitWindup(BattleState st, BattleUnit foe, int segmentIndex, int segmentTotal)
        {
            if (st == null || foe == null || foe.Side != TeamSide.Enemy) return;
            if (foe.FoeFamily == FoeFamily.None) return;
            var fam = foe.FoeFamily;
            int famBits = (int)fam;
            // 头顶预告（经帧流送到表现层）。⚠ 段数 >1 时才带"k/n"，单段不显示段号（少一层噪音）。
            foe.FoeTelegraph = FoeRules.WindupColorCn(fam) + "·" + FoeRules.FamilyCn(fam)
                             + (segmentTotal > 1 ? " " + (segmentIndex + 1) + "/" + segmentTotal : "");
            st.Log.Add(st.Turn, BattleEventKind.Windup, actorId: foe.RuntimeId,
                       skillName: FoeRules.FamilyCn(fam),
                       amount: (famBits << 16) | ((segmentIndex + 1) << 8) | (segmentTotal & 0xFF),
                       note: FoeRules.WindupColorCn(fam) + "·" + FoeRules.FamilyCn(fam)
                             + (segmentTotal > 1 ? "（第 " + (segmentIndex + 1) + "/" + segmentTotal + " 段）" : "")
                             + "　对策：" + FoeRules.FamilyCounterCn(fam));
        }

        /// <summary>把 Windup 事件的 amount 拆回结构化字段（表现层用，避免解析中文 Note）。</summary>
        public static FoeFamily FamilyOf(int packed)
        {
            int fam = (packed >> 16) & 0xFF;
            if (fam < (int)FoeFamily.Strike || fam > (int)FoeFamily.Breath) return FoeFamily.None;
            return (FoeFamily)fam;
        }
        public static int SegIndexOf(int packed) => ((packed >> 8) & 0xFF) - 1;
        public static int SegTotalOf(int packed) => packed & 0xFF;

        /// <summary>清掉头顶预告（行动结束时调用，避免预告挂着不走）。</summary>
        public static void ClearTelegraph(BattleUnit foe)
        {
            if (foe == null || foe.Side != TeamSide.Enemy) return;
            foe.FoeTelegraph = null;
        }
    }
}