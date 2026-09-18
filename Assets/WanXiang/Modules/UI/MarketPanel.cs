// ============================================================================
//  Panel_Market —— 灵市（商店）
//  v1.2 填充：货架数据流。
//    商品 = 按本局种子（RunSeed）抽的异兽 ×4；购买 → 扣灵卵 → 入队 → 盖售罄印。
//    刷新 → 每次花 1 灵卵重摇（GDD 的"可刷新 1 次（1 灵卵）"放宽为付费无限，
//           省一份"每节点刷没刷过"的状态管理；1 卵/次已足以约束）。
//    GDD 的灵魂 / 技能重铸槽：待灵魂库与 SkillOverride 应用落地后开放。
// ============================================================================

using System;
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
    [UIPanel("Panel_Market", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
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
        [SerializeField] private WanXiang.Fusion.ContentCatalogSO _contentCatalog;  // 由生成器注入
        [SerializeField] private WanXiang.Battle.Presentation.SpriteCatalog _sprites;  // 同上

        private sealed class Good
        {
            public BeastDef Beast;
            public int Price;
            public bool Sold;
        }

        private readonly List<Good> _goods = new List<Good>();
        private int _refreshCount;
        private static readonly Color RowIdle = new Color(0.96f, 0.94f, 0.89f, 1f);

        protected override void OnCreate()
        {
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(OnRefreshClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);   // 回到节点地图
            for (int i = 0; i < _goodsBtns.Length; i++)
            {
                var idx = i;
                if (_goodsBtns[i] != null) _goodsBtns[i].onClick.AddListener(() => OnGoodsClicked(idx));
            }
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Reroll(free: true);      // 每次进灵市先免费摇一批（之后刷新收费）
            return UniTask.CompletedTask;
        }

        // ================================================================
        //  摇货架
        // ================================================================

        private void Reroll(bool free)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            if (!free)
            {
                if (run.Eggs < 1)
                {
                    if (_tmpHint != null) _tmpHint.text = "灵卵不足，刷不动货架了";
                    return;
                }
                run.Eggs -= 1;
                _refreshCount++;
            }

            var all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
            if (all == null || all.Length == 0)
            {
                if (_tmpHint != null) _tmpHint.text = "（内容目录缺失，货架空）";
                return;
            }

            ulong seed = CoreMath.Fnv1a("market:" + run.RunSeed + ":" + run.Act + ":" + _refreshCount);
            var rng = new DeterministicRandom(seed);

            _goods.Clear();
            var used = new HashSet<string>();
            int guard = 0;
            while (_goods.Count < 4 && guard++ < 40)
            {
                var b = all[rng.NextInt(0, all.Length)];
                if (used.Contains(b.Id)) continue;
                used.Add(b.Id);
                // 价格随稀有度与幕数上浮（GDD：价格随幕数与劫数上浮）
                int baseP = b.Rarity == Rarity.Rare ? 8 : (b.Rarity == Rarity.Epic ? 12 : 5);
                _goods.Add(new Good
                {
                    Beast = b,
                    Price = System.Math.Max(2, (int)(baseP * (1f + (run.Act - 1) * 0.3f))),
                });
            }

            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
            if (_tmpRefreshCost != null) _tmpRefreshCost.text = "刷新（1 灵卵）";
            Paint();
            WanXiang.Run.RunSave.SaveCurrent();
        }

        private void Paint()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (_goodsBtns == null) return;

            for (int i = 0; i < _goodsBtns.Length; i++)
            {
                bool has = i < _goods.Count;
                if (_goodsBtns[i] != null) _goodsBtns[i].gameObject.SetActive(has);
                if (!has) continue;

                var g = _goods[i];
                if (_imgGoodsHead != null && i < _imgGoodsHead.Length && _imgGoodsHead[i] != null && _sprites != null)
                {
                    var head = _sprites.GetHead(g.Beast.Id);
                    if (head != null) { _imgGoodsHead[i].sprite = head; _imgGoodsHead[i].color = Color.white; }
                }
                if (_tmpGoodsPrice != null && i < _tmpGoodsPrice.Length && _tmpGoodsPrice[i] != null)
                    _tmpGoodsPrice[i].text = g.Sold ? "已售出" : g.Price + " 灵卵";
                if (_imgGoodsSold != null && i < _imgGoodsSold.Length && _imgGoodsSold[i] != null)
                    _imgGoodsSold[i].gameObject.SetActive(g.Sold);
            }

            if (_tmpHint != null)
                _tmpHint.text = run != null && run.Team != null && run.Team.Count >= 5
                    ? "队伍已满（5 只）—— 买到的异兽会暂存图鉴（后续开放替换）"
                    : "点商品用灵卵购买，买到的异兽直接加入队伍";
        }

        // ================================================================
        //  购买 / 刷新
        // ================================================================

        private void OnRefreshClicked()
        {
            Reroll(free: false);
        }

        private void OnGoodsClicked(int index)
        {
            if (index < 0 || index >= _goods.Count) return;
            var g = _goods[index];
            if (g.Sold) return;

            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            if (run.Eggs < g.Price)
            {
                if (_tmpHint != null) _tmpHint.text = "灵卵不够（需要 " + g.Price + "）";
                return;
            }

            if (run.Team == null) run.Team = new System.Collections.Generic.List<string>();
            if (run.Team.Count >= 5)
            {
                if (_tmpHint != null) _tmpHint.text = "队伍已满（5 只），买不下这只了";
                return;
            }

            run.Eggs -= g.Price;
            run.Team.Add(g.Beast.Id);
            g.Sold = true;
            WanXiang.Run.RunSave.SaveCurrent();

            if (_tmpHint != null) _tmpHint.text = g.Beast.DisplayName + " 加入了队伍！";
            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
            Paint();
        }
    }
}
