// ============================================================================
//  Panel_Home —— 主界面（常驻）
//  流程：出征 → Panel_Campaign；图鉴 / 局外成长 / 设置分别开各自面板。
//  结构与绑定契约见《UGUI 拼装规范 v1.0》第 7 章；美术见《美术资产生产方案 v1.2》Ch.08
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Home", Layer = UILayer.Main, CachePolicy = UICachePolicy.Resident,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class HomePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpEggs;        // Tmp_Eggs      灵卵数量
        [SerializeField] private TMP_Text _tmpInk;         // Tmp_Ink       墨锭数量
        [SerializeField] private Image   _imgHero;         // Img_HeroSprite 主战异兽立绘
        [SerializeField] private Button  _btnDeploy;       // Btn_Deploy    出征
        [SerializeField] private Button  _btnCodex;        // Btn_Codex     图鉴
        [SerializeField] private Button  _btnMeta;         // Btn_Meta      局外成长
        [SerializeField] private Button  _btnMarket;       // Btn_Market    灵市（底排布袋图标）
        [SerializeField] private Button  _btnTale;         // Hot_Tale      异闻（底排亭子图标）
        [SerializeField] private Button  _btnTrial;        // Hot_Trial     试炼（底排刀剑图标，直进战斗场景）
        [SerializeField] private Button  _btnForge;        // Hot_Forge     铸魂台（底排双人图标）
        [SerializeField] private Button  _btnOmen;         // Hot_Omen      天象（底排卷轴图标）
        [SerializeField] private Button  _btnSettings;     // Btn_Settings  设置

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        // ---- 旅程状态（选档后有值；没选档给默认首程值）----
        private int RunEggs
        {
            get { return WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Eggs : 12; }
        }

        private int RunInk
        {
            get { return WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Ink : 3; }
        }

        protected override void OnCreate()
        {
            if (_btnDeploy != null) _btnDeploy.onClick.AddListener(OnDeployClicked);
            if (_btnCodex != null) _btnCodex.onClick.AddListener(OnCodexClicked);
            if (_btnMeta != null) _btnMeta.onClick.AddListener(OnMetaClicked);
            if (_btnMarket != null) _btnMarket.onClick.AddListener(OnMarketClicked);
            if (_btnTale != null) _btnTale.onClick.AddListener(OnTaleClicked);
            if (_btnTrial != null) _btnTrial.onClick.AddListener(OnTrialClicked);
            if (_btnForge != null) _btnForge.onClick.AddListener(OnForgeClicked);
            if (_btnOmen != null) _btnOmen.onClick.AddListener(OnOmenClicked);
            if (_btnSettings != null) _btnSettings.onClick.AddListener(OnSettingsClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // 结构验证版：灵卵/墨锭是占位数值，等 RunState / MetaState 接进来后替换
            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + RunEggs;
            if (_tmpInk != null) _tmpInk.text = "墨锭 " + RunInk;

            // 主立绘取队伍第一只
            if (_imgHero != null && _sprites != null && _contentCatalog != null)
            {
                var id = BattleRequestFactory.FirstBeastId(_contentCatalog);
                var sprite = !string.IsNullOrEmpty(id) ? _sprites.Get(id) : null;
                if (sprite != null)
                {
                    _imgHero.sprite = sprite;
                    _imgHero.color = Color.white;
                    _imgHero.preserveAspect = true;
                }
            }
            return UniTask.CompletedTask;
        }

        private void OnDeployClicked()
        {
            OpenPanelAsync<CampaignPanel>().Forget();
        }

        private void OnCodexClicked()
        {
            OpenPanelAsync<CodexPanel>().Forget();
        }

        private void OnMetaClicked()
        {
            OpenPanelAsync<MetaPanel>().Forget();
        }

        private void OnMarketClicked()
        {
            OpenPanelAsync<MarketPanel>().Forget();
        }

        private void OnTaleClicked()
        {
            OpenPanelAsync<TalePanel>().Forget();
        }

        /// <summary>试炼：不经过节点地图，直接用内容目录组一场默认战斗进战斗场景。</summary>
        private void OnTrialClicked()
        {
            SceneFlow.EnterBattle(null);
        }

        private void OnForgeClicked()
        {
            OpenPanelAsync<ForgePanel>().Forget();
        }

        private void OnOmenClicked()
        {
            OpenPanelAsync<OmenPanel>().Forget();
        }

        private void OnSettingsClicked()
        {
            OpenPanelAsync<SettingsPanel>().Forget();
        }
    }
}
