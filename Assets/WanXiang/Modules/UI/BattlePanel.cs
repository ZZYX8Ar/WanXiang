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
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] private bool _manualBattle;
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
    }
}
