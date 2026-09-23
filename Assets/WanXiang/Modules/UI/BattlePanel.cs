// ============================================================================
//  Panel_Battle —— 战斗界面（运行期回放驱动）
//  ---------------------------------------------------------------------------
//  流程：OnOpenAsync 拿到 BattleRequest → BattlePlayback 一次跑完 →
//        本面板按 0.28 秒/事件回放，边播边更新立绘 / 血条 / 怒气 / 日志 →
//        播完自动开 Panel_Result。
//  立绘来源：SpriteCatalog（key = BeastDef.Id）；敌方水平镜像复用同一批图。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Battle", Layer = UILayer.Main, CachePolicy = UICachePolicy.Resident,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed class BattlePanel : UIPanelBase
    {
        /// <summary>战斗 HUD 有「离开」按钮，Esc 误关会让玩家以为卡死（基类默认允许，这里显式禁止）。</summary>
        public override bool AllowBackClose => false;

        [SerializeField] private TMP_Text _tmpRound;            // Tmp_Round
        /// <summary>回合制手动模式：开启后我方行动前等玩家下令（需操作区就绪）。</summary>
        [SerializeField] private bool _manualBattle = true;   // 回合制手动（操作区已就位；关掉即全自动）
        [SerializeField] private GameObject _rootWeather;       // Root_WeatherBanner
        [SerializeField] private TMP_Text _tmpWeatherName;      // Tmp_WeatherName
        [SerializeField] private RectTransform _rootStage;      // Root_Stage
        [SerializeField] private RectTransform _rootUnits;      // Root_Units（单位容器，运行时填充）
        [SerializeField] private RectTransform _hpBarTemplate;  // Item_HpBar（模板，默认隐藏）
        [SerializeField] private Button _btnUltimate;           // Btn_Ultimate
        [SerializeField] private Image _imgRage;                // Img_RageRing
        [SerializeField] private TMP_Text _tmpRage;             // Tmp_RageValue
        [SerializeField] private Button _btnSpeed;              // Btn_Speed
        [SerializeField] private Button _btnAuto;               // Btn_Auto
        [SerializeField] private Button _btnLeave;              // Btn_Leave
        [SerializeField] private TMP_Text _tmpLog;              // Tmp_LogLine

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        /// <summary>
        /// 回放节奏：**一次推进一个事件**，间隔按事件类型给。
        ///
        /// 为什么不再"按批推进"（旧版 0.12 秒 × 6×速度 个事件 = 100 事件/秒）：
        /// 那样一场 500 多事件 5 秒就播完了 —— 观众根本看不清谁出手、谁挨打，
        /// 表现层已有的抖动/变色/伤害数字全被下一批冲掉。自动战斗的观感 =
        /// "一只一只来"，所以节奏必须由事件语义决定：
        ///   出手（SkillCast）慢下来给冲锋演出，伤害留时间看反馈，琐碎事件快过。
        /// 速度按钮（×1/×2/×4）缩放的是这些间隔，不再缩放"一批多少个"。
        /// </summary>
        private static float IntervalOf(BattleEventKind kind)
        {
            switch (kind)
            {
                case BattleEventKind.ActionBegin:   return 0.20f;
                case BattleEventKind.SkillCast:     return 0.34f;   // 出手：配合冲锋前冲+停留
                case BattleEventKind.Damage:        return 0.26f;   // 受击：看抖动、看数字
                case BattleEventKind.Crit:          return 0.22f;
                case BattleEventKind.Heal:          return 0.22f;
                case BattleEventKind.Shield:        return 0.16f;
                case BattleEventKind.Death:         return 0.55f;   // 阵亡：留时间看灰化下沉
                case BattleEventKind.Revive:        return 0.45f;
                case BattleEventKind.TurnStart:     return 0.32f;
                case BattleEventKind.TurnEnd:       return 0.22f;
                case BattleEventKind.RoundResolve:  return 0.28f;
                case BattleEventKind.BattleEnd:     return 0.40f;
                case BattleEventKind.BattleStart:   return 0.45f;
                default:                            return 0.10f;   // 状态/怒气等琐碎
            }
        }

        private sealed class UnitView
        {
            public RectTransform Root;
            public Image Body;
            public Image HpFill;
            public TMP_Text HpNum;
            public RectTransform HpBar;
        }

        private readonly Dictionary<string, UnitView> _views = new Dictionary<string, UnitView>(12);
        private BattlePlayback _play;
        private BattleRequest _req;
        private float _speed = 1f;   // 默认 1x：先能看清，再谈加速
        private bool _autoCast = true;
        private bool _playing;      // 回放中（控制舞台是否逐帧推进）

        // ---- 战斗场景模式（单位由 BattleStage2D 渲染，本面板只当 HUD）----
        private bool _sceneMode;
        private BattleStage2D _stage;
        private int _eventIndex = -1;

        // ================================================================
        //  生命周期
        // ================================================================

        protected override void OnCreate()
        {
            BuildActionBar();

            // ⚠ 必须在 BuildActionBar 之外挂：prefab 已绑定操作区时那个方法会提前 return，
            //    把挂载写在里面就永远挂不上（用户实测"悬浮面板没有"就是这个原因）。
            BuildSkillTip();
            if (_skillBtns != null)
                for (int i = 0; i < _skillBtns.Length; i++)
                    HookTip(_skillBtns[i], i);

            BuildOrderList();   // 右上角"行动顺序"，让玩家看清轮到谁（v2.1 P4 前置）
            if (_rootWeather != null) _rootWeather.SetActive(false);
            if (_btnUltimate != null) _btnUltimate.onClick.AddListener(OnUltimateClicked);
            if (_btnSpeed != null) _btnSpeed.onClick.AddListener(OnSpeedClicked);
            if (_btnAuto != null) _btnAuto.onClick.AddListener(OnAutoClicked);
            if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            ApplyPvpUiMode();   // ★ 每次进入战斗面板都重新应用 PvP UI 模式（时机修正）
            // ★ 每场战斗重置 自动/倍速：面板是 Cached 复用的，字段会带过来。
            //   放在 OnOpenAsync（每次打开必然经过）比放在 PlayLoop 更可靠（用户实测未生效）。
            _autoBattle = !SceneFlow.LastWasPvp;   // ★ PvP 不许被重置掉
            _speed = 1f;
            RefreshSpeedLabel();          // ★ 必须同步刷新按钮文案，否则仍显示上一场的 ×4（用户实测）

            // 两条路径：战斗场景（单位交给 BattleStage2D，本面板只当 HUD）/
            // 主城内直接打（本面板自己画单位视图）
            var sceneCtx = payload as BattleSceneContext;
            if (sceneCtx != null)
            {
                _sceneMode = true;
                _stage = sceneCtx.Stage;
                _play = sceneCtx.Play;
                _req = sceneCtx.Request;
            }
            else
            {
                _sceneMode = false;
                _req = payload as BattleRequest ?? BuildDefaultRequest();
            }

            if (_req == null || _req.Player.Count == 0)
            {
                Debug.LogError("[BattlePanel] 没有可用的战斗入参，且无法从 ContentCatalog 组队。" +
                               "请确认 Panel_Battle 预制体上的 _contentCatalog 已绑定。");
                return UniTask.CompletedTask;
            }

            if (_play == null) _play = new BattlePlayback(_req, _manualBattle);

            // ★★ PvP 补偿：ApplyPvpUiMode 跑的时候 _play 还是 null（Console 已证实），
            //    所以真正的「提交 AI 指令」必须放在 _play 就绪之后 —— 否则回放一直等指令（异兽不动）。
            if (SceneFlow.LastWasPvp && _play != null)
            {
                _autoBattle = true;
                _play.SubmitCommand(-1, -1);
                Debug.Log("[BattlePanel][调试] PvP 补偿提交 -1（_play 就绪），回放开始");
            }

            if (_tmpWeatherName != null) _tmpWeatherName.text = _req.WeatherName ?? "";
            if (_rootWeather != null) _rootWeather.SetActive(!string.IsNullOrEmpty(_req.WeatherName));

            // 场景模式下单位由舞台渲染，HUD 不再画一遍
            if (_sceneMode) ClearUnitViews();
            else BuildUnitViews();

            RunTask(PlayLoop);
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            ClearUnitViews();
            _play = null;
        }

        // ================================================================
        //  组队（没有入参时的兜底：从内容目录取前 5 只 vs 后 5 只）
        // ================================================================

        private BattleRequest BuildDefaultRequest()
        {
            if (_contentCatalog == null)
            {
                Debug.LogError("[BattlePanel] _contentCatalog 未绑定，无法组队。");
                return null;
            }

            var all = ContentLibrary.BuildBeasts(_contentCatalog);
            if (all == null || all.Length == 0) return null;

            var req = new BattleRequest
            {
                Title = "小暑 · 温风至",
                WeatherName = "温风至：每回合全场 3% 灼烧",
                Seed = 20260914UL,
            };
            for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);
            for (int i = 0; i < 5 && 5 + i < all.Length; i++) req.Enemy.Add(all[5 + i]);
            return req;
        }

        // ================================================================
        //  单位视图
        // ================================================================

        private void BuildUnitViews()
        {
            ClearUnitViews();
            if (_rootUnits == null)
            {
                var go = new GameObject("Root_Units", typeof(RectTransform));
                go.transform.SetParent(_rootStage != null ? _rootStage : transform, false);
                _rootUnits = (RectTransform)go.transform;
                _rootUnits.anchorMin = Vector2.zero;
                _rootUnits.anchorMax = Vector2.one;
                _rootUnits.offsetMin = Vector2.zero;
                _rootUnits.offsetMax = Vector2.zero;
            }

            for (int side = 0; side < 2; side++)
            {
                var s = (TeamSide)side;
                foreach (var u in _play.State.UnitsOf(s))
                    CreateView(u, s == TeamSide.Enemy);
            }
        }

        private void ClearUnitViews()
        {
            _views.Clear();
            if (_rootUnits == null) return;
            for (int i = _rootUnits.childCount - 1; i >= 0; i--)
                Destroy(_rootUnits.GetChild(i).gameObject);
        }

        private void CreateView(BattleUnit u, bool isEnemy)
        {
            // 敌我左右分屏，各自 3×3；前排靠近中线
            int cell = u.Pos.IsValid ? u.Pos.Index : 4;
            int col = cell % 3, row = cell / 3;
            float x0 = isEnemy ? 0.52f : 0.02f;
            float ax = x0 + (col + 0.5f) * 0.155f;
            float ay = 0.53f + (2 - row + 0.5f) * 0.15f;

            var root = new GameObject("Unit_" + u.RuntimeId, typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(_rootUnits, false);
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(150f, 150f);
            rt.anchoredPosition = Vector2.zero;

            var bodyGo = new GameObject("Img_Body", typeof(RectTransform));
            var bodyRt = (RectTransform)bodyGo.transform;
            bodyRt.SetParent(rt, false);
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = Vector2.zero;
            bodyRt.offsetMax = Vector2.zero;

            var body = bodyGo.AddComponent<Image>();
            body.raycastTarget = false;
            body.preserveAspect = true;

            var sprite = _sprites != null ? _sprites.Get(u.Def.Id) : null;
            if (sprite != null)
            {
                body.sprite = sprite;
                body.color = Color.white;
                // 敌我同源：敌方水平镜像复用同一张立绘（美术方案 Chapter 04）
                if (isEnemy) bodyRt.localScale = new Vector3(-1f, 1f, 1f);
            }
            else
            {
                body.color = isEnemy
                    ? new Color(0.55f, 0.58f, 0.66f, 0.9f)
                    : new Color(0.78f, 0.83f, 0.72f, 0.9f);
            }

            var view = new UnitView { Root = rt, Body = body };

            // 头顶血条：克隆 Item_HpBar 模板
            if (_hpBarTemplate != null)
            {
                var bar = Instantiate(_hpBarTemplate.gameObject, rt);
                bar.name = "Bar_" + u.RuntimeId;
                bar.SetActive(true);
                var barRt = (RectTransform)bar.transform;
                barRt.anchorMin = new Vector2(0f, 1f);
                barRt.anchorMax = new Vector2(1f, 1f);
                barRt.pivot = new Vector2(0.5f, 0f);
                barRt.anchoredPosition = new Vector2(0f, 4f);
                barRt.sizeDelta = new Vector2(0f, 22f);
                barRt.localScale = Vector3.one;
                view.HpBar = barRt;

                var fill = barRt.Find("Img_HpFill");
                if (fill != null) view.HpFill = fill.GetComponent<Image>();
                var num = barRt.Find("Tmp_HpNum");
                if (num != null) view.HpNum = num.GetComponent<TMP_Text>();
            }

            _views[u.RuntimeId] = view;
        }

        // ================================================================
        //  回放
        // ================================================================

        private async UniTask PlayLoop(CancellationToken ct)
        {
            ApplyFrames();
            if (_tmpLog != null) _tmpLog.text = "战斗开始";
            _playing = true;
            // ★ 每场战斗重置自动/倍速：面板是 Cached 复用的，字段会从上场带过来，
            //   玩家若不注意会莫名_autoBattle = !SceneFlow.LastWasPvp;   // ★ PvP 不许被重置掉。
            _autoBattle = false;
            _speed = 1f;

            int guard = 0;
            while (!_play.Finished && guard++ < 20000)
            {
                // ★ 顺序铁律：**先把已产生的事件全部播完，再考虑等令**。
                //   模拟推进是"跑一段"（可能一次性产生多个事件），若一看到 AwaitingCommand
                //   就停住等令，那段事件会被跳过 —— 表现就是"第一次攻击没效果、之后才补播"（实测）。
                if (HasPendingEvent())
                {
                    if (!StepOnce()) break;
                    var k2 = _play.Current.Kind;
                    float w2 = IntervalOf(k2) / Mathf.Max(1f, _speed);
                    if (MustSee(k2)) w2 = Mathf.Max(w2, 0.36f / Mathf.Max(1f, _speed));
                    await UniTask.Delay(TimeSpan.FromSeconds(w2), cancellationToken: ct);
                    continue;
                }

                // ⚠ 回合制：等玩家下令时必须**原地等**，绝不能 break ——
                //    break 会被下方收尾逻辑当成"播完"，导致一进战斗就直接结算（踩过）。
                if (_play.AwaitingCommand)
                {
                    // ⚠ 只在"待令单位变化"时刷一次 UI —— 绝不要每帧刷：
                    //    每帧 SetActive/interactable/SetText 会持续触发 UI 重建，导致卡死（用户实测）。
                    var pu = _play.PendingUnit;
                    string puId = pu != null ? pu.RuntimeId : "none";
                    if (puId != _lastWaitedId)
                    {
                        _lastWaitedId = puId;
                        RefreshActionBar();                   // 亮出操作区（新单位上台时刷一次）
                        RefreshComboButton();                 // 连携按钮同步刷一次
                        RefreshOrderList();                   // ⚠ 行动条也要刷 —— 等令期间没有事件推进，
                                                              //   不刷的话高亮还停在"上一个异兽"（用户实测）
                    }
                    if (_autoBattle)
                    {
                        _play.SubmitCommand(-1, -1);          // 自动战斗：AI 代下令
                        _lastWaitedId = null;                 // 自动推进：下一轮必然重刷
                        RefreshActionBar();
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    continue;                                 // 不推进事件、不退出循环
                }

                if (!StepOnce()) break;

                // 节奏由事件语义决定；速度倍率只缩放间隔
                float wait = IntervalOf(_play.Current.Kind) / Mathf.Max(1f, _speed);

                // ⚠ 关键事件保底时长：技能释放 / 伤害 / 治疗 / 死亡这些"要看清楚"的事件，
                //   若间隔太小会一闪而过 —— 玩家会觉得"第一次攻击没效果"（实测）。
                //   只给这几类保底，其他事件（回合开始、结算等）保持原节奏，不会拖慢整体。
                if (MustSee(_play.Current.Kind)) wait = Mathf.Max(wait, 0.36f / Mathf.Max(1f, _speed));

                await UniTask.Delay(TimeSpan.FromSeconds(wait), cancellationToken: ct);
            }

            await DrainRest(ct);                     // 收尾：分帧走完剩余事件（见 DrainRest 注释）
            _playing = false;
            FinishAndLeave(0.8f, ct);
        }

        /// <summary>
        /// 舞台演出由这里**逐帧**推进（血条缓降、抖动回弹、冲锋、伤害数字上浮）。
        /// 不能像旧版那样"每次推进事件时 Step(整段时长)"：那样补间一步到位，
        /// 抖动和冲锋看着就是瞬移。
        /// </summary>
        private void Update()
        {
            // 提示面板兜底收起：**被置灰（interactable=false）的按钮不触发 PointerExit**，
            // 悬停后又移开会让面板一直留着（用户实测）。这里轮询鼠标位置兜底，
            // 面板可见时才检查 3 个按钮的矩形，开销可忽略。
            // 自动战斗指示：显示 + 旋转（金色 ⟳，1.4s 一圈，够醒目又不晃眼）
            if (_autoSpin != null)
            {
                bool show = _autoBattle && _playing;
                if (_autoSpin.gameObject.activeSelf != show) _autoSpin.gameObject.SetActive(show);
                if (show) _autoSpin.Rotate(0f, 0f, -360f * Time.deltaTime / 1.4f);
            }

            if (_tipPanel != null && _tipPanel.gameObject.activeSelf) CheckTipHover();

            if (!_playing || _stage == null) return;
            if (State != UIPanelState.Opened) return;      // 被上层盖住/暂停时不推进
            _stage.Step(Time.deltaTime * Mathf.Max(1f, _speed));
        }

        /// <summary>
        /// 刷新连携按钮（只在**待令单位变化**时调用，不进每帧路径）。
        /// AvailableCombos 每次会分配新 List、还要找子节点 —— 逐帧调它就是卡顿源。
        /// </summary>
        /// <summary>
        /// 转圈控件的兜底创建（prefab 被防覆盖跳过新控件时也能立刻看到）。
        /// 美术替换：把 Image 的 sprite 换成圆环/序列帧即可，旋转逻辑通用。
        /// </summary>
        private void EnsureAutoSpin()
        {
            if (_autoSpin != null) return;
            var go = new GameObject("Img_AutoSpin", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.79f, 0.63f, 0.39f, 1f);      // 金色占位
            img.raycastTarget = false;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -46f);
            r.sizeDelta = new Vector2(72f, 72f);
            go.SetActive(false);
            _autoSpin = r;
        }

        /// <summary>刷新倍速按钮文案（必须与 _speed 同步，避免显示与状态不一致）。</summary>
        private void RefreshSpeedLabel()
        {
            if (_btnSpeed == null) return;
            var label = _btnSpeed.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "×" + _speed.ToString("0.##");
        }

        private void RefreshComboButton()
        {
            if (_btnCombo == null || _play == null) return;
            var combos = _play.AvailableCombos;
            _btnCombo.interactable = combos.Count > 0;
            var label = _btnCombo.GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null)
                label.text = combos.Count > 0 ? "连携·" + combos[0].Name : "连携";
        }

        /// <summary>目标选择器的中文名（提示面板用；与 TargetSelector 一一对应）。</summary>
        private static string TargetNameOf(WanXiang.Battle.Core.TargetSelector t)
        {
            switch (t)
            {
                case WanXiang.Battle.Core.TargetSelector.Self: return "自己";
                case WanXiang.Battle.Core.TargetSelector.SingleLowestHp: return "生命最低的敌人";
                case WanXiang.Battle.Core.TargetSelector.SingleHighestHp: return "生命最高的敌人";
                case WanXiang.Battle.Core.TargetSelector.SingleHighestAtk: return "攻击最高的敌人";
                case WanXiang.Battle.Core.TargetSelector.AllEnemies: return "全体敌人";
                case WanXiang.Battle.Core.TargetSelector.AllAllies: return "全体友方";
                case WanXiang.Battle.Core.TargetSelector.RandomEnemy: return "随机敌人";
                case WanXiang.Battle.Core.TargetSelector.RandomEnemyMultiHit: return "随机敌人（连击）";
                case WanXiang.Battle.Core.TargetSelector.AdjacentToSelf: return "自身相邻";
                case WanXiang.Battle.Core.TargetSelector.AllOthers: return "全场其他";
                case WanXiang.Battle.Core.TargetSelector.SingleFrontMost: return "最前排";
                case WanXiang.Battle.Core.TargetSelector.SingleBackMost: return "最后排";
                default: return "—";
            }
        }

        /// <summary>是否还有"已产生但未播放"的事件。</summary>
        private bool HasPendingEvent()
        {
            return _play != null && _play.State != null && _eventIndex + 1 < _play.State.Log.Count;
        }

        /// <summary>这些事件必须让玩家看清（保底演出时长）。</summary>
        private static bool MustSee(WanXiang.Battle.Core.BattleEventKind kind)
        {
            return kind == WanXiang.Battle.Core.BattleEventKind.SkillCast
                || kind == WanXiang.Battle.Core.BattleEventKind.Damage
                || kind == WanXiang.Battle.Core.BattleEventKind.Heal
                || kind == WanXiang.Battle.Core.BattleEventKind.Death
                || kind == WanXiang.Battle.Core.BattleEventKind.Shield;
        }

        /// <summary>推进一个事件：HUD + 舞台同步刷新。返回 false 表示已播完。</summary>
        private bool StepOnce()
        {
            if (_play == null || _play.Finished) return false;
            // ⚠ 这里绝不能因"在等令"就提前返回 —— 等令期间**仍有已产生但未播的事件**
            //   （模拟推进是"跑一段"，事件先产生、播放器随后逐条播）。
            //   曾因提前返回把"第一次攻击"整段跳过（用户实测）。由 PlayLoop 决定何时等令。
            if (!_play.Step()) return false;

            _eventIndex++;
            ApplyFrames();

            var e = _play.Current;
            ApplyEvent(e);
            if (_stage != null) _stage.ApplyEvent(_eventIndex, e);

            // ⚠ 行动条必须**随事件推进刷新**，不能只在"等玩家下令"时刷 ——
            //   否则敌方行动期间高亮不动，玩家看到的是"敌人打完了指针还停在我方/敌人身上"
            //   （用户实测）。RefreshOrderList 内部有指纹守卫（顺序 + 当前行动者 + 回合），
            //   只有真的变了才重建 UI，所以每事件调一次也不会卡。
            RefreshOrderList();
            return true;
        }

        private void ApplyFrames()
        {
            for (int i = 0; i < _play.PendingFrames.Count; i++)
            {
                var f = _play.PendingFrames[i];
                if (_stage != null) _stage.ApplyFrame(f);      // 场景模式：单位交给舞台

                for (int k = 0; k < f.Changed.Length; k++)
                {
                    var snap = f.Changed[k];
                    UnitView view;
                    if (!_views.TryGetValue(snap.UnitId, out view)) continue;
                    if (view.HpFill != null) view.HpFill.fillAmount = Mathf.Clamp01(snap.HpRatio);
                    if (view.HpNum != null) view.HpNum.text = snap.Hp + "/" + snap.MaxHp;
                    if (view.Body != null && !snap.Alive)
                        view.Body.color = new Color(0.45f, 0.45f, 0.45f, 0.35f);
                }
                if (_tmpRound != null && f.Turn > 0) _tmpRound.text = "第 " + f.Turn + " 回合";
            }

            // 元气环取我方第一个单位，作为这一局的手感指示器
            if (_imgRage != null && _play != null)
            {
                var mine = _play.State.UnitsOf(TeamSide.Player);
                if (mine.Count > 0)
                {
                    var u = mine[0];
                    _imgRage.fillAmount = u.RageCap > 0f ? Mathf.Clamp01(u.Rage / u.RageCap) : 0f;
                    if (_tmpRage != null) _tmpRage.text = "元气 " + (int)u.Rage;
                }
            }
        }

        private void ApplyEvent(BattleEvent e)
        {
            if (_tmpLog == null) return;
            string actor = e.ActorId ?? "";
            string target = e.TargetId ?? "";
            switch (e.Kind)
            {
                case BattleEventKind.BattleStart: _tmpLog.text = "战斗开始"; break;
                case BattleEventKind.TurnStart: _tmpLog.text = "第 " + e.Turn + " 回合"; break;
                case BattleEventKind.SkillCast: _tmpLog.text = actor + " 施放 " + (e.SkillName ?? "技能"); break;
                case BattleEventKind.Damage: _tmpLog.text = actor + " → " + target + " 伤害 " + e.Amount; break;
                case BattleEventKind.Heal: _tmpLog.text = target + " 回复 " + e.Amount; break;
                case BattleEventKind.Shield: _tmpLog.text = target + " 护盾 " + e.Amount; break;
                case BattleEventKind.Death: _tmpLog.text = target + " 阵亡"; break;
                case BattleEventKind.BattleEnd: _tmpLog.text = _play.Summary(); break;
            }
        }

        // ================================================================
        //  手动推进（编辑器自动化 / 逐帧工具用）
        //  ----------------------------------------------------------------
        //  为什么需要：本机编辑器的播放器循环不推进（frameCount 不涨），
        //  UniTask.Delay 永远等不到 —— 与本工程既有的战斗回放工具（Battle2DBuilderWindow、
        //  BattleStageWindow）同样的原因，它们也全部用手动 Advance。
        //  真机上帧循环正常，走 PlayLoop 即可；这里给自动化与逐帧验收留一条路。
        // ================================================================

        /// <summary>手动推进 n 个事件（不依赖帧循环）。返回是否还没播完。</summary>
        public bool AdvanceEvents(int n)
        {
            if (_play == null) return false;
            for (int i = 0; i < n; i++)
            {
                if (!StepOnce()) break;
            }
            if (_stage != null) _stage.Step(0.05f * n);
            if (_tmpRound != null) _tmpRound.text = "第 " + _play.State.Turn + " 回合";
            return !_play.Finished;
        }

        /// <summary>快进到结尾并交接（不依赖帧循环）。</summary>
        public void CompleteAndShowResult()
        {
            if (_play == null) return;
            int __drain = 0;
            while (StepOnce() && __drain++ < 2000) { }   // ⚠ 同步入口：加上限，避免单帧跑爆主线程
            FinishAndLeave(0f, CancellationToken.None);
        }

        /// <summary>战斗收尾：刷终局状态 → 交接给结算（场景模式则回主城）。</summary>
        private void FinishAndLeave(float delay, CancellationToken ct)
        {
            if (_tmpRound != null) _tmpRound.text = "第 " + _play.State.Turn + " 回合";
            if (_tmpLog != null) _tmpLog.text = _play.Summary();

            // ★★ 结算前兜底判定胜负：Outcome 只在 BattleSimulator 的循环里由 CheckOutcome 赋值，
            //    手动操作路径若没跑到判负点，Outcome 会停在 Ongoing
            //    ⇒ PlayerWin = false ⇒ **明明赢了却按失败结算**（用户实测 A 情况）。
            //    这里在结算前补一次判定；CheckOutcome 会据"敌方全灭/我方全灭"给出正确结果。
            if (_play != null && _play.State != null)
            {
                var before = _play.State.Outcome;
                if (before == WanXiang.Battle.Core.BattleOutcome.Ongoing) _play.State.CheckOutcome();
                var st = _play.State;
                int mine = 0, foe = 0;
                var pu = st.UnitsOf(WanXiang.Battle.Core.TeamSide.Player);
                var eu = st.UnitsOf(WanXiang.Battle.Core.TeamSide.Enemy);
                for (int i = 0; i < pu.Count; i++) if (pu[i].IsAlive) mine++;
                for (int i = 0; i < eu.Count; i++) if (eu[i].IsAlive) foe++;
                Debug.Log("[BattlePanel] 结算诊断：Outcome " + before + " → " + st.Outcome +
                          "｜我方存活=" + mine + " 敌方存活=" + foe + "（PlayerWin=" + _play.PlayerWin + "）");
            }

            var result = new ResultRequest
            {
                Win = _play.PlayerWin,
                Turns = _play.State.Turn,
                Fingerprint = _play.State.Log.Fingerprint,
                Summary = _play.Summary(),
            };

            // 播放器循环不推进时（本机编辑器），延时是没有意义的 —— 直接交接
            if (!HasFrameLoop)
            {
                HandOff(result);
                return;
            }

            RunTask(async token =>
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token);
                HandOff(result);
            });
        }

        /// <summary>真正交接：场景模式回主城（弹结算），否则就地开结算面板。</summary>
        private void HandOff(ResultRequest result)
        {
            if (_sceneMode)
            {
                // 先关自己再切场景：UI 根节点是 DontDestroyOnLoad 的，
                // 不关的话战斗 HUD 会跟着到主城、压在结算面板后面。
                CloseSelf();
                SceneFlow.ExitBattle(result);
            }
            else
            {
                OpenPanelAsync<ResultPanel>(result).Forget();
                CloseSelf();     // 战斗在 Main 层（低于结算），不关会一直挂在栈上
            }
        }

        /// <summary>本机编辑器播放器循环不推进时，延时交接没有意义（见文件头）。</summary>
        private static bool HasFrameLoop
        {
            get { return Application.isPlaying && Time.frameCount > 8; }
        }

        // ================================================================
        //  按钮
        // ================================================================

        private void OnUltimateClicked()
        {
            // 结构验证版：战斗核心暂不开放"本回合指定绝技"，这里先反馈状态
            if (_tmpLog != null) _tmpLog.text = _autoCast ? "绝技：自动释放中" : "绝技：手动（待接战斗核心）";
        }

        private void OnSpeedClicked()
        {
            _speed = _speed >= 3.9f ? 1f : (_speed >= 1.9f ? 4f : 2f);
            if (_tmpLog != null) _tmpLog.text = "速度 ×" + _speed;
            var label = _btnSpeed != null ? _btnSpeed.transform.Find("Tmp_Label") : null;
            if (label != null)
            {
                var t = label.GetComponent<TMP_Text>();
                if (t != null) t.text = "x" + _speed;
            }
        }

        private void OnAutoClicked()
        {
            _autoCast = !_autoCast;
            if (_tmpLog != null) _tmpLog.text = _autoCast ? "托管：开" : "托管：关";
        }

        private void OnLeaveClicked()
        {
            // 中途退出按失败结算
            if (_tmpLog != null) _tmpLog.text = "撤退（按失败结算）";
            HandOff(new ResultRequest
            {
                Win = false,
                Turns = _play != null ? _play.State.Turn : 0,
                Fingerprint = _play != null ? _play.State.Log.Fingerprint : 0u,
                Summary = "撤退",
                Retreated = true,
            });
        }

        // ================================================================
        //  回合制 v2.1 P1-3：战记操作区
        //  ------------------------------------------------------------------
        //  代码自建（不改 prefab / 生成器）：战记三槽 = SkillType 的三个枚举值，
        //  点一下就把指令交给 BattlePlayback.SubmitCommand，目标暂交给 AI（-1）。
        //  「自动战斗」= 反复下 -1 指令（= 交给 AI），与 v2.1 文档第 13 节一致。
        // ================================================================
        [SerializeField] private RectTransform _actionBar;    // Root_Action（生成器产物）
        [SerializeField] private TMP_Text _tmpActor;          // Tmp_Actor 当前待令单位
        [SerializeField] private Button[] _skillBtns;         // Btn_Skill_0..2（下标 = SkillType）
        [SerializeField] private Button _btnAutoBattle;       // Btn_AutoBattle
        [SerializeField] private Button _btnCombo;            // Btn_Combo（连携）
        [SerializeField] private RectTransform _autoSpin;     // Tmp_AutoSpin（自动战斗转圈）
        private bool _autoBattle;

        private void BuildActionBar()
        {
            // prefab 已绑定（生成器产物）→ 只接线，不重复建控件
            if (_actionBar != null)
            {
                if (_skillBtns != null)
                    for (int i = 0; i < _skillBtns.Length && i < 3; i++)
                    {
                        int idx = i;
                        if (_skillBtns[i] != null) _skillBtns[i].onClick.AddListener(() => OnSkillClicked(idx));
                    }
                if (_btnAutoBattle != null) _btnAutoBattle.onClick.AddListener(OnAutoBattleClicked);
                if (_btnCombo != null) _btnCombo.onClick.AddListener(OnComboClicked);
                if (_btnCombo != null) _btnCombo.onClick.AddListener(OnComboClicked);
                if (_btnCombo != null) _btnCombo.onClick.AddListener(OnComboClicked);
                HookTipCombo();
                EnsureAutoSpin();
                _actionBar.gameObject.SetActive(false);
                return;
            }

            // 兜底：prefab 里没有操作区时（例如旧 prefab 没重跑生成器）代码自建一套
            var go = new GameObject("Root_Action", typeof(RectTransform));
            _actionBar = (RectTransform)go.transform;
            _actionBar.SetParent(transform, false);
            _actionBar.anchorMin = new Vector2(0f, 0f);
            _actionBar.anchorMax = new Vector2(1f, 0f);
            _actionBar.pivot = new Vector2(0.5f, 0f);
            _actionBar.anchoredPosition = new Vector2(0f, 10f);
            _actionBar.sizeDelta = new Vector2(-40f, 150f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.13f, 0.09f, 0.72f);

            var art = new GameObject("Tmp_Actor", typeof(RectTransform)).GetComponent<RectTransform>();
            art.SetParent(_actionBar, false);
            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.anchoredPosition = new Vector2(0f, -6f);
            art.sizeDelta = new Vector2(-24f, 40f);
            _tmpActor = art.gameObject.AddComponent<TextMeshProUGUI>();
            _tmpActor.fontSize = 26;
            _tmpActor.color = new Color(0.98f, 0.96f, 0.9f, 1f);
            _tmpActor.alignment = TextAlignmentOptions.Center;
            _tmpActor.raycastTarget = false;

            string[] names = { "普攻", "战记", "终结技", "觉醒技" };   // 下标 = SkillType 枚举值（3=Awaken）
            _skillBtns = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var brt = new GameObject("Btn_Skill_" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                brt.SetParent(_actionBar, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0f, 0f);
                brt.pivot = new Vector2(0f, 0f);
                brt.anchoredPosition = new Vector2(20f + i * 210f, 18f);
                brt.sizeDelta = new Vector2(190f, 76f);
                var img = brt.gameObject.AddComponent<Image>();
                img.color = new Color(0.79f, 0.63f, 0.39f, 1f);
                var btn = brt.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;

                var lrt = new GameObject("Tmp", typeof(RectTransform)).GetComponent<RectTransform>();
                lrt.SetParent(brt, false);
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(6f, 4f);
                lrt.offsetMax = new Vector2(-6f, -4f);
                var tmp = lrt.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.text = names[i];
                tmp.fontSize = 26;
                tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;

                int idx = i;
                btn.onClick.AddListener(() => OnSkillClicked(idx));
                _skillBtns[i] = btn;
            }

            var ar = new GameObject("Btn_Auto", typeof(RectTransform)).GetComponent<RectTransform>();
            ar.SetParent(_actionBar, false);
            ar.anchorMin = ar.anchorMax = new Vector2(1f, 0f);
            ar.pivot = new Vector2(1f, 0f);
            ar.anchoredPosition = new Vector2(-20f, 18f);
            ar.sizeDelta = new Vector2(200f, 76f);
            var aimg = ar.gameObject.AddComponent<Image>();
            aimg.color = new Color(0.94f, 0.92f, 0.88f, 1f);
            _btnAutoBattle = ar.gameObject.AddComponent<Button>();
            _btnAutoBattle.targetGraphic = aimg;
            var lrt2 = new GameObject("Tmp", typeof(RectTransform)).GetComponent<RectTransform>();
            lrt2.SetParent(ar, false);
            lrt2.anchorMin = Vector2.zero;
            lrt2.anchorMax = Vector2.one;
            lrt2.offsetMin = new Vector2(6f, 4f);
            lrt2.offsetMax = new Vector2(-6f, -4f);
            var tmp2 = lrt2.gameObject.AddComponent<TextMeshProUGUI>();
            tmp2.text = "自动战斗";
            tmp2.fontSize = 24;
            tmp2.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp2.alignment = TextAlignmentOptions.Center;
            tmp2.raycastTarget = false;
            _btnAutoBattle.onClick.AddListener(OnAutoBattleClicked);

            _actionBar.gameObject.SetActive(false);
            ApplyPvpUiMode();      // ★ 好友对战：隐藏手动 UI + 强制自动（每次激活都会再应用）
        }

        /// <summary>
        /// 好友对战 UI 模式：隐藏【所有】手动操作（技能/连携/自动切换/倍速/自动布阵/撤退），
        /// 强制开启自动战斗。⚠ 必须在【每次战斗开始】时调用 ——
        /// BuildActionBar 只在面板创建时跑一次，那时 LastWasPvp 还没被 EnterBattle 设置（踩过）。
        /// </summary>
        private void ApplyPvpUiMode()
        {
            Debug.Log("[BattlePanel][调试] ApplyPvpUiMode 调用：LastWasPvp=" + SceneFlow.LastWasPvp);
            if (!SceneFlow.LastWasPvp) return;

            // 1) 技能四槽 + 连携 + 自动战斗切换
            if (_skillBtns != null)
                for (int i = 0; i < _skillBtns.Length; i++)
                    if (_skillBtns[i] != null) _skillBtns[i].gameObject.SetActive(false);
            if (_btnCombo != null) _btnCombo.gameObject.SetActive(false);
            if (_btnAutoBattle != null) _btnAutoBattle.gameObject.SetActive(false);
            _autoBattle = true;
            // ★ 关键：光设 true 不会动 —— 必须像 OnAutoBattleClicked 那样提交一次 AI 指令
            if (_play != null)
            {
                _play.SubmitCommand(-1, -1);
                Debug.Log("[BattlePanel][调试] PvP 已提交 -1（AI 指令），回放应开始");
            }
            else
            {
                Debug.Log("[BattlePanel][调试] PvP 时 _play 尚未就绪，等待 OnOpenAsync 重置点补偿提交");
            }

            // 2) Root_Action 下的其它按钮（×N 倍速 / 自动布阵 / 撤退 等代码自建按钮）
            if (_actionBar != null)
            {
                for (int i = 0; i < _actionBar.childCount; i++)
                {
                    var c = _actionBar.GetChild(i);
                    string n = c.name;
                    bool isActor = n == "Tmp_Actor";
                    if (!isActor && c.GetComponent<Button>() != null)
                        c.gameObject.SetActive(false);
                }
            }

            Debug.Log("[BattlePanel] 好友对战：手动 UI 已全部隐藏，自动战斗开启");
        }

        // ================================================================
        //  战记悬浮提示（hover → 左侧弹出 / 离开消失，DOTween 做动画）
        // ================================================================
        private int _tipShowingSlot = -1;                      // 当前展示的战记槽（防抖：同一按钮不重复建动画）
        [SerializeField] private RectTransform _tipPanel;       // Root_SkillTip（生成器产物）
        [SerializeField] private TMP_Text _tipText;            // Tmp_Tip
        private CanvasGroup _tipGroup;
        private DG.Tweening.Tween _tipTween;

        private void BuildSkillTip()
        {
            if (_tipPanel != null)
            {
                // prefab 已绑定（生成器产物）→ 取引用即可（_tipText 也来自 prefab）
                _tipGroup = _tipPanel.GetComponent<CanvasGroup>();
                if (_tipGroup == null) _tipGroup = _tipPanel.gameObject.AddComponent<CanvasGroup>();
                _tipGroup.alpha = 0f;
                _tipGroup.blocksRaycasts = false;
                _tipPanel.gameObject.SetActive(false);
                return;
            }

            var go = new GameObject("Root_SkillTip", typeof(RectTransform));
            _tipPanel = (RectTransform)go.transform;
            _tipPanel.SetParent(transform, false);           // 挂在本面板下，不受操作区显隐影响
            _tipPanel.anchorMin = _tipPanel.anchorMax = new Vector2(0f, 0.5f);
            _tipPanel.pivot = new Vector2(0f, 0.5f);
            _tipPanel.sizeDelta = new Vector2(430f, 260f);
            _tipPanel.anchoredPosition = new Vector2(30f, 40f);   // 屏幕左侧

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.95f);    // 墨底，和战斗 HUD 一致

            _tipGroup = go.AddComponent<CanvasGroup>();
            _tipGroup.alpha = 0f;
            _tipGroup.blocksRaycasts = false;

            var trt = new GameObject("Tmp_Tip", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_tipPanel, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 14f);
            trt.offsetMax = new Vector2(-18f, -14f);
            _tipText = trt.gameObject.AddComponent<TextMeshProUGUI>();
            _tipText.fontSize = 22;
            _tipText.color = new Color(0.97f, 0.94f, 0.88f, 1f);
            _tipText.alignment = TextAlignmentOptions.TopLeft;
            _tipText.raycastTarget = false;

            _tipPanel.gameObject.SetActive(false);
        }

        /// <summary>给连携按钮挂悬浮说明（用户不知道连携是什么 —— 界面必须自我解释）。</summary>
        private void HookTipCombo()
        {
            if (_btnCombo == null) return;
            var trg = _btnCombo.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _btnCombo.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowComboTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideSkillTip());
            trg.triggers.Add(exit);
        }

        /// <summary>连携说明（无论当前是否可发动，都要解释它是什么）。</summary>
        private void ShowComboTip()
        {
            if (_tipPanel == null || _tipText == null) return;
            _tipShowingSlot = 90;          // 连携（不复用战记槽位）
            var combos = _play != null ? _play.AvailableCombos : null;
            string title, body;
            if (combos != null && combos.Count > 0)
            {
                var c = combos[0];
                title = "连携·" + c.Name;
                body = c.Note + "\n\n消耗：双方各 2 灵力（合计 4）\n限制：每场每种连携限用一次\n条件：主兽在场 + 对应元素伙伴在场\n\n点按钮立即发动";
            }
            else
            {
                title = "连携技";
                var pending = _play != null ? _play.PendingUnit : null;
                var why = pending != null && _play.State != null
                    ? ComboRules.WhyNot(_play.State, pending)
                    : "没有待令单位";
                body = "两只特定异兽同场时解锁的双人合击（例如：句芒+任何木属性伙伴 ⇒ 青阳共鸣）。\n\n当前："
                     + (why ?? "可以发动")
                     + "\n\n发动时机：连携占用主兽本次行动。";

            _tipText.text = "<size=26><b>" + title + "</b></size>\n" + body;

            _tipPanel.gameObject.SetActive(true);
            _tipTween?.Kill();
            _tipGroup.alpha = 0f;
            _tipPanel.anchoredPosition = new Vector2(30f, 40f);
            _tipTween = DOTween.Sequence()
                .Join(_tipPanel.DOAnchorPos(new Vector2(52f, 40f), 0.18f).SetEase(Ease.OutQuad))
                .Join(_tipGroup.DOFade(1f, 0.18f));
            }
        }

        /// <summary>
        /// 鼠标是否已离开所有战记按钮 → 是则收起提示。
        /// Overlay 画布用 null 相机（RectTransformUtility 对 ScreenSpaceOverlay 要求 cam = null）。
        /// </summary>
        private void CheckTipHover()
        {
            if (_skillBtns == null || _btnCombo == null) { if (_skillBtns == null) HideSkillTip(); return; }
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    _btnCombo.transform as RectTransform, Input.mousePosition, null))
                return;      // 在连携按钮上
            for (int i = 0; i < _skillBtns.Length; i++)
            {
                if (_skillBtns[i] == null) continue;
                var rt = _skillBtns[i].transform as RectTransform;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                    return;      // 还在某个战记按钮上
            }
            HideSkillTip();
        }

        /// <summary>给按钮挂鼠标进出（用 EventTrigger，不改 prefab）。</summary>
        private void HookTip(Button btn, int slot)
        {
            if (btn == null) return;
            var trg = btn.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = btn.gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowSkillTip(slot));
            trg.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideSkillTip());
            trg.triggers.Add(exit);
        }

        /// <summary>填充并淡入提示：从左滑入 20px + 透明度 0→1。</summary>
        private void ShowSkillTip(int slot)
        {
            if (_tipPanel == null || _play == null) return;
            // ★ 防抖：同一槽位且面板已显示 → 直接返回。反复悬浮会疯狂创建 DOTween
            //   Sequence（每次 1 个容器 + 2 个 Tweener），编辑器实测直接卡死。
            if (_tipShowingSlot == slot && _tipPanel.gameObject.activeSelf) return;
            _tipShowingSlot = slot;
            var u = _play.PendingUnit;

            string title, body;
            string descHint = "（描述里的百分比是攻击力系数，不是生命百分比）";
            if (slot == 0) { title = "普攻"; body = CostLine(0); }
            else if (slot == 1) { title = "战记 · 主动"; body = CostLine(1); }
            else { title = "终结技"; body = CostLine(2); }

            if (u != null)
            {
                var sk = u.GetSkill((SkillType)slot);
                if (sk != null)
                {
                    if (!string.IsNullOrEmpty(sk.Name)) title = sk.Name;
                    string desc = string.IsNullOrEmpty(sk.Description) ? "（暂无描述）" : sk.Description;
                    // 目标规则：与核心的"普攻打最前排"特判保持一致（否则界面会误导布阵）。
                    //  ⚠ 取**效果 atom** 的目标而不是 PrimaryTarget —— 后者是"主目标"语义，
                    //    对"给自己人加盾"这类技能会显示成"生命最低的敌人"（实测踩到）。
                    var target = sk.PrimaryTarget;
                    if (sk.Effects != null)
                        for (int ai = 0; ai < sk.Effects.Length; ai++)
                            if (sk.Effects[ai].Target != WanXiang.Battle.Core.TargetSelector.Self)
                            { target = sk.Effects[ai].Target; break; }
                    string targetName = sk.Cd == 0
                        ? "最前排（普攻默认打前排，站位决定谁先承伤）"
                        : TargetNameOf(target);
                    body = desc + "\n目标：" + targetName + "\n" + descHint + "\n\n" + body;
                    if (slot == 2 && u.Rage < u.RageCap)
                        body += "\n当前元气 " + (int)u.Rage + "/" + (int)u.RageCap + "（满值才可释放）";
                }
                else
                {
                    body = "这只异兽没有这一槽战记。";
                }
            }
            else
            {
                body = "轮到某个单位行动时才能查看具体战记效果。\n\n" + body;
            }

            _tipText.text = "<size=26><b>" + title + "</b></size>\n" + body;

            _tipPanel.gameObject.SetActive(true);
            _tipTween?.Kill();
            _tipGroup.alpha = 0f;
            _tipPanel.anchoredPosition = new Vector2(30f, 40f);          // 起点：略靠左（滑入）
            _tipTween = DOTween.Sequence()
                .Join(_tipPanel.DOAnchorPos(new Vector2(52f, 40f), 0.18f).SetEase(Ease.OutQuad))
                .Join(_tipGroup.DOFade(1f, 0.18f));
        }

        private string CostLine(int slot)
        {
            switch (slot)
            {
                case 0: return "消耗：无（0 灵力，永远可用）";
                case 1: return "消耗：灵力 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点（全队共享）";
                default: return "消耗：元气满 100 时手动释放，每场一次";
            }
        }

        /// <summary>淡出并收起。</summary>
        private void HideSkillTip()
        {
            if (_tipPanel == null || !_tipPanel.gameObject.activeSelf) return;
            _tipShowingSlot = -1;
            _tipTween?.Kill();
            _tipTween = _tipGroup.DOFade(0f, 0.12f).OnComplete(() =>
            {
                if (_tipPanel != null) _tipPanel.gameObject.SetActive(false);
            });
        }

        // ================================================================
        //  行动顺序（右上角）—— 头像 + 名字，一眼看清轮到谁
        //  ------------------------------------------------------------------
        //  · 顺序取自核心 BuildActionOrderInto（按有效速度降序，我敌混排）= 单一真源
        //  · **刷新守卫**：只有"顺序或当前行动者变化"时才重建 UI。
        //    之前每帧重建 TMP 文本 ⇒ 每帧触发 mesh 重建 + 大量 GC ⇒ Unity 卡死（用户实测）。
        //    现在每帧只做一次 10 单位的排序 + 指纹比对，开销可忽略。
        // ================================================================
        private const int OrderRowCount = 8;
        [SerializeField] private RectTransform _orderPanel;     // Root_OrderList（生成器产物）
        [SerializeField] private RectTransform[] _orderRows;    // OrderRow_0..7
        private Image[] _orderHeads;                            // 行内引用：生成器产物里按名字取
        private TMP_Text[] _orderNames;
        private readonly System.Collections.Generic.List<BattleUnit> _orderBuf =
            new System.Collections.Generic.List<BattleUnit>(16);
        private int _lastOrderStamp = -1;
        private string _lastActorId;
        private string _lastWaitedId;   // 上次已刷新的待令单位（避免每帧刷 UI）
        private string _lastActorLine;

        private void BuildOrderList()
        {
            // ⚠ 不能写成 `if (_orderPanel != null) return;` —— prefab 绑定时会直接返回，
            //   把下面"取行内引用"的代码整段跳过（_orderHeads/_orderNames 永远为 null，
            //   行动条就成了空壳）。必须在这里取完引用再 return。（这个坑踩过三次了）
            if (_orderPanel != null && _orderRows != null && _orderRows.Length > 0 && _orderRows[0] != null)
            {
                _orderHeads = new Image[_orderRows.Length];
                _orderNames = new TMP_Text[_orderRows.Length];
                for (int i = 0; i < _orderRows.Length; i++)
                {
                    if (_orderRows[i] == null) continue;
                    var h = _orderRows[i].Find("Head");
                    if (h != null) _orderHeads[i] = h.GetComponent<Image>();
                    var n = _orderRows[i].Find("Tmp_Name");
                    if (n != null) _orderNames[i] = n.GetComponent<TMP_Text>();
                }
                _orderPanel.gameObject.SetActive(false);
                return;
            }
            if (_orderPanel != null) { _orderPanel.gameObject.SetActive(false); return; }   // 只绑了面板没绑行：显示空壳，不崩

            var go = new GameObject("Root_OrderList", typeof(RectTransform));
            _orderPanel = (RectTransform)go.transform;
            _orderPanel.SetParent(transform, false);
            _orderPanel.anchorMin = _orderPanel.anchorMax = new Vector2(1f, 1f);
            _orderPanel.pivot = new Vector2(1f, 1f);
            _orderPanel.sizeDelta = new Vector2(330f, 380f);
            _orderPanel.anchoredPosition = new Vector2(-24f, -120f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.62f);

            // 标题
            var trt = new GameObject("Tmp_Title", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_orderPanel, false);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -6f);
            trt.sizeDelta = new Vector2(-16f, 30f);
            var title = trt.gameObject.AddComponent<TextMeshProUGUI>();
            title.text = "行动顺序（按速度）";
            title.fontSize = 20;
            title.color = new Color(0.96f, 0.94f, 0.88f, 1f);
            title.alignment = TextAlignmentOptions.Center;
            title.raycastTarget = false;

            // 8 行：头像 + 名字
            _orderRows = new RectTransform[OrderRowCount];
            _orderHeads = new Image[OrderRowCount];
            _orderNames = new TMP_Text[OrderRowCount];
            for (int i = 0; i < OrderRowCount; i++)
            {
                var row = new GameObject("Row_" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(_orderPanel, false);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -38f - i * 41f);
                row.sizeDelta = new Vector2(-14f, 38f);
                _orderRows[i] = row;

                var head = new GameObject("Head", typeof(RectTransform)).GetComponent<RectTransform>();
                head.SetParent(row, false);
                head.anchorMin = head.anchorMax = new Vector2(0f, 0.5f);
                head.pivot = new Vector2(0f, 0.5f);
                head.anchoredPosition = new Vector2(6f, 0f);
                head.sizeDelta = new Vector2(36f, 36f);
                _orderHeads[i] = head.gameObject.AddComponent<Image>();
                _orderHeads[i].raycastTarget = false;
                _orderHeads[i].preserveAspect = true;

                var nm = new GameObject("Tmp_Name", typeof(RectTransform)).GetComponent<RectTransform>();
                nm.SetParent(row, false);
                nm.anchorMin = Vector2.zero;
                nm.anchorMax = Vector2.one;
                nm.offsetMin = new Vector2(50f, 0f);
                nm.offsetMax = new Vector2(-6f, 0f);
                _orderNames[i] = nm.gameObject.AddComponent<TextMeshProUGUI>();
                _orderNames[i].fontSize = 19;
                _orderNames[i].alignment = TextAlignmentOptions.MidlineLeft;
                _orderNames[i].raycastTarget = false;
            }
            _orderPanel.gameObject.SetActive(false);
        }

        /// <summary>刷新行动顺序（带守卫：只有变化才重建 UI）。</summary>
        private void RefreshOrderList()
        {
            if (_orderPanel == null || _play == null || _play.State == null) return;
            var stt = _play.State;

            // ⚠ 读 State.TurnOrder（回合开始时定下的那份），不要自己按当前速度重排 ——
            //   否则加速/减速之后，界面显示的顺序会与模拟真正执行的顺序不一致。
            _orderBuf.Clear();
            // ⚠ 显示"**接下来**轮到谁"：以当前事件的 actorId 在本回合序列中的位置为界，
            //   只展示它之后（含它自己）的单位 —— 已行动过的不再列出来（用户实测嫌乱）。
            _orderBuf.AddRange(stt.TurnOrder);
            // ⚠ "当前行动者"的语义**分两种**（都实测过，别合并）：
            //   · 演出中 → 跟随画面（当前事件的 actorId）
            //   · 等玩家下令 → **待令单位就是"该行动的"** —— 此时 _play.Current 还停留在
            //     上一单位的收尾事件（ActionEnd）上，若按它找 cur，高亮永远慢一拍（用户实测）。
            BattleUnit cur = null;
            if (_play.AwaitingCommand)
            {
                cur = _play.PendingUnit;
            }
            else
            {
                var ev = _play.Current;   // BattleEvent 是 struct，无需 null 检查
                if (!string.IsNullOrEmpty(ev.ActorId))
                    for (int i = 0; i < _orderBuf.Count; i++)
                        if (_orderBuf[i].RuntimeId == ev.ActorId) { cur = _orderBuf[i]; break; }
            }

            // 指纹：顺序（RuntimeId 的 hash 组合）+ 当前行动者
            int stamp = 17;
            for (int i = 0; i < _orderBuf.Count; i++)
                stamp = stamp * 31 + (_orderBuf[i].RuntimeId != null ? _orderBuf[i].RuntimeId.GetHashCode() : 0);
            string curId = (cur != null ? cur.RuntimeId : null) + "#" + stt.Turn;
            bool same = stamp == _lastOrderStamp && curId == _lastActorId;
            if (same && _orderPanel.gameObject.activeSelf) return;   // ★ 守卫：没变化就什么都不做
            _lastOrderStamp = stamp;
            _lastActorId = curId;

            if (_orderBuf.Count == 0) { _orderPanel.gameObject.SetActive(false); return; }
            _orderPanel.gameObject.SetActive(true);

            // 只显示"**还没行动**"的单位：从当前演出者（ev.ActorId）的位置开始。
            // 之前显示整条回合序列，已行动过的还挂在上面 —— 用户实测嫌乱（"越做越奇怪"）。
            // ⚠ start 与 cur 必须**同一来源**：等令时 cur = PendingUnit（九尾狐），
            //   而 start 若还按"上一个已播事件"的 actor 找，就会从别的位置开始切，
            //   把待令单位自己切掉（用户截图：轮到九尾狐，面板却只剩鹿蜀）。
            int start = 0;
            if (cur != null)
                for (int i = 0; i < _orderBuf.Count; i++)
                    if (_orderBuf[i].RuntimeId == cur.RuntimeId) { start = i; break; }

            int shown = 0;
            for (int i = start; i < _orderBuf.Count && shown < OrderRowCount; i++)
            {
                var u = _orderBuf[i];
                if (!u.IsAlive) continue;

                bool mine = u.Side == WanXiang.Battle.Core.TeamSide.Player;
                // 用户要求：**只高亮我方**。敌方出手不必高亮（玩家只需知道"还没轮到我"），
                // 高亮跳到敌人身上反而误导成"该我操作了"。
                bool isCur = mine && cur != null && u.RuntimeId == cur.RuntimeId;

                _orderRows[shown].gameObject.SetActive(true);
                if (_orderHeads[shown] != null && _sprites != null && u.Def != null)
                {
                    var sp = _sprites.GetHead(u.Def.Id);
                    _orderHeads[shown].sprite = sp;
                    _orderHeads[shown].color = sp != null
                        ? (isCur ? Color.white : new Color(1f, 1f, 1f, 0.85f))
                        : new Color(0f, 0f, 0f, 0f);
                }
                if (_orderNames[shown] != null)
                {
                    _orderNames[shown].text = (isCur ? "▶ " : "") + u.DisplayName
                        + (mine ? "（我方）" : "（敌方）") + (isCur ? " ← 行动中" : "");
                    _orderNames[shown].color = isCur
                        ? new Color(0.94f, 0.76f, 0.43f, 1f)
                        : (mine ? new Color(0.75f, 0.85f, 0.72f, 1f) : new Color(0.91f, 0.71f, 0.66f, 1f));
                }
                shown++;
            }
            for (int i = shown; i < OrderRowCount; i++)
            {
                if (_orderRows[i] == null) continue;
                // 隐藏行顺手清文本：避免残留内容在将来复用/截图时露出来
                if (_orderNames != null && i < _orderNames.Length && _orderNames[i] != null)
                    _orderNames[i].text = string.Empty;
                if (_orderHeads != null && i < _orderHeads.Length && _orderHeads[i] != null)
                    _orderHeads[i].sprite = null;
                _orderRows[i].gameObject.SetActive(false);
            }
        }

        /// <summary>按"是否在等下令"刷新操作区（StepPlayback 每帧调）。</summary>
        private void RefreshActionBar()
        {
            if (_actionBar == null || _play == null) return;
            // ⚠ 自动模式下也要显示操作区：否则按钮消失后再也点不到「自动战斗」开关，
            //    玩家就被卡在自动里出不来（用户实测反馈：打着打着自动了、按钮没了）。
            bool show = _play.AwaitingCommand || _autoBattle;
            if (_actionBar.gameObject.activeSelf != show) _actionBar.gameObject.SetActive(show);
            if (!show) return;

            var u = _play.PendingUnit;
            if (_autoBattle && u == null)
            {
                // 自动推进中（不是在等某个单位）：给一句可回退的提示
                if (_tmpActor != null) _tmpActor.text = "自动战斗中……（点任意战记即可接管）";
                return;
            }
            var stt = _play.State;
            int mp = stt != null ? stt.TeamMp : 0;
            int mpMax = stt != null ? stt.TeamMpMax : 0;

            RefreshOrderList();

            var cfg = stt != null ? stt.Config : null;
            if (_tmpActor != null && u != null)
            {
                string line = "轮到「" + u.DisplayName + "」　灵力 " + mp + "/" + mpMax +
                              "（战记 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点）";
                if (u.GetSkill(SkillType.Ultimate) != null)
                    line += "　元气 " + (int)u.Rage + "/" + (int)u.RageCap;
                // 守卫：文本没变就不重设（TMP 每次 SetText 都会重建 mesh，逐帧设置会卡）
                if (line != _lastActorLine) { _lastActorLine = line; _tmpActor.text = line; }
            }
            else if (_tmpActor != null)
            {
                if (_lastActorLine != "轮到我方行动")
                {
                    _lastActorLine = "轮到我方行动";
                    _tmpActor.text = _lastActorLine;
                }
            }

            if (_skillBtns != null && u != null)
            {
                _skillBtns[0].interactable = true;      // 普攻永远可用（0 耗兜底）
                // 战记：有技能 + 灵力够 —— 灵力不足时置灰，让"取舍"看得见
                _skillBtns[1].interactable = u.GetSkill(SkillType.Active) != null
                                          && mp >= WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active);
                // 终结技：直接复用核心的 CanCast（CD + 怒气满），单一真源，不重复判断规则
                _skillBtns[2].interactable = u.GetSkill(SkillType.Ultimate) != null
                                          && cfg != null
                                          && u.CanCast(SkillType.Ultimate, cfg);

                // ★ 觉醒技（第 4 槽）：**没装备就隐藏**（用户要求）；
                //   装了则按 CanCast（元气满）决定可否点击。
                if (_skillBtns.Length > 3 && _skillBtns[3] != null)
                {
                    bool hasAwaken = u.GetSkill(SkillType.Awaken) != null;
                    if (_skillBtns[3].gameObject.activeSelf != hasAwaken)
                        _skillBtns[3].gameObject.SetActive(hasAwaken);
                    if (hasAwaken)
                        _skillBtns[3].interactable = cfg != null && u.CanCast(SkillType.Awaken, cfg);
                }
            }

            // 连携按钮不在每帧路径里刷新（见 RefreshComboButton，避免每帧 GC/查子节点）
        }

        private void OnSkillClicked(int skillIndex)
        {
            if (_play == null) return;

            // 自动推进中点战记 = 接管：先关自动（此刻不在"待令"状态，SubmitCommand 会无效）
            if (_autoBattle && !_play.AwaitingCommand)
            {
                _autoBattle = false;
                if (_tmpLog != null) _tmpLog.text = "已接管 —— 从下一个单位开始由你下令";
                RefreshActionBar();
                return;
            }

            _autoBattle = false;                        // 手动下令即视为关自动
            _play.SubmitCommand(skillIndex, -1);        // 下标 = SkillType；目标暂交 AI
            RefreshActionBar();
        }

        private void OnComboClicked()
        {
            if (_play == null || !_play.AwaitingCommand) return;
            var combos = _play.AvailableCombos;
            if (combos.Count == 0) return;
            _autoBattle = false;
            _play.SubmitCombo(combos[0].Id);      // v1：发第一条可用连携（多条选择下一轮做）
            RefreshActionBar();
        }

        private void OnAutoBattleClicked()
        {
            if (_play == null) return;
            _autoBattle = !_autoBattle;
            if (_autoBattle) _play.SubmitCommand(-1, -1);   // -1 = 交给 AI（自动战斗）
            if (_tmpLog != null) _tmpLog.text = _autoBattle ? "自动战斗：开" : "自动战斗：关（等你下令）";
            RefreshActionBar();
        }

        /// <summary>
        /// 收尾排空：把剩余事件/帧走完，但**每 8 步让出一帧**。
        /// ★ 原来这里是 `while (StepOnce()) { }` —— 纯同步空转，一场战斗几千个事件
        ///   全挤在一帧里跑完 ⇒ 主线程占死、Unity 整个界面无响应（用户实测"战斗卡死"）。
        /// </summary>
        private async UniTask DrainRest(CancellationToken ct)
        {
            int n = 0;
            while (StepOnce())
            {
                if (++n % 8 == 0) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

    }
}
