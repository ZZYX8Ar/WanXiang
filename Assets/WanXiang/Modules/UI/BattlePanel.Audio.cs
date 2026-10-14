// ============================================================================
//  WanXiang · 战斗面板 · 音效（partial）
//  ---------------------------------------------------------------------------
//  把战斗事件流（BattleEvent）翻译成音效。
//
//  为什么挂在「事件流」而不是「按钮点击」：
//    战斗里的每一件事都会进 BattleEventKind 枚举（开局 / 施法 / 伤害 / 暴击 /
//    治疗 / 护盾 / 阵亡 / 复活 / 收尾…），事件流是**唯一的真相来源**。
//    接在按钮上会漏掉自动战斗、连携、Boss 机制、持续伤害跳数这些「没有按钮」的场面。
//
//  ⛔ 节流是必须的：一次群体技能会连发 5 条 Damage 事件，照单全收就是 5 声命中音
//     同时叠上来（音效池只有 6 条，还会互相打断，听感是一团糊）。
//     Sfx() 里做了「同一条音效的最小重播间隔」，见 SfxMinGap。
// ============================================================================

using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Framework.Audio;

namespace WanXiang.Modules.UI
{
    public partial class BattlePanel
    {
        /// <summary>同一条音效的最小重播间隔（秒）。用来挡掉「群体技能连发多条伤害」造成的叠音。</summary>
        private const float SfxMinGap = 0.05f;

        /// <summary>
        /// 判定为「重击」的伤害阈值。低于它用轻击音，让打击有轻重层次。
        /// 校准依据：见 _归档_设计文档/万相_音频设计文档/Audio_CREDITS.md 里记录的实战伤害分布。
        /// </summary>
        private const int HeavyHitDamage = 160;

        private bool _critPending;

        /// <summary>上一帧当前单位是否已元气满。用于只在"由不满变满"那一下播提示音（不能每帧都响）。</summary>
        private bool _lastRageFull;

        /// <summary>按战斗事件播对应音效。由 StepOnce 在 ApplyEvent 之后调用。</summary>
        private void PlayEventSfx(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.BattleStart:
                    Sfx("sfx_bt_start");
                    // 异兽登场音：开着场那一下，给队伍一个"亮相"的仪式感。
                    // ⚠ 这一条本来想放 BattleStage2D 的单位创建处（更贴"入场"时机），
                    //   但 Battle 表现层所在程序集**不引用 WanXiang.Framework**，拿不到 AudioSystem
                    //   （实测报 CS0234）。所以由 UI 层在 BattleStart 事件代播 —— 时机几乎一致。
                    Sfx("sfx_beast_enter_small", 0.7f);
                    break;

                case BattleEventKind.TurnStart:
                    Sfx("sfx_bt_round_start", 0.6f);   // 每回合都响，压低音量免得吵
                    break;

                case BattleEventKind.SkillCast:
                    Sfx(SkillCastSfx(e));
                    break;

                case BattleEventKind.Crit:
                    // Crit 事件排在 Damage **之前**，这里只做标记，
                    // 由紧跟其后的 Damage 决定到底播哪一声（暴击 / 重击 / 轻击）。
                    _critPending = true;
                    break;

                case BattleEventKind.Damage:
                    Sfx(DamageSfx(_critPending, e.Amount));
                    _critPending = false;
                    break;

                case BattleEventKind.Heal:
                    Sfx("sfx_bt_heal", 0.8f);
                    break;

                case BattleEventKind.Shield:
                    if (e.Amount > 0) Sfx("sfx_bt_shield_gain", 0.7f);
                    break;

                case BattleEventKind.StatusApplied:
                    Sfx(StatusSfx(e), 0.8f);
                    break;

                case BattleEventKind.StatChange:
                    // Amount 正 = 增益，负 = 减益（Amount 是变化量，不是结果值）
                    Sfx(e.Amount >= 0 ? "sfx_bt_buff_up" : "sfx_bt_debuff_down", 0.7f);
                    break;

                case BattleEventKind.Death:
                    // 我方异兽阵亡用「哀鸣」（玩家对自家队伍有感情），敌方用中性的倒地音。
                    Sfx(IsPlayerUnit(e.TargetId) ? "sfx_beast_death_cry" : "sfx_bt_death_unit", 0.85f);
                    break;

                case BattleEventKind.Revive:
                    Sfx("sfx_bt_revive");
                    break;

                case BattleEventKind.BattleEnd:
                    Sfx(EndSfx());
                    break;

                // 以下事件刻意不配音效：
                //   RoundResolve  —— 场地/天时/持续伤害结算，每回合都来，配了会吵
                //   ActionBegin   —— 只是「轮到谁」，频繁且无听感价值
                //   ActionEnd / TurnEnd / StatusRemoved / StatChange(无变化) —— 无反馈价值
            }
        }

