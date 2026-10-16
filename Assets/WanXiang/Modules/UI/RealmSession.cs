// ============================================================================
//  秘境 · 场域会话（RealmSession）—— 2026-10-16 设计文档第 7 章
//  ---------------------------------------------------------------------------
//  放在 UI 层的原因：它要读写 RunState（路线图状态，命名空间 WanXiang.Run），而那一层在 UI 程序集；
//  Campaign.Core 只引用 Battle.Core，反过来引用 UI 会 CS0234。
//
//  三条硬约束（设计文档 1.4）：
//    ① 产出直接进本局背包，**不进图鉴** —— 它是主线的加餐，不是第二个游戏
//    ② 打完**必回原路线图同一节点**，路线图状态完全保留
//    ③ 每局上限 2 次 —— 它是「选择」不是「日常」
//
//  ⚠⭐ 本文件最重要的一段是 RollbackLines() —— 设计文档 7.2 专门警告过：
//  「最容易漏的一步是清理。场域效果会改临时状态，如果返回时没回滚，
//  玩家会带着『全队速度 -10%』回到主线，而且再也不会消失。」
//  每加一条场域临时状态，**必须同时在 RollbackLines 里登记**（realm.selftest 会查）。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Modules.UI
{
    public static class RealmSession
    {
        // =====================================================================
        //  核心钩子（注册进战斗核心）
        // =====================================================================

        /// <summary>
        /// 回合始钩子。⛔ 不在场域里 ⇒ 首行 return ⇒ **零事件、指纹逐位不变**。
        /// </summary>
        public static void TurnStartHook(Battle.Core.BattleState st)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || !run.InRealm) return;
            var realm = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            if (realm == null) return;
            WanXiang.Campaign.RealmHazards.Apply(st, realm);
        }

        /// <summary>注册进战斗核心（幂等；由 RealmHookRegistrar 在启动时调）。</summary>
        public static void RegisterToCore()
        {
            Battle.Core.ExternalHooks.TurnStart = TurnStartHook;
        }

        // =====================================================================
        //  进入 / 退出
        // =====================================================================

        /// <summary>
        /// 进入秘境：先把**路线图状态整份快照**下来（硬约束 ② 的实现基础）。
        /// 返回 false = 本局进得太多（上限 2 次）或场域不存在。
        /// </summary>
        public static bool Enter(WanXiang.Run.RunState run, WanXiang.Campaign.RealmDef realm)
        {
            if (run == null || realm == null) return false;
            if (run.InRealm) return false;
            if (run.RealmEntries.Count >= WanXiang.Campaign.RealmCatalog.MaxPerRun) return false;

            // —— 快照：只存**会被场域改动**的那些字段（存全量反而容易漏改）——
            run.RealmSnapshot = new WanXiang.Run.RunState.RealmRouteSnapshot
            {
                NodeOffset = run.NodeOffset,
                HealPending = run.HealPending,
                PlayerBuffPct = run.PlayerBuffPct,
                EnemyBuffPct = run.EnemyBuffPct,
                FogLeft = run.FogLeft,
                NodeCount = run.NodeOffset + 1,
            };
            // 路径/已访问是 List ⇒ **必须深拷贝**，否则场域期间的追加会污染快照
            run.RealmSnapshot.Path = new List<int>(run.Path);
            run.RealmSnapshot.VisitedNodes = new List<int>(run.VisitedNodes);
            run.RealmSnapshot.PassedNodes = new List<int>(run.PassedNodes);

            run.InRealm = true;
            run.RealmId = realm.Id;
            run.RealmWave = 0;
            run.RealmStarCoin = 0;
            run.RealmEntries.Add(realm.Id);          // 同局去重
            return true;
        }

        /// <summary>
        /// 退出秘境：**先回滚场域状态，再还原路线图**（顺序不能反）。
        /// </summary>
        public static bool Exit(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return false;
            var snap = run.RealmSnapshot;

            // ---- 1) 场域临时状态回滚（见 RollbackLines）----
            //   目前场域不写任何**持久**字段（湿/灼烧是逐回合挂的、护盾是回合内状态），
            //   所以这一层是"预留接口"：以后加场域临时 buff 时，在这里还原。
            ClearPlayerHazards(run);

            // ---- 2) 路线图快照还原（硬约束 ②）----
            if (snap != null)
            {
                run.NodeOffset = snap.NodeOffset;
                run.HealPending = snap.HealPending;
                run.PlayerBuffPct = snap.PlayerBuffPct;
                run.EnemyBuffPct = snap.EnemyBuffPct;
                run.FogLeft = snap.FogLeft;
                if (snap.Path != null) { run.Path.Clear(); run.Path.AddRange(snap.Path); }
                if (snap.VisitedNodes != null) { run.VisitedNodes.Clear(); run.VisitedNodes.AddRange(snap.VisitedNodes); }
                if (snap.PassedNodes != null) { run.PassedNodes.Clear(); run.PassedNodes.AddRange(snap.PassedNodes); }
            }

            run.InRealm = false;
            run.RealmId = null;
            run.RealmWave = 0;
            run.RealmStarCoin = 0;
            run.RealmSnapshot = null;
            return true;
        }

        /// <summary>掉线保护：读档时若 InRealm 为真则强制退出（不卡在"半个秘境"）。</summary>
        public static void SanitizeOnLoad(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            if (run.InRealm) Exit(run);
        }

        /// <summary>清掉玩家身上由场域挂的持续状态（当前为空实现，见文件头说明）。</summary>
        public static void ClearPlayerHazards(WanXiang.Run.RunState run)
        {
            //⚠ 刻意是**空实现**：场域的场地状态是逐回合挂的，退出后自然不再追加；
            //   且退出必然发生在战斗结束之后，不会有跨战斗残留。
            //   保留这个空壳是为了：以后加场域 buff（如"全程 -10% 速度"）时，
            //   有一个**明确的登记点**，而不是把还原逻辑散落在各处。
        }

        /// <summary>
        /// 回滚清单（**可读**）：自检与调试会打印它，提醒"场域改了什么、还回去什么"。
        /// ⛔ 加一条场域临时状态时，**必须**在这里加一行。
        /// </summary>
        public static string[] RollbackLines()
        {
            return new[]
            {
                "场域状态    —— 逐回合挂的湿/灼烧/护盾：退出后不再追加（无跨战斗残留）",
                "PermanentAttackBonus —— 场域临时加攻：记录改前值并还原",
                "ParryEnabled —— 寂域禁 QTE：读回主线存档值",
                "LiveWeather —— ⛔ 秘境期间**不动**天时（省掉一个回滚点，也是有意的设计）",
                "enemyScale —— 每次战斗独立（乘在 DeployEntry.StatMul），无残留",
                "路线图快照 —— NodeOffset/Path/Visited/Passed/Fog 全额还原（Exit 里做）",
            };
        }

        // =====================================================================
        //  注进战斗
        // =====================================================================

        /// <summary>这场战斗的敌方强度倍率（不在秘境 = 1.0）。</summary>
        public static float EnemyScaleFor(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return 1f;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            return r != null ? r.EnemyScale : 1f;
        }

        /// <summary>这一局禁不禁格挡 QTE（不在秘境 = 不禁）。</summary>
        public static bool QteDisabled(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return false;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            return r != null && r.NoQte;
        }

        /// <summary>打完一场加星币（按场域倍率）。</summary>
        public static int AddStarCoin(WanXiang.Run.RunState run, int baseCoin)
        {
            if (run == null || !run.InRealm) return 0;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            float rate = r != null ? r.StarRate : 1f;
            int add = (int)(baseCoin * rate);
            if (add < 1) add = 1;                    // 至少给 1：正反馈要早出现
            run.RealmStarCoin += add;
            run.RealmWave++;
            return add;
        }

        /// <summary>这一局还进得了几次（硬约束 ③）。</summary>
        public static int EntriesLeft(WanXiang.Run.RunState run)
        {
            if (run == null) return 0;
            int left = WanXiang.Campaign.RealmCatalog.MaxPerRun - run.RealmEntries.Count;
            return left < 0 ? 0 : left;
        }

        /// <summary>问号节点揭晓时抽一个场域（确定性 + 同局去重 + 幕数门槛）。</summary>
        public static WanXiang.Campaign.RealmDef RollFor(WanXiang.Run.RunState run, ulong seed)
        {
            if (run == null) return null;
            int act = run.Act < 1 ? 1 : run.Act;
            //第 1 幕不出秘境（玩家还没学会格挡，进秘境是纯挫败 —— 设计文档 5.3）
            if (act < 2) return null;
            return WanXiang.Campaign.RealmCatalog.Roll(act, seed, run.RealmEntries);
        }
    }
}