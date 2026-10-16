// ============================================================================
//  秘境 · 场地效果（RealmHazards）—— 2026-10-16 设计文档第 5 章
//  ---------------------------------------------------------------------------
//  放在 Campaign.Core 的原因：它只依赖 Battle.Core（战斗状态），**不依赖 UI 的 RunState**。
//  分层：`WanXiang.Campaign.Core` 只引用 `WanXiang.Battle.Core`，所以任何碰 RunState
//  的逻辑都必须放在 UI 层（见 WanXiang.Modules.UI.RealmSession）。
//
//  ⛔ 不在场域里 ⇒ 调用方直接 return ⇒ 零事件、指纹逐位不变。
// ============================================================================

namespace WanXiang.Campaign
{
    public static class RealmHazards
    {
        /// <summary>
        /// 场域的回合始效果：给一方全体挂状态或护盾。
        /// ⚠ **调用方必须先判"是否在场域中"**（见 <c>ExternalHooks.RunTurnStart</c> 的注册方）。
        /// </summary>
        public static void Apply(Battle.Core.BattleState st, RealmDef realm)
        {
            if (st == null || realm == null) return;
            switch (realm.Hazard)
            {
                case RealmHazardKind.FoeStatusAll:
                    StatusToAll(st, Battle.Core.TeamSide.Enemy,
                                realm.HazardStatusId, realm.HazardStacks, realm.HazardTurns, realm.Name);
                    break;
                case RealmHazardKind.FoeShieldAll:
                    ShieldToAll(st, Battle.Core.TeamSide.Enemy,
                                realm.HazardShieldPct, realm.HazardTurns, realm.Name);
                    break;
                case RealmHazardKind.SelfShieldAll:
                    // 寂域的"蓄盾"：禁了 QTE 就必须有替代手段，否则难度是纯惩罚
                    ShieldToAll(st, Battle.Core.TeamSide.Player,
                                realm.HazardShieldPct, realm.HazardTurns, realm.Name);
                    break;
            }
        }

        private static void StatusToAll(Battle.Core.BattleState st, Battle.Core.TeamSide side,
            string statusId, int stacks, int turns, string realmName)
        {
            if (st == null || string.IsNullOrEmpty(statusId)) return;
            var def = Battle.Core.StatusCatalog.Get(statusId);
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                u.ApplyStatus(statusId, stacks, turns);
                st.Log.Add(st.Turn, Battle.Core.BattleEventKind.StatusApplied,
                           targetId: u.RuntimeId, note: realmName + "·场地：" + def.Name);
            }
        }

        private static void ShieldToAll(Battle.Core.BattleState st, Battle.Core.TeamSide side,
            float pctOfMaxHp, int turns, string realmName)
        {
            if (st == null || pctOfMaxHp <= 0f) return;
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                int amt = (int)(u.MaxHp * pctOfMaxHp);
                if (amt <= 0) continue;
                u.AddShield(amt, turns);
                st.Log.Add(st.Turn, Battle.Core.BattleEventKind.Shield,
                           targetId: u.RuntimeId, amount: amt, note: realmName + "·场地护盾");
            }
        }
    }
}