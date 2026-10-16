// ============================================================================
//  RealmFlow —— 进出秘境的**唯一流程**（2026-10-16）
//  ---------------------------------------------------------------------------
//  用户要求（原话）：
//    · "我说了进入这个秘境和离开都是有这个转场的，还是说这个测试的没有，
//       这个测试的按钮和正常进入是一样的啊"
//    · "点击离开的时候要弹出面板确定和说明离开不会有什么惩罚"
//
//  ⇒ 所以"测试按钮"和"路线图上的秘境节点"必须走**同一条代码路径**。
//    如果各写一份，就一定会出现"测试的能进、正式的没转场"或"两个入口的清理不一样"。
//    于是把整条流程收在这里，两个入口都只调这里的方法。
//
//  ⛔ 本类不改战斗数据：进场/回滚全在 <see cref="RealmSession"/>。
//  ⛔ 转场里 onSwitch 才是"真正切界面"的时刻 —— 玩家看不到切换那一帧。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    /// <summary>进出秘境的统一流程（进入 / 撤离确认 / 执行撤离）。</summary>
    internal static class RealmFlow
    {
        /// <summary>
        /// 进入秘境：存档进场 → 转场（粒子合拢 → 秘境地名 + 艺术字 → 拨开）→ 打开秘境路线图。
        /// <param name="onAfterEnter">转场揭晓后要执行的额外动作（调试入口用它撤销"次数"痕迹）。</param>
        /// </summary>
        public static void Enter(WanXiang.Campaign.RealmDef realm, Action onAfterEnter = null)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || realm == null) return;

            if (!RealmSession.Enter(run, realm))
            {
                Debug.LogWarning("[RealmFlow] 进入被拒（本局次数已用完 / 已在秘境中）");
                return;
            }
            CampaignPanel.PendingCommit = -1;      // 进秘境不算"通过某个节点"
            WanXiang.Run.RunSave.SaveCurrent();

            var map = WanXiang.Campaign.RealmMapBuilder.Build(realm, RealmSession.RealmSeedFor(run));
            if (map == null || map.Graph == null)
            {
                Debug.LogError("[RealmFlow] 建不出秘境图，回滚进场");
                RealmSession.Exit(run);
                WanXiang.Run.RunSave.SaveCurrent();
                return;
            }

            // 注册"撤离"回调（秘境模式下 CampaignPanel 的离开按钮走它）
            CampaignPanel.OnRealmQuitRequested = () => QuitNow(realm.Id);

            UniTask.Void(async () =>
            {
                await RealmTransition.PlayAsync(realm.Id, () =>
                {
                    var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                    if (ui != null) ui.OpenAsync<CampaignPanel>(map.Graph).Forget();
                });
                onAfterEnter?.Invoke();
            });
        }

        /// <summary>
        /// 弹确认框问"是否离开秘境"，确认后走转场离开。
        /// ⛔ 文案必须**照实说**代价：默认无惩罚，只有标了 PenalizesOnQuit 的场域才有
        ///   （见 <see cref="WanXiang.Campaign.RealmCatalog.PenalizesOnQuit"/>）。
        /// </summary>
        public static void ConfirmAndQuit()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || !run.InRealm) return;

            var realm = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
            bool penalizes = WanXiang.Campaign.RealmCatalog.PenalizesOnQuit(realm);

            string body = penalizes
                ? realm.QuitPenaltyText
                : "撤离后本局的路线图进度、遗物、灵卵都不会有任何变化 ——\n" +
                  "秘境内已获得的奖励照常带走，只是没拿到的就留在里面了。";

            UniTask.Void(async () =>
            {
                bool ok = await DialogPanel.Confirm(
                    "离开秘境",
                    "确定要离开「" + (realm != null ? realm.Name : "秘境") + "」吗？\n\n" + body,
                    "离开", "继续探索");
                if (!ok) return;
                QuitNow(realm != null ? realm.Id : null);
            });
        }

        /// <summary>
        /// 执行撤离：转场（粒子合拢 → 揭晓 → 拨开）→ 回滚场域 → 原地重建主线图。
        /// ⚠ 回滚必须在 onSwitch（屏幕被盖满）里做：否则玩家会看到"秘境地图上的节点
        ///   一瞬间变成主线节点"的错位。
        /// </summary>
        public static void QuitNow(string realmId)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            string id = realmId ?? run.RealmId;
            UniTask.Void(async () =>
            {
                await RealmTransition.PlayAsync(id, () =>
                {
                    var r = WanXiang.Run.RunSave.Current;
                    RealmSession.Exit(r);              // ★ 回滚 + 还原位置（硬约束 ②）
                    if (r != null) WanXiang.Run.RunSave.SaveCurrent();

                    // 原地重建主线图：走 OnRefresh 那条**同实例**路径
                    // ⛔ 不要在这里 OpenAsync<CampaignPanel>(null) —— 面板已经开着，
                    //   UISystem 对"已打开的实例"只调 InternalRefresh，不重放 OnOpenAsync；
                    //   而"正在关闭的实例"会被**静默忽略** ⇒ 画面一片空白且无报错（踩过两次）。
                    var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                    if (ui != null) ui.OpenAsync<CampaignPanel>(null).Forget();
                });
            });
        }
    }
}