        /// <summary>施法音：按技能档位 + 五行属性分流。</summary>
        private static string SkillCastSfx(BattleEvent e)
        {
            switch (e.Skill)
            {
                case SkillType.Awaken:   return "sfx_bt_awaken";
                case SkillType.Ultimate: return ElementSfx("sfx_bt_ult_", e.Element);
                case SkillType.Active:   return ElementSfx("sfx_bt_skill_", e.Element);
                default:                 return null;   // 普攻不另播 —— 它的反馈落在 Damage 上
            }
        }

        /// <summary>
        /// 命中音：暴击 &gt; 重击（伤害过阈值）&gt; 轻击。
        /// 刻意写成 static + 显式入参（而不是读实例的 _critPending），
        /// 这样能脱离战斗实例单独校验这张映射表 —— 见 Audio_CREDITS.md 的验证记录。
        /// </summary>
        private static string DamageSfx(bool crit, int amount)
        {
            if (crit) return "sfx_bt_hit_crit";
            return amount >= HeavyHitDamage ? "sfx_bt_hit_heavy" : "sfx_bt_hit_light";
        }

        /// <summary>五行后缀。Element.None（无属性）没有专属素材，返回 null 保持安静 ——
        /// 这种场面由命中音兜底，硬塞一个别的属性音反而会误导玩家。</summary>
        private static string ElementSfx(string prefix, Element el)
        {
            switch (el)
            {
                case Element.Wood:  return prefix + "wood";
                case Element.Fire:  return prefix + "fire";
                case Element.Earth: return prefix + "earth";
                case Element.Metal: return prefix + "metal";
                case Element.Water: return prefix + "water";
                default:            return null;
            }
        }

        /// <summary>
        /// 状态音。⚠ 只认 Element / Amount 这类**结构化字段**，
        /// 绝不去解析 Note 里的中文文案 —— 这是项目既定口径：
        /// 表现层解析文案串，一旦有人改了措辞就会静默失效。
        /// </summary>
        private static string StatusSfx(BattleEvent e)
        {
            switch (e.Element)
            {
                case Element.Fire:  return "sfx_bt_burn";     // 灼烧 / 引燃
                case Element.Water: return "sfx_bt_freeze";   // 冰封 / 冻结
                case Element.Wood:  return "sfx_bt_bind";     // 缠绕 / 根缚
                case Element.Earth: return "sfx_bt_poison";   // 瘴气 / 中毒
                default:            return null;
            }
        }

        /// <summary>某个 RuntimeId 是不是我方单位（用来给"我方阵亡"选更贴的哀鸣音）。</summary>
        private bool IsPlayerUnit(string runtimeId)
        {
            var st = _play != null ? _play.State : null;
            if (st == null || string.IsNullOrEmpty(runtimeId)) return false;

            var mine = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < mine.Count; i++)
                if (mine[i] != null && mine[i].RuntimeId == runtimeId) return true;
            return false;
        }

        /// <summary>收尾音：胜 / 败 / 平局（打满回合数按 Draw 结算）。</summary>
        private string EndSfx()
        {
            var st = _play != null ? _play.State : null;
            if (st == null) return "sfx_bt_win";
            switch (st.Outcome)
            {
                case BattleOutcome.Draw:      return "sfx_bt_draw";
                case BattleOutcome.PlayerWin: return "sfx_bt_win";
                default:                      return "sfx_bt_lose";
            }
        }

        /// <summary>
        /// 播一条音效。节流逻辑已提到基类 <c>SfxThrottled</c>（地图面板也要用同一套），
        /// 这里保留短名字只是为了战斗内的调用点读起来干净。
        /// </summary>
        private void Sfx(string id, float vol = 1f)
        {
            SfxThrottled(id, vol, SfxMinGap);
        }
    }
}
