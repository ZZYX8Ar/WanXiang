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
        private float _speed = 1f;   // 默认 1x：先能看清，再谈加速
        private bool _autoCast = true;
        private bool _playing;      // 回放中（控制舞台是否逐帧推进）

        // ---- 战斗场景模式（单位由 BattleStage2D 渲染，本面板只当 HUD）----
        private bool _sceneMode;
        private BattleStage2D _stage;

        // 技能预览的格位高亮用的复用列表（避免每次悬浮都分配）
        private readonly System.Collections.Generic.List<WanXiang.Battle.Core.BattleUnit> _previewTargets
            = new System.Collections.Generic.List<WanXiang.Battle.Core.BattleUnit>(10);
        private readonly System.Collections.Generic.List<int> _previewAllyCells = new System.Collections.Generic.List<int>(9);
        private readonly System.Collections.Generic.List<int> _previewFoeCells = new System.Collections.Generic.List<int>(9);
        // ⚠ 原 `private int _eventIndex` 已删：事件游标的真值只有 BattlePlayback.EventIndex 一份。
        //   两份计数器必然漂移（Step() 等令时返回 true 却不推进）⇒ 循环卡死（实测：涨到 46 万）。

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
            if (_btnSpeed != null) _btnSpeed.onClick.AddListener(OnSpeedClicked);
            if (_btnAuto != null) _btnAuto.onClick.AddListener(OnAutoClicked);
            if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            ApplyPvpUiMode();   // ★ 每次进入战斗面板都重新应用 PvP UI 模式（时机修正）
            // ★ 每场战斗重置 自动/倍速：面板是 Cached 复用的，字段会带过来。
            //   放在 OnOpenAsync（每次打开必然经过）比放在 PlayLoop 更可靠（用户实测未生效）。
            _autoBattle = SceneFlow.LastWasPvp;   // ★ PvP=自动；正常战斗=手动(玩家自己下令)，与回放 manual 一致
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

            // ★ PvP 走全自动回放（manual=false，与 BattleSceneDriver 一致）；
            //   单机非场景战保持 _manualBattle。auto 模式整场已预模拟，无需 SubmitCommand。
            if (_play == null) _play = new BattlePlayback(_req, _manualBattle && !SceneFlow.LastWasPvp);

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

        /// <summary>真正交接：场景模式回主城（弹结算），否则就地开结算面板。</summary>
        private void HandOff(ResultRequest result)
        {
            if (_sceneMode)
            {
                // 先关自己再切场景：UI 根节点是 DontDestroyOnLoad 的，
                // 不关的话战斗 HUD 会跟着到主城、压在结算面板后面。
                CloseSelf();
                // ★ PvP：不弹结果面板、不回节点地图 —— 直接回好友对战面板（用户要求）。
                if (SceneFlow.LastWasPvp)
                {
                    SceneFlow.PendingPvpReturn = true;
                    SceneFlow.ExitBattle(null);   // 不带结算 → 主城不弹 ResultPanel
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
        private int _lastDecisionSeq = -1;   // 上次已刷新的决策序号（替代原"待令单位 id"，见 PlayLoop 注释）

        /// <summary>主循环"推进事件"步数的看门狗上限。**只统计事件步**，等令帧不计、且每轮等令清零。
        /// 触顶只告警并清零，绝不中途退出循环（退出会把没打完的仗当成打完 ⇒ 没死却判负）。</summary>
        private const int GuardLimit = 20000;
        private string _lastActorLine;
    }
}
