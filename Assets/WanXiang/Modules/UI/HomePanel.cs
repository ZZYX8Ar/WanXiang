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
        /// <summary>主城是常驻底层，返回键绝不能关它 —— 否则上层面板（如节点地图）的
        ///  返回输入会穿透到这里，把主城关掉，屏幕只剩背景色（用户实测空白）。</summary>
        public override bool AllowBackClose => false;

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
            HideRetiredEntries();
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
            HideRetiredEntries();

            // 天阙抉择挂起 → 弹三选一（登天阙 / 续劫 / 归元）。Overlay 层盖住主城，必须选。
            if (SceneFlow.PendingFinale)
            {
                SceneFlow.PendingFinale = false;
                OpenPanelAsync<TrialPanel>().Forget();
            }
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
            // ★ 判据放宽：以前只认 NodeOffset>=0 / Act>1，但"进过节点却没通过"时
            //   NodeOffset 仍是 -1（节点改为延后推进）⇒ 判定成"无进度"，直接进图不弹窗
            //   （用户实测）。现在只要有过任何探索痕迹就算有进度。
            bool hasProgress = run != null && (
                run.NodeOffset >= 0 || run.Act > 1
                || (run.Path != null && run.Path.Count > 0)
                || (run.VisitedNodes != null && run.VisitedNodes.Count > 0)
                || run.Wins > 0 || run.Losses > 0);

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

            // ★ pick：0 = 重开一局、1 = 继续、-1 = 关闭/点遮罩（什么都不选）
            //   旧逻辑只判了 pick == 1，导致"点 × "也会掉进重开确认弹窗（用户实测）。
            if (pick != 0)
            {
                if (pick == 1)
                {
                    CloseSelf();                                            // ★ 关主界面，否则栈里残留
                    OpenPanelAsync<CampaignPanel>().Forget();               // 继续旅程
                }
                return;                                                   // -1 = 留在主界面
            }
            CloseSelf();                                                  // ★ 重开 = 也关主界面（后面回节点图）

            bool ok = await Dialog.Confirm("重开一局",
                "本次旅程的进度、灵卵与队伍编成都会清空，确定重开？", "确定重开", "再想想");
            if (!ok) return;

            run.Act = 1;
            run.NodeOffset = -1;
            run.Wins = 0;
            run.Eggs = 0;
            run.Ink = 0;
            run.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);   // 重开 = 全新路线图
            if (run.Path != null) run.Path.Clear();
            if (run.VisitedNodes != null) run.VisitedNodes.Clear();
            if (run.QuestionRevealed != null) run.QuestionRevealed.Clear();
            WanXiang.Run.RunSave.SaveCurrent();

            RefreshDeployLabel();
            OpenPanelAsync<CampaignPanel>().Forget();
        }


        /// <summary>
        /// 隐藏已收口到路线图节点的四个入口（灵市 / 异闻 / 铸魂台 / 天象）。
        /// 留着"能点但没用"的按钮比没有更糟 —— 玩家会反复点、然后以为游戏坏了。
        /// </summary>
        private void HideRetiredEntries()
        {
            // ① 已收口为路线图节点：主城不再提供入口
            if (_btnMarket != null) _btnMarket.gameObject.SetActive(false);
            if (_btnTale != null) _btnTale.gameObject.SetActive(false);
            if (_btnForge != null) _btnForge.gameObject.SetActive(false);
            if (_btnOmen != null) _btnOmen.gameObject.SetActive(false);

            // ② 「试炼」语义错配：它打开的 Panel_Trial 其实是 GDD 第 7 章的
            //    「天阙抉择」（通关后三选一：登天阙 / 续劫 / 归元），属于**流程内**面板，
            //    不该由主城直接进 —— GDD 全文没有"试炼"这个词（已核对，出现 0 次）。
            if (_btnTrial != null) _btnTrial.gameObject.SetActive(false);

            // ③ 「成长」= GDD 第 8 章局外成长（五条轨道 / 领奖励 / 数值增益封顶 8%），
            //    但 MetaPanel 目前只有 UI 骨架（OnCreate 里只有 TODO），
            //    点开只会看到静态界面 ⇒ 先隐藏，等实现再接回来。
            if (_btnMeta != null) _btnMeta.gameObject.SetActive(false);
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
            if (_btnMeta != null) _btnMeta.gameObject.SetActive(false);
        }

        /// <summary>
        /// v1.2 收口：该功能已改为路线图节点，主城不再提供入口。
        /// 按钮本身会在 OnOpenAsync 里隐藏 —— 保留这个空实现只是防"万一被别处调用"。
        /// </summary>
        private void OnMarketClicked()
        {
            if (_btnMarket != null) _btnMarket.gameObject.SetActive(false);
        }

        /// <summary>
        /// v1.2 收口：该功能已改为路线图节点，主城不再提供入口。
        /// 按钮本身会在 OnOpenAsync 里隐藏 —— 保留这个空实现只是防"万一被别处调用"。
        /// </summary>
        private void OnTaleClicked()
        {
            if (_btnTale != null) _btnTale.gameObject.SetActive(false);
        }

        /// <summary>试炼：不经过节点地图，直接用内容目录组一场默认战斗进战斗场景。</summary>
        private void OnTrialClicked()
        {
            if (_btnTrial != null) _btnTrial.gameObject.SetActive(false);
        }

        /// <summary>
        /// v1.2 收口：该功能已改为路线图节点，主城不再提供入口。
        /// 按钮本身会在 OnOpenAsync 里隐藏 —— 保留这个空实现只是防"万一被别处调用"。
        /// </summary>
        private void OnForgeClicked()
        {
            if (_btnForge != null) _btnForge.gameObject.SetActive(false);
        }

        /// <summary>
        /// v1.2 收口：该功能已改为路线图节点，主城不再提供入口。
        /// 按钮本身会在 OnOpenAsync 里隐藏 —— 保留这个空实现只是防"万一被别处调用"。
        /// </summary>
        private void OnOmenClicked()
        {
            if (_btnOmen != null) _btnOmen.gameObject.SetActive(false);
        }

        private void OnSettingsClicked()
        {
            OpenPanelAsync<SettingsPanel>().Forget();
        }
    }
}
