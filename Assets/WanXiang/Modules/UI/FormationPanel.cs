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
using UnityEngine.EventSystems;
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
        [SerializeField] private Button _btnBack;              // Btn_Back（兜底创建）
        [SerializeField] private Button _btnClear;             // Btn_Clear
        /// <summary>出战上限（GDD：一队 5 只）。上阵/拖放共用这个值，别再各处硬编码。</summary>
        private const int MaxDeploy = 5;
        [SerializeField] private Button _btnDeploy;            // Btn_Deploy

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        /// <summary>推荐站位：前排一侧 → 中宫 → 后排两侧（与 GDD 的站位纪律一致）。</summary>
        private static readonly int[] Slots = { 0, 1, 4, 7, 8 };

        private NodeRequest _node = new NodeRequest();
        private BeastDef[] _all;
        private readonly int[] _deployed = new int[9];   // cell → beast index（-1 = 空）

        // ---- 拖拽状态 ----
        // ---- 羁绊 / 中宫 悬浮说明（用户要"给一个说明"：共鸣、五行、中宫）----
        private RectTransform _bondTipPanel;
        private CanvasGroup _bondTipGroup;
        private TMP_Text _bondTipText;
        private int _bondTipSlot = -1;
        private GameObject _ghost;          // 拖拽幽灵（跟随指针的立绘）
        private int _dragBeast = -1;        // 从卡池拖出来的异兽序号（-1 = 不是）
        private int _dragFromCell = -1;     // 从哪个格子拖出来的（-1 = 不是）

        // ---- 敌方情报（悬浮介绍）----
        private System.Collections.Generic.List<WanXiang.Battle.Core.BeastDef> _enemyDefs;
        /// <summary>本场我方总倍率（来自真实战斗请求 req.PlayerMul：含遗物 + 孵穴/天象等）。
        /// ★ 原战力只按裸面板算，漏了这层 ， 玩家看到的战力比实战低一大截（用户 2026-10-12 报障）。</summary>
        private float _relicPlayerMul = 1f;

        protected override void OnCreate()
        {
            if (_imgCellHi != null) _imgCellHi.gameObject.SetActive(false);
            if (_btnAutoFill != null) _btnAutoFill.onClick.AddListener(OnAutoFillClicked);
            if (_btnClear != null) _btnClear.onClick.AddListener(OnClearClicked);
            if (_btnDeploy != null) _btnDeploy.onClick.AddListener(OnDeployClicked);
            ClearSlots();
            WireCells();
            BuildBondTip();      // 羁绊/中宫悬浮说明（prefab Root_BondTip）
            WireBondTips();
            WireCenterTip();
            WireWeatherTip();    // 右上角天时（含谷/生机等状态）说明
            WireEnemyTips();     // 敌方阵容悬浮介绍（名称/五行定位/四维/图鉴描述）
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            EnsureBackButton();
            _node = payload as NodeRequest ?? _node;
            var allBeasts = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
            // ★ 编阵只列"你已拥有的"：图鉴里买到的 + 初始队伍。
            //   之前摆出整本图鉴 —— 没买过的也能上阵，"拥有"就没意义了。
            var run = WanXiang.Run.RunSave.Current;
            if (allBeasts != null && run != null)
            {
                var ownedIds = new System.Collections.Generic.List<string>();
                if (run.Collection != null) ownedIds.AddRange(run.Collection);
                if (run.Team != null) ownedIds.AddRange(run.Team);      // 初始队伍也算拥有

                if (ownedIds.Count > 0)
                {
                    var byId = new System.Collections.Generic.Dictionary<string, BeastDef>();
                    foreach (var b in allBeasts) byId[b.Id] = b;
                    var owned = new System.Collections.Generic.List<BeastDef>();
                    foreach (var id in ownedIds)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        BeastDef d;
                        if (byId.TryGetValue(id, out d) && !owned.Contains(d)) owned.Add(d);
                    }
                    // 空图鉴兜底：一场都没买过时先给全图鉴，保证新档能开局
                    _all = owned.Count > 0 ? owned.ToArray() : allBeasts;
                }
                else
                {
                    _all = allBeasts;
                }
            }
            else
            {
                _all = allBeasts;      // 没存档（试玩/直接进编阵）→ 全图鉴
            }

            if (_tmpNodeTitle != null) _tmpNodeTitle.text = _node.Title;
            if (_tmpWeatherWarn != null) _tmpWeatherWarn.text = _node.Weather;

            FillEnemyIntel();
            BuildRoster();
            RefreshBonds();
            if (!TryInheritTeam())
                OnAutoFillClicked(); // 没有可继承的队伍 → 默认摆一套，玩家再微调
            return UniTask.CompletedTask;
        }

        // ================================================================
        //  敌方情报
        // ================================================================

        /// <summary>
        /// 敌方情报：★ 必须用**真实战斗请求**里的敌人。
        /// 旧实现拿"图鉴数组后半段"当预览、强度写死 5.35 ， 与实际开打的敌人完全不符（用户实测）。
        /// </summary>
        private void FillEnemyIntel()
        {
            if (_all == null) return;

            var run = WanXiang.Run.RunSave.Current;
            BattleRequest req;
            bool ok;
            // 预览不能消耗挂起修正（孵穴回血/天象加成）， 先记住、调完工厂再还原
            int hp = 0, pb = 0, eb = 0;
            if (run != null) { hp = run.HealPending; pb = run.PlayerBuffPct; eb = run.EnemyBuffPct; }

            ok = BuildRequestForNode(run, out req);
            if (run != null) { run.HealPending = hp; run.PlayerBuffPct = pb; run.EnemyBuffPct = eb; }
            if (!ok || req == null) return;

            var enemies = req.Enemy;
            _enemyDefs = enemies;
            _relicPlayerMul = req.PlayerMul > 0f ? req.PlayerMul : 1f;   // ★ 我方战力要乘它（含遗物）
            for (int i = 0; i < _imgEnemies.Length; i++)
            {
                if (_imgEnemies[i] == null) continue;
                if (i >= enemies.Count || enemies[i] == null)
                {
                    _imgEnemies[i].enabled = false;      // 敌人没那么多 ， 隐藏空位
                    continue;
                }
                var sprite = _sprites != null ? _sprites.GetHead(enemies[i].Id) : null;
                if (sprite != null)
                {
                    _imgEnemies[i].enabled = true;
                    _imgEnemies[i].sprite = sprite;
                    _imgEnemies[i].color = new Color(0.72f, 0.72f, 0.80f, 1f);   // 敌方压暗
                    _imgEnemies[i].preserveAspect = true;
                }
            }
            // 敌方总战力：与**我方/结算/历程共用同一份口径**（BattlePower）。
            // ⚠ 原来是 "敌方强度 BP 7.01"（EnemyBudget 的计价口径，是个"预算价格"不是战力）
            //   ， 玩家看不懂（用户实测报障）。改用战力：与上方我方数字同一把尺，可比。
            if (_tmpEnemyPower != null)
                _tmpEnemyPower.text = "敌方总战力 " + WanXiang.Battle.Core.BattlePower.Sum(enemies, req.EnemyMul)
                                    + "（" + enemies.Count + " 只）";
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

                // 挂拖拽/点击代理（点击 = 推荐位上阵；拖拽 = 拖到格子上阵）
                var drag = item.gameObject.GetComponent<RosterDragItem>();
                if (drag == null) drag = item.gameObject.AddComponent<RosterDragItem>();
                drag.Owner = this;
                drag.BeastIndex = i;

                var head = item.Find("Img_Head");
                if (head != null)
                {
                    var img = head.GetComponent<Image>();
                    // ★ 卡池项必须能接收点击/拖拽：图若被设成不接收射线，事件到不了代理组件
                    if (img != null) img.raycastTarget = true;
                    var sprite = _sprites != null ? _sprites.GetHead(_all[i].Id) : null;
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
                var sprite = _sprites != null ? _sprites.GetHead(_all[idx].Id) : null;
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
                // ⚠ 必须**同时挡上界**：`_all` 可能只是"已拥有"的子集（owned.ToArray()），
                //   而 _deployed 里的索引来自存档/上一次布局 ， 可能超出当前 _all.Length。
                //   这里原来只挡了 idx < 0，于是 `_all[idx]` 直接抛 IndexOutOfRange
                //   （实测：从战役点"下一关"打开编成面板必崩在 318 行）。
                if (_all == null || idx < 0 || idx >= _all.Length) continue;
                deployedCount++;
                int el = (int)_all[idx].Element;
                if (el >= 0 && el < sameElement.Length) sameElement[el]++;
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
                    // 与上面同一条不变量：索引可能超出当前 _all.Length
                    if (_all == null || idx < 0 || idx >= _all.Length) continue;
                    // ★ 真实战力（原来是 `1100 + idx*37` 的占位假值 ， 与敌方那串不可比，用户看不懂）
                    // ★ 再乘本场总倍率（遗物 + 加成）， 与实战/敌方同一把尺（用户 2026-10-12 报障）
                    power += WanXiang.Battle.Core.BattlePower.Of(_all[idx], _relicPlayerMul);
                }
                _tmpPower.text = "我方总战力 " + power
                               + (Mathf.Abs(_relicPlayerMul - 1f) > 0.005f ? "（含遗物/加成）" : "");
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
        //  羁绊 / 中宫 悬浮说明（自我解释，用户 2026-09-27 要求）
        // ================================================================

        /// <summary>
        /// 取悬浮说明面板：**优先用 prefab 里的 Root_BondTip**（铁律：UI 必须在 prefab 里看得见改得着），
        /// 找不到才运行时兜底并打日志提醒补 prefab。
        /// ⛔ 之前踩坑：运行时创建时用了 `AddComponent<TMP_Text>()` —— TMP_Text 是抽象类，
        ///   添加必失败返回 null ， 下一行 NRE 打断 OnCreate ， WireBondTips 没跑 ， 悬浮全灭（用户实测）。
        ///   兜底路径也必须用具体类 TextMeshProUGUI。
        /// </summary>
        private void BuildBondTip()
        {
            var found = transform.Find("Root_BondTip");
            if (found != null)
            {
                _bondTipPanel = (RectTransform)found;
                _bondTipGroup = found.GetComponent<CanvasGroup>();
                if (_bondTipGroup == null) _bondTipGroup = found.gameObject.AddComponent<CanvasGroup>();
                _bondTipText = found.GetComponentInChildren<TMP_Text>(true);
                found.gameObject.SetActive(false);
                Debug.Log("[Formation] 悬浮说明面板已接入 prefab（Root_BondTip） text=" + (_bondTipText != null) +
                          " group=" + (_bondTipGroup != null));
                return;
            }

            // ---- 兜底：prefab 里没有该控件时才运行时建（正常不应走到；打日志提醒补 prefab）----
            Debug.LogWarning("[Formation] Panel_Formation.prefab 缺 Root_BondTip，运行时兜底创建（请在 prefab 补上）");
            var go = new GameObject("Root_BondTip", typeof(RectTransform));
            _bondTipPanel = (RectTransform)go.transform;
            _bondTipPanel.SetParent(transform, false);
            _bondTipPanel.anchorMin = _bondTipPanel.anchorMax = new Vector2(0f, 0.5f);
            _bondTipPanel.pivot = new Vector2(0f, 0.5f);
            _bondTipPanel.sizeDelta = new Vector2(430f, 230f);
            _bondTipPanel.anchoredPosition = new Vector2(30f, 0f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.95f);

            _bondTipGroup = go.AddComponent<CanvasGroup>();
            _bondTipGroup.alpha = 0f;
            _bondTipGroup.blocksRaycasts = false;

            var trt = new GameObject("Tmp_BondTip", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_bondTipPanel, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 12f); trt.offsetMax = new Vector2(-16f, -12f);
            _bondTipText = trt.gameObject.AddComponent<TextMeshProUGUI>();
            _bondTipText.fontSize = 20;
            _bondTipText.color = new Color(0.97f, 0.94f, 0.88f, 1f);
            _bondTipText.alignment = TextAlignmentOptions.TopLeft;
            _bondTipText.raycastTarget = false;

            _bondTipPanel.gameObject.SetActive(false);
        }

        /// <summary>给 8 个羁绊徽章挂悬浮说明（共鸣 / 五行相生）。</summary>
        private void WireBondTips()
        {
            if (_imgBonds == null) return;
            int wired = 0;
            for (int i = 0; i < _imgBonds.Length; i++)
            {
                var img = _imgBonds[i];
                if (img == null) continue;
                img.raycastTarget = true;     // 保证悬浮能命中
                // ★ 名字文本浮在图标上方、TMP 默认 raycastTarget=true，会抢走射线 ， 图标收不到悬浮
                if (_tmpBonds != null && i < _tmpBonds.Length && _tmpBonds[i] != null)
                    _tmpBonds[i].raycastTarget = false;
                var trg = img.gameObject.GetComponent<EventTrigger>();
                if (trg == null) trg = img.gameObject.AddComponent<EventTrigger>();
                int slot = i;
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowBondTip(slot));
                trg.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => HideBondTip());
                trg.triggers.Add(exit);
                wired++;
            }
            Debug.Log("[Formation] 羁绊悬浮已挂接 " + wired + "/" + _imgBonds.Length + " 个徽章");
        }

        /// <summary>给中宫（中间格，cell 4）挂悬浮说明：中宫土德 / 平息相冲 / 连携须站中宫。</summary>
        private void WireCenterTip()
        {
            if (_cells == null || _cells.Length <= 4 || _cells[4] == null) return;
            var cellImg = _cells[4].GetComponent<Image>();
            if (cellImg != null) cellImg.raycastTarget = true;   // 格子必须有可命中 Graphic 才能收到悬浮
            var trg = _cells[4].gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _cells[4].gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowCenterTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideBondTip());
            trg.triggers.Add(exit);
        }

        /// <summary>给右上角天时行（Tmp_WeatherWarn）挂悬浮：本场天时效果 + 状态（谷/生机等）说明。</summary>
        private void WireWeatherTip()
        {
            if (_tmpWeatherWarn == null) return;
            _tmpWeatherWarn.raycastTarget = true;
            var trg = _tmpWeatherWarn.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _tmpWeatherWarn.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowWeatherTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideBondTip());
            trg.triggers.Add(exit);
        }

        /// <summary>
        /// 天时说明（含「谷/生机」等状态——状态说明拼在 WeatherDef.Describe 对应行后，56042ff 起）。
        /// </summary>
        private void ShowWeatherTip()
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == 91 && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = 91;
            string title, body;
            var w = WanXiang.Campaign.LiveWeather.Current;
            if (w != null)
            {
                title = "天时 · " + w.BuffName;
                body = w.Describe();
                if (string.IsNullOrEmpty(body)) body = "（本条天时没有数值效果）";
                body += "\n\n行内「——」后面是状态说明（谷=每层+1%全属性、生机=每层回血2%等）。\n星移余气也一并计入本场天时。";
            }
            else
            {
                title = "天时";
                body = "本场无天时（节点不在节气上或天气未翻译）。";
            }
            PresentBondTip("<size=24><b>" + title + "</b></size>\n" + body);
        }

        /// <summary>给敌方阵容缩略图挂悬浮：显示该敌人的介绍（名称 / 五行定位 / 四维 / 图鉴描述）。</summary>
        private void WireEnemyTips()
        {
            if (_imgEnemies == null) return;
            for (int i = 0; i < _imgEnemies.Length; i++)
            {
                var img = _imgEnemies[i];
                if (img == null) continue;
                img.raycastTarget = true;   // 缩略图必须能命中射线才能收到悬浮
                var trg = img.gameObject.GetComponent<EventTrigger>();
                if (trg == null) trg = img.gameObject.AddComponent<EventTrigger>();
                int slot = 200 + i;         // 200+ 避开羁绊(0..7)/中宫(90)/天时(91)
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => ShowEnemyTip(slot - 200));
                trg.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => HideBondTip());
                trg.triggers.Add(exit);
            }
        }

        /// <summary>
        /// 敌方单位介绍面板（走与羁绊/天时同一块 Root_BondTip）。
        /// ★ 内容只放**战斗相关**：四维 + （首领）机制介绍 / （异兽）四个技能。
        ///   用户 2026-10-12：不要图鉴典故文案（"共工的臣属…"），要的是属性 / 技能 / 辅助攻击这类能指导出战的。
        /// </summary>
        private void ShowEnemyTip(int i)
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            int slot = 200 + i;
            if (_bondTipSlot == slot && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = slot;

            var d = (_enemyDefs != null && i >= 0 && i < _enemyDefs.Count) ? _enemyDefs[i] : null;
            if (d == null) { HideBondTip(); return; }

            bool isBoss = WanXiang.Battle.Core.BossCatalog.Get(d.Id) != null;
            var sb = new System.Text.StringBuilder();
            sb.Append(Cn.Of(d.Element)).Append('属').Append(" · ").Append(Cn.Of(d.Role));
            if (isBoss) sb.Append(" · 首领");
            sb.Append('\n');
            sb.Append("生命 ").Append(d.BaseHp)
              .Append("　攻 ").Append(d.BaseAtk)
              .Append("　防 ").Append(d.BaseDef)
              .Append("　速 ").Append(d.BaseSpeed);

            if (isBoss)
            {
                sb.Append("\n\n【首领机制】\n");
                string mech = WanXiang.Battle.Core.BossCatalog.MechanicText(d.Id);
                sb.Append(string.IsNullOrEmpty(mech) ? "（暂无机制说明）" : mech);
            }
            else
            {
                sb.Append("\n\n【技能】");
                AppendSkillLine(sb, "普攻", d.Basic);
                AppendSkillLine(sb, "战技", d.Active);
                AppendSkillLine(sb, "绝技", d.Ultimate);
                AppendSkillLine(sb, "觉醒", d.Awaken);
            }

            PresentBondTip("<size=24><b>" + d.DisplayName + "</b></size>\n" + sb);
        }

        /// <summary>把一条技能的"名称 + 目标 + 效果"写进说明文本（目标优先取效果的目标，与战斗口径一致）。</summary>
        private static void AppendSkillLine(System.Text.StringBuilder sb, string slotName, WanXiang.Battle.Core.SkillDef sk)
        {
            if (sk == null) return;
            sb.Append("\n· ").Append(slotName).Append(' ').Append(sk.Name);
            var target = sk.PrimaryTarget;
            if (sk.Effects != null)
                for (int k = 0; k < sk.Effects.Length; k++)
                    if (sk.Effects[k].Target != WanXiang.Battle.Core.TargetSelector.Self)
                    { target = sk.Effects[k].Target; break; }
            string tgt = WanXiang.Battle.Core.SkillMath.TargetCn(target);
            if (!string.IsNullOrEmpty(tgt)) sb.Append(tgt);
            if (!string.IsNullOrEmpty(sk.Description)) sb.Append('：').Append(sk.Description);
        }

        private void ShowBondTip(int i)
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == i && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = i;

            string title, body;
            if (i >= 0 && i < 5)
            {
                var g = WanXiang.Battle.Core.ElementMatrix.GenerateRing;
                string a = Cn.Of(g[i]), c = Cn.Of(g[(i + 1) % 5]);
                title = "五行相生 · " + a + "生" + c;
                body = "五行：木火土金水。相生 = 前者生后者（" + a + "生" + c + "）。\n" +
                       "这一格亮起，说明你场上有相邻的「" + a + "属性 + " + c + "属性」一对异兽，触发相生羁绊增益。\n" +
                       "相生链：木生火、火生土、土生金、金生水、水生木。";
            }
            else
            {
                int tier = i - 5;
                int need = tier == 0 ? 2 : (tier == 1 ? 4 : 5);
                title = "同属共鸣 · " + need + " 只";
                body = "上阵同属性异兽达 " + need + " 只，全队攻击提升：\n" +
                       "2 只 +8%、4 只 +16%、5 只 +25%（同属 5 只时，该属性单位绝技冷却 -2）。\n" +
                       "每回合开局自动结算，数值实时显示在面板右侧。\n\n" +
                       "（共鸣技 = 同属 4 只解锁的专属合击，本版本尚未开放。）";
            }
            PresentBondTip("<size=24><b>" + title + "</b></size>\n" + body);
        }

        /// <summary>当前站在中宫（格 4）的那只异兽；没上阵返回 null。</summary>
        private WanXiang.Battle.Core.BeastDef CenterBeast()
        {
            if (_all == null || _deployed == null || _deployed.Length <= 4) return null;
            int idx = _deployed[4];
            return (idx >= 0 && idx < _all.Length) ? _all[idx] : null;
        }

        private void ShowCenterTip()
        {
            if (_bondTipPanel == null || _bondTipText == null) return;
            if (_bondTipSlot == 90 && _bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = 90;
            var sb = new System.Text.StringBuilder();
            sb.Append("中宫有特殊效果：\n");
            sb.Append("· 中宫土德：站中间的异兽受到伤害 -8%（全属性减伤）。\n");
            sb.Append("· 平息相冲：若相冲（相克）涉及中宫，该次相冲被化解。\n");
            sb.Append("· 连携技必须由站中宫的主兽发动。\n");
            sb.Append("所以把主兽放进中间格收益最大。");

            // ★ 只列**当前站中宫那只**能发动的连携（用户 2026-10-12：原来把 50 条全列出来太乱）
            var center = CenterBeast();
            sb.Append("\n\n【当前中宫连携】");
            if (center == null)
            {
                sb.Append("\n（中宫还没有异兽 —— 拖一只主兽进来）");
            }
            else
            {
                int found = 0;
                foreach (var c in WanXiang.Battle.Core.ComboRules.All)
                {
                    if (c == null || c.HostId != center.Id) continue;
                    found++;
                    sb.Append("\n· ").Append(c.Name).Append("：").Append(c.Note)
                      .Append("（倍率 ").Append((c.Power * 100f).ToString("0")).Append("%）");
                }
                if (found == 0)
                    sb.Append("\n（").Append(center.DisplayName)
                      .Append(" 没有连携技。连携主兽为句芒 / 祝融 / 蓐收等）");
            }
            PresentBondTip("<size=24><b>九宫格中宫（中间格）</b></size>\n" + sb);
        }

        private void PresentBondTip(string text)
        {
            _bondTipPanel.gameObject.SetActive(true);
            _bondTipText.text = text;
            // ★ 高度自适应：首领机制/技能列表可能很长，面板固定高会溢出。按文本实宽量高度再撑开。
            float w = _bondTipText.rectTransform.rect.width;
            if (w < 1f) w = Mathf.Max(_bondTipPanel.sizeDelta.x - 32f, 200f);
            float h = _bondTipText.GetPreferredValues(text, w, 0f).y + 32f;
            var sz = _bondTipPanel.sizeDelta;
            _bondTipPanel.sizeDelta = new Vector2(sz.x, Mathf.Min(Mathf.Max(h, 120f), Screen.height - 40f));
            _bondTipGroup.alpha = 1f;
        }

        private void HideBondTip()
        {
            if (_bondTipPanel == null || !_bondTipPanel.gameObject.activeSelf) return;
            _bondTipSlot = -1;
            _bondTipPanel.gameObject.SetActive(false);
            _bondTipGroup.alpha = 0f;
        }

        // ================================================================
        //  拖拽 / 点击布阵
        //  ----------------------------------------------------------------
        //  交互约定（与 GDD"布阵是最重要的决策界面"对齐）：
        //    · 点卡池条目 → 上阵到第一个空推荐位；再点一次（或点已上阵的卡）→ 下阵
        //    · 拖卡池条目到格子 → 上阵（原格的兽自动回卡池）
        //    · 拖格子上的兽到另一格 → 移动 / 交换
        //    · 点格子（空手）→ 下阵
        //    · 每次变动都重算羁绊与战力（相生相邻、共鸣档位实时变）
        // ================================================================

        private Image _ghostImg;
        private bool _dragActive;

        /// <summary>给 9 个格子挂上点击/拖拽代理（幂等，面板常驻时只挂一次）。</summary>
        private void WireCells()
        {
            if (_cells == null) return;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == null) continue;
                var item = _cells[i].gameObject.GetComponent<CellDragItem>();
                if (item == null) item = _cells[i].gameObject.AddComponent<CellDragItem>();
                item.Owner = this;
                item.CellIndex = i;
            }
        }

        /// <summary>点击卡池条目：已上阵 → 下阵；未上阵 → 放进第一个空推荐位。</summary>
        public void OnRosterClicked(int beastIndex)
        {
            for (int cell = 0; cell < _deployed.Length; cell++)
            {
                if (_deployed[cell] == beastIndex) { _deployed[cell] = -1; CommitLayout(); return; }
            }
            foreach (var slot in Slots)
            {
                if (_deployed[slot] < 0) { _deployed[slot] = beastIndex; CommitLayout(); return; }
            }
            Debug.Log("[FormationPanel] 九宫格已满，先点击格子下阵一只。");
        }

        /// <summary>拖拽卡池项时临时关掉 ScrollRect，避免拖拽被滚动抢走。</summary>
        public void SetRosterScrollEnabled(bool on)
        {
            if (_scrollRoster != null) _scrollRoster.enabled = on;
        }

        // ---- 卡池"列表内=滚动 / 拖出=上阵"（见 RosterDragItem）----
        /// <summary>把拖拽转发给卡池 ScrollRect（列表内拖动 = 滚动）。</summary>
        public void RosterScrollInit(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnInitializePotentialDrag(e);
        }
        public void RosterScrollBegin(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnBeginDrag(e);
        }
        public void RosterScrollDrag(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnDrag(e);
        }
        public void RosterScrollEnd(PointerEventData e)
        {
            if (_scrollRoster != null) _scrollRoster.OnEndDrag(e);
        }

        /// <summary>指针是否已离开卡池区域（离开 = 意图拖到棋盘上阵）。</summary>
        public bool IsPointerOutsideRoster(Vector2 screenPos)
        {
            var rt = _scrollRoster != null ? _scrollRoster.transform as RectTransform : null;
            if (rt == null) return true;
            return !RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }

        /// <summary>空手点击格子：有兽 → 下阵。</summary>
        public void OnCellClicked(int cell)
        {
            if (cell < 0 || cell >= _deployed.Length || _deployed[cell] < 0) return;
            _deployed[cell] = -1;
            CommitLayout();
        }

        /// <summary>
        /// 拖拽开始。beastIndex / fromCell 二选一有值：从卡池拖 or 从格子拖。
        /// 从空格子拖起 = 什么都没有，直接取消。
        /// </summary>
        public void OnDragBegin(int beastIndex, int fromCell, PointerEventData e)
        {
            _dragActive = false;
            _dragBeast = beastIndex;
            _dragFromCell = fromCell;

            int showIndex = beastIndex >= 0 ? beastIndex : (fromCell >= 0 ? _deployed[fromCell] : -1);
            if (showIndex < 0) { _dragBeast = -1; _dragFromCell = -1; return; }

            ShowGhost(showIndex, e.position);
            _dragActive = true;
        }

        public void OnDragMove(PointerEventData e)
        {
            if (!_dragActive) return;
            MoveGhost(e.position);

            // 悬停高亮：拖到哪个格子，哪个格子亮起来
            int hover = FindCellAt(e.position);
            if (_imgCellHi != null)
            {
                bool on = hover >= 0;
                _imgCellHi.gameObject.SetActive(on);
                if (on) _imgCellHi.transform.position = _cells[hover].position;
            }
        }

        public void OnDragEnd(int beastIndex, int fromCell, PointerEventData e)
        {
            if (_imgCellHi != null) _imgCellHi.gameObject.SetActive(false);
            HideGhost();
            if (!_dragActive) { _dragBeast = -1; _dragFromCell = -1; return; }
            _dragActive = false;

            int target = FindCellAt(e.position);
            if (fromCell >= 0)
            {
                // 格子 → 格子：移动 / 交换
                if (target >= 0 && target != fromCell)
                {
                    int moved = _deployed[fromCell];
                    _deployed[fromCell] = _deployed[target];
                    _deployed[target] = moved;
                }
            }
            else if (beastIndex >= 0 && target >= 0)
            {
                // 卡池 → 格子：上阵。★ 拖拽路径此前**没有任何校验**（点选路径 OnRosterClicked 有），
                //   所以会出现"超过 5 只还能上""同一只可重复上"（用户实测）。这里补齐两道。
                for (int c = 0; c < _deployed.Length; c++)
                {
                    if (_deployed[c] == beastIndex)
                    {
                        Debug.Log("[FormationPanel] 该异兽已在阵中，不能重复上阵。");
                        _dragBeast = -1; _dragFromCell = -1;
                        CommitLayout();
                        return;
                    }
                }
                // ★ 落到**占用格** = 直接替换（换一只上去、原兽回卡池）—— 不算新增，满员也允许；
                //   只有落到**空格**时才受"最多 5 只"限制（用户 2026-10-12 报"不同异兽替换不了"）。
                bool occupied = _deployed[target] >= 0;
                int used = 0;
                for (int c = 0; c < _deployed.Length; c++) if (_deployed[c] >= 0) used++;
                if (!occupied && used >= MaxDeploy)
                {
                    Debug.Log("[FormationPanel] 最多上阵 " + MaxDeploy + " 只，请先下阵一只。");
                    _dragBeast = -1; _dragFromCell = -1;
                    CommitLayout();
                    return;
                }
                _deployed[target] = beastIndex;   // 空格=上阵；占用格=替换（原兽自动回卡池）
            }

            _dragBeast = -1;
            _dragFromCell = -1;
            CommitLayout();
        }

        /// <summary>
        /// 旧档继承：把存档里记录的队伍按 id 摆回九宫格。
        /// id 对不上目录的（版本变动/阵容重排）跳过；一只都没摆上 → 返回 false 走自动布阵。
        /// </summary>
        private bool TryInheritTeam()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.Team == null || run.Team.Count == 0) return false;

            for (int i = 0; i < _deployed.Length; i++) _deployed[i] = -1;
            int placed = 0;
            foreach (var id in run.Team)
            {
                int beastIndex = IndexOfBeast(id);
                if (beastIndex < 0) continue;
                int slot = NextEmptySlot();
                if (slot < 0) break;
                _deployed[slot] = beastIndex;
                placed++;
            }
            if (placed == 0) return false;
            CommitLayout();
            Debug.Log("[FormationPanel] 已继承存档队伍：" + placed + " 只。");
            return true;
        }

        /// <summary>出征前把当前队伍写回旅程（下次进来直接摆好）。</summary>
        private void SaveTeamToRun()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _all == null) return;
            run.Team.Clear();
            foreach (var idx in _deployed)
                if (idx >= 0 && idx < _all.Length) run.Team.Add(_all[idx].Id);
            WanXiang.Run.RunSave.SaveCurrent();
        }

        private int IndexOfBeast(string id)
        {
            if (_all == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _all.Length; i++)
                if (_all[i] != null && _all[i].Id == id) return i;
            return -1;
        }

        private int NextEmptySlot()
        {
            foreach (var slot in Slots)
                if (_deployed[slot] < 0) return slot;
            return -1;
        }

        private void CommitLayout()
        {
            RefreshCells();
            RefreshBonds();
        }

        /// <summary>屏幕坐标 → 九宫格格位（-1 = 不在任何格子上）。</summary>
        private int FindCellAt(Vector2 screenPos)
        {
            if (_cells == null) return -1;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(_cells[i], screenPos, null))
                    return i;
            }
            return -1;
        }

        private void ShowGhost(int beastIndex, Vector2 screenPos)
        {
            if (_ghost == null)
            {
                _ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                var grt = (RectTransform)_ghost.transform;
                grt.SetParent(transform, false);
                grt.sizeDelta = new Vector2(150f, 150f);
                var cg = _ghost.GetComponent<CanvasGroup>();
                cg.blocksRaycasts = false;      // 幽灵不接事件，别挡住格子的判定
                cg.alpha = 0.85f;
                _ghostImg = _ghost.GetComponent<Image>();
                _ghostImg.raycastTarget = false;
                _ghostImg.preserveAspect = true;
                _ghost.transform.SetAsLastSibling();
            }
            _ghost.SetActive(true);

            var sprite = (_sprites != null && _all != null && beastIndex < _all.Length)
                ? _sprites.GetHead(_all[beastIndex].Id) : null;
            if (sprite != null)
            {
                _ghostImg.sprite = sprite;
                _ghostImg.color = Color.white;
            }
            else
            {
                _ghostImg.sprite = null;
                _ghostImg.color = new Color(0.78f, 0.83f, 0.72f, 0.9f);
            }
            MoveGhost(screenPos);
        }

        private void MoveGhost(Vector2 screenPos)
        {
            if (_ghost != null) ((RectTransform)_ghost.transform).position = screenPos;
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }

        // ================================================================
        //  出征
        // ================================================================

        /// <summary>
        /// 按当前节点构建战斗请求 —— **预览与出征共用这一处**（曾经两处各写一份，
        /// 改了一处漏另一处）。分派规则：
        /// · 终局格（第 5 幕末格，<see cref="NodeRequest.IsFinale"/>）
        ///   → <see cref="TrialPanel.BuildFinaleBattle"/>：Boss(中宫) + 我方镜像×3；
        /// · 其余（含幕 1~4 守关，IsBoss=true）→ <c>BattleRequestFactory</c>（守关走 BossSquadFor）。
        /// ⚠ 少了终局分支，第 5 幕末格会退回普通遭遇 —— 用户实测"第五幕 boss 关还是小异兽"。
        /// </summary>
        private bool BuildRequestForNode(WanXiang.Run.RunState run, out BattleRequest req)
        {
            req = null;
            if (run != null && _node != null && _node.IsFinale)
            {
                var cat = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
                var allBeasts = (cat != null && cat.Length > 0)
                    ? WanXiang.Fusion.ContentLibrary.BuildBeasts(cat[0]) : null;
                if (allBeasts == null || allBeasts.Length == 0)
                {
                    UnityEngine.Debug.LogError("[FormationPanel] 终局战：内容表为空，无法构建");
                    return false;
                }
                req = TrialPanel.BuildFinaleBattle(run, allBeasts);
                return req != null;
            }
            return (run != null)
                ? BattleRequestFactory.TryBuildFromRun(_contentCatalog, run, _node.Weather, out req,
                                                       _node.Kind, -1, _node.IsBoss)
                : BattleRequestFactory.TryBuild(_contentCatalog, _node.Title, _node.Weather,
                                                _node.Seed, out req);
        }

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
            var run = WanXiang.Run.RunSave.Current;
            bool ok = BuildRequestForNode(run, out req);
            if (!ok)
            {
                Debug.LogError("[FormationPanel] 组队失败，无法进入战斗。");
                return;
            }

            // ⚠ 必须保留 TryBuildFromRun 注入的觉醒技：工厂把觉醒技装进 req.Player 的克隆上，
            //   下面替换默认队形时若直接加 _all[idx]（内容目录原兽，无觉醒技），觉醒技会被丢掉，
            //   导致战斗中觉醒技按钮不显示（hasAwaken=False）。先按 Id 建"注入了觉醒技的克隆"查表。
            var injectedById = new System.Collections.Generic.Dictionary<string, WanXiang.Battle.Core.BeastDef>();
            if (req.Player != null)
                foreach (var injected in req.Player) if (injected != null) injectedById[injected.Id] = injected;

            // 用玩家真实布阵替换工厂给的默认队形
            req.Player.Clear();
            if (req.PlayerCells == null) req.PlayerCells = new System.Collections.Generic.List<int>();
            req.PlayerCells.Clear();
            for (int cell = 0; cell < _deployed.Length; cell++)
            {
                int idx = _deployed[cell];
                if (idx < 0 || _all == null || idx >= _all.Length) continue;
                var catalogBeast = _all[idx];
                // 优先用注入了觉醒技的克隆；目录里查不到对应克隆时退回原兽
                var beast = (catalogBeast != null && injectedById.TryGetValue(catalogBeast.Id, out var inj)) ? inj : catalogBeast;
                req.Player.Add(beast);
                req.PlayerCells.Add(cell);      // ★ 记录真实格号，战斗按它站位
            }

            // ★ 记录本场【我方】上场 id（结算生成编队码用）
            WanXiang.Modules.UI.SceneFlow.LastAllyIds.Clear();
            if (WanXiang.Modules.UI.SceneFlow.LastAllyCells == null)
                WanXiang.Modules.UI.SceneFlow.LastAllyCells = new System.Collections.Generic.List<int>();
            WanXiang.Modules.UI.SceneFlow.LastAllyCells.Clear();
            if (req.Player != null)
            {
                for (int pi = 0; pi < req.Player.Count; pi++)
                {
                    var pb = req.Player[pi];
                    if (pb == null) continue;
                    WanXiang.Modules.UI.SceneFlow.LastAllyIds.Add(pb.Id);
                    // 真实站位：PlayerCells 与 Player 一一对应（缺失时回退序号）
                    int cell = (req.PlayerCells != null && req.PlayerCells.Count > pi)
                        ? req.PlayerCells[pi] : pi;
                    WanXiang.Modules.UI.SceneFlow.LastAllyCells.Add(cell);
                }
            }

            // ★ 记录本场敌方异兽 id（结算掉魂用）
            WanXiang.Modules.UI.SceneFlow.LastFoeIds.Clear();
            if (req.EnemyEntries != null && req.EnemyEntries.Count > 0)
            {
                foreach (var en in req.EnemyEntries)
                    if (en.Def != null) WanXiang.Modules.UI.SceneFlow.LastFoeIds.Add(en.Def.Id);
            }
            else if (req.Enemy != null)
            {
                foreach (var b in req.Enemy)
                    if (b != null) WanXiang.Modules.UI.SceneFlow.LastFoeIds.Add(b.Id);
            }

            SaveTeamToRun();
            // 出征 = 切到战斗场景。关掉布阵界面：布阵是 Normal 层，
            // 战斗 HUD 在 Main 层，留着会被布阵的遮罩压住。
            // ★ 记录当前节点：战斗里点「重新挑战」时要回到同一个节点的编队界面（2026-09-29）。
            SceneFlow.PendingBattleNode = _node;
            // ★ 重新挑战修复（2026-09-29）：把"待推进节点"在【出征】这一刻重新写回。
            //   点节点时 CampaignPanel 已写过一次 PendingCommit，但重新挑战会先把 PendingCommit 清 -1
            //   （避免回地图时把"放弃的本场"误判为通过）；这里再次写入，保证【真正打赢】回到地图时
            //   才落地通过。正常出征时这行是幂等的（本就是同一个 NodeIndex）。
            if (_node != null) CampaignPanel.PendingCommit = _node.NodeIndex;
            CloseSelf();
            SceneFlow.EnterBattle(req);
        }

        /// <summary>返回地图按钮兜底（进编阵后必须能退出去看节点）。</summary>
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
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.sizeDelta = Vector2.zero;

            _btnBack = btn;
            btn.onClick.AddListener(() =>
            {
                // ★ 返回 = 取消这次出征：节点不该被算作通过（用户实测"返回也直接过了"）
                CampaignPanel.PendingCommit = -1;
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    // ★ 先开地图（立刻盖住屏幕）、再关编队 —— 原先是"先关编队 + DelayFrame(30) 再开地图"，
                    //   中间那 30 帧空屏会露出主界面（用户 2026-10-12 报"点返回地图闪一下主界面"）。
                    await ui.OpenAsync<CampaignPanel>();
                    CloseSelf();
                });
            });
        }

    }
}
