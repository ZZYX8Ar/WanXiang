// ============================================================================
//  万相 · 战斗面板 · ActionBar（从 BattlePanel.cs 拆出：**纯搬家、零行为变更**）
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

        private void RefreshComboButton()
        {
            if (_btnCombo == null || _play == null) return;
            var combos = _play.AvailableCombos;
            // ★ 只有"等我方下令"时才可点连携；敌人回合/伤害结算一律置灰（用户 2026-10-13）
            _btnCombo.interactable = _play.AwaitingCommand && combos.Count > 0;
            var label = _btnCombo.GetComponentInChildren<TMPro.TMP_Text>();
            if (label != null)
                label.text = combos.Count > 0 ? "连携·" + combos[0].Name : "连携";
        }

        /// <summary>目标选择器的中文名（提示面板用；与 TargetSelector 一一对应）。</summary>
        private static string TargetNameOf(WanXiang.Battle.Core.TargetSelector t)
        {
            switch (t)
            {
                case WanXiang.Battle.Core.TargetSelector.Self: return "自己";
                case WanXiang.Battle.Core.TargetSelector.SingleLowestHp: return "生命最低的敌人";
                case WanXiang.Battle.Core.TargetSelector.SingleHighestHp: return "生命最高的敌人";
                case WanXiang.Battle.Core.TargetSelector.SingleHighestAtk: return "攻击最高的敌人";
                case WanXiang.Battle.Core.TargetSelector.AllEnemies: return "全体敌人";
                case WanXiang.Battle.Core.TargetSelector.AllAllies: return "全体友方";
                case WanXiang.Battle.Core.TargetSelector.RandomEnemy: return "随机敌人";
                case WanXiang.Battle.Core.TargetSelector.RandomEnemyMultiHit: return "随机敌人（连击）";
                case WanXiang.Battle.Core.TargetSelector.AdjacentToSelf: return "自身相邻";
                case WanXiang.Battle.Core.TargetSelector.AllOthers: return "全场其他";
                case WanXiang.Battle.Core.TargetSelector.SingleFrontMost: return "最前排";
                case WanXiang.Battle.Core.TargetSelector.SingleBackMost: return "最后排";
                default: return "—";
            }
        }

        private void BuildActionBar()
        {
            // prefab 已绑定（生成器产物）→ 只接线，不重复建控件
            if (_actionBar != null)
            {
                if (_skillBtns != null)
                    for (int i = 0; i < _skillBtns.Length; i++)
                    {
                        int idx = i;
                        if (_skillBtns[i] != null) _skillBtns[i].onClick.AddListener(() => OnSkillClicked(idx));
                    }
                if (_btnAutoBattle != null) _btnAutoBattle.onClick.AddListener(OnAutoBattleClicked);
                if (_btnCombo != null) _btnCombo.onClick.AddListener(OnComboClicked);
                // ⚠ 这里**只能注册一次**！原来连着写了 3 行 AddListener(OnComboClicked)
                //   ， 点一次连携触发 3 次 SubmitCombo（灵力多扣 / 效果重复，用户实测报障）。
                // ★ 统一的"不可行动"置灰色（用户 2026-10-13：敌人回合/伤害结算时按钮要点不了，
                //   且要有"能点/不能点"的颜色变化，不靠隐藏）。设 colorBlock.disabledColor，
                //   interactable=false 时 Unity 自动应用。
                var disabledCol = new Color(0.40f, 0.38f, 0.34f, 0.55f);
                if (_skillBtns != null)
                    for (int i = 0; i < _skillBtns.Length; i++)
                        if (_skillBtns[i] != null)
                        {
                            var cb = _skillBtns[i].colors; cb.disabledColor = disabledCol;
                            _skillBtns[i].colors = cb;
                            _skillBtns[i].transition = Selectable.Transition.ColorTint;
                        }
                if (_btnCombo != null)
                {
                    var cb = _btnCombo.colors; cb.disabledColor = disabledCol;
                    _btnCombo.colors = cb;
                    _btnCombo.transition = Selectable.Transition.ColorTint;
                }
                HookTipCombo();
                EnsureAutoSpin();
                _actionBar.gameObject.SetActive(false);
                return;
            }

            // （原「prefab 没有才自建」的兜底段已删除 —— 该分支不可达，UI 一律在 prefab 里做好）
}

        /// <summary>
        /// 好友对战 UI 模式：隐藏【所有】手动操作（技能/连携/自动切换/倍速/自动布阵/撤退），
        /// 强制开启自动战斗。⚠ 必须在【每次战斗开始】时调用 ——
        /// BuildActionBar 只在面板创建时跑一次，那时 LastWasPvp 还没被 EnterBattle 设置（踩过）。
        /// </summary>
        private void ApplyPvpUiMode()
        {
            if (SceneFlow.LastWasPvp)
            {
                // ★ PvP：隐藏全部手动操作 + 强制自动（面板是 Cached 复用的，上一局 PvE 亮出的按钮要收掉）
                if (_skillBtns != null)
                    for (int i = 0; i < _skillBtns.Length; i++)
                        if (_skillBtns[i] != null) _skillBtns[i].gameObject.SetActive(false);
                if (_btnCombo != null) _btnCombo.gameObject.SetActive(false);
                if (_btnAutoBattle != null) _btnAutoBattle.gameObject.SetActive(false);
                _autoBattle = true;   // auto 模式整场已预模拟，设 true 即可（回放自动推进）

                // 2) Root_Action 下的其它按钮（×N 倍速 / 自动布阵 / 撤退 等代码自建按钮）
                if (_actionBar != null)
                {
                    for (int i = 0; i < _actionBar.childCount; i++)
                    {
                        var c = _actionBar.GetChild(i);
                        bool isActor = c.name == "Tmp_Actor";
                        if (!isActor && c.GetComponent<Button>() != null)
                            c.gameObject.SetActive(false);
                    }
                }

                Debug.Log("[BattlePanel] 好友对战：手动 UI 已全部隐藏，自动战斗开启");
            }
            else
            {
                // ★ 正常战斗：Cached 面板若在上一局 PvP 里被隐藏过手动 UI，这里必须**重新亮出**，
                //   否则 PvP 之后打正常战斗会看到"空的操作区"（技能/连携/自动按钮都不见）。
                //   第 4 槽（觉醒技）是否显示交给 RefreshActionBar 按"是否装备"决定，这里只恢复 0..2 三槽。
                if (_skillBtns != null)
                    for (int i = 0; i < _skillBtns.Length && i < 3; i++)
                        if (_skillBtns[i] != null) _skillBtns[i].gameObject.SetActive(true);
                if (_btnCombo != null) _btnCombo.gameObject.SetActive(true);
                if (_btnAutoBattle != null) _btnAutoBattle.gameObject.SetActive(true);
                _autoBattle = false;   // 正常战斗=手动，等令分支不自动提交

                if (_actionBar != null)
                {
                    for (int i = 0; i < _actionBar.childCount; i++)
                    {
                        var c = _actionBar.GetChild(i);
                        bool isActor = c.name == "Tmp_Actor";
                        if (!isActor && c.GetComponent<Button>() != null)
                            c.gameObject.SetActive(true);
                    }
                }

                Debug.Log("[BattlePanel] 正常战斗：手动 UI 已重新亮出（技能/连携/自动战斗）");
            }
        }

        private string CostLine(int slot)
        {
            switch (slot)
            {
                case 0: return "消耗：无（0 灵力，永远可用）";
                case 1: return "消耗：灵力 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点（全队共享）";
                case 3: return "消耗：元气满 100 时手动释放（觉醒技，每场一次）";
                default: return "消耗：元气满 100 时手动释放，每场一次";
            }
        }

        /// <summary>
        /// 现在能不能下令：正等我方下令 + **没有"已产生但还没播完"的事件**。
        /// ★ 关键：回合开始那一串棋盘/天时结算（回血扣血、相生、出手序列）是"待令之后才播完"的，
        ///   只看 AwaitingCommand 的话，玩家会在结算还在飘字时就能点按钮 → 出手被浪费（用户 2026-10-13 报障）。
        ///   HasPendingEvent() 为真 = 还有事件在播 → 此时一律不给点。
        /// </summary>
        private bool CanCommandNow()
        {
            return _play != null && _play.AwaitingCommand && !_play.Finished && !HasPendingEvent();
        }

        private void RefreshActionBar()
        {
            if (_actionBar == null || _play == null) return;
            // ★ 战斗中操作区**常驻可见**（不再随回合显隐 —— 用户 2026-10-13：老是显隐观感差）。
            //   "能不能点"用 interactable + 置灰色表达，不靠隐藏。show=_playing 整场稳定，不逐回合翻。
            bool show = _playing;
            if (_actionBar.gameObject.activeSelf != show)
                _actionBar.gameObject.SetActive(show);
            if (!show) return;

            // 只有"等我方下令 + 结算事件已播完"才能行动；敌人行动/伤害结算/自动推进中一律置灰不可点。
            bool canAct = CanCommandNow();

            var u = _play.PendingUnit;
            if (_autoBattle && u == null)
            {
                // 自动推进中（不是在等某个单位）：给一句可回退的提示
                // ★ 带回合数（用户 2026-09-28：30 回合上限打满会平局，必须让玩家看见进度）
                var stAuto = _play.State;
                if (_tmpActor != null) _tmpActor.text = "自动战斗中…… 第 " + (stAuto != null ? stAuto.Turn : 0) + "/" +
                    (stAuto != null && stAuto.Config != null ? stAuto.Config.MaxTurns : 48) + " 回合（点任意战记即可接管）";
                SetSkillButtonsInteractable(false);   // 自动推进中技能/连携不可点（要接管先关自动或等我方回合）
                return;
            }
            var stt = _play.State;
            int mp = stt != null ? stt.TeamMp : 0;
            int mpMax = stt != null ? stt.TeamMpMax : 0;

            RefreshOrderList();

            var cfg = stt != null ? stt.Config : null;
            if (_tmpActor != null && u != null)
            {
                // ★ 回合数常驻显示（用户 2026-09-28）：打到 MaxTurns(48) 上限会判平局，
                //   之前界面完全没提示，玩家"没人死却输了"一头雾水。
                string line = "第 " + (stt != null ? stt.Turn : 0) + "/" +
                              (cfg != null ? cfg.MaxTurns : 48) + " 回合　轮到「" + u.DisplayName + "」　灵力 " + mp + "/" + mpMax +
                              "（战记 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点）";
                if (u.GetSkill(SkillType.Ultimate) != null)
                    line += "　元气 " + (int)u.Rage + "/" + (int)u.RageCap;

                // 元气刚攒满 → 提示音（"终结技 / 觉醒技可以放了"的信号）。
                // ⚠ 只在"由不满变满"的那一帧播；RefreshActionBar 是逐帧调的，每帧都响会变成噪音。
                bool rageFull = u.RageCap > 0 && u.Rage >= u.RageCap;
                if (rageFull && !_lastRageFull) Sfx("sfx_bt_rage_full", 0.9f);
                _lastRageFull = rageFull;
                // 守卫：文本没变就不重设（TMP 每次 SetText 都会重建 mesh，逐帧设置会卡）
                if (line != _lastActorLine) { _lastActorLine = line; _tmpActor.text = line; }
            }
            else if (_tmpActor != null)
            {
                if (_lastActorLine != "轮到我方行动")
                {
                    _lastActorLine = "轮到我方行动";
                    _tmpActor.text = _lastActorLine;
                }
            }

            // ⛔ 觉醒技（第 4 槽）：**未觉醒 / 未装备就隐藏**（用户 2026-10-11 要求）。
            bool hasAwaken = u != null && u.GetSkill(SkillType.Awaken) != null;
            if (_skillBtns != null && _skillBtns.Length > 3 && _skillBtns[3] != null)
            {
                if (_skillBtns[3].gameObject.activeSelf != hasAwaken)
                    _skillBtns[3].gameObject.SetActive(hasAwaken);
            }

            if (_skillBtns != null)
            {
                // ★ 不能行动（敌人回合/伤害结算）时全部置灰；能行动时按规则逐条判。
                //   以前 u==null（非待令）时整段跳过 ⇒ 按钮停在上一个我方单位的可用态，
                //   敌人回合还能点却没反应（用户 2026-10-13 报障）。
                _skillBtns[0].interactable = canAct;      // 普攻：能行动即可用（0 耗兜底）
                _skillBtns[1].interactable = canAct && u != null
                                              && u.GetSkill(SkillType.Active) != null
                                              && mp >= WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active);
                _skillBtns[2].interactable = canAct && u != null
                                              && u.GetSkill(SkillType.Ultimate) != null
                                              && cfg != null
                                              && u.CanCast(SkillType.Ultimate, cfg);
                if (_skillBtns.Length > 3 && _skillBtns[3] != null)
                    _skillBtns[3].interactable = hasAwaken && canAct && cfg != null && u.CanCast(SkillType.Awaken, cfg);
            }

            // 连携按钮不在每帧路径里刷新（见 RefreshComboButton，避免每帧 GC/查子节点）
        }

        /// <summary>批量置灰/恢复技能按钮（自动推进、敌人回合用）。</summary>
        private void SetSkillButtonsInteractable(bool on)
        {
            if (_skillBtns == null) return;
            for (int i = 0; i < _skillBtns.Length; i++)
                if (_skillBtns[i] != null && _skillBtns[i].gameObject.activeSelf)
                    _skillBtns[i].interactable = on;
            if (_btnCombo != null) _btnCombo.interactable = on && _play != null && _play.AwaitingCommand && _play.AvailableCombos.Count > 0;
        }

        private void OnSkillClicked(int skillIndex)
        {
            if (_play == null) return;

            // 自动推进中点战记 = 接管：先关自动（此刻不在"待令"状态，SubmitCommand 会无效）
            if (_autoBattle && !_play.AwaitingCommand)
            {
                _autoBattle = false;
                if (_tmpLog != null) _tmpLog.text = "已接管 —— 从下一个单位开始由你下令";
                Sfx("sfx_bt_cmd_select");               // 接管是一次"选中/接管"动作
                RefreshActionBar();
                return;
            }

            _autoBattle = false;                        // 手动下令即视为关自动
            _play.SubmitCommand(skillIndex, -1);        // 下标 = SkillType；目标暂交 AI
            Sfx("sfx_bt_cmd_confirm");                  // 下令音（施法音随后由战斗事件流播）
            RefreshActionBar();
        }

        private void OnComboClicked()
        {
            if (_play == null || !_play.AwaitingCommand) return;
            var combos = _play.AvailableCombos;
            if (combos.Count == 0) return;
            _autoBattle = false;
            _play.SubmitCombo(combos[0].Id);      // v1：发第一条可用连携（多条选择下一轮做）
            Sfx("sfx_bt_cmd_confirm");            // 下令音（连携的"发动"音由事件流补）
            RefreshActionBar();
        }

        private void OnAutoBattleClicked()
        {
            if (_play == null) return;
            _autoBattle = !_autoBattle;
            if (_autoBattle) _play.SubmitCommand(-1, -1);   // -1 = 交给 AI（自动战斗）
            if (_tmpLog != null) _tmpLog.text = _autoBattle ? "自动战斗：开" : "自动战斗：关（等你下令）";
            Sfx("sfx_bt_auto_toggle");
            RefreshActionBar();
        }
    }
}
