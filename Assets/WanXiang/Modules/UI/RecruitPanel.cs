// ============================================================================
//  Panel_Recruit —— 招募节点（每幕首层固定节点，用户 2026-09-29 重设计）
//  ---------------------------------------------------------------------------
//  布局参考【灵市】（Panel_Market）+【局外养成】（Panel_Meta）：
//    左侧候选格子（五行各 2 只 ⇒ 最多 10 格）——单点格子看右侧详情；
//    双击格子（或点右侧详情里的【选择】）即入队；
//    顶部明确写「本幕可选至多 N 只 · 已选 M / N」，多选会被拦并提示。
//  第一幕选 3 只、第二~四幕选 1 只；免费刷新 1 次，之后每次刷新消耗灵卵。
//  选中的异兽入 Collection（拥有），队伍未满 5 则同时进 Team（可直接出战）。
//
//  UI 全在 prefab：格子由模板 Item_CardTemplate 克隆，详情区字段按名绑定。
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
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpHint;            // Tmp_Hint（本幕可选 N 只 · 已选 M）
        [SerializeField] private RectTransform _gridContent;   // Scroll_Candidates/Viewport/Content
        [SerializeField] private GameObject _cardTemplate;     // Item_CardTemplate（prefab 内隐藏）
        [SerializeField] private GameObject _detailRoot;       // Recruit_Detail
        [SerializeField] private Image _detailBig;             // Img_DetailBig
        [SerializeField] private TMP_Text _detailName;         // Tmp_DetailName
        [SerializeField] private TMP_Text _detailClass;        // Tmp_DetailClass
        [SerializeField] private TMP_Text _detailStats;        // Tmp_DetailStats
        [SerializeField] private TMP_Text _detailSkills;       // Tmp_DetailSkills
        [SerializeField] private Button _btnPick;              // Btn_Pick（选择 / 取消选择）
        [SerializeField] private TMP_Text _tmpEggCost;         // Tmp_EggCost
        [SerializeField] private Button _btnRefresh;           // Btn_Refresh
        [SerializeField] private Button _btnConfirm;           // Btn_Confirm

        private const int RefreshEggCost = 2;
        private const float DoubleClickWindow = 0.32f;

        private int _limit = 3;                 // 本幕可挑数量（act1=3，其余=1）
        private bool _refreshUsed;
        private readonly HashSet<string> _picked = new HashSet<string>();
        private readonly List<BeastDef> _candidates = new List<BeastDef>();
        private readonly List<GameObject> _cards = new List<GameObject>();
        private int _detailIndex = -1;
        private int _lastClickIndex = -1;
        private float _lastClickTime = -1f;

        protected override void OnCreate()
        {
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(OnRefreshClicked);
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
            if (_btnPick != null) _btnPick.onClick.AddListener(OnPickClicked);
            if (_cardTemplate != null) _cardTemplate.SetActive(false);
            if (_detailRoot != null) _detailRoot.SetActive(false);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            _limit = (run != null && run.Act <= 1) ? 3 : 1;
            _refreshUsed = false;
            _picked.Clear();
            _detailIndex = -1;
            _lastClickIndex = -1;
            if (_detailRoot != null) _detailRoot.SetActive(false);

            if (_tmpTitle != null)
                _tmpTitle.text = _limit == 3 ? "招募 · 挑选至多 3 只异兽" : "招募 · 挑选 1 只异兽";
            if (_tmpEggCost != null) _tmpEggCost.text = "刷新：免费";

            Reroll();
            RefreshHeader();
            return UniTask.CompletedTask;
        }

        // ---- 抽候选：五行各随机 2 只 ----
        private void Reroll()
        {
            foreach (var c in _cards) if (c != null) UnityEngine.Object.Destroy(c);
            _cards.Clear();
            _picked.Clear();
            _detailIndex = -1;
            _lastClickIndex = -1;
            if (_detailRoot != null) _detailRoot.SetActive(false);

            var cat = UnityEngine.Resources.FindObjectsOfTypeAll<ContentCatalogSO>();
            if (cat == null || cat.Length == 0) return;
            var all = ContentLibrary.BuildBeasts(cat[0]);
            if (all == null || all.Length == 0) return;

            var byElem = new Dictionary<Element, List<BeastDef>>();
            foreach (var b in all)
            {
                if (b == null) continue;
                if (!byElem.TryGetValue(b.Element, out var list))
                { list = new List<BeastDef>(); byElem[b.Element] = list; }
                list.Add(b);
            }

            var rng = new System.Random((int)(System.DateTime.Now.Ticks & 0x7fffffff));
            _candidates.Clear();
            foreach (var kv in byElem)
            {
                var pool = kv.Value;
                for (int n = 0; n < 2 && pool.Count > 0; n++)
                    _candidates.Add(pool[rng.Next(pool.Count)]);
            }
            // 打乱顺序，避免五行扎堆
            for (int i = _candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = _candidates[i]; _candidates[i] = _candidates[j]; _candidates[j] = t;
            }

            BuildCards();
        }

        private void BuildCards()
        {
            if (_cardTemplate == null || _gridContent == null) return;
            var cat = FindCatalog();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var beast = _candidates[i];
                var card = Instantiate(_cardTemplate, _gridContent);
                card.SetActive(true);
                card.name = "Item_Card_" + i;

                var nameTxt = card.transform.Find("Tmp_CardName")?.GetComponent<TMP_Text>();
                if (nameTxt != null) nameTxt.text = beast.DisplayName;
                var elemTxt = card.transform.Find("Tmp_CardElem")?.GetComponent<TMP_Text>();
                if (elemTxt != null) elemTxt.text = Cn.Of(beast.Element);
                var head = card.transform.Find("Img_CardHead")?.GetComponent<Image>();
                if (head != null && cat != null)
                {
                    head.sprite = cat.GetHead(beast.Id);
                    head.preserveAspect = true;
                }
                var sel = card.transform.Find("Img_CardSel");
                if (sel != null) sel.gameObject.SetActive(false);

                int idx = i;
                var btn = card.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => OnCardClicked(idx));
                _cards.Add(card);
            }
            RepaintSelection();
        }

        // ---- 格子点击：单击看详情，双击入队 ----
        private void OnCardClicked(int index)
        {
            if (index < 0 || index >= _candidates.Count) return;
            float now = Time.unscaledTime;
            if (_lastClickIndex == index && now - _lastClickTime <= DoubleClickWindow)
            {
                _lastClickIndex = -1;                 // 消费掉这一对点击
                TogglePick(index);
                return;
            }
            _lastClickIndex = index;
            _lastClickTime = now;
            ShowDetail(index);
        }

        private void OnPickClicked()
        {
            if (_detailIndex >= 0) TogglePick(_detailIndex);
        }

        private void TogglePick(int index)
        {
            if (index < 0 || index >= _candidates.Count) return;
            var beast = _candidates[index];
            if (_picked.Contains(beast.Id))
            {
                _picked.Remove(beast.Id);
            }
            else
            {
                if (_picked.Count >= _limit)
                {
                    if (_tmpHint != null)
                        _tmpHint.text = "本幕最多选 " + _limit + " 只 —— 已选满，先取消一只再选";
                    RepaintSelection();
                    return;
                }
                _picked.Add(beast.Id);
            }
            RepaintSelection();
            RefreshDetail();
            RefreshHeader();
        }

        private void RepaintSelection()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card == null) continue;
                bool sel = _picked.Contains(_candidates[i].Id);
                var img = card.GetComponent<Image>();
                if (img != null)
                    img.color = sel ? new Color(0.79f, 0.63f, 0.39f, 1f) : new Color(0.96f, 0.94f, 0.89f, 1f);
                var mark = card.transform.Find("Img_CardSel");
                if (mark != null) mark.gameObject.SetActive(sel);
            }
        }

        private void RefreshHeader()
        {
            if (_tmpHint != null)
                _tmpHint.text = "本幕可选至多 " + _limit + " 只（可少选）· 已选 " +
                                _picked.Count + " / " + _limit + "　—— 点格子看详情，双击入队";
            if (_btnConfirm != null) _btnConfirm.interactable = _picked.Count > 0;
        }

        // ---- 右侧详情 ----
        private void ShowDetail(int index)
        {
            if (index < 0 || index >= _candidates.Count) return;
            _detailIndex = index;
            RefreshDetail();
            if (_detailRoot != null) _detailRoot.SetActive(true);
        }

        private void RefreshDetail()
        {
            if (_detailIndex < 0 || _detailIndex >= _candidates.Count) return;
            var b = _candidates[_detailIndex];

            if (_detailBig != null)
            {
                var cat = FindCatalog();
                _detailBig.sprite = cat != null ? cat.GetHead(b.Id) : null;
                _detailBig.color = Color.white;
                _detailBig.preserveAspect = true;
            }
            if (_detailName != null) _detailName.text = b.DisplayName;
            if (_detailClass != null)
                _detailClass.text = Cn.Of(b.Element) + " · " + Cn.Of(b.Role) + " · " + Cn.Of(b.Rarity);
            if (_detailStats != null)
            {
                var st = b.Clone();
                BattleConfig.Default.ApplyPlaceholderStats(st);
                _detailStats.text = "生命 " + st.BaseHp + "　攻击 " + st.BaseAtk +
                                    "　防御 " + st.BaseDef + "　速度 " + st.BaseSpeed + "　（入队即 1 级基准）";
            }
            if (_detailSkills != null) _detailSkills.text = BuildSkillsText(b);
            if (_btnPick != null)
            {
                bool sel = _picked.Contains(b.Id);
                var l = _btnPick.GetComponentInChildren<TMP_Text>();
                if (l != null) l.text = sel ? "取消选择" : "选择";
            }
        }

        /// <summary>技能三行 + 特性：与局外养成同一排版（名字（类型）　按该兽攻击实算的点数）。</summary>
        private static string BuildSkillsText(BeastDef b)
        {
            var st = b.Clone();
            BattleConfig.Default.ApplyPlaceholderStats(st);
            int atk = st.BaseAtk, hp = st.BaseHp;

            var skills = b.AllSkills;      // 固定 4 项：[0]普攻 [1]战技 [2]终结技 [3]觉醒
            string[] marks = { "①", "②", "③" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                var sk = skills != null && i < skills.Length ? skills[i] : null;
                sb.Append(marks[i]).Append(' ');
                if (sk == null) { sb.Append("——\n"); continue; }
                sb.Append(sk.Name).Append("（").Append(Cn.Of(sk.Type)).Append("）　");
                if (SkillMath.TryDescribe(sk, atk, hp, true, out var nums)) sb.Append(nums);
                else sb.Append(string.IsNullOrEmpty(sk.Description) ? "（暂无描述）" : sk.Description);
                sb.Append('\n');
            }
            if (!string.IsNullOrEmpty(b.Trait.Name))
            {
                sb.Append("特性：").Append(b.Trait.Name);
                if (!string.IsNullOrEmpty(b.Trait.Description)) sb.Append("　").Append(b.Trait.Description);
            }
            return sb.ToString();
        }

        // ---- 刷新 / 确认 ----
        private void OnRefreshClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (!_refreshUsed)
            {
                _refreshUsed = true;
                if (_tmpEggCost != null) _tmpEggCost.text = "刷新：" + RefreshEggCost + " 灵卵";
                Reroll();
                RefreshHeader();
                return;
            }
            if (run != null && run.Eggs >= RefreshEggCost)
            {
                run.Eggs -= RefreshEggCost;
                WanXiang.Run.RunSave.SaveCurrent();
                Reroll();
                RefreshHeader();
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

        // ---- 小工具 ----
        private WanXiang.Battle.Presentation.SpriteCatalog _catalogCache;
        private WanXiang.Battle.Presentation.SpriteCatalog FindCatalog()
        {
            if (_catalogCache != null) return _catalogCache;
            var all = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Battle.Presentation.SpriteCatalog>();
            foreach (var c in all)
                if (c != null && c.Entries.Count > 0) { _catalogCache = c; break; }
            return _catalogCache;
        }
    }
}
