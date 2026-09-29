// ============================================================================
//  Panel_Relic —— 遗物节点（每幕第二层固定节点，参考杀戮尖塔）
//  ---------------------------------------------------------------------------
//  从遗物池随机抽 3 个，玩家挑 1 个入队（局内变强载体）。
//  UI 全部在 prefab 里：三张草稿卡（Draft_0/1/2 + Tmp_DraftName/Desc）+ 确认/跳过。
//   绑定名与 ResultPanel 的奖励区完全一致，便于美术在 prefab 上改。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Campaign;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Relic", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class RelicPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;
        [BindArray("Draft_{0}", 3)]
        [SerializeField] private Button[] _draftBtns;
        [BindArray("Tmp_DraftName_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftNames;
        [BindArray("Tmp_DraftDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftDescs;
        [SerializeField] private Button _btnConfirm;
        [SerializeField] private Button _btnSkip;

        private int _selected = -1;
        private System.Collections.Generic.List<RelicDef> _offered;

        protected override void OnCreate()
        {
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                var idx = i;
                if (_draftBtns[i] != null) _draftBtns[i].onClick.AddListener(() => OnDraftClicked(idx));
            }
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
            if (_btnSkip != null) _btnSkip.onClick.AddListener(OnSkipClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // 抽 3 个遗物（本节点本身是「遗物」节点，视为稀有权重偏高）
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.Act : 1;
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                (ulong)System.DateTime.Now.Ticks ^ (uint)(act * 40503));
            _offered = RelicCatalog.Roll(3, act, true, rng, run != null ? run.Relics : null);
            _selected = -1;

            if (_tmpTitle != null) _tmpTitle.text = "遗物 · 三选一（本局已持有 " +
                (run != null && run.Relics != null ? run.Relics.Count : 0) + " 件）";

            for (int i = 0; i < _tmpDraftNames.Length; i++)
            {
                if (_tmpDraftNames[i] != null)
                    _tmpDraftNames[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Name : "—";
                if (_tmpDraftDescs[i] != null)
                    _tmpDraftDescs[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Desc : "";
            }

            if (_btnConfirm != null) _btnConfirm.interactable = false;
            return UniTask.CompletedTask;
        }

        private void OnDraftClicked(int index)
        {
            _selected = index;
            if (_btnConfirm != null) _btnConfirm.interactable = true;
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                if (_draftBtns[i] == null) continue;
                var img = _draftBtns[i].targetGraphic as Image;
                if (img != null)
                    img.color = (i == index) ? new Color(0.79f, 0.63f, 0.39f, 1f) : Color.white;
            }
        }

        private void OnSkipClicked() => BackToCampaign();

        private void OnConfirmClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && _offered != null && _selected >= 0 && _selected < _offered.Count)
            {
                var relic = _offered[_selected];
                if (run.Relics == null) run.Relics = new System.Collections.Generic.List<string>();
                if (!run.Relics.Contains(relic.Id)) run.Relics.Add(relic.Id);
                WanXiang.Run.RunSave.SaveCurrent();
                Debug.Log("[RelicPanel] 获得遗物：" + relic.Name + "（" + relic.Id + "）");
            }
            BackToCampaign();
        }

        private void BackToCampaign()
        {
            CloseSelf();
            OpenPanelAsync<CampaignPanel>().Forget();
        }
    }
}
