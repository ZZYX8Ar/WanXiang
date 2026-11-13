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
    public sealed partial class BattlePanel : UIPanelBase
    {        /// <summary>战斗 HUD 有「离开」按钮，Esc 误关会让玩家以为卡死（基类默认允许，这里显式禁止）。</summary>
        public override bool AllowBackClose => false;

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

        /// <summary>
        /// 战斗背景音乐：守关首领与好友对战各自换曲，普通 / 精英沿用基类表里的战斗曲。
        ///
        /// ⛔ 这里必须**只看 Payload**，不能读 <c>_req</c>：本属性由框架在 InternalOpenAsync 中、
        /// 执行 OnOpenAsync **之前**调用（刻意如此 —— 音乐不该等资源加载），
        /// 那一刻 _req 还是上一场的残留值，读它会让首领战放着普通战的曲子。
        /// </summary>
        public override string BgmId
        {
            get
            {
                var req = Payload as BattleRequest;
                if (req == null)
                {
                    var ctx = Payload as BattleSceneContext;
                    if (ctx != null) req = ctx.Request;
                }
                if (req == null) return base.BgmId;
                if (req.IsPvpMatch) return "bgm_battle_pvp";

                // 按幕取曲：五幕各有一首战斗曲 / 首领曲（ACE-Step 定制那套正好按幕做的）。
                var run = WanXiang.Run.RunSave.Current;
                int act = Mathf.Clamp(run != null ? run.Act : 1, 1, 5);
                if (req.IsBoss)  return "bgm_boss_act" + act;    // 守关首领
                if (req.IsElite) return "bgm_battle_elite";      // 精英战
                return "bgm_battle_act" + act;                   // 普通战
            }
        }

        /// <summary>
        /// 战斗面板**不**挂通用按钮点击音。
        /// 理由：战斗内部的反馈由事件流驱动（技能、命中、指令各有专门音效），
        /// 再叠一层 「click」 只会让"点技能"听起来糊成两声响。
        /// </summary>
        protected override bool AutoButtonSfx => false;
        private float _speed = 1f;   // 默认 1x：先能看清，再谈加速
        private bool _playing;      // 回放中（控制舞台是否逐帧推进）
        private float _haltedAt;    // _halted 置 true 的时刻（看门狗用）
        private float _hitStop;     // 命中顿帧剩余时间（>0 时冻结舞台动画，制造打击"顿"感）
        private const float HitStopSec = 0.055f;   // 命中/暴击顿帧时长（受倍速缩放）
                private bool _actionEnabled = true;        // 技能/连携按钮当前是否可点（每帧兜底：非我方决策点就置灰）
        /// <summary>
        /// 回放闸：置 true 后 <see cref="PlayLoop"/> 原地停下（不推进模拟、不播事件），
        /// 用于「重新挑战」的确认框期间冻结战场。
        /// ★ 用户 2026-10-15 要求："点击的时候战斗应该停止" ——
        ///   原来等确认框期间战斗照跑（自动战斗更快），等点确定时仗已经打完
        ///   ⇒ Outcome != Ongoing 提前 return ⇒ 按钮点了没反应、面板关不掉（实测报障）。
        /// </summary>
        private bool _halted;

        // ---- 战斗场景模式（单位由 BattleStage2D 渲染，本面板只当 HUD）----
        private bool _sceneMode;
        private BattleStage2D _stage;

        // 技能预览的格位高亮用的复用列表（避免每次悬浮都分配）
        private readonly System.Collections.Generic.List<WanXiang.Battle.Core.BattleUnit> _previewTargets
            = new System.Collections.Generic.List<WanXiang.Battle.Core.BattleUnit>(10);
        private readonly System.Collections.Generic.List<int> _previewAllyCells = new System.Collections.Generic.List<int>(9);
        private readonly System.Collections.Generic.List<int> _previewFoeCells = new System.Collections.Generic.List<int>(9);
        // ⚠ 原 `private int _eventIndex` 已删：事件游标的真值只有 BattlePlayback.EventIndex 一份。
        //   两份计数器必然漂移（Step() 等令时返回 true 却不推进）， 循环卡死（实测：涨到 46 万）。

        // ================================================================
        //  生命周期
        // ================================================================

        protected override void OnCreate()
        {
            BuildActionBar();

            // ⚠ 必须在 BuildActionBar 之外挂：prefab 已绑定操作区时那个方法会提前 return，
            //    把挂载写在里面就永远挂不上（用户实测"悬浮面板没有"就是这个原因）。
            BuildSkillTip();

            BuildOrderList();   // 右上角"行动顺序"，让玩家看清轮到谁（v2.1 P4 前置）
            if (_rootWeather != null) _rootWeather.SetActive(false);
            if (_btnSpeed != null) _btnSpeed.onClick.AddListener(OnSpeedClicked);
            if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // ★ 米色横幅上的文字改墨色（代码兜底：即使用户 prefab 是旧色也生效）
            var _ink = new Color(0.16f, 0.13f, 0.09f, 1f);
            foreach (var _t in new[] { "_tmpActor" })
            {
                var _f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var _fi = GetType().GetField(_t, _f);
                var _tx = _fi != null ? _fi.GetValue(this) as TMP_Text : null;
                if (_tx != null) _tx.color = _ink;
            }
            InitCapture();
            var _orderT = GameObject.Find("Tmp_OrderTitle");
            if (_orderT != null) _orderT.GetComponent<TMP_Text>().color = _ink;

            ApplyPvpUiMode();   // ★ 每次进入战斗面板都重新应用 PvP UI 模式（时机修正）
            // ★ 清掉可能从**上一场残留**的技能悬浮提示 + 觉醒技槽（用户 2026-10-13 报障：
            //   重新挑战→回节点地图→再进场，白魍这种没觉醒的怪却弹出"觉醒技/没有装备觉醒技"提示）。
            //   根因：_tipPanel 只在 PointerExit 隐藏，面板关闭时没清；觉醒技槽在 prefab 默认显示。
            if (_tipPanel != null) _tipPanel.gameObject.SetActive(false);
            _tipShowingSlot = -1;
            // ★ 每场战斗重置 自动/倍速：面板是 Cached 复用的，字段会带过来。
            //   放在 OnOpenAsync（每次打开必然经过）比放在 PlayLoop 更可靠（用户实测未生效）。
            // ★★ 2026-10-16 用户："局外好友对战我又想改回可以手动作战了，也可以自动作战，
            //   和平常的探索没有什么区别，就是敌人变成玩家养成好的异兽而已"
            //   ⇒ PvP 不再强制自动，与 PvE 同口径：默认手动，玩家可点"自动战斗"切换。
            _autoBattle = false;
            _speed = 1f;
            // ⛔ 双保险：回放闸必须在这里也是 false。
            //   面板 Cached 复用 ⇒ 上一场「重新挑战」冻结后留下的 _halted=true 会带进来
            //   ⇒ PlayLoop 首帧起就冻结、战斗一动不动（用户实测报障：
            //   "进入战斗不动了，重新运行就行了，其他办法都不行"）。
            //   OnClose 里已复位，这里再兜一次（OnClose 可能被异常路径跳过）。
            _halted = false;
            RefreshSpeedLabel();          // ★ 必须同步刷新按钮文案，否则仍显示上一场的 ×4（用户实测）

            // 遗物 HUD（左上角一排小牌，悬浮看说明）
            // ⛔ 2026-11-04 用户："boss挑战和pvp对战怎么能看到遗物呢" ——
            //   首领试炼与好友对战是**独立玩法**，不吃本局遗物；但这里原来无条件读
            //   `RunSave.Current.Relics`，只要存档里还留着一局没结束，这两种战斗左上角
            //   就会挂出一排**根本不起作用**的遗物（越看越像 bug）。
            //   判定用 SceneFlow 的两个标记：LastWasBossRush（回主城时复位）、LastWasPvp（每场按 req 置位）。
            bool independentMode = WanXiang.Modules.UI.SceneFlow.LastWasBossRush
                                || WanXiang.Modules.UI.SceneFlow.LastWasPvp;
            var hudRun = WanXiang.Run.RunSave.Current;
            RelicHud.Populate(_rootRelics, _relicTemplate,
                independentMode ? null : (hudRun != null ? hudRun.Relics : null),
                def => RelicHud.ShowTip(_relicTipRoot, _relicTipText, def),
                () => RelicHud.HideTip(_relicTipRoot));
            if (_rootRelics != null) _rootRelics.gameObject.SetActive(!independentMode);

            // 两条路径：战斗场景（单位交给 BattleStage2D，本面板只当 HUD）/
            // 主城内直接打（本面板自己画单位视图）
            var sceneCtx = payload as BattleSceneContext;
            if (sceneCtx != null)
            {
                _sceneMode = true;
                _stage = sceneCtx.Stage;
                // 双击单位 → 打开查看面板并暂停战斗
                _stage.OnInspectRequested += ShowUnitInspect;
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

            // ★ 2026-10-16：PvP 与 PvE 同口径（用户："好友对战改回可以手动作战，
            //   和平常的探索没有什么区别"）⇒ 一律按 _manualBattle 决定回放模式，不再对 PvP 特判。
            if (_play == null) _play = new BattlePlayback(_req, _manualBattle);

            // ★ 顶部横幅（用户 2026-10-03 要求）：
            //   第一行 = **节点名称**（"第X幕 · 第 N 节 · 守关（首领）/精英战/遭遇战"），
            //   第二行 = **天时名称 + 该节点天时的效果说明**（WeatherDef.Describe()）。
            //   原来只显示一句天时名，玩家看不到"这个天时到底干嘛的"。
            if (_tmpWeatherName != null)
            {
                var sb = new System.Text.StringBuilder(256);
                if (!string.IsNullOrEmpty(_req.Title)) sb.Append(_req.Title);

                string wx = _req.WeatherName ?? "";
                string detail = (_req.Weather != null) ? _req.Weather.Describe() : null;
                if (!string.IsNullOrEmpty(detail))
                    wx = string.IsNullOrEmpty(wx) ? detail : wx + "：" + detail;

                if (!string.IsNullOrEmpty(wx))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("<size=80%>").Append(wx).Append("</size>");   // 效果小一号，主次分明
                }
                _tmpWeatherName.text = sb.ToString();
            }
            if (_rootWeather != null)
                _rootWeather.SetActive(!string.IsNullOrEmpty(_req.WeatherName) ||
                                       !string.IsNullOrEmpty(_req.Title));

            // 场景模式下单位由舞台渲染，HUD 不再画一遍
            if (_sceneMode) ClearUnitViews();
            else BuildUnitViews();

            RunTask(PlayLoop);
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            CancelCaptureAim();
            ClearUnitViews();
            _play = null;
            // ⛔⛔ **必须复位回放闸**（用户实测报障："进入战斗不动了，重新运行才行，其他办法都不行"）：
            //   面板是 Cached 复用的（关闭不销毁）—— 点「重新挑战」确认后 _halted 留 true，
            //   CloseSelf 触发 OnClose，若不清，下次战斗复用同一实例 ⇒ PlayLoop 从第一帧就被
            //   永久冻结 ⇒ 战斗完全不动、技能按钮全灰（截图：第1/48回合停住）。
            //   "重新运行 Unity"之所以能好：那是新实例，_halted 默认 false。
            //   放在 OnClose（而非只在 OnOpenAsync）是因为关闭是**唯一必然发生**的路径。
            _halted = false;
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

            // ★ 悬停棋盘上的异兽 → 状态列表（用户定案：触发走世界空间，鼠标直接指单位）
            UpdateUnitStatusHover();
            PollCaptureClick();

            if (!_playing) return;
            // ★ 回放闸：冻结期间也不推进舞台动画/按钮刷新（战场要真的"停住"，用户 2026-10-15）。
      if (_halted) return;
      if (State != UIPanelState.Opened) return;      // 被上层盖住/暂停时不推进

            // ★ 每帧兜底按钮状态（**不分场景/非场景模式**）：
            //   只有"等我方下令 + 回合始结算事件已播完"才给点（CanCommandNow），否则置灰。
            //   以前只看 AwaitingCommand ⇒ 结算还在飘字时就能点（用户 2026-10-13 报障）。
            //   这里管**两个方向**：给点 → 按规则点亮(RefreshActionBar)；不给点 → 全置灰 + 收觉醒槽。
            if (_play != null)
            {
                bool canAct = CanCommandNow();
                if (canAct != _actionEnabled)
                {
                    _actionEnabled = canAct;
                    if (canAct)
                    {
                        RefreshActionBar();        // 结算播完、轮到我方 → 按规则逐条点亮
                        RefreshComboButton();
                    }
                    else
                    {
                        SetSkillButtonsInteractable(false);
                        // 觉醒技槽一并收起：敌人回合/结算时不该露着（避免误悬浮弹出"没有装备觉醒技"）
                    }
                }
            }

            // ★ 2026-10-31 防卡死看门狗：
            //   用户实测"点重新挑战→关掉确认框→战斗永远不动"。
            //   根因是 `_halted` 被置 true 后没有任何人复位（确认框被非常规路径关掉时，
            //   `await Confirm` 不是唯一的恢复者）。这里兜一层：冻结超过 1.5s 且
            //   既没有查看面板、也没有弹窗 ⇒ 强制解冻并告警。
            if (_halted)
            {
                if (_haltedAt <= 0f) _haltedAt = Time.unscaledTime;
                bool inspectReallyOpen = _inspectPanel != null &&
                                         _inspectPanel.State != UIPanelState.Closed &&
                                         _inspectPanel.State != UIPanelState.None;
                // ⛔ 2026-10-31 二次修复：原先判 `_inspectOpen` 布尔，面板被异常关闭时它永远是 true
                //   ⇒ 看门狗一次都不触发（用户："还是一样动不了"）。改成**看实例的状态**。
                //   另加硬上限 6s：无论什么理由，冻结都不能超过 6 秒。
                if (!inspectReallyOpen && !Dialog.IsAnyOpen &&
                    (Time.unscaledTime - _haltedAt > 1.5f || Time.unscaledTime - _haltedAt > 6f))
                {
                    Debug.LogWarning("[BattlePanel] 冻结超时（无弹窗/无查看面板）—— 强制解冻，避免卡死");
                    _halted = false;
                    _inspectOpen = false;
                    _inspectPanel = null;
                    _haltedAt = 0f;
                }
            }
            else _haltedAt = 0f;

            if (_stage == null) return;                    // 舞台推进才需要 _stage
            // ★ 命中顿帧：受击/暴击的瞬间冻结舞台动画几帧（dt=0），打出"顿"的打击感。
            //   倍速越高顿帧越短（除以 _speed），避免高速下变成长时间卡顿。
            float sdt = Time.deltaTime * Mathf.Max(1f, _speed);
            if (_hitStop > 0f)
            {
                _hitStop -= Time.deltaTime;
                if (_hitStop < 0f) _hitStop = 0f;
                sdt = 0f;                                  // 冻结：单位抖动/冲锋/下沉全停一拍
            }
            // 舞台动画按倍速推进；但大招演出（cut-in/镜头）用**真实 dt**，不被倍速带快。
            // ⛔ 2026-10-30 用户："大招往内聚的冲击波一卡一卡的" ——
            //   原来顿帧时 `sdt == 0 → fxDt 也传 0`，把**演出时钟**一起冻了：
            //   多段大招每段命中都置一次 _hitStop ⇒ 蓄力环冻→跳→冻→跳。
            //   顿帧只该冻"舞台动作"（冲锋/抖动/下沉），不该冻 shader 进度。
            float fxDt = Mathf.Min(0.05f, Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : Time.deltaTime);
            _stage.Step(sdt, fxDt);
        }

        /// <summary>
        /// 刷新连携按钮（只在**待令单位变化**时调用，不进每帧路径）。
        /// AvailableCombos 每次会分配新 List、还要找子节点 —— 逐帧调它就是卡顿源。
        /// </summary>
        /// <summary>
        /// 转圈控件绑定（prefab 节点 <c>Img_AutoSpin</c>，美术与位置都在 prefab 里改）。
        /// ⚠ <b>禁止运行时建 UI</b> —— 早期版本用 <c>new GameObject("Img_AutoSpin")</c> +
        ///   AddComponent&lt;Image&gt; 在这里拼出来，违反"UI 一律 prefab"铁律，用户也看不到/改不了。
        ///   现在只做兜底自愈：prefab 缺节点时才补，且补出来的节点带完整美术与位置。
        /// </summary>
        private void EnsureAutoSpin()
        {
            if (_autoSpin == null)
            {
                // prefab 里没有该节点（老存档/生成器产物）→ 这里才补一个，
                // 但依然要跟 prefab 同规格：同一张美术、同一位置，用户在 prefab 里也能照着改。
                var go = new GameObject("Img_AutoSpin", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var img = go.AddComponent<Image>();
                var spin = Resources.Load<Sprite>("Battle/UI_Battle_Spin");
                if (spin != null) { img.sprite = spin; img.color = Color.white; }
                else { img.color = new Color(0.79f, 0.63f, 0.39f, 1f); }   // 金色占位
                img.raycastTarget = false;
                var r = (RectTransform)go.transform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
                r.anchoredPosition = new Vector2(0f, -84f);
                r.sizeDelta = new Vector2(72f, 72f);
                go.SetActive(false);
                _autoSpin = r;
                Debug.LogWarning("[BattlePanel] prefab 缺 Img_AutoSpin 节点，已按同规格兜底创建（建议把该节点补进 Panel_Battle.prefab）。");
            }
        }

        /// <summary>刷新倍速按钮文案（必须与 _speed 同步，避免显示与状态不一致）。</summary>
        private void RefreshSpeedLabel()
        {
            if (_btnSpeed == null) return;
            var label = _btnSpeed.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "×" + _speed.ToString("0.##");
        }

        /// <summary>真正交接：场景模式回主城（弹结算），否则就地开结算面板。</summary>
        /// <summary>
        /// 战后血量结算（2026-11-02 用户定案，取代原来的「阵亡=本局失去」）。
        /// ------------------------------------------------------------------
        /// 规则：**每只参战异兽的血量 = 本场剩余 + 50%，上限 100%**。
        ///   · 阵亡（0%）= 0 + 50 ⇒ **以 50% 血复活**，不再从队伍/拥有里移除；
        ///   · 存活（如 30%）= 30 + 50 = 80%，"在剩下的基础上加 50"；
        ///   · 没上场的队员不动（没打就没有收益）。
        /// ⛔ 旧实现（2026-10-04）是"阵亡直接从 Team/Collection 删除，灵市重新买"——
        ///   与"50% 复活"冲突，用户实测报"异兽直接消失了"。
        /// ⚠ 只在**打赢（过节点）**时结算，见 HandOff；战败/平局撤退/重挑不结算。
        /// PvP 不影响本局阵容，直接跳过。
        /// </summary>
        private void RecordHpAfterBattle()
        {
            if (SceneFlow.LastWasPvp) return;
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _play == null || _play.State == null) return;
            if (run.TeamHpPct == null)
                run.TeamHpPct = new System.Collections.Generic.Dictionary<string, int>();

            int gain = WanXiang.Run.ProgressionTuning.ReviveHpPct;   // 50
            string log = "";
            var units = _play.State.UnitsOf(WanXiang.Battle.Core.TeamSide.Player);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.Def == null) continue;
                string id = u.Def.Id;
                if (string.IsNullOrEmpty(id)) continue;

                int pct = u.MaxHp > 0
                        ? UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(100f * u.Hp / u.MaxHp), 0, 100)
                        : 0;
                int next = UnityEngine.Mathf.Clamp(pct + gain, 0, 100);
                run.TeamHpPct[id] = next;
                log += id + " " + pct + "%→" + next + "%" + (u.IsAlive ? "" : "(阵亡→复活)") + "  ";
            }

            WanXiang.Run.RunSave.SaveCurrent();
            UnityEngine.Debug.Log("[BattlePanel][血量] 战后结算（剩余 +" + gain + "%，上限 100%）：" + log);
        }

        private bool _inspectOpen;
        private UnitInspectPanel _inspectPanel;   // 实例（看门狗要判它是否真的还开着）

        /// <summary>双击单位：暂停战斗，弹出正式查看面板（崩铁式）。</summary>
        private void ShowUnitInspect(string name, string stats, string status, string skills,
                                     string mech, Color accent)
        {
            if (_inspectOpen) return;
            _inspectOpen = true;
            _halted = true;                       // ← 战斗暂停（PlayLoop / QTE 都认这个开关）

            // 秘境镇守：完整机制文案在 Campaign 层（RealmChampionDef.MechanicText），
            // Battle 层拿不到 ⇒ 约定格式 `@realm:<rcId>|<短标签>`，这里反查补全。
            if (!string.IsNullOrEmpty(mech) && mech.StartsWith("@realm:"))
            {
                int bar = mech.IndexOf('|');
                string rcId = bar > 7 ? mech.Substring(7, bar - 7) : mech.Substring(7);
                string tag = bar >= 0 && bar + 1 < mech.Length ? mech.Substring(bar + 1) : "";
                var full = RealmMechanicText(rcId);
                mech = string.IsNullOrEmpty(full) ? tag : full;
            }

            var data = new UnitInspectData
            {
                Name = name, Stats = stats, Statuses = status,
                Skills = string.IsNullOrEmpty(skills) ? "" : skills,
                Mechanic = mech, Accent = accent,
            };
            OpenInspectAsync(data).Forget();
        }

        /// <summary>按镇守 id（rc_xxx）反查 Campaign 里的完整机制文案。</summary>
        private static string RealmMechanicText(string rcId)
        {
            var realms = WanXiang.Campaign.RealmCatalog.All;
            if (realms == null) return null;
            for (int i = 0; i < realms.Count; i++)
            {
                var r = realms[i];
                if (r == null || r.Champions == null) continue;
                for (int c = 0; c < r.Champions.Length; c++)
                {
                    var ch = r.Champions[c];
                    if (ch != null && !string.IsNullOrEmpty(ch.HookId) && ch.HookId == rcId)
                        return ch.MechanicText;
                }
            }
            return null;
        }

        private async UniTask OpenInspectAsync(UnitInspectData data)
        {
            var panel = await OpenPanelAsync<UnitInspectPanel>(data);
            _inspectPanel = panel;
            if (panel != null)
                panel.OnClosed = () => { _inspectOpen = false; _inspectPanel = null; _halted = false; };
            else { _inspectOpen = false; _inspectPanel = null; _halted = false; }
        }

        /// <summary>
        private void HandOff(ResultRequest result)
        {
            // ★ 捕获已移入战斗内（Btn_Capture + 精灵球动画），胜利交接不再弹捕获询问。
            HandOffCore(result);
        }

        private void HandOffCore(ResultRequest result)
        {
            // ★ 首领试炼：不结算、不进节点图 ⇒ 直接播转场回试炼面板
            //   （用户 2026-10-25："战斗胜利怎么就回到节点地图，还弹出了面板"）
            if (SceneFlow.LastWasBossRush)
            {
                Debug.Log("[BattlePanel] 首领试炼结束（" + (result != null && result.Win ? "胜" : "非胜") +
                          "）→ 回试炼面板，不弹结算");
                SceneFlow.ReturnToBossRushPanel(result);
                return;
            }

            // ★ 战后血量结算时机（2026-11-02 改）：**只在过节点时结算**
            //   打赢才把"剩余血量 +50%"写进 TeamHpPct；战败 / 平局撤退 / 重新挑战都不写
            //   （输了可以无限重试，没有惩罚 —— 用户已知并接受）。
            if (result.Win && !result.Draw)
            {
                RecordHpAfterBattle();
                // ★ 同一存档事务里把"待落地的节点"也持久化：否则"胜利但没点结果面板就退游戏"
                //   会留下"阵亡已扣、节点未过"的半截状态（用户 2026-10-20 实测 bug）。
                var runW = WanXiang.Run.RunSave.Current;
                if (runW != null && !SceneFlow.LastWasPvp)
                {
                    if (CampaignPanel.PendingCommit >= 0)
                        runW.PendingBattlePass = CampaignPanel.PendingCommit;

                    WanXiang.Run.RunSave.SaveCurrent();

                    // ★ 2026-10-20 用户："历程应该是实时记录的，每一把都是一个历程，
                    //   随着异兽/遗物多少变化，最后定格于战败或通关" ——
                    //   每场战斗后 upsert 本局记录（key=RunSeed，同局覆盖刷新）。
                    try { WanXiang.Modules.UI.ResultPanel.PushRunHistory(runW); }
                    catch (System.Exception ex) { Debug.LogWarning("[BattlePanel] 实时历程写入失败：" + ex.Message); }
                }
            }
            if (_sceneMode)
            {
                // B2 骨架：战斗统计回传本局（道痕来源：反应/最大伤害/首领/阵亡/速通）
                var stEnd = _play != null ? _play.State : null;
                var runEnd = WanXiang.Run.RunSave.Current;
                if (stEnd != null && runEnd != null)
                {
                    // B4-3：后土试炼胜利 → ClearedHoutu（真结局前置）
                    if (result != null && result.Win && runEnd.PendingHoutuTrial)
                    {
                        runEnd.PendingHoutuTrial = false;
                        runEnd.ClearedHoutu = true;
                        Debug.Log("[BattlePanel] ★后土试炼通过——真结局前置达成");
                    }
                    runEnd.AddBattleStats(stEnd.StatReactions, stEnd.StatMaxHit,
                                          stEnd.StatBossKills, stEnd.StatDeaths,
                                          stEnd.StatFastWins);
                    // B2：战斗后即评估道痕（幂等——Grant 取最高星，重复授予无害）
                    foreach (var kv in WanXiang.Battle.Core.DaoMarkRules.Evaluate(runEnd.Stats))
                        WanXiang.Modules.UI.DaoMarkPrefs.Grant(kv.Key, kv.Value);
                    // B5-3：悟道点发放（局末口径：N×2 + 通关 +10；首通幕 ×5 = 幕胜利时按 BestAct 差值）
                    if (result != null && result.Win && runEnd.ChallengeLevel > 0)
                    {
                        int firstActs = 0;
                        var metaI = WanXiang.Meta.MetaStore.Ensure();
                        if (metaI != null && runEnd.Act > metaI.BestActReached)
                        {
                            firstActs = runEnd.Act - metaI.BestActReached;
                            metaI.BestActReached = runEnd.Act;
                        }
                        int gain = WanXiang.Battle.Core.InspirationRules.WudaoGain(
                            runEnd.ChallengeLevel, runEnd.EndingId == "end_houtu" || runEnd.Act >= 5, firstActs);
                        metaI.WudaoPoints += gain;
                        WanXiang.Meta.MetaStore.Save();
                        Debug.Log("[BattlePanel] 悟道点 +" + gain + "（余额 " + metaI.WudaoPoints + "）");
                    }

                    // B4-3：终局判定——幕 5 胜利即终局（结局互斥顺序见 endings.csv）
                    if (result != null && result.Win && runEnd.Act >= 5 && string.IsNullOrEmpty(runEnd.EndingId))
                    {
                        var ectx = new WanXiang.Battle.Core.EndingsRules.Ctx
                        {
                            MarkCount = runEnd.TuMarkCount(),
                            HoutuCleared = runEnd.ClearedHoutu,
                            N = runEnd.ChallengeLevel,
                            Captures = runEnd.Stats.Captures,
                            RelicCount = runEnd.Relics != null ? runEnd.Relics.Count : 0,
                            Realm = runEnd.Realm,
                        };
                        var ed = WanXiang.Battle.Core.EndingsRules.Evaluate(ectx);
                        if (ed != null)
                        {
                            runEnd.EndingId = ed.Id;
                            WanXiang.Modules.UI.EndingUnlocks.Mark(ed.Id);
                            Debug.Log("[BattlePanel] ★终局判定：" + ed.Id + "「" + ed.Name + "」" + (ed.IsTrue ? "（真结局）" : ""));
                        }
                    }
                    WanXiang.Run.RunSave.Save(runEnd);
                    Debug.Log("[BattlePanel] 本局统计回传：反应 " + stEnd.StatReactions
                              + "｜最大伤害 " + stEnd.StatMaxHit
                              + "｜首领 " + stEnd.StatBossKills
                              + "｜阵亡 " + stEnd.StatDeaths
                              + "｜速通 " + stEnd.StatFastWins);
                }

                // 先关自己再切场景：UI 根节点是 DontDestroyOnLoad 的，
                // 不关的话战斗 HUD 会跟着到主城、压在结算面板后面。
                CloseSelf();
                // ★ PvP：不弹结果面板、不回节点地图 —— 直接回好友对战面板（用户要求）。
                if (SceneFlow.LastWasPvp)
                {
                    // ⛔ 这里也必须把 result 传下去，否则打赢也播「败退」
                    SceneFlow.ReturnToPvpPanel(result);
                    Debug.Log("[BattlePanel][调试] PvP 结束 → 回主城直接重开对战面板（无结算）");
                    return;
                }
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

        private void OnSpeedClicked()
        {
            _speed = _speed >= 3.9f ? 1f : (_speed >= 1.9f ? 4f : 2f);
            var label = _btnSpeed != null ? _btnSpeed.transform.Find("Tmp_Label") : null;
            if (label != null)
            {
                var t = label.GetComponent<TMP_Text>();
                if (t != null) t.text = "x" + _speed;
            }
        }

      private async void OnLeaveClicked()
        {
      // ★★ 第一步就冻结战场（用户 2026-10-15："点击的时候战斗应该停止"）。
        //   为什么必须先冻结：原来先弹确认框、战斗照跑 —— 自动战斗下等玩家点"确定"时
        //   仗早打完，Outcome != Ongoing ⇒ 下面 early-return ⇒ 按钮点了没反应、
        //   战斗面板永远挂在节点地图上关不掉（用户实测截图：确认框浮在节点地图上）。
   //   冻结后仗不会自己结束，early-return 那条路也就不会再误触。
     _halted = true;
 bool ok = false;
     try
       {
    // ★ 重新挑战前先确认（用户 2026-09-29 要求）：避免误触直接退出本场。
            ok = await Dialog.Confirm("重新挑战",
      "放弃本场战斗，回到编队界面重新布阵重新打？\n（不结算、不记败、不处理阵亡）");
     }
   finally
    {
       // 无论确认框怎么结束（确认/取消/异常）都要解冻，否则取消后战斗永远冻住。
    //   ⚠ 必须在这里解，而不是在分支里：取消路径也必须恢复可打（用户实测报障）。
       if (!ok) _halted = false;
        }
  if (!ok) return;   // 取消 = 留在战斗里继续打（已解冻）

            // 冻结期间 Outcome 不可能变（战斗没推进），所以这里可以安全地重入。
            // 若面板已被别处关闭（战斗在确认框期间被结算接管），就别抢流程。
            if (_play == null || _play.State == null) return;
            if (_play.State.Outcome != BattleOutcome.Ongoing) { _halted = false; return; }

      // ★ 重新挑战（用户 2026-09-29 要求）：放弃本场，回编队界面重新布阵重新打。
 //   不结算、不记败、不处理阵亡（RunState.Team 本就未被本场修改，死兽仍在队伍里）。

            // ★★ 2026-10-25：PvP 重战**必须先分支**，不能先 CloseSelf ——
            //   上一版先 CloseSelf 再用 BattleRequest 重开，面板走了"非场景模式"，
            //   在已有舞台上又画了一遍单位 ⇒ 棋盘上出现两套单位（用户实测截图）。
            //   现在交给 BattleSceneDriver.Restart：清舞台 → 重建 playback → CloseAll → 重开面板。
            if (SceneFlow.LastWasBossRush)
            {
                // 首领试炼：重挑回试炼面板（不结算、不进节点图）
                SceneFlow.ReturnToBossRushPanel();
                return;
            }

            if (SceneFlow.LastWasPvp)
            {
                // ★ 2026-10-25 用户："我要的是败退转场回到对战面板" ——
                //   播败退转场 → 主城 → 自动打开好友对战面板（分享码队伍还在）。
                SceneFlow.ReturnToPvpPanel();
                return;
            }

            CloseSelf();       // 关闭会取消 PanelToken ⇒ PlayLoop 被中止（RunTaskInternal 捕获 OperationCanceledException）
   var node = SceneFlow.PendingBattleNode;
          if (node != null && !SceneFlow.LastWasPvp)
      {
            // 标记重挑战：主城入口（MainSceneEntry）会直接打开编队界面（叠在节点地图之上）
      SceneFlow.PendingRechallengeNode = node;
                // ★ 重挑 = 不通过：清掉"点节点时写的待落地"，避免回图时被当成已通过
                CampaignPanel.PendingCommit = -1;
                SceneFlow.ExitBattle(null);   // 回主城（不带结算）→ MainSceneEntry 接管
      }
    else
    {
        // 试炼等其它无节点战斗：直接回主城
        SceneFlow.ExitBattle(null);
    }
        }

        // ================================================================
        //  回合制 v2.1 P1-3：战记操作区
        //  ------------------------------------------------------------------
        //  代码自建（不改 prefab / 生成器）：战记三槽 = SkillType 的三个枚举值，
        //  点一下就把指令交给 BattlePlayback.SubmitCommand，目标暂交给 AI（-1）。
        //  「自动战斗」= 反复下 -1 指令（= 交给 AI），与 v2.1 文档第 13 节一致。
        // ================================================================
        [SerializeField] private RectTransform _actionBar;    // Root_Action（生成器产物）
        [SerializeField] private Button[] _actorBtns;         // B5 底栏：5 只异兽名按钮（点=选它为下一行动者）
        [SerializeField] private TMP_Text[] _actorNameTmps;   // B5 底栏：异兽名显示
        [SerializeField] private Button[] _actorSkillBtns;    // B5 底栏：5×3 技能按钮（点=该异兽立即用该技能）
        [SerializeField] private TMP_Text _tmpActor;          // Tmp_Actor 当前待令单位
        [SerializeField] private Button _btnAutoBattle;       // Btn_AutoBattle
        [SerializeField] private Button _btnCombo;            // Btn_Combo（连携）
        [SerializeField] private RectTransform _autoSpin;     // Tmp_AutoSpin（自动战斗转圈）
        private bool _autoBattle;

        // ================================================================
        //  战记悬浮提示（hover → 左侧弹出 / 离开消失，DOTween 做动画）
        // ================================================================
        private int _tipShowingSlot = -1;                      // 当前展示的战记槽（防抖：同一按钮不重复建动画）
        [SerializeField] private RectTransform _tipPanel;       // Root_SkillTip（生成器产物）
        [SerializeField] private TMP_Text _tipText;            // Tmp_Tip
        private CanvasGroup _tipGroup;
        private DG.Tweening.Tween _tipTween;

        // ================================================================
        //  行动顺序（右上角）—— 头像 + 名字，一眼看清轮到谁
        //  ------------------------------------------------------------------
        //  · 顺序取自核心 BuildActionOrderInto（按有效速度降序，我敌混排）= 单一真源
        //  · **刷新守卫**：只有"顺序或当前行动者变化"时才重建 UI。
        //    之前每帧重建 TMP 文本 ， 每帧触发 mesh 重建 + 大量 GC ， Unity 卡死（用户实测）。
        //    现在每帧只做一次 10 单位的排序 + 指纹比对，开销可忽略。
        // ================================================================
        private const int OrderRowCount = 8;
        [SerializeField] private RectTransform _orderPanel;     // Root_OrderList（生成器产物）

        // 遗物 HUD（左上角一排小牌 + 悬浮说明；见 RelicHud / Editor/UITool/BuildRelicHudPatch.cs）
        [SerializeField] private RectTransform _rootRelics;    // Root_Relics
        [SerializeField] private GameObject _relicTemplate;    // Item_Relic
        [SerializeField] private GameObject _relicTipRoot;     // Root_RelicTip
        [SerializeField] private TMP_Text _relicTipText;       // Tmp_RelicTip
        [SerializeField] private RectTransform[] _orderRows;    // OrderRow_0..7
        private Image[] _orderHeads;                            // 行内引用：生成器产物里按名字取
        private TMP_Text[] _orderNames;
        private TMP_Text _orderTitle;                           // Tmp_OrderTitle（运行时按名取，显示"第 N 回合 · 行动顺序"）
        private string _orderActorId;                           // 当前"正在行动"的单位（只在 ActionBegin 更新，避免伤害结算刷屏）
        private string _lastOrderTitle;

        // 状态悬浮列表（Root_StatusTip）—— 悬停「行动顺序」的行 → 列出该单位的全部状态 + 效果说明。
        //   参考《杀戮尖塔》：它把 buff/debuff 摊成一个列表，玩家不用猜图标。
        [SerializeField] private RectTransform _statusTip;       // Root_StatusTip
        [SerializeField] private TMP_Text _statusTipTitle;       // Tmp_StatusTipTitle
        [SerializeField] private RectTransform _statusTipRows;   // Root_StatusRows
        [SerializeField] private RectTransform _statusTipItem;   // Item_StatusRow（模板行，运行时克隆）
        private readonly System.Collections.Generic.List<BattleUnit> _orderBuf =
            new System.Collections.Generic.List<BattleUnit>(16);
        private int _lastOrderStamp = -1;
        private string _lastActorId;
        private int _lastDecisionSeq = -1;   // 上次已刷新的决策序号（替代原"待令单位 id"，见 PlayLoop 注释）

        /// <summary>主循环"推进事件"步数的看门狗上限。**只统计事件步**，等令帧不计、且每轮等令清零。
        /// 触顶只告警并清零，绝不中途退出循环（退出会把没打完的仗当成打完 ， 没死却判负）。</summary>
        private const int GuardLimit = 20000;
        private string _lastActorLine;
    }
}
