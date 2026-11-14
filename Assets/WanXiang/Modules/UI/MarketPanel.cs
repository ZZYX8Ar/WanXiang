// ============================================================================
//  万相 · 灵市（2026-11-13 重构：商品改为「疗愈」）
//  ---------------------------------------------------------------------------
//  ⛔ 旧商品（异兽 / 魂 / 铸魂台）已全部删除：招募与魂系统都早已废弃，
//     货架还在卖异兽与"魂"是旧数据（用户 11-13："这个是旧的数据，给我去掉"）。
//  · 现商品：3 档疗愈（全队恢复 30% / 60% / 100% 生命），各 2 件，共 6 格
//  · 价格随幕上涨（MarketPricing.FinalPrice），刷新 1 灵卵
//  · 数据源：RunState.TeamHpPct（队伍每只当前血量百分比，上限 100）
// ============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Market", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class MarketPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpEggs;            // Tmp_Eggs        灵卵余额
        [SerializeField] private Button _btnRefresh;           // Btn_Refresh
        [SerializeField] private TMP_Text _tmpRefreshCost;     // Tmp_RefreshCost
        [SerializeField] private Button _btnBack;              // Btn_Back  返回节点地图
        [SerializeField] private Button[] _goodsBtns;          // 6 张商品卡
        [SerializeField] private Image[] _imgGoodsHead;        // 商品头像
        [SerializeField] private TMP_Text[] _tmpGoodsPrice;    // 价格
        [SerializeField] private Image[] _imgGoodsSold;        // 售罄印章
        [SerializeField] private TMP_Text _tmpHint;            // Tmp_Hint

        [SerializeField] private RectTransform _detailRoot;    // Market_DetailMask（整体显示/隐藏）
        [SerializeField] private Image _detailBig;             // Img_Big
        [SerializeField] private TMP_Text _detailName;         // Tmp_Name
        [SerializeField] private TMP_Text _detailInfo;         // Tmp_Info
        [SerializeField] private TMP_Text _detailSource;       // Tmp_Source
        [SerializeField] private TMP_Text _detailPrice;        // Tmp_Price
        [SerializeField] private Button _detailBuy;            // Btn_Buy
        [SerializeField] private Button _detailClose;          // Btn_Close

        /// <summary>一档疗愈（表驱动：加档 = 加一行）。</summary>
        private struct HealTier
        {
            public string Name;
            public int HealPct;
            public int BasePrice;
            public Color Tint;
        }

        private static readonly HealTier[] Tiers =
        {
            new HealTier { Name = "疗愈·小", HealPct = 30,  BasePrice = 3,  Tint = new Color(0.62f, 0.86f, 0.62f, 1f) },
            new HealTier { Name = "疗愈·中", HealPct = 60,  BasePrice = 6,  Tint = new Color(0.50f, 0.82f, 0.78f, 1f) },
            new HealTier { Name = "疗愈·全", HealPct = 100, BasePrice = 10, Tint = new Color(0.93f, 0.80f, 0.48f, 1f) },
        };

        private sealed class Good
        {
            public int TierIndex;
            public int Price;
            public bool Sold;
            public string Name => Tiers[TierIndex].Name;
        }

        private readonly List<Good> _goods = new List<Good>();
        private int _refreshCount;
        private int _detailIndex = -1;
        private Sprite _healIcon;

        protected override void OnCreate()
        {
            _healIcon = Resources.Load<Sprite>("UI/Kit/market_heal");
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(() => Reroll(false));
            if (_detailBuy != null) _detailBuy.onClick.AddListener(OnBuyClicked);
            if (_detailClose != null) _detailClose.onClick.AddListener(HideDetail);
            if (_detailRoot != null)
            {
                _detailRoot.gameObject.SetActive(false);
                var mb = _detailRoot.GetComponent<Button>();
                if (mb == null) mb = _detailRoot.gameObject.AddComponent<Button>();
                mb.transition = Selectable.Transition.None;
                mb.onClick.AddListener(HideDetail);
            }
            if (_btnBack != null) _btnBack.onClick.AddListener(BackToMap);
            for (int i = 0; i < _goodsBtns.Length; i++)
            {
                int idx = i;
                if (_goodsBtns[i] != null) _goodsBtns[i].onClick.AddListener(() => OnGoodsClicked(idx));
            }
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Reroll(true);      // 进店免费摇一批
            return UniTask.CompletedTask;
        }

        private void BackToMap()
        {
            CloseSelf();
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) Cysharp.Threading.Tasks.UniTask.Void(async () =>
            {
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                await ui.OpenAsync<CampaignPanel>();
            });
        }

        private void Reroll(bool free)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            if (!free)
            {
                // ★ IS「诡意行商」：本幕首次刷新免费（RunState.FreeRefreshAct 记账）
                bool freeThisAct = run.FreeRefreshAct != run.Act;
                if (!freeThisAct && run.Eggs < 1)
                {
                    SetHint("灵卵不足，刷不动货架了");
                    SfxThrottled("sfx_ui_currency_lack");
                    return;
                }
                if (freeThisAct)
                {
                    run.FreeRefreshAct = run.Act;
                    SetHint("商队初见：本幕首次刷新免费");
                }
                else run.Eggs -= 1;
                _refreshCount++;
                SfxThrottled("sfx_ui_reroll");
            }

            float priceMul = WanXiang.Campaign.WeatherMapEffects.Current.PriceMul;
            _goods.Clear();
            // 3 档 × 2 件 = 6 格（顺序交替，货架看起来不呆板）
            for (int copy = 0; copy < 2; copy++)
                for (int t = 0; t < Tiers.Length; t++)
                    _goods.Add(new Good
                    {
                        TierIndex = t,
                        Price = WanXiang.Run.MarketPricing.FinalPrice(Tiers[t].BasePrice, run.Act, priceMul),
                    });

            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
            if (_tmpRefreshCost != null)
                _tmpRefreshCost.text = run.FreeRefreshAct != run.Act
                    ? "刷新（本幕首次免费）" : "刷新（1 灵卵）";
            SetHint("灵市：只卖疗愈。灵卵是唯一货币。");
            Paint();
            WanXiang.Run.RunSave.SaveCurrent();
        }

        private void Paint()
        {
            if (_goodsBtns == null) return;
            for (int i = 0; i < _goodsBtns.Length; i++)
            {
                bool has = i < _goods.Count;
                if (_goodsBtns[i] != null) _goodsBtns[i].gameObject.SetActive(has);
                if (!has) continue;
                var g = _goods[i];
                var tier = Tiers[g.TierIndex];
                if (_imgGoodsHead != null && i < _imgGoodsHead.Length && _imgGoodsHead[i] != null)
                {
                    _imgGoodsHead[i].sprite = _healIcon;
                    _imgGoodsHead[i].type = Image.Type.Simple;
                    _imgGoodsHead[i].preserveAspect = true;
                    _imgGoodsHead[i].color = _healIcon != null ? tier.Tint : new Color(1f, 1f, 1f, 0.25f);
                }
                if (_tmpGoodsPrice != null && i < _tmpGoodsPrice.Length && _tmpGoodsPrice[i] != null)
                    _tmpGoodsPrice[i].text = g.Sold ? "已售出" : g.Price + " 灵卵";
                if (_imgGoodsSold != null && i < _imgGoodsSold.Length && _imgGoodsSold[i] != null)
                    _imgGoodsSold[i].gameObject.SetActive(g.Sold);
            }
            var run = WanXiang.Run.RunSave.Current;
            if (_tmpEggs != null && run != null) _tmpEggs.text = "灵卵 " + run.Eggs;
        }

        private void OnGoodsClicked(int index)
        {
            if (index < 0 || index >= _goods.Count) return;
            if (_goods[index].Sold) return;
            ShowDetail(index);
        }

        private void ShowDetail(int index)
        {
            var g = _goods[index];
            _detailIndex = index;
            var tier = Tiers[g.TierIndex];
            if (_detailBig != null)
            {
                _detailBig.sprite = _healIcon;
                _detailBig.color = _healIcon != null ? tier.Tint : new Color(1f, 1f, 1f, 0.25f);
            }
            var run = WanXiang.Run.RunSave.Current;
            int avg = TeamAvgHpPct(run);
            if (_detailName != null) _detailName.text = tier.Name;
            if (_detailInfo != null) _detailInfo.text = "全队恢复 " + tier.HealPct + "% 生命（当前平均 " + avg + "%）";
            if (_detailSource != null) _detailSource.text = "灵市药囊：立即作用于全队每一只异兽，上限 100%。";
            if (_detailPrice != null) _detailPrice.text = "价格：" + g.Price + " 灵卵";
            if (_detailBuy != null)
            {
                bool afford = run != null && run.Eggs >= g.Price;
                _detailBuy.interactable = afford;
                var l = _detailBuy.GetComponentInChildren<TMP_Text>();
                if (l != null) l.text = afford ? "购买" : "灵卵不足";
            }
            if (_detailRoot != null)
            {
                _detailRoot.gameObject.SetActive(true);
                _detailRoot.SetAsLastSibling();
            }
        }

        private void OnBuyClicked()
        {
            int index = _detailIndex;
            if (index < 0 || index >= _goods.Count) return;
            var g = _goods[index];
            if (g.Sold) { HideDetail(); return; }

            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.ActiveFloorRule == "hard_noheal")
            {
                SetHint("本段「守关在望」：地图禁疗，药囊暂不可用");
                return;
            }
            if (run.Eggs < g.Price)
            {
                SetHint("灵卵不够（需要 " + g.Price + "）");
                SfxThrottled("sfx_ui_currency_lack");
                return;
            }

            run.Eggs -= g.Price;
            int healed = ApplyHeal(run, Tiers[g.TierIndex].HealPct);
            g.Sold = true;
            WanXiang.Run.RunSave.SaveCurrent();
            SfxThrottled("sfx_ui_buy");
            SetHint("已购入 " + g.Name + "：全队恢复 " + Tiers[g.TierIndex].HealPct + "%（现平均 " + healed + "%）");
            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
            HideDetail();
            Paint();
            UnityEngine.Debug.Log("[Market] 购买 " + g.Name + "，全队平均血量 → " + healed + "%");
        }

        /// <summary>全队加血（上限 100），返回加完后的平均百分比。</summary>
        private static int ApplyHeal(WanXiang.Run.RunState run, int healPct)
        {
            if (run.TeamHpPct == null)
                run.TeamHpPct = new Dictionary<string, int>();
            if (run.Team != null)
                for (int i = 0; i < run.Team.Count; i++)
                {
                    string id = run.Team[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    int cur;
                    if (!run.TeamHpPct.TryGetValue(id, out cur)) cur = 100;
                    run.TeamHpPct[id] = Mathf.Clamp(cur + healPct, 0, 100);
                }
            return TeamAvgHpPct(run);
        }

        private static int TeamAvgHpPct(WanXiang.Run.RunState run)
        {
            if (run == null || run.Team == null || run.Team.Count == 0) return 100;
            int sum = 0, n = 0;
            for (int i = 0; i < run.Team.Count; i++)
            {
                string id = run.Team[i];
                if (string.IsNullOrEmpty(id)) continue;
                int v = 100;
                if (run.TeamHpPct != null) run.TeamHpPct.TryGetValue(id, out v);
                sum += v; n++;
            }
            return n > 0 ? sum / n : 100;
        }

        private void HideDetail()
        {
            _detailIndex = -1;
            if (_detailRoot != null) _detailRoot.gameObject.SetActive(false);
        }

        private void SetHint(string msg)
        {
            if (_tmpHint != null) _tmpHint.text = msg;
        }
    }
}
