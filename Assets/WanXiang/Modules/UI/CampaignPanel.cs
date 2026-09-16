// ============================================================================
//  Panel_Campaign —— 节点地图（四幕节气）
//  结构验证版：不接 SolarTermGraph 的 24 节点拓扑，先固定一个"当前节点"，
//  Btn_Next 进入布阵；等 RunState 接进来后把节点按钮按拓扑生成即可。
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
    /// <summary>从一个节点进入布阵时携带的上下文。</summary>
    public sealed class NodeRequest
    {
        public string Title = "小暑 · 温风至";
        public string Weather = "本场精英位：每回合全场 3% 灼烧";
        public ulong Seed = 20260914UL;
        public int NodeIndex = 7;
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
            if (_tmpActTitle != null) _tmpActTitle.text = "第二幕 · 夏 · 朱明";
            if (_tmpJie != null) _tmpJie.text = "第 1 境 · 第 1 劫";

            var node = payload as NodeRequest ?? _current;
            if (_tmpNodeName != null) _tmpNodeName.text = node.Title;
            if (_tmpNodeType != null) _tmpNodeType.text = "精英 · 敌方规模 +1";
            if (_tmpWeather != null) _tmpWeather.text = node.Weather;

            FillTeamPreview();
            return UniTask.CompletedTask;
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
            // 进布阵前先关掉节点地图：两个都是 Normal 层，留着会盖住下面的战斗界面
            CloseSelf();
            OpenPanelAsync<FormationPanel>(_current).Forget();
        }
    }
}
