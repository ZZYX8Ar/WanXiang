// ============================================================================
//  万相 · 游戏入口
//  ---------------------------------------------------------------------------
//  挂在启动场景的一个空物体上（建议放在 [UIBootstrap] 同一个物体或隔壁）。
//  等 UIBootstrap 把 UISystem 建好之后，打开第一个界面：开始界面。
//
//  为什么单独一个组件而不是写进 UIBootstrap：
//    UIBootstrap 是"框架层"，不该知道业务上第一个界面是谁。
//    换了第一个界面，改这里一行，不用动框架。
// ============================================================================

using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.Boot;
using WanXiang.Framework.UI;
using WanXiang.Modules.UI;

namespace WanXiang.Modules.Boot
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class GameEntry : MonoBehaviour
    {
        [Tooltip("启动后打开的第一个面板。留空则打开 Panel_Start。")]
        [SerializeField] private string _firstPanel = "Panel_Start";

        [Tooltip("是否在打开第一个界面前预热常用面板（降低首次打开卡顿）。")]
        [SerializeField] private bool _preloadCommonPanels = true;

        private async UniTaskVoid Start()
        {
            // UIBootstrap(-1000) 的 Awake 早于本组件(-500) 的 Start，所以这里 UI 一定已就绪；
            // 但仍然判一下，避免场景里漏挂 UIBootstrap 时静默失败。
            var ui = UIBootstrap.UI;
            if (ui == null)
            {
                Debug.LogError("[GameEntry] UIBootstrap.UI 为空 —— 场景里没有 UIBootstrap，" +
                               "或它启动失败。UI 流程无法开始。");
                return;
            }

            if (_preloadCommonPanels)
            {
                await ui.PreloadAsync<SavePanel>();
                await ui.PreloadAsync<HomePanel>();
                await ui.PreloadAsync<CampaignPanel>();
                await ui.PreloadAsync<FormationPanel>();
                await ui.PreloadAsync<BattlePanel>();
                await ui.PreloadAsync<ResultPanel>();
            }

            string first = string.IsNullOrEmpty(_firstPanel) ? "Panel_Start" : _firstPanel;
            if (first == "Panel_Start")
            {
                await ui.OpenAsync<StartPanel>();
            }
            else
            {
                // 想直接跳过标题进主界面时用（调试方便）
                await ui.OpenAsync<HomePanel>();
            }
            Debug.Log("[GameEntry] 第一个界面已打开：" + first);
        }
    }
}
