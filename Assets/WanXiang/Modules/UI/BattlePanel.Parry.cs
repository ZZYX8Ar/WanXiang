// ============================================================================
//  万相 · 战斗面板 · QTE 窗口（2026-10-14，用户点名参考 33号远征队）
//  ---------------------------------------------------------------------------
//  模拟层在两类时机挂起（NeedParry），本文件负责：
//    · Guard     敌方"带伤害的行动"前 → 时机圈环；多段攻击**每段一个窗口**（连按 N 次）
//    · Finisher  我方终结技打出前   → 时机圈环；完美 ⇒ 伤害加强，失败 ⇒ 减弱
//    · 真实时间计时（与倍速解耦，和终结技演出同一纪律）
//    · 输入：鼠标左键 / 空格。⛔ UI 程序集没有 Unity.InputSystem 引用（CS0234），
//      所以用旧 Input 直读，不走 InputService 上下文（战斗里 UI Map 是关的）
//
//  判定规则：环收缩进度 k∈[0,1]；
//    完美区：|k - 0.80| ≤ half；普通区：|k - 0.80| ≤ half2；
//    超出普通区（按太早/太晚）与超时（k≥1）都算"未挡/失谐"。
//  数值（格挡 0.2/0.6/反震 4%、终结技 1.60/0.45）在 BattleSimulator.Parry.cs。
// ============================================================================

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public sealed partial class BattlePanel
    {
        /// <summary>格挡窗口总时长（真实秒）。static 非 const：留一个运行时可调的口子（手感调参/验证）。</summary>
        private static float ParryWindowSeconds = 1.10f;
        /// <summary>终结技窗口总时长（真实秒）—— 比格挡短：输出向不该拖慢节奏。</summary>
        private static float FinisherWindowSeconds = 0.85f;

        /// <summary>判定中心（环收缩进度 0..1；到 0.80 时环正好套上目标圈）。</summary>
        private const float ParryPerfectCenter = 0.80f;
        /// <summary>完美区半径（格挡用）。</summary>
        private const float ParryPerfectHalf = 0.06f;
  /// <summary>普通区半径（格挡用；手感上"能打出来"的范围）。</summary>
      private const float ParryNormalHalf = 0.22f;

        /// <summary>提示条是否正在展示（主循环用它决定何时淡出收口）。</summary>
  private bool _qteHintShown;

        /// <summary>
        /// QTE 窗口：弹时机圈 → 等玩家点击/超时 → 提交判定。
        /// 由主循环在 _play.AwaitingParry 时调用（原地等，不推进事件）。
        ///
        /// ⚠ **多段连击不需要这里循环**：模拟层已经把 N 个 NeedParry 排好了，
        ///   主循环每收到一个就调本方法一次 ⇒ N 段 = N 次调用，天然"连按 N 次键"。
        ///   段号/总段数由模拟层写在 <c>State.ParrySegmentIndex/Total</c>，UI 只读不写。
        /// </summary>
        private async UniTask RunParryWindowAsync(CancellationToken ct)
        {
            var actor = _play.ParryAttacker;
            string actorId = actor != null ? actor.RuntimeId : null;
            string actorName = actor != null ? actor.DisplayName : "?";

            var st = _play.State;
            bool finisher = st.ParryQteKind == WanXiang.Battle.Core.ParryQteKind.Finisher;
            int segIndex = st.ParrySegmentIndex;
            int segTotal = Mathf.Max(1, st.ParrySegmentTotal);

            if (_stage != null) _stage.ShowParryPrompt(actorId);
            // 行动箭头指向被判定者（ActionBegin 还没发，不点的话箭头停在上一行动者头上）
            if (_stage != null && !string.IsNullOrEmpty(actorId)) _stage.SetTurnActor(actorId);

            // ★ 按键提示（用户："这个 qte 是按什么按键的啊，都没有提示"）：
            //   屏幕中央一条常驻文案；终结技与格挡文案不同，连击带"第 k/n 段"计数。
            if (_stage != null)
            {
                if (finisher) _stage.SetQteHint("空格 / 鼠标左键　终结技·共鸣", segTotal, segIndex);
                else _stage.SetQteHint("空格 / 鼠标左键　格挡", segTotal, segIndex);
            }
            Sfx("sfx_bt_cmd_select");   // 提示音（没有专用素材，先复用指令音）

            float window = finisher ? FinisherWindowSeconds : ParryWindowSeconds;
            // 终结技输出向：容错放宽一档（抢拍也认，别让玩家白丢终结技）
            float perfectHalf = finisher ? ParryPerfectHalf * 2f : ParryPerfectHalf;
            float normalHalf = finisher ? ParryNormalHalf * 1.6f : ParryNormalHalf;

            float t0 = Time.unscaledTime;
            WanXiang.Battle.Core.ParryGrade grade = WanXiang.Battle.Core.ParryGrade.None;

            while (true)
            {
                float k = (Time.unscaledTime - t0) / window;
                if (_stage != null) _stage.SetParryProgress(Mathf.Clamp01(k));

                if (ParryClickedThisFrame())
                {
                    float d = Mathf.Abs(Mathf.Clamp01(k) - ParryPerfectCenter);
                    grade = d <= perfectHalf ? WanXiang.Battle.Core.ParryGrade.Perfect
                          : d <= normalHalf ? WanXiang.Battle.Core.ParryGrade.Normal
                          : WanXiang.Battle.Core.ParryGrade.None;
                    break;
                }
                if (k >= 1f) break;   // 超时 = 未挡 / 失谐
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (_stage != null)
            {
                _stage.HideParryPrompt();
                _stage.ParryFeedback(actorId, (int)grade, finisher);
                // ⛔ 连击时**不要**在这里清提示条 —— 下一段紧接着就弹，淡出会闪一下。
                //   统一由主循环在整段播完后调 ClearQteHint 收口。
            }
            if (grade == WanXiang.Battle.Core.ParryGrade.Perfect) Sfx("sfx_bt_cmd_confirm");
            else if (grade == WanXiang.Battle.Core.ParryGrade.Normal) Sfx("sfx_bt_cmd_select");

            // ⚠ 只在"还是我在等"时提交：若期间已被外部提交（自动化测试注入），不覆盖、也不打日志
            //   （否则日志会打出本地超时值，与真实生效值不一致 —— 排查时踩过）。
            if (_play.AwaitingParry)
            {
                Debug.Log("[BattlePanel] " + (finisher ? "终结技QTE " : "格挡判定 ")
                          + grade + "（" + actorName + "，第" + (segIndex + 1) + "/" + segTotal + "段）");
                _play.SubmitParry(grade);
            }
        }

        /// <summary>
        /// 本帧是否有 QTE 输入。用旧 Input 直读 ——
        /// ⛔ 本程序集（WanXiang.Modules.UI）没有引用 Unity.InputSystem（asmdef 有意为之），
        ///    新输入系统直读会 CS0234；工程 Active Input Handling = Both，旧 API 运行时可用。
        /// </summary>
        private static bool ParryClickedThisFrame()
        {
            if (Input.GetMouseButtonDown(0)) return true;
            if (Input.GetKeyDown(KeyCode.Space)) return true;
            return false;
        }
    }
}
