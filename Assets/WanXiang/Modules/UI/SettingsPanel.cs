// ============================================================================
//  Panel_Settings —— 设置（表单弹窗，全复用组件）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Settings", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached)]
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

        protected override void OnCreate()
        {
            if (_sldBgm != null) _sldBgm.onValueChanged.AddListener(OnBgmChanged);
            if (_sldSfx != null) _sldSfx.onValueChanged.AddListener(OnSfxChanged);
            if (_tglFullscreen != null) _tglFullscreen.onValueChanged.AddListener(OnFullscreenChanged);
            if (_tglVsync != null) _tglVsync.onValueChanged.AddListener(OnVsyncChanged);
            if (_btnImport != null) _btnImport.onClick.AddListener(OnImportClicked);
            if (_btnExport != null) _btnExport.onClick.AddListener(OnExportClicked);
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
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
            // TODO(交互): TeamCodec 解码导入；失败 Toast
        }

        private void OnExportClicked()
        {
            // TODO(交互): TeamCodec 编码导出；成功 Toast
        }
    }
}
