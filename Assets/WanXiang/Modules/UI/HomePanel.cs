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
        [SerializeField] private Button  _btnMeta;            // Hot_Meta  局外成长（底排阵盘位，绑 MetaPanel）
        [SerializeField] private Button  _btnArchive;      // Btn_Archive 残卷阁（底排·卷轴图标）
        [SerializeField] private Button  _btnSettings;     // Hot_Settings 设置
        [SerializeField] private Button  _btnHistory;      // Btn_History 历程（底排·沙漏图标）
        [SerializeField] private HomeMapJourney _homeMap;  // Map_Journey   中央四季旅程图（进图刷新进度）
        [SerializeField] private HomeAmbientFx _ambientFx;      // 背景 + 天气特效（2026-10-11）

        // ⚠ 2026-10-11 布局调整（用户要求）：
        //   ① 左侧竖排三个按钮（局外成长/历程/残卷阁）移到**下方横排**，与图鉴/设置同排；
        //   ② 底排的阵盘图标（Hot_Meta）改绑 MetaPanel（局外成长）—— 原来 _btnMeta 字段
        //      误绑到左侧的 Btn_Meta，导致点底排阵盘"没反应"；左侧那个重复的已删除。
        //   ③ 三个字段（_btnMeta/_btnArchive/_btnHistory）现在都指向**底排**节点。

        // ⚠ 2026-10-11 清理：灵市/异闻/铸魂/天象/试炼 这5 个主城入口已退役 ——
        //   ① 前四个 v1.2 起收口为路线图节点（CampaignPanel 里进）；
        //   ② 试炼打开的 Panel_Trial 实为「天阙抉择」（通关后三选一），语义错配，GDD 全文无"试炼"一词。
        //   它们原先是"隐藏 + 保留空 handler"，现已连prefab 节点、序列化字段、监听器一并删除。

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        // ---- 旅程状态（选档后有值；没选档给默认首程值）----
        private int RunEggs
        {
            get { return WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Eggs : 0; }
        }

        // ⚠ 2026-09-25 删掉了 RunInk 属性：局内墨锭（RunState.Ink）全项目无人消费，已一并删除。

        protected override void OnCreate()
        {
            HideRetiredEntries();
            if (_btnDeploy != null) _btnDeploy.onClick.AddListener(OnDeployClicked);
            if (_btnCodex != null) _btnCodex.onClick.AddListener(OnCodexClicked);
            if (_btnMeta != null) _btnMeta.onClick.AddListener(OnMetaClicked);
            if (_btnHistory != null) _btnHistory.onClick.AddListener(() => OpenPanelAsync<HistoryPanel>().Forget());
            if (_btnArchive != null) _btnArchive.onClick.AddListener(OnArchiveClicked);
            if (_btnSettings != null) _btnSettings.onClick.AddListener(OnSettingsClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ★ 左上角的灵卵/墨锭已按用户要求隐藏（信息重复：灵卵在编阵/灵市里看，
            //   墨铊在局外成长面板看）。prefab 里的节点保留，需要时可再打开。
            if (_tmpEggs != null) _tmpEggs.gameObject.SetActive(false);
            if (_tmpInk != null) _tmpInk.gameObject.SetActive(false);

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

            // ---- 背景 + 天气特效（2026-10-11）：按当前探索的幕换背景+ 季节粒子 ----
            if (_ambientFx == null) _ambientFx = GetComponent<HomeAmbientFx>();
            if (_ambientFx == null) _ambientFx = gameObject.AddComponent<HomeAmbientFx>();
            var ambRun = WanXiang.Run.RunSave.Current;
            _ambientFx.Running = true;          // ⛔ 必须开这个开关，否则特效组件的 Update 直接 return（粒子/雨/涟漪全不动）
            _ambientFx.Rebuild(ambRun != null ? ambRun.Act : 1);
            // 天气：取当前存档的天时（无则晴）。有 LiveWeather 时按灵卵剩余切阴天。
            _ambientFx.SetWeather(0);

            // 中央四季节气环：每次回到主城都按当前旅程进度重刷（幕/层 → 扇区点亮 + 标记位置）
            if (_homeMap != null) _homeMap.Refresh();

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
            // ★ 未出发（NodeOffset = -1）时显示"第 1 层"，不要显示"第 0 层"
            int layer = run != null ? Mathf.Max(1, run.NodeOffset + 1) : 1;
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
                { CampaignPanel.RequestIntroFromMenu = true; OpenPanelAsync<CampaignPanel>().Forget(); }
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
                if (pick == 1) { CampaignPanel.RequestIntroFromMenu = true; OpenPanelAsync<CampaignPanel>().Forget(); }   // 继续旅程
                return;                                                   // -1 = 留在主界面
            }

            bool ok = await Dialog.Confirm("重开一局",
                "本次旅程的进度、灵卵与队伍编成都会清空，确定重开？", "确定重开", "再想想");
            if (!ok) return;

            run.Act = 1;
            run.NodeOffset = -1;
            run.Wins = 0;
            run.Eggs = 0;
            run.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);   // 重开 = 全新路线图
            if (run.Path != null) run.Path.Clear();
            if (run.VisitedNodes != null) run.VisitedNodes.Clear();
            if (run.QuestionRevealed != null) run.QuestionRevealed.Clear();
            if (run.XingyiUsed != null) run.XingyiUsed.Clear();        // 星移次数随重开清零
            if (run.XingyiLingers != null) run.XingyiLingers.Clear();  // 星移余气持久化镜像随重开清零
            WanXiang.Campaign.LiveWeather.Reset();                     // 会话级星移余气一并清空，防跨局污染
            WanXiang.Run.RunSave.SaveCurrent();

            RefreshDeployLabel();
            { CampaignPanel.RequestIntroFromMenu = true; OpenPanelAsync<CampaignPanel>().Forget(); }
        }


        /// <summary>
        /// 隐藏已收口到路线图节点的四个入口（灵市 / 异闻 / 铸魂台 / 天象）。
        /// 留着"能点但没用"的按钮比没有更糟 —— 玩家会反复点、然后以为游戏坏了。
        /// </summary>
        /// <summary>
        /// 退役入口的隐藏逻辑。
        /// ⚠ 2026-10-11：灵市/异闻/铸魂/天象/试炼 已连 prefab 节点一起删除，本方法清空。
        ///    保留这个空壳只为让OnCreate 调用点不动，将来新退役入口仍在此登记。
        /// </summary>
        private void HideRetiredEntries()
        {
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

        /// <summary>残卷阁：局外主城入口 —— 查看各异兽的剧情碎片进度并领取觉醒材料。</summary>
        private void OnArchiveClicked()
        {
            OpenPanelAsync<ArchivePanel>().Forget();
        }

        private void OnMetaClicked()
        {
            // ★ 打开局外成长面板（原来这里只把自己隐藏了，等于点不进去）
            OpenPanelAsync<MetaPanel>().Forget();
        }

        private void OnSettingsClicked()
        {
            OpenPanelAsync<SettingsPanel>().Forget();
        }
    }
}
