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
            _btnCombo.interactable = combos.Count > 0;
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
                //   ⇒ 点一次连携触发 3 次 SubmitCombo（灵力多扣 / 效果重复，用户实测报障）。
                HookTipCombo();
                EnsureAutoSpin();
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

            string[] names = { "普攻", "战记", "终结技", "觉醒技" };   // 下标 = SkillType 枚举值（3=Awaken）
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
            ApplyPvpUiMode();      // ★ 好友对战：隐藏手动 UI + 强制自动（每次激活都会再应用）
        }

        /// <summary>
        /// 好友对战 UI 模式：隐藏【所有】手动操作（技能/连携/自动切换/倍速/自动布阵/撤退），
        /// 强制开启自动战斗。⚠ 必须在【每次战斗开始】时调用 ——
        /// BuildActionBar 只在面板创建时跑一次，那时 LastWasPvp 还没被 EnterBattle 设置（踩过）。
        /// </summary>
        private void ApplyPvpUiMode()
        {
            Debug.Log("[BattlePanel][调试] ApplyPvpUiMode 调用：LastWasPvp=" + SceneFlow.LastWasPvp);
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

        /// <summary>按"是否在等下令"刷新操作区（StepPlayback 每帧调）。</summary>
        /// <summary>
        /// ★ 临时诊断（定位"操作区/技能按钮消失"用；定位后删）：
        /// 打印操作区与四个技能按钮的显隐/可点状态。**只在状态变化时调用**，避免刷屏。
        /// </summary>
        private void LogActionBarState(string why)
        {
            if (_play == null && _actionBar == null) return;
            var u = _play != null ? _play.PendingUnit : null;
            var sb = new System.Text.StringBuilder();
            sb.Append("[战斗诊断] ").Append(why);
            if (_play != null)
                sb.Append("｜awaiting=").Append(_play.AwaitingCommand)
                  .Append(" auto=").Append(_autoBattle)
                  .Append(" seq=").Append(_play.DecisionSeq);
            sb.Append(" 待令=").Append(u != null ? (u.DisplayName + "/" + u.Side) : "null");
            sb.Append(" 操作区=").Append(_actionBar != null ? (_actionBar.gameObject.activeSelf ? "亮" : "隐") : "null");
            if (_skillBtns != null)
            {
                sb.Append(" 技能按钮[");
                for (int i = 0; i < _skillBtns.Length; i++)
                    sb.Append(_skillBtns[i] == null
                              ? "null"
                              : ((_skillBtns[i].gameObject.activeSelf ? "亮" : "隐") +
                                 (_skillBtns[i].interactable ? "/可点" : "/灰"))).Append(' ');
                sb.Append(']');
            }
            Debug.Log(sb.ToString());
        }

        private void RefreshActionBar()
        {
            if (_actionBar == null || _play == null) return;
            // ⚠ 自动模式下也要显示操作区：否则按钮消失后再也点不到「自动战斗」开关，
            //    玩家就被卡在自动里出不来（用户实测反馈：打着打着自动了、按钮没了）。
            bool show = _play.AwaitingCommand || _autoBattle;
            bool before = _actionBar.gameObject.activeSelf;
            if (before != show)
            {
                _actionBar.gameObject.SetActive(show);
                LogActionBarState(show ? "操作区亮出" : "操作区隐藏");   // ★ 临时诊断
            }
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

            RefreshOrderList();

            var cfg = stt != null ? stt.Config : null;
            if (_tmpActor != null && u != null)
            {
                string line = "轮到「" + u.DisplayName + "」　灵力 " + mp + "/" + mpMax +
                              "（战记 " + WanXiang.Battle.Core.BattleState.MpCostOf(SkillType.Active) + " 点）";
                if (u.GetSkill(SkillType.Ultimate) != null)
                    line += "　元气 " + (int)u.Rage + "/" + (int)u.RageCap;
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

                // ★ 觉醒技（第 4 槽）：**没装备就隐藏**（用户要求）；
                //   装了则按 CanCast（元气满）决定可否点击。
                if (_skillBtns.Length > 3 && _skillBtns[3] != null)
                {
                    bool hasAwaken = u.GetSkill(SkillType.Awaken) != null;
                    bool before3 = _skillBtns[3].gameObject.activeSelf;
                    if (before3 != hasAwaken)
                        _skillBtns[3].gameObject.SetActive(hasAwaken);
                    if (hasAwaken)
                        _skillBtns[3].interactable = cfg != null && u.CanCast(SkillType.Awaken, cfg);
                }
            }

            // 连携按钮不在每帧路径里刷新（见 RefreshComboButton，避免每帧 GC/查子节点）
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
            LogActionBarState("点了技能槽 " + skillIndex);   // ★ 临时诊断
            _play.SubmitCommand(skillIndex, -1);        // 下标 = SkillType；目标暂交 AI
            LogActionBarState("提交指令后");             // ★ 临时诊断
            RefreshActionBar();
        }

        private void OnComboClicked()
        {
            if (_play == null || !_play.AwaitingCommand) return;
            var combos = _play.AvailableCombos;
            if (combos.Count == 0) return;
            _autoBattle = false;
            _play.SubmitCombo(combos[0].Id);      // v1：发第一条可用连携（多条选择下一轮做）
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
