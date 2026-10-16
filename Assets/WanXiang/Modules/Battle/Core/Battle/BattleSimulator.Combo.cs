// ============================================================================
//  万相 · 战斗核心 · Combo（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域，成员可访问性与语义完全不变。
//  ⚠ v1.3 扩表：ComboEffect 3 → 18 种，本文件两个函数**必须成对修改**：
//      · ExecuteCombo         —— 真正结算
//      · PreviewComboTargets  —— 九宫格高亮（表现层只读它，绝不自己推一份）
//    只改一处 = 高亮与实际打到的目标不一致（用户报障过"放连携没有格子高亮"）。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static partial class BattleSimulator
    {
        /// <summary>连携的默认数值（Effect 未指定 Power2 时用）。</summary>
        private const float ComboHealPct = 0.15f;      // 我方全体回复 15% 最大生命
        private const float ComboTruePct = 0.08f;      // 追加真实伤害 8% 目标最大生命
        private const float ComboAdvance = 0.20f;      // 行动条前进 20%
        private const string ComboShredStatus = StatusCatalog.ArmorBreak;

        /// <summary>
        /// 【预览】连携会作用于哪些单位 —— **只读、不消费随机数、不改任何状态**
        /// （供战斗面板的九宫格高亮）。
        /// 目标口径与 <see cref="ExecuteCombo"/> 一一对应；
        /// ⚠ 改结算目标时**这里要一起改** —— 别再让表现层自己推一份。
        /// </summary>
        public static void PreviewComboTargets(BattleState st, BattleUnit host, ComboDef combo,
                                               List<BattleUnit> into)
        {
            into.Clear();
            if (st == null || host == null || combo == null) return;

            var foeSide = BattleState.Opponent(host.Side);
            // ⚠ `CollectAlive` **会先清空列表** ， 多池必须各自收进临时表再合并。
            var tmp = new List<BattleUnit>(8);
            switch (combo.Effect)
            {
                // ---- 全体敌方（可叠我方） ----
                case ComboEffect.WoodPulse:            // 全体敌方 + 我方全体
                case ComboEffect.TeamHealShield:       // 我方全体（无伤害）
                case ComboEffect.TeamShieldReflect:
                case ComboEffect.TeamHealCleanse:
                case ComboEffect.TeamHealLostHp:
                    if (combo.Effect != ComboEffect.TeamHealShield &&
                        combo.Effect != ComboEffect.TeamShieldReflect &&
                        combo.Effect != ComboEffect.TeamHealCleanse &&
                        combo.Effect != ComboEffect.TeamHealLostHp)
                    { st.CollectAlive(foeSide, tmp); MergeInto(into, tmp); }
                    st.CollectAlive(host.Side, tmp); MergeInto(into, tmp);
                    break;

                case ComboEffect.FireStorm:
                case ComboEffect.AllAoeStatus:
                case ComboEffect.AllAoeBurnStack:
                case ComboEffect.AllAoeDrainMp:
                case ComboEffect.AllAoeStripBuff:
                    st.CollectAlive(foeSide, tmp); MergeInto(into, tmp);
                    break;

                // ---- 最前排单体 ----
                case ComboEffect.MetalBreak:
                case ComboEffect.FrontShred:
                case ComboEffect.FrontTrue:
                    PickByRank(st, foeSide, into, host, true);
                    break;

                // ---- 敌方最高攻击 ----
                case ComboEffect.HighestAtkStrike:
                    PickOne(st, foeSide, into, PickHighestAtk);
                    break;

                // ---- 当前生命最低的敌人 ----
                case ComboEffect.LowestHpExecute:
                    PickOne(st, foeSide, into, PickLowestHp);
                    break;

                // ---- 随机 2~3 名（⚠ 不掷骰：预览降级为"该池全部存活"，由调用方标注"随机"）----
                case ComboEffect.RandomTwoHit:
                case ComboEffect.RandomTwoAdvance:
                case ComboEffect.RandomThreeHit:
                    st.CollectAlive(foeSide, tmp); MergeInto(into, tmp);
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
        /// 执行**阵形连携**（2026-10-16 全替换重构）：主兽（站中宫者）出大招能量 + 全队按存活数扣灵力，
        /// 按 ComboEffect 结算；威力随存活单位数提升。
        /// 返回 false = 条件不满足（阵形不齐 / 已用过 / 元气不足 / 灵力不足），不产生任何事件。
        ///
        /// ⛔ 条件口径与 <see cref="ComboRules.CanFire"/> **逐条一致**（UI 用它显示"差什么"）。
        /// ⛔ 真正被玩家点下的路径是"QTE 判定成功后才调本函数"（见 BattleSimulator.Steps 的连携段）。
        /// </summary>
        public static bool ExecuteCombo(BattleState st, string comboId, BattleUnit host)
        {
            var combo = ComboRules.For(comboId);
            string why;
            if (!ComboRules.CanFire(st, host, combo, out why)) return false;

            var cfg = st.Config;
            int alive = ComboRules.AliveCount(st, host.Side);
            int mpCost = ComboRules.MpCostFor(st, host.Side);

            // ---- 结算消耗（用户 2026-10-16 口径）----
            //  ① 灵力：每存活 1 只 1 点
            if (host.Side == TeamSide.Player) st.TeamMp -= mpCost;
            else st.EnemyMp -= mpCost;
            //  ② 主兽大招能量：整份（「无相」遗物关掉元气需求时不扣）
            if (cfg.UltimateNeedsRage) host.SpendRage(cfg.UltimateRageCost);
            st.UsedCombos.Add(comboId);

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: host.RuntimeId,
                       skillName: combo.Name, element: Element.None,
                       note: host.DisplayName + "·" + combo.Name + "（阵形·存活" + alive + "）",
                       skill: SkillType.Active);

            // ---- 威力：随存活数提升（0.4 + 0.12×存活，5 只 = 100%）----
            float aliveMul = ComboRules.PowerMulFor(alive);
            int power = CoreMath.RoundDamage(host.Attack * combo.Power * aliveMul);
            var foeSide = BattleState.Opponent(host.Side);
            var foes = st.UnitsOf(foeSide);
            var ours = st.UnitsOf(host.Side);

            switch (combo.Effect)
            {
                // ================= 全体敌方 =================
                case ComboEffect.WoodPulse:
                {
                    HitAll(st, host, foes, power);
                    HealTeam(st, host, ours, Pct(combo.Power2, ComboHealPct) * aliveMul);
                    break;
                }
                case ComboEffect.FireStorm:
                {
                    HitAll(st, host, foes, power);
                    break;
                }
                case ComboEffect.AllAoeStatus:
                {
                    HitAll(st, host, foes, power);
                    ApplyStatusOnFoes(st, host, foes, StatusCatalog.Frost, 2, 2);
                    break;
                }
                case ComboEffect.AllAoeBurnStack:
                {
                    // 每命中 1 名目标额外叠加 1 层灼烧（灼烧 DotFlat 按施法者攻击 30% 折算）
                    for (int i = 0; i < foes.Count; i++)
                    {
                        var u = foes[i];
                        if (!u.IsAlive) continue;
                        u.TakeDamage(power);
                        LogComboDamage(st, host, u, power);
                        u.ApplyStatus(StatusCatalog.Burn, 1, 2, host.Attack * 0.30f);
                        st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: host.RuntimeId,
                                   targetId: u.RuntimeId, amount: 1,
                                   note: "灼烧 ×" + u.GetStacks(StatusCatalog.Burn) + "（连携）");
                    }
                    break;
                }
                case ComboEffect.AllAoeDrainMp:
                {
                    int hitCount = 0;
                    for (int i = 0; i < foes.Count; i++)
                    {
                        var u = foes[i];
                        if (!u.IsAlive) continue;
                        u.TakeDamage(power);
                        LogComboDamage(st, host, u, power);
                        hitCount++;
                    }
                    // 每命中 1 名目标夺取 2 点**敌方灵力池**（转为我方）
                    int drained = hitCount * 2;
                    if (drained > 0)
                    {
                        int steal = CoreMath.Min(drained, st.EnemyMp);
                        st.EnemyMp -= steal;
                        st.TeamMp = CoreMath.Min(st.TeamMpMax, st.TeamMp + steal);
                        st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: host.RuntimeId,
                                   amount: steal, note: "连携·夺灵：我方灵力 +" + steal);
                    }
                    break;
                }
                case ComboEffect.AllAoeStripBuff:
                {
                    HitAll(st, host, foes, power);
                    for (int i = 0; i < foes.Count; i++)
                    {
                        var u = foes[i];
                        if (!u.IsAlive) continue;
                        int stripped = StripOneBuff(u);
                        if (stripped > 0)
                            st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: host.RuntimeId,
                                       targetId: u.RuntimeId, amount: stripped, note: "连携·剥离增益");
                    }
                    break;
                }

                // ================= 单体 / 前排 =================
                case ComboEffect.MetalBreak:
                    PickByRank(st, foeSide, foeBuf, host, true);
                    if (foeBuf.Count > 0 && foeBuf[0].IsAlive) { foeBuf[0].TakeDamage(power); LogComboDamage(st, host, foeBuf[0], power); }
                    break;

                case ComboEffect.FrontShred:
                case ComboEffect.FrontTrue:
                {
                    PickByRank(st, foeSide, foeBuf, host, true);
                    if (foeBuf.Count == 0 || !foeBuf[0].IsAlive) break;
                    var dst = foeBuf[0];
                    dst.TakeDamage(power);
                    LogComboDamage(st, host, dst, power);
                    if (!dst.IsAlive) break;
                    if (combo.Effect == ComboEffect.FrontShred)
                    {
                        dst.ApplyStatus(ComboShredStatus, 3, 2);
                        st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: host.RuntimeId,
                                   targetId: dst.RuntimeId, amount: 3,
                                   note: "裂甲 ×" + dst.GetStacks(ComboShredStatus) + "（连携·破防）");
                    }
                    else
                    {
                        int extra = CoreMath.RoundDamage(dst.MaxHp * Pct(combo.Power2, ComboTruePct));
                        int dealt = dst.TakeTrueDamage(extra);
                        st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: host.RuntimeId,
                                   targetId: dst.RuntimeId, amount: dealt, element: host.Element,
                                   note: "连携·碎地真伤");
                        if (dealt > 0 && !dst.IsAlive)
                            st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                                       note: dst.DisplayName + " 阵亡（连携·碎地）");
                    }
                    break;
                }

                case ComboEffect.HighestAtkStrike:
                {
                    PickOne(st, foeSide, foeBuf, PickHighestAtk);
                    if (foeBuf.Count > 0 && foeBuf[0].IsAlive) { foeBuf[0].TakeDamage(power); LogComboDamage(st, host, foeBuf[0], power); }
                    break;
                }

                case ComboEffect.LowestHpExecute:
                {
                    PickOne(st, foeSide, foeBuf, PickLowestHp);
                    if (foeBuf.Count == 0 || !foeBuf[0].IsAlive) break;
                    var dst = foeBuf[0];
                    int dmg = power;
                    if (dst.HasStatus(StatusCatalog.Burn)) dmg = CoreMath.RoundDamage(dmg * 1.40f);  // 带灼烧 +40%
                    dst.TakeDamage(dmg);
                    LogComboDamage(st, host, dst, dmg);
                    break;
                }

                // ================= 随机多目标 =================
                case ComboEffect.RandomTwoHit:
                case ComboEffect.RandomThreeHit:
                {
                    int n = combo.Effect == ComboEffect.RandomThreeHit ? 3 : 2;
                    for (int i = 0; i < n; i++)
                    {
                        st.CollectAlive(foeSide, rndBuf);
                        if (rndBuf.Count == 0) break;
                        var dst2 = rndBuf[st.Random.NextInt(0, rndBuf.Count)];
                        dst2.TakeDamage(power);
                        LogComboDamage(st, host, dst2, power);
                    }
                    break;
                }
                case ComboEffect.RandomTwoAdvance:
                {
                    for (int i = 0; i < 2; i++)
                    {
                        st.CollectAlive(foeSide, rndBuf);
                        if (rndBuf.Count == 0) break;
                        var dst2 = rndBuf[st.Random.NextInt(0, rndBuf.Count)];
                        dst2.TakeDamage(power);
                        LogComboDamage(st, host, dst2, power);
                    }
                    host.AdvanceActionGauge(ComboAdvance);     // 推进自身行动条
                    break;
                }

                // ================= 我方 =================
                // ⚠ 我方类连携的 ComboDef.Power 记的是**护盾系数（× 施法者攻击）**，
                //   回复百分比取 Power2（未填则用默认值）。文档里"回复 12% + 护盾 60% 攻击"
                //   这种双数值无法用一个 Power 表达，故回血比例统一走 Power2 / 默认值。
                case ComboEffect.TeamHealShield:
                {
                    HealTeam(st, host, ours, Pct(combo.Power2, 0.12f) * aliveMul);
                    ShieldTeam(st, host, ours, host.Attack * combo.Power * aliveMul);
                    break;
                }
                case ComboEffect.TeamShieldReflect:
                {
                    ShieldTeam(st, host, ours, host.Attack * combo.Power * aliveMul);
                    break;
                }
                case ComboEffect.TeamHealCleanse:
                {
                    HealTeam(st, host, ours, Pct(combo.Power2, 0.14f) * aliveMul);
                    for (int i = 0; i < ours.Count; i++)
                    {
                        var u = ours[i];
                        if (!u.IsAlive) continue;
                        int removed = u.RemoveStatus(null);     // null = 清除全部减益
                        if (removed > 0)
                            st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: host.RuntimeId,
                                       targetId: u.RuntimeId, amount: removed, note: "连携·驱散减益");
                    }
                    break;
                }
                case ComboEffect.TeamHealLostHp:
                {
                    float pct = Pct(combo.Power2, 0.15f) * aliveMul;
                    for (int i = 0; i < ours.Count; i++)
                    {
                        var u = ours[i];
                        if (!u.IsAlive) continue;
                        int lost = u.MaxHp - u.Hp;
                        int healed = u.Heal(CoreMath.RoundDamage(lost * pct));
                        if (healed > 0)
                            st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: host.RuntimeId,
                                       targetId: u.RuntimeId, amount: healed, element: host.Element,
                                       note: "连携·补损");
                    }
                    break;
                }
            }
            return true;
        }

        /// <summary>
        /// 阵形连携 **QTE 触发失败**（2026-10-16 用户口径）：
        ///   "如果触发失败则会扣除 1 个灵力和 50% 的主异兽大招能量，并提示触发失败"。
        /// ⚠ 失败**不标记** UsedCombos（阵形还在、元气攒回来可以再试），也不消耗行动
        ///   （调用方 Steps.cs 在失败后仍会走该单位本回合的正常行动）。
        /// </summary>
        public static void ComboFailed(BattleState st, BattleUnit host, ComboDef combo)
        {
            if (st == null || host == null) return;
            var cfg = st.Config;

            // ① 灵力 -1（不足则清 0，不欠账）
            if (host.Side == TeamSide.Player)
                st.TeamMp = st.TeamMp > 1 ? st.TeamMp - 1 : 0;
            else
                st.EnemyMp = st.EnemyMp > 1 ? st.EnemyMp - 1 : 0;

            // ② 主兽大招能量 -50%（「无相」关掉元气需求时不扣）
            if (cfg.UltimateNeedsRage) host.SpendRage(cfg.UltimateRageCost * 0.5f);

            // ③ 记一条"触发失败"（UI 直接显示 Note；结构化字段保持最小）
            st.Log.Add(st.Turn, BattleEventKind.ParryResult, actorId: host.RuntimeId,
                       note: "连携·" + (combo != null ? combo.Name : "?") + " 触发失败（灵力 -1，大招能量 -50%）");
        }

        // ================================================================
        //  连携内部小工具（全部无随机、无副作用）
        // ⚠ 这三个 List 是 **static 复用缓冲**：ExecuteCombo 是单线程同步调用，
        //   不会重入（项目里没有任何地方在连携结算中再调连携）。
        //   若日后要并行跑战斗模拟，这里必须改为按 BattleState 持有。
        // ================================================================
        private static readonly List<BattleUnit> foeBuf = new List<BattleUnit>(8);
        private static readonly List<BattleUnit> rndBuf = new List<BattleUnit>(8);

        private static float Pct(float v, float fallback) => v > 0f ? v : fallback;

        private static void HitAll(BattleState st, BattleUnit host, IReadOnlyList<BattleUnit> foes,
                                   int power)
        {
            for (int i = 0; i < foes.Count; i++)
            {
                var u = foes[i];
                if (!u.IsAlive) continue;
                u.TakeDamage(power);
                LogComboDamage(st, host, u, power);
            }
        }

        private static void ApplyStatusOnFoes(BattleState st, BattleUnit host,
                                             IReadOnlyList<BattleUnit> foes, string statusId,
                                             int stacks, int turns)
        {
            for (int i = 0; i < foes.Count; i++)
            {
                var u = foes[i];
                if (!u.IsAlive) continue;
                u.ApplyStatus(statusId, stacks, turns);
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: host.RuntimeId,
                           targetId: u.RuntimeId, amount: stacks,
                           note: StatusCatalog.Get(statusId).Name + " ×" + u.GetStacks(statusId) + "（连携）");
            }
        }

        private static void HealTeam(BattleState st, BattleUnit host, IReadOnlyList<BattleUnit> ours, float pct)
        {
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (!u.IsAlive) continue;
                int healed = u.Heal(CoreMath.RoundDamage(u.MaxHp * pct));
                if (healed > 0)
                    st.Log.Add(st.Turn, BattleEventKind.Heal, actorId: host.RuntimeId,
                               targetId: u.RuntimeId, amount: healed, element: host.Element,
                               note: "连携·回气");
            }
        }

        private static void ShieldTeam(BattleState st, BattleUnit host, IReadOnlyList<BattleUnit> ours, float amount)
        {
            int amt = CoreMath.RoundDamage(amount);
            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (!u.IsAlive) continue;
                int added = u.AddShield(amt, st.Turn);
                if (added > 0)
                    st.Log.Add(st.Turn, BattleEventKind.Shield, actorId: host.RuntimeId,
                               targetId: u.RuntimeId, amount: added, element: host.Element,
                               note: "连携·护体");
            }
        }

        /// <summary>剥离目标 1 个增益，返回剥离的数量（0/1）。</summary>
        private static int StripOneBuff(BattleUnit u)
        {
            for (int i = 0; i < u.Statuses.Count; i++)
            {
                if (u.Statuses[i].Def.IsDebuff) continue;
                u.RemoveStatusStacks(u.Statuses[i].Id, 1);
                return 1;
            }
            return 0;
        }

        private static void LogComboDamage(BattleState st, BattleUnit src, BattleUnit dst, int amount)
        {
            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: src.RuntimeId, targetId: dst.RuntimeId,
                       amount: amount, element: src.Element, note: "连携");
            if (!dst.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: dst.DisplayName + " 阵亡（连携）");
        }
    }
}
