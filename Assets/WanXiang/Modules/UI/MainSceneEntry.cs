// ============================================================================
//  万相 · 主城场景入口
//  ---------------------------------------------------------------------------
//  挂在 Main 场景的一个空物体上。进来时做一件事：
//    · 如果是从战斗场景回来的（SceneFlow 里有结算）→ 弹结算面板
//    · 否则（从开始界面进来 / 直接跑这个场景调试）→ 开主界面
//
//  为什么不是"谁发起谁负责开界面"：发起方（开始界面 / 战斗场景）在切换的那一刻
//  已经随旧场景销毁了，跨场景的界面收尾只能由**新场景的入口**接手。
// ============================================================================

using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.Boot;
using WanXiang.Modules.UI;

namespace WanXiang.Modules.Boot
{
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class MainSceneEntry : MonoBehaviour
    {
        private async UniTaskVoid Start()
        {
            var ui = UIBootstrap.UI;
            if (ui == null)
            {
                Debug.LogError("[MainSceneEntry] UIBootstrap.UI 为空 —— Boot 场景没起来，" +
                               "或本场景被单独运行。请从 Boot 场景开始。");
                return;
            }

            if (SceneFlow.ConsumeResult(out var result))
            {
                // ⚠ 旧的天阙触发条件（Act==4 && NodeOffset==11）已废弃并删除：
                //   11 是"4 层 × 3 格"时代的遗留格号，与现在的 12 层图对不上；
                //   而且幕推进会把 NodeOffset 重置为 -1 ⇒ 条件永远不成立 ⇒ 天阙永不触发。
                //   现在改为：**幕推进到第 5 幕的那一刻**置位 PendingFinale（见 CampaignPanel）。

                await ui.OpenAsync<ResultPanel>(result);
                Debug.Log("[MainSceneEntry] 已弹出战斗结算。");
                return;
            }

            // ⚠ 开始界面必须在这里关掉：StartPanel 与 Home 同在 Main 层，
            //   它一直 Opened 就会一直"遮挡"Home ⇒ Home 永远 Paused，
            //   表现就是「从节点地图返回主城，主界面是灰的/点不动」。
            ui.Close<StartPanel>();

            // ★★ 登天阙：刚从天阙抉择选了"登天阙" ⇒ 直接在**第 5 幕天阙图**开局，
            //    而不是进主界面（用户要求：登天后直接进入第五幕节点地图）。
            //    放在这里是因为：场景已加载完成、UI 栈干净，不会像"Overlay 面板内切面板"那样被栈重算判掉。
            if (SceneFlow.EnterFinaleMap)
            {
                SceneFlow.EnterFinaleMap = false;
                await ui.OpenAsync<CampaignPanel>();
                Debug.Log("[MainSceneEntry] 登天阙 ⇒ 已进入第五幕节点地图。");
                return;
            }

            await ui.OpenAsync<HomePanel>();
            Debug.Log("[MainSceneEntry] 已进入主界面（开始界面已关闭）。");
        }
    }
}
