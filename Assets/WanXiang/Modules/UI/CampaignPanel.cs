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
        [SerializeField] private TMP_Text _tmpActTitle;        // Tmp_ActTitle  幕名
        [SerializeField] private TMP_Text _tmpJie;             // Tmp_JieCount  劫数
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
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
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
                // 先失活再 Destroy：Destroy 要到帧末才真正销毁，
                // 同一帧里"旧节点 + 新节点"会同时存在（重建列表时会被看到）。
                var old = content.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            _nodeItems.Clear();

            _acts = WanXiang.Campaign.SolarTermGraph.BuildDefault();
            var run = WanXiang.Run.RunSave.Current;
            int act = Mathf.Clamp(run != null ? run.Act : 1, 1, _acts.Length);
            _graph = _acts[act - 1];
            _currentOffset = run != null ? run.NodeOffset : -1;

            if (_tmpActTitle != null)
                _tmpActTitle.text = "第" + CnNum(_graph.Act) + "幕 · " + _graph.SeasonCn + " · 守关 " + _graph.BossName;
            if (_tmpJie != null)
                _tmpJie.text = run != null ? run.RealmText : "第一境 · 第一劫";

            if (_graph.IsEmpty) return;

            for (int layer = 0; layer < _graph.Layers.Length; layer++)
            {
                var row = _graph.Layers[layer];
                for (int k = 0; k < row.Length; k++)
                    SpawnNodeItem(content, row[k], layer);
            }
        }

        private void SpawnNodeItem(RectTransform content, int offset, int layer)
        {
            var item = Instantiate(_nodeItemTemplate, content);
            item.name = "Item_Node_" + offset;
            item.gameObject.SetActive(true);

            var kind = _graph.KindOf(offset);
            bool isHere = offset == _currentOffset;
            bool canGo = IsReachable(offset);
            bool passed = _currentOffset >= 0 &&
                          _graph.LayerOf(offset) <= _graph.LayerOf(_currentOffset);

            var label = item.Find("Tmp_NodeText") != null
                ? item.Find("Tmp_NodeText").GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                label.text = "第 " + (offset + 1) + " 节 · " + TermName(_graph.Terms[offset]) +
                             "　【" + WanXiang.Campaign.NodeKinds.Cn(kind) + "】" +
                             (isHere ? "　◀ 当前" : canGo ? "　← 可前往" : passed ? "　已过" : "");
                label.color = isHere ? NodeVisited : canGo ? NodeReachable : NodeLocked;
            }

            var dot = item.Find("Img_Dot") != null ? item.Find("Img_Dot").GetComponent<Image>() : null;
            if (dot != null)
                dot.color = isHere ? NodeVisited : canGo ? NodeReachable : NodeLocked;

            var btn = item.GetComponent<Button>();
            if (btn == null) btn = item.gameObject.AddComponent<Button>();
            btn.interactable = canGo || isHere;
            var captured = offset;
            btn.onClick.AddListener(() => SelectNode(captured, silent: false));

            if (isHere) item.SetAsFirstSibling();
            _nodeItems.Add(item);
        }

        /// <summary>可达 = 起点层，或"从当前节点走一步"（ActGraph.CanMove 的规则）。</summary>
        private bool IsReachable(int offset)
        {
            if (_graph == null) return false;
            if (_currentOffset < 0) return _graph.LayerOf(offset) == 0;
            return _graph.CanMove(_currentOffset, offset);
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
                run.NodeOffset = _selected;
                WanXiang.Run.RunSave.SaveCurrent();
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
                    // TODO(玩法): 孵穴是"回复 40% 生命 / 取 2 枚灵卵"二选一，
                    //   还缺一个二选一弹窗（可复用 Panel_Trial 的卡片形态）。
                    //   先按"取 2 枚灵卵"结算，别把玩家卡在节点上。
                    if (run != null) { run.Eggs += 2; WanXiang.Run.RunSave.SaveCurrent(); }
                    Debug.Log("[Campaign] 孵穴：暂按「取 2 枚灵卵」结算（二选一弹窗待做）。");
                    return;
            }

            // 战斗节点 → 布阵
            OpenPanelAsync<FormationPanel>(_current).Forget();
        }

    }
}
