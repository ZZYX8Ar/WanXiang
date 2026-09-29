// ============================================================================
//  Panel_Recruit —— 招募节点（每幕首层固定节点，用户 2026-09-29 重设计）
//  ---------------------------------------------------------------------------
//  每属性随机 2 选（共 5 属性 × 2 = 10 个候选），玩家挑：
//    第一幕挑 3 只、第二~四幕挑 1 只。免费刷新 1 次，之后每次刷新消耗灵卵。
//  选中的异兽入 Collection（拥有），队伍未满 5 则同时进 Team（可直接出战）。
//
//  UI 全在 prefab：一张 Card_Template（运行时克隆），其余控件按名字取。
//  候选卡用 Instantiate 克隆（用户规则允许的"模板克隆"，非运行时拼界面）。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Recruit", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class RecruitPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;
        [SerializeField] private TMP_Text _tmpHint;
        [SerializeField] private TMP_Text _tmpSelected;
        [SerializeField] private TMP_Text _tmpEggCost;
        [SerializeField] private RectTransform _scrollContent;
        [SerializeField] private GameObject _cardTemplate;     // 预制候选卡（prefab 里保持失活，运行时克隆）
        [SerializeField] private Button _btnRefresh;
        [SerializeField] private Button _btnConfirm;

        private const int RefreshEggCost = 2;
        private int _limit = 3;                 // 本幕可挑数量（act1=3，其余=1）
        private bool _refreshUsed;
        private readonly HashSet<string> _picked = new HashSet<string>();
        private readonly List<GameObject> _cards = new List<GameObject>();
        private List<BeastDef> _candidates;

        protected override void OnCreate()
        {
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(OnRefreshClicked);
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            _limit = (run != null && run.Act <= 1) ? 3 : 1;
            _refreshUsed = false;
            _picked.Clear();

            if (_tmpTitle != null)
                _tmpTitle.text = (_limit == 3 ? "招募 · 挑选 3 只异兽" : "招募 · 挑选 1 只异兽");
            if (_tmpHint != null)
                _tmpHint.text = "每属性随机 2 选；免费刷新 1 次，之后每次刷新消耗 " + RefreshEggCost + " 灵卵。";
            if (_tmpEggCost != null)
                _tmpEggCost.text = "刷新：免费";

            Reroll(forceFree: true);
            RefreshSelectedText();
            return UniTask.CompletedTask;
        }

        // ---- 抽候选：5 属性 × 2 ----
        private void Reroll(bool forceFree)
        {
            // 清旧卡
            foreach (var c in _cards) UnityEngine.Object.Destroy(c);
            _cards.Clear();
            _picked.Clear();

            var cat = UnityEngine.Resources.FindObjectsOfTypeAll<ContentCatalogSO>();
            if (cat == null || cat.Length == 0) return;
            var all = ContentLibrary.BuildBeasts(cat[0]);
            if (all == null || all.Length == 0) return;

            // 按属性分桶
            var byElem = new Dictionary<Element, List<BeastDef>>();
            foreach (var b in all)
            {
                if (b == null) continue;
                if (!byElem.TryGetValue(b.Element, out var list))
                { list = new List<BeastDef>(); byElem[b.Element] = list; }
                list.Add(b);
            }

            var rng = new System.Random((int)(System.DateTime.Now.Ticks & 0x7fffffff) + _cards.Count * 31);
            _candidates = new List<BeastDef>();
            foreach (var kv in byElem)
            {
                var pool = kv.Value;
                for (int n = 0; n < 2 && pool.Count > 0; n++)
                {
                    var pick = pool[rng.Next(pool.Count)];
                    _candidates.Add(pick);
                }
            }
            // 打乱顺序，避免五行扎堆
            for (int i = _candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = _candidates[i]; _candidates[i] = _candidates[j]; _candidates[j] = t;
            }

            if (_cardTemplate == null || _scrollContent == null) return;
            for (int i = 0; i < _candidates.Count; i++)
            {
                var beast = _candidates[i];
                var card = Instantiate(_cardTemplate, _scrollContent);
                card.SetActive(true);
                card.name = "Card_" + beast.Id;
                var nameTxt = card.transform.Find("Tmp_CardName")?.GetComponent<TMPro.TMP_Text>();
                if (nameTxt != null) nameTxt.text = beast.DisplayName;
                var elemTxt = card.transform.Find("Tmp_CardElem")?.GetComponent<TMPro.TMP_Text>();
                if (elemTxt != null) elemTxt.text = ElementCn(beast.Element);
                var btn = card.transform.Find("Btn_CardPick")?.GetComponent<UnityEngine.UI.Button>();
                var id = beast.Id;
                if (btn != null) btn.onClick.AddListener(() => OnCardClicked(id, card));
                _cards.Add(card);
            }
        }

        private void OnCardClicked(string id, GameObject card)
        {
            if (_picked.Contains(id))
            {
                _picked.Remove(id);
                SetCardHighlight(card, false);
            }
            else
            {
                if (_picked.Count >= _limit) return;   // 已达上限，先取消别的
                _picked.Add(id);
                SetCardHighlight(card, true);
            }
            RefreshSelectedText();
        }

        private void SetCardHighlight(GameObject card, bool on)
        {
            var img = card.GetComponent<Image>();
            if (img != null) img.color = on ? new Color(0.79f, 0.63f, 0.39f, 1f) : Color.white;
        }

        private void OnRefreshClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (!_refreshUsed)
            {
                _refreshUsed = true;
                if (_tmpEggCost != null) _tmpEggCost.text = "刷新：" + RefreshEggCost + " 灵卵";
                Reroll(false);
                RefreshSelectedText();
                return;
            }
            if (run != null && run.Eggs >= RefreshEggCost)
            {
                run.Eggs -= RefreshEggCost;
                WanXiang.Run.RunSave.SaveCurrent();
                Reroll(false);
                RefreshSelectedText();
            }
            else if (_tmpHint != null)
            {
                _tmpHint.text = "灵卵不足，无法刷新（需 " + RefreshEggCost + " 灵卵）。";
            }
        }

        private void OnConfirmClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && _picked.Count > 0)
            {
                if (run.Collection == null) run.Collection = new List<string>();
                if (run.Team == null) run.Team = new List<string>();
                foreach (var id in _picked)
                {
                    if (!run.Collection.Contains(id)) run.Collection.Add(id);
                    if (run.Team.Count < 5 && !run.Team.Contains(id)) run.Team.Add(id);
                }
                WanXiang.Run.RunSave.SaveCurrent();
                Debug.Log("[RecruitPanel] 招募入队 " + _picked.Count + " 只：" + string.Join(",", _picked));
            }
            CloseSelf();
            OpenPanelAsync<CampaignPanel>().Forget();
        }

        private void RefreshSelectedText()
        {
            if (_tmpSelected != null)
                _tmpSelected.text = "已选 " + _picked.Count + " / " + _limit;
            if (_btnConfirm != null) _btnConfirm.interactable = _picked.Count > 0;
        }

        private static string ElementCn(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return "？";
            }
        }
    }
}
