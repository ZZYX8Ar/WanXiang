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
    public sealed partial class FormationPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpNodeTitle;       // Tmp_NodeTitle
        [SerializeField] private TMP_Text _tmpWeatherWarn;     // Tmp_WeatherWarn
        [BindArray("Img_EnemyAvatar_{0}", 9)]   // ★ 2026-11-17：续劫难度敌人最多 9 只（原 5 只 → 7 只时显示不全）
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

        [Header("异兽战斗详情（点击卡池 / 悬停格子 → 中上详情区，用户 2026-10-14）")]
        [SerializeField] private RectTransform _detailPanel;   // Root_BeastDetail
        [SerializeField] private Image _detailHead;            // Img_DetailHead
        [SerializeField] private TMP_Text _detailText;         // Tmp_DetailText

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
            WireCellDetailTips();// 格子悬停 → 我方异兽战斗详情
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
            SwapGridAndDetailOnce();
            RefreshBonds();
            RefreshShapePreview();
            if (!TryInheritTeam())
                OnAutoFillClicked(); // 没有可继承的队伍 → 默认摆一套，玩家再微调
            ShowBeastDetailByIndex(0);   // 默认展示第一只：中上详情区不空白（点击/悬停后替换）
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
            // ★ 2026-11-15 用户："写着 6 只怎么就显示 5 只立绘" —— 槽位原来只有 5 个，
            //   prefab 已补到 6 个；这里按实际显示数写括号，避免只数对不上。
            int shownEnemies = 0;
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
                    shownEnemies++;
                }
                else _imgEnemies[i].enabled = false;
            }
            // 敌方总战力：与**我方/结算/历程共用同一份口径**（BattlePower）。
            // ⚠ 原来是 "敌方强度 BP 7.01"（EnemyBudget 的计价口径，是个"预算价格"不是战力）
            //   ， 玩家看不懂（用户实测报障）。改用战力：与上方我方数字同一把尺，可比。
            if (_tmpEnemyPower != null)
            {
                // 战力按全部敌人算（真实数值），括号写"已显示/总数"，只数对不上时看得出来
                string cnt = shownEnemies >= enemies.Count
                    ? enemies.Count + " 只"
                    : shownEnemies + "/" + enemies.Count + " 只";
                _tmpEnemyPower.text = "敌方总战力 " + WanXiang.Battle.Core.BattlePower.Sum(enemies, req.EnemyMul)
                                    + "（" + cnt + "）";
            }
        }

        // ================================================================
        //  卡池
        // ================================================================

        // ★ 2026-11-01 点选上阵：先单击左侧卡池选中（高亮边框），再点九宫格格子上阵/移动。
        private int _selectedBeast = -1;
        private Image _selectedOutline;

        private void SelectBeast(int index, Image outline)
        {
            if (_selectedOutline != null) _selectedOutline.enabled = false;
            _selectedBeast = index;
            _selectedOutline = outline;
            if (_selectedOutline != null) _selectedOutline.enabled = true;
            Debug.Log("[Formation] 选中异兽 idx=" + index + "（再点格子放置）");
        }

        /// <summary>格子点击：有选中兽 → 放进去（已上阵则移动）；没选中 → 原来的双击下阵。</summary>
        public void PlaceSelectedIntoCell(int cell)
        {
            if (_selectedBeast < 0 || _deployed == null || cell < 0 || cell >= _deployed.Length) return;

            // 已在别处 → 先清空原位（"改变到点击的格子"）
            for (int c = 0; c < _deployed.Length; c++)
                if (_deployed[c] == _selectedBeast) _deployed[c] = -1;

            _deployed[cell] = _selectedBeast;
            if (_selectedOutline != null) _selectedOutline.enabled = false;
            _selectedOutline = null;
            _selectedBeast = -1;
            CommitLayout();
            Debug.Log("[Formation] 放入格子 " + cell);
        }

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

            // ★ 2026-11-01：列表顶部标题（用户："加个我方异兽的名字，结合游戏来起"）
            //   取名「万灵谱」——万相世界观里异兽皆灵，谱 = 可查阅的名录。
            {
                var titleGo = new GameObject("Tmp_RosterTitle", typeof(RectTransform));
                var trt = (RectTransform)titleGo.transform;
                trt.SetParent(content, false);
                trt.sizeDelta = new Vector2(0f, 44f);
                var tt = titleGo.AddComponent<TextMeshProUGUI>();
                tt.font = TMP_Settings.defaultFontAsset;
                tt.fontSize = 26f;
                tt.fontStyle = FontStyles.Bold;
                tt.alignment = TextAlignmentOptions.MidlineLeft;
                tt.raycastTarget = false;
                tt.color = new Color(0.16f, 0.13f, 0.09f, 1f);
                tt.text = "万灵谱 · 我方异兽（单击选中，再点格子放置）";
            }

            // ★ 2026-11-01（第二次修）：上一版改 LayoutGroup.padding 无效 —— 行由布局组按
            //   anchoredPosition 摆，offsetMin 又会被覆盖。这次直接挪 **ScrollRect 本身**，
            //   整列（含头像/名字/属性块）一起右移，布局组管不到容器外部。
            if (_scrollRoster != null && !_rosterShifted)
            {
                _rosterShifted = true;
                // ⛔ 挪 **viewport**（不是 ScrollRect 本体，也不是行 offset）：
                //   ScrollRect 每帧写 content.anchoredPosition，行又由 LayoutGroup 摆，
                //   只有 viewport 的 RectTransform 没人动 ⇒ 位移能稳定保留。
                var vp = _scrollRoster.viewport != null ? _scrollRoster.viewport
                                                        : _scrollRoster.transform as RectTransform;
                if (vp != null)
                {
                    vp.offsetMin = new Vector2(vp.offsetMin.x + 34f, vp.offsetMin.y);
                    vp.offsetMax = new Vector2(vp.offsetMax.x - 4f, vp.offsetMax.y);
                }
            }

            for (int i = 0; i < _all.Length; i++)
            {
                var item = Instantiate(_rosterItemTemplate, content);
                item.name = "Item_Roster_" + i;
                item.gameObject.SetActive(true);

                // 整条往右挪（用户："异兽一整个横条要往右移"）——直接改 RectTransform，不依赖 LayoutGroup
                var irt = item as RectTransform;
                if (irt != null)
                {
                    irt.offsetMin = new Vector2(irt.offsetMin.x + 18f, irt.offsetMin.y);
                    irt.offsetMax = new Vector2(irt.offsetMax.x - 6f, irt.offsetMax.y);
                }

                // 挂拖拽/点击代理（点击 = 推荐位上阵；拖拽 = 拖到格子上阵）
                var drag = item.gameObject.GetComponent<RosterDragItem>();
                if (drag == null) drag = item.gameObject.AddComponent<RosterDragItem>();
                drag.Owner = this;
                drag.BeastIndex = i;

                // 选中高亮框（默认隐藏）
                Image selImg = null;
                {
                    var selGo = new GameObject("Img_SelOutline", typeof(RectTransform), typeof(Image));
                    var srt2 = (RectTransform)selGo.transform;
                    srt2.SetParent(item, false);
                    srt2.anchorMin = Vector2.zero; srt2.anchorMax = Vector2.one;
                    srt2.offsetMin = new Vector2(-2f, -2f); srt2.offsetMax = new Vector2(2f, 2f);
                    selImg = selGo.GetComponent<Image>();
                    selImg.color = new Color(1f, 0.72f, 0.25f, 0.35f);
                    selImg.raycastTarget = false;
                    selImg.enabled = false;
                }
                var rd = item.gameObject.GetComponent<RosterDragItem>();
                if (rd != null) rd.SelectionOutline = selImg;

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
            RefreshShapePreview();
        }

        private void OnClearClicked()
        {
            ClearSlots();
            RefreshCells();
            RefreshBonds();
            RefreshShapePreview();
        }

        // ================================================================
        //  阵形激活预览（2026-10-20）
        //  ------------------------------------------------------------------
        //  ⛔ UI 一律 prefab：节点 Root_ShapeBar/Shape_0..N 已烘进 Panel_Formation，
        //     这里只做**填数据**：写名字/染色/位置微调/悬停接线，不再运行时建控件。
        //  成阵判据与战斗同源：阵形要求的格子被上阵单位占满（Cells 或 CellsAlt 任一）。
        // ================================================================
        private RectTransform _shapeBar;

        /// <summary>
        /// 阵型是否凑齐。★ 2026-11-16：改用 Core 的 FormationMatchedByCells ——
        /// 原来这里有一份 AllCellsFilled 自己实现，与 Core 判阵型是两条真相源。
        /// </summary>
        private bool ShapeActive(WanXiang.Battle.Core.ComboDef c)
            => WanXiang.Battle.Core.ComboRules.FormationMatchedByCells(OccupiedCells(), c);

        /// <summary>把 _deployed 转成"哪些格有人"，喂给 Core 的纯站位 API。</summary>
        private bool[] OccupiedCells()
        {
            var occ = new bool[9];
            if (_deployed == null) return occ;
            for (int i = 0; i < _deployed.Length && i < 9; i++) occ[i] = _deployed[i] >= 0;
            return occ;
        }

        private void RefreshShapePreview()
        {
            if (_shapeBar == null)
            {
                _shapeBar = transform.Find("Root_ShapeBar") as RectTransform;
                if (_shapeBar == null)
                {
                    Debug.LogWarning("[Formation] Panel_Formation 缺 Root_ShapeBar（阵形预览已在预制体里预制，不再运行时创建）");
                    return;
                }
            }

            // ★ 位置已烘进预制体（Root_ShapeBar 的 anchoredPosition），运行时不再摆位。
            var all = WanXiang.Battle.Core.ComboRules.All;
            int n = all != null ? all.Count : 0;

            // ★ 同时只能成**一条**阵：按表的优先度取第一条满足的（与战斗里 AvailableFor 同口径），
            //   其余即使格位也满足也不点亮（用户 2026-10-20："还可以同时激活两个阵形的吗"）。
            int activeIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (ShapeActive(all[i])) { activeIdx = i; break; }
            }

            for (int i = 0; i < _shapeBar.childCount; i++)
            {
                var chip = (RectTransform)_shapeBar.GetChild(i);
                bool used = i < n;
                if (chip.gameObject.activeSelf != used) chip.gameObject.SetActive(used);
                if (!used) continue;

                var c = all[i];
                bool on = i == activeIdx;   // 只点亮真正会发动的那一条

                var img = chip.GetComponent<Image>();
                if (img != null)
                    img.color = on ? new Color(1f, 0.95f, 0.80f, 1f)     // 成阵：暖金底
                                   : new Color(0.88f, 0.87f, 0.84f, 1f); // 未成：灰纸

                var labelT = chip.Find("Tmp_Name");
                var label = labelT != null ? labelT.GetComponent<TextMeshProUGUI>() : null;
                if (label != null)
                {
                    label.text = c.Name;
                    label.color = on ? new Color(0.50f, 0.24f, 0.10f, 1f)    // 成阵：赭红字
                                     : new Color(0.55f, 0.53f, 0.50f, 1f);   // 未成：灰字
                }

                var trg = chip.GetComponent<EventTrigger>();
                if (trg == null) trg = chip.gameObject.AddComponent<EventTrigger>();
                trg.triggers.Clear();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                var cc = c;
                bool isActiveOne = i == activeIdx;
                enter.callback.AddListener(_ => PresentBondTip(
                    "<size=24><b>阵形 · " + cc.Name + "（" + (isActiveOne ? "已激活" : (ShapeActive(cc) ? "可成阵（优先级较低）" : "未凑齐")) + "）</b></size>\n" +
                    cc.Note + "\n形状：" + WanXiang.Battle.Core.ComboRules.ShapeText(cc) +
                    MissingHint(cc)));   // ★ 未凑齐时明确写出"还差哪几格"
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => HideBondTip());
                trg.triggers.Add(enter);
                trg.triggers.Add(exit);
            }
        }

        /// <summary>把上阵的立绘落到格子上（文字/头像由 Img_Head 之类承担，这里直接画立绘）。</summary>
        /// <summary>把九宫格容器与说明面板的兄弟顺序互换（2026-11-01 用户要求）。</summary>
        /// <summary>
        /// 未凑齐时的补充提示："还差：前上、后下"。
        /// 这是编队阶段最需要的反馈 —— 玩家要能看见"再挪一格就成阵"，才会主动调站位。
        /// </summary>
        private string MissingHint(WanXiang.Battle.Core.ComboDef c)
        {
            if (c == null || ShapeActive(c)) return "";
            var occ = OccupiedCells();
            var ov = WanXiang.Battle.Core.ComboRules.OverviewByCells(occ);
            for (int i = 0; i < ov.Count; i++)
            {
                if (ov[i].Def != c) continue;
                var miss = ov[i].MissingCells;
                if (miss == null || miss.Length == 0) return "";
                var sb = new System.Text.StringBuilder("\n<color=#8C2F1E>还差：");
                for (int m = 0; m < miss.Length; m++)
                {
                    if (m > 0) sb.Append("、");
                    sb.Append(WanXiang.Battle.Core.ComboRules.CellName(miss[m]));
                }
                sb.Append("</color>");
                return sb.ToString();
            }
            return "";
        }

        private void SwapGridAndDetailOnce()
        {
            if (_swappedLayout || _cells == null || _cells.Length == 0 || _cells[0] == null || _detailPanel == null) return;
            _swappedLayout = true;

            Transform Top(Transform t)
            {
                while (t != null && t.parent != null && t.parent != transform) t = t.parent;
                return t;
            }
            var a = Top(_cells[0]);
            var b = Top(_detailPanel);
            if (a == null || b == null || a == b) return;
            int ia = a.GetSiblingIndex(), ib = b.GetSiblingIndex();
            a.SetSiblingIndex(ib);
            b.SetSiblingIndex(ia);
            Debug.Log("[Formation] 九宫格与说明面板已换位");
        }

        private bool _swappedLayout;

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

                // ★ 2026-11-01 用户："编队的出手顺序要在九宫格右上角加数字"。
                //   顺序 = 上阵顺序（去掉空格后计 1、2、3…），与战斗里"我方按上阵位次行动"一致。
                int order = 0;
                for (int c2 = 0; c2 <= cell && c2 < _deployed.Length; c2++)
                    if (_deployed[c2] >= 0) order++;

                var numGo = new GameObject("Tmp_Order", typeof(RectTransform));
                var nrt = (RectTransform)numGo.transform;
                nrt.SetParent(crt, false);
                nrt.anchorMin = new Vector2(1f, 1f);
                nrt.anchorMax = new Vector2(1f, 1f);
                nrt.pivot = new Vector2(1f, 1f);
                nrt.anchoredPosition = new Vector2(-4f, -4f);
                nrt.sizeDelta = new Vector2(30f, 30f);
                var nx = numGo.AddComponent<TextMeshProUGUI>();
                nx.font = TMP_Settings.defaultFontAsset;
                nx.fontSize = 24f;
                nx.fontStyle = FontStyles.Bold;
                nx.alignment = TextAlignmentOptions.TopRight;
                nx.raycastTarget = false;
                nx.text = order.ToString();
                nx.color = new Color(0.10f, 0.09f, 0.07f, 0.95f);
                // 描影：读不清时也能压住浅色底
                nx.outlineWidth = 0.18f;
                nx.outlineColor = new Color(1f, 1f, 1f, 0.85f);

                // 等级 + 战后血量（设计稿 §6.1 / §7）：共享主等级 + 下一场以 50% 血开场的提示
                var runNow = WanXiang.Run.RunSave.Current;
                if (runNow != null)
                {
                    var lvGo = new GameObject("Tmp_Lv", typeof(RectTransform));
                    var lrt = (RectTransform)lvGo.transform;
                    lrt.SetParent(crt, false);
                    lrt.anchorMin = new Vector2(1f, 1f);
                    lrt.anchorMax = new Vector2(1f, 1f);
                    lrt.pivot = new Vector2(1f, 1f);
                    lrt.anchoredPosition = new Vector2(-4f, -34f);
                    lrt.sizeDelta = new Vector2(60f, 22f);
                    var lv = lvGo.AddComponent<TextMeshProUGUI>();
                    lv.font = TMP_Settings.defaultFontAsset;
                    lv.fontSize = 16f;
                    lv.alignment = TextAlignmentOptions.TopRight;
                    lv.raycastTarget = false;
                    // 进化标记 + 专精概览（设计稿 §6）
                    bool evo = runNow.IsEvolved(_all[idx].Id);
                    int mSum = 0;
                    for (int ms = 0; ms < 3; ms++) mSum += runNow.MasteryOf(_all[idx].Id, ms);
                    lv.text = "Lv" + runNow.PlayerLevel + (evo ? " · 觉" : "") + (mSum > 0 ? " · 专精" + mSum : "");
                    lv.color = new Color(0.16f, 0.13f, 0.09f, 0.9f);

                    int hp = 100;
                    if (runNow.TeamHpPct != null && runNow.TeamHpPct.TryGetValue(_all[idx].Id, out int hpv)) hp = hpv;
                    if (hp < 100)
                    {
                        var hpGo = new GameObject("Tmp_Hp", typeof(RectTransform));
                        var hrt = (RectTransform)hpGo.transform;
                        hrt.SetParent(crt, false);
                        hrt.anchorMin = new Vector2(0f, 0f);
                        hrt.anchorMax = new Vector2(1f, 0f);
                        hrt.pivot = new Vector2(0.5f, 0f);
                        hrt.anchoredPosition = new Vector2(0f, 4f);
                        hrt.sizeDelta = new Vector2(-8f, 18f);
                        var ht = hpGo.AddComponent<TextMeshProUGUI>();
                        ht.font = TMP_Settings.defaultFontAsset;
                        ht.fontSize = 15f;
                        ht.alignment = TextAlignmentOptions.Bottom;
                        ht.raycastTarget = false;
                        ht.text = "HP " + hp + "%";
                        ht.color = new Color(0.75f, 0.18f, 0.12f, 0.95f);
                    }
                }
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
                var runNowMat = WanXiang.Run.RunSave.Current;
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
                // 五行俱全：上阵 5 只且 5 种不同元素 ⇒ 全队 ×1.10（与组队逻辑同一判据）
                var elems = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < _deployed.Length; i++)
                {
                    int idx2 = _deployed[i];
                    if (_all == null || idx2 < 0 || idx2 >= _all.Length) continue;
                    elems.Add((int)_all[idx2].Element);
                }
                bool five = elems.Count >= 5;
                var runLv = WanXiang.Run.RunSave.Current;
                _tmpPower.text = "Lv " + (runLv != null ? runLv.PlayerLevel : 1) + "　我方总战力 " + power
                               + (Mathf.Abs(_relicPlayerMul - 1f) > 0.005f ? "（含遗物/加成）" : "")
                               + (five ? "　五行俱全 +10%" : "")
                               + "　素材 " + (runNowMat != null ? runNowMat.Materials : 0);
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
            // ★ 2026-11-16：重新挑战 → 复用原战场敌人快照（同一节点才用）
            //   用户实测"编队显示 6 只 16915，重挑战回来变 4 只 9059"：
            //   根因是重算 seed（含 run.NodeOffset/run.Wins）得到另一组敌人。
            if (SceneFlow.RechallengeEnemySnapshot != null
                && SceneFlow.RechallengeEnemySnapshot.Count > 0
                && _node != null
                && string.Equals(SceneFlow.RechallengeNodeTitle, _node.Title,
                                 System.StringComparison.Ordinal))
            {
                bool ok = BattleRequestFactory.TryBuildFromRun(_contentCatalog, run, _node.Weather,
                                                               out req, _node.Kind, -1, _node.IsBoss);
                if (ok && req != null)
                {
                    req.Enemy = new System.Collections.Generic.List<WanXiang.Battle.Core.BeastDef>(
                        SceneFlow.RechallengeEnemySnapshot);
                    UnityEngine.Debug.Log("[FormationPanel] 重挑战：复用原敌人快照 "
                                          + req.Enemy.Count + " 只（" + _node.Title + "）");
                    return true;
                }
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

            // ★ 2026-11-19：把节点分配的附加目标带进战斗（空=无目标）
            if (req != null && _node != null)
            {
                req.GoalId = _node.GoalId ?? "";
                if (!string.IsNullOrEmpty(req.GoalId))
                    Debug.Log("[FormationPanel] 本场附加目标：" + WanXiang.Battle.Core.ObjectiveRules.Describe(req.GoalId));
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
            // ★ 2026-11-16：节点地图出征必须清掉"首领连战"来源标记。
            //   否则打完一次首领连战后 LastWasBossRush 一直是 true，
            //   之后从节点地图进任何战斗点"重新挑战"都会被误判回首领连战面板。
            SceneFlow.LastWasBossRush = false;
            SceneFlow.PendingBattleNode = _node;
            // ★ 重挑战快照是"一次性"的：真正出征后清掉，避免下次进同名节点误用旧敌人
            SceneFlow.RechallengeEnemySnapshot = null;
            SceneFlow.RechallengeNodeTitle = null;
            // ★ 重新挑战修复（2026-09-29）：把"待推进节点"在【出征】这一刻重新写回。
            //   点节点时 CampaignPanel 已写过一次 PendingCommit，但重新挑战会先把 PendingCommit 清 -1
            //   （避免回地图时把"放弃的本场"误判为通过）；这里再次写入，保证【真正打赢】回到地图时
            //   才落地通过。正常出征时这行是幂等的（本就是同一个 NodeIndex）。
            if (_node != null) CampaignPanel.PendingCommit = _node.NodeIndex;
            // ★ 必须在 CloseSelf **之前**置位：关编队会让底下地图 Resume，
            //   若不锁住，地图会把"待推进"当场落地 ⇒ 仗没打节点就通过了。
            SceneFlow.BattleDeploying = true;
            CloseSelf();
            SceneFlow.EnterBattle(req);
        }

        /// <summary>返回地图按钮兜底（进编阵后必须能退出去看节点）。</summary>
        private void EnsureBackButton()
        {
            // ⛔ 禁止运行时建 UI（用户硬规矩）：Btn_Back 由 Panel_Formation.prefab 提供并绑定；
            //    这里只负责挂点击。缺按钮时报警，不再 new GameObject 兜底。
            if (_btnBack == null)
            {
                Debug.LogWarning("[Formation] prefab 缺 Btn_Back —— 请在 prefab 里补并绑定 _btnBack");
                return;
            }
            _btnBack.onClick.RemoveAllListeners();
            _btnBack.onClick.AddListener(() =>
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
