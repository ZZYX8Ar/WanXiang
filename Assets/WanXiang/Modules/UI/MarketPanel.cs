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

        // ---- 详情卡（由 Panel_Market.prefab 注入；生成器见 Editor/UITool/UIBuildMarketPatch.cs）----
        [SerializeField] private RectTransform _detailRoot;    // Market_DetailMask（整体显示/隐藏）
        [SerializeField] private Image _detailBig;             // Img_Big
        [SerializeField] private TMP_Text _detailName;         // Tmp_Name
        [SerializeField] private TMP_Text _detailInfo;         // Tmp_Info
        [SerializeField] private TMP_Text _detailSource;       // Tmp_Source
        [SerializeField] private TMP_Text _detailPrice;        // Tmp_Price
        [SerializeField] private Button _detailBuy;            // Btn_Buy
        [SerializeField] private Button _detailClose;          // Btn_Close

        private sealed class Good
        {
            public BeastDef Beast;
            public int Price;
            public bool Sold;
            /// <summary>★ 魂商品（用户 2026-09-28：买魂别单独按钮+弹窗，要像异兽一样摆货架）。</summary>
            public bool IsSoul;
            /// <summary>魄名稳定序号（= 来源异兽在内容表中的下标），同一只魂每次导出同一个魄名。</summary>
            public int SoulOrdinal;
        }

        private readonly List<Good> _goods = new List<Good>();
        private int _refreshCount;
        private static readonly Color RowIdle = new Color(0.96f, 0.94f, 0.89f, 1f);

        protected override void OnCreate()
        {
            if (_btnRefresh != null) _btnRefresh.onClick.AddListener(OnRefreshClicked);
            // ★ 魂已改走货架（用户 2026-09-28）：与异兽同一套 商品→详情卡→购买→售罄印 流程。
            //   旧的「买魂」按钮 + 魂选单（Btn_BuySoul / Market_SoulPicker）已从 prefab 与代码中移除。
            if (_detailBuy != null) _detailBuy.onClick.AddListener(OnBuyClicked);
            if (_detailClose != null) _detailClose.onClick.AddListener(HideDetail);
            if (_detailRoot != null) _detailRoot.gameObject.SetActive(false);   // 默认隐藏
            EnsureBackButton();
            if (_btnBack != null) _btnBack.onClick.AddListener(() => {
                CloseSelf();
                var __ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                if (__ui != null) Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                    await __ui.OpenAsync<CampaignPanel>();
                });
            });   // 回到节点地图（继续探索）
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
            // ★ 商店只卖你"还没有"的异兽：排除已拥有（图鉴/队伍），
            //   阵亡失去的异兽自然重新出现在货架上，可花钱买回（异兽唯一、可重购）。
            var owned = new HashSet<string>();
            if (run.Collection != null) foreach (var id in run.Collection) owned.Add(id);
            if (run.Team != null) foreach (var id in run.Team) owned.Add(id);

            // ★ 缺的优先（用户 2026-10-06）：本局**曾经拥有、后来阵亡失去**的兽加权上架。
            //   口径 = 开局快照 SnapBeastIds 里有、但现在不在 Collection∪Team 里的 ——
            //   这正是"阵亡丢掉的"，不必另立 LostThisRun 名单（那套已被推翻）。
            var lost = new List<BeastDef>();
            if (run.SnapBeastIds != null)
            {
                var snap = new HashSet<string>();
                foreach (var id in run.SnapBeastIds) snap.Add(id);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && snap.Contains(all[i].Id) && !owned.Contains(all[i].Id))
                        lost.Add(all[i]);
            }

            int guard = 0;
            while (_goods.Count < 4 && guard++ < 40)
            {
                // 一半概率从"你丢掉的"里摇 —— 想买回阵亡兽就不用刷那么多次
                var b = (lost.Count > 0 && rng.NextInt(0, 2) == 0)
                    ? lost[rng.NextInt(0, lost.Count)]
                    : all[rng.NextInt(0, all.Length)];
                if (used.Contains(b.Id) || owned.Contains(b.Id)) continue;
                used.Add(b.Id);
                // 价格随稀有度与幕数上浮（GDD：价格随幕数与劫数上浮）
                int baseP = b.Rarity == Rarity.Rare ? 8 : (b.Rarity == Rarity.Epic ? 12 : 5);
                // ★ 天气地图效果层：当前节点天时影响物价（LiveWeather.Current 为 null ⇒ ×1，旧行为）。
                float priceMul = WanXiang.Campaign.WeatherMapEffects.Current.PriceMul;
                _goods.Add(new Good
                {
                    Beast = b,
                    Price = System.Math.Max(2, (int)(baseP * (1f + (run.Act - 1) * 0.3f) * priceMul)),
                });
            }

            // ★ 魂上架（用户 2026-09-28）：货架共 6 格、异兽占 4，剩余格摆魂。
            //   与异兽同一套 货架→详情卡→购买→售罄印 流程；同一次摇货不重复同一来源。
            float soulMul = WanXiang.Campaign.WeatherMapEffects.Current.PriceMul;
            int soulGuard = 0;
            var soulUsed = new HashSet<string>();
            while (_goods.Count < _goodsBtns.Length && soulGuard++ < 40)
            {
                var b = all[rng.NextInt(0, all.Length)];
                if (soulUsed.Contains(b.Id)) continue;
                soulUsed.Add(b.Id);
                _goods.Add(new Good
                {
                    Beast = b,
                    Price = System.Math.Max(2, (int)(SoulPrice * (1f + (run.Act - 1) * 0.3f) * soulMul)),
                    IsSoul = true,
                    SoulOrdinal = System.Array.IndexOf(all, b),
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
                    if (head != null)
                    {
                        _imgGoodsHead[i].sprite = head;
                        // 魂用淡青色调与异兽区分（同一个头像底图）
                        _imgGoodsHead[i].color = g.IsSoul ? new Color(0.78f, 0.9f, 1f, 1f) : Color.white;
                    }
                }
                if (_tmpGoodsPrice != null && i < _tmpGoodsPrice.Length && _tmpGoodsPrice[i] != null)
                    _tmpGoodsPrice[i].text = g.Sold ? "已售出" : (g.IsSoul ? "魂 " : "") + g.Price + " 灵卵";
                if (_imgGoodsSold != null && i < _imgGoodsSold.Length && _imgGoodsSold[i] != null)
                    _imgGoodsSold[i].gameObject.SetActive(g.Sold);
            }

            if (_tmpHint != null)
                // ★ 澄清"拥有"与"上阵"的区别（用户疑问：不是可以超过 5 只吗？）
                //   拥有：不设上限（都进图鉴/收藏）；上阵：最多 5 只，在编阵界面挑选。
                _tmpHint.text = (run != null && run.Team != null && run.Team.Count >= 5)
                    ? "当前拥有 " + (run.Collection != null ? run.Collection.Count : 0) +
                      " 只（不设上限）｜出战时最多上阵 5 只 —— 在编阵界面调整阵容"
                    : "点商品购买：异兽直接入队 · 魂进背包（铸魂台融合用）";
        }

        // ================================================================
        //  购买 / 刷新
        // ================================================================

        private void OnRefreshClicked()
        {
            Reroll(free: false);
        }

        /// <summary>点货架 = 打开【详情卡】（用户要求：不能一点就买，先看介绍再决定）。</summary>
        private void OnGoodsClicked(int index)
        {
            if (index < 0 || index >= _goods.Count) return;
            if (_goods[index].Sold) return;          // 已售出：不弹
            ShowDetail(index);
        }

        // ================================================================
        //  详情卡（代码自建，不动 prefab）：立绘 / 名字 / 五行·定位·稀有度 /
        //  出处 / 价格 + 「购买」「关闭」。购买只作用于当前选中的这只。
        // ================================================================
        private int _detailIndex = -1;    // 当前详情卡指向的货架下标（字段已改为 prefab 注入）


        /// <summary>魂的基准价（灵卵）；实际上架价随幕数与天时物价浮动（见 Reroll）。</summary>
        private const int SoulPrice = 5;

        private void SetHint(string msg)
        {
            if (_tmpHint != null) _tmpHint.text = msg;
        }


        private void ShowDetail(int index)
        {
            var g = _goods[index];
            _detailIndex = index;
            if (g.IsSoul) { ShowSoulDetail(g); return; }

            if (_detailBig != null)
            {
                _detailBig.sprite = _sprites != null ? _sprites.Get(_detailBig_BeastId(g)) : null;
                _detailBig.color = _detailBig.sprite != null ? Color.white : new Color(0.86f, 0.82f, 0.74f, 1f);
            }
            if (_detailName != null) _detailName.text = g.Beast.DisplayName;

            // ★ 详情只讲"战斗相关"（用户要求）：属性 / 推荐站位（前后排）/ 技能 / 特性。
            //   原来的典籍原文对玩家没有决策价值，已去掉（最长一段大段古文占了大半张卡）。
            if (_detailInfo != null)
                _detailInfo.text = Cn.Of(g.Beast.Element) + " · " + Cn.Of(g.Beast.Role) +
                                    " · " + RarityCn(g.Beast.Rarity);

            if (_detailSource != null)
            {
                var b = new System.Text.StringBuilder();
                b.Append("推荐站位：").Append(RowHint(g.Beast.Role));
                b.Append("\n技能：");
                if (g.Beast.Basic != null) b.Append(g.Beast.Basic.Name).Append("（基础）");
                if (g.Beast.Active != null) b.Append("　").Append(g.Beast.Active.Name).Append("（主动）");
                if (g.Beast.Ultimate != null) b.Append("　").Append(g.Beast.Ultimate.Name).Append("（奥义）");
                if (!string.IsNullOrEmpty(g.Beast.Trait.Name))
                    b.Append("\n特性：").Append(g.Beast.Trait.Name);
                _detailSource.text = b.ToString();
            }
            if (_detailPrice != null) _detailPrice.text = "价格：" + g.Price + " 灵卵";
            if (_detailBuy != null)
            {
                var run = WanXiang.Run.RunSave.Current;
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

        /// <summary>魂商品的详情卡：与异兽共用同一张卡（用户 2026-09-28：别单独弹窗）。</summary>
        private void ShowSoulDetail(Good g)
        {
            var soul = WanXiang.Fusion.SoulForge.Derive(g.Beast, g.SoulOrdinal);
            if (_detailBig != null)
            {
                _detailBig.sprite = _sprites != null ? _sprites.Get(g.Beast.Id) : null;
                _detailBig.color = _detailBig.sprite != null
                    ? new Color(0.78f, 0.9f, 1f, 1f)          // 与货架一致：魂=淡青色调
                    : new Color(0.86f, 0.82f, 0.74f, 1f);
            }
            if (_detailName != null)
                _detailName.text = soul.DisplayName + " ·「" + soul.Epithet + "」";
            if (_detailInfo != null)
                _detailInfo.text = "魂 · " + Cn.Of(g.Beast.Element) + " · " + RarityCn(g.Beast.Rarity) + " · 融合材料";
            if (_detailSource != null)
            {
                var b = new System.Text.StringBuilder();
                b.Append("来源：").Append(g.Beast.DisplayName).Append("之魂（铸魂台融合用）");
                b.Append("\n融合时改写宿主外观与特性");
                // ⚠ TraitDef 是 struct（同 StatusDef 的坑）：不能 != null，直接取字段
                if (!string.IsNullOrEmpty(soul.TraitInjection.Name))
                    b.Append("\n注入特性：").Append(soul.TraitInjection.Name)
                     .Append("（").Append(soul.TraitInjection.Description).Append("）");
                _detailSource.text = b.ToString();
            }
            if (_detailPrice != null) _detailPrice.text = "价格：" + g.Price + " 灵卵";
            if (_detailBuy != null)
            {
                var run = WanXiang.Run.RunSave.Current;
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

        /// <summary>推荐站位（用户要的"前后排"提示）：由定位推导，与战斗的 RanksCloser 一致。</summary>
        private static string RowHint(WanXiang.Battle.Core.RoleType role)
        {
            switch (role)
            {
                case WanXiang.Battle.Core.RoleType.Guard:   return "前排（御：血厚攻低，优先承伤）";
                case WanXiang.Battle.Core.RoleType.Striker: return "后排（攻：脆但爆发）";
                case WanXiang.Battle.Core.RoleType.Swift:   return "后排（疾：依赖先手）";
                case WanXiang.Battle.Core.RoleType.Caster:  return "中排（术：输出主力）";
                case WanXiang.Battle.Core.RoleType.Support: return "中排（辅：增益治疗）";
                default: return "任意";
            }
        }

        private static string RarityCn(WanXiang.Battle.Core.Rarity r)
        {
            switch (r.ToString())
            {
                case "Common": return "凡";
                case "Rare": return "珍";
                case "Epic": return "史诗";
                case "Legend": return "传说";
                case "Legendary": return "传说";
                default: return r.ToString();
            }
        }

        private string _detailBig_BeastId(Good g) => g.Beast != null ? g.Beast.Id : "";

        private void HideDetail()
        {
            _detailIndex = -1;
            if (_detailRoot != null) _detailRoot.gameObject.SetActive(false);
        }

        /// <summary>购买（只买详情卡里当前选中的那只）。</summary>
        private void OnBuyClicked()
        {
            int index = _detailIndex;
            if (index < 0 || index >= _goods.Count) return;
            var g = _goods[index];
            if (g.Sold) { HideDetail(); return; }

            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (run.Eggs < g.Price)
            {
                if (_tmpHint != null) _tmpHint.text = "灵卵不够（需要 " + g.Price + "）";
                return;
            }

            // ★ 魂购买（用户 2026-09-28）：与异兽同一入口，买成功盖「已售出」印 + 底部提示。
            if (g.IsSoul)
            {
                run.Eggs -= g.Price;
                if (run.Souls == null) run.Souls = new System.Collections.Generic.List<string>();
                run.Souls.Add(g.Beast.Id);
                g.Sold = true;
                WanXiang.Run.RunSave.SaveCurrent();
                if (_tmpHint != null)
                    _tmpHint.text = "已购入 " + g.Beast.DisplayName + " 之魂（现有 " + run.Souls.Count +
                                    " 个）—— 铸魂台可用";
                if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
                HideDetail();
                Paint();
                UnityEngine.Debug.Log("[Market] 货架买魂 " + g.Beast.Id + "（现有 " + run.Souls.Count + "）");
                return;
            }

            if (run.Collection == null) run.Collection = new System.Collections.Generic.List<string>();

            run.Eggs -= g.Price;
            if (run.Collection == null) run.Collection = new System.Collections.Generic.List<string>();
            if (!run.Collection.Contains(g.Beast.Id)) run.Collection.Add(g.Beast.Id);  // ★ 异兽唯一，不重复购买
            g.Sold = true;
            WanXiang.Run.RunSave.SaveCurrent();

            if (_tmpHint != null)
                _tmpHint.text = g.Beast.DisplayName + " 已收入图鉴（当前拥有 " + run.Collection.Count +
                                " 只）——  出战最多 5 只，在编阵界面调整";
            if (_tmpEggs != null) _tmpEggs.text = "灵卵 " + run.Eggs;
            HideDetail();
            Paint();
        }

        /// <summary>
        /// 返回地图按钮的兜底创建：prefab 里没有（防覆盖后新控件不落盘）时也要能返回，
        /// 否则玩家进了灵市就出不去了。同 EnsureAutoSpin 模式。
        /// </summary>
        private void EnsureBackButton()
        {
            if (_btnBack != null) return;

            var go = new GameObject("Btn_Back", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.94f, 0.92f, 0.88f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(96f, -52f);
            r.sizeDelta = new Vector2(152f, 64f);

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = "返回地图";
            tmp.fontSize = 26;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            _btnBack = btn;
            btn.onClick.AddListener(() =>
            {
                CampaignPanel.PendingCommit = -1;      // 返回 = 未完成，节点不通过
                CloseSelf();
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                    await ui.OpenAsync<CampaignPanel>();
                });
            });
        }

    }
}
