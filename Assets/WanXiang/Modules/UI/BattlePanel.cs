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
            if (_rootWeather != null) _rootWeather.SetActive(false);
            if (_btnUltimate != null) _btnUltimate.onClick.AddListener(OnUltimateClicked);
            if (_btnSpeed != null) _btnSpeed.onClick.AddListener(OnSpeedClicked);
            if (_btnAuto != null) _btnAuto.onClick.AddListener(OnAutoClicked);
            if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
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

            int guard = 0;
            while (!_play.Finished && guard++ < 20000)
            {
                // ⚠ 回合制：等玩家下令时必须**原地等**，绝不能 break ——
                //    break 会被下方收尾逻辑当成"播完"，导致一进战斗就直接结算（踩过）。
                if (_play.AwaitingCommand)
                {
                    RefreshActionBar();                       // 亮出操作区
                    if (_autoBattle)
                    {
                        _play.SubmitCommand(-1, -1);          // 自动战斗：AI 代下令
                        RefreshActionBar();
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    continue;                                 // 不推进事件、不退出循环
                }

                if (!StepOnce()) break;

                // 节奏由事件语义决定；速度倍率只缩放间隔
                float wait = IntervalOf(_play.Current.Kind) / Mathf.Max(1f, _speed);
                await UniTask.Delay(TimeSpan.FromSeconds(wait), cancellationToken: ct);
            }

            while (StepOnce()) { }                   // 收尾：把剩余事件/帧走完
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
            if (!_playing || _stage == null) return;
            if (State != UIPanelState.Opened) return;      // 被上层盖住/暂停时不推进
            _stage.Step(Time.deltaTime * Mathf.Max(1f, _speed));
        }

        /// <summary>推进一个事件：HUD + 舞台同步刷新。返回 false 表示已播完。</summary>
        private bool StepOnce()
        {
            if (_play == null || _play.Finished) return false;
            if (_play.AwaitingCommand) return true;    // 等下令（由 PlayLoop 处理），不是"播完"✗
            if (!_play.Step()) return false;

            _eventIndex++;
            ApplyFrames();

            var e = _play.Current;
            ApplyEvent(e);
            if (_stage != null) _stage.ApplyEvent(_eventIndex, e);
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

            // 怒气环取我方第一个单位，作为这一局的手感指示器
            if (_imgRage != null && _play != null)
            {
                var mine = _play.State.UnitsOf(TeamSide.Player);
                if (mine.Count > 0)
                {
                    var u = mine[0];
                    _imgRage.fillAmount = u.RageCap > 0f ? Mathf.Clamp01(u.Rage / u.RageCap) : 0f;
                    if (_tmpRage != null) _tmpRage.text = "怒气 " + (int)u.Rage;
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
            while (StepOnce()) { }
            FinishAndLeave(0f, CancellationToken.None);
        }

        /// <summary>战斗收尾：刷终局状态 → 交接给结算（场景模式则回主城）。</summary>
        private void FinishAndLeave(float delay, CancellationToken ct)
        {
            if (_tmpRound != null) _tmpRound.text = "第 " + _play.State.Turn + " 回合";
            if (_tmpLog != null) _tmpLog.text = _play.Summary();

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

            string[] names = { "普攻", "战记", "终结技" };   // 下标 = SkillType 枚举值
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
        }

        // ================================================================
        //  战记悬浮提示（hover → 左侧弹出 / 离开消失，DOTween 做动画）
        // ================================================================
        private RectTransform _tipPanel;
        private TMP_Text _tipText;
        private CanvasGroup _tipGroup;
        private DG.Tweening.Tween _tipTween;

        private void BuildSkillTip()
        {
            if (_tipPanel != null) return;

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
            var u = _play.PendingUnit;

            string title, body;
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
                    body = desc + "\n\n" + body;
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
            _tipTween?.Kill();
            _tipTween = _tipGroup.DOFade(0f, 0.12f).OnComplete(() =>
            {
                if (_tipPanel != null) _tipPanel.gameObject.SetActive(false);
            });
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

            var cfg = stt != null ? stt.Config : null;
            if (_tmpActor != null && u != null)
            {
                string line = "轮到「" + u.DisplayName + "」　灵力 " + mp + "/" + mpMax +
                              "（战记 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点）";
                if (u.GetSkill(SkillType.Ultimate) != null)
                    line += "　元气 " + (int)u.Rage + "/" + (int)u.RageCap;
                _tmpActor.text = line;
            }
            else if (_tmpActor != null)
            {
                _tmpActor.text = "轮到我方行动";
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
            }
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

        private void OnAutoBattleClicked()
        {
            if (_play == null) return;
            _autoBattle = !_autoBattle;
            if (_autoBattle) _play.SubmitCommand(-1, -1);   // -1 = 交给 AI（自动战斗）
            if (_tmpLog != null) _tmpLog.text = _autoBattle ? "自动战斗：开" : "自动战斗：关（等你下令）";
            RefreshActionBar();
        }
    }
}
