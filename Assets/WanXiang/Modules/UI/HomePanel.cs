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

        [SerializeField] private Image   _imgHero;         // Img_HeroSprite 主战异兽立绘
        [SerializeField] private Button  _btnDeploy;       // Hot_Deploy    出征
        [SerializeField] private Button  _btnCodex;        // Btn_Codex     图鉴
        [SerializeField] private Button  _btnMeta;            // Hot_Meta  局外成长（底排阵盘位，绑 MetaPanel）
        [SerializeField] private Button  _btnArchive;      // Btn_Archive 残卷阁（底排·卷轴图标）
        [SerializeField] private Button  _btnSettings;     // Hot_Settings 设置
        [SerializeField] private Button  _btnHistory;      // Btn_History 历程（底排·沙漏图标）
        [SerializeField] private HomeMapJourney _homeMap;  // Map_Journey   中央四季旅程图（进图刷新进度）
        [SerializeField] private HomeScene3D _scene3D;       // 3D 层：背景 + 异兽行走 + 天气粒子（2026-10-11）

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
            // ⚠ 2026-10-11：Root_TopBar（灵卵/墨锭/境界徽）已按用户要求整块删除，
            //   原先"SetActive(false) 隐藏"的两行也一并清掉（字段已删，否则编译不过）。
            //   灵卵在编阵/灵市里看，墨铊在局外成长面板看。

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

            // ---- 3D 层：背景 + 出战异兽走动 + 天气粒子（2026-10-11 混合方案）----
            // ⛔ 背景必须由 3D 层画：UIRoot 的 Canvas 是 ScreenSpaceOverlay，
            //   Overlay 永远渲染在所有相机之上 ⇒ UI 层只要有一张全屏不透明图，
            //   就会把 3D 的异兽/天气整个盖住（第一版就是这样，什么都看不见）。
            var ambRun = WanXiang.Run.RunSave.Current;
            if (_scene3D == null) _scene3D = GetComponent<HomeScene3D>();
            if (_scene3D == null) _scene3D = gameObject.AddComponent<HomeScene3D>();
            _scene3D.Init();
            _scene3D.Running = true;
            // 幕 + 幕内进度 → 氛围强度（雾/风/天气/自然元素随探索进度实时变化）
            int ambAct = ambRun != null ? Mathf.Clamp(ambRun.Act, 1, 5) : 1;
            float ambProgress = 0f;
            if (ambRun != null)
            {
                // 幕内进度：NodeOffset = 当前锚点节点（-1 = 尚未出发）。
                // 分母用层数×3（节点上限；实际节点数随种子浮动）—— 这里只驱动视觉强度，
                // 近似即可，不必精确到实际节点数。
                int approxNodes = WanXiang.Campaign.SolarTermGraph.DefaultLayers * 3;
                if (approxNodes > 0) ambProgress = Mathf.Clamp01((ambRun.NodeOffset + 1f) / approxNodes);
            }
            _scene3D.ApplyAmbience(ambAct, ambProgress);
            // ⚠ 2026-10-11：异兽行走已按用户要求移除（"看着太奇怪了"）。
            //   主界面的动态改由自然元素承担：飞鸟 / 草丛摇曳 / 河流流动 + 天气。

            // ⚠ 2026-10-11：2D 版 HomeAmbientFx（背景 + 季节粒子 + 雨丝/涟漪）已整体退役 ——
            //   背景与天气全部移交 3D 层（HomeScene3D），用户也明确"主界面不需要粒子"。
            //   组件与文件已删除，这里不再调用。

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
                { GoCampaign(); }
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
                if (pick == 1) { GoCampaign(); }   // 继续旅程
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
            { GoCampaign(); }
        }

        /// <summary>
        /// 带**转场**进入节点地图（2026-10-11 用户："从主界面进入节点地图也生硬，
        /// 加一些动效"）。转场方式按当前幕自动选：春·水墨铺开 / 夏·粒子右→左横扫 /
        /// 秋·双帘拨开 / 冬·粒子横扫 / 长夏·双帘拨开。
        /// ⚠ 转场中点才真正打开 CampaignPanel ⇒ 用户看到的顺序是
        ///   "特效盖住屏幕 → 换页 → 特效退去露出节点地图（并开始写字开场）"。
        /// </summary>
        private void GoCampaign()
        {
            CampaignPanel.RequestIntroFromMenu = true;
            var r = WanXiang.Run.RunSave.Current;
            int act = r != null ? Mathf.Clamp(r.Act, 1, 5) : 1;
            UiTransition.PlayAuto(act, () => OpenPanelAsync<CampaignPanel>().Forget());
        }


        /// <summary>
        /// 隐藏已收口到路线图节点的四个入口（灵市 / 异闻 / 铸魂台 / 天象）。
        /// 留着"能点但没用"的按钮比没有更糟 —— 玩家会反复点、然后以为游戏坏了。
        /// </summary>
        private void OnDisable()
        {
            // 主城被隐藏（进战斗等）⇒ 停掉 3D 层：相机与 3D 内容挂在 Canvas 之外，
            // 不随面板隐藏，必须显式停，否则在别的界面后面空转。
            if (_scene3D != null) _scene3D.Running = false;
        }

        protected override void OnResume()
        {
            base.OnResume();
            // 被上层弹层盖过再露出时恢复 3D 渲染（与 OnDisable 成对）
            if (_scene3D != null) _scene3D.Running = true;
        }

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
