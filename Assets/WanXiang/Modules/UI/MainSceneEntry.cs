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
                await ui.OpenAsync<ResultPanel>(result);
                Debug.Log("[MainSceneEntry] 已弹出战斗结算。");
                return;
            }

            await ui.OpenAsync<HomePanel>();
            Debug.Log("[MainSceneEntry] 已进入主界面。");
        }
    }
}
