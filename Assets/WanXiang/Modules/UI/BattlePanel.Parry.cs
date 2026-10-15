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
        /// <summary>
        /// 格挡窗口总时长（真实秒）。static 非 const：留一个运行时可调的口子（手感调参/验证）。
        /// ⛔ 别调长：用户 2026-10-15 反馈"时机太容易抓住了" —— 1.10s 的窗口里
        ///   完美区有 0.12×1.10≈132ms，加上"任何时候按都有普通档兜底"，
        ///   实际上闭眼按也能中。要难度就调**两个半径**，别调窗口长度。
    private static float ParryWindowSeconds = 0.85f;
 /// <summary>终结技窗口总时长（真实秒）—— 比格挡短：输出向不该拖慢节奏。</summary>
        private static float FinisherWindowSeconds = 0.70f;

   /// <summary>判定中心（环收缩进度 0..1；到 0.80 时环正好套上目标圈）。</summary>
        private const float ParryPerfectCenter = 0.80f;
   /// <summary>完美区半径（格挡用）。⚠ 调难度主要调这两个半径（用户嫌太容易）。</summary>
        private const float ParryPerfectHalf = 0.035f;
        /// <summary>普通区半径（格挡用）。</summary>
      private const float ParryNormalHalf = 0.11f;

/// <summary>
    /// 连段各段的按键（用户 2026-10-15："第一段按 Q，下一段按 E 等等，这同样也可以应用到敌人身上"）。
    /// ⛔ 为什么不是"每段都按同一个键"：连按同一个键容易被当成"按住不放连触发"，
        ///   也区分不出"这一段我到底按了没有"（实测多段全完美里第 2 段档位曾可疑）。
        ///   换键 + 提示条逐段显示当前键 ⇒ 玩家必须**逐段重新看一眼**。
        /// ★ 单段攻击用 <see cref="ParryPrimaryKey"/>（空格）—— 绝大多数情况是单段，别一上来就 Q。
/// </summary>
   private static readonly KeyCode[] ParryComboKeys =
   {
       KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F,
  };
        /// <summary>单段攻击 / 终结技用的主键（也是连段的第 1 段键之外的通用键）。</summary>
     private const KeyCode ParryPrimaryKey = KeyCode.Space;

        /// <summary>取第 segIndex 段该按的键（越界回退到主键）。</summary>
        private static KeyCode KeyForSegment(int segIndex)
     {
  if (segIndex >= 0 && segIndex < ParryComboKeys.Length) return ParryComboKeys[segIndex];
         return ParryPrimaryKey;
        }

        /// <summary>第 segIndex 段的按键文案（提示条用，如「Q」「E」「空格」）。</summary>
        private static string KeyLabelForSegment(int segIndex)
        {
      return KeyForSegment(segIndex).ToString();
        }

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
            //   屏幕中央一条常驻文案；**逐段显示该段该按的键**（Q→E→R→F 连段递进，
            //   用户 2026-10-15 原话："第一段按 Q，下一段按 E 等等，这同样也可以应用到敌人身上"）。
            //   单段时给「空格 或 鼠标左键」—— 绝大多数是单段，不必一上来就 Q。
    string keyText = segTotal > 1
      ? "按 " + KeyLabelForSegment(segIndex) + "　格挡"
      : "空格 / 鼠标左键　格挡";
            if (_stage != null)
            {
          if (finisher) _stage.SetQteHint("空格 / 鼠标左键　终结技·共鸣", segTotal, segIndex);
      else _stage.SetQteHint(keyText, segTotal, segIndex);
   }
            Sfx("sfx_bt_cmd_select");   // 提示音（没有专用素材，先复用指令音）

            float window = finisher ? FinisherWindowSeconds : ParryWindowSeconds;
            // 终结技输出向：容错放宽一档（抢拍也认，别让玩家白丢终结技）
            float perfectHalf = finisher ? ParryPerfectHalf * 2f : ParryPerfectHalf;
            float normalHalf = finisher ? ParryNormalHalf * 1.6f : ParryNormalHalf;

            float t0 = Time.unscaledTime;
         WanXiang.Battle.Core.ParryGrade grade = WanXiang.Battle.Core.ParryGrade.None;
            // ★ 冻结时不耗窗口时长（重新挑战确认框期间）：把已过时间补回来，
  //   否则弹出确认框的那一秒会被算成"没按 ⇒ 未挡"，取消后回来白挨一下。
        //   ⚠ 本窗口只在我方手动战斗时开（自动战斗 ParryEnabled=false），但仍写上以防万一。
      float frozen = 0f;

            while (true)
            {
   if (_halted)
      {
    // 冻结：暂停累计窗口进度（把这段时间从已耗时里扣掉）
     frozen += Time.unscaledTime - t0;
           await UniTask.Yield(PlayerLoopTiming.Update, ct);
           continue;
      }
         float k = (Time.unscaledTime - t0 - frozen) / window;
                if (_stage != null) _stage.SetParryProgress(Mathf.Clamp01(k));

     if (ParryPressedThisFrame(segIndex))
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
       + grade + "（" + actorName + "，第" + (segIndex + 1) + "/" + segTotal
       + "段，键=" + KeyLabelForSegment(segIndex) + "）");
                _play.SubmitParry(grade);
   }
        }

        /// <summary>
        /// 本帧是否有 QTE 输入。用旧 Input 直读 ——
    /// ⛔ 本程序集（WanXiang.Modules.UI）没有引用 Unity.InputSystem（asmdef 有意为之），
        ///    新输入系统直读会 CS0234；工程 Active Input Handling = Both，旧 API 运行时可用。
        /// </summary>
    /// <param name="segIndex">第几段（0 起）。连段逐段换键 Q→E→R→F，
 ///   单段（segIndex=0 且总段数 1）用主键空格。</param>
  private static bool ParryPressedThisFrame(int segIndex)
        {
   // 鼠标左键任何段都认：玩家不一定要用键盘（这是"能不能玩"的问题，不是"难度"）。
    if (Input.GetMouseButtonDown(0)) return true;
            if (Input.GetKeyDown(KeyForSegment(segIndex))) return true;
        // ⛔ 连段时**不再接受主键**：否则"每段都按空格"和"只看提示按对应键"没有区别，
            //   连段的逐段换键就白设了（用户要的是"按几个键"，不是"按 N 下"）。
            //   单段 / 终结技仍接受空格。
            if (segIndex == 0 && Input.GetKeyDown(ParryPrimaryKey)) return true;
  return false;
   }
    }
}
