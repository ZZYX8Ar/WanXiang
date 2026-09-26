// ============================================================================
//  万相 · 战斗面板 · Playback（从 BattlePanel.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 字段仍声明在主文件里，这里只放方法（共享作用域，照旧可访问）。
//  ⚠ 只挪位置，没改任何一行逻辑。
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
    public sealed partial class BattlePanel : UIPanelBase
    {

        [SerializeField] private TMP_Text _tmpRound;            // Tmp_Round
        /// <summary>回合制手动模式：开启后我方行动前等玩家下令（需操作区就绪）。</summary>
        [SerializeField] private bool _manualBattle = true;   // 回合制手动（操作区已就位；关掉即全自动）
        [SerializeField] private GameObject _rootWeather;       // Root_WeatherBanner
        [SerializeField] private TMP_Text _tmpWeatherName;      // Tmp_WeatherName
        [SerializeField] private RectTransform _rootStage;      // Root_Stage
        [SerializeField] private RectTransform _rootUnits;      // Root_Units（单位容器，运行时填充）
        [SerializeField] private RectTransform _hpBarTemplate;  // Item_HpBar（模板，默认隐藏）
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

        // ================================================================
        //  回放
        // ================================================================

        private async UniTask PlayLoop(CancellationToken ct)
        {
            ApplyFrames();
            if (_tmpLog != null) _tmpLog.text = "战斗开始";
            _playing = true;
            // ★ 每场战斗重置自动/倍速：面板是 Cached 复用的，字段会从上场带过来。
            //   PvP 必须保持自动战斗（否则异兽卡在等令、不动）；单机正常战斗=手动（玩家自己下令）。
            _autoBattle = SceneFlow.LastWasPvp;
            _speed = 1f;

            int guard = 0;
            while (!_play.Finished)
            {
                // ★ 顺序铁律：**先把已产生的事件全部播完，再考虑等令**。
                //   模拟推进是"跑一段"（可能一次性产生多个事件），若一看到 AwaitingCommand
                //   就停住等令，那段事件会被跳过 —— 表现就是"第一次攻击没效果、之后才补播"（实测）。
                // ⚠⚠ guard 只统计"**推进事件**"的步数，**绝不能把等玩家下令的帧算进去**。
                //   以前写成 `while (... && guard++ < 20000)` ⇒ 每轮都 ++（含 await Yield 的等令帧）
                //   ⇒ 一场长仗 + 玩家思考时间就把 20000 用完 ⇒ 主循环**中途退出**：
                //      · 旧版：掉进 DrainRest 的 while(StepOnce()) ⇒ 死循环 = 卡死
                //      · 新版：DrainRest 立刻退出 ⇒ FinishAndLeave ⇒ **没死却判负**（用户实测报障）
                //   实测日志："主循环步数触顶(20000) 但战斗未结束 —— awaiting=True seq=57"。
                //   触顶只告警并清零，**绝不中途结算**。
                // ★ 等令 ⇒ **立刻**亮出操作区，不等"开局那一串棋盘结算"播完。
                //   以前这段放在"播事件"分支之后 ⇒ 玩家要干等约 1.5 秒（TurnStart + 相生回复 + 出手序列）
                //   才看到面板，观感是"异兽自动打了一下才轮到我"（用户实测报障；
                //   实测出手次数其实是 0/0，那串只是棋盘结算没有攻击）。
                // ⚠ 这里**不 continue**：事件必须继续往下播，否则会出现"第一次攻击没效果"（旧踩坑）。
                if (_play.AwaitingCommand && _play.DecisionSeq != _lastDecisionSeq)
                {
                    _lastDecisionSeq = _play.DecisionSeq;
                    LogActionBarState("新决策点 seq=" + _play.DecisionSeq);   // ★ 临时诊断
                    RefreshActionBar();                   // 亮出操作区
                    RefreshComboButton();                 // 连携按钮同步刷一次
                    RefreshOrderList();                   // ⚠ 行动条也要刷 —— 等令期间没有事件推进，
                                                          //   不刷的话高亮还停在"上一个异兽"（用户实测）
                }

                if (HasPendingEvent())
                {
                    if (!StepOnce()) break;
                    if (++guard > GuardLimit)
                    {
                        Debug.LogWarning("[BattlePanel] 事件步数异常(>" + GuardLimit + ") —— 已清零继续");
                        guard = 0;
                    }
                    var k2 = _play.Current.Kind;
                    float w2 = IntervalOf(k2) / Mathf.Max(1f, _speed);
                    if (MustSee(k2)) w2 = Mathf.Max(w2, 0.36f / Mathf.Max(1f, _speed));
                    await UniTask.Delay(TimeSpan.FromSeconds(w2), cancellationToken: ct);
                    continue;
                }

                // ⚠ 回合制：等玩家下令时必须**原地等**，绝不能 break ——
                //    break 会被下方收尾逻辑当成"播完"，导致一进战斗就直接结算（踩过）。
                //  （"亮出操作区"已提到循环开头，见上方注释：要立刻亮、但事件继续播）
                if (_play.AwaitingCommand)
                {
                    if (_autoBattle)
                    {
                        _play.SubmitCommand(-1, -1);          // 自动战斗：AI 代下令
                        _lastDecisionSeq = -1;                // 自动推进：下一轮必然重刷
                        RefreshActionBar();
                    }
                    guard = 0;                                // ★ 等玩家下令：合法且时长不可控 ⇒ 计数清零
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    continue;                                 // 不推进事件、不退出循环
                }

                if (!StepOnce()) break;
                if (++guard > GuardLimit)
                {
                    Debug.LogWarning("[BattlePanel] 事件步数异常(>" + GuardLimit + ") —— 已清零继续");
                    guard = 0;
                }

                // 节奏由事件语义决定；速度倍率只缩放间隔
                float wait = IntervalOf(_play.Current.Kind) / Mathf.Max(1f, _speed);

                // ⚠ 关键事件保底时长：技能释放 / 伤害 / 治疗 / 死亡这些"要看清楚"的事件，
                //   若间隔太小会一闪而过 —— 玩家会觉得"第一次攻击没效果"（实测）。
                //   只给这几类保底，其他事件（回合开始、结算等）保持原节奏，不会拖慢整体。
                if (MustSee(_play.Current.Kind)) wait = Mathf.Max(wait, 0.36f / Mathf.Max(1f, _speed));

                await UniTask.Delay(TimeSpan.FromSeconds(wait), cancellationToken: ct);
            }

            // ★ 走到这里 = 战斗真的打完了（`!_play.Finished` 才会离开循环；
            //   guard 触顶只告警+清零，**不再中途退出** —— 那会把没打完的仗判成结束）。
            await DrainRest(ct);                     // 收尾：分帧走完剩余事件（见 DrainRest 注释）
            _playing = false;
            FinishAndLeave(0.8f, ct);
        }

        /// <summary>是否还有"已产生但未播放"的事件。</summary>
        private bool HasPendingEvent()
        {
            // ⚠ 必须用 BattlePlayback 的**权威游标**，本面板不再自己维护计数（见 EventIndex 注释）
            return _play != null && _play.State != null && _play.EventIndex + 1 < _play.State.Log.Count;
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

            // ⚠ 不要在这里自增任何本地游标：真值只有 _play.EventIndex 一份（见其注释）。
            ApplyFrames();

            var e = _play.Current;
            ApplyEvent(e);
            if (_stage != null) _stage.ApplyEvent(_play.EventIndex, e);

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

        /// <summary>
        /// 收尾排空：把剩余事件/帧走完，但**每 8 步让出一帧**。
        /// ★ 原来这里是 `while (StepOnce()) { }` —— 纯同步空转，一场战斗几千个事件
        ///   全挤在一帧里跑完 ⇒ 主线程占死、Unity 整个界面无响应（用户实测"战斗卡死"）。
        /// </summary>
        private async UniTask DrainRest(CancellationToken ct)
        {
            int n = 0;
            // ⚠⚠ 必须同时判"不在等令"：`BattlePlayback.Step()` 在等令状态下会**返回 true 却不推进**
            //   （它把"在等玩家下令"也当作"还有事没做完"）⇒ 只看 StepOnce() 会**无限空转**，
            //   表现就是"操作区再也不出现、战斗冻结"（实测：面板游标涨到 463667，核心一步没走）。
            while (!_play.AwaitingCommand && StepOnce())
            {
                if (++n % 8 == 0) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (n > 200000) { Debug.LogWarning("[BattlePanel] DrainRest 步数异常，强制退出"); break; }
            }
        }

    }
}
