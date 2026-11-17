// ============================================================================
//  万相 · 秘境（Realm）模式（2026-11-17 代码骨架重构）
//  从 CampaignPanel.cs 原样搬入，逻辑零改动。
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
        private void GrantRealmChampionReward(WanXiang.Run.RunState run, int offset)
        {
            if (run == null || _graph == null) return;
            if (_graph.KindOf(offset) != WanXiang.Campaign.NodeKind.RealmChampion) return;
            if (run.RealmChampionRewarded == null)
                run.RealmChampionRewarded = new System.Collections.Generic.List<int>();
            if (run.RealmChampionRewarded.Contains(offset)) return;
            run.RealmChampionRewarded.Add(offset);

            // ★ 奖励由**镇守**决定 —— 与信息卡同一个 pick（同 offset + 同种子），
            //   保证"看到的是谁，打的就是谁，掉的就是谁的那份"。
            //   ⛔ 星币已删（它只是"灵卵+固定汇率"的中间步骤）——产出直接给灵卵，走 EggRate。
            var realm = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            var champ = WanXiang.Campaign.RealmChampionPick.Pick(
                realm, offset, RealmSession.RealmSeedFor(run));
            int eggs = champ != null ? champ.Eggs : 3;
            eggs = RealmSession.AddEggs(run, eggs);
            string soul = PickVaultSoul(run);
            if (!string.IsNullOrEmpty(soul)) run.Souls.Add(soul);
            Debug.Log("[Campaign] 击败镇守「" + (champ != null ? champ.Name : "?") +
                      "」（第 " + (offset + 1) + " 格）：灵卵 +" + eggs + "、灵魂 +1（" + soul + "）—— " +
                      (champ != null ? champ.RewardText : "秘境专属奖励"));
        }


        /// <para>设计：让玩家在"立刻兑现"和"换一件遗物"之间选 —— 前者无风险但收益封顶，
        /// 后者是三选一（有得挑，但拿到哪件不由你定）。</para>
        /// <para>⛔ 复用 <see cref="DialogPanel"/> 与 <see cref="RelicPanel"/>，不新建面板：
        ///   这两个 UI 已经承担全项目同类职责，再开一个只会让"三选一的规则"出现第二份实现。</para>
        /// </summary>
        private void OpenRealmVault()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            PendingCommit = _selected;      // 走正常"通过节点"流程（秘境侧记 RealmCleared）

            int coin = 6 + run.Act;
            UniTask.Void(async () =>
            {
                int pick = await DialogPanel.Choose(
                    "秘藏",
                    "秘藏在幽深处静置已久。\n\n" +
                    "· 取走它：灵卵 +" + (coin + 2) + "、灵魂 +1\n" +
                    "· 或者把它献给铸魂台，换一次遗物三选一",
                    "取走", "换遗物");

                var r = WanXiang.Run.RunSave.Current;
                if (r == null) return;

                if (pick == 1)
                {
                    // 换遗物：复用主线那张三选一（规则只有一份）
                    RelicPanel.SetReturnTarget(() =>
                    {
                        var u = WanXiang.Framework.Boot.UIBootstrap.UI;
                        if (u != null) u.OpenAsync<CampaignPanel>(null).Forget();
                    });
                    var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                    if (ui != null) ui.OpenAsync<RelicPanel>(null).Forget();
                }
                else
                {
                    RealmSession.AddEggs(r, coin + 2);
                    r.Souls.Add(PickVaultSoul(r));
                    WanXiang.Run.RunSave.SaveCurrent();
                    Debug.Log("[Campaign] 秘藏：灵卵 +" + (coin + 2) + "、灵魂 +1");
                    var u = WanXiang.Framework.Boot.UIBootstrap.UI;
                    if (u != null) u.OpenAsync<CampaignPanel>(null).Forget();
                }
            });
        }

        /// <summary>秘藏给的灵魂：优先给本队没有的（给重复的等于没给）。</summary>
        private static string PickVaultSoul(WanXiang.Run.RunState run)
        {
            var cat = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            var c = (cat != null && cat.Length > 0) ? cat[0] : null;
            if (c != null && c.Beasts != null && c.Beasts.Length > 0)
            {
                for (int i = 0; i < c.Beasts.Length; i++)
                {
                    var id = c.Beasts[(run.Souls.Count + i) % c.Beasts.Length].BeastId;
                    if (!string.IsNullOrEmpty(id) && !run.Souls.Contains(id)) return id;
                }
                return c.Beasts[run.Souls.Count % c.Beasts.Length].BeastId;
            }
            return "";
        }

        /// <summary>
        /// 「离开秘境」按钮（2026-10-16 用户："这个离开按钮不要和节点地图返回主界面共用，
        ///   新加一个放左下角往上一点…点击离开的时候要弹出面板确定和说明离开不会有什么惩罚"）。
        /// <para>⛔ 与 <see cref="OnBackToHome"/> 是**两条路**：撤离要回滚场域状态 + 还原原节点，
        /// 回主界面则要留在原地图 —— 共用一个按钮就必须靠 if/else 猜玩家意图，迟早出错。</para>
        /// </summary>
            private void OnRealmQuitClicked() => RealmFlow.ConfirmAndQuit();

        /// <summary>该节点属于秘境第几层（层号+1：第 0 层是入口，对玩家显示为「第1层」）。</summary>
        private int RealmLayerOf(int offset)
        {
            int layer = _graph != null ? _graph.LayerOf(offset) : 0;
            return layer + 1;
        }

        /// <summary>
        /// 秘境模式的信息卡：**不显示节气与天时**，改显示"我在第几层 + 这个场域什么脾气"。
        /// <para>为什么必须换掉而不是留着：天时是**天穹历法**的产物（按幕取节气），
        /// 秘境不在历法里 —— 显示"第1节·立春 / 天时 蛰虫始振"会让玩家以为天时在场域内生效，
        /// 而实际上秘境战斗用的是场域自己的场地效果。**文案与实际生效的规则不一致是最坏的 bug。**</para>
        /// </summary>
        private void UpdateRealmInfoCard(int offset, WanXiang.Campaign.NodeKind kind, bool silent)
        {
            var run = WanXiang.Run.RunSave.Current;
            var realm = WanXiang.Campaign.RealmCatalog.For(run != null ? run.RealmId : null);
            int layer = RealmLayerOf(offset);

            if (_tmpNodeName != null)
                _tmpNodeName.text = "第 " + layer + " 层" +
                                    (realm != null ? " · " + realm.Name : "");

            if (_tmpNodeType != null)
            {
                string n = WanXiang.Campaign.NodeKinds.Cn(kind);
                // 秘境专属节点在图里要说清楚"它和外面不一样"，否则玩家不知道值不值得绕路
                // ★ 镇守要写**名字 + 专属机制**（用户："特殊的敌人有一些特殊的战斗方式"），
                //   只写"特殊敌人"玩家不知道特殊在哪，进去才发现被噤声/被拖走，观感是"被阴了"。
                var __champ = kind == WanXiang.Campaign.NodeKind.RealmChampion
                    ? WanXiang.Campaign.RealmChampionPick.Pick(
                        realm, offset, RealmSession.RealmSeedFor(run)) : null;

                // ★ 2026-10-22：镇守立绘（用户提供 10 张透明立绘，已放 Resources/UI/Realm/Champion）。
                //   镇守格把信息卡顶部的 Img_NodeIcon 放大成 180×180 显示立绘；
                //   其它节点保持原样（不改 prefab，只在该格临时改 sprite/size）。
                if (_imgNodeIcon != null)
                {
                    var __art = __champ != null ? WanXiang.Campaign.RealmChampionArt.Get(__champ) : null;
                    if (__art != null)
                    {
                        _imgNodeIcon.sprite = __art;
                        _imgNodeIcon.preserveAspect = true;
                        _imgNodeIcon.rectTransform.sizeDelta = new Vector2(180f, 180f);
                    }
                    else
                    {
                        _imgNodeIcon.preserveAspect = true;
                        _imgNodeIcon.rectTransform.sizeDelta = new Vector2(140f, 140f);
                    }
                }
                string extra = kind == WanXiang.Campaign.NodeKind.RealmVault
                    ? "　（秘境专属 · 一次性重奖）"
                    : kind == WanXiang.Campaign.NodeKind.RealmChampion
                        ? "　镇守 · " + (__champ != null ? __champ.Name : "？")
                        : kind == WanXiang.Campaign.NodeKind.ActSkip
                            ? "　（续劫专属 · 直入下一幕）"
                            : (WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "　（战斗）" : "　（休整）");
                _tmpNodeType.text = n + extra;
            }

            // 正文 = 场域特性（场地效果 / 收益 / 代价三行）。⛔ 不写"天时"。
            if (_tmpWeather != null)
            {
                if (realm == null) _tmpWeather.text = "";
                else
                {
                    // ★ 镇守格：把它的专属机制写在最上面 —— 这是玩家决定"打不打"的依据。
                    //   pick 与战斗请求同一公式（offset + 种子）⇒ 看到的 = 打到的。
                    var __c = kind == WanXiang.Campaign.NodeKind.RealmChampion
                        ? WanXiang.Campaign.RealmChampionPick.Pick(
                            realm, offset, RealmSession.RealmSeedFor(run)) : null;
                    string mech = __c != null
                        ? "镇守 · " + __c.Name + "：" + __c.MechanicText + "\n" +
                          "击败必得：" + __c.RewardText + "\n"
                        : "";
                    _tmpWeather.text =
                        mech +
                        "场地：" + WanXiang.Modules.UI.RealmText.Hazard(realm) + "\n" +
                        "收益：" + realm.Boon + "\n" +
                        // ⛔ 走 RealmText.Cost（从真实字段推导）；不要读 realm.Cost 手写串
                        "代价：" + WanXiang.Modules.UI.RealmText.Cost(realm);
                }
            }

            _current.Title = "第" + layer + "层";
            _current.Weather = "";                       // ⛔ 秘境不吃天时：别把主线天气带进战斗
            _current.Seed = (ulong)(offset * 977 + 31);  // 秘境种子与主线 act*1000+offset 隔离
            _current.NodeIndex = offset;
            _current.Act = 0;                            // 0 = 不属于任何一幕（与 _graph.Act 同口径）
            _current.Offset = offset;
            _current.Kind = kind;
            _current.RealmLayer = RealmLayerOf(offset);   // ★ 秘境真实层号（1 起）
            _current.IsBoss = false;                     // 秘境首领走 RealmMap.BossOffset，不走主线 IsBoss
        }

        private void OpenRealmPanel()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.Act < 2 || RealmSession.EntriesLeft(run) <= 0)
            {
                if (_tmpNodeName != null)
                    _tmpNodeName.text = "秘境：第 2 幕起才会出现，且本局进入次数已用完。";
                return;
            }
            if (RealmSession.RollFor(run, RealmSession.RealmSeedFor(run)) == null) return;

            // 问号揭晓成秘境时，把记录改成"秘境"，让节点图那一格显示成「秘境」
            bool wasQuestion = _graph != null && _graph.KindOf(_selected) == WanXiang.Campaign.NodeKind.Question;
            if (wasQuestion && run.QuestionRevealed != null)
            {
                for (int i = 0; i < run.QuestionRevealed.Count; i++)
                {
                    var rec = run.QuestionRevealed[i];
                    if (rec == null || !rec.StartsWith(_selected + ":")) continue;
                    run.QuestionRevealed[i] = _selected + ":"
                        + (int)WanXiang.Campaign.NodeKind.Realm;
                    break;
                }
                BuildNodeMap();          // 重建节点图，让那一格显示成「秘境」
            }
            PendingCommit = -1;          // 进秘境不算本节点通过
            WanXiang.Run.RunSave.SaveCurrent();
            OpenPanelAsync<RealmPanel>().Forget();
        }
    }
}
