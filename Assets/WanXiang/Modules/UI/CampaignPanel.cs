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

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        private readonly NodeRequest _current = new NodeRequest();

        protected override void OnCreate()
        {
            if (_rootNodeInfo != null) _rootNodeInfo.SetActive(true);
            if (_btnNext != null) _btnNext.onClick.AddListener(OnNextClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(OnBackToHome);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ★ 待推进落地：从事件面板（灵市/孵穴/铸魂台/异闻/天象）返回节点图时，
            //   把之前未完成的节点记为通过 —— 玩家没处理完就关游戏的话，_pendingCommit 是内存变量、
            //   不会持久化，节点也不会推进 ⇒ 下次进来还能重做（奖励不丢）。
            if (PendingCommit >= 0)
            {
                var prun = WanXiang.Run.RunSave.Current;
                if (prun != null)
                {
                    prun.NodeOffset = PendingCommit;
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

            // ⚠ 临时：把布局数值并进这条"图诊断"普通日志（调试完删除这段拼接）
            {
                var vpX = (_scrollNodes != null && _scrollNodes.viewport != null) ? _scrollNodes.viewport.rect : new Rect();
                string pos0 = "?";
                if (_scrollNodes != null && _scrollNodes.content != null)
                    for (int i = 0; i < _scrollNodes.content.childCount; i++)
                    {
                        var c = _scrollNodes.content.GetChild(i) as RectTransform;
                        if (c != null && c.name.StartsWith("Node_")) { pos0 = c.anchoredPosition.ToString(); break; }
                    }
                Debug.Log("[Campaign] 布局调试：lc=" +
                    ((_graph != null && _graph.Layers != null) ? _graph.Layers.Length : -1) +
                    " 图Layers=" + ((_graph != null && _graph.Layers != null) ? _graph.Layers.Length : -1) +
                    "｜viewport=" + vpX.width.ToString("0") + "x" + vpX.height.ToString("0") +
                    "｜content=" + (_scrollNodes != null && _scrollNodes.content != null
                        ? (_scrollNodes.content.sizeDelta.x.ToString("0") + "x" + _scrollNodes.content.sizeDelta.y.ToString("0")) : "?") +
                    "｜子数=" + (_scrollNodes != null && _scrollNodes.content != null ? _scrollNodes.content.childCount : -1) +
                    "｜首节点pos=" + pos0 +
                    "｜NodeH=" + NodeH + " GapY=" + GapY);
            }

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
        private const int Layers = 12;
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

            // ⚠⚠ 调试（问题解决后删除）：进入本方法就打印，确认代码路径一定执行
            Debug.Log("[Campaign][排版调试A] 进入 BuildNodeMap｜_graph=" +
                (_graph != null ? ("Act" + _graph.Act + " Layers" + (_graph.Layers != null ? _graph.Layers.Length : -1) +
                 " NodeCount" + _graph.NodeCount) : "null") +
                "｜_scrollNodes=" + (_scrollNodes != null ? "✓" : "null") +
                " content=" + (content != null ? "✓" : "null") +
                " viewport=" + (_scrollNodes != null && _scrollNodes.viewport != null ? "✓" : "null"),
                this);

            // ★★ 首帧强制刷新 Canvas：第一次打开本面板时，viewport/content 的 rect 还没被
            //    布局系统算出来，此时用它们的尺寸排版会错位（用户实测："第一次进不行、
            //    返回再进来就成功了"）。这里先强制算一次。
            UnityEngine.Canvas.ForceUpdateCanvases();

            // ★ 本图的实际层数（天阙图只有 5 层，普通幕 12 层）—— 摆位/高度/自动定位都用它
            int lc = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;

            // ---- 数据源：v1.2 路线图（12 层、层内 2~3、种子稳定）----
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

            // ⚠⚠ 调试（问题解决后删除）：排版定稿后的数值
            {
                var vpD = (_scrollNodes != null && _scrollNodes.viewport != null) ? _scrollNodes.viewport.rect : new Rect();
                int lcD = (_graph != null && _graph.Layers != null) ? _graph.Layers.Length : Layers;
                string p0 = "?";
                for (int i = 0; i < content.childCount; i++)
                {
                    var c = content.GetChild(i) as RectTransform;
                    if (c != null && c.name.StartsWith("Node_")) { p0 = c.anchoredPosition.ToString(); break; }
                }
                Debug.Log("[Campaign][排版调试B] totalH=" + (lcD * (NodeH + GapY) + 80f).ToString("0") + " lc=" + lcD +
                    " viewport=" + vpD.width.ToString("0") + "x" + vpD.height.ToString("0") +
                    " content=" + content.sizeDelta.x.ToString("0") + "x" + content.sizeDelta.y.ToString("0") +
                    " 子数=" + content.childCount + " totalH=" + (lcD * (NodeH + GapY) + 80f).ToString("0") +
                    " 首节点pos=" + p0, this);
            }

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
                string name;
                if (kind == WanXiang.Campaign.NodeKind.Question && !IsRevealed(offset))
                    name = "？ 未知";
                else if (kind == WanXiang.Campaign.NodeKind.Question)
                    name = WanXiang.Campaign.NodeKinds.Cn(RevealedKind(offset));   // 揭晓后显示真实类型
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
            // ★ 天阙最后一格 = 终局战（后土）：类型显示为"守关·后土"，而不是占位用的【精英】
            bool isFinaleBoss = _graph != null && _graph.Act >= 5 && offset == _graph.NodeCount - 1;
            if (isFinaleBoss && _tmpNodeName != null)
                _tmpNodeName.text = "终局 · " + (_graph.BossName ?? "后土");
            if (_tmpNodeType != null && !isFinaleBoss)
                _tmpNodeType.text = WanXiang.Campaign.NodeKinds.Cn(kind) +
                                    (WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "　（战斗）" : "　（休整）");
            if (_tmpWeather != null) _tmpWeather.text = WeatherHint(kind);

            // _current 是常驻实例（表单里被 Btn_Next 复用），逐字段赋值而不是换新对象
            _current.Title = term;
            _current.Weather = WeatherHint(kind);
            _current.Seed = (ulong)(_graph.Act * 1000 + offset + 7);
            _current.NodeIndex = offset;
            _current.Act = _graph.Act;
            _current.Offset = offset;
            _current.Kind = kind;

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

        /// <summary>节点的说明文案。天气/场地事件由 WeatherComposer / FieldEvents 决定，先给占位。</summary>
        private static string WeatherHint(WanXiang.Campaign.NodeKind kind)
        {
            switch (kind)
            {
                case WanXiang.Campaign.NodeKind.Elite: return "精英：敌方规模 +1，旗舰携劫象";
                case WanXiang.Campaign.NodeKind.Shop: return "灵市：用灵卵换异兽 / 灵魂 / 重铸";
                case WanXiang.Campaign.NodeKind.Nest: return "孵穴：回复全队 40% 生命 或 取 2 枚灵卵";
                case WanXiang.Campaign.NodeKind.Tale: return "异闻：典籍轶事，三选一（可拒绝换灵卵）";
                case WanXiang.Campaign.NodeKind.Forge: return "铸魂台：免费融合一次，另赠 1 个随机灵魂";
                case WanXiang.Campaign.NodeKind.Omen: return "天象：三选一，增益都配一条明确代价";
                default: return "遭遇：常规战斗，敌方按幕数规模成队";
            }
        }

        /// <summary>
        /// 孵穴二选一（GDD §4.3）：回复全队 40% 生命 ／ 取 2 枚灵卵。
        /// 「回复」在每场满血开局的架构下的表现 = 下一场战斗我方全体 ×1.4（用后清零）；
        /// 等 HP 跨场持续化后再改回真·回复。
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid NestChoose(WanXiang.Run.RunState run)
        {
            if (run == null) return;
            int pick = await Dialog.Choose("孵穴",
                "二选一：\nA. 下一场战斗全队状态回复，能力 ×1.4\nB. 取 2 枚灵卵",
                "状态回复", "取 2 灵卵");
            if (pick == 0) run.HealPending = 40;
            else run.Eggs += 2;

            // 二选一完成 ⇒ 这时才把节点记为通过（未完成前不推进）
            // ★ 同上：孵穴也只记"已处理"，推进交给 OnOpenAsync 的落地（PendingCommit）。
            if (run.VisitedNodes != null && !run.VisitedNodes.Contains(_selected))
                run.VisitedNodes.Add(_selected);
            WanXiang.Run.RunSave.SaveCurrent();
            await Dialog.Tip("孵穴", pick == 0
                ? "全队状态回复！下一场战斗能力 ×1.4"
                : "获得 2 枚灵卵（当前 " + run.Eggs + "）");

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
                WanXiang.Run.RunSave.SaveCurrent();
            }

            // ---- 问号节点：走上去这一刻揭晓（v1.2 核心）----
            // 揭晓池 = 休整类五种（绝不揭晓成精英，保底见《节点地图设计 v1.2》第 4 节）
            if (kind == WanXiang.Campaign.NodeKind.Question)
            {
                var pool = new[]
                {
                    WanXiang.Campaign.NodeKind.Shop, WanXiang.Campaign.NodeKind.Nest,
                    WanXiang.Campaign.NodeKind.Tale, WanXiang.Campaign.NodeKind.Forge,
                    WanXiang.Campaign.NodeKind.Omen,
                };
                var rng = new WanXiang.Battle.Core.DeterministicRandom(
                    CoreMath.Fnv1a("reveal:" + (run != null ? run.RunSeed : 0) + ":" + _selected));
                var revealed = pool[rng.NextInt(0, pool.Length)];
                if (run != null)
                {
                    if (run.QuestionRevealed == null) run.QuestionRevealed = new System.Collections.Generic.List<string>();
                    run.QuestionRevealed.Add(_selected + ":" + (int)revealed);
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                kind = revealed;
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
                case WanXiang.Campaign.NodeKind.Nest:
                    NestChoose(run).Forget();
                    return;
            }

            // 战斗节点 → 布阵
            OpenPanelAsync<FormationPanel>(_current).Forget();
        }

    }
}
