// ============================================================================
//  万相 · 战斗面板 · 格挡 QTE（2026-10-14，用户点名参考 33号远征队）
//  ---------------------------------------------------------------------------
//  模拟层在敌方"带伤害的行动"前挂起（NeedParry）⇒ 本文件负责：
//    · 在攻击者身上弹"收缩环 + 目标圈"的**时机圈**（视觉在 BattleStage2D，灰盒约定）
//    · 真实时间计时（与倍速解耦，和终结技演出同一纪律）
//    · 输入：鼠标左键 / 空格（新输入系统直读，不依赖 InputService 上下文 —— 战斗里
//      UI Map 是关的，走 ActionPerformed 收不到）
//    · 提交判定后由主循环继续（BattlePlayback.SubmitParry）
//
//  判定规则：环收缩进度 k∈[0,1]；
//    完美区：|k - 0.80| ≤ 0.06；普通区：|k - 0.80| ≤ 0.22；
//    超出普通区（按太早/太晚）与超时（k≥1）都算"未挡"。
//  数值（完美减伤 0.2 / 普通 0.6 / 反震 4% 最大生命）在 BattleSimulator.Parry.cs。
// ============================================================================

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public sealed partial class BattlePanel
    {
        /// <summary>时机圈总时长（真实秒）。static 非 const：留一个运行时可调的口子（手感调参/验证）。</summary>
        private static float ParryWindowSeconds = 1.10f;
        /// <summary>完美判定中心（环收缩进度 0..1；到 0.80 时环正好套上目标圈）。</summary>
        private const float ParryPerfectCenter = 0.80f;
        /// <summary>完美区半径。</summary>
        private const float ParryPerfectHalf = 0.06f;
        /// <summary>普通区半径。</summary>
        private const float ParryNormalHalf = 0.22f;

        /// <summary>
        /// 格挡窗口：弹时机圈 → 等玩家点击/超时 → 提交判定。
        /// 由主循环在 _play.AwaitingParry 时调用（原地等，不推进事件）。
        /// </summary>
        private async UniTask RunParryWindowAsync(CancellationToken ct)
        {
            var attacker = _play.ParryAttacker;
            string attackerId = attacker != null ? attacker.RuntimeId : null;
            string attackerName = attacker != null ? attacker.DisplayName : "?";

            if (_stage != null) _stage.ShowParryPrompt(attackerId);
            // 行动箭头指向被格挡的攻击者（ActionBegin 还没发，不点的话箭头停在上一行动者头上）
            if (_stage != null && !string.IsNullOrEmpty(attackerId)) _stage.SetTurnActor(attackerId);
            Sfx("sfx_bt_cmd_select");   // 提示音（没有专用素材，先复用指令音）

            float t0 = Time.unscaledTime;
            WanXiang.Battle.Core.ParryGrade grade = WanXiang.Battle.Core.ParryGrade.None;

            while (true)
            {
                float k = (Time.unscaledTime - t0) / ParryWindowSeconds;
                if (_stage != null) _stage.SetParryProgress(Mathf.Clamp01(k));

                if (ParryClickedThisFrame())
                {
                    float d = Mathf.Abs(Mathf.Clamp01(k) - ParryPerfectCenter);
                    grade = d <= ParryPerfectHalf ? WanXiang.Battle.Core.ParryGrade.Perfect
                          : d <= ParryNormalHalf ? WanXiang.Battle.Core.ParryGrade.Normal
                          : WanXiang.Battle.Core.ParryGrade.None;
                    break;
                }
                if (k >= 1f) break;   // 超时 = 未挡
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            if (_stage != null)
            {
                _stage.HideParryPrompt();
                _stage.ParryFeedback(attackerId, (int)grade);
            }
            if (grade == WanXiang.Battle.Core.ParryGrade.Perfect) Sfx("sfx_bt_cmd_confirm");
            else if (grade == WanXiang.Battle.Core.ParryGrade.Normal) Sfx("sfx_bt_cmd_select");

            // ⚠ 只在"还是我在等"时提交：若期间已被外部提交（自动化测试注入），不覆盖、也不打日志
            //   （否则日志会打出本地超时值，与真实生效值不一致 —— 排查时踩过）。
            if (_play.AwaitingParry)
            {
                Debug.Log("[BattlePanel] 格挡判定 " + grade + "（对手 " + attackerName + "）");
                _play.SubmitParry(grade);
            }
        }

        /// <summary>
        /// 本帧是否有"格挡"输入。用旧 Input 直读 ——
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
