// ============================================================================
//  Panel_Campaign —— 节点地图（五幕节气）
//  接的是 Modules/Campaign/Core 的既有战役 Core：
//    · SolarTermGraph.BuildDefault() 给出五幕图（每幕 6 个节气节点、4 层拓扑）
//    · 节点类型七种（遭遇/精英/灵市/孵穴/异闻/铸魂台/天象），非战斗节点直接
//      路由到已有面板（灵市→Panel_Market、异闻→Panel_Tale、铸魂台→Panel_Forge、
//      天象→Panel_Omen），战斗节点→Panel_Formation
//    · 可达性用 ActGraph.CanMove（只能走相邻下一层）
//  进度存 RunSave（Act / NodeOffset），与存档系统共用一条线。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    /// <summary>从一个节点进入布阵时携带的上下文。</summary>
    public sealed class NodeRequest
    {
        public string Title = "小暑 · 温风至";
        public string Weather = "本场精英位：每回合全场 3% 灼烧";
        public ulong Seed = 20260914UL;
        public int NodeIndex = 7;

        /// <summary>所属幕（1..5）与节点下标（幕内 0..5）。</summary>
        public int Act = 1;
        public int Offset = -1;

        /// <summary>节点类型（决定要不要打仗、带不带劫象）。</summary>
        public WanXiang.Campaign.NodeKind Kind = WanXiang.Campaign.NodeKind.Encounter;

        /// <summary>
        /// 是否**守关**节点（幕末最后一格）。图里这一格复用 <see cref="WanXiang.Campaign.NodeKind.Elite"/>
        /// 作类型占位，光看 Kind 分不出「普通精英」与「守关」—— 所以由 CampaignPanel 显式置位。
        /// 战斗工厂据此调 SeededEnemyProvider.BossSquadFor（Boss + 随从），而不是普通精英抽签。
        /// </summary>
        public bool IsBoss;

        /// <summary>
        /// 是否**终局战**节点（第 5 幕 / 天阙的最后一格）。
        /// ★ 用户 2026-10-03 实测：点这一格出征，打出来的是**普通精英战**（诸怀/旱魃/毕方），
        ///   不是归墟之主/鸿蒙。根因是 `IsBoss` 只覆盖幕 1~4，第 5 幕终局格 IsBoss=false
        ///   ⇒ FormationPanel 按普通遭遇建敌。终局战的构建方式（Boss + 我方镜像×3）与守关
        ///   完全不同，所以**单独立一个标志**，由 FormationPanel.OnDeployClicked 分派到
        ///   TrialPanel.BuildFinaleBattle，而不是靠 IsBoss 兼管。
        /// </summary>
        public bool IsFinale;
    }

    [UIPanel("Panel_Campaign", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class CampaignPanel : UIPanelBase
    {
        /// <summary>节点地图不允许返回键关闭 —— 关掉后 Home 不会自动重开，屏幕就是空白（用户实测）。
        ///  回主城走面板上的返回按钮。</summary>
        public override bool AllowBackClose => false;

        /// <summary>返回主城：节点图是全屏面板，只 Close 不 Open Home 会留白屏（用户实测）。</summary>
        private void OnBackToHome()
        {
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            CloseSelf();
            if (ui != null) _ = ui.OpenAsync<HomePanel>();
        }

        [SerializeField] private TMP_Text _tmpActTitle;        // Tmp_ActTitle  幕名
        [SerializeField] private TMP_Text _tmpJie;             // Tmp_JieCount  劫数

        /// <summary>待推进节点（跨面板共享）：出征时保留、返回地图时撤销。</summary>
        public static int PendingCommit = -1;      // 事件类节点：完成后才推进（未完成就关游戏 ⇒ 节点不通过、奖励不丢）
        [SerializeField] private ScrollRect _scrollNodes;      // Scroll_Nodes  节点长卷
        [SerializeField] private RectTransform _nodeItemTemplate;  // Item_Node（模板，默认隐藏）
        [SerializeField] private GameObject _rootNodeInfo;     // Root_NodeInfo 右侧信息卡
        [SerializeField] private TMP_Text _tmpNodeName;        // Tmp_NodeName
        [SerializeField] private TMP_Text _tmpNodeType;        // Tmp_NodeType
        [SerializeField] private TMP_Text _tmpWeather;         // Tmp_Weather
        [SerializeField] private Image _imgNodeIcon;           // Img_NodeIcon
        [BindArray("Img_Avatar_{0}", 5)]
        [SerializeField] private Image[] _imgAvatars;          // 队伍预览 5 个头像
        [SerializeField] private Button _btnNext;              // Btn_Next
        [SerializeField] private Button _btnBack;              // Btn_Back
        [SerializeField] private Button _btnXingyi;            // Btn_Xingyi  星移（注入余气）

        // 遗物 HUD（左上角一排小牌 + 悬浮说明；见 RelicHud / Editor/UITool/BuildRelicHudPatch.cs）
        [SerializeField] private RectTransform _rootRelics;    // Root_Relics
        [SerializeField] private GameObject _relicTemplate;    // Item_Relic
        [SerializeField] private GameObject _relicTipRoot;     // Root_RelicTip
        [SerializeField] private TMP_Text _relicTipText;       // Tmp_RelicTip

        /// <summary>星移余气持续节点数（Batch 3 星移）。每次星移把当前天时减半带入后续 N 节。</summary>
        private const int XINGYI_NODES = 3;
        /// <summary>星移消耗的灵卵数（2026-09-27 定案：不再免费，防无限制白嫖；一处调参）。</summary>
        private const int XINGYI_COST = 3;

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        private readonly NodeRequest _current = new NodeRequest();

        protected override void OnCreate()
        {
            if (_rootNodeInfo != null) _rootNodeInfo.SetActive(true);
            if (_btnNext != null) _btnNext.onClick.AddListener(OnNextClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(OnBackToHome);
            // 星移：把当前节点天时减半、注入为余气带入后续节点（仅在有时天时可见）
            if (_btnXingyi != null)
            {
                _btnXingyi.onClick.AddListener(OnXingyiClicked);
                _btnXingyi.gameObject.SetActive(false);   // 默认隐藏，选中有天时的节点才显示
            }
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ★ 跨存档恢复星移余气：把持久化的 XingyiLingers 重建为会话级 LiveWeather.Lingers，
            //   保证「用了星移→关游戏→重进」星移天气仍在（之前天气随会话丢失，等于白花灵卵）。
            RehydrateXingyi();

            // ★ 每一幕的开场/转场动画（宣纸底 + 大字逐字浮现 + 当季落叶）。
            //   触发口子**只放这一处**：进图与换幕都会经过 CampaignPanel.OnOpenAsync，
            //   免得两处各写一遍、行为还不一致。是否已播记在存档 RunState.IntroShownAct。
            {
                var introRun = WanXiang.Run.RunSave.Current;
                if (introRun != null && ActIntroPanel.ShouldPlayAndMark(introRun.Act))
                    OpenPanelAsync<ActIntroPanel>().Forget();   // 开面板只能由面板自己调（protected）
            }

            // 遗物 HUD（左上角一排小牌，悬浮看说明）
            var hudRun = WanXiang.Run.RunSave.Current;
            RelicHud.Populate(_rootRelics, _relicTemplate, hudRun != null ? hudRun.Relics : null,
                def => RelicHud.ShowTip(_relicTipRoot, _relicTipText, def),
                () => RelicHud.HideTip(_relicTipRoot));

            // ★ 待推进落地：从事件面板（灵市/孵穴/铸魂台/异闻/天象）返回节点图时，
            //   把之前未完成的节点记为通过 —— 玩家没处理完就关游戏的话，_pendingCommit 是内存变量、
            //   不会持久化，节点也不会推进 ⇒ 下次进来还能重做（奖励不丢）。
            if (PendingCommit >= 0)
            {
                var prun = WanXiang.Run.RunSave.Current;
                if (prun != null)
                {
                    prun.NodeOffset = PendingCommit;
                    // ★ 节点真正「通过」（离开事件/战斗面板回地图落地）⇒ 所有星移余气 -1（扣到负移除）。
                    //   推迟到此处而非「出征」点击，保证「进编队又返回」不算通过、星移仍可撤销
                    //   （用户实测：出征→编队→返回后星移撤销不了，根因就是出征时就把余气 -1 了）。
                    WanXiang.Campaign.LiveWeather.OnNodeCommitted();
                    SyncXingyiSaveFromLive();   // ★ 把星移余气剩余节点数同步回存档镜像
                    if (prun.VisitedNodes != null && !prun.VisitedNodes.Contains(PendingCommit))
                        prun.VisitedNodes.Add(PendingCommit);

                    // ★★ 天阙（第 5 幕）最后一格 = 后土：通过它 ⇒ 打【终局战】（而非回主城/推进）。
                    if (_graph != null && _graph.Act >= 5 && PendingCommit == _graph.NodeCount - 1)
                    {
                        var cat = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
                        var all = (cat != null && cat.Length > 0)
                            ? WanXiang.Fusion.ContentLibrary.BuildBeasts(cat[0]) : null;
                        if (all != null && all.Length > 0)
                        {
                            var req = TrialPanel.BuildFinaleBattle(prun, all);
                            SceneFlow.IsFinaleBattle = true;
                            PendingCommit = -1;
                            CloseSelf();
                            SceneFlow.EnterBattle(req);
                            return UniTask.CompletedTask;
                        }
                    }

                    // ★★ 幕推进绑在"通过"这一刻：刚通过的是本幕最后一格 ⇒ 进下一幕。
                    //    原先靠"人在最后一格(NodeOffset==NodeCount-1)"当判据是错的：
                    //    只要存档停在这个位置（不论是否真通过），一进节点图就会跳幕，
                    //    而且它同时被当作"可达性锚点"⇒ 全图锁定灰（用户实测）。
                    int lastCell = _graph != null ? _graph.NodeCount - 1 : -1;
                    if (lastCell >= 0 && PendingCommit == lastCell && prun.Act < 5)
                    {
                        prun.Act++;
                        prun.NodeOffset = -1;
                        if (prun.VisitedNodes != null) prun.VisitedNodes.Clear();
                        Debug.Log("[Campaign] 通过本幕最后一格 ⇒ 推进到第 " + prun.Act + " 幕");
                        WanXiang.Run.RunSave.SaveCurrent();

                        // ★★ 第四幕通关 ⇒ 第 5 幕 = 天阙：挂起"天阙抉择"，并直接回主城等玩家选。
                        //    旧实现靠 MainSceneEntry 里 `NodeOffset == 11` 判定（当时是 4 层×3 格的遗留），
                        //    既与现在的 12 层图对不上，又会被幕推进重置成 -1 ⇒ 天阙永远不会触发。
                        if (prun.Act >= 5)
                        {
                            // ★★ 关键：**不切场景**！就地重建天阙图 + 原地弹出天阙抉择。
                            //    原来这里 EnterMain() 切场景 ⇒ MainSceneEntry 跑两次 ⇒
                            //    面板实例在"关闭中"状态被再次 OpenAsync，返回实例却不激活
                            //    ⇒ 最后画面一片空白 / 主界面（日志实测：打开成功但 2 帧后 active=False）。
                            SceneFlow.PendingFinale = false;   // 不需要跨场景挂起了
                            Debug.Log("[Campaign] 已到天阙 ⇒ 就地弹出天阙抉择（不切场景）");
                            WanXiang.Run.RunSave.SaveCurrent();
                            RebuildGraphFor(prun.Act, prun.RunSeed);   // 换成天阙图（5 节点）
                            _currentOffset = -1;
                            _visited.Clear();
                            BuildNodeMap();
                            var uiF = WanXiang.Framework.Boot.UIBootstrap.UI;
                            if (uiF != null) uiF.OpenAsync<TrialPanel>().Forget();
                            return UniTask.CompletedTask;
                        }

                        RebuildGraphFor(prun.Act, prun.RunSeed);   // 换新幕的图
                        _currentOffset = -1;
                        _visited.Clear();
                    }
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                PendingCommit = -1;
            }

            BuildNodeMap();
            // ⚠ 图完整性告警：BuildRoute 在 64 次重试都失败时会回退到"缺省图"
            //   （旧 4 层结构、只有 6 个节点、布局重叠）—— 必须让它在 Console 里可见，
            //   否则就是"地图坏了但没有任何报错"（今天已经踩过这种静默降级）。
            if (_graph != null && _graph.Act < 5 && _graph.NodeCount < Layers)
                Debug.LogWarning("[Campaign] ⚠ 节点图不完整：NodeCount=" + _graph.NodeCount +
                                 " < 期望 " + Layers + " 层 —— BuildRoute 回退到了缺省图，" +
                                 "请检查 MeetsV12Constraints 或更换 RunSeed。");

Debug.Log("[Campaign] 图诊断：幕=" + (_graph != null ? _graph.Act.ToString() : "?") +
                      " NodeCount=" + (_graph != null ? _graph.NodeCount.ToString() : "?") +
                      " 实际画出=" + _nodeItems.Count +
                      "｜Act=" + (WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : -1) +
                      " RunSeed=" + (WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.RunSeed : 0));

            var node = payload as NodeRequest ?? _current;
            FillTeamPreview();
            SelectNode(node != null ? node.Offset : -1, silent: true);
            return UniTask.CompletedTask;
        }

        // ================================================================
        //  节点图（按 SolarTermGraph 的层拓扑生成）
        // ================================================================

        private static readonly Color NodeVisited = new Color(0.79f, 0.63f, 0.39f);   // 金
        private static readonly Color NodeReachable = new Color(0.47f, 0.57f, 0.38f); // 木绿
        private static readonly Color NodeLocked = new Color(0.61f, 0.58f, 0.53f);    // 灰

        // 路线图尺寸常量（连线的节点坐标必须与排布公式一致，所以提出来共用）
        private const int Layers = WanXiang.Campaign.SolarTermGraph.DefaultLayers;   // 路线图层数（每幕节点数；连线的节点坐标必须与排布公式一致）
        private const float NodeW = 380f, NodeH = 110f, GapX = 40f, GapY = 90f;
        private static readonly Color EdgeInk = new Color(0.72f, 0.68f, 0.60f, 0.9f);
        private static readonly Color PathGold = new Color(0.79f, 0.63f, 0.39f, 1f);
        private static readonly Color QuestionInk = new Color(0.45f, 0.42f, 0.62f, 1f);

        private readonly HashSet<int> _visited = new HashSet<int>();
        private WanXiang.Campaign.ActGraph[] _acts;
        private WanXiang.Campaign.ActGraph _graph;
        private int _currentOffset = -1;      // 当前所在节点（-1 = 还没出发）
        private int _selected = -1;
        private readonly List<RectTransform> _nodeItems = new List<RectTransform>(8);

        private void BuildNodeMap()
        {
            if (_scrollNodes == null || _nodeItemTemplate == null) return;
            var content = _scrollNodes.content;
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var old = content.GetChild(i).gameObject;
                old.SetActive(false);      // Destroy 要到帧末才生效，先失活避免同帧新旧共存
                Destroy(old);
            }
            _nodeItems.Clear();

            // ★★ 首帧强制刷新 Canvas：第一次打开本面板时，viewport/content 的 rect 还没被
            //    布局系统算出来，此时用它们的尺寸排版会错位（用户实测："第一次进不行、
            //    返回再进来就成功了"）。这里先强制算一次。
            UnityEngine.Canvas.ForceUpdateCanvases();

            // ★ 本图的实际层数（天阙图 5 层、普通幕 12 层）—— 摆位/高度/自动定位都用它。
            //  ⚠ 注意：本方法内部才建 `_graph`（见下方 BuildRoute），所以这里先给兜底值，
            //    建图之后必须**重新计算**一次 lc —— 否则永远拿到兜底的 12（用户实测：
            //    第一次进天阙图 content 高 2480、多出 1440 空白区）。
            int lc = Layers;

            // ---- 数据源：v1.2 路线图（Layers 层、层内 2~3、种子稳定）----
            var run = WanXiang.Run.RunSave.Current;
            // ★★ 存档自愈：早期幕推进判据用过 `>=`，可能把 Act 反复推到上限、
            //   或让 NodeOffset 越界，导致存档与节点图错位（用户实测"全部存档不能推进"）。
            //   这里把越界值钳回合理范围，保证存档一定能继续玩。
            if (run != null)
            {
                if (run.Act < 1 || run.Act > 5)
                {
                    run.Act = Mathf.Clamp(run.Act, 1, 5);
                    run.NodeOffset = -1;
                    Debug.LogWarning("[Campaign] 存档自愈：Act 越界 → 钳到 " + run.Act);
                }
                if (run.NodeOffset >= Layers * 3)      // 格号上限 = 层数 × 每层格数
                {
                    Debug.LogWarning("[Campaign] 存档自愈：NodeOffset 越界(" + run.NodeOffset + ") → 回到本幕起点");
                    run.NodeOffset = -1;
                }
                WanXiang.Run.RunSave.SaveCurrent();
            }

            int act = Mathf.Clamp(run != null ? run.Act : 1, 1, 5);
            // ⚠ 种子绑定**本局**（RunSeed）而不是槽位：局内重进是同一张图，
            //   重开一局 / 新档 → 新种子 → 全新路线图（用户：每局都要随机）。
            if (run != null && run.RunSeed == 0)
            {
                run.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);
                WanXiang.Run.RunSave.SaveCurrent();
            }
            ulong seed = CoreMath.Fnv1a("route:" + (run != null ? run.RunSeed : 0) + ":" + act);
            _graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(act, seed, Layers);

            // ★★ 建图后重算实际层数（上面那个 lc 是兜底值！）
            lc = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;

            // ★ 幕推进已改为"通过最后一格时"触发（见上方 PendingCommit 落地处），
            //   这里不再用"人在最后一格"当判据 —— 它既会误跳幕，又会让可达性锚点失效。

            // ★★ 存档自愈：NodeOffset 必须落在"本幕可继续"的范围内。
            //    停在幕末格（== NodeCount-1）多半是上一幕的残留位置（修复前产生的存档），
            //    它会同时导致"全图锁定灰"，这里一律回到本幕起点。
            if (run != null && _graph != null && run.NodeOffset >= _graph.NodeCount - 1)
            {
                Debug.LogWarning("[Campaign] 自愈：NodeOffset(" + run.NodeOffset +
                                 ") 停在幕末/越界 → 回到本幕起点");
                run.NodeOffset = -1;
                WanXiang.Run.RunSave.SaveCurrent();
            }

            _currentOffset = run != null ? run.NodeOffset : -1;
            _visited.Clear();          // readonly 字段只能就地清空（不能 new）
            if (run != null && run.VisitedNodes != null)
                foreach (var v in run.VisitedNodes) _visited.Add(v);

            if (_tmpActTitle != null)
                _tmpActTitle.text = "第" + CnNum(_graph.Act) + "幕 · " + _graph.SeasonCn +
                                    " · 守关 " + _graph.BossName;
            if (_tmpActTitle != null) _tmpActTitle.text += "　｜　灵卵 " + (run != null ? run.Eggs : 0) + " 枚";
            if (_tmpJie != null)
                _tmpJie.text = run != null ? run.RealmText : "第一境 · 第一劫";

            if (_graph.IsEmpty) return;

            // ⚠ 手动排布前必须关掉 content 上的自动布局：
            //   VerticalLayoutGroup / ContentSizeFitter 会按"里面的元素"自己算高度，
            //   把我们设的 sizeDelta 覆盖掉 —— 表现出来是滚动范围不对、下面的层滑不到。
            DisableAutoLayout(content);

            // ---- 连线层：先建（渲染在节点之下）----
            var lineLayer = new GameObject("LineLayer", typeof(RectTransform));
            var lineRt = (RectTransform)lineLayer.transform;
            lineRt.SetParent(content, false);
            lineRt.anchorMin = lineRt.anchorMax = new Vector2(0.5f, 1f);
            lineRt.pivot = new Vector2(0.5f, 1f);
            lineRt.anchoredPosition = Vector2.zero;
            lineRt.sizeDelta = Vector2.zero;

            // ---- 节点：**从下往上**（第 0 层在底部，守关在顶部）----
            for (int layer = 0; layer < _graph.Layers.Length; layer++)
            {
                var row = _graph.Layers[layer];
                // ★★ 用【图的真实层数】而不是常量 Layers(12)：
                //    天阙图只有 5 层，用 12 会把节点摆到很下面、content 也算出 2480 的空白区，
                //    表现就是"往上滑一片空白、还滑不回来"（用户实测）。
                float y = -(lc - 1 - layer) * (NodeH + GapY);   // 层号越大越靠上
                for (int k = 0; k < row.Length; k++)
                {
                    var item = SpawnNodeItem(content, row[k], layer);
                    item.sizeDelta = new Vector2(NodeW, NodeH);
                    item.anchorMin = item.anchorMax = new Vector2(0.5f, 1f);
                    item.pivot = new Vector2(0.5f, 1f);
                    float x = row.Length <= 1
                        ? 0f
                        : (k - (row.Length - 1) * 0.5f) * (NodeW + GapX);
                    item.anchoredPosition = new Vector2(x, y);
                }
            }

            DrawEdges(lineRt);

            float totalH = lc * (NodeH + GapY) + 80f;      // lc = 实际层数
            // ★★ content 高度必须 >= 视口高度，否则 ScrollRect 的滚动范围会算错，
            //    表现是"往上滑过头就回不来了 / 一片空白"（用户实测：天阙图只有 5 个节点时）。
            float viewH = (_scrollNodes != null && _scrollNodes.viewport != null)
                ? _scrollNodes.viewport.rect.height : 0f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(totalH, viewH + 1f));

            // 打开时滚到当前层（12 层比一屏高，别让玩家自己找）
            float curY = (_currentOffset >= 0)
                ? -(lc - 1 - _graph.LayerOf(_currentOffset)) * (NodeH + GapY)
                : -(lc - 1) * (NodeH + GapY);
            // ⚠ content.sizeDelta 刚改过，布局要等下一次 Canvas 更新才算完 ——
            //   不 ForceUpdate 的话这行设置会被后续布局覆盖，玩家只能自己往上滑（用户实测）。
            UnityEngine.Canvas.ForceUpdateCanvases();
            if (_scrollNodes != null && _scrollNodes.viewport != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_scrollNodes.viewport);
            UnityEngine.Canvas.ForceUpdateCanvases();

_scrollNodes.verticalNormalizedPosition = Mathf.Clamp01(1f - (Mathf.Abs(curY) - 200f) / Mathf.Max(1f, totalH));
        }

        /// <summary>关掉 content 上的自动布局组件（手动排布的前提）。</summary>
        private static void DisableAutoLayout(RectTransform content)
        {
            // ★★ 修正视口 —— prefab 里 Viewport 的高度是 **负数**（-350），
            //    会导致 ScrollRect 的滚动范围完全算错：滑过头回不来、滚轮无效（用户实测）。
            //    负尺寸 = 布局炸裂（和 Dialog 的负 sizeDelta 同一个病）。
            // 本方法是静态的：用 content 的父级（ScrollRect 标准层级里就是 Viewport）取视口，
            // 不依赖 _scrollNodes 实例字段。
            var vp = content != null ? content.parent as RectTransform : null;
            if (vp != null && vp.rect.height <= 1f)
            {
                vp.offsetMin = Vector2.zero;
                vp.offsetMax = Vector2.zero;
                UnityEngine.Debug.LogWarning("[Campaign] 节点图视口高度异常(" + vp.rect.height +
                                             ") → 已铺满修正（否则滚动范围算错：滑过头回不来）");
            }

            var vlg = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;
            var hlg = content.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg != null) hlg.enabled = false;
            var fitter = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        /// <summary>层间连线。走过的边描金，其余淡墨 —— 一眼看出自己的路线。</summary>
        private void DrawEdges(RectTransform lineLayer)
        {
            if (_graph == null) return;

            for (int layer = 0; layer + 1 < _graph.Layers.Length; layer++)
            {
                var from = _graph.Layers[layer];
                var to = _graph.Layers[layer + 1];
                foreach (var a in from)
                {
                    // 按真拓扑边表画：图上看到的连线 = 真正能走的路（所见即所得）
                    if (_graph.Edges == null || a >= _graph.Edges.Count) continue;
                    foreach (var b in _graph.Edges[a])
                    {
                    Vector2 pa = NodePos(a);
                    Vector2 pb = NodePos(b);
                    bool walked = _visited.Contains(a) && _visited.Contains(b);

                    var go = new GameObject("Edge_" + a + "_" + b, typeof(RectTransform));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(lineLayer, false);
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb), walked ? 7f : 4f);
                    rt.anchoredPosition = pa;
                    float ang = Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg;
                    rt.localRotation = Quaternion.Euler(0f, 0f, ang);

                    var img = go.AddComponent<Image>();
                    img.sprite = WhiteSprite();
                    img.raycastTarget = false;
                    img.color = walked ? PathGold : EdgeInk;
                    }
                }
            }
        }

        /// <summary>节点中心的本地坐标（与 BuildNodeMap 的排布公式必须一致）。</summary>
        private Vector2 NodePos(int offset)
        {
            int layer = _graph.LayerOf(offset);
            if (layer < 0) return Vector2.zero;
            var row = _graph.Layers[layer];
            int k = System.Array.IndexOf(row, offset);
            float x = row.Length <= 1 ? 0f : (k - (row.Length - 1) * 0.5f) * (NodeW + GapX);
            int lc2 = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;
            float y = -(lc2 - 1 - layer) * (NodeH + GapY) - NodeH * 0.5f;
            return new Vector2(x, y);
        }

        private static Sprite _white;
        private static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            return _white;
        }

        private RectTransform SpawnNodeItem(RectTransform content, int offset, int layer)
        {
            var item = Instantiate(_nodeItemTemplate, content);
            item.name = "Item_Node_" + offset;
            item.gameObject.SetActive(true);

            var kind = _graph.KindOf(offset);
            bool isHere = offset == _currentOffset;
            bool canGo = IsReachable(offset);
            bool passed = _visited.Contains(offset);

            var label = item.Find("Tmp_NodeText") != null
                ? item.Find("Tmp_NodeText").GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                // ★ 守卫格在图里都是【精英】占位（NodeKind 没有 Boss 类型），显示上必须讲明：
                //   · 幕 1~4 守关  → 「守关 · <首领名>」，走 BossSquadFor 抽 BossCatalog 首领
                //   · 幕 5 天阙末格 → 「终局 · 后土」，走 TrialPanel.BuildFinaleBattle
                // ⚠ 两个分支都要在这里判：只判 IsSeasonBossNode 会让**天阙终局格漏成【精英】**
                //   （实测截图：第 5 幕"小寒"格显示【精英】，玩家以为没有 boss 关）。
                bool isSeasonBossCell = IsSeasonBossNode(offset);
                bool isFinaleCell = _graph != null && _graph.Act >= 5 &&
                                    _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;
                string name;
                if (kind == WanXiang.Campaign.NodeKind.Question && !IsRevealed(offset))
                    name = "？ 未知";
                else if (kind == WanXiang.Campaign.NodeKind.Question)
                    name = WanXiang.Campaign.NodeKinds.Cn(RevealedKind(offset));   // 揭晓后显示真实类型
                else if (isFinaleCell)
                    name = "终局 · " + (_graph.BossName ?? "后土");
                else if (isSeasonBossCell)
                    name = "守关 · " + (_graph.BossName ?? "首领");
                else
                    name = WanXiang.Campaign.NodeKinds.Cn(kind);
                label.text = (offset + 1) + ". " + TermName(_graph.Terms[offset]) + "　【" + name + "】" +
                             (isHere ? "　◀ 当前" : canGo ? "　← 可前往" : passed ? "　已过" : "");
                label.color = isHere ? NodeVisited : canGo ? NodeReachable : passed ? PathGold : NodeLocked;
            }

            var dot = item.Find("Img_Dot") != null ? item.Find("Img_Dot").GetComponent<Image>() : null;
            if (dot != null)
            {
                bool q = kind == WanXiang.Campaign.NodeKind.Question && !IsRevealed(offset);
                dot.color = q ? QuestionInk : isHere ? NodeVisited : canGo ? NodeReachable
                              : passed ? PathGold : NodeLocked;
            }

            var btn = item.GetComponent<Button>();
            if (btn == null) btn = item.gameObject.AddComponent<Button>();
            btn.targetGraphic = item.GetComponent<Image>();
            // 已过的节点：不可再点 + 变灰 + 右上角一个 ✕（用户要求"通过了就不能再点"）
            btn.interactable = canGo || isHere;

            var mark = item.Find("Tmp_Done") != null ? item.Find("Tmp_Done").GetComponent<TMP_Text>() : null;
            if (mark == null)
            {
                var mrt = new GameObject("Tmp_Done", typeof(RectTransform)).GetComponent<RectTransform>();
                mrt.SetParent(item, false);
                mrt.anchorMin = new Vector2(1f, 1f);
                mrt.anchorMax = new Vector2(1f, 1f);
                mrt.pivot = new Vector2(0.5f, 0.5f);
                mrt.anchoredPosition = new Vector2(-30f, -20f);
                mrt.sizeDelta = new Vector2(60f, 50f);
                mark = mrt.gameObject.AddComponent<TextMeshProUGUI>();
                mark.fontSize = 40;
                mark.fontStyle = FontStyles.Bold;
                mark.alignment = TextAlignmentOptions.Center;
                mark.raycastTarget = false;
            }
            mark.gameObject.SetActive(passed && !isHere && !canGo);
            if (mark.gameObject.activeSelf) mark.color = new Color(0.55f, 0.52f, 0.46f, 0.9f);
            var captured = offset;
            btn.onClick.AddListener(() => SelectNode(captured, silent: false));

            if (isHere) item.SetAsFirstSibling();
            _nodeItems.Add(item);
            return item;
        }

        /// <summary>读揭晓结果（没揭晓返回 Question 本身）。</summary>
        private WanXiang.Campaign.NodeKind RevealedKind(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && run.QuestionRevealed != null)
                foreach (var rec in run.QuestionRevealed)
                    if (rec != null && rec.StartsWith(offset + ":"))
                        return (WanXiang.Campaign.NodeKind)int.Parse(rec.Substring(rec.IndexOf(':') + 1));
            return WanXiang.Campaign.NodeKind.Question;
        }

        /// <summary>问号是否已揭晓（读存档的 "offset:kind" 记录）。</summary>
        private bool IsRevealed(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.QuestionRevealed == null) return false;
            foreach (var rec in run.QuestionRevealed)
                if (rec != null && rec.StartsWith(offset + ":")) return true;
            return false;
        }

        /// <summary>可达 = 起点层，或"与当前节点有边相连"（真拓扑，杀戮尖塔式解锁）。</summary>
        private bool IsReachable(int offset)
        {
            if (_graph == null) return false;
            if (_currentOffset < 0) return _graph.LayerOf(offset) == 0;
            return _graph.HasEdge(_currentOffset, offset);
        }

        /// <summary>选中一个节点：刷新信息卡与"出征/前往"按钮文案。</summary>
        /// <summary>
        /// 重置显示状态（归元/开新局时用）—— 光关面板不够，`_currentOffset`/`_visited`
        /// 这些字段还留着上一局的值，重开时会沿用旧位置（用户实测：归元后重进
        /// 落点在第 27 节，而不是起点）。
        /// </summary>
        public void ResetViewForNewRun()
        {
            _currentOffset = -1;
            _visited.Clear();
            // ★ 跨局清空星移余气（会话级、非存档），避免上一局的余气污染新局天气
            WanXiang.Campaign.LiveWeather.Reset();
            var _run = WanXiang.Run.RunSave.Current;
            if (_run != null && _run.XingyiLingers != null) _run.XingyiLingers.Clear();   // 持久化镜像一并清，防跨局残留
            Debug.Log("[Campaign] 视图状态已重置（_currentOffset=-1, visited 清空）");
        }

        /// <summary>
        /// 按【当前存档】重建图并重画 —— 供天阙抉择里"续劫"复用本面板时调用
        /// （节点图就在栈里，弹窗关掉后会露出来；只需让它按新的 Act 重排）。
        /// </summary>
        public void ReloadForCurrentAct()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            RebuildGraphFor(run.Act, run.RunSeed);
            _currentOffset = run.NodeOffset;
            _visited.Clear();
            if (run.VisitedNodes != null)
                foreach (var v in run.VisitedNodes) _visited.Add(v);
            BuildNodeMap();
            Debug.Log("[Campaign] 已按当前存档重排节点图：Act=" + run.Act +
                      " Layers=" + (_graph != null && _graph.Layers != null ? _graph.Layers.Length : -1) +
                      " NodeCount=" + (_graph != null ? _graph.NodeCount : -1));
        }

        /// <summary>按幕号重建路线图（换幕时用）。</summary>
        private void RebuildGraphFor(int act, int runSeed)
        {
            int a = Mathf.Clamp(act, 1, 5);
            ulong sd = CoreMath.Fnv1a("route:" + runSeed + ":" + a);
            _graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(a, sd, Layers);
        }

        private void SelectNode(int offset, bool silent)
        {
            if (_graph == null)
            {
                if (!silent) Debug.LogWarning("[Campaign] 节点图还没建好。");
                return;
            }
            if (offset < 0 || offset >= _graph.NodeCount)
            {
                // ★★ 越界必须先归零！否则下面循环条件（offset < 0）不成立、循环不执行，
                //    带着越界值走到 Terms[offset] 就 IndexOutOfRange（用户实测：奖励后回节点图报错）。
                //    越界来源：payload 带的 Offset 属于旧图/上一幕，格号超出当前图范围。
                if (offset >= _graph.NodeCount) offset = -1;

                // 没有 payload 时默认指向第一个可前往的节点，避免右侧信息卡空着
                for (int l = 0; l < _graph.Layers.Length && offset < 0; l++)
                    foreach (var o in _graph.Layers[l])
                        if (IsReachable(o)) { offset = o; break; }
                if (offset < 0) return;
            }

            _selected = offset;
            var kind = _graph.KindOf(offset);
            string term = TermName(_graph.Terms[offset]);

            if (_tmpNodeName != null) _tmpNodeName.text = "第 " + (offset + 1) + " 节 · " + term;
            // ★ 天阙最后一格 = 终局战（后土）：类型显示为"终局·后土"，而不是占位用的【精英】
            bool isFinaleBoss = _graph != null && _graph.Act >= 5 && offset == _graph.NodeCount - 1;
            if (isFinaleBoss && _tmpNodeName != null)
                _tmpNodeName.text = "终局 · " + (_graph.BossName ?? "后土");

            // ★★ 守关（幕 1~4 的最后一格）也必须改类型标签：它在图里复用【精英】作占位，
            //   光看 kind 会显示成"【精英】"——和"普通精英"完全看不出区别，玩家不知道下一格是首领。
            //   isFinaleBoss（第 5 幕）走上面那行；这里只管四季幕的守关。
            bool isSeasonBoss = IsSeasonBossNode(offset);
            if (isSeasonBoss && _tmpNodeType != null)
                _tmpNodeType.text = "守关 · " + (_graph.BossName ?? "首领") + "　（战斗）";
            else if (_tmpNodeType != null && !isFinaleBoss)
                _tmpNodeType.text = WanXiang.Campaign.NodeKinds.Cn(kind) +
                                    (WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "　（战斗）" : "　（休整）");
            if (_tmpWeather != null) _tmpWeather.text = WeatherHint(kind, isSeasonBoss, isFinaleBoss);

            // _current 是常驻实例（表单里被 Btn_Next 复用），逐字段赋值而不是换新对象
            _current.Title = term;
            _current.Weather = WeatherHint(kind, isSeasonBoss, isFinaleBoss);
            _current.Seed = (ulong)(_graph.Act * 1000 + offset + 7);
            _current.NodeIndex = offset;
            _current.Act = _graph.Act;
            _current.Offset = offset;
            _current.Kind = kind;
            // ★ 守关 = 幕末最后一格（图里它是【精英】占位，靠这个标志与"普通精英"区分）。
            //   只对**四季幕（1~4）**置位：这些幕的守关抽 BossCatalog 首领（BossSquadFor）。
            //   天阙（第 5 幕）最后一格是终局战（后土），由 TrialPanel.BuildFinaleBattle 处理，
            //   不走这里，故排除，避免"双重首领"。
            _current.IsBoss = _graph.Act >= 1 && _graph.Act <= 4 &&
                              _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;

            // ★ 终局战格（第 5 幕末格）：走 TrialPanel.BuildFinaleBattle（Boss + 我方镜像×3），
            //   与守关（BossSquadFor：Boss + 2 随从）是**两条不同的建敌路径**，必须分开标。
            //   ⚠ 少了这个标志，第 5 幕最后一格会退回普通遭遇 —— 用户实测"第五幕 boss 关
            //     还是小异兽（诸怀/旱魃/毕方），标题只写天时、没有 Boss 关字样"。
            _current.IsFinale = _graph.Act >= 5 &&
                                _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;

            // ★ 活链路天气：选节点时算好「节点天时 + 重放 run.Path 还原的余气」合成一份
            //   WeatherDef 存入 LiveWeather.Current，供 BattleRequestFactory / FormationPanel 取用
            //   （取代原来只给占位的 WeatherHint）。可复现性红线：算不出 = null（旧行为逐位一致）。
            // ★ 星移区统一刷新：合成天时 → 信息卡（标题 + 去重后的效果行 + 星移摘要）→ 按钮状态。
            //   每节点限一次（写存档：换节点/进出编队/重启都不重置）；未出发可撤销；灵卵不足置灰。
            RefreshXingyiUI();

            // 选中反馈：节点本身要有变化 —— 只看右侧信息卡不够明显（玩家会以为没点到）
            for (int i = 0; i < _nodeItems.Count; i++)
            {
                var it = _nodeItems[i];
                if (it == null) continue;
                bool on = it.name == "Item_Node_" + offset;
                it.localScale = on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
                var d = it.Find("Img_Dot") != null ? it.Find("Img_Dot").GetComponent<Image>() : null;
                if (d != null && on) d.color = NodeVisited;
                else if (d != null && IsReachable(int.Parse(it.name.Substring("Item_Node_".Length))))
                    d.color = NodeReachable;
            }

            if (_btnNext != null)
            {
                var label = _btnNext.transform.Find("Tmp_Label");
                var t = label != null ? label.GetComponent<TMP_Text>() : null;
                if (t != null)
                    t.text = WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "出征" : "前往 · " +
                             WanXiang.Campaign.NodeKinds.Cn(kind);
            }
        }

        /// <summary>
        /// 该格是不是**四季幕（1~4）的守关**（幕末最后一格）。
        /// ⚠ 唯一口径：类型标签、说明文案、<c>NodeRequest.IsBoss</c> 三处都读它 ——
        ///   守卫格在图里复用 NodeKind.Elite 作占位，判定公式散在一处就会漂移
        ///   （曾漏掉类型标签，导致守关显示成"【精英】"）。改这里等于改全局。
        /// 第 5 幕（天阙）**不算**：它最后一格是终局战（后土），走 TrialPanel.BuildFinaleBattle。
        /// </summary>
        private bool IsSeasonBossNode(int offset)
        {
            return _graph != null && _graph.Act >= 1 && _graph.Act <= 4 &&
                   _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;
        }

        /// <summary>
        /// 节点的说明文案。天气/场地事件由 WeatherComposer / FieldEvents 决定，先给占位。
        /// <paramref name="isSeasonBoss"/>：幕 1~4 的守关格（幕末最后一格）—— 说明要写首领，
        /// 不能沿用精英那句"规模 +1，旗舰携劫象"，否则玩家进战前以为只是精英战。
        /// <paramref name="isFinaleBoss"/>：第 5 幕终局战（后土）。
        /// </summary>
        private static string WeatherHint(WanXiang.Campaign.NodeKind kind, bool isSeasonBoss = false,
                                          bool isFinaleBoss = false)
        {
            // 守关优先：它与精英在图里同占位，但战斗内容完全不同（首领 + 随从，带 BossUnitMul）。
            if (isFinaleBoss) return "终局战：后土镇守中宫，敌方还有你队伍前几只的镜像";
            if (isSeasonBoss) return "守关：本幕首领亲自镇守（属性 ×1.35，另有随从）——硬仗";
            switch (kind)
            {
                case WanXiang.Campaign.NodeKind.Elite: return "精英：敌方规模 +1，旗舰携劫象";
                case WanXiang.Campaign.NodeKind.Shop: return "灵市：用灵卵换异兽 / 灵魂 / 重铸";
                case WanXiang.Campaign.NodeKind.Nest: return "孵穴：回复全队 40% 生命 或 取 2 枚灵卵";
                case WanXiang.Campaign.NodeKind.Tale: return "异闻：典籍轶事，三选一（可拒绝换灵卵）";
                case WanXiang.Campaign.NodeKind.Forge: return "铸魂台：免费融合一次（灵魂需在灵市购买）";
                case WanXiang.Campaign.NodeKind.Omen: return "天象：三选一，增益都配一条明确代价";
                case WanXiang.Campaign.NodeKind.Recruit: return "招募：免费挑选异兽入队（每属性随机 2 选，可刷新）";
                case WanXiang.Campaign.NodeKind.Relic: return "遗物：从遗物池随机 3 选 1，局内变强";
                default: return "遭遇：常规战斗，敌方按幕数规模成队";
            }
        }

        /// <summary>五行 → 中文（用于天时标题里标注属性）。</summary>
        private static string ElementCn(WanXiang.Battle.Core.Element e)
        {
            switch (e)
            {
                case WanXiang.Battle.Core.Element.Wood: return "木";
                case WanXiang.Battle.Core.Element.Fire: return "火";
                case WanXiang.Battle.Core.Element.Earth: return "土";
                case WanXiang.Battle.Core.Element.Metal: return "金";
                case WanXiang.Battle.Core.Element.Water: return "水";
                default: return "";
            }
        }

        /// <summary>
        /// 选节点时合成「本场真实天时」：节点天时 + 重放 run.Path 还原的上一幕余气，
        /// 结果存进 <see cref="WanXiang.Campaign.LiveWeather.Current"/>（非存档容器，工厂读取）。
        /// 重放用与活链路**完全相同**的 <c>BuildRoute(act, Fnv1a("route:"+RunSeed+":"+act), Layers)</c>，
        /// 保证天气/余气与玩家实际走过的图逐位一致（可背版、可复盘）。
        /// <para>可复现性红线：算不出（run 空 / offset 非法）→ 存 null，战斗退回旧行为。</para>
        /// </summary>
        private void ComposeLiveWeatherFor(int offset)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || offset < 0) { WanXiang.Campaign.LiveWeather.Current = null; return; }

            // 五幕图：与 RebuildGraphFor 同一公式重建（act≥5 走 BuildFinale 固定图）。
            var acts = new WanXiang.Campaign.ActGraph[5];
            for (int a = 1; a <= 5; a++)
                acts[a - 1] = WanXiang.Campaign.SolarTermGraph.BuildRoute(
                    a, CoreMath.Fnv1a("route:" + run.RunSeed + ":" + a), Layers);

            var rs = new WanXiang.Campaign.RunState(acts, WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm);

            // 重放历史走过的节点（run.Path 存的是各幕节点 offset）。
            // 遇到非法移动（重复/越界）跳过不崩；到达守关点则推进幕再继续重放。
            if (run.Path != null)
                foreach (var off in run.Path)
                    if (!rs.EnterNode(off) && rs.AtBoss) { rs.DefeatBoss(); rs.EnterNode(off); }

            // 预览当前选中的节点（只算天气、不写回 run.Path）：
            // 先把可能跨幕的点推进，再进入；仍失败 = 异常（如还没通过的守关后续幕节点）⇒ 落到安全兜底。
            if (!rs.EnterNode(offset) && rs.AtBoss) { rs.DefeatBoss(); rs.EnterNode(offset); }

            // ★ 合成「节点天时 + 幕间余气（rs.Lingers）+ 玩家星移余气（LiveWeather.Lingers）」。
            //   星移余气是会话级、玩家主动注入，与幕间余气叠加（都减半强度、各自计节点）。
            var nodeW = rs.CurrentOffset >= 0
                ? WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(rs.CurrentGraph.Terms[rs.CurrentOffset])
                : null;
            var ext = new System.Collections.Generic.List<WanXiang.Battle.Core.WeatherDef>();
            foreach (var l in rs.Lingers) ext.Add(l.Weather);
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers) ext.Add(l.Weather);
            WanXiang.Campaign.LiveWeather.Current = ext.Count == 0 ? nodeW
                : WanXiang.Campaign.WeatherComposer.Compose(nodeW, ext);
        }

        /// <summary>
        /// 星移（Batch 3 玩家注入余气）：把「当前节点天时」减半，作为一条余气注入
        /// <see cref="WanXiang.Campaign.LiveWeather.Lingers"/>，带入后续 <see cref="XINGYI_NODES"/> 节。
        /// 与幕间余气（rs.Lingers）同一套合成口径（WeatherComposer.Compose），叠加生效。
        /// <para>规则（用户 2026-09-27 定案）：消耗 <see cref="XINGYI_COST"/> 灵卵；**每节点限一次**
        /// （写存档：换节点/进出编队/重启都不重置）；**未出发前再点一次 = 撤销**（退还灵卵、
        /// 移除本节点注入的余气；出发扣减 NodesLeft 后锁定不可撤）。</para>
        /// </summary>
        private void OnXingyiClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _selected < 0) return;
            int key = XingyiKey(_graph != null ? _graph.Act : 1, _selected);
            bool used = run.XingyiUsed != null && run.XingyiUsed.Contains(key);

            // ---- 撤销：本节点已星移且尚未出发（NodesLeft 未被扣 = 仍 == XINGYI_NODES）----
            if (used && HasCancellableXingyi(key))
            {
                var L = WanXiang.Campaign.LiveWeather.Lingers;
                for (int i = L.Count - 1; i >= 0; i--)
                    if (L[i].OwnerKey == key) L.RemoveAt(i);
                run.XingyiUsed.Remove(key);
                // ★ 同步移除持久化镜像（跨存档也不残留）
                if (run.XingyiLingers != null)
                    for (int i = run.XingyiLingers.Count - 1; i >= 0; i--)
                        if (XingyiKey(run.XingyiLingers[i].Act, run.XingyiLingers[i].Offset) == key)
                            run.XingyiLingers.RemoveAt(i);
                run.Eggs += XINGYI_COST;
                WanXiang.Run.RunSave.SaveCurrent();
                RefreshEggsLabel();
                RefreshXingyiUI();
                Debug.Log("[Campaign] 星移已撤销：退还灵卵 " + XINGYI_COST + "，星移余气数=" + L.Count);
                return;
            }

            // ---- 注入 ----
            var w = WanXiang.Campaign.LiveWeather.Current;
            if (w == null) { Debug.Log("[Campaign] 星移：当前节点无天时可移。"); return; }
            if (used)
            {
                Debug.Log("[Campaign] 星移：本节点已用过（每节点限一次，换个节点才能再移）。");
                UpdateXingyiButton(run, key, true);
                return;
            }
            if (run.Eggs < XINGYI_COST)
            {
                Debug.Log("[Campaign] 星移：灵卵不足（需 " + XINGYI_COST + "，现有 " + run.Eggs + "）。");
                UpdateXingyiButton(run, key, true);
                return;
            }

            // ★ 星移 = 把「本节点天时」减半带走。用节点本身的节气天气（确定性、可序列化），
            //   与重进游戏后 RehydrateXingyi 重建的是同一条公式，保证跨存档一致。
            int term = (_graph != null && _graph.Terms != null && _selected >= 0 && _selected < _graph.Terms.Length)
                ? _graph.Terms[_selected] : 0;
            var baseW = WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(term);
            if (baseW == null) { Debug.Log("[Campaign] 星移：本节点节气无天气可移。"); return; }

            run.Eggs -= XINGYI_COST;
            if (run.XingyiUsed == null) run.XingyiUsed = new System.Collections.Generic.List<int>();
            run.XingyiUsed.Add(key);
            WanXiang.Campaign.LiveWeather.Lingers.Add(new WanXiang.Campaign.RunState.LingerEntry
            {
                Weather = baseW.ScaledHalf("xingyi_" + baseW.Id + "_" + key),
                NodesLeft = XINGYI_NODES,
                FromTerm = term,
                OwnerKey = key,
            });
            // ★ 持久化镜像：跨存档恢复用（重进游戏按 term 重建上面的 LingerEntry）
            if (run.XingyiLingers == null) run.XingyiLingers = new System.Collections.Generic.List<WanXiang.Run.RunState.XingyiLingerSave>();
            run.XingyiLingers.Add(new WanXiang.Run.RunState.XingyiLingerSave
            {
                Act = _graph != null ? _graph.Act : 1,
                Offset = _selected,
                NodesLeft = XINGYI_NODES,
                Term = term,
            });
            WanXiang.Run.RunSave.SaveCurrent();

            RefreshEggsLabel();
            RefreshXingyiUI();
            Debug.Log("[Campaign] 星移已注入：-" + XINGYI_COST + " 灵卵，持续 " + XINGYI_NODES +
                      " 节，星移余气数=" + WanXiang.Campaign.LiveWeather.Lingers.Count);
        }

        /// <summary>
        /// 跨存档恢复星移余气：把持久化的 RunState.XingyiLingers 重建为会话级 LiveWeather.Lingers。
        /// WeatherDef 不可 Json 序列化，故存档只存来源节气 + 剩余节点数；这里重拉天气并减半，
        /// 与注入时同一公式（baseW.ScaledHalf），保证「关游戏→重进」星移天气仍在、不白花灵卵。
        /// </summary>
        private void RehydrateXingyi()
        {
            var run = WanXiang.Run.RunSave.Current;
            WanXiang.Campaign.LiveWeather.Lingers.Clear();
            if (run == null || run.XingyiLingers == null) return;
            foreach (var s in run.XingyiLingers)
            {
                var w = WanXiang.Battle.Core.WeatherCatalog.GetSolarTerm(s.Term);
                if (w == null) continue;
                int key = XingyiKey(s.Act, s.Offset);
                WanXiang.Campaign.LiveWeather.Lingers.Add(new WanXiang.Campaign.RunState.LingerEntry
                {
                    Weather = w.ScaledHalf("xingyi_" + w.Id + "_" + key),
                    NodesLeft = s.NodesLeft,
                    FromTerm = s.Term,
                    OwnerKey = key,
                });
            }
        }

        /// <summary>把会话级 LiveWeather.Lingers（含星移余气，已随节点通过 -1）写回存档镜像。</summary>
        private void SyncXingyiSaveFromLive()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.XingyiLingers == null) run.XingyiLingers = new System.Collections.Generic.List<WanXiang.Run.RunState.XingyiLingerSave>();
            run.XingyiLingers.Clear();
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers)
            {
                int act = l.OwnerKey / 1000;
                int off = l.OwnerKey % 1000;
                run.XingyiLingers.Add(new WanXiang.Run.RunState.XingyiLingerSave
                {
                    Act = act, Offset = off, NodesLeft = l.NodesLeft, Term = l.FromTerm,
                });
            }
        }

        /// <summary>星移节点 key：act*1000+offset（跨幕不撞号）。</summary>
        private static int XingyiKey(int act, int offset) { return act * 1000 + offset; }

        /// <summary>本节点注入的星移余气还可撤销 = 存在 OwnerKey 匹配且没被出发扣过（NodesLeft 仍 == XINGYI_NODES）。</summary>
        private static bool HasCancellableXingyi(int key)
        {
            foreach (var l in WanXiang.Campaign.LiveWeather.Lingers)
                if (l.OwnerKey == key && l.NodesLeft == XINGYI_NODES) return true;
            return false;
        }

        private void SetXingyiLabel(string text)
        {
            var label = _btnXingyi != null ? _btnXingyi.transform.Find("Tmp_Label") : null;
            var t = label != null ? label.GetComponent<TMP_Text>() : null;
            if (t != null) t.text = text;
        }

        /// <summary>星移扣/退灵卵后同步顶栏「灵卵 N 枚」（拼装式与 BuildNodeMap 同口径）。</summary>
        private void RefreshEggsLabel()
        {
            if (_tmpActTitle == null || _graph == null) return;
            var run = WanXiang.Run.RunSave.Current;
            _tmpActTitle.text = "第" + CnNum(_graph.Act) + "幕 · " + _graph.SeasonCn +
                                " · 守关 " + _graph.BossName +
                                "　｜　灵卵 " + (run != null ? run.Eggs : 0) + " 枚";
        }

        /// <summary>
        /// 星移区统一刷新：合成当前天时 → 写信息卡（标题 + 效果行 + 星移摘要）→ 按钮状态。
        /// 无天时节点回落到节点类型说明（WeatherHint）；按钮只在有天时或可撤销时显示。
        /// </summary>
        private void RefreshXingyiUI()
        {
            ComposeLiveWeatherFor(_selected);
            var wLinger = WanXiang.Campaign.LiveWeather.Current;
            var run = WanXiang.Run.RunSave.Current;
            int key = XingyiKey(_graph != null ? _graph.Act : 1, _selected);

            if (wLinger != null)
            {
                string title = "天时 " + wLinger.BuffName + "（" + ElementCn(wLinger.Element) + "）";
                if (_current != null) _current.Weather = title;
                if (_tmpWeather != null)
                {
                string text = title + "\n" + wLinger.Describe();
                text += BuildXingyiLingerSummary();
                    _tmpWeather.text = text;
                }
            }
            else
            {
                // ⚠ 回落文案也要带守关标志：否则选中守关格而本场无天时节点时，
                //   又会退回"精英：规模 +1"那句（同一个坑，见 IsSeasonBossNode 注释）。
                bool fbBoss = IsSeasonBossNode(_selected);
                var fb = WeatherHint(_graph != null ? _graph.KindOf(_selected) : WanXiang.Campaign.NodeKind.Encounter,
                                     fbBoss, false);
                if (_current != null) _current.Weather = fb;
                if (_tmpWeather != null) _tmpWeather.text = fb;
            }

            UpdateXingyiButton(run, key, wLinger != null);
        }

        /// <summary>星移余气明细：列出每条仍在生效的星移注入（来源天时名 + 剩余节点数），
        /// 让玩家一眼看出「哪些是星移带来的、还剩几节」。无则空串。</summary>
        private string BuildXingyiLingerSummary()
        {
            var lingers = WanXiang.Campaign.LiveWeather.Lingers;
            if (lingers == null || lingers.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("\n· 星移余气 ").Append(lingers.Count).Append(" 条（减半·耗 ")
              .Append(XINGYI_COST).Append(" 灵卵·未出发可撤销）");
            foreach (var l in lingers)
            {
                string nm = (l.Weather != null && !string.IsNullOrEmpty(l.Weather.BuffName)) ? l.Weather.BuffName : "？";
                sb.Append("\n   - ").Append(nm).Append(" 剩 ").Append(l.NodesLeft).Append(" 节");
            }
            return sb.ToString();
        }

        private void UpdateXingyiButton(WanXiang.Run.RunState run, int key, bool hasWeather)
        {
            if (_btnXingyi == null) return;
            bool used = run != null && run.XingyiUsed != null && run.XingyiUsed.Contains(key);
            bool canCancel = used && HasCancellableXingyi(key);
            bool afford = run != null && run.Eggs >= XINGYI_COST;
            _btnXingyi.gameObject.SetActive(hasWeather || canCancel);
            SetXingyiLabel(canCancel ? "撤销星移" : "星移");
            _btnXingyi.interactable = canCancel || (hasWeather && !used && afford);
        }

        /// <summary>
        /// 孵穴二选一（GDD §4.3）：回复全队 40% 生命 ／ 取 2 枚灵卵。
        /// 「回复」在每场满血开局的架构下的表现 = 下一场战斗我方全体 ×1.4（用后清零）；
        /// 等 HP 跨场持续化后再改回真·回复。
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid NestChoose(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            // ★ 天气地图效果层：孵穴受当前节点天时影响（LiveWeather.Current 为 null ⇒ 中性）。
            var wx = WanXiang.Campaign.WeatherMapEffects.Current;
            int healPct = System.Math.Max(0, 40 + wx.HealBonusPct);
            int eggGain = System.Math.Max(0, (int)System.Math.Round(2f * wx.EggRewardMul));
            int pick = await Dialog.Choose("孵穴",
                "二选一（受当前天时影响）：\nA. 下一场战斗全队状态回复，能力 ×1.4（回复 " + healPct + "%）\nB. 取 " + eggGain + " 枚灵卵",
                "状态回复", "取 " + eggGain + " 灵卵");
            if (pick == 0) run.HealPending = healPct;
            else run.Eggs += eggGain;

            // 二选一完成 ⇒ 这时才把节点记为通过（未完成前不推进）
            // ★ 同上：孵穴也只记"已处理"，推进交给 OnOpenAsync 的落地（PendingCommit）。
            if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                run.VisitedNodes.Add(_selected);
            WanXiang.Run.RunSave.SaveCurrent();
            await Dialog.Tip("孵穴", pick == 0
                ? "全队状态回复！下一场战斗能力 ×1.4（回复 " + healPct + "%）"
                : "获得 " + eggGain + " 枚灵卵（当前 " + run.Eggs + "）");

            // ★ 处理完回节点地图继续探索（之前什么都不做 ⇒ 玩家以为被踢回主城界面）
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null)
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);   // 等弹窗淡出结束
                    await ui.OpenAsync<CampaignPanel>();
                });
        }

        private static string CnNum(int n)
        {
            switch (n)
            {
                case 1: return "一";
                case 2: return "二";
                case 3: return "三";
                case 4: return "四";
                case 5: return "五";
                default: return n.ToString();
            }
        }

        /// <summary>24 节气名（下标 1..24，来自 GDD 3.3 的节气序列）。</summary>
        private static string TermName(int term)
        {
            string[] names =
            {
                "", "立春", "雨水", "惊蛰", "春分", "清明", "谷雨",
                "立夏", "小满", "芒种", "夏至", "小暑", "大暑",
                "立秋", "处暑", "白露", "秋分", "寒露", "霜降",
                "立冬", "小雪", "大雪", "冬至", "小寒", "大寒",
            };
            return term >= 1 && term < names.Length ? names[term] : ("第 " + term + " 节");
        }

        private void FillTeamPreview()
        {
            if (_imgAvatars == null) return;
            var all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
            for (int i = 0; i < _imgAvatars.Length; i++)
            {
                if (_imgAvatars[i] == null) continue;
                var sprite = (all != null && i < all.Length && _sprites != null)
                    ? _sprites.GetHead(all[i].Id) : null;
                if (sprite != null)
                {
                    _imgAvatars[i].sprite = sprite;
                    _imgAvatars[i].color = Color.white;
                    _imgAvatars[i].preserveAspect = true;
                }
            }
        }

        private void OnNextClicked()
        {
            if (_selected < 0 || _graph == null)
            {
                Debug.Log("[Campaign] 先点一个可前往的节点。");
                return;
            }
            if (!IsReachable(_selected))
            {
                Debug.Log("[Campaign] 这个节点不在可达层（只能走相邻下一层）。");
                return;
            }

            var kind = _graph.KindOf(_selected);

            // 进度落盘：走到哪一节记在存档里，下次打开还在这一节
            var run = WanXiang.Run.RunSave.Current;
            if (run != null)
            {
                run.Act = _graph.Act;

                // ★★ 「访问过」必须在**进入节点时**就记，不能跟着"通过"一起延后！
                //    节点图的可达性依赖 VisitedNodes——之前把这段一起延后了，导致它永远是空的，
                //    节点图认为"哪儿都没去过" ⇒ 只有第 0 层可达 ⇒ **点不动任何节点**（用户实测）。
                //    「访问」与「通过」是两件事：进入即访问，胜利/处理完才算通过。
                if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                    run.VisitedNodes.Add(_selected);
                WanXiang.Run.RunSave.SaveCurrent();
                // ★ 事件类节点（灵市/孵穴/铸魂台/异闻/天象）**延后推进**：
                //   点进去就写 NodeOffset 的话，玩家还没处理完就关游戏，再进来节点已通过 ⇒ 奖励丢失。
                //   改为"离开事件面板、回到节点图时"才落地（见 OnOpenAsync 的 PendingCommit）。
                //   战斗类保持立即推进（打完/撤退都会回到节点图，语义一致）。
                // ★ **所有**节点都延后推进：点节点只是"进入"，真正通过要等
                //   ① 事件类处理完回地图（OnOpenAsync 落地）
                //   ② 战斗类点了「出征」（FormationPanel 写入）
                //   —— 否则"进编阵看一眼再返回"会被算作通过（用户实测：白嫖节点）。
                PendingCommit = _selected;

                // ★★ NodeOffset 只表示"已经通过到哪一节"（可达性锚点 + 进度），
                //    这里**不能**写它 —— 否则"点开战斗节点、再点返回地图"也被算作通过
                //    （用户实测："这个战斗节点没有打，点击返回地图却通过了"）。
                //    真正推进在通过时落地：见 OnOpenAsync 里对 PendingCommit 的处理。
                if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                    run.VisitedNodes.Add(_selected);
                if (run.Path != null) run.Path.Add(_selected);
                // ★ 注意：星移余气（Lingers）的「-1」不再在此处扣减！
                //   出征只是「进入节点」，战斗/事件尚未结算；若此刻就扣，玩家进了编队又返回
                //   会被当成「已通过」，导致星移无法撤销（用户实测）。余气扣减统一推迟到
                //   节点真正通过时（见 OnOpenAsync 里对 PendingCommit 的落地）。
                WanXiang.Run.RunSave.SaveCurrent();
            }

            // ---- 问号节点：走上去这一刻揭晓（v1.2 核心）----
            // 揭晓池（用户定案 2026-09-26）：纳入 普通遭遇 + 精英 —— ？也要能开出战斗。
            //   休整类权重高（惊喜为主）、战斗类权重低（惩罚为辅）：精英只占 1/12≈8%，
            //   避免 ？变成「常遇精英战」。揭晓结果写存档（QuestionRevealed），
            //   否则玩家靠「退出重进」无限重抽（这类机制的头号坑）。
            if (kind == WanXiang.Campaign.NodeKind.Question)
            {
                var weighted = new System.Collections.Generic.List<WanXiang.Campaign.NodeKind>();
                System.Action<WanXiang.Campaign.NodeKind, int> addW = (k, w) =>
                    { for (int i = 0; i < w; i++) weighted.Add(k); };
                addW(WanXiang.Campaign.NodeKind.Shop, 2);
                addW(WanXiang.Campaign.NodeKind.Nest, 2);
                addW(WanXiang.Campaign.NodeKind.Tale, 2);
                addW(WanXiang.Campaign.NodeKind.Omen, 2);
                addW(WanXiang.Campaign.NodeKind.Forge, 1);
                addW(WanXiang.Campaign.NodeKind.Encounter, 2);
                addW(WanXiang.Campaign.NodeKind.Elite, 1);
                var rng = new WanXiang.Battle.Core.DeterministicRandom(
                    CoreMath.Fnv1a("reveal:" + (run != null ? run.RunSeed : 0) + ":" + _selected));
                var revealed = weighted[rng.NextInt(0, weighted.Count)];
                if (run != null)
                {
                    if (run.QuestionRevealed == null) run.QuestionRevealed = new System.Collections.Generic.List<string>();
                    run.QuestionRevealed.Add(_selected + ":" + (int)revealed);
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                kind = revealed;
                // ★ 揭晓成战斗类（遭遇/精英）时，必须把真实 kind 写回 _current，
                //   否则 FormationPanel 仍按 Question 建敌 ⇒ 精英缩放失效、战斗甚至不触发。
                //   （图里的原始 kind 仍是 Question，揭晓结果只存在 QuestionRevealed 存档里，
                //     所以这里必须显式用揭晓后的值覆盖。）
                _current.Kind = kind;
                // ★ 不再弹揭晓弹窗（用户要求）：这个 Dialog 在实战里关不掉，索性不弹。
                //   揭晓结果通过节点图本身呈现——该节点会按 RevealedKind 显示成真实类型（见 RefreshNodes）。
                Debug.Log("[Campaign] ？节点揭晓 → " + WanXiang.Campaign.NodeKinds.Cn(revealed));
            }

            // 先关节点地图：它与接下来要开的面板同在 Normal 层
            CloseSelf();

            // 非战斗节点直接路由到已有面板 —— 面板本身承担该节点的玩法
            switch (kind)
            {
                case WanXiang.Campaign.NodeKind.Shop:
                    OpenPanelAsync<MarketPanel>().Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Tale:
                    OpenPanelAsync<TalePanel>().Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Forge:
                    OpenPanelAsync<ForgePanel>().Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Omen:
                    OpenPanelAsync<OmenPanel>().Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Recruit:
                    OpenPanelAsync<RecruitPanel>(_current).Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Relic:
                    OpenPanelAsync<RelicPanel>(_current).Forget();
                    return;
                case WanXiang.Campaign.NodeKind.Nest:
                    NestChoose(run).Forget();
                    return;
            }

            // 战斗节点 → 布阵
            OpenPanelAsync<FormationPanel>(_current).Forget();
        }

    }
}
