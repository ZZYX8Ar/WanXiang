// ============================================================================
//  万相 · 战斗核心 · Damage（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域，成员可访问性与语义完全不变。
//  ⚠ 只挪位置，没改任何一行逻辑。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static partial class BattleSimulator
    {

        // ================================================================
        //  原子效果结算
        // ================================================================

        private static void ResolveAtom(BattleState st, BattleUnit src, SkillDef skill,
                                        in EffectAtom atom, Buffers buf)
        {
            // 目标池口径：伤害类打对面，其余（治疗/护盾/增益）打自己这边。
            // 这是最不容易出错的默认值 —— 真要跨阵营（如"给敌方上减益"），
            // 用 ApplyStatus 的减益状态也一样落在对面，见下面的分支。
            TeamSide pool = PoolFor(atom, src);

            ResolveTargets(st, src, atom.Target, pool, buf.Targets);
            if (buf.Targets.Count == 0) return;

            switch (atom.Kind)
            {
                case EffectAtomKind.Damage:
                {
                    Element el = ResolveElement(atom.ElementOverride, skill, src);
                    int hits = CoreMath.Max(1, atom.Hits);

                    if (atom.Target == TargetSelector.RandomEnemyMultiHit)
                    {
                        // 多段随机：**每一段重新抽一次目标**，这才是"乱击"的手感
                        for (int h = 0; h < hits; h++)
                        {
                            ResolveTargets(st, src, TargetSelector.RandomEnemy, pool, buf.Targets);
                            if (buf.Targets.Count == 0) break;
                            DealDamage(st, src, skill, atom, el, buf.Targets[0]);
                            if (st.IsOver) break;
                        }
                    }
                    else
                    {
                        for (int t = 0; t < buf.Targets.Count; t++)
                        {
                            for (int h = 0; h < hits; h++)
                            {
                                if (!buf.Targets[t].IsAlive) break;
                                DealDamage(st, src, skill, atom, el, buf.Targets[t]);
                                if (st.IsOver) break;
                            }
                            if (st.IsOver) break;
                        }
                    }
                    break;
                }

                case EffectAtomKind.Heal:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.CanBeHealed || st.HealBanned) continue;   // 禁疗（小雪）
                        float raw = src.Attack * atom.Power + dst.MaxHp * atom.PercentOfMaxHp;
                        int amount = CoreMath.RoundDamage(raw * src.HealShieldMultiplier);
                        int healed = dst.Heal(amount);
                        if (healed <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: healed, element: src.Element);
                    }
                    break;
                }

                case EffectAtomKind.Shield:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;
                        float raw = src.Attack * atom.Power + dst.MaxHp * atom.PercentOfMaxHp;
                        int amount = CoreMath.RoundDamage(raw * src.HealShieldMultiplier);

                        // 小雪「虹藏不见」：所有护盾效果 +50%（天时修正，判空在前）
                        if (st.Weather != null)
                        {
                            float shieldMul = st.Weather.ShieldGainMul;
                            if (shieldMul != 1f) amount = CoreMath.RoundDamage(amount * shieldMul);
                        }

                        int added = dst.AddShield(amount);
                        if (added <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: added, element: src.Element);
                    }
                    break;
                }

                case EffectAtomKind.ApplyStatus:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;

                        // 持续伤害在**施加瞬间**折算成数值（见 StatusCatalog 文件头的取舍说明）：
                        //   Power>0            → 按施加者攻击力的比例
                        //   PercentOfMaxHp>0   → 按目标最大生命的比例
                        float dot = 0f;
                        if (atom.Power > 0f) dot += src.Attack * atom.Power;
                        if (atom.PercentOfMaxHp > 0f) dot += dst.MaxHp * atom.PercentOfMaxHp;

                        // 05 清明：免疫混乱/沉默、我方减益时长 -1（与天时自身的施加路径共用）
                        int turns = atom.StatusTurns;
                        var def = StatusCatalog.Get(atom.StatusId);
                        if (!WeatherFilterStatus(st, dst, atom.StatusId, ref turns))
                        {
                            st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: src.RuntimeId,
                                       targetId: dst.RuntimeId, skillName: skill?.Name,
                                       note: $"天时免疫 {def.Name}");
                            continue;
                        }

                        dst.ApplyStatus(atom.StatusId, atom.StatusStacks, turns, dot);
                        st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: atom.StatusStacks,
                                   note: $"{def.Name} ×{dst.GetStacks(atom.StatusId)}");
                    }
                    break;
                }

                case EffectAtomKind.RemoveStatus:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        string what = atom.StatusId == null ? "全部减益" : StatusCatalog.Get(atom.StatusId).Name;
                        int removed = dst.RemoveStatus(atom.StatusId);
                        if (removed <= 0) continue;
                        st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   amount: removed, note: $"驱散 {what}");
                    }
                    break;
                }

                case EffectAtomKind.StatModifier:
                {
                    for (int t = 0; t < buf.Targets.Count; t++)
                    {
                        var dst = buf.Targets[t];
                        if (!dst.IsAlive) continue;
                        dst.AddModifier(atom.StatKey, atom.StatDelta, atom.StatTurns);
                        st.Log.Add(st.Turn, BattleEventKind.StatChange, actorId: src.RuntimeId,
                                   targetId: dst.RuntimeId, skillName: skill?.Name,
                                   note: $"{atom.StatKey} {(atom.StatDelta >= 0f ? "+" : "")}" +
                                         $"{atom.StatDelta * 100f:F0}%" +
                                         (atom.StatTurns == 0 ? "（本场）" : $"（{atom.StatTurns} 回合）"));
                    }
                    break;
                }

                case EffectAtomKind.Revive:
                {
                    // STEP 1 不做复活：它牵涉"阵亡单位的槽位还占不占格"这类棋盘语义，
                    // 值得单独一轮设计。这里显式留痕，不静默吞掉。
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: src.RuntimeId,
                               skillName: skill?.Name, note: "复活原子尚未实现（STEP 1 范围外）");
                    break;
                }
            }
        }
        /// <summary>一段伤害的完整结算。多段技能每段独立判定暴击（GDD 未写，取独立）。</summary>
        private static void DealDamage(BattleState st, BattleUnit src, SkillDef skill,
                                       in EffectAtom atom, Element el, BattleUnit dst)
        {
            if (!dst.IsAlive) return;

            bool crit = st.Random.Chance(src.Def.CritRate);
            int dmg = ComputeDamage(st, src, dst, el, atom.Power, atom.TrueDamage, crit,
                                    IsAoeTarget(atom.Target));

            int dealt;
            if (atom.TrueDamage) dealt = dst.TakeTrueDamage(dmg);
            else dealt = dst.TakeDamage(dmg, atom.IgnoreShield);

            if (crit) st.Log.Add(st.Turn, BattleEventKind.Crit, actorId: src.RuntimeId, targetId: dst.RuntimeId);

            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId, targetId: dst.RuntimeId,
                       skillName: skill?.Name, amount: dmg, element: el,
                       note: BuildDamageNote(st, src, dst, el, crit, atom.TrueDamage, dealt));

            dst.AddRage(st.Config.RageWhenHit);

            if (dealt > 0 && !dst.IsAlive)
            {
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: $"{dst.DisplayName} 阵亡");
            }

            // ---- 天时钩子（GDD 3.3 剩余 8 条）：命中/暴击/击杀/受击四条通路 ----
            // 判空短路：无天时这一行不进函数体，战斗逐位不变（指纹红线）。
            if (st.Weather != null) PostDamageHooks(st, src, dst, el, dmg, dealt, crit, atom.TrueDamage);
        }
        // ================================================================
        //  天时事件钩子（GDD 3.3 剩余 8 条）
        //  ----------------------------------------------------------------
        //  这些规则的共同点是"必须挂在战斗过程的某个点上"，没法用回合开始/结束的
        //  原子与乘数表达。全部**先判字段为假就返回** —— 无天时/空天时零影响。
        // ================================================================

        /// <summary>
        /// 一段伤害结算之后的钩子：立夏附烧（命中）→ 处暑溢出转盾（击杀）→ 芒种追击（暴击）
        /// → 立冬受击冻结（受击）。
        /// 顺序即语义：先结算这一击自身的效果，再判击杀溢出，最后才追加追击 ——
        /// 追击打出去时"目标是否已死"的答案才是最终的。
        /// </summary>
        private static void PostDamageHooks(BattleState st, BattleUnit src, BattleUnit dst,
                                            Element el, int dmg, int dealt, bool crit, bool trueDamage)
        {
            var w = st.Weather;

            // 07 立夏「炎气初升」：我方所有攻击附带燃烧（按施法者攻击力折算，与技能 DoT 同一套）
            float burnPower = w.AttackBurnPowerFor(src.Side);
            if (burnPower > 0f && dst.IsAlive)
            {
                dst.ApplyStatus(StatusCatalog.Burn, 1, w.AttackBurnTurns, src.Attack * burnPower);
                var burnDef = StatusCatalog.Get(StatusCatalog.Burn);
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                           targetId: dst.RuntimeId, amount: 1,
                           note: $"{burnDef.Name} ×{dst.GetStacks(StatusCatalog.Burn)}（天时附魔）");
            }

            // 14 处暑「鹰祭而后猎」：我方**击杀**时，溢出伤害按比例转成全队护盾
            if (!dst.IsAlive && src.Side == TeamSide.Player && w.KillOverflowShieldOn)
            {
                int overkill = dmg - dealt;                       // dealt = 实际掉的血（含被护盾吃掉的部分）
                if (overkill > 0)
                {
                    int total = CoreMath.RoundDamage(overkill * w.KillOverflowShieldRatio);
                    GrantTeamShieldFromKill(st, w, src, total);
                }
            }

            // 24 大寒「寒气之逆极」下半：火属性技能命中 → 融冰（公开成 WeatherIceMelt 供测试）
            if (w.IceMeltOnFireSkill && el == Element.Fire)
                WeatherIceMelt(st, src, dst);

            // 09 芒种「螳螂生」：我方暴击时追加一次追击（每次行动限 1 次）
            // 取舍：追击**不再判暴击**（暴击的追击再暴击会链式触发，GDD 没规定这种递归）。
            if (crit && dst.IsAlive && !src.PursuitUsedThisAction)
            {
                float pursuitPower = w.PursuitPowerFor(src.Side);
                if (pursuitPower > 0f)
                {
                    src.PursuitUsedThisAction = true;
                    int pd = ComputeDamage(st, src, dst, el, pursuitPower, false, false);
                    int pdl = dst.TakeDamage(pd);
                    st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId,
                               targetId: dst.RuntimeId, amount: pd, element: el,
                               note: "天时·螳螂生：追击");
                    if (pdl > 0 && !dst.IsAlive)
                        st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                                   note: $"{dst.DisplayName} 阵亡（追击）");
                }
            }

            // 19 立冬「水始成冰」：受击时按概率被冻结（**全场** —— GDD 只说"受击时"）
            float freezeChance = w.FreezeOnHitChance;
            if (freezeChance > 0f && dst.IsAlive && !dst.HasStatus(StatusCatalog.Freeze))
            {
                if (st.Random.Chance(freezeChance))
                {
                    dst.ApplyStatus(StatusCatalog.Freeze, 1, w.FreezeOnHitTurns);
                    st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src.RuntimeId,
                               targetId: dst.RuntimeId, amount: 1,
                               note: $"天时·水始成冰：{dst.DisplayName} 被冻结");
                }
            }
        }
        /// <summary>
        /// 24 大寒「寒气之逆极」下半：火属性技能命中时，移除该单位 2 层冰蚀
        /// 并造成 5% 最大生命的额外伤害 —— 「火能融冰」把五行相克变成看得见的画面。
        /// 取舍：普攻也是技能槽 0，算"火属性技能"（GDD 没区分）；持续伤害不走这里。
        /// public：自检直接调它（DealDamage 是私有的，测试不该为它开洞）。
        /// </summary>
        public static void WeatherIceMelt(BattleState st, BattleUnit src, BattleUnit dst)
        {
            if (st.Weather == null || !st.Weather.IceMeltOnFireSkill) return;
            if (!dst.HasStatus(StatusCatalog.IceErosion)) return;

            dst.RemoveStatusStacks(StatusCatalog.IceErosion, 2);
            int melt = CoreMath.RoundDamage(dst.MaxHp * 0.05f);
            int meltDealt = dst.IsAlive ? dst.TakeTrueDamage(melt) : 0;
            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId,
                       targetId: dst.RuntimeId, amount: meltDealt, element: Element.Fire,
                       note: "天时·火能融冰");
            if (meltDealt > 0 && !dst.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: $"{dst.DisplayName} 阵亡（融冰）");
        }
        /// <summary>
        /// 击杀溢出转全队护盾：总量按**存活人数均分**，除不尽的余数给站得最前的那位。
        /// ⚠ GDD 只写"转化为全队护盾"，没说"每人一份"还是"大家分一份" —— 取分一份
        ///   （每人一份会让 5 人队凭空拿到 5 倍护盾，显然过强）。待策划确认。
        /// </summary>
        private static void GrantTeamShieldFromKill(BattleState st, WeatherRuntime w,
                                                    BattleUnit killer, int total)
        {
            int alive = st.AliveCountOf(TeamSide.Player);
            if (alive <= 0 || total <= 0) return;

            int share = total / alive;
            int remainder = total - share * alive;
            var list = st.UnitsOf(TeamSide.Player);
            bool first = true;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int amount = share + (first ? remainder : 0);
                first = false;
                if (amount <= 0) continue;
                int added = u.AddShield(amount);
                if (added <= 0) continue;
                st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: killer.RuntimeId,
                           targetId: u.RuntimeId, amount: added, element: u.Element,
                           note: "天时·鹰祭而后猎：击杀溢出转护盾");
            }
        }
        /// <summary>
        /// 17 寒露「寒露凝华」：每 N 回合，我方全体获得「凝神」。
        /// 语义取舍：GDD 写"下一次技能 CD 立即减少 2 回合"，这里在**获得时立即扣减**
        /// 当前所有在冷却的技能（对下一次可放的技能等价，且不需要"技能槽级"的钩子）。
        /// 凝神状态本身留作可读凭据（日志/UI 看得到谁拿到了）。
        /// </summary>
        private static void WeatherTurnStartHooks(BattleState st)
        {
            if (st.Weather == null) return;
            int every = st.Weather.HasteEveryNTurns;
            if (every <= 0 || st.Turn % every != 0) return;

            int cdCut = st.Weather.HasteCdReduction;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                for (int k = 0; k < u.Cooldowns.Length; k++)
                    if (u.Cooldowns[k] > 0) u.Cooldowns[k] = CoreMath.Max(0, u.Cooldowns[k] - cdCut);
                u.ApplyStatus(StatusCatalog.Haste, 1, 2);
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: u.RuntimeId,
                           targetId: u.RuntimeId, amount: 1,
                           note: $"天时·寒露凝华：凝神（技能 CD -{cdCut}）");
            }
        }
        /// <summary>
        /// 23 小寒「寒鸦北去」：每回合结束，我方速度最高的单位获得一次额外普攻。
        /// 取舍：这次普攻**不加怒气、不进冷却**（它是天时给的"白送一击"，不是技能循环的一环）。
        /// 冻结/混乱（不能行动）的单位不给 —— 控制流不该被天时绕过。
        /// </summary>
        private static void WeatherEndExtraActions(BattleState st, Buffers buf)
        {
            if (st.Weather == null || !st.Weather.ExtraBasicAttackOnTurnEnd) return;
            if (st.IsOver) return;

            BattleUnit fastest = null;
            float bestSpeed = 0f;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive || !u.CanAct) continue;
                float sp = st.EffectiveSpeed(u);
                if (fastest == null || sp > bestSpeed) { fastest = u; bestSpeed = sp; }
            }
            if (fastest == null) return;

            var basic = fastest.GetSkill(SkillType.Basic);
            if (basic == null || basic.Effects == null || basic.Effects.Length == 0) return;

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: fastest.RuntimeId,
                       skillName: basic.Name, element: ResolveElement(Element.None, basic, fastest),
                       note: $"天时·寒鸦北去：{fastest.DisplayName} 额外普攻",
                       skill: SkillType.Basic);
            for (int i = 0; i < basic.Effects.Length; i++)
            {
                ResolveAtom(st, fastest, basic, basic.Effects[i], buf);
                if (st.IsOver) break;
            }
        }
        /// <summary>
        /// 05 清明「桐始华」对"施加状态"的过滤：返回 false = 被免疫。
        /// **技能与天时两条施加路径共用它** —— 分成两份口径迟早会分叉
        /// （出现过"技能被免疫、天时上状态却能上"这类不一致）。
        /// </summary>
        public static bool WeatherFilterStatus(BattleState st, BattleUnit dst, string statusId, ref int turns)
        {
            if (st.Weather == null) return true;
            var def = StatusCatalog.Get(statusId);
            if (!def.IsDebuff) return true;

            if (st.Weather.ImmuneConfuseSilenceFor(dst.Side)
                && (statusId == StatusCatalog.Confuse || statusId == StatusCatalog.Silence))
                return false;

            if (st.Weather.DebuffDurationMinusOneFor(dst.Side))
                turns = CoreMath.Max(1, turns - 1);
            return true;
        }
        /// <summary>
        /// 伤害公式。**唯一实现**，别在别处再拼一次 —— 两处公式迟早会分叉。
        ///
        ///     普通伤害 = 攻击 × 技能倍率 × (1 + 同气) × 五行系数 × (1 + 暴伤若暴击)
        ///                × (1 - 防御减伤) × 受伤乘数
        ///     真实伤害 = 攻击 × 技能倍率 × (1 + 同气)      ← 连减伤与受伤乘数一起跳过
        ///
        /// ⚠ 五行系数**只盖章在伤害上**，相生不参与（GDD 2.3 设计说明）。
        /// ⚠ 无天时路径的浮点运算顺序必须与引入天时前完全一致（指纹红线）；
        ///   天时分支全部包在判空里，乘数默认值恰为 1（IEEE 恒等，不引入舍入）。
        /// </summary>
        public static int ComputeDamage(BattleState st, BattleUnit src, BattleUnit dst,
                                        Element el, float power, bool trueDamage, bool crit,
                                        bool aoeSkill = false, bool forPreview = false)
        {
            var cfg = st.Config;
            float raw = src.Attack * power * (1f + src.QiSkillBonus(cfg.QiEffectPerStack));
            if (trueDamage) return CoreMath.RoundDamage(raw);

            float v = raw * ElementMatrix.Coefficient(el, dst.Element, cfg.Elements);
            if (crit) v *= (1f + src.Def.CritDamage);

            float defense = dst.Def.BaseDef;
            float mitigation = defense / (defense + cfg.DefenseConstant);
            v *= (1f - mitigation);
            v *= dst.DamageTakenMultiplier;

            // ---- 天时修正（GDD 3.1/3.4）。st.Weather 为 null 时零改动 ⇒ 指纹不变 ----
            if (st.Weather != null)
            {
                // 全场伤害乘数（夏至「极阳」：造成的与受到的同时 +25%）
                v *= st.Weather.DamageAllMultiplier;

                // 五行伤害乘数（大暑火 -30% 土 +30%、祷雨火 -20%、祈晴火 +25%）
                v *= st.Weather.ElementDamageMul(el);

                // 技能形态乘数（秋分：AOE -40%、单体 +25%）
                if (st.Weather.AoeDamageMul != 1f || st.Weather.SingleDamageMul != 1f)
                    v *= st.Weather.FormDamageMul(aoeSkill);

                // 暴伤加成（立秋/白露）：GDD"暴击伤害 +40%"＝ 最终暴伤倍率相加
                //（150% + 40% = 190%），不是 (1+暴伤)×(1+加成) 的连乘。
                // 上面的暴击已按 (1+暴伤) 乘过一次，这里先把除回来再加到位。
                // ⚠ bonus == 0 时不动 v —— 保证夏至这类无暴伤修正的天时路径逐位不变。
                if (crit)
                {
                    float bonus = st.Weather.CritDamageBonusFor(src.Side);
                    if (bonus != 0f)
                    {
                        v /= (1f + src.Def.CritDamage);
                        v *= (1f + src.Def.CritDamage + bonus);
                    }
                }

                // 首回合先手方伤害乘数（冬至 +50%）
                float firstTurn = st.Weather.FirstTurnDamageMulFor(st, src.Side);
                if (firstTurn != 1f) v *= firstTurn;

                // 逆天时反噬：覆盖天时的属性被节气相克时，我方该属性单位 +15% 承伤
                var w = st.Weather;
                if (w.BacklashActive && dst.Side == TeamSide.Player && dst.Element == w.BacklashElement)
                    v *= (1f + st.Config.BacklashExtraDamage);   // 劫律 03「逆天之罚」可调
            }

            // 抖动（GDD v1.1 §3.1：Rand ∈ [0.95, 1.05]）。
            // 只在这里消费一次随机数 ⇒ 同种子逐位可复现；DamageJitter=0 时完全不掷骰
            //（对照实验用：证明其他结算路径没有被抖动污染）。
            //
            // ⚠⚠ forPreview = true 时**绝不掷骰** —— 面板/悬浮提示这类"只是看看"的调用
            //     若消费了战斗随机数，会推进随机流、把后续每一次结算都改掉
            //     （踩过：UI 预览让同种子战斗结果漂移）。预览侧要自己把 ±jitter 作为区间撑开。
            float jitter = st.Config.DamageJitter;
            if (!forPreview && jitter > 0f)
                v *= 1f + (st.Random.NextFloat() - 0.5f) * 2f * jitter;

            return CoreMath.RoundDamage(v);
        }
        /// <summary>技能的五行归属：原子覆盖 &gt; 技能自身 &gt; 施法者五行。</summary>
        public static Element ResolveElement(Element atomOverride, SkillDef skill, BattleUnit src)
        {
            if (atomOverride != Element.None) return atomOverride;
            if (skill != null && skill.Element != Element.None) return skill.Element;
            return src.Element;
        }
        /// <summary>
        /// 原子的目标形状是不是 AOE（秋分/移山这类"按形态修正"的判定口径）：
        /// 一口气打多个的算 AOE；单体（含多段随机——每段重新抽一个目标，手感是连打而非一锅端）算单体。
        /// </summary>
        public static bool IsAoeTarget(TargetSelector target)
            => target == TargetSelector.AllEnemies
            || target == TargetSelector.AllAllies
            || target == TargetSelector.AllOthers;
        private static string BuildDamageNote(BattleState st, BattleUnit src, BattleUnit dst,
                                              Element el, bool crit, bool trueDamage, int dealtToHp)
        {
            var sb = new System.Text.StringBuilder();
            if (trueDamage) sb.Append("真实伤害");
            else
            {
                var rel = ElementMatrix.Relation(el, dst.Element);
                if (rel != ElementRelation.Neutral)
                {
                    sb.Append(Cn.Of(rel)).Append(" ×").Append(
                        ElementMatrix.Coefficient(el, dst.Element, st.Config.Elements).ToString("F2"));
                }
            }
            if (crit) sb.Append(sb.Length > 0 ? "·暴击" : "暴击");
            if (!trueDamage && dst.Pos.IsCenter) sb.Append(sb.Length > 0 ? "·中宫减伤" : "中宫减伤");
            if (dealtToHp == 0 && dst.IsAlive) sb.Append(sb.Length > 0 ? "·全被护盾吸收" : "全被护盾吸收");
            return sb.ToString();
        }
    }
}
