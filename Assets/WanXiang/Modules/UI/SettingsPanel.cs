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
        // ⚠ 原 _inputShare（Tmp_InputShare 分享码导入）已删：分享码由 Panel_Pvp 取代，
        //   设置面板不再承担导入/导出队伍码的职责（2026-09-26 用户定案）。
        [SerializeField] private Button _btnImport;            // Btn_Import  → 重新读取存档
        [SerializeField] private Button _btnExport;            // Btn_Export  → 保存旅程
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
            if (_btnImport != null) _btnImport.onClick.AddListener(OnImportClicked);
            if (_btnExport != null) _btnExport.onClick.AddListener(OnExportClicked);
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

        private void OnImportClicked()
        {
            // 重读磁盘上当前槽位的存档（另一台机器拷过来的档也能这样接上）
            var slot = WanXiang.Run.RunSave.ActiveSlot;
            if (slot <= 0) { Debug.Log("[Settings] 还没有进行中的旅程，先去存档面板选一档。"); return; }
            var state = WanXiang.Run.RunSave.Load(slot);
            if (state == null) { Debug.LogWarning("[Settings] 槽位 " + slot + " 在磁盘上不存在。"); return; }
            WanXiang.Run.RunSave.ContinueWith(state);
            WanXiang.Meta.MetaStore.ReloadHistory();   // ★ 重读该槽位历程，避免显示其它档的记录
            WanXiang.Meta.MetaStore.ReloadMeta();      // ★ 局外数据同样按槽重载（墨铊/精魄/等级/觉醒技/图鉴）
            Debug.Log("[Settings] 已重新读取槽位 " + slot + "：" + state.RealmText + " 灵卵 " + state.Eggs);
        }

        private void OnExportClicked()
        {
            // 导出按钮 = 保存旅程（旅程数据本身是 JSON，无需 TeamCodec）
            if (WanXiang.Run.RunSave.Current == null)
            {
                Debug.Log("[Settings] 没有进行中的旅程可保存。");
                return;
            }
            WanXiang.Run.RunSave.SaveCurrent();
            var cur = WanXiang.Run.RunSave.Current;
            Debug.Log("[Settings] 旅程已保存：槽位 " + cur.Slot + " " + cur.RealmText + " 灵卵 " + cur.Eggs);
        }

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
