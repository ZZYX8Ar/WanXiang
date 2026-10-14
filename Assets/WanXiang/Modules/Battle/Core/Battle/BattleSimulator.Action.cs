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
                    //   以前双方共用 st.TeamMp ， 敌方 AI 放战记花的是**玩家的**灵力
                    //   ， 玩家一直普攻也攒不出战记（用户实测报障）。
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
        // （先手连击机制已于 2026-10-14 移除：用户认为"每回合额外行动一次"观感怪异，
        //   且会让速度快的单位每回合行动两次。现每个单位每回合固定只行动一次。）

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
