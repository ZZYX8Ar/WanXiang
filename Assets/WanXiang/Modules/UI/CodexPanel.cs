// ============================================================================
//  Panel_Codex —— 图鉴（印章墙网格 + 详情页两态）
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;
using WanXiang.Fusion;
using WanXiang.Battle.Presentation;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Codex", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class CodexPanel : UIPanelBase
    {
        [BindArray("Tab_{0}", 6)]
        [SerializeField] private Button[] _tabBtns;            // 五行 + 全部
        [SerializeField] private TMP_Text _tmpRate;            // Tmp_CollectRate 收集度
        [SerializeField] private ScrollRect _scrollGrid;       // Scroll_Grid
        [SerializeField] private RectTransform _sealTemplate;  // Item_Seal（模板，默认隐藏）
        [SerializeField] private GameObject _rootDetail;       // Root_Detail 详情页（默认隐藏）
        [SerializeField] private Image _imgBig;                // Img_BeastBig 立绘大图
        [SerializeField] private TMP_Text _tmpName;            // Tmp_Name
        [SerializeField] private TMP_Text _tmpSource;          // Tmp_ClassicSource
        [SerializeField] private TMP_Text _tmpQuote;           // Tmp_ClassicQuote
        [SerializeField] private TMP_Text _tmpTrait;           // Tmp_Trait
        [SerializeField] private TMP_Text _tmpSchool;          // Tmp_School
        [SerializeField] private Button _btnCloseDetail;       // Btn_CloseDetail
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        private int _tab;                                       // 0 = 全部，1..5 = 五行

        protected override void OnCreate()
        {
            if (_rootDetail != null) _rootDetail.SetActive(false);
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                var idx = i;
                if (_tabBtns[i] != null) _tabBtns[i].onClick.AddListener(() => OnTabClicked(idx));
            }
            if (_btnCloseDetail != null) _btnCloseDetail.onClick.AddListener(OnCloseDetailClicked);
        }

        protected override Cysharp.Threading.Tasks.UniTask OnOpenAsync(object payload)
        {
            if (_rootDetail != null) _rootDetail.SetActive(false);
            Rebuild();
            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        private void OnTabClicked(int index)
        {
            _tab = index;
            Rebuild();
        }

        private void OnCloseDetailClicked()
        {
            if (_rootDetail != null) _rootDetail.SetActive(false);
        }

        /// <summary>
        /// 重建图鉴网格：已解锁（CodexUnlock）显示立绘+名字，未解锁显示剪影+「？？？」。
        /// 数据来自 ContentCatalogSO（与编阵同源），解锁状态来自局外永久表。
        /// </summary>
        private void Rebuild()
        {
            if (_scrollGrid == null || _sealTemplate == null || _contentCatalog == null) return;
            var content = _scrollGrid.content;
            if (content == null) return;

            var all = WanXiang.Fusion.ContentLibrary.BuildBeasts(_contentCatalog);
            if (all == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                if (c == _sealTemplate) continue;
                Destroy(c.gameObject);
            }

            int unlocked = 0, shown = 0, ownedTotal = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                ownedTotal++;
                // 页签过滤：0 = 全部；1..5 对应 Element 的枚举值 +1（木/火/土/金/水）
                if (_tab > 0 && (int)b.Element != _tab - 1) continue;
                shown++;
                bool has = WanXiang.Run.CodexUnlock.IsUnlocked(b.Id);
                if (has) unlocked++;

                var item = Instantiate(_sealTemplate, content);
                item.name = "Item_Seal_" + b.Id;
                item.gameObject.SetActive(true);

                var imgT = item.Find("Img_Seal");
                var nmT = item.Find("Tmp_SealName");
                var img = imgT != null ? imgT.GetComponent<Image>() : null;
                var nm = nmT != null ? nmT.GetComponent<TMP_Text>() : null;
                if (has)
                {
                    if (img != null)
                    {
                        img.sprite = _sprites != null ? _sprites.GetHead(b.Id) : null;
                        img.color = Color.white;
                        img.preserveAspect = true;
                    }
                    if (nm != null) nm.text = b.DisplayName;
                }
                else
                {
                    if (img != null) { img.sprite = null; img.color = new Color(0.22f, 0.20f, 0.18f, 1f); }  // 剪影
                    if (nm != null) nm.text = "？？？";
                }

                var btn = item.GetComponent<Button>();
                if (btn == null) btn = item.gameObject.AddComponent<Button>();
                var captured = b;
                var unlockedFlag = has;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => ShowDetail(captured, unlockedFlag));
            }

            if (_tmpRate != null)
                _tmpRate.text = "收集度 " + WanXiang.Run.CodexUnlock.Count + " / " + ownedTotal +
                                "（本页 " + shown + "）";
        }

        /// <summary>填详情页。未解锁的只显示剪影与提示，不剧透。</summary>
        private void ShowDetail(WanXiang.Battle.Core.BeastDef b, bool unlocked)
        {
            if (b == null || _rootDetail == null) return;
            _rootDetail.SetActive(true);
            if (_imgBig != null)
            {
                _imgBig.sprite = unlocked && _sprites != null ? _sprites.GetHead(b.Id) : null;
                _imgBig.color = unlocked ? Color.white : new Color(0.22f, 0.20f, 0.18f, 1f);
                _imgBig.preserveAspect = true;
            }
            if (_tmpName != null) _tmpName.text = unlocked ? b.DisplayName : "？？？";
            if (_tmpSource != null) _tmpSource.text = unlocked ? (b.Source ?? "") : "——尚未收录——";
            if (_tmpQuote != null) _tmpQuote.text = unlocked ? (b.Quote ?? "") : "";
            if (_tmpTrait != null) _tmpTrait.text = unlocked ? b.Trait.Name : "";
            if (_tmpSchool != null)
                _tmpSchool.text = unlocked
                    ? (b.Element + " · " + b.Role + " · " + b.Rarity)
                    : "获得后解锁图鉴";
        }
    }
}
