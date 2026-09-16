// ============================================================================
//  Panel_Codex —— 图鉴（印章墙网格 + 详情页两态）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Codex", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached)]
    public sealed class CodexPanel : UIPanelBase
    {
        [BindArray("Tab_{0}", 6)]
        [SerializeField] private Button[] _tabBtns;            // 五行 + 全部
        [SerializeField] private TMP_Text _tmpRate;            // Tmp_CollectRate 收集度
        [SerializeField] private ScrollRect _scrollGrid;       // Scroll_Grid
        [SerializeField] private RectTransform _sealTemplate;  // Item_Seal（模板，默认隐藏）
        [SerializeField] private GameObject _rootDetail;       // Root_Detail 详情页（默认隐藏）
        [SerializeField] private Image _imgBig;                // Img_BeastBig 立绘大图
        [SerializeField] private TMP_Text _tmpName;            // Tmp_Name
        [SerializeField] private TMP_Text _tmpSource;          // Tmp_ClassicSource
        [SerializeField] private TMP_Text _tmpQuote;           // Tmp_ClassicQuote
        [SerializeField] private TMP_Text _tmpTrait;           // Tmp_Trait
        [SerializeField] private TMP_Text _tmpSchool;          // Tmp_School
        [SerializeField] private Button _btnCloseDetail;       // Btn_CloseDetail

        protected override void OnCreate()
        {
            if (_rootDetail != null) _rootDetail.SetActive(false);
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                var idx = i;
                if (_tabBtns[i] != null) _tabBtns[i].onClick.AddListener(() => OnTabClicked(idx));
            }
            if (_btnCloseDetail != null) _btnCloseDetail.onClick.AddListener(OnCloseDetailClicked);
        }

        private void OnTabClicked(int index)
        {
            // TODO(交互): 过滤网格
        }

        private void OnCloseDetailClicked()
        {
            // TODO(交互): 返回网格页
        }
    }
}
