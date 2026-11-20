// ============================================================================
//  万相 · 转场与延迟落地（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
//  ⚠ 含落印动画"压透明期推迟、恢复后补播"逻辑（用户 11-16 实测修复），勿动。
// ============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public sealed partial class CampaignPanel
    {
        internal static bool IsFinaleCellCommit()
        {
            if (PendingCommit < 0) return false;
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.Act >= 5) return false;      // 第 5 幕终局走另一套
            try
            {
                var g = WanXiang.Campaign.SolarTermGraph.BuildRoute(
                    run.Act, WanXiang.Campaign.SolarTermGraph.RouteSeed(run.RunSeed, run.Act),
                    WanXiang.Campaign.MapRules.Layers);
                return g != null && PendingCommit == g.NodeCount - 1;
            }
            catch { return false; }
        }

        /// <summary>
        /// 幕末守关胜利后的转场流程：**不先显示节点地图**。
        /// 直接落地（Act++）→ 播本幕转场 → 转场结束后才打开地图。
        /// </summary>
        private static CanvasGroup _transitionMapCg;

        /// <summary>关闭奖励面板前把已在栈里的地图压透明（防止 Resume 闪一帧）。</summary>
        internal static void HideMapForTransition()
        {
            var cp = UnityEngine.Object.FindObjectOfType<CampaignPanel>(true);
            if (cp == null) return;
            var cg = cp.GetComponent<UnityEngine.CanvasGroup>();
            if (cg == null) cg = cp.gameObject.AddComponent<UnityEngine.CanvasGroup>();
            cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
            _transitionMapCg = cg;
            Debug.Log("[Campaign] 关闭奖励前：旧地图已压透明（不 Resume 闪帧）");
            AutoRestoreMapIfStuckAsync().Forget();   // 兜底：任何异常路径都不会让地图永久黑屏
        }

        /// <summary>
        /// 兜底：压透明后 20 秒还没有任何恢复调用（例如走了战败/异常分支），
        /// 强制把地图恢复可见 —— 宁可闪一下，也不能永久黑屏。
        /// </summary>
        private static async Cysharp.Threading.Tasks.UniTaskVoid AutoRestoreMapIfStuckAsync()
        {
            int guard = 0;
            while (guard < 1200 && _transitionMapCg != null && _transitionMapCg.alpha < 0.01f)
            {
                guard++;
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            if (_transitionMapCg != null && _transitionMapCg.alpha < 0.01f)
            {
                Debug.LogWarning("[Campaign] 地图透明无人恢复（" + guard + " 帧）→ 强制恢复，避免黑屏");
                ShowMapAfterTransition();
            }
        }

        /// <summary>转场播完恢复地图可见。</summary>
        internal static void ShowMapAfterTransition()
        {
            if (_transitionMapCg == null)
            {
                var cp = UnityEngine.Object.FindObjectOfType<CampaignPanel>(true);
                if (cp != null) _transitionMapCg = cp.GetComponent<UnityEngine.CanvasGroup>();
            }
            if (_transitionMapCg != null)
            {
                _transitionMapCg.alpha = 1f; _transitionMapCg.blocksRaycasts = true; _transitionMapCg.interactable = true;
                Debug.Log("[Campaign] 转场播完：地图恢复可见");
                // ★ 若落印/画线动画因"地图不可见"被推迟，这里补播（等画面稳定一点再播）
                var cpNow = UnityEngine.Object.FindObjectOfType<CampaignPanel>(true);
                if (cpNow != null && cpNow._pendingAnim)
                    cpNow.StartCoroutine(cpNow.PlayPendingAnimAfter(0.35f));
            }
            _transitionMapCg = null;
        }

        internal static void BeginActTransitionFlow()
        {
            RewardAwaitingConfirm = false;
            DirectTransitionFlow().Forget();
        }

        private static async Cysharp.Threading.Tasks.UniTaskVoid DirectTransitionFlow()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            int fromAct = run.Act;
            Debug.Log("[Campaign] ★直接转场流程：第 " + fromAct + " 幕末格，先落地再播转场（不显示地图）");
            // 关键：**打开前**先预热实例并压到完全透明，否则 OpenAsync 那一帧会闪出旧幕地图。
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            var pre = await ui.PrewarmAsync<CampaignPanel>();
            var cg = pre != null ? pre.GetComponent<UnityEngine.CanvasGroup>() : null;
            if (pre != null && cg == null) cg = pre.gameObject.AddComponent<UnityEngine.CanvasGroup>();
            if (cg != null) { cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }
            var cp = await ui.OpenAsync<CampaignPanel>();
            if (cp == null) return;
            // 等转场面板播完并关闭
            int guard = 0;
            while (guard < 2400)
            {
                var intro = UnityEngine.Object.FindObjectOfType<WanXiang.Modules.UI.ActIntroPanel>(true);
                if (intro != null && intro.gameObject.activeInHierarchy) break;
                guard++;
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            int guard2 = 0;
            while (guard2 < 2400)
            {
                var intro = UnityEngine.Object.FindObjectOfType<WanXiang.Modules.UI.ActIntroPanel>(true);
                if (intro == null || !intro.gameObject.activeInHierarchy) break;
                guard2++;
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            ShowMapAfterTransition();
            Debug.Log("[Campaign] 转场播完（等 " + guard + "+" + guard2 + " 帧）→ 显示第 " + run.Act + " 幕地图");
        }

        private static bool _deferredCommitScheduled;

        /// <summary>奖励确认后的唯一落地入口：OnRefresh / OnResume / OnOpenAsync 三处都收敛到这里，只跑一次。</summary>
        private void ScheduleDeferredCommit()
        {
            if (_deferredCommitScheduled) return;
            _deferredCommitScheduled = true;
            DeferredCommitAsync().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid DeferredCommitAsync()
        {
            await WaitResultClosedAsync();
            _deferredCommitScheduled = false;
            // 奖励关闭后：如果 pending 已被别处消费，就不重复落地
            if (PendingCommit < 0)
            {
                Debug.Log("[Campaign] 延后落地：pending 已消费，仅刷新视图");
                BuildNodeMap();
                ApplyViewAfterGraphChange(silentSelect: true);
                return;
            }
            Debug.Log("[Campaign] ★奖励已确认 → 唯一一次落地 PendingCommit=" + PendingCommit);
            bool terminal = LandPendingCommit();
            if (terminal) return;                 // 终局战/天阙分支会自己接管
            BuildNodeMap();
            ApplyViewAfterGraphChange(silentSelect: true);
        }

        /// <summary>奖励面板（ResultPanel，含 draft 三选一）是否还开着。</summary>
        internal static bool ResultPanelIsOpen()
        {
            if (RewardAwaitingConfirm) return true;   // ★ 闸门优先：结算未完成就当作"奖励界面还在"
            var rp = UnityEngine.Object.FindObjectOfType<WanXiang.Modules.UI.ResultPanel>(true);
            return rp != null && rp.gameObject.activeInHierarchy;
        }

        /// <summary>所有幕间转场的统一等待：奖励界面确认并关闭后才允许播。</summary>
        internal static async Cysharp.Threading.Tasks.UniTask WaitResultClosedAsync()
        {
            int guard = 0;
            while ((RewardAwaitingConfirm || ResultPanelIsOpen()) && guard < 1800)
            {
                guard++;
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            if (guard >= 1800)
            {
                // 兜底：ResultPanel 从未打开/异常未关闭时，不能让闸门永久卡死节点推进
                Debug.LogWarning("[Campaign] 等待奖励确认超时 → 强制放行（避免卡关）");
                RewardAwaitingConfirm = false;
            }
            else if (guard > 0) Debug.Log("[Campaign] 已等奖励界面关闭 " + guard + " 帧，允许转场");
        }

        private bool LandPendingCommit()
        {
            // ★★ 2026-11-15 用户实测："没点奖励确定就播第四幕转场，点确定又播第五幕"
            //   根因：落地（Act++/换幕图/播转场）在 OnOpenAsync 里同步发生，而 ResultPanel 还开着。
            //   → 门闩：奖励面板未确认前，绝不落地、绝不推进幕、绝不播转场。
            if (ResultPanelIsOpen())
            {
                Debug.Log("[Campaign] 奖励界面未确认 → 挂起落地（PendingCommit=" + PendingCommit + " 保留）");
                ScheduleDeferredCommit();   // ★ 奖励关闭后由唯一入口落地一次，避免多点重复
                return true;   // true = 调用方不建图、提前返回
            }
            // ★★ 2026-10-20：出征/战斗期间**禁止**落地通过。
            //   出征时编队面板关闭 → 地图 Resume → 这里会立刻把待推进节点落地，
            //   于是"仗还没打，节点已通过；战斗中退游戏，回来就是已通过"（用户实测）。
            if (SceneFlow.BattleDeploying)
            {
                Debug.Log("[Campaign] 出征中（战斗未结算），暂不落地节点通过");
                return false;
            }
                if (PendingCommit >= 0 && !UIPanelBase.GhostSilent)
                {
       _justPassedNode = PendingCommit;   // ★ 真·通过（这里才把 NodeOffset 落地）， 记下它供落印/画线动画
                    var prun = WanXiang.Run.RunSave.Current;
                    if (prun != null)
                {
                        // ★ 2026-11-01 共享主等级：通过节点给经验（精英/首领更多），
                        //   上阵 5 人共享这一条等级（设计稿 §6.1）。
                        {
                            int kindVal = (_graph != null && _graph.Kinds != null &&
                                           PendingCommit >= 0 && PendingCommit < _graph.Kinds.Length)
                                          ? (int)_graph.Kinds[PendingCommit] : -1;
                            // ★ 2026-11-02：经验值读 ProgressionTuning（progression.csv 可覆盖）
                            int gain = WanXiang.Run.ProgressionTuning.ExpWinNormal;   // 普通战斗 / 事件
                            bool isFinale = _graph != null && PendingCommit == _graph.NodeCount - 1;
                            if (isFinale) gain = WanXiang.Run.ProgressionTuning.ExpWinBoss;   // 幕末首领
                            else if (kindVal == (int)WanXiang.Campaign.NodeKind.Elite)
                                gain = WanXiang.Run.ProgressionTuning.ExpWinElite;
                            else if (_realmMode) gain = WanXiang.Run.ProgressionTuning.ExpWinRealm;   // 秘境
                            // ★ 2026-11-02：血量结算搬到 BattlePanel.RecordHpAfterBattle（剩余 +50%，上限 100%）。
                            //   ⛔ 这里原来有个"把所有人抬到至少 50%"的兜底 —— 它会把 30% 的残血
                            //      直接抹成 50%，等于把"打完加 50%"的规则盖掉，已删除。

                            // ★ 2026-11-02 素材掉落：打赢硬仗才给（普通 0 / 精英 1 / 幕末 2）
                            {
                                int matGain = isFinale
                                    ? WanXiang.Run.ProgressionTuning.MaterialWinBoss
                                    : (kindVal == (int)WanXiang.Campaign.NodeKind.Elite
                                        ? WanXiang.Run.ProgressionTuning.MaterialWinElite : 0);
                                if (matGain > 0)
                                {
                                    prun.Materials += matGain;
                                    Debug.Log("[Campaign] 获得进化素材 +" + matGain + "（现有 " + prun.Materials + "）");
                                }
                            }

                            // B4-3：精英「残碑」——持一印时 60%（种子确定性）发现残碑 → 土之印·二
                            if (kindVal == (int)WanXiang.Campaign.NodeKind.Elite
                                && prun.GrantedItems.Contains("item_tu_1")
                                && !prun.GrantedItems.Contains("item_tu_2"))
                            {
                                var srng = new WanXiang.Battle.Core.DeterministicRandom(
                                    WanXiang.Battle.Core.CoreMath.Fnv1a("stele:" + prun.RunSeed + ":" + prun.Act));
                                if (srng.NextFloat() < 0.60f)
                                {
                                    prun.GrantedItems.Add("item_tu_2");
                                    Debug.Log("[Campaign] ★残碑现世 → 土之印·二（印记 " + prun.TuMarkCount() + "/3）");
                                }
                            }

                            int ups = prun.AddExp(gain);
                            if (ups > 0)
                                Debug.Log("[Campaign] 主等级提升 → Lv" + prun.PlayerLevel + "（经验 +" + gain + "）");
                        }

           // ⛔⛔ 秘境模式：**一个字都不许写进主线的进度字段**。
            //   NodeOffset / VisitedNodes / PassedNodes 全是"主线走到第几格"的存档，
            //   语义完全属于主线。秘境借用它们 ⇒ ① 中途存档/退出再进来，位置就停在秘境格上；
            //   ② 更糟的是 If 崩在写入与 Exit 之间，主线进度直接被污染。
            //   秘境的进度一律走 RealmSession 的独立字段（RealmWave / RealmCleared），
            //   主线节点偏移由 Enter 存的快照在 Exit 时还原（硬约束 ②）。
                        if (_realmMode)
                        {
                            RealmSession.MarkCleared(prun, PendingCommit);
                            // ★ 镇守：**击败必掉**专属奖励（用户："打死了会有特殊奖励"）。
                            //   ⛔ 放在"通过节点"这一刻而不是战斗结算里：结算面板是主线共用的，
                            //     往那儿塞秘境掉落会让"主线战利品"多出一个只在场域里出现的分支。
                            GrantRealmChampionReward(prun, PendingCommit);
                            Debug.Log("[Campaign] 秘境通过第 " + (PendingCommit + 1) + " 格（进度记在秘境自己的字段里，主线未动）");
                            WanXiang.Run.RunSave.SaveCurrent();

                            // ★★ 2026-10-22 用户："秘境通关最后一个节点了，怎么没有返回节点地图"
                            //   原来**没有任何清关判定** —— 打完出口就停在那儿。
                            //   出口 = 最后一格：判掉即撤场（撤场不罚）、原地重建成主线图。
                            if (_graph != null && PendingCommit >= _graph.NodeCount - 1)
                            {
                                RealmSession.Exit(prun, penalize: false);
                                WanXiang.Run.RunSave.SaveCurrent();
                                _realmMode = false;
                                _realmGraphPayload = null;
                                _injectedGraph = null;
                                _currentOffset = -1;
                                _visited.Clear();
                                _passed.Clear();
                                Debug.Log("[Campaign] 秘境清关 → 已退回主线节点图");
                                Dialog.Tip("秘境", "此处已无路。\n\n你带着这一次的收获，回到了自己的路上。").Forget();
                            }
                        }
         else
            {
                 prun.NodeOffset = PendingCommit;
        // ★ 记一笔"真·通过"（落印✕ / 连线实线 只看它；VisitedNodes 只是"进入"，不算通过）
        if (prun.PassedNodes == null) prun.PassedNodes = new System.Collections.Generic.List<int>();
     if (!prun.PassedNodes.Contains(PendingCommit)) prun.PassedNodes.Add(PendingCommit);
              // ★ 瘴雾：真正通过一格 ， 剩余雾 -1；扣到 0 雾就散。
                //   ⚠ 重掷记录（RerolledKinds）**不随雾散清掉** —— 地图被改过就该一直算数（本幕内），
             //     否则"雾散了图又变回去"会很怪。它跟 VisitedNodes 一样在换幕/重开时清。
                        if (prun.FogLeft > 0)
                        {
                            if (PendingCommit == prun.FogTriggerNode)
                            {
                                // ★ 起雾那一格（天象节点）**不算进 3 节**（用户定案："要后续 3 个节点"）， 跳过这次扣减
                                prun.FogTriggerNode = -1;
                                Debug.Log("[Campaign] 瘴雾：通过「起雾格」（不计入）， 仍剩 " + prun.FogLeft + " 节");
                            }
                            else
                            {
                                prun.FogLeft--;
                                Debug.Log("[Campaign] 瘴雾：通过节点 ， 剩余 " + prun.FogLeft + " 节" +
                                          (prun.FogLeft == 0 ? "（雾散）" : ""));
                            }
                            if (prun.FogLeft == 0) { ShowFogBanner("雾散了，前路已非"); SfxThrottled("sfx_map_fog_clear", 0.85f); prun.FogTriggerNode = -1; }
                        }
                        // ★ 节点真正「通过」（离开事件/战斗面板回地图落地）， 所有星移余气 -1（扣到负移除）。
                        //   推迟到此处而非「出征」点击，保证「进编队又返回」不算通过、星移仍可撤销
                        //   （用户实测：出征→编队→返回后星移撤销不了，根因就是出征时就把余气 -1 了）。
                        WanXiang.Campaign.LiveWeather.OnNodeCommitted();
                        SyncXingyiSaveFromLive();   // ★ 把星移余气剩余节点数同步回存档镜像
                        if (prun.VisitedNodes != null && !prun.VisitedNodes.Contains(PendingCommit))
                            prun.VisitedNodes.Add(PendingCommit);

    // ★★ 天阙（第 5 幕）最后一格 = 后土：通过它 ， 打【终局战】（而非回主城/推进）。
          if (!_realmMode && _graph != null && _graph.Act >= 5 && PendingCommit == _graph.NodeCount - 1)
                        {
                            var cat = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
                            var all = (cat != null && cat.Length > 0)
                                ? WanXiang.Fusion.ContentLibrary.BuildBeasts(cat[0]) : null;
                            if (all != null && all.Length > 0)
                            {
                                var req = TrialPanel.BuildFinaleBattle(prun, all);
                                SceneFlow.IsFinaleBattle = true;
                                PendingCommit = -1;
                                CloseSelf();
                                SceneFlow.EnterBattle(req);
                                return true;
                            }
                        }

                        // ★★ 幕推进绑在"通过"这一刻：刚通过的是本幕最后一格 ， 进下一幕。
                        //    原先靠"人在最后一格(NodeOffset==NodeCount-1)"当判据是错的：
                        //    只要存档停在这个位置（不论是否真通过），一进节点图就会跳幕，
                        //    而且它同时被当作"可达性锚点"， 全图锁定灰（用户实测）。
    int lastCell = _graph != null ? _graph.NodeCount - 1 : -1;
                // ⛔⛔ 秘境模式**绝不能推进主线 Act**：秘境内部图的 offset 是"秘境波次"，
                  //   和主线 act*1000+offset 的命名空间完全不同。放行的话会出现
                  //   "打完秘境第 2 波，主线莫名跳到第 4 幕"，而且完全查不出来源。
           //   秘境的进度由 RealmSession 记（RealmWave / RealmCleared），与主线无关。
                if (!_realmMode && lastCell >= 0 && PendingCommit == lastCell && prun.Act < 5)
           {
                            prun.Act++;
                            WanXiang.Modules.UI.InspirationBonus.GrantActEggs(prun);   // ★ 2026-11-20 广陌
                            prun.Stats.ActsCleared++;   // B2 骨架：通关幕数（道痕来源之一）
                            // B2 骨架：纯色队判定——上阵异兽全同五行（有任一不在表/不足 1 只则不算）
                            {
                                string el = null; bool same = prun.Team != null && prun.Team.Count > 0;
                                for (int ti = 0; same && ti < prun.Team.Count; ti++)
                                {
                                    BeastProfile tp;
                                    if (!BeastTable.TryGet(prun.Team[ti], out tp) || tp == null) { same = false; break; }
                                    var e = tp.Element.ToString();
                                    if (el == null) el = e; else if (el != e) same = false;
                                }
                                if (same) prun.Stats.SameElementTeam = true;
                            }
                            prun.NodeOffset = -1;
                            if (prun.VisitedNodes != null) prun.VisitedNodes.Clear();
                            if (prun.PassedNodes != null) prun.PassedNodes.Clear();       // 换幕：通过记录作废（offset 是"本幕"的）
                            if (prun.RerolledKinds != null) prun.RerolledKinds.Clear();   // 换幕：重掷记录作废
                            // ⛔ 2026-10-20：同层互斥记录是"本幕节点下标"，跨幕必须清 ——
                            //   否则上一幕选过的下标会在下一幕被当成"本层已进过"，整层点不动。
                            if (prun.ChosenNodes != null) prun.ChosenNodes.Clear();
                            if (prun.FogWeatherNodes != null) prun.FogWeatherNodes.Clear(); // 换幕：挂的瘴雾天时作废
                            prun.FogGenAct = 0;                                           // 换幕：下一幕重新生成瘴雾节点
                            prun.FogTriggerNode = -1;
                            prun.FogLeft = 0; prun.FogRerolled = false;                   // 换幕：雾散
                            Debug.Log("[Campaign] 通过本幕最后一格 ， 推进到第 " + prun.Act + " 幕");
                            WanXiang.Run.RunSave.SaveCurrent();
                            // ★ 2026-11-14 用户：打完守关 Boss 要有"写字转场"。原来 Act++ 后只内部刷新
                            //   地图（面板不重走 OnOpenAsync）⇒ ActIntro 的播放判断永远不执行。
                            //   这里显式触发一次（ShouldPlay 会按 IntroShownAct 防重播）。
                            if (!_realmMode && ActIntroPanel.ShouldPlay(prun.Act, false))
                                DeferredIntroAsync().Forget();

                            // ★ 2026-10-21 灵兽蛋：每通过一个节点就少一格，归零即出壳入队
                    if (prun.EggHatchIn > 0)
                    {
                        prun.EggHatchIn--;
                        if (prun.EggHatchIn == 0 && !string.IsNullOrEmpty(prun.EggBeastId))
                        {
                            if (prun.Collection == null) prun.Collection = new System.Collections.Generic.List<string>();
                            if (prun.Team == null) prun.Team = new System.Collections.Generic.List<string>();
                            if (!prun.Collection.Contains(prun.EggBeastId)) prun.Collection.Add(prun.EggBeastId);
                            if (prun.Team.Count < 5 && !prun.Team.Contains(prun.EggBeastId)) prun.Team.Add(prun.EggBeastId);
                            Debug.Log("[Campaign] 灵兽蛋出壳：" + prun.EggBeastId + " 已入队");
                            prun.EggHatchIn = -1;
                            prun.EggBeastId = null;
                        }
                        WanXiang.Run.RunSave.SaveCurrent();
                    }

                    // ★★ 第四幕通关 ， 第 5 幕 = 天阙：挂起"天阙抉择"，并直接回主城等玩家选。
                            //    旧实现靠 MainSceneEntry 里 `NodeOffset == 11` 判定（当时是 4 层×3 格的遗留），
                            //    既与现在的 12 层图对不上，又会被幕推进重置成 -1 ， 天阙永远不会触发。
                            if (prun.Act >= 5)
                            {
                                // ★★ 关键：**不切场景**！就地重建天阙图 + 原地弹出天阙抉择。
                                //    原来这里 EnterMain() 切场景 ， MainSceneEntry 跑两次 ，
                                //    面板实例在"关闭中"状态被再次 OpenAsync，返回实例却不激活
                                //    ， 最后画面一片空白 / 主界面（日志实测：打开成功但 2 帧后 active=False）。
                                SceneFlow.PendingFinale = false;   // 不需要跨场景挂起了
                                Debug.Log("[Campaign] 已到天阙 ， 就地弹出天阙抉择（不切场景）");
                                WanXiang.Run.RunSave.SaveCurrent();
                                RebuildGraphFor(prun.Act, prun.RunSeed);   // 换成天阙图（5 节点）
                                _currentOffset = -1;
                                _visited.Clear();
                                BuildNodeMap();
                                // 先等奖励界面关 + 播转场，再弹天阙抉择（原来同时抢屏 → 用户实测"第四幕被跳过"）
                                PendingCommit = -1;   // ★ 这个 return 原来跳过末尾清理 → 二次落地又推进一幕
                                if (prun.PendingBattlePass >= 0) { prun.PendingBattlePass = -1; WanXiang.Run.RunSave.SaveCurrent(); }
                                DeferredIntroThenTrialAsync().Forget();
                                return true;
                            }

                            RebuildGraphFor(prun.Act, prun.RunSeed);   // 换新幕的图
            _currentOffset = -1;
        _visited.Clear();
          }        // ← 收口：上面整段（瘴雾/天阙/幕推进）只属于主线，秘境不进来
             WanXiang.Run.RunSave.SaveCurrent();
           }             // ← 收口：if (prun != null)
             }             // ← 收口：else（秘境分支）
             PendingCommit = -1;
             // ★ 同步清存档镜像：否则 OnRefresh 会 RestorePendingBattlePass 把同一次胜利再落地一次
             var landed = WanXiang.Run.RunSave.Current;
             if (landed != null && landed.PendingBattlePass >= 0)
             {
                 landed.PendingBattlePass = -1;
                 WanXiang.Run.RunSave.SaveCurrent();
             }
          }
        return false;
        }
    }
}
