// ============================================================================
//  Panel_Settings —— 设置（表单弹窗，全复用组件）
//  ============================================================================

using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Settings", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class SettingsPanel : UIPanelBase
    {
        [SerializeField] private Slider _sldBgm;               // Sld_Bgm       音乐音量
        [SerializeField] private Slider _sldSfx;               // Sld_Sfx       音效音量
        [SerializeField] private Toggle _tglFullscreen;        // Tgl_Fullscreen
        [SerializeField] private Toggle _tglVsync;             // Tgl_Vsync
        [SerializeField] private ScrollRect _scrollKeys;       // Scroll_Keys   按键重绑定
        [SerializeField] private RectTransform _keyItemTemplate;  // Item_Key（模板，默认隐藏）
        // ⚠ 原 _inputShare（分享码导入）与 _btnImport / _btnExport（挂在同一块 Root_SaveLoad 里）
        //   已全部删除：分享码由 Panel_Pvp 取代；"保存/读取旅程"在存档面板（Panel_Save）里做，
        //   设置面板放这两个按钮是冗余的（2026-09-26 用户定案：这块没用，删掉）。
        [SerializeField] private Button _btnClose;             // Btn_Close
        [SerializeField] private Button _btnBackToStart;  // Btn_BackToStart 返回开始界面（prefab 里那个，已接线）

        protected override void OnCreate()
        {
            // ⚠ 这里**不再**做任何"兜底建 UI"（原 EnsureBackToStart 已删）。
            //   项目铁律：UI 一律在 prefab 里可见可改，禁止运行时 new GameObject 搭界面。
            //   那个兜底正是"面板里出现两个 Btn_BackToStart"的原因：
            //   prefab 里本来就有按钮，只是字段没接线 ⇒ 兜底又建了一个（用户实测报障）。
            if (_sldBgm != null) _sldBgm.onValueChanged.AddListener(OnBgmChanged);
            if (_sldSfx != null) _sldSfx.onValueChanged.AddListener(OnSfxChanged);
            if (_tglFullscreen != null) _tglFullscreen.onValueChanged.AddListener(OnFullscreenChanged);
            if (_tglVsync != null) _tglVsync.onValueChanged.AddListener(OnVsyncChanged);
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
            if (_btnBackToStart != null) _btnBackToStart.onClick.AddListener(OnBackToStartClicked);
        }

        private void OnBgmChanged(float value)
        {
            // TODO(交互): 即时生效并写入存档
        }

        private void OnSfxChanged(float value)
        {
            // TODO(交互): 即时生效并写入存档
        }

        private void OnFullscreenChanged(bool on)
        {
            // TODO(交互): 切换全屏
        }

        private void OnVsyncChanged(bool on)
        {
            // TODO(交互): 切换垂直同步
        }

        // ⚠ 原 OnImportClicked（重读槽位存档 + ReloadHistory/ReloadMeta）与 OnExportClicked（保存旅程）
        //   已随 Root_SaveLoad 一起删除 —— 这两件事属于**存档面板 Panel_Save** 的职责，
        //   设置面板里是冗余入口（用户定案"这块没用"）。
        //   若日后要恢复"从磁盘重读存档"，去存档面板接，别在设置面板重开一个入口。

        /// <summary>
        /// 返回开始界面：关掉当前所有界面，回到最初的开始面板。
        /// 注意顺序 —— 必须先 CloseAll 再开 StartPanel，否则新开的会被一起关掉。
        /// （旅程存档不动：玩家回来还能接着玩，这是"回菜单"而不是"弃档"。）
        /// </summary>
        private void OnBackToStartClicked()
        {
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui == null) return;

            ui.CloseAll();
            OpenPanelAsync<StartPanel>().Forget();
            Debug.Log("[Settings] 已返回开始界面（旅程存档保留）");
        }

        /// <summary>
        /// 原 EnsureBackToStart（运行时兜底创建 Btn_BackToStart）**已删除**。
        /// 删它的两个理由：
        ///   ① 违反项目铁律「UI 一律做成 prefab」—— 运行时建的按钮在 prefab 里看不见、改不了；
        ///   ② 它就是"面板里出现两个 Btn_BackToStart"的直接原因：
        ///      prefab 里本来就有这个按钮，只是 `_btnBackToStart` 没接线 ⇒ 兜底又建了一个。
        /// 现在接线已修（prefab 里 Btn_BackToStart → 字段），运行时会走到上面 OnCreate 的绑定。
        /// </summary>
        // （原方法体已删，留此说明避免日后有人"顺手加回兜底"）

    }
}
