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
        private int _pendingCommit = -1;

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
            if (_pendingCommit >= 0)
            {
                var prun = WanXiang.Run.RunSave.Current;
                if (prun != null)
                {
                    prun.NodeOffset = _pendingCommit;
                    if (prun.VisitedNodes != null && !prun.VisitedNodes.Contains(_pendingCommit))
                        prun.VisitedNodes.Add(_pendingCommit);
                    WanXiang.Run.RunSave.SaveCurrent();
                }
                _pendingCommit = -1;
            }

            BuildNodeMap();

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

            // ---- 数据源：v1.2 路线图（12 层、层内 2~3、种子稳定）----
            var run = WanXiang.Run.RunSave.Current;
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

            // ★★ 幕推进：用 **格号** 判定（NodeOffset 范围 0..NodeCount-1，每层 3 格 × Layers 层）。
            //    之前误用 Layers-1(=11) 当阈值 ⇒ 走到第 4 层左右就误判"幕末"提前跳幕
            //    （用户实测"明明第二幕却直接到第三幕"）。必须在建图之后判、判完重建新幕的图。
            if (run != null && run.Act < 5 && _graph.NodeCount > 0
                && run.NodeOffset >= _graph.NodeCount - 1)
            {
                run.Act++;
                run.NodeOffset = -1;
                if (run.VisitedNodes != null) run.VisitedNodes.Clear();   // 新幕重新探索
                WanXiang.Run.RunSave.SaveCurrent();
                Debug.Log("[Campaign] 幕推进 ⇒ 第 " + run.Act + " 幕");

                act = Mathf.Clamp(run.Act, 1, 5);
                seed = CoreMath.Fnv1a("route:" + run.RunSeed + ":" + act);
                _graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(act, seed, Layers);
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
                float y = -(Layers - 1 - layer) * (NodeH + GapY);   // 层号越大越靠上
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

            float totalH = Layers * (NodeH + GapY) + 80f;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(totalH, 600f));

            // 打开时滚到当前层（12 层比一屏高，别让玩家自己找）
            float curY = (_currentOffset >= 0)
                ? -(Layers - 1 - _graph.LayerOf(_currentOffset)) * (NodeH + GapY)
                : -(Layers - 1) * (NodeH + GapY);
            // ⚠ content.sizeDelta 刚改过，布局要等下一次 Canvas 更新才算完 ——
            //   不 ForceUpdate 的话这行设置会被后续布局覆盖，玩家只能自己往上滑（用户实测）。
            UnityEngine.Canvas.ForceUpdateCanvases();
            _scrollNodes.verticalNormalizedPosition = Mathf.Clamp01(1f - (Mathf.Abs(curY) - 200f) / Mathf.Max(1f, totalH));
        }

        /// <summary>关掉 content 上的自动布局组件（手动排布的前提）。</summary>
        private static void DisableAutoLayout(RectTransform content)
        {
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
            float y = -(Layers - 1 - layer) * (NodeH + GapY) - NodeH * 0.5f;
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
        private void SelectNode(int offset, bool silent)
        {
            if (_graph == null)
            {
                if (!silent) Debug.LogWarning("[Campaign] 节点图还没建好。");
                return;
            }
            if (offset < 0 || offset >= _graph.NodeCount)
            {
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
            if (_tmpNodeType != null)
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
            run.NodeOffset = _selected;
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
                // ★ 事件类节点（灵市/孵穴/铸魂台/异闻/天象）**延后推进**：
                //   点进去就写 NodeOffset 的话，玩家还没处理完就关游戏，再进来节点已通过 ⇒ 奖励丢失。
                //   改为"离开事件面板、回到节点图时"才落地（见 OnOpenAsync 的 _pendingCommit）。
                //   战斗类保持立即推进（打完/撤退都会回到节点图，语义一致）。
                // ★ **所有**节点都延后推进：点节点只是"进入"，真正通过要等
                //   ① 事件类处理完回地图（OnOpenAsync 落地）
                //   ② 战斗类点了「出征」（FormationPanel 写入）
                //   —— 否则"进编阵看一眼再返回"会被算作通过（用户实测：白嫖节点）。
                _pendingCommit = _selected;
                PendingCommit = _selected;
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
                // 揭晓不再弹窗（该 Dialog 在实战里关不掉）。揭晓结果由节点图本身呈现：
                // 该节点会按 RevealedKind(offset) 显示成真实类型，玩家在图上直接看到。
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
