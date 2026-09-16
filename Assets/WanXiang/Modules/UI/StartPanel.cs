// ============================================================================
//  Panel_Start —— 开始界面（标题画面）
//  ---------------------------------------------------------------------------
//  游戏启动后的第一个界面。只做三件事：进游戏、进设置、退出。
//  结构见《UGUI 拼装规范 v1.0》第 7 章；这是 v1.0 之后新增的第 14 个面板。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Start", Layer = UILayer.Main, CachePolicy = UICachePolicy.Resident,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class StartPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;      // Tmp_Title      万相
        [SerializeField] private TMP_Text _tmpTitleEn;    // Tmp_TitleEn    MYRIAD
        [SerializeField] private TMP_Text _tmpTagline;    // Tmp_Tagline    山海万相，皆由我生
        [SerializeField] private TMP_Text _tmpVersion;    // Tmp_Version    版本号
        [SerializeField] private Button _btnStart;        // Btn_Start      开始游戏
        [SerializeField] private Button _btnSettings;     // Btn_Settings   设置
        [SerializeField] private Button _btnQuit;         // Btn_Quit       退出

        protected override void OnCreate()
        {
            if (_btnStart != null) _btnStart.onClick.AddListener(OnStartClicked);
            if (_btnSettings != null) _btnSettings.onClick.AddListener(OnSettingsClicked);
            if (_btnQuit != null) _btnQuit.onClick.AddListener(OnQuitClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            if (_tmpVersion != null) _tmpVersion.text = "v0.1 · 结构验证版";
            return UniTask.CompletedTask;
        }

        private void OnStartClicked()
        {
            // 开始游戏：先关掉标题，再切到主城场景（Main 的入口会开主界面）
            CloseSelf();
            SceneFlow.EnterMain();
        }

        private void OnSettingsClicked()
        {
            OpenPanelAsync<SettingsPanel>().Forget();
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
