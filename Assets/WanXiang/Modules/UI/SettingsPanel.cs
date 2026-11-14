// ============================================================================
//  万相 · 设置面板（2026-11-14 分页重做）
//  ---------------------------------------------------------------------------
//  结构见 文档/设置界面结构.md（先图后码，用户永久约束）。
//  分页：音频 / 战斗 / 显示 / 操作 / 存档 —— Tab 按钮切页，同一时刻只显示一个 Root_*。
//  ⛔ 只放有真实消费点的设置项（SettingsStore 统一收口）：
//     自动战斗/特写/飘字/震屏/提示/战斗速度/帧率/静音 → 均有代码读取。
//  ⚠ prefab 若还没补节点（_tabBtns 等为空），面板仍能用原有音频/画面/按键项，不报错。
// ============================================================================
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;
using WanXiang.Framework.Audio;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Settings", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class SettingsPanel : UIPanelBase
    {
        [SerializeField] private Slider _sldBgm;               // Sld_Bgm       音乐音量
        [SerializeField] private Slider _sldSfx;               // Sld_Sfx       音效音量
        [SerializeField] private Toggle _tglMute;              // Tgl_Mute      静音
        [SerializeField] private Toggle _tglFullscreen;        // Tgl_Fullscreen
        [SerializeField] private Toggle _tglVsync;             // Tgl_Vsync
        [SerializeField] private ScrollRect _scrollKeys;       // Scroll_Keys   按键重绑定
        [SerializeField] private RectTransform _keyItemTemplate;  // Item_Key（模板，默认隐藏）
        [SerializeField] private Button _btnClose;             // Btn_Close
        [SerializeField] private Button _btnBackToStart;       // Btn_BackToStart 返回开始界面

        // ---- 分页 ----
        [SerializeField] private Button[] _tabBtns;            // Tab_Audio / Tab_Battle / Tab_Video / Tab_Keybind / Tab_Save
        [SerializeField] private GameObject[] _tabPages;       // Root_Audio / Root_Battle / Root_Video / Root_Keybinds / Root_Save
        [SerializeField] private TMP_Text _tmpPageHint;        // Tmp_PageHint（当前页说明，可空）

        // ---- 战斗页 ----
        [SerializeField] private Toggle _tglAutoBattle;        // Tgl_AutoBattle
        [SerializeField] private Toggle _tglCutIn;             // Tgl_CutIn
        [SerializeField] private Toggle _tglDamageNum;         // Tgl_DamageNum
        [SerializeField] private Toggle _tglShake;             // Tgl_Shake
        [SerializeField] private Toggle _tglTooltip;           // Tgl_Tooltip
        [SerializeField] private Button[] _speedBtns;          // Btn_Speed1 / Btn_Speed15 / Btn_Speed2

        // ---- 显示页 ----
        [SerializeField] private Button[] _fpsBtns;            // Btn_Fps30 / Btn_Fps60 / Btn_Fps120 / Btn_FpsUnlimited

        // ---- 存档页 ----
        [SerializeField] private Button _btnClearSave;         // Btn_ClearSave

        /// <summary>设置面板是否打开（战斗层据此避免 ESC 重复开）。</summary>
        public static bool IsOpen;

        private int _tab;
        private bool _pauseBattleOnClose;

        protected override void OnCreate()
        {
            if (_sldBgm != null) _sldBgm.onValueChanged.AddListener(OnBgmChanged);
            if (_sldSfx != null) _sldSfx.onValueChanged.AddListener(OnSfxChanged);
            if (_tglMute != null) _tglMute.onValueChanged.AddListener(v => { AudioSystem.Muted = v; });
            if (_tglFullscreen != null) _tglFullscreen.onValueChanged.AddListener(OnFullscreenChanged);
            if (_tglVsync != null) _tglVsync.onValueChanged.AddListener(OnVsyncChanged);
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
            if (_btnBackToStart != null) _btnBackToStart.onClick.AddListener(OnBackToStartClicked);

            // 战斗页开关
            if (_tglAutoBattle != null) _tglAutoBattle.onValueChanged.AddListener(v => SettingsStore.AutoBattle = v);
            if (_tglCutIn != null) _tglCutIn.onValueChanged.AddListener(v => SettingsStore.SkillCutIn = v);
            if (_tglDamageNum != null) _tglDamageNum.onValueChanged.AddListener(v => SettingsStore.DamageNumbers = v);
            if (_tglShake != null) _tglShake.onValueChanged.AddListener(v => SettingsStore.ScreenShake = v);
            if (_tglTooltip != null) _tglTooltip.onValueChanged.AddListener(v => SettingsStore.Tooltips = v);

            if (_speedBtns != null)
                for (int i = 0; i < _speedBtns.Length; i++)
                {
                    int idx = i;
                    if (_speedBtns[i] != null) _speedBtns[i].onClick.AddListener(() => SetSpeed(idx));
                }
            if (_fpsBtns != null)
                for (int i = 0; i < _fpsBtns.Length; i++)
                {
                    int idx = i;
                    if (_fpsBtns[i] != null) _fpsBtns[i].onClick.AddListener(() => SetFps(idx));
                }
            if (_btnClearSave != null) _btnClearSave.onClick.AddListener(() => ClearSaveAsync().Forget());

            // Tab
            if (_tabBtns != null)
                for (int i = 0; i < _tabBtns.Length; i++)
                {
                    int idx = i;
                    if (_tabBtns[i] != null) _tabBtns[i].onClick.AddListener(() => SwitchTab(idx));
                }
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // payload = true 表示从战斗内打开 → 关面板时恢复战斗推进
            IsOpen = true;
            _pauseBattleOnClose = payload is bool b && b;
            if (_pauseBattleOnClose) Time.timeScale = 0f;

            RefreshControls();
            SwitchTab(0);
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            IsOpen = false;
            if (_pauseBattleOnClose)
            {
                _pauseBattleOnClose = false;
                Time.timeScale = 1f;   // 恢复战斗
            }
        }

        private void SwitchTab(int index)
        {
            if (_tabPages == null || _tabPages.Length == 0) return;
            _tab = Mathf.Clamp(index, 0, _tabPages.Length - 1);
            for (int i = 0; i < _tabPages.Length; i++)
                if (_tabPages[i] != null) _tabPages[i].SetActive(i == _tab);
            if (_tmpPageHint != null)
                _tmpPageHint.text = PageHint(_tab);
        }

        private static string PageHint(int tab)
        {
            switch (tab)
            {
                case 0: return "音量与静音：改动立即生效，自动保存。";
                case 1: return "战斗表现：只影响观感，不影响数值结算。";
                case 2: return "画面：全屏 / 垂直同步 / 帧率上限。";
                case 3: return "操作：点击按键格可重新绑定。";
                case 4: return "存档：清空会删除当前槽位的进度，不可恢复。";
                default: return "";
            }
        }

        private void RefreshControls()
        {
            if (_sldBgm != null) _sldBgm.SetValueWithoutNotify(AudioSystem.MusicVolume);
            if (_sldSfx != null) _sldSfx.SetValueWithoutNotify(AudioSystem.SfxVolume);
            if (_tglMute != null) _tglMute.SetIsOnWithoutNotify(AudioSystem.Muted);
            if (_tglFullscreen != null) _tglFullscreen.SetIsOnWithoutNotify(Screen.fullScreen);
            if (_tglVsync != null) _tglVsync.SetIsOnWithoutNotify(QualitySettings.vSyncCount > 0);
            if (_tglAutoBattle != null) _tglAutoBattle.SetIsOnWithoutNotify(SettingsStore.AutoBattle);
            if (_tglCutIn != null) _tglCutIn.SetIsOnWithoutNotify(SettingsStore.SkillCutIn);
            if (_tglDamageNum != null) _tglDamageNum.SetIsOnWithoutNotify(SettingsStore.DamageNumbers);
            if (_tglShake != null) _tglShake.SetIsOnWithoutNotify(SettingsStore.ScreenShake);
            if (_tglTooltip != null) _tglTooltip.SetIsOnWithoutNotify(SettingsStore.Tooltips);
        }

        private void SetSpeed(int idx)
        {
            float[] table = { 1f, 1.5f, 2f };
            if (idx < 0 || idx >= table.Length) return;
            SettingsStore.BattleSpeed = table[idx];
            Debug.Log("[Settings] 战斗速度 = " + table[idx].ToString("0.0") + "x（下一场生效）");
        }

        private void SetFps(int idx)
        {
            int[] table = { 30, 60, 120, 0 };
            if (idx < 0 || idx >= table.Length) return;
            SettingsStore.TargetFps = table[idx];
            if (_tmpPageHint != null)
                _tmpPageHint.text = table[idx] <= 0 ? "帧率上限：不限制" : "帧率上限：" + table[idx];
        }

        private async UniTaskVoid ClearSaveAsync()
        {
            var run = WanXiang.Run.RunSave.Current;
            int slot = run != null ? run.Slot : 0;
            bool ok = await DialogPanel.Confirm(
                "清空当前存档",
                "将删除槽位 " + slot + " 的全部进度（异兽、灵卵、遗物、道痕不保留）。\n\n此操作不可恢复。");
            if (!ok) return;
            WanXiang.Run.RunSave.Delete(slot);
            Debug.Log("[Settings] 已清空存档槽 " + slot);
            CloseSelf();
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) ui.OpenAsync<HomePanel>().Forget();
        }

        private void OnBgmChanged(float value) { AudioSystem.MusicVolume = value; AudioSystem.Save(); }
        private void OnSfxChanged(float value) { AudioSystem.SfxVolume = value; AudioSystem.Save(); }

        private void OnFullscreenChanged(bool v) => Screen.fullScreen = v;
        private void OnVsyncChanged(bool v) => QualitySettings.vSyncCount = v ? 1 : 0;

        private void OnBackToStartClicked()
        {
            CloseSelf();
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) ui.OpenAsync<HomePanel>().Forget();
        }
    }
}
