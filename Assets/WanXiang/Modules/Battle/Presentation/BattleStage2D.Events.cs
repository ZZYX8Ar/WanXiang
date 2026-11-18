// ============================================================================
//  万相 · 战斗事件应用（2026-11-18 从 BattleStage2D.cs 拆出）
//  行为零变化：仅移动方法。共享字段在 Fields.cs，partial 内互通。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public sealed partial class BattleStage2D
    {
        public void ApplyEvent(int index, BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.ActionBegin:
                {
                    _actingId = e.ActorId;
                    // ★ 2026-11-02：谁在行动 —— 回合横幅补上"我方/敌方"
                    // ★ 2026-11-02 用户："不用每一个人行动都显示" ⇒ **只在阵营切换时改一次字**
                    //   （我方开始→"…我方"；轮到敌方→"…敌方"），同一个阵营内的连续行动不再触发。
                    // ★ 2026-11-13（用户第 7 次报障）：这里的"第 N 回合 · 我方回合"横幅要等
                    //   第一个单位行动才出现（时机错），已删除；回合横幅只在 TurnStart 出现一次。
                    break;
                }

                case BattleEventKind.TurnStart:
                    _actingId = null;
                    _turnNo = e.Turn;
                    // 回合数只记录；横幅交给阶段事件（我方/敌方各一次，时机正确）
                    break;

                case BattleEventKind.PlayerPhaseStart:
                    ShowTurnBanner(e.Turn > 0 ? e.Turn : _turnNo,
                        "第 " + (e.Turn > 0 ? e.Turn : _turnNo) + " 回合 · 我方回合", TbColPlayer);
                    break;

                case BattleEventKind.EnemyPhaseStart:
                    ShowTurnBanner(e.Turn > 0 ? e.Turn : _turnNo,
                        "第 " + (e.Turn > 0 ? e.Turn : _turnNo) + " 回合 · 敌方回合", TbColEnemy);
                    break;

                case BattleEventKind.BattleEnd:
                    _actingId = null;   // 回合结算 / 战斗结束期间不显示行动箭头
                    break;

                case BattleEventKind.SkillCast:
                    // ★ 终结技 / 觉醒技 大演出（2026-10-13）：每个异兽都有，元素色 + id 哈希定造型。
                    //   只给 Ultimate/Awaken 播（大招要稀有、要炸）；普攻/战技保持轻量不刷屏。
                    if (e.Skill == SkillType.Ultimate || e.Skill == SkillType.Awaken)
                    {
                        // 大招：冲锋**延后到 cut-in 之后的起爆那一下**（PlaySkillFx 里绑 OnImpact）
                        PlaySkillFx(e.ActorId, e.TargetId, e.Element, e.Skill == SkillType.Awaken, e.SkillName);
                    }
                    else if (e.IsCombo && e.Skill != SkillType.Basic)
                    {
                        // ★ 连携发动（2026-10-18 用户："连携成功也闪头像，主兽居中，其余缩小环绕"）
                        PlayComboFx(e.ActorId, e.Element, e.SkillName);
                    }
                    else
                    {
                        // 出手：冲上去（GDD 的"跑到目标面前"）。目标 id 缺失（群体技/
                        // 纯增益）时退化为"朝敌方方向冲一段"，不影响观感。
                        StartLunge(e.ActorId, e.TargetId);

                        // ★ 连携追击：每次追加都飘一句"追加攻击"，让玩家看清是谁在补刀
                        if (e.IsCombo && e.ActorId != null && _views.TryGetValue(e.ActorId, out var fu))
                        {
                            // 连携追击的说明要一眼看懂（用户："给一个提示说明这是连携的普攻"）
                            string who = fu.NameText != null ? fu.NameText.text : "";
                            SpawnNumberInternal(fu, "连携·追击", new Color(1f, 0.74f, 0.24f), 1.25f, 0.85f, raise: 0.95f);
                            if (!string.IsNullOrEmpty(who))
                                SpawnNumberInternal(fu, who, new Color(1f, 0.92f, 0.72f), 0.9f, 0.7f, raise: 1.25f);
                        }
                    }
                    // ★ 技能名飘字（用户 2026-10-06："怎么体现这些技能效果"）：
                    //   普攻每回合都放，飘了会刷屏 ， 只飘战技/绝技。技能名事件里本来就有（skillName）。
                    if (!string.IsNullOrEmpty(e.SkillName) && e.SkillName != "普攻" &&
                        e.ActorId != null && _views.TryGetValue(e.ActorId, out var scv))
                        SpawnNumber(scv, e.SkillName, SkillNameColor);
                    break;

                case BattleEventKind.Summon:
                    // 召唤物 / 分身落地：一圈金环 + 轻微震屏（别让它凭空出现）
                    if (!string.IsNullOrEmpty(e.TargetId) && _views.TryGetValue(e.TargetId, out var suv) && suv.Root != null)
                    {
                        var gold = new Color(0.95f, 0.82f, 0.45f, 1f);
                        BossSkillBurst.Play(suv.Root.transform.position, gold, 1.15f, 8, 0.40f, 0);
                        TriggerShake(0.03f, 0.18f);
                        if (suv.NameText != null && !string.IsNullOrEmpty(e.Note))
                            SpawnNumberInternal(suv, e.Note + " 现身", gold, 1.0f, 0.8f, raise: 0.9f);
                    }
                    break;

                case BattleEventKind.Devour:
                    // 枯荣吞噬：绿色环向内收 + 治疗数字（用户 2026-10-30："吞噬有特效吗"）
                    if (!string.IsNullOrEmpty(e.TargetId) && _views.TryGetValue(e.TargetId, out var dvv) && dvv.Root != null)
                    {
                        var green = new Color(0.45f, 0.85f, 0.55f, 1f);
                        BossSkillBurst.PlayInward(dvv.Root.transform.position, green, 2.2f, 0.55f);
                        TriggerShake(0.05f, 0.22f);
                        if (e.Amount > 0) SpawnNumberInternal(dvv, "+" + e.Amount, green, 1.3f, 0.9f, raise: 0.9f);
                    }
                    break;

                case BattleEventKind.Damage:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hit))
                    {
                        bool crit = _critNext; _critNext = false;
                        Vector2 actorHome = hit.HomePos;
                        if (e.ActorId != null && _views.TryGetValue(e.ActorId, out var atk)) actorHome = atk.HomePos;
                        bool melt = e.Note != null && e.Note.Contains("融冰");
                        // ★ 元素反应（2026-10-17）：认 BattleEvent.Reaction 标记，**不解析 Note**
                        //   （解析文案是错的做法，改一句措辞就会让演出失效）。
                        bool reaction = e.Reaction != ReactionKind.None;
                        if (!hit.PlayerSide) SpawnCorruptBurst(hit);   // 魇化：紫碎片（Sprite）

                        if (reaction)
                        {
                            SpawnReactionFx(hit, e.Reaction);
                            // ★ 2026-10-26 用户："Boss 有护盾却还在掉血" —— 反应是**真实伤害**，
                            //   会绕过护盾。这里补一个「穿盾」标记，避免玩家以为护盾失效。
                            if (hit.ShieldPlate != null && hit.ShieldPlate.gameObject.activeSelf)
                                SpawnNumberInternal(hit, "反应·穿盾", new Color(1f, 0.55f, 0.35f), 1.0f, 0.85f, raise: 1.35f);
                            // ★ 反应也染格子：用户反映"格子颜色变化没看到"，只靠克制太稀有了
                            ReactionInfo(e.Reaction, out _, out Color rxC, out _);
                            FlashCounterCell(hit, rxC);
                        }
                        // 属性克制 → 受击者脚下格子染成攻击属性色
                        if (e.ElementCounter) FlashCounterCell(hit, BattlePalette.OfElement(e.Element));
                        // ★ 大招 cut-in 正在播 → 伤害数字押后到"起爆"那一下再飘（用户 2026-10-14：
                        //   否则伤害在特写期间就飘完/被盖住，看着像"没伤害"）
                        if (_skillFx != null && _skillFx.CutInActive)
                            _pendingDmg.Add(new PendDmg { View = hit, Amount = e.Amount, Melt = melt, Reaction = reaction, Crit = crit, ActorHome = actorHome });
                        else
                            ApplyDamageVisual(hit, e.Amount, melt, reaction, crit, actorHome);
                    }
                    break;

                case BattleEventKind.StatusApplied:
                    // ★ 只处理**反应型**状态变更：助燃（灼烧+2）/ 镇（熄灭）没有 Damage 事件，
                    //   不在这里补演出的话，这两个反应在画面上完全静默。
                    //   普通状态施加（灼烧/湿/根缚…）走头顶状态图标，**不在这里重复演出**。
                    if (e.Reaction != ReactionKind.None && e.TargetId != null &&
                        _views.TryGetValue(e.TargetId, out var rsv))
                        SpawnReactionFx(rsv, e.Reaction);
                    break;

                case BattleEventKind.Heal:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var hv))
                    {
                        SpawnNumber(hv, "+" + e.Amount, new Color(0.42f, 0.85f, 0.45f));   // 绿：恢复
                        hv.Body.color = new Color(0.72f, 1f, 0.78f);
                    }
                    break;

                case BattleEventKind.Shield:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var sv))
                    {
                        SpawnNumber(sv, "盾 +" + e.Amount, new Color(0.45f, 0.72f, 1f));   // 蓝：护盾
                        sv.Body.color = new Color(0.75f, 0.88f, 1f);
                    }
                    break;

                case BattleEventKind.Crit:
                    // Crit 先于 Damage 到：置标记让下一条伤害数字放大，并飘"暴击!"
                    _critNext = true;
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var cv))
                    {
                        cv.Body.color = new Color(1f, 0.35f, 0.28f);
                        // 大招特写期间押后（和伤害数字一起在起爆那下飘）
                        if (_skillFx != null && _skillFx.CutInActive) _pendingCritFloat = cv;
                        else SpawnNumber(cv, "暴击!", new Color(1f, 0.85f, 0.35f));
                    }
                    break;

                case BattleEventKind.Revive:
                    // ★ 双子同命 / 灵卵复活：把"灰尸"复位成满色并弹一下，玩家一眼能看到（2026-10-12）
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var rv))
                    {
                        rv.Alive = true;
                        rv.DeadBlend = 0f;                       // 复位：否则复活后仍顶着死亡灰调
                        RestoreBodyMaterial(rv);
                        if (rv.Root != null && !rv.Root.activeSelf) rv.Root.SetActive(true);
                        rv.Body.color = rv.BaseColor;
                        SetHpBar2D(rv, rv.Hp, rv.MaxHp);
                        SpawnNumber(rv, "复活!", new Color(1f, 0.85f, 0.40f));   // 金：同命复活
                    }
                    break;
                case BattleEventKind.Death:
                    if (e.TargetId != null && _views.TryGetValue(e.TargetId, out var dv))
                    {
                        // 首领阵亡：一圈"溃散"爆发 + 重震屏（小兵保持安静）
                        bool wasBoss = !dv.PlayerSide && !string.IsNullOrEmpty(dv.DefId) &&
                                       (WanXiang.Battle.Core.BossCatalog.IsBoss(dv.DefId) ||
                                        dv.DefId.StartsWith("rc_"));
                        if (wasBoss && dv.Root != null)
                        {
                            var st = BossFxStyle.For(dv.DefId);
                            int shape = st.HasValue ? st.Value.Shape : 0;
                            var col = new Color(1f, 0.88f, 0.62f, 1f);
                            BossSkillBurst.Play(dv.Root.transform.position, col, 3.4f, 20, 0.75f, shape);
                            BossSkillBurst.Play(dv.Root.transform.position, col, 2.2f, 10, 0.50f, 0);
                            TriggerShake(0.14f, 0.45f);
                            SpawnNumberInternal(dv, "溃散", col, 1.4f, 1.1f, raise: 1.1f);
                        }
                        dv.Alive = false;
                    }
                    break;
            }
        }
    }
}
