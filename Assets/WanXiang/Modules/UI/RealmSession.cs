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
        public static bool Enter(WanXiang.Run.RunState run, WanXiang.Campaign.RealmDef realm,
                                 bool bypassLimits = false)
        {
            if (run == null || realm == null) return false;
            if (run.InRealm) return false;
            // 调试旁路只放宽"每局次数"这一条；"已在秘境中"仍然要挡（那是状态机约束，不是配额）
            if (!bypassLimits && run.RealmEntries.Count >= WanXiang.Campaign.RealmCatalog.MaxPerRun)
                return false;

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
  if (run.RealmCleared == null) run.RealmCleared = new System.Collections.Generic.List<int>();
  run.RealmCleared.Clear();      // 每次进秘境是全新一趟
            if (run.RealmChampionRewarded == null)
                run.RealmChampionRewarded = new System.Collections.Generic.List<int>();
            run.RealmChampionRewarded.Clear();   // ★ 幂等表也必须清，否则第二次进来镇守不给奖励
      run.RealmCursor = -1;
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

            // ---- 1.5) 撤离代价（只有明确标了 PenalizesOnQuit 的场域才有）----
            //   ⛔ 放在"还原快照"**之前**：代价动的是本次秘境的临时账（灵卵），
            //     与主线字段无关；放后面会和快照还原的语义混在一起，以后加新代价容易搞错顺序。
            ApplyQuitPenalty(run);


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
            if (run.RealmCleared != null) run.RealmCleared.Clear();
    run.RealmCursor = -1;
            run.RealmSnapshot = null;
          return true;
        }

        /// <summary>
      /// 记一个秘境节点已通过（秘境进度**只写这里**）。
        /// ⛔ 绝不要顺手写 NodeOffset / VisitedNodes / PassedNodes —— 那是主线的存档。
        ///   借用的后果：中途存档/退出再进来，主线位置就停在秘境格上（且很难联想到是秘境干的）。
        /// </summary>
        public static void MarkCleared(WanXiang.Run.RunState run, int offset)
  {
   if (run == null) return;
            if (run.RealmCleared == null) run.RealmCleared = new System.Collections.Generic.List<int>();
  if (!run.RealmCleared.Contains(offset)) run.RealmCleared.Add(offset);
            run.RealmCursor = offset;
      run.RealmWave = run.RealmCleared.Count;
    }

        /// <summary>
        /// 撤离代价（2026-10-16，用户："后续也可以设计有一下秘境有惩罚"）。
        /// <para>目前只有一个实现：荆墟 —— 撤离清空本次秘境累积的星币
        /// （它是"赌坊"，中途离席就认赔；灵魂/遗物不受影响）。</para>
        /// <para>⛔ 加新代价时：① 在 <see cref="WanXiang.Campaign.RealmDef"/> 里填
        /// <c>PenalizesOnQuit</c> + <c>QuitPenaltyText</c>（确认框要照实说），
        /// ② 在这里补上真正生效的逻辑，③ 在 <see cref="RollbackLines"/> 登记一行。</para>
        /// </summary>
        public static void ApplyQuitPenalty(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            var realm = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            if (!WanXiang.Campaign.RealmCatalog.PenalizesOnQuit(realm)) return;

            // ⛔ 实扣字段（QuitPenaltyEggs）与文案（QuitPenaltyText）必须一起改 ——
            //   只有文案没扣数，就是又一次"假代价"（本项目已经栽过）。
            int cost = realm.QuitPenaltyEggs;
            if (cost <= 0) return;
            int paid = System.Math.Min(cost, run.Eggs);
            run.Eggs -= paid;
            UnityEngine.Debug.Log("[RealmSession] " + realm.Name + " 撤离代价：" +
                                  QuitPenaltyText(realm) + "（实扣灵卵 " + paid + "）");
        }

        /// <summary>撤离代价的一句话（确认框与日志共用，别再各写一份）。</summary>
        public static string QuitPenaltyText(WanXiang.Campaign.RealmDef realm)
        {
            return realm != null && !string.IsNullOrEmpty(realm.QuitPenaltyText)
                ? realm.QuitPenaltyText : "（无）";
        }

        /// <summary>掉线保护：读档时若 InRealm 为真则强制退出（不卡在"半个秘境"）。</summary>
        /// <remarks>
        /// ⛔⛔ **这个函数必须被调用**（2026-10-17 修）：它原先只定义、没有任何调用点 ——
        ///   于是"进场存了档、转场还没走完就退出"会在存档里留下 <c>InRealm = true</c>，
        ///   之后所有进入都被 <see cref="Enter"/> 的"已在秘境中"判据挡掉，
        ///   **玩家的体感就是"点秘境按钮没反应"**（连报错都没有，因为 Enter 只是 return false）。
        ///   现在由 <c>RunSave.Load</c> 统一调用；<see cref="RecoverStuck"/> 兜住"已加载但没重开"的情况。
        /// </remarks>
        public static void SanitizeOnLoad(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            if (!run.InRealm) return;
            UnityEngine.Debug.Log("[RealmSession] 读档发现卡在秘境内（InRealm=true，场域=" +
                                  run.RealmId + "）—— 强制退出并还原路线图");
            Exit(run);
        }

        /// <summary>
        /// 兜底自愈：当前 RunState 卡在"进了秘境但一个节点都没走过"的状态时把它退出。
        /// <para>判据 <c>RealmCursor &lt; 0 且 RealmCleared 为空</c> = **进场后一步都没走** ⇒
        ///   必然是"进场存档了但转场/界面没起来"的中断残留，不是玩家真的在玩。</para>
        /// <para>⚠ 只在这个极窄的条件下自愈；玩家已经走过格子就不能动（那是真进度）。</para>
        /// </summary>
        public static bool RecoverStuck(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return false;
            bool neverMoved = run.RealmCursor < 0
                              && (run.RealmCleared == null || run.RealmCleared.Count == 0);
            if (!neverMoved) return false;

            UnityEngine.Debug.LogWarning("[RealmSession] 自愈：检测到「进秘境但未开始」的中断残留" +
                                         "（场域=" + run.RealmId + "），先退出再重新进入");
            Exit(run);
            return true;
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
                "撤离代价 —— 仅 PenalizesOnQuit 的场域（荆墟清空本次星币），见 ApplyQuitPenalty",
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

        /// <summary>
        /// 这一局场域给我方的**速度乘数**（不在秘境 = 1.00）。
        /// <para>⛔ 只乘我方：场地代价是"这个环境在拖慢我"，不是把敌方也拖慢。</para>
        /// </summary>
        public static float PlayerSpeedMulFor(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return 1f;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            return r != null ? r.PlayerSpeedMul : 1f;
        }

        /// <summary>这一局禁不禁格挡 QTE（不在秘境 = 不禁）。</summary>
        public static bool QteDisabled(WanXiang.Run.RunState run)
        {
            if (run == null || !run.InRealm) return false;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            return r != null && r.NoQte;
        }

        /// <summary>
        /// 打完一场给灵卵（按场域 <c>EggRate</c> 倍率）。
        /// <para>⛔ 星币已删（2026-10-17 用户拍板）：它只是"灵卵 + 固定汇率"的中间步骤 ——
        ///   玩家要多记一个数字、多算一次除法，收益为零。产出直接给灵卵，
        ///   场域收益文案也随之变成"本域灵卵产出 ×N"。</para>
        /// </summary>
        public static int AddEggs(WanXiang.Run.RunState run, int baseEggs)
        {
            if (run == null || !run.InRealm) return 0;
            var r = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            float rate = r != null ? r.EggRate : 1f;
            int add = WanXiang.Battle.Core.CoreMath.RoundDamage(baseEggs * rate);
            if (add < 1) add = 1;                    // 至少给 1：正反馈要早出现
            run.Eggs += add;
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

        /// <summary>
        /// 抽场域用的**共享种子**。⚡ 必须由揭晓处与面板处**共用同一个函数** ——
        ///   两边各写一份种子公式就会"揭晓说是苍泽、面板抽出烬岭"的错位。
        /// </summary>
        public static ulong RealmSeedFor(WanXiang.Run.RunState run, int reroll = 0)
        {
            ulong s = (ulong)(run != null ? run.RunSeed : 0) * 31UL;
            s ^= WanXiang.Battle.Core.CoreMath.Fnv1a("realm-pick");
            // ★ reroll>0 必须换种子，否则"换一个"是**空操作**（2026-10-17 实测：
            //   苍泽 → 换一个 → 还是苍泽。根因：种子只由 RunSeed 决定，而 Roll 是纯函数）。
            //   ⚠ 只影响面板里"抽谁"；地图种子仍用 reroll=0，不跟着变。
            if (reroll > 0) s ^= WanXiang.Battle.Core.CoreMath.Fnv1a("realm-reroll:" + reroll);
            return s;
        }

        /// <summary>问号节点揭晓时抽一个场域（确定性 + 同局去重 + 幕数门槛）。</summary>
        /// <param name="excludeId">额外排除某个场域 —— "换一个"必须保证**真的换掉**，
        ///   不能靠"换了种子后大概率不同"（那会偶尔原地不动）。</param>
        public static WanXiang.Campaign.RealmDef RollFor(WanXiang.Run.RunState run, ulong seed,
                                                         bool bypassLimits = false,
                                                         string excludeId = null)
        {
            if (run == null) return null;
            int act = run.Act < 1 ? 1 : run.Act;

            // ★ 调试旁路（2026-10-17）：**不改 run 的任何字段**，只放宽"抽谁"的准入。
            //   ⛔ 早先的调试入口是"把 run.Act 临时抬到 2、清空 RealmEntries"，撤离再还原 ——
            //     那套做法一旦某条撤离路径漏了还原，玩家的幕数会被**永久**改掉。
            //     现在改成"判定放宽"，run 一个字节都不动，从根上没有还原问题。
            //   act 传 5 = 所有场域都可出现（含第 4 幕才出的寂域）。
            if (bypassLimits)
            {
                var only = string.IsNullOrEmpty(excludeId)
                    ? null : new System.Collections.Generic.List<string> { excludeId };
                // 排除后没得选就退回不排除（宁可能抽到同一个，也别给玩家一个空面板）
                return WanXiang.Campaign.RealmCatalog.Roll(5, seed, only)
                       ?? WanXiang.Campaign.RealmCatalog.Roll(5, seed, null);
            }

            //第 1 幕不出秘境（玩家还没学会格挡，进秘境是纯挫败 —— 设计文档 5.3）
            if (act < 2) return null;
            if (string.IsNullOrEmpty(excludeId))
                return WanXiang.Campaign.RealmCatalog.Roll(act, seed, run.RealmEntries);

            var used = new System.Collections.Generic.List<string>(run.RealmEntries) { excludeId };
            return WanXiang.Campaign.RealmCatalog.Roll(act, seed, used)
                   ?? WanXiang.Campaign.RealmCatalog.Roll(act, seed, run.RealmEntries);
        }
    }
}