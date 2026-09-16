// ============================================================================
//  Panel_Tale —— 异闻事件弹窗（选择不允许误触跳过）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Tale", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class TalePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpQuote;           // Tmp_ClassicQuote 典籍引文
        [SerializeField] private TMP_Text _tmpStory;           // Tmp_StoryText    白话叙述
        [BindArray("Opt_{0}", 3)]
        [SerializeField] private Button[] _optBtns;            // 三个选项
        [BindArray("Tmp_OptionText_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpOpts;          // 选项文本
        [BindArray("Tmp_Cost_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpCosts;         // 选项代价
        [SerializeField] private Button _btnDecline;           // Btn_Decline 拒绝

        public override bool AllowBackClose => false;

        protected override void OnCreate()
        {
            for (int i = 0; i < _optBtns.Length; i++)
            {
                var idx = i;
                if (_optBtns[i] != null) _optBtns[i].onClick.AddListener(() => OnOptionClicked(idx));
            }
            if (_btnDecline != null) _btnDecline.onClick.AddListener(OnDeclineClicked);
        }

        private void OnOptionClicked(int index)
        {
            // TODO(交互): 执行效果 → 关闭
        }

        private void OnDeclineClicked()
        {
            // TODO(交互): +1 灵卵 → 关闭
        }
    }
}
