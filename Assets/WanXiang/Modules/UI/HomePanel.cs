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

        // ---- 看板模式（2026-10-17：残卷阁集满 6 碎片解锁的动态立绘展示，设计文档见
        //      《万相_主界面动态立绘_设计文档.md》）----
        [SerializeField] private Button _btnPortrait;         // Btn_Portrait   底排「看板」入口（进立绘模式）
        [SerializeField] private GameObject _portraitRoot;    // Portrait_Root  立绘模式整组节点（默认隐藏）
        [SerializeField] private HomePortraitStage _portraitStage; // Portrait_Root 上的播放层
        [SerializeField] private Button _btnPortraitBack;     // Btn_Back       立绘模式 → 回地图
        [SerializeField] private Button _btnBoardManager;     // Btn_BoardManager 底排「立绘库」（轮换管理面板）

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

        private bool _portraitMode;   // 是否处于立绘展示模式

        // ---- 鼠标静止 15s 隐藏全部按钮（一动就恢复；性能开销 ≈ 每帧一次向量比较）----
        private readonly System.Collections.Generic.List<GameObject> _hideableButtons =
            new System.Collections.Generic.List<GameObject>(16);
        private Vector3 _lastMousePos;
        private float _idleTimer;
        private bool _buttonsHidden;
        private const float IdleHideSeconds = 15f;

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
            if (_btnPortrait != null) _btnPortrait.onClick.AddListener(OnPortraitClicked);
            if (_btnPortraitBack != null) _btnPortraitBack.onClick.AddListener(ExitPortrait);
            if (_btnBoardManager != null)
                _btnBoardManager.onClick.AddListener(() => OpenPanelAsync<PortraitBoardPanel>().Forget());
            CacheHideableButtons();
            _lastMousePos = Input.mousePosition;
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ⚠ 2026-10-11：Root_TopBar（灵卵/墨锭/境界徽）已按用户要求整块删除，
            //   原先"SetActive(false) 隐藏"的两行也一并清掉（字段已删，否则编译不过）。
            //   灵卵在编阵/灵市里看，墨铊在局外成长面板看。

            // ⚠ 常驻复用复位：上次开面板若停在立绘模式，这次回地图模式（_halted 同源坑）。
            ExitPortrait();

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
            //   Overlay 永远渲染在所有相机之上 ， UI 层只要有一张全屏不透明图，
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
            //   NodeOffset 仍是 -1（节点改为延后推进）， 判定成"无进度"，直接进图不弹窗
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

            // ★ 2026-10-20：改为**统一重置**（RunState.ResetForNewRun）——
            //   之前手写清单漏了 Relics / Team / PassedNodes / ChosenNodes（同层互斥）/ 待领奖励 / 秘境，
            //   导致"重开后还带着上一局的遗物""节点整层点不动"。
            run.ResetForNewRun(UnityEngine.Random.Range(1, int.MaxValue));   // 重开 = 全新路线图
            WanXiang.Campaign.LiveWeather.Reset();                     // 会话级星移余气一并清空，防跨局污染
            // 内存里的"待落地"也必须清：否则新局第一格会被上一局的待办劫持
            CampaignPanel.PendingCommit = -1;
            SceneFlow.BattleDeploying = false;   // 重开一局：解除任何遗留的出征锁
            WanXiang.Modules.UI.RealmSession.ClearPlayerHazards(run);  // 秘境给我方挂的临时状态一并清

            RefreshDeployLabel();
            { GoCampaign(); }
        }

        /// <summary>
        /// 带**转场**进入节点地图（2026-10-11 用户："从主界面进入节点地图也生硬，
        /// 加一些动效"）。转场方式按当前幕自动选：春·水墨铺开 / 夏·粒子右→左横扫 /
        /// 秋·双帘拨开 / 冬·粒子横扫 / 长夏·双帘拨开。
        /// ⚠ 转场中点才真正打开 CampaignPanel ， 用户看到的顺序是
        ///   "特效盖住屏幕 → 换页 → 特效退去露出节点地图（并开始写字开场）"。
        /// </summary>
        // 节点地图本幕背景（与 CampaignPanel 的 Img_MapBg 同一张，做转场贯穿底图用）
        // ⚠ 命名规则 map_bg_act{1..5}_{season}，season 顺序与 SeasonPalette 一致
        private static readonly string[] MapBgByAct =
        {
            "UI/Campaign/map_bg_act1_spring", "UI/Campaign/map_bg_act2_summer",
            "UI/Campaign/map_bg_act3_autumn", "UI/Campaign/map_bg_act4_winter",
            "UI/Campaign/map_bg_act5_latesummer",
        };

        private void GoCampaign()
        {
            CampaignPanel.RequestIntroFromMenu = true;
            var r = WanXiang.Run.RunSave.Current;
            int act = r != null ? Mathf.Clamp(r.Act, 1, 5) : 1;
            Debug.Log("[Home] 出征 → 播转场 act=" + act + " kind=" + UiTransition.KindForAct(act));

            // ★ 首进图的"环境级"预热（2026-10-14）：实测首次进图会卡两刀
            //   （切换帧 ~0.4s + 开场首帧 ~0.5s），且已定位为"完整游戏环境
            //   （主界面 3D + 本转场粒子墙都在渲染）下的首次组合渲染"——
            //   放在更早的转场里预热不触发。所以就在本转场的遮盖期里先把冷面板
            //   真开一遍（开→渲染→关，静默），代价全落在屏幕被盖住的时候。
            System.Func<Cysharp.Threading.Tasks.UniTask> coldPre = null;
            var uiRef = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (uiRef != null && (!CampaignPanel.SessionOpened || !ActIntroPanel.SessionPlayed))
            {
                coldPre = async () =>
                {
                    if (!CampaignPanel.SessionOpened)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await uiRef.GhostOpenCloseAsync<CampaignPanel>();
                        Debug.Log("[Home] 遮盖期彩排 CampaignPanel " + sw.ElapsedMilliseconds + "ms");
                    }
                    if (!ActIntroPanel.SessionPlayed)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await uiRef.GhostOpenCloseAsync<ActIntroPanel>();
                        Debug.Log("[Home] 遮盖期彩排 ActIntroPanel " + sw.ElapsedMilliseconds + "ms");
                    }
                };
            }

            // ★ 遮盖层用**节点地图本幕的背景图**当底 ， 转场盖住时就是同一张底图，
            //   退场淡出露出的也是它 ， 全程一张底图、不跳变
            //   （用户 2026-10-12：'因为转场完又有一个背景图，能不能把这个背景图贯穿'）。
            // tag=Home2Map：与「开始→主界面」链路在 Console 里可区分，互不混淆
            UiTransition.PlayAuto(act, () => OpenPanelAsync<CampaignPanel>().Forget(),
                                  MapBgByAct[act - 1], 2.4f, "Home2Map", coldPre);
        }


        /// <summary>
        /// 隐藏已收口到路线图节点的四个入口（灵市 / 异闻 / 铸魂台 / 天象）。
        /// 留着"能点但没用"的按钮比没有更糟 —— 玩家会反复点、然后以为游戏坏了。
        /// </summary>
        private void OnDisable()
        {
            // 主城被隐藏（进战斗等）， 停掉 3D 层：相机与 3D 内容挂在 Canvas 之外，
            // 不随面板隐藏，必须显式停，否则在别的界面后面空转。
            if (_scene3D != null) _scene3D.Running = false;
            // 立绘模式一并停播（视频播放器挂在 Canvas 内，但状态必须复位）
            if (_portraitStage != null) _portraitStage.Leave();
            _portraitMode = false;
            if (_buttonsHidden) ShowButtons();   // 回到主界面按钮必须是可见的
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

        // ==================================================================== 看板模式（2026-10-17）
        //  参考《蔚蓝档案》主界面：切换后全屏展示已解锁异兽的动态立绘（视频），
        //  切回恢复四季旅程图。解锁来源 = 残卷阁集满 6 片剧情碎片后领取。

        private void OnPortraitClicked()
        {
            if (_portraitMode) { ExitPortrait(); return; }
            OnPortraitEnterClickedAsync().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid OnPortraitEnterClickedAsync()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            var ids = meta != null ? new System.Collections.Generic.List<string>(meta.UnlockedLivePortraits) : null;
            if (ids == null || ids.Count == 0)
            {
                // 一只都没解锁：指路残卷阁（代价写在脸上——告诉玩家怎么解锁）
                int pick = await Dialog.Choose(
                    "还没有动态立绘",
                    "在残卷阁集满某只异兽的 6 片剧情碎片，\n即可领取它的动态立绘（主界面看板展示）。",
                    "稍后再说", "去残卷阁");
                if (pick == 1) OpenPanelAsync<ArchivePanel>().Forget();
                return;
            }
            EnterPortrait(ids, meta);
        }

        private void EnterPortrait(System.Collections.Generic.List<string> ids, WanXiang.Meta.MetaState meta)
        {
            if (_portraitRoot == null || _portraitStage == null)
            {
                Debug.LogError("[Home] Portrait_Root / _portraitStage 未绑定（跑补丁：万相/UI/补丁：主界面动态立绘）");
                return;
            }
            _portraitMode = true;
            if (_buttonsHidden) ShowButtons();

            // 轮换序列 = 看板库选择的（最多 5 个，按玩家点击顺序）∩ 已解锁；
            // 没选过 → 看板兽单只兜底。
            var rot = new System.Collections.Generic.List<string>();
            foreach (var id in PortraitBoardPrefs.LoadSelected())
                if (ids.Contains(id) && !rot.Contains(id)) rot.Add(id);
            if (rot.Count == 0)
            {
                string pref = !string.IsNullOrEmpty(meta.HomePortraitId) && ids.Contains(meta.HomePortraitId)
                    ? meta.HomePortraitId : ids[0];
                rot.Add(pref);
            }

            _portraitRoot.SetActive(true);
            // 入口按钮是 Portrait_Root 的后置兄弟节点，会透到视频上层 → 立绘模式藏起来
            if (_btnPortrait != null) _btnPortrait.gameObject.SetActive(false);
            if (_btnBoardManager != null) _btnBoardManager.gameObject.SetActive(false);
            if (_scene3D != null) _scene3D.Running = false;   // 3D 层停渲染省电（切回时恢复）

            _portraitStage.NameResolver = BeastLabelOf;
            _portraitStage.ShownChanged -= OnPortraitShown;
            _portraitStage.ShownChanged += OnPortraitShown;
            _portraitStage.Enter(rot, rot[0]);
        }

        private void ExitPortrait()
        {
            if (!_portraitMode) return;   // 未进入时是幂等空操作（OnOpenAsync 复位也调它）
            _portraitMode = false;
            if (_portraitStage != null) _portraitStage.Leave();
            if (_portraitRoot != null) _portraitRoot.SetActive(false);
            if (_btnPortrait != null) _btnPortrait.gameObject.SetActive(true);
            if (_btnBoardManager != null) _btnBoardManager.gameObject.SetActive(true);
            if (_scene3D != null) _scene3D.Running = true;    // 与 OnDisable 成对恢复
        }

        /// <summary>换片回调：手动切换才落盘记忆看板兽（自动轮换不写档，30s 一次太吵）。</summary>
        private void OnPortraitShown(string beastId, bool manual)
        {
            if (!manual) return;
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (meta == null || meta.HomePortraitId == beastId) return;
            meta.HomePortraitId = beastId;
            WanXiang.Meta.MetaStore.Save();
        }

        /// <summary>id → "句芒 ·《山海经·海外东经》"（找不到就原样回 id）。</summary>
        private string BeastLabelOf(string id)
        {
            var beasts = LoadBeastDefs();
            if (beasts != null)
            {
                for (int i = 0; i < beasts.Length; i++)
                {
                    if (beasts[i] == null || beasts[i].Id != id) continue;
                    return string.IsNullOrEmpty(beasts[i].Source)
                        ? beasts[i].DisplayName
                        : beasts[i].DisplayName + " · " + beasts[i].Source;
                }
            }
            return id;
        }

        private static WanXiang.Battle.Core.BeastDef[] LoadBeastDefs()
        {
            if (_beastCache != null) return _beastCache;
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) return null;
            _beastCache = WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
            return _beastCache;
        }

        private static WanXiang.Battle.Core.BeastDef[] _beastCache;

        // ==================================================================== 鼠标空闲隐藏按钮（参考蔚蓝档案）
        //  主界面鼠标不动超过 15 秒 → 全部按钮隐藏（地图/背景保留）；
        //  鼠标一动立刻恢复。性能开销 ≈ 每帧一次向量比较，可忽略。
        //  立绘模式不走这里（它有自己的「隐藏界面」按钮）。

        private void Update()
        {
            if (_portraitMode)
            {
                if (_buttonsHidden) ShowButtons();
                return;
            }

            var m = Input.mousePosition;
            if ((m - _lastMousePos).sqrMagnitude > 9f)          // 动了（>3px 视为有效移动）
            {
                _lastMousePos = m;
                _idleTimer = 0f;
                if (_buttonsHidden) ShowButtons();
            }
            else
            {
                _idleTimer += Time.unscaledDeltaTime;
                if (!_buttonsHidden && _idleTimer >= IdleHideSeconds) HideButtons();
            }
        }

        /// <summary>收集立绘模式以外的全部按钮（OnCreate 一次；prefab 增删按钮后重开面板自动刷新不了
        ///  —— 常驻面板只建一次，新增按钮要同时补这里）。</summary>
        private void CacheHideableButtons()
        {
            _hideableButtons.Clear();
            var portraitRoot = _portraitRoot != null ? _portraitRoot.transform : null;
            var btns = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                if (portraitRoot != null && b.transform.IsChildOf(portraitRoot)) continue; // 立绘模式自带隐藏
                _hideableButtons.Add(b.gameObject);
            }
        }

        private void HideButtons()
        {
            _buttonsHidden = true;
            for (int i = 0; i < _hideableButtons.Count; i++)
                if (_hideableButtons[i] != null) _hideableButtons[i].SetActive(false);
        }

        private void ShowButtons()
        {
            _buttonsHidden = false;
            for (int i = 0; i < _hideableButtons.Count; i++)
                if (_hideableButtons[i] != null) _hideableButtons[i].SetActive(true);
            _idleTimer = 0f;
        }
    }
}
