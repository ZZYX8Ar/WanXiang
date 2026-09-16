// ============================================================================
//  Panel_Omen —— 天象抉择弹窗（收益鎏金 / 代价朱红，两栏等宽）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Omen", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class OmenPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpGain;            // Tmp_GainText  收益（鎏金栏）
        [SerializeField] private TMP_Text _tmpCost;            // Tmp_CostText  代价（朱红栏）
        [SerializeField] private Button _btnAccept;            // Btn_Accept
        [SerializeField] private Button _btnDecline;           // Btn_Decline

        public override bool AllowBackClose => false;

        protected override void OnCreate()
        {
            if (_btnAccept != null) _btnAccept.onClick.AddListener(OnAcceptClicked);
            if (_btnDecline != null) _btnDecline.onClick.AddListener(OnDeclineClicked);
        }

        private void OnAcceptClicked()
        {
            // TODO(交互): 写入 RunState 并关闭
        }

        private void OnDeclineClicked()
        {
            // TODO(交互): 放弃并关闭
        }
    }
}
