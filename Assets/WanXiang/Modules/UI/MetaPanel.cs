// ============================================================================
//  Panel_Meta —— 局外成长（境 · 五条轨道）
//  ============================================================================

using TMPro;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Meta", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class MetaPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpRealm;           // Tmp_RealmText 当前境·劫
        [BindArray("Tmp_TrackName_{0}", 5)]
        [SerializeField] private TMP_Text[] _tmpTrackNames;    // 五条轨道名
        [BindArray("Track_{0}", 5)]
        [SerializeField] private RectTransform[] _trackNodes;  // 五条轨道容器（节点圆点由代码生成）
        [SerializeField] private RectTransform _rewardItemTemplate;  // Item_Reward（模板，默认隐藏）
        [SerializeField] private TMP_Text _tmpCap;             // Tmp_MetaCap 数值类增益封顶 +8%

        protected override void OnCreate()
        {
            // TODO(交互): 领取奖励由代码生成的 Reward 按钮触发
        }
    }
}
