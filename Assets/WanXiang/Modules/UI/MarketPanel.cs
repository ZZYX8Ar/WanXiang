// ============================================================================
//  Panel_Market —— 灵市（商店）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Market", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class MarketPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpEggs;            // Tmp_Eggs        灵卵余额
        [SerializeField] private Button _btnRefresh;           // Btn_Refresh
        [SerializeField] private TMP_Text _tmpRefreshCost;     // Tmp_RefreshCost
        [BindArray("Goods_{0}", 6)]
        [SerializeField] private Button[] _goodsBtns;          // 6 张商品卡
        [BindArray("Img_GoodsHead_{0}", 6)]
        [SerializeField] private Image[] _imgGoodsHead;        // 商品头像
        [BindArray("Tmp_GoodsPrice_{0}", 6)]
        [SerializeField] private TMP_Text[] _tmpGoodsPrice;    // 价格
        [BindArray("Img_GoodsSold_{0}", 6)]
        [SerializeField] private Image[] _imgGoodsSold;        // 售罄印章
        [SerializeField] private TMP_Text _tmpHint;            // Tmp_Hint

        protected override void OnCreate()
        {
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(OnRefreshClicked);
            for (int i = 0; i < _goodsBtns.Length; i++)
            {
                var idx = i;
                if (_goodsBtns[i] != null) _goodsBtns[i].onClick.AddListener(() => OnGoodsClicked(idx));
            }
        }

        private void OnRefreshClicked()
        {
            // TODO(交互): 扣刷新费，重摇货架
        }

        private void OnGoodsClicked(int index)
        {
            // TODO(交互): 灵卵够 → 扣款入库并盖售罄印；不够 → Toast
        }
    }
}
