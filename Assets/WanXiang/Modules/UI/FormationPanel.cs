// ============================================================================
//  Panel_Formation —— 布阵界面（战斗前，全作最重要的决策界面）
//  ---------------------------------------------------------------------------
//  结构验证版做到：
//    · 展示关卡名 / 天时预警 / 敌方情报 / 羁绊栏 / 九宫格 / 卡池 / 总战力
//    · 一键布阵（按战力把卡池前 5 只放进推荐站位）
//    · 清空
//    · 出征 → 组队并打开 Panel_Battle
//  拖拽上阵留给下一步（需要 IDragHandler 与相生相邻的实时判定）。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Formation", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class FormationPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpNodeTitle;       // Tmp_NodeTitle
        [SerializeField] private TMP_Text _tmpWeatherWarn;     // Tmp_WeatherWarn
        [BindArray("Img_EnemyAvatar_{0}", 5)]
        [SerializeField] private Image[] _imgEnemies;          // 敌方阵容缩略
        [SerializeField] private TMP_Text _tmpEnemyPower;      // Tmp_EnemyPower
        [SerializeField] private Image _imgWeather;            // Img_WeatherIcon
        [BindArray("Img_Bond_{0}", 8)]
        [SerializeField] private Image[] _imgBonds;            // 羁绊图标
        [BindArray("Tmp_BondName_{0}", 8)]
        [SerializeField] private TMP_Text[] _tmpBonds;         // 羁绊名
        [BindArray("Cell_{0}", 9)]
        [SerializeField] private RectTransform[] _cells;       // 九宫格 0~8
        [SerializeField] private Image _imgCellHi;             // Img_CellHighlight
        [SerializeField] private ScrollRect _scrollRoster;     // Scroll_Roster
        [SerializeField] private RectTransform _rosterItemTemplate;  // Item_Roster（模板）
        [SerializeField] private TMP_Text _tmpPower;           // Tmp_TotalPower
        [SerializeField] private Button _btnAutoFill;          // Btn_AutoFill
        [SerializeField] private Button _btnClear;             // Btn_Clear
        [SerializeField] private Button _btnDeploy;            // Btn_Deploy

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        /// <summary>推荐站位：前排一侧 → 中宫 → 后排两侧（与 GDD 的站位纪律一致）。</summary>
        private static readonly int[] Slots = { 0, 1, 4, 7, 8 };

        private NodeRequest _node = new NodeRequest();
        private BeastDef[] _all;
        private readonly int[] _deployed = new int[9];   // cell → beast index（-1 = 空）

        protected override void OnCreate()
        {
            if (_imgCellHi != null) _imgCellHi.gameObject.SetActive(false);
            if (_btnAutoFill != null) _btnAutoFill.onClick.AddListener(OnAutoFillClicked);
            if (_btnClear != null) _btnClear.onClick.AddListener(OnClearClicked);
            if (_btnDeploy != null) _btnDeploy.onClick.AddListener(OnDeployClicked);
            ClearSlots();
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            _node = payload as NodeRequest ?? _node;
            _all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;

            if (_tmpNodeTitle != null) _tmpNodeTitle.text = _node.Title;
            if (_tmpWeatherWarn != null) _tmpWeatherWarn.text = _node.Weather;

            FillEnemyIntel();
            BuildRoster();
            RefreshBonds();
            OnAutoFillClicked();     // 默认先摆好一套，玩家再微调
            return UniTask.CompletedTask;
        }

        // ================================================================
        //  敌方情报
        // ================================================================

        private void FillEnemyIntel()
        {
            if (_all == null) return;
            int half = System.Math.Max(1, _all.Length / 2);
            for (int i = 0; i < _imgEnemies.Length; i++)
            {
                if (_imgEnemies[i] == null) continue;
                int idx = System.Math.Min(half + i, _all.Length - 1);
                var sprite = _sprites != null ? _sprites.Get(_all[idx].Id) : null;
                if (sprite != null)
                {
                    _imgEnemies[i].sprite = sprite;
                    _imgEnemies[i].color = new Color(0.72f, 0.72f, 0.80f, 1f);   // 敌方压暗
                    _imgEnemies[i].preserveAspect = true;
                }
            }
            if (_tmpEnemyPower != null)
                _tmpEnemyPower.text = "敌方强度 BP 5.35";
        }

        // ================================================================
        //  卡池
        // ================================================================

        private void BuildRoster()
        {
            if (_scrollRoster == null || _rosterItemTemplate == null || _all == null) return;

            var content = _scrollRoster.content;
            if (content == null) return;

            // 清掉上一轮的项（保留模板本体）
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child == _rosterItemTemplate) continue;
                Destroy(child.gameObject);
            }

            for (int i = 0; i < _all.Length; i++)
            {
                var item = Instantiate(_rosterItemTemplate, content);
                item.name = "Item_Roster_" + i;
                item.gameObject.SetActive(true);

                var head = item.Find("Img_Head");
                if (head != null)
                {
                    var img = head.GetComponent<Image>();
                    var sprite = _sprites != null ? _sprites.Get(_all[i].Id) : null;
                    if (img != null && sprite != null)
                    {
                        img.sprite = sprite;
                        img.color = Color.white;
                        img.preserveAspect = true;
                    }
                }
                var nameRt = item.Find("Tmp_Name");
                if (nameRt != null)
                {
                    var t = nameRt.GetComponent<TMP_Text>();
                    if (t != null)
                        t.text = _all[i].DisplayName + "　" + Cn.Of(_all[i].Element) + Cn.Of(_all[i].Role);
                }
                var elem = item.Find("Img_Element");
                if (elem != null)
                {
                    var t = elem.GetComponent<TMP_Text>();
                    if (t != null) t.text = "";
                }
            }
        }

        // ================================================================
        //  九宫格 + 羁绊
        // ================================================================

        private void ClearSlots()
        {
            for (int i = 0; i < _deployed.Length; i++) _deployed[i] = -1;
        }

        private void OnAutoFillClicked()
        {
            ClearSlots();
            if (_all == null) return;
            int n = Mathf.Min(Slots.Length, _all.Length, 5);
            for (int i = 0; i < n; i++) _deployed[Slots[i]] = i;
            RefreshCells();
            RefreshBonds();
        }

        private void OnClearClicked()
        {
            ClearSlots();
            RefreshCells();
            RefreshBonds();
        }

        /// <summary>把上阵的立绘落到格子上（文字/头像由 Img_Head 之类承担，这里直接画立绘）。</summary>
        private void RefreshCells()
        {
            if (_cells == null) return;
            for (int cell = 0; cell < _cells.Length && cell < _deployed.Length; cell++)
            {
                var rt = _cells[cell];
                if (rt == null) continue;

                var existing = rt.Find("Img_Unit");
                if (existing != null) Destroy(existing.gameObject);

                int idx = _deployed[cell];
                if (idx < 0 || _all == null || idx >= _all.Length) continue;

                var go = new GameObject("Img_Unit", typeof(RectTransform));
                var crt = (RectTransform)go.transform;
                crt.SetParent(rt, false);
                crt.anchorMin = Vector2.zero;
                crt.anchorMax = Vector2.one;
                crt.offsetMin = new Vector2(6f, 6f);
                crt.offsetMax = new Vector2(-6f, -6f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                var sprite = _sprites != null ? _sprites.Get(_all[idx].Id) : null;
                if (sprite != null) img.sprite = sprite;
            }
        }

        /// <summary>
        /// 结构验证版的羁绊栏：同属共鸣按上阵数量点亮 2/4/5 档，相生羁绊按"相邻格是否成对"点亮。
        /// 与 GDD 第 2 章的规则同源，但这里只做显示，真正的战斗加成交给 BattleSimulator。
        /// </summary>
        private void RefreshBonds()
        {
            if (_imgBonds == null) return;

            int deployedCount = 0;
            var sameElement = new int[6];
            for (int i = 0; i < _deployed.Length; i++)
            {
                int idx = _deployed[i];
                if (idx < 0 || _all == null) continue;
                deployedCount++;
                sameElement[(int)_all[idx].Element]++;
            }

            int maxSame = 0;
            for (int e = 1; e < 6; e++) maxSame = Mathf.Max(maxSame, sameElement[e]);

            // 相生 5 条（木火 / 火土 / 土金 / 金水 / 水木）：棋盘上是否存在相邻的一对
            var pairs = new[,]
            {
                { 0, 1 }, { 1, 4 }, { 4, 3 }, { 8, 2 }, { 2, 0 },   // 0..8 格的相邻对（正交）
            };
            var bondOn = new bool[5];
            int[] order = { 1, 2, 3, 4, 5 };                        // 木火土金水
            for (int b = 0; b < 5; b++)
            {
                int a = order[b], c = order[(b + 1) % 5];
                for (int p = 0; p < pairs.GetLength(0); p++)
                {
                    int c1 = pairs[p, 0], c2 = pairs[p, 1];
                    if (!CellHasElement(c1, a) || !CellHasElement(c2, c)) continue;
                    bondOn[b] = true;
                    break;
                }
            }

            for (int i = 0; i < _imgBonds.Length; i++)
            {
                if (_imgBonds[i] == null) continue;
                bool on;
                string label;
                if (i < 5)
                {
                    on = bondOn[i];
                    label = Cn.Of((Element)order[i]) + Cn.Of((Element)order[(i + 1) % 5]);
                }
                else
                {
                    int tier = i - 5;                     // 0→2 环, 1→4 环, 2→5 环
                    int need = tier == 0 ? 2 : (tier == 1 ? 4 : 5);
                    on = maxSame >= need;
                    label = "共鸣 " + need;
                }
                _imgBonds[i].color = on ? Color.white : new Color(0.38f, 0.36f, 0.33f, 0.45f);
                if (_tmpBonds != null && i < _tmpBonds.Length && _tmpBonds[i] != null)
                    _tmpBonds[i].text = label;
            }

            if (_tmpPower != null)
            {
                int power = 0;
                for (int i = 0; i < _deployed.Length; i++)
                {
                    int idx = _deployed[i];
                    if (idx < 0 || _all == null) continue;
                    power += 1100 + idx * 37;      // 结构验证版的假战力，接真实面板后替换
                }
                _tmpPower.text = "总战力 " + power;
            }
        }

        private bool CellHasElement(int cell, int element)
        {
            if (cell < 0 || cell >= _deployed.Length) return false;
            int idx = _deployed[cell];
            if (idx < 0 || _all == null || idx >= _all.Length) return false;
            return (int)_all[idx].Element == element;
        }

        // ================================================================
        //  出征
        // ================================================================

        private void OnDeployClicked()
        {
            bool any = false;
            for (int i = 0; i < _deployed.Length; i++) if (_deployed[i] >= 0) { any = true; break; }
            if (!any)
            {
                Debug.LogWarning("[FormationPanel] 至少上阵 1 只异兽才能出征。");
                return;
            }

            BattleRequest req;
            if (!BattleRequestFactory.TryBuild(_contentCatalog, _node.Title, _node.Weather,
                                              _node.Seed, out req))
            {
                Debug.LogError("[FormationPanel] 组队失败，无法进入战斗。");
                return;
            }

            // 用玩家真实布阵替换工厂给的默认队形
            req.Player.Clear();
            for (int cell = 0; cell < _deployed.Length; cell++)
            {
                int idx = _deployed[cell];
                if (idx < 0 || _all == null || idx >= _all.Length) continue;
                req.Player.Add(_all[idx]);
            }

            // 出征 = 切到战斗场景。关掉布阵界面：布阵是 Normal 层，
            // 战斗 HUD 在 Main 层，留着会被布阵的遮罩压住。
            CloseSelf();
            SceneFlow.EnterBattle(req);
        }
    }
}
