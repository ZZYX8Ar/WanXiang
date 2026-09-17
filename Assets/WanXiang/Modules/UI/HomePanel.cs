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
            RefreshDeployLabel();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 出征：**不是**"开新局" —— 它是「继续当前旅程」。
        /// 有进度时必须让玩家明确选一次（继续 / 重开），否则没人分得清这次出征是继续还是重来。
        /// </summary>
        private void OnDeployClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            int layer = run != null ? run.NodeOffset + 1 : 0;
            bool hasProgress = run != null && (run.NodeOffset >= 0 || run.Act > 1);

            if (!hasProgress)
            {
                OpenPanelAsync<CampaignPanel>().Forget();
                return;
            }

            DeployChoice(run, layer).Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid DeployChoice(WanXiang.Run.RunState run, int layer)
        {
            int pick = await Dialog.Choose(
                "继续旅程？",
                string.Format("当前进度：第{0}幕 · 第 {1} 层\n继续会从这一层接着爬；重开会清空本次旅程的进度与队伍。",
                              run.Act, layer),
                "重开一局", "继续");

            if (pick == 1)
            {
                OpenPanelAsync<CampaignPanel>().Forget();
                return;
            }

            bool ok = await Dialog.Confirm("重开一局",
                "本次旅程的进度、灵卵与队伍编成都会清空，确定重开？", "确定重开", "再想想");
            if (!ok) return;

            run.Act = 1;
            run.NodeOffset = -1;
            run.Wins = 0;
            run.Eggs = 0;
            run.Ink = 0;
            if (run.Path != null) run.Path.Clear();
            if (run.VisitedNodes != null) run.VisitedNodes.Clear();
            if (run.QuestionRevealed != null) run.QuestionRevealed.Clear();
            WanXiang.Run.RunSave.SaveCurrent();

            RefreshDeployLabel();
            OpenPanelAsync<CampaignPanel>().Forget();
        }

        /// <summary>出征按钮的文案带上进度 —— 一眼看清"点下去是继续哪一层"。</summary>
        private void RefreshDeployLabel()
        {
            if (_btnDeploy == null) return;
            var label = _btnDeploy.transform.Find("Tmp_Label");
            var t = label != null ? label.GetComponent<TMP_Text>() : null;
            if (t == null) return;

            var run = WanXiang.Run.RunSave.Current;
            bool hasProgress = run != null && (run.NodeOffset >= 0 || run.Act > 1);
            t.text = hasProgress
                ? string.Format("继续 · 第{0}幕 第{1}层", run.Act, run.NodeOffset + 1)
                : "出征";
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
            // v1.2 收口：这四个功能属于"旅途中遇到的节点"，不再从主城直接进。
            // 直接进会让玩家以为两边是同一件事（旧版就是这样，语义冲突）。
            Dialog.Tip("灵市", "请在旅程的路线图上走到「灵市」节点后进入。").Forget();
        }

        private void OnTaleClicked()
        {
            // v1.2 收口：这四个功能属于"旅途中遇到的节点"，不再从主城直接进。
            // 直接进会让玩家以为两边是同一件事（旧版就是这样，语义冲突）。
            Dialog.Tip("异闻", "请在旅程的路线图上走到「异闻」节点后进入。").Forget();
        }

        /// <summary>试炼：不经过节点地图，直接用内容目录组一场默认战斗进战斗场景。</summary>
        private void OnTrialClicked()
        {
            SceneFlow.EnterBattle(null);
        }

        private void OnForgeClicked()
        {
            // v1.2 收口：这四个功能属于"旅途中遇到的节点"，不再从主城直接进。
            // 直接进会让玩家以为两边是同一件事（旧版就是这样，语义冲突）。
            Dialog.Tip("铸魂台", "请在旅程的路线图上走到「铸魂台」节点后进入。").Forget();
        }

        private void OnOmenClicked()
        {
            // v1.2 收口：这四个功能属于"旅途中遇到的节点"，不再从主城直接进。
            // 直接进会让玩家以为两边是同一件事（旧版就是这样，语义冲突）。
            Dialog.Tip("天象", "请在旅程的路线图上走到「天象」节点后进入。").Forget();
        }

        private void OnSettingsClicked()
        {
            OpenPanelAsync<SettingsPanel>().Forget();
        }
    }
}
