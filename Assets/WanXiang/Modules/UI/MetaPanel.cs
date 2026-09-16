// ============================================================================
//  Panel_Meta —— 局外成长（境 · 五条轨道）
//  ============================================================================

using TMPro;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Meta", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached)]
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
