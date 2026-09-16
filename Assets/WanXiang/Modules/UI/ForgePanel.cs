// ============================================================================
//  Panel_Forge —— 铸魂台（融合）：宿主 | 漩涡 | 灵魂 三栏对称
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Forge", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached)]
    public sealed class ForgePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpEggs;            // Tmp_Eggs      灵卵余额
        [SerializeField] private ScrollRect _scrollHosts;      // Scroll_Hosts  宿主列表
        [SerializeField] private Image _imgHost;               // Img_HostPreview 宿主立绘
        [SerializeField] private Image _imgVortex;             // Img_Vortex    墨色漩涡
        [SerializeField] private ScrollRect _scrollSouls;      // Scroll_Souls  灵魂列表
        [SerializeField] private Image _imgSoul;               // Img_SoulPreview 灵魂球体
        [BindArray("Img_Cover_{0}", 5)]
        [SerializeField] private Image[] _imgCover;            // 五行覆盖（木火土金水，未覆盖灰置）
        [BindArray("Img_Sw_{0}", 5)]
        [SerializeField] private Image[] _imgSwatch;           // 五色区预览（bodyMain/bodyAccent/energyGlow/eyeCore/outline）
        [SerializeField] private TMP_Text _tmpResult;          // Tmp_ResultName 融合预览名
        [SerializeField] private Button _btnFuse;              // Btn_Fuse

        protected override void OnCreate()
        {
            if (_btnFuse != null) _btnFuse.onClick.AddListener(OnFuseClicked);
        }

        private void OnFuseClicked()
        {
            // TODO(交互): 调 SoulForge 执行融合 → 漩涡收束动画 → 结算提示
        }
    }
}
