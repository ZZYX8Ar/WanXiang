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
        [SerializeField] private TMP_InputField _inputShare;   // Tmp_InputShare 分享码导入
        [SerializeField] private Button _btnImport;            // Btn_Import
        [SerializeField] private Button _btnExport;            // Btn_Export
        [SerializeField] private Button _btnClose;             // Btn_Close
        [SerializeField] private Button _btnBackToStart;  // Btn_BackToStart 返回开始界面

        protected override void OnCreate()
        {
            EnsureBackToStart();
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

        /// <summary>返回开始界面按钮的兜底创建：prefab 里没有这个按钮时也要能返回，
        /// 否则玩家点不到（用户实测"返回开始界面"没反应）。同 EnsureBackButton 模式。</summary>
        private void EnsureBackToStart()
        {
            if (_btnBackToStart != null) return;

            var go = new GameObject("Btn_BackToStart", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.94f, 0.92f, 0.88f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(0f, 60f);
            r.sizeDelta = new Vector2(240f, 72f);

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = "返回开始界面";
            tmp.fontSize = 26;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            _btnBackToStart = btn;
            btn.onClick.AddListener(OnBackToStartClicked);
        }

    }
}
