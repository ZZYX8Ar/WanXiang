// ============================================================================
//  Panel_Trial —— 天阙抉择（通关后，全作最重要的一次选择）
//  三卡等宽等高，禁止默认高亮引导；AllowBackClose = false。
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Trial", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Transient,
             CloseOnMaskClick = false)]
    public sealed class TrialPanel : UIPanelBase
    {
        [BindArray("Choice_{0}", 3)]
        [SerializeField] private Button[] _choiceBtns;         // 登天阙 / 续劫 / 归元
        [BindArray("Tmp_ChoiceTitle_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpChoiceTitles;  // 卡题
        [BindArray("Tmp_ChoiceDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpChoiceDescs;   // 后果说明
        [SerializeField] private TMP_Text _tmpStatus;          // Tmp_Status 当前境劫与灵卵数

        public override bool AllowBackClose => false;

        protected override void OnCreate()
        {
            for (int i = 0; i < _choiceBtns.Length; i++)
            {
                var idx = i;
                if (_choiceBtns[i] != null) _choiceBtns[i].onClick.AddListener(() => OnChoiceClicked(idx));
            }
        }

        private void OnChoiceClicked(int index)
        {
            // TODO(交互): 0 登天阙 → 结算并解锁下一境；1 续劫 → 劫数+1 重开一轮；2 归元 → 结算回主界面
        }
    }
}
