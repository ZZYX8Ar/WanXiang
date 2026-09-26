// ============================================================================
//  万相 · 战斗核心 · Action（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
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
        /// 一个单位的行动。技能优先级：绝技 → 战技 → 普攻。
        /// （STEP 1 走自动战斗；绝技在正式玩法里是玩家的手动干预点，见 BattleConfig.AutoCastUltimate。）
        /// </summary>
        private static void ExecuteAction(BattleState st, BattleUnit actor, Buffers buf)
        {
            var cfg = st.Config;
            actor.PursuitUsedThisAction = false;   // 09 芒种：追击"每次行动限 1 次"的计数
            st.Log.Add(st.Turn, BattleEventKind.ActionBegin, actorId: actor.RuntimeId,
                       note: actor.DisplayName);

            if (!actor.CanAct)
            {
                // 冻结之类的"不能行动"。注意**不能跳过回合末的 CD 推进** ——
                // 被冻住不等于技能也跟着暂停，否则控制队会被自己坑。
                st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId, note: "无法行动");
                return;
            }

            var skill = ChooseSkill(st, actor);
            if (skill == null || skill.Effects == null || skill.Effects.Length == 0)
            {
                st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId, note: "没有可用技能");
                return;
            }

            // ---- v2.1 P4：普攻（无冷却）默认打「最前排」----
            //  ⚠ 抽成 EffectiveSkill 并与预览共用：否则面板高亮会按"原始技能"算（如生命最低者），
            //    与实际打的"最前排"不一致（用户实测报障：高亮在一格、伤害飘在另一格）。
            skill = EffectiveSkill(skill);

            // ★ 普攻回灵（2026-09-25 用户定案）：我方每次普攻额外 +1 灵力 ——
            //   让"一直普攻"也能攒出战记（原来只有每回合 +2 的自然回复）。
            //   ⚠ 只给我方：敌方若同样回灵等于顺手加强敌人；要对称改这一行即可。
            if (actor.Side == TeamSide.Player && skill.Cd == 0)
                st.TeamMp = System.Math.Min(st.TeamMpMax, st.TeamMp + 1);

            st.Log.Add(st.Turn, BattleEventKind.SkillCast, actorId: actor.RuntimeId,
                       skillName: skill.Name, element: ResolveElement(Element.None, skill, actor),
                       note: $"{actor.DisplayName}·{Cn.Of(skill.Type)}", skill: skill.Type);

            for (int i = 0; i < skill.Effects.Length; i++)
                ResolveAtom(st, actor, skill, skill.Effects[i], buf);

            // 冷却：绝技额外吃「同属共鸣」的 CD 缩减（STEP 1 的替身用法，见 BattleUnit.ResonanceCdDelta）
            int cdDelta = skill.Type == SkillType.Ultimate ? actor.ResonanceCdDelta : 0;
            actor.PutOnCooldown(skill.Type, skill.Cd, cdDelta);

            if (skill.Type == SkillType.Basic) actor.AddRage(cfg.RagePerBasicAttack);
            if (skill.Type == SkillType.Ultimate) actor.SpendRage(cfg.UltimateRageCost);
            // ★ 觉醒技：同样耗满额元气（用户定案），且伤害额外 ×1.4（"觉醒技就是厉害"）
            if (skill.Type == SkillType.Awaken) actor.SpendRage(cfg.UltimateRageCost);

            st.Log.Add(st.Turn, BattleEventKind.ActionEnd, actorId: actor.RuntimeId);
        }
        private static SkillDef ChooseSkill(BattleState st, BattleUnit u)
        {
            var cfg = st.Config;
            if (!u.CanCastSkills) return u.GetSkill(SkillType.Basic);   // 05 清明的对立面：沉默只封技能

            // ---- 回合制 v2.1 P2：灵力裁决 ----
            //  先按既有规则（玩家指令优先 / AI）选出技能，再统一结算灵力消耗：
            //  不够就落回普攻（普攻 0 耗，永远可用的兜底，避免回合卡死）。
            {
                var slot = DecideSlot(st, u, out var chosen);
                if (chosen != null)
                {
                    int cost = BattleState.MpCostOf(slot);
                    // ★ 灵力**按阵营各自结算**（2026-09-25 用户定案）：
                    //   以前双方共用 st.TeamMp ⇒ 敌方 AI 放战记花的是**玩家的**灵力
                    //   ⇒ 玩家一直普攻也攒不出战记（用户实测报障）。
                    bool mine = u.Side == TeamSide.Player;
                    int pool = mine ? st.TeamMp : st.EnemyMp;
                    if (cost <= pool)
                    {
                        if (mine) st.TeamMp -= cost;
                        else st.EnemyMp -= cost;
                        return chosen;
                    }
                }
                return u.GetSkill(SkillType.Basic);
            }
            //  SkillIndex 直接用 SkillType 的枚举值（Basic/Active/Ultimate），-1 = 未指定（走 AI）。
            //  不可用（冷却/元气不足）时回退普攻 —— 玩家的选择不该把回合卡死，
            //  但界面在点之前就该禁用按钮，所以这里的回退只是兜底。
            if (st.PlayerControlled && u.Side == TeamSide.Player
                && st.PendingCommand.Valid && st.PendingCommand.ActorId == u.RuntimeId
                && st.PendingCommand.SkillIndex >= 0)
            {
                var want = (SkillType)st.PendingCommand.SkillIndex;
                st.PendingCommand = default;      // 一令一用：消费掉，避免影响下一个单位
                var picked = u.GetSkill(want);
                bool usable = picked != null && (want == SkillType.Basic || u.CanCast(want, cfg));
                if (usable) return picked;
                return u.GetSkill(SkillType.Basic);
            }

            return u.GetSkill(SkillType.Basic);
        }
        /// <summary>
        /// 决定"这个单位这次放哪个槽"（不扣灵力）：玩家指令优先，其次 AI 规则。
        /// 灵力消耗交给 ChooseSkill 统一结算。
        /// </summary>
        private static SkillType DecideSlot(BattleState st, BattleUnit u, out SkillDef chosen)
        {
            var cfg = st.Config;

            // ---- 玩家指令优先（P1-2）----
            if (st.PlayerControlled && u.Side == TeamSide.Player
                && st.PendingCommand.Valid && st.PendingCommand.ActorId == u.RuntimeId
                && st.PendingCommand.SkillIndex >= 0)
            {
                var want = (SkillType)st.PendingCommand.SkillIndex;
                st.PendingCommand = default;      // 一令一用
                var picked = u.GetSkill(want);
                if (picked != null && (want == SkillType.Basic || u.CanCast(want, cfg)))
                {
                    chosen = picked;
                    return want;
                }
                chosen = u.GetSkill(SkillType.Basic);
                return SkillType.Basic;
            }

            // ---- AI 规则（按策略组调整，v2.1 P4；只影响敌方，我方由玩家下令）----
            var ult = u.GetSkill(SkillType.Ultimate);
            var act = u.GetSkill(SkillType.Active);

            if (cfg.AiProfile == AiProfile.Aggressive)
            {
                // 激进：资源优先兑现成伤害 —— 终结技能放就放，不等时机
                if (ult != null && u.CanCast(SkillType.Ultimate, cfg)) { chosen = ult; return SkillType.Ultimate; }
                if (act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }
            else if (cfg.AiProfile == AiProfile.Cautious)
            {
                // 稳健：生命低于一半才舍得放战记（其余普攻攒着），终结技同理
                bool hurt = u.HpPercent < 0.5f;
                if (!hurt && act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }
            else
            {
                // 均衡（默认）：沿用原规则
                if (ult != null && cfg.AutoCastUltimate && u.CanCast(SkillType.Ultimate, cfg))
                { chosen = ult; return SkillType.Ultimate; }
                if (act != null && u.CanCast(SkillType.Active, cfg)) { chosen = act; return SkillType.Active; }
            }

            chosen = u.GetSkill(SkillType.Basic);
            return SkillType.Basic;
        }
        // ================================================================
        //  先手连击（GDD v1.1 §3.6，v1.1 新增规则）
        //  ----------------------------------------------------------------
        //  为什么要有这条：回合制里"先出手"本身价值有限（早 0.1 秒行动还是一回合），
        //  导致「疾」这个职业拿 1100 生命 / 130 攻击换来一个几乎没价值的属性。
        //  这条规则给速度确权 —— 跨过门槛直接**多打一次**。
        //
        //  判定：每回合生成出手序列前，若某单位速度 ≥ 敌方当前最高速度 × 门槛，
        //        该单位本回合常规行动后额外行动一次；**每方每回合最多 1 次**。
        //  门槛：BattleConfig.InitiativeRatio（1.50）；大雪节点降到 1.20。
        //
        //  ⚠ 「敌方当前最高速度」取**本回合开始时**的快照（含天时/状态修正），
        //    不取行动过程中的动态值 —— 否则先手方打掉对方最快的单位时，
        //    连击资格会凭空出现，规则变得不可预测、也无法在回合开始时预告给玩家。
        // ================================================================

        private static BattleUnit _initiativePlayer;        private static BattleUnit _initiativeEnemy;        private static bool _initiativePlayerUsed;        private static bool _initiativeEnemyUsed;
        private static void ResolveInitiativeChain(BattleState st, Buffers buf)
        {
            float ratio = st.Weather != null
                ? st.Weather.InitiativeRatioOrDefault(st.Config.InitiativeRatio)
                : st.Config.InitiativeRatio;

            _initiativePlayer = PickInitiativeUnit(st, TeamSide.Player, ratio);
            _initiativeEnemy = PickInitiativeUnit(st, TeamSide.Enemy, ratio);
            _initiativePlayerUsed = false;
            _initiativeEnemyUsed = false;

            if (_initiativePlayer != null)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _initiativePlayer.RuntimeId,
                           note: $"先手连击：{_initiativePlayer.DisplayName} 本回合额外行动（门槛 {ratio:F2}×）");
            if (_initiativeEnemy != null)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _initiativeEnemy.RuntimeId,
                           note: $"先手连击（敌）：{_initiativeEnemy.DisplayName} 本回合额外行动（门槛 {ratio:F2}×）");
        }
        /// <summary>挑出本回合获得额外行动的单位：速度达标者里最快的那个（每方最多 1 个）。</summary>
        private static BattleUnit PickInitiativeUnit(BattleState st, TeamSide side, float ratio)
        {
            float oppMax = 0f;
            var opp = st.UnitsOf(side == TeamSide.Player ? TeamSide.Enemy : TeamSide.Player);
            for (int i = 0; i < opp.Count; i++)
            {
                var u = opp[i];
                if (!u.IsAlive) continue;
                float sp = st.EffectiveSpeed(u);
                if (sp > oppMax) oppMax = sp;
            }
            if (oppMax <= 0f) return null;

            BattleUnit best = null;
            float bestSpeed = 0f;
            var mine = st.UnitsOf(side);
            for (int i = 0; i < mine.Count; i++)
            {
                var u = mine[i];
                if (!u.IsAlive) continue;
                float sp = st.EffectiveSpeed(u);
                if (sp < oppMax * ratio) continue;
                if (best == null || sp > bestSpeed) { best = u; bestSpeed = sp; }
            }
            return best;
        }
        /// <summary>常规行动结束后补上那次额外行动（同一回合内只补一次）。</summary>
        private static void ResolveInitiativeExtraAction(BattleState st, Buffers buf)
        {
            ExtraActionFor(st, buf, _initiativePlayer, TeamSide.Player, ref _initiativePlayerUsed);
            ExtraActionFor(st, buf, _initiativeEnemy, TeamSide.Enemy, ref _initiativeEnemyUsed);
        }
        private static void ExtraActionFor(BattleState st, Buffers buf, BattleUnit u,
                                           TeamSide side, ref bool used)
        {
            if (used || u == null) return;
            used = true;
            if (!u.IsAlive || !u.CanAct) return;      // 这回合被打死/被控就不补了
            ExecuteAction(st, u, buf);
        }
        /// <summary>
        /// 「吞噬」劫象（§5.5）：行动结束后剥离对侧 1 个增益（第一个非减益状态），
        /// 自身损失 4% 最大生命（真实伤害）。解法：速攻 —— 别给它行动机会。
        /// </summary>
        private static void DevourAfterAction(BattleState st, BattleUnit actor)
        {
            if (actor == null || !actor.TraitDevour || !actor.IsAlive) return;
            if (!actor.CanAct) return;

            var foes = st.UnitsOf(actor.Side == TeamSide.Player ? TeamSide.Enemy : TeamSide.Player);
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                if (!f.IsAlive) continue;
                for (int k = f.Statuses.Count - 1; k >= 0; k--)
                {
                    if (f.Statuses[k].Def.IsDebuff) continue;
                    var inst = f.Statuses[k];
                    string name = inst.Def.Name;
                    inst.Stacks -= 1;
                    if (inst.Stacks <= 0) f.Statuses.RemoveAt(k);
                    else f.Statuses[k] = inst;
                    st.Log.Add(st.Turn, BattleEventKind.StatusRemoved, actorId: actor.RuntimeId,
                               targetId: f.RuntimeId, note: $"天时·吞噬：剥离 {name}");
                    break;
                }
                break;   // 每次行动只剥一个目标的一个增益
            }

            int self = CoreMath.RoundDamage(actor.MaxHp * 0.04f);
            if (self > 0)
            {
                int dealt = actor.TakeTrueDamage(self);
                st.Log.Add(st.Turn, BattleEventKind.Damage, targetId: actor.RuntimeId,
                           amount: dealt, note: "天时·吞噬：自损");
                if (!actor.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: actor.RuntimeId,
                               note: $"{actor.DisplayName} 阵亡（吞噬自损）");
            }
        }
        /// <summary>
        /// 03 惊蛰「蛰虫始振」/「复苏」劫象共用：虫卵倒计时，到点破卵复活。
        /// ⚠ 复苏劫象的卵**不依赖天时**也能孵（EndOfTurn 的调用条件是"有天时或有卵"）。
        /// </summary>
        private static void TickEggHatch(BattleState st, BattleUnit u)
        {
            if (!u.HasEgg) return;
            u.EggTurnsLeft--;
            if (u.EggTurnsLeft > 0) return;

            int hp = CoreMath.RoundDamage(u.MaxHp * u.EggReviveHpPercent);
            u.ReviveAtHp(hp);
            st.Log.Add(st.Turn, BattleEventKind.Revive, actorId: u.RuntimeId, targetId: u.RuntimeId,
                       amount: u.Hp,
                       note: $"{(u.EggFromTrait ? "劫象·复苏" : "天时·蛰虫始振")}：{u.DisplayName} 破卵而生");
        }
    }
}
