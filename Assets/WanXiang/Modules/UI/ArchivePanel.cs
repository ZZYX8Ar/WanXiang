// ============================================================================
//  Panel_Archive —— 残卷阁（剧情面板）
//  ---------------------------------------------------------------------------
//  形态（用户定案）：
//    · 顶部五行分类 tab（全部 / 木 / 火 / 土 / 金 / 水）—— 与图鉴、异兽培养一致；
//      五行 == 剧情文档的「五册」，所以按五行筛就是按册筛。
//    · 网格卡片（模板 Item_BeastCard 克隆）：左 = 异兽立绘，右 = 6 个待点亮碎片格；
//      收集到第 N 片 ⇒ 第 N 格自动点亮。
//    · 点击**已点亮**的碎片 ⇒ 弹剧情面板（章节标题 + 正文 2~3 句）。
//    · 集齐前 3 片（MetaState.AwakenSoulThreshold）⇒ 卡片上「领取材料」可点，
//      领到该兽的**专属材料**（**进化**的前置；领材料是附加，主线是看剧情）。
//
//  ⚠ 铁律（用户反复强调）：**所有 UI 节点都在 prefab 里**，本类只负责填数据与显隐 ——
//    不做任何运行时构建/AddComponent，否则美术没法在 prefab 上改。
//    卡片内的控件按**名字**取（与图鉴 Panel_Codex 同款做法）。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Archive", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class ArchivePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;            // Tmp_Title
        [SerializeField] private RectTransform _scrollContent;  // Scroll_Archive/Viewport/Content
        [SerializeField] private Button _btnClose;              // Btn_Close
        [BindArray("Tab_{0}", 6)]
        [SerializeField] private Button[] _tabBtns;             // Tab_0..5（全部/木/火/土/金/水）
        [SerializeField] private RectTransform _cardTemplate;   // Item_BeastCard（模板，默认隐藏）

        [Header("剧情弹层")]
        [SerializeField] private GameObject _storyOverlay;      // StoryOverlay
        [SerializeField] private TMP_Text _storyTitle;          // Tmp_StoryTitle
        [SerializeField] private TMP_Text _storyBody;           // Tmp_StoryBody
        [SerializeField] private TMP_Text _storyPage;           // Tmp_StoryPage
        [SerializeField] private Button _btnStoryClose;         // Btn_StoryClose

        [SerializeField] private TMP_Text _tmpHint;             // Tmp_Hint

        // ---- 配色（与 prefab 占位色一致；运行时按状态改色，属"填数据"）----
        private static readonly Color C_Silk   = new Color(0.914f, 0.886f, 0.824f, 1f);
        private static readonly Color C_Gold   = new Color(0.79f, 0.63f, 0.39f, 1f);
        private static readonly Color C_Ink    = new Color(0.165f, 0.129f, 0.094f, 1f);
        private static readonly Color C_Ink2   = new Color(0.42f, 0.38f, 0.34f, 1f);
        private static readonly Color C_Dim    = new Color(0.58f, 0.55f, 0.50f, 1f);
        private static readonly Color C_LitBg  = new Color(0.86f, 0.76f, 0.55f, 1f);
        private static readonly Color C_SlotOff = new Color(0.90f, 0.89f, 0.87f, 1f);

        private int _tab;                                        // 0 = 全部，1..5 = 木/火/土/金/水

        private static WanXiang.Battle.Presentation.SpriteCatalog _catalogCache;

        // ==================================================================== 生命周期

        protected override void OnCreate()
        {
            if (_btnClose != null) _btnClose.onClick.AddListener(CloseSelf);
            if (_btnStoryClose != null) _btnStoryClose.onClick.AddListener(HideStory);
            if (_cardTemplate != null) _cardTemplate.gameObject.SetActive(false);
            if (_storyOverlay != null) _storyOverlay.SetActive(false);

            for (int i = 0; i < (_tabBtns != null ? _tabBtns.Length : 0); i++)
            {
                int idx = i;
                if (_tabBtns[i] != null) _tabBtns[i].onClick.AddListener(() => OnTabClicked(idx));
            }
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            if (_tmpTitle != null) _tmpTitle.text = "残卷阁 · 异兽剧情";
            Rebuild();
            return UniTask.CompletedTask;
        }

        private void OnTabClicked(int index)
        {
            _tab = index;
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                if (_tabBtns[i] == null) continue;
                var img = _tabBtns[i].targetGraphic as Image;
                if (img != null) img.color = (i == _tab) ? C_Gold : C_Silk;
            }
            Rebuild();
        }

        // ==================================================================== 网格

        private void Rebuild()
        {
            if (_scrollContent == null || _cardTemplate == null) return;

            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var c = _scrollContent.GetChild(i);
                if (c == _cardTemplate) continue;        // 模板自己留着
                Destroy(c.gameObject);
            }

            var all = LoadBeasts();
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (all == null || meta == null) return;

            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                // ⚠ 五行筛选必须走 ElementTabs（Element 枚举是 None=0/Wood=1/…，
                //   自己推 `tab - 1` 会把「木」映射到 None ⇒ 列表空白，踩过）。
                if (!ElementTabs.Matches(b.Element, _tab)) continue;
                BuildCard(b, meta);
            }
        }

        private void BuildCard(BeastDef b, WanXiang.Meta.MetaState meta)
        {
            int got = Mathf.Clamp(meta.FragmentCountOf(b.Id), 0, BeastLore.FragmentTotal);
            bool claimed = meta.HasAwakenSoul(b.Id);
            bool canClaim = meta.CanClaimAwakenSoul(b.Id);
            int need = WanXiang.Meta.MetaState.AwakenSoulThreshold;

            var card = Instantiate(_cardTemplate, _scrollContent);
            card.name = "Card_" + b.Id;
            card.gameObject.SetActive(true);

            // ---- 立绘 ----
            var pImg = FindImg(card, "Img_Portrait");
            if (pImg != null)
            {
                var sp = Catalog() != null ? Catalog().GetHead(b.Id) : null;
                if (sp != null)
                {
                    pImg.sprite = sp;
                    pImg.color = Color.white;
                    pImg.preserveAspect = true;
                }
            }

            // ---- 名字 / 专属材料 / 进度 ----
            var nm = FindTmp(card, "Tmp_Name");
            if (nm != null) nm.text = b.DisplayName;

            var mat = FindTmp(card, "Tmp_Material");
            string matName = BeastLore.MaterialName(b.Id);
            if (mat != null)
            {
                mat.text = claimed ? ("专属材料 · " + matName + "（已领）") : ("专属材料 · " + matName);
                mat.color = claimed ? C_Gold : C_Ink2;
            }

            var prog = FindTmp(card, "Tmp_Progress");
            if (prog != null) prog.text = "剧情碎片 " + got + "/" + BeastLore.FragmentTotal;

            // ---- 6 个碎片格：已收集才亮、才可点 ----
            for (int i = 0; i < BeastLore.FragmentTotal; i++)
            {
                var slot = card.Find("Frag_" + i) as RectTransform;
                if (slot == null) continue;
                bool lit = i < got;

                var img = slot.GetComponent<Image>();
                if (img != null) img.color = lit ? C_LitBg : C_SlotOff;
                var num = FindTmp(slot, "Tmp_Num");
                if (num != null) num.color = lit ? C_Ink : C_Dim;

                var sbtn = slot.GetComponent<Button>();
                if (sbtn != null)
                {
                    sbtn.interactable = lit;
                    sbtn.onClick.RemoveAllListeners();
                    if (lit)
                    {
                        var beast = b; int idx = i;
                        sbtn.onClick.AddListener(() => ShowStory(beast, idx));
                    }
                }
            }

            // ---- 领取专属材料（附加功能；门槛 = 前 3 片）----
            var claim = card.Find("Btn_Claim") as RectTransform;
            if (claim != null)
            {
                var cimg = claim.GetComponent<Image>();
                if (cimg != null)
                    cimg.color = claimed ? C_Dim
                               : (canClaim ? C_Gold : new Color(0.86f, 0.85f, 0.83f, 1f));
                var clb = FindTmp(claim, "Tmp_Label");
                if (clb != null)
                {
                    clb.text = claimed ? "已领取"
                             : (canClaim ? "领取材料" : ("还差 " + Mathf.Max(0, need - got) + " 片"));
                    clb.color = canClaim ? C_Ink : C_Ink2;
                }
                var cbtn = claim.GetComponent<Button>();
                if (cbtn != null)
                {
                    cbtn.interactable = canClaim;
                    cbtn.onClick.RemoveAllListeners();
                    if (canClaim)
                    {
                        var beast = b;
                        cbtn.onClick.AddListener(() => OnClaim(beast));
                    }
                }
            }
        }

        private void OnClaim(BeastDef b)
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (meta == null || !meta.CanClaimAwakenSoul(b.Id)) return;
            meta.ClaimAwakenSoul(b.Id);
            WanXiang.Meta.MetaStore.Save();
            Debug.Log("[ArchivePanel] 领取专属材料：" + b.DisplayName + " · " + BeastLore.MaterialName(b.Id));
            Rebuild();
        }

        // ==================================================================== 剧情弹层

        private void ShowStory(BeastDef b, int index)
        {
            var frag = BeastLore.Fragment(b.Id, index);
            if (string.IsNullOrEmpty(frag.Title) || _storyOverlay == null) return;

            if (_storyTitle != null) _storyTitle.text = "「" + frag.Title + "」";
            if (_storyBody != null) _storyBody.text = frag.Text;
            if (_storyPage != null)
                _storyPage.text = BeastLore.FragmentName(b.DisplayName, index)
                                + "　·　第 " + (index + 1) + " / " + BeastLore.FragmentTotal + " 片"
                                + "　·　" + (b.Source ?? "");

            _storyOverlay.SetActive(true);
            _storyOverlay.transform.SetAsLastSibling();   // 盖在网格之上
        }

        private void HideStory()
        {
            if (_storyOverlay != null) _storyOverlay.SetActive(false);
        }

        // ==================================================================== 工具

        private static Image FindImg(Transform root, string name)
        {
            var t = root.Find(name);
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static TMP_Text FindTmp(Transform root, string name)
        {
            var t = root.Find(name);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        private static WanXiang.Battle.Presentation.SpriteCatalog Catalog()
        {
            if (_catalogCache != null) return _catalogCache;
            var all = Resources.FindObjectsOfTypeAll<WanXiang.Battle.Presentation.SpriteCatalog>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Entries.Count > 0) { _catalogCache = all[i]; break; }
            return _catalogCache;
        }

        private static BeastDef[] LoadBeasts()
        {
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) return null;
            return WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
        }
    }
}
