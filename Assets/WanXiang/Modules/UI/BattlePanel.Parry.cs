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
        /// <summary>阵形连携窗口总时长（真实秒）—— 与终结技同档：都是"大招级"输出 QTE。</summary>
        private static float ComboWindowSeconds = 0.75f;

   /// <summary>判定中心（环收缩进度 0..1；到 0.80 时环正好套上目标圈）。</summary>
        private const float ParryPerfectCenter = 0.80f;
   /// <summary>完美区半径（格挡用）。⚠ 调难度主要调这两个半径（用户嫌太容易）。</summary>
        private const float ParryPerfectHalf = 0.035f;
        /// <summary>普通区半径（格挡用）。</summary>
      private const float ParryNormalHalf = 0.11f;

        /// <summary>
        /// 连段可用的按键池。
        /// ⛔ 2026-10-15 用户："这个 qte 不要老是固定循序，我发现这些按键的循序都是固定的，随机一下"
        ///   ⇒ 每次**连段开始**时对这个池做一次洗牌，段内按洗好的顺序取键。
        ///   ⚠ 洗牌是**每次连段**一次（不是每段一次）：段内顺序必须稳定，
        ///     否则提示条显示 Q、判定却要 E（同一段内读两次键会不一致）。
        /// </summary>
        private static readonly KeyCode[] ParryComboPool =
        {
            KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F,
        };
        /// <summary>单段攻击 / 终结技用的主键（也是连段的第 1 段键之外的通用键）。</summary>
        private const KeyCode ParryPrimaryKey = KeyCode.Space;

        /// <summary>本次连段的按键序列（洗牌结果；长度 = 池长）。</summary>
        private readonly KeyCode[] _comboKeys = new KeyCode[4];
        /// <summary>当前 _comboKeys 对应的"连段世代"（-1 = 还没洗过）。</summary>
        private int _comboKeyGen = -1;

        /// <summary>
        /// 为本次连段洗一套按键顺序（每段调用前判一次，同一次连段只洗一次）。
        /// 用 segTotal+segIndex==0 判"新连段开始"：段 0 一定是新连段的开头。
        /// </summary>
        private void EnsureComboKeys(int segIndex)
        {
            // 段 0 = 新连段 ⇒ 重洗；否则沿用本次连段已洗好的顺序。
            if (_comboKeyGen >= 0 && segIndex != 0) return;

            for (int i = 0; i < ParryComboPool.Length; i++) _comboKeys[i] = ParryComboPool[i];
            // Fisher-Yates 洗牌（UnityEngine.Random 即可 —— 这是表现层，不进战斗指纹）
            for (int i = _comboKeys.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var t = _comboKeys[i]; _comboKeys[i] = _comboKeys[j]; _comboKeys[j] = t;
            }
            _comboKeyGen++;
        }

        /// <summary>取第 segIndex 段该按的键（越界回退到主键）。</summary>
        private KeyCode KeyForSegment(int segIndex)
        {
            if (segIndex >= 0 && segIndex < _comboKeys.Length) return _comboKeys[segIndex];
            return ParryPrimaryKey;
        }

        /// <summary>第 segIndex 段的按键文案（提示条用，如「Q」「E」「空格」）。</summary>
        private string KeyLabelForSegment(int segIndex)
        {
            return KeyForSegment(segIndex).ToString();
        }

        /// <summary>
        /// 「快慢刀」每段的窗口时长倍率（用户 2026-10-15："还是太容易判断了，
        /// 能不能整一些快慢刀，不要每一个判断都一样"）。
        ///
        /// 设计：连段内部的节奏**刻意不均匀** —— 1.00（标准）→ 0.72（快刀）→ 1.28（慢刀）
        /// → 0.88（偏快）。玩家不能靠"心里数 0.8 秒"蒙对，必须每段重看时机环。
        /// ⚠ 单段（绝大多数普攻）恒为 1.00 —— 别把日常节奏也搞乱。
        /// ⚠ 倍率只改**窗口时长**，判定半径不动：快刀难在"反应时间不够"，
        ///   而不是"判定更窄"，后者会让快刀变得不讲道理。
        /// </summary>
        private static float SegmentSpeedMul(int segIndex)
        {
            switch (segIndex)
            {
                case 1: return 0.72f;   // 快刀
                case 2: return 1.28f;   // 慢刀
                case 3: return 0.88f;   // 偏快
                default: return 1.00f;  // 第 1 段 / 单段：标准
            }
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
            bool comboQte = st.ParryQteKind == WanXiang.Battle.Core.ParryQteKind.Combo;
            bool offensive = finisher || comboQte;      // 进攻向（放宽容错、短窗口）
            int segIndex = st.ParrySegmentIndex;
            int segTotal = Mathf.Max(1, st.ParrySegmentTotal);

            // ★★ 大招 cut-in 播完再弹格挡窗（用户 2026-10-15：
            //    "敌方放大招的时候闪头像和那个格挡判断是一起，要不要改成先闪完头像再判断"）。
            //    ⛔ 为什么必须等：大招的 SkillCast 事件一播就起 cut-in（约 1~2s），
            //     而格挡窗按真实时间倒计时 —— 不等的话，玩家在看头像特写的整段时间里
            //     时机环一直在收缩，等特写结束环早跑完了 ⇒ 必判"未挡"。
            //    这不只是观感问题，是**判定不公平**（用户实测："格挡成功但感觉没看到圈"）。
            //    ⚠ 只等 cut-in，不等全部 fx（起爆特效还要继续播，等它会让窗口迟迟不弹）。
            if (_stage != null)
            {
                int guard = 0;
                while (_stage.CutInPlaying)
                {
                    // 冻结期（重新挑战确认框）不消耗兜底计数：cut-in 也停着，不该算超时
                    if (!_halted && ++guard > 900) break;   // 兜底上限：约 15 秒（60fps）
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }

            if (_stage != null) _stage.ShowParryPrompt(actorId);
            // 行动箭头指向被判定者（ActionBegin 还没发，不点的话箭头停在上一行动者头上）
            if (_stage != null && !string.IsNullOrEmpty(actorId)) _stage.SetTurnActor(actorId);

            // ★ 按键提示（用户："这个 qte 是按什么按键的啊，都没有提示"）：
            //   屏幕中央一条常驻文案；**逐段显示该段该按的键**。
            //   ★ 2026-10-15 起改为**每次连段随机洗牌**（用户："这些按键的循序都是固定的，随机一下"）。
            //   单段时给「空格 或 鼠标左键」—— 绝大多数是单段，不必一上来就 Q。
            // ★★ 快慢刀（用户："还是太容易判断了，能不能整一些快慢刀，不要每一个判断都一样"）：
            //   每段的窗口时长**不一样**，靠段号决定（见 SegmentSpeedMul）——
            //   快刀窗口短、时机环收缩快，慢刀反过来。提示条标"快/慢"让玩家有预期。
            float segMul = SegmentSpeedMul(segIndex);
            string paceTag = segMul < 0.85f ? "快刀" : (segMul > 1.15f ? "慢刀" : "");

            // ⚠ 别用括号包 paceTag：SetQteHint 结尾会再拼一个「（连击 k/n）」，
            //   两层括号挤在一起读不清（"格挡（快刀）　（连击 2/3）"）。空格分隔即可。
            string actionName = comboQte
                ? ("连携·" + (string.IsNullOrEmpty(st.ParryComboName) ? "阵形" : st.ParryComboName))
                : (finisher ? "终结技·共鸣" : "格挡");
            string keyText;
            if (segTotal > 1)
            {
                // 连段（含我方终结技/连携的多段）：逐段换键 + 逐段重洗后的对应键
                EnsureComboKeys(segIndex);
                keyText = "按 " + KeyLabelForSegment(segIndex) + "　" + actionName;
            }
            else
            {
                keyText = "空格 / 鼠标左键　" + actionName;
            }
            if (paceTag.Length > 0) keyText += "　" + paceTag;
            if (_stage != null) _stage.SetQteHint(keyText, segTotal, segIndex);
            Sfx("sfx_bt_cmd_select");   // 提示音（没有专用素材，先复用指令音）

            float window = (comboQte ? ComboWindowSeconds
                          : finisher ? FinisherWindowSeconds
                          : ParryWindowSeconds) * segMul;
            // 进攻向（终结技/连携）：容错放宽一档（抢拍也认，别让玩家白丢大招/连携）
            float perfectHalf = offensive ? ParryPerfectHalf * 2f : ParryPerfectHalf;
            float normalHalf = offensive ? ParryNormalHalf * 1.6f : ParryNormalHalf;

            // ★ 快慢刀的关键一步：**把判定半径按 1/segMul 反向补偿**，
            //   保持"判定区间的绝对毫秒数"恒定。
            //   为什么必须补：完美区在进度轴上的宽度是 2×perfectHalf，
            //   换算成时间是 2×perfectHalf×window。窗口被 segMul 缩放后，
            //   若不补，快刀（0.72）的完美区会从 59ms 缩到 42ms —— 那是"double 惩罚"
            //   （又要更快反应、又要更准），打起来像耍人。
            //   补偿后：三种刀的判定时长一致，区别只在"时机环收缩多快"（反应窗口长度），
            //   这才是玩家能练、能感知的难度。
            perfectHalf /= segMul;
            normalHalf /= segMul;

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

     if (ParryPressedThisFrame(segIndex, segTotal))
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

            // ★★ 成功反馈（用户 2026-10-15："格挡成功的反馈我好像没有怎么体会到"）。
            //   三件套叠起来才"体会得到"，之前只有飘字所以像没发生：
            //     ① 爆闪环：在攻击者身上炸一圈（完美=金色大环+白闪，普通=青白小环）
            //     ② 震屏：完美重震 + Zoom Punch（镜头一顿一顶）；普通轻震
            //     ③ 顿帧：走 BattlePanel 的 _hitStop（冻结舞台几帧，制造"兵器相击"的顿感）
            //   ⚠ 全部只在"挡到了"时触发；未挡不震不闪（没挡住还反馈是噪音）。
            if (_stage != null && grade != WanXiang.Battle.Core.ParryGrade.None)
            {
                bool perfect = grade == WanXiang.Battle.Core.ParryGrade.Perfect;
                _stage.ParryImpact(actorId, (int)grade);
                if (perfect)
                {
                    _stage.TriggerShake(0.28f, 0.26f);   // 比原先 0.16 更重（用户说没体会到）
                    _stage.TriggerZoom(0.30f, 0.22f);    // 镜头一顿一顶：完美档专属
                }
                else
                {
                    _stage.TriggerShake(0.12f, 0.16f);
                }
                // 顿帧：完美更长（0.10s），普通短（0.05s）。倍速越高越短（见 Update 里除以 _speed）。
                _hitStop = Mathf.Max(_hitStop, perfect ? 0.10f : 0.05f);
            }

            if (_stage != null)
            {
                _stage.HideParryPrompt();
                _stage.ParryFeedback(actorId, (int)grade, finisher, comboQte);
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
        /// <param name="segIndex">第几段（0 起）。连段逐段换键（每次连段随机洗牌）。</param>
        /// <param name="segTotal">本次行动总段数。&gt;1 = 连段，**只认该段对应的键**。</param>
        private bool ParryPressedThisFrame(int segIndex, int segTotal)
        {
            bool combo = segTotal > 1;

            // ⛔⛔ 连段时**只认该段对应的键**（用户 2026-10-15 报障：
            //    "这个按键 QTE 怎么按空格和左键也能生效啊，不应该是对应的按键吗"）。
            //    上一版判据是"鼠标左键任何段都认 + 第 1 段也认空格"，
            //    玩家永远不用换键 ⇒ 逐段换键的设计意图被架空。
            if (combo) return Input.GetKeyDown(KeyForSegment(segIndex));

            // 单段：空格 / 鼠标左键都认（这是"能不能玩"的兜底，不是"难度"）。
            if (Input.GetMouseButtonDown(0)) return true;
            if (Input.GetKeyDown(ParryPrimaryKey)) return true;
            return false;
        }
    }
}
