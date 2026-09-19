// ============================================================================
//  Panel_Result —— 战斗结算（含三选一技能草稿）
//  ---------------------------------------------------------------------------
//  三种出口：确认（拿草稿继续）→ 回节点地图；跳过（+1 灵卵）→ 回节点地图；
//  失败/撤退 → 也回节点地图（结构验证版不做真正的失败惩罚）。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>结算面板入参。</summary>
    public sealed class ResultRequest
    {
        public bool Win;
        public bool Retreated;
        public int Turns;
        public uint Fingerprint;
        public string Summary = "";
    }

    [UIPanel("Panel_Result", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]   // 奖励三选一：点空白不能关 —— 用户点歪一下整个面板就没了（实测）
    public sealed class ResultPanel : UIPanelBase
    {
        /// <summary>结算必须看完（不该被 Esc 关掉）（基类默认允许，这里显式禁止）。</summary>
        public override bool AllowBackClose => false;

        [SerializeField] private Image _imgBanner;             // Img_Banner    胜负横幅
        [BindArray("Tmp_Line_{0}", 4)]
        [SerializeField] private TMP_Text[] _tmpLines;         // 结算明细 4 行
        [SerializeField] private Image _imgEgg;                // Img_RewardEgg 灵卵大图
        [BindArray("Draft_{0}", 3)]
        [SerializeField] private Button[] _draftBtns;          // 三选一草稿卡
        [BindArray("Tmp_DraftName_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftNames;    // 草稿名
        [BindArray("Tmp_DraftDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftDescs;    // 草稿描述
        [SerializeField] private Button _btnSkip;              // Btn_Skip
        [SerializeField] private Button _btnConfirm;           // Btn_Confirm

        [Header("美术（由生成器自动绑定）")]
        [SerializeField] private Sprite _spriteWin;
        [SerializeField] private Sprite _spriteLose;

        private int _selectedDraft = -1;
        private bool _win;      // 本场胜负（结算奖励用）

        protected override void OnCreate()
        {
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                var idx = i;
                if (_draftBtns[i] != null) _draftBtns[i].onClick.AddListener(() => OnDraftClicked(idx));
            }
            if (_btnSkip != null) _btnSkip.onClick.AddListener(OnSkipClicked);
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var req = payload as ResultRequest;
            bool win = req != null && req.Win;
            _win = win;
            _selectedDraft = -1;
            _win = false;

            if (_imgBanner != null)
            {
                var s = win ? _spriteWin : _spriteLose;
                if (s != null) _imgBanner.sprite = s;
                _imgBanner.color = s != null ? Color.white : (win ? new Color(0.78f, 0.21f, 0.17f, 1f)
                                                                 : new Color(0.45f, 0.41f, 0.35f, 1f));
            }

            // 明细行：回合 / 事件指纹 / 结果
            SetLine(0, win ? "战斗胜利" : (req != null && req.Retreated ? "撤退" : "战斗失败"));
            SetLine(1, "回合数：" + (req != null ? req.Turns : 0));
            SetLine(2, "过程指纹：0x" + (req != null ? req.Fingerprint.ToString("X8") : "00000000"));
            SetLine(3, "灵卵 +1");

            for (int i = 0; i < _tmpDraftNames.Length; i++)
                if (_tmpDraftNames[i] != null) _tmpDraftNames[i].text = "技能草稿 " + (i + 1);
            for (int i = 0; i < _tmpDraftDescs.Length; i++)
                if (_tmpDraftDescs[i] != null) _tmpDraftDescs[i].text = "（结构验证版未接内容）";

            if (_btnConfirm != null) _btnConfirm.interactable = false;
            return UniTask.CompletedTask;
        }

        private void SetLine(int i, string text)
        {
            if (_tmpLines != null && i < _tmpLines.Length && _tmpLines[i] != null)
                _tmpLines[i].text = text;
        }

        private void OnDraftClicked(int index)
        {
            _selectedDraft = index;
            if (_btnConfirm != null) _btnConfirm.interactable = true;
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                if (_draftBtns[i] == null) continue;
                var img = _draftBtns[i].targetGraphic as Image;
                if (img != null)
                    img.color = (i == index) ? new Color(0.79f, 0.63f, 0.39f, 1f) : Color.white;
            }
        }

        private void OnSkipClicked()
        {
            BackToCampaign();
        }

        private void OnConfirmClicked()
        {
            // 结构验证版：三选一草稿还不是真实内容，没选也允许继续（选了就记一下）
            if (_selectedDraft >= 0)
                Debug.Log("[ResultPanel] 已选择技能草稿 " + (_selectedDraft + 1));
            ApplyOutcome();
            BackToCampaign();
        }

        /// <summary>
        /// 把这一场的胜败写进旅程：赢了给灵卵/墨锭并推进劫数，输了也有少量保底。
        /// 数值是占位节奏（每胜一场进一劫；三劫一境、三境一周目），正式数值等 GDD 定稿后改这里。
        /// </summary>
        private void ApplyOutcome()
        {
            var cur = WanXiang.Run.RunSave.Current;
            if (cur == null)
            {
                Debug.LogWarning("[ResultPanel] 没有进行中的旅程（试炼直进战斗），本场不写入存档。");
                return;
            }

            if (_win)
            {
                cur.Wins++;
                cur.Eggs += 8 + cur.Jie * 2;
                cur.Ink += 1;
                cur.Jie++;
                if (cur.Jie > 3) { cur.Jie = 1; cur.Realm++; }
            }
            else
            {
                cur.Losses++;
                cur.Eggs += 2;      // 保底：败了也给一点，别把玩家卡死
            }
            WanXiang.Run.RunSave.Save(cur);
            Debug.Log("[ResultPanel] 旅程已保存：槽位 " + cur.Slot + " " + cur.RealmText +
                      " 灵卵 " + cur.Eggs);
        }

        private void BackToCampaign()
        {
            CloseSelf();
            OpenPanelAsync<CampaignPanel>().Forget();
        }
    }
}
