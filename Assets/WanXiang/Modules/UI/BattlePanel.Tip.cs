// ============================================================================
//  万相 · 战斗面板 · Tip（从 BattlePanel.cs 拆出：**纯搬家、零行为变更**）
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

        private void BuildSkillTip()
        {
            if (_tipPanel != null)
            {
                // prefab 已绑定（生成器产物）→ 取引用即可（_tipText 也来自 prefab）
                _tipGroup = _tipPanel.GetComponent<CanvasGroup>();
                if (_tipGroup == null) _tipGroup = _tipPanel.gameObject.AddComponent<CanvasGroup>();
                _tipGroup.alpha = 0f;
                _tipGroup.blocksRaycasts = false;
                _tipPanel.gameObject.SetActive(false);
                return;
            }

            var go = new GameObject("Root_SkillTip", typeof(RectTransform));
            _tipPanel = (RectTransform)go.transform;
            _tipPanel.SetParent(transform, false);           // 挂在本面板下，不受操作区显隐影响
            // ★ 左上角锚定（pivot 也左上）：高度随内容自动向下生长，文字永远从左上角起排
            //   （用户 2026-10-12："悬浮面板的字位置会动来动去，从左上角对齐，多的往下加不就行了"）。
            //   之前固定 260 高 + 左中 pivot ， 长文本溢出、短文本留白，内容一变视觉就乱跳。
            _tipPanel.anchorMin = _tipPanel.anchorMax = new Vector2(0f, 1f);
            _tipPanel.pivot = new Vector2(0f, 1f);
            _tipPanel.sizeDelta = new Vector2(430f, 100f);   // 宽度固定；高度由 ContentSizeFitter 撑开
            _tipPanel.anchoredPosition = new Vector2(30f, -40f);   // 屏幕左上，向下偏移 40
            // 代码建的提示框挂 kit toast 底（深色半透明 + 青细线）
            var tipImg = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            var toastSp = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Kit/toast");
            if (toastSp != null) { tipImg.sprite = toastSp; tipImg.type = Image.Type.Sliced; tipImg.color = Color.white; }
            tipImg.raycastTarget = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.95f);    // 墨底，和战斗 HUD 一致

            _tipGroup = go.AddComponent<CanvasGroup>();
            _tipGroup.alpha = 0f;
            _tipGroup.blocksRaycasts = false;

            var trt = new GameObject("Tmp_Tip", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_tipPanel, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 14f);
            trt.offsetMax = new Vector2(-18f, -14f);
            _tipText = trt.gameObject.AddComponent<TextMeshProUGUI>();
            _tipText.fontSize = 22;
            _tipText.color = new Color(0.16f, 0.13f, 0.09f, 1f);   // 米色纸面底 ⇒ 墨字
            _tipText.alignment = TextAlignmentOptions.TopLeft;
            _tipText.raycastTarget = false;

            _tipPanel.gameObject.SetActive(false);
        }

        /// <summary>给连携按钮挂悬浮说明（用户不知道连携是什么 —— 界面必须自我解释）。</summary>
        private void HookTipCombo()
        {
            if (_btnCombo == null) return;
            var trg = _btnCombo.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = _btnCombo.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowComboTip());
            trg.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideSkillTip());
            trg.triggers.Add(exit);
        }

        /// <summary>连携说明（无论当前是否可发动，都要解释它是什么）。</summary>
        private void ShowComboTip()
        {
            if (_tipPanel == null || _tipText == null) return;
            // ★ 防抖（与 ShowSkillTip 同款）：同一槽位且面板已显示 ， 直接返回。
            //   反复 PointerEnter 会 Kill+重建 DOTween Sequence（1 容器 + 3 Tweener），
            //   淡入永远走不完 ， 表现就是"连携悬浮提示有时候不显示"（用户报障）。
            if (_tipShowingSlot == 90 && _tipPanel.gameObject.activeSelf) return;
            _tipShowingSlot = 90;          // 连携（不复用战记槽位）
            var combos = _play != null ? _play.AvailableCombos : null;
            string title, body;
            var pendingUnit = _play != null ? _play.PendingUnit : null;
            if (combos != null && combos.Count > 0)
            {
                var c = combos[0];
                title = "连携·" + c.Name;
                body = c.Note;
                // ★ 伤害实算（用户 2026-09-28：可发动时"没看见描述伤害"）：连携是**固定系数、无随机**，
                //   与 ExecuteCombo 完全同公式 RoundDamage(攻击 × Power × 存活系数)，面板数字 = 实战数字。
                if (pendingUnit != null && _play.State != null)
                {
                    int alive = ComboRules.AliveCount(_play.State, WanXiang.Battle.Core.TeamSide.Player);
                    float mul = ComboRules.PowerMulFor(alive);
                    body += "\n伤害实算：每个目标 "
                          + WanXiang.Battle.Core.CoreMath.RoundDamage(pendingUnit.Attack * c.Power * mul)
                          + " 点（攻击 " + (int)pendingUnit.Attack + " × " + (c.Power * 100f).ToString("0") + "%"
                          + " × 存活系数 " + (mul * 100f).ToString("0") + "%）";
                    body += "\n消耗：灵力 " + ComboRules.MpCostFor(_play.State, WanXiang.Battle.Core.TeamSide.Player)
                          + " 点（每存活 1 只 1 点）+ 主兽满份大招能量";
                }
                else
                {
                    body += "\n消耗：每存活 1 只 1 点灵力 + 主兽满份大招能量";
                }
                body += "\n限制：每种阵形每场限一次\n条件：主兽站【中宫】+ 阵形格全被存活队友占据"
                      + "\n释放：先判 QTE（三段按键），命中过半才发动；失败扣 1 灵力 + 50% 大招能量"
                      + "\n\n点按钮开始判定";
                // ★ 连携也要九宫格高亮（用户报障：放连携没有高亮提示）。
                //   目标口径走核心的 PreviewComboTargets（只读、与 ExecuteCombo 同源）。
                HighlightComboTargets(pendingUnit, c);
            }
            else
            {
                title = "连携技";
                var why = pendingUnit != null && _play.State != null
                    ? ComboRules.WhyNot(_play.State, pendingUnit)
                    : "没有待令单位";
                // ★ 2026-10-20 用户："不用那么复杂的说明，直接说明效果是什么就行了"
                body = "效果：全员追加一次普攻；中宫主兽按阵形释放一次全体技。\n"
                     + "条件：中宫有主兽，且阵形格位被占满（下方阵形栏亮起即可）。\n"
                     + "消耗：主兽满大招能量 + 每存活 1 只 1 点灵力。\n\n当前："
                     + (why ?? "可以发动");
            }

            // ★ 修复（用户 2026-09-28 报障："连携可以释放时悬浮提示就没了"）：
            //   原来填字+显示+淡入这段被缩进了 else 分支 ， 可发动分支只赋值 title/body 就结束，
            //   面板根本没显示。两个分支都必须走到这里。
            _tipText.text = "<size=26><b>" + title + "</b></size>\n" + body;
            // 内容变了 ， 立即按新文本重算面板高度（左上锚定下向下生长），避免当帧还是旧尺寸
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_tipPanel);

            _tipPanel.gameObject.SetActive(true);
            _tipTween?.Kill();
            _tipGroup.alpha = 0f;
            _tipPanel.anchoredPosition = new Vector2(30f, -40f);
            _tipTween = DOTween.Sequence()
                .Join(_tipPanel.DOAnchorPos(new Vector2(52f, -40f), 0.18f).SetEase(Ease.OutQuad))
                .Join(_tipGroup.DOFade(1f, 0.18f));
        }

        /// <summary>
        /// 鼠标是否已离开所有战记按钮 → 是则收起提示。
        /// Overlay 画布用 null 相机（RectTransformUtility 对 ScreenSpaceOverlay 要求 cam = null）。
        /// </summary>
        private void CheckTipHover()
        {
            if (_skillBtns == null || _btnCombo == null) { if (_skillBtns == null) HideSkillTip(); return; }
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    _btnCombo.transform as RectTransform, Input.mousePosition, null))
                return;      // 在连携按钮上
            for (int i = 0; i < _skillBtns.Length; i++)
            {
                if (_skillBtns[i] == null) continue;
                var rt = _skillBtns[i].transform as RectTransform;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
                    return;      // 还在某个战记按钮上
            }
            HideSkillTip();
        }

        /// <summary>给按钮挂鼠标进出（用 EventTrigger，不改 prefab）。</summary>
        private void HookTip(Button btn, int slot)
        {
            if (btn == null) return;
            var trg = btn.gameObject.GetComponent<EventTrigger>();
            if (trg == null) trg = btn.gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowSkillTip(slot));
            trg.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideSkillTip());
            trg.triggers.Add(exit);
        }

        /// <summary>填充并淡入提示：从左滑入 20px + 透明度 0→1。</summary>
        private void ShowSkillTip(int slot)
        {
            if (_tipPanel == null || _play == null) return;
            // ★ 防抖：同一槽位且面板已显示 → 直接返回。反复悬浮会疯狂创建 DOTween
            //   Sequence（每次 1 个容器 + 2 个 Tweener），编辑器实测直接卡死。
            if (_tipShowingSlot == slot && _tipPanel.gameObject.activeSelf) return;
            _tipShowingSlot = slot;
            var u = _play.PendingUnit;

            string title, body;
            if (slot == 0) { title = "普攻"; body = CostLine(0); }
            else if (slot == 1) { title = "战记 · 主动"; body = CostLine(1); }
            else if (slot == 2) { title = "终结技"; body = CostLine(2); }
            else { title = "觉醒技"; body = CostLine(3); }   // slot==3（及更高，防御性默认）

            if (u != null)
            {
                var sk = u.GetSkill((SkillType)slot);
                if (sk != null)
                {
                    if (!string.IsNullOrEmpty(sk.Name)) title = sk.Name;
                    // ★ 用户要求：面板数字要与实战**对得上** ， 伤害走**战斗公式实算**：
                    //   对场上每个存活敌人用 BattleSimulator.ComputeDamage 各算一遍
                    //   （含五行克制 / 目标防御 / 天时），显示**区间**；暴击是随机的，单独标出。
                    //   ⚠ 公式与战斗同源（同一个 ComputeDamage），不产生第二份实现。
                    string desc;
                    if (WanXiang.Battle.Core.SkillMath.TryDescribeInBattle(
                            _play.State, u, sk, out string battleNums))
                    {
                        desc = battleNums;
                    }
                    else if (WanXiang.Battle.Core.SkillMath.TryDescribe(
                                 sk, (int)u.Attack, u.MaxHp, false, out string nums))
                    {
                        // 场上没有存活敌人等算不了实算的情形 ， 退回战前预览口径（基础值 + 免责说明）
                        desc = nums + (WanXiang.Battle.Core.SkillMath.HasDamage(sk)
                                     ? "（" + WanXiang.Battle.Core.SkillMath.DamageNote + "）" : "");
                    }
                    else
                    {
                        // 全是状态/驱散这类算不出点数的效果 ， 退回内容原文
                        desc = string.IsNullOrEmpty(sk.Description) ? "（暂无描述）" : sk.Description;
                        // 原文里可能出现百分比，此时才需要那句解释
                        if (desc.Contains("%"))
                            desc += "\n（描述里的百分比是攻击力系数，不是生命百分比）";
                    }

                    // ★★ 元素预告（2026-10-18 用户："攻击前提示，告诉玩家这样攻击将可以干什么"）：
                    //   把"克制倍率 / 会引爆什么反应 / 会留下什么印记"直接写在技能提示里，
                    //   而不是等打完才在飘字里看到。只读，不掷骰（不碰随机数）。
                    string elemHint = BuildElementHint(_play.State, u, sk);
                    if (!string.IsNullOrEmpty(elemHint)) desc += "\n" + elemHint;

                    // ★ 九宫格高亮：一眼看出"打谁 / 给谁加盾"（用户诉求）
                    HighlightPreviewTargets(u, sk);

                    // 目标规则：与核心的"普攻打最前排"特判保持一致（否则界面会误导布阵）。
                    //  ⚠ 取**效果 atom** 的目标而不是 PrimaryTarget —— 后者是"主目标"语义，
                    //    对"给自己人加盾"这类技能会显示成"生命最低的敌人"（实测踩到）。
                    var target = sk.PrimaryTarget;
                    if (sk.Effects != null)
                        for (int ai = 0; ai < sk.Effects.Length; ai++)
                            if (sk.Effects[ai].Target != WanXiang.Battle.Core.TargetSelector.Self)
                            { target = sk.Effects[ai].Target; break; }
                    string targetName = sk.Cd == 0
                        ? "最前排（普攻默认打前排，站位决定谁先承伤）"
                        : TargetNameOf(target);
                    body = desc + "\n目标：" + targetName + "\n\n" + body;
                    if ((slot == 2 || slot == 3) && u.Rage < u.RageCap)
                        body += "\n当前元气 " + (int)u.Rage + "/" + (int)u.RageCap + "（满值才可释放）";
                }
                else
                {
                    body = slot == 3 ? "这只异兽没有装备觉醒技。" : "这只异兽没有这一槽战记。";
                }
            }
            else
            {
                body = "轮到某个单位行动时才能查看具体战记效果。\n\n" + body;
            }

            _tipText.text = "<size=26><b>" + title + "</b></size>\n" + body;
            // 内容变了 ， 立即按新文本重算面板高度（左上锚定下向下生长），避免当帧还是旧尺寸
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_tipPanel);

            _tipPanel.gameObject.SetActive(true);
            _tipTween?.Kill();
            _tipGroup.alpha = 0f;
            _tipPanel.anchoredPosition = new Vector2(30f, -40f);          // 起点：左上、略靠左（滑入）
            _tipTween = DOTween.Sequence()
                .Join(_tipPanel.DOAnchorPos(new Vector2(52f, -40f), 0.18f).SetEase(Ease.OutQuad))
                .Join(_tipGroup.DOFade(1f, 0.18f));
        }

        /// <summary>
        /// 技能预览：把"会被作用到的九宫格格子"点亮 —— 解决"打谁 / 给谁治盾"看不见的问题。
        /// 敌方格暖红、"我方格柔绿；随机类目标（RandomEnemy）无法预测 ， 点亮该侧全部存活格。
        /// ⚠ 目标预测走 <see cref="WanXiang.Battle.Core.BattleSimulator.PreviewTargets"/>
        ///   （只读、不掷骰），**不在这里另写一套目标选择逻辑**（否则必然与实战漂移）。
        /// </summary>
        /// <summary>
        /// **元素预告**（2026-10-18）：这一击打出去会克制谁、会引爆什么反应、会留下什么印记。
        /// <para>口径与战斗同源：克制用 <see cref="WanXiang.Battle.Core.ElementMatrix.Relation"/>，
        /// 反应用与 <c>ResolveElementReactions</c> 完全相同的属性+状态组合；只读不掷骰。</para>
        /// </summary>
        private static string BuildElementHint(WanXiang.Battle.Core.BattleState st,
                                              WanXiang.Battle.Core.BattleUnit src,
                                              WanXiang.Battle.Core.SkillDef sk)
        {
            if (st == null || src == null || sk == null || sk.Effects == null) return null;
            var foes = st.UnitsOf(WanXiang.Battle.Core.TeamSide.Enemy);
            if (foes == null || foes.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            bool anyCounter = false, anyCountered = false;
            var reactions = new System.Collections.Generic.List<string>();

            for (int a = 0; a < sk.Effects.Length; a++)
            {
                var atom = sk.Effects[a];
                if (atom.Kind != WanXiang.Battle.Core.EffectAtomKind.Damage) continue;
                var el = WanXiang.Battle.Core.BattleSimulator.ResolveElement(atom.ElementOverride, sk, src);
                if (el == WanXiang.Battle.Core.Element.None) continue;

                // 印记预告（只有战技/绝技会落印，与 ApplyElementalMark 同口径）
                if (sk.Type != WanXiang.Battle.Core.SkillType.Basic)
                {
                    if (el == WanXiang.Battle.Core.Element.Fire && sb.ToString().IndexOf("灼烧") < 0)
                        sb.Append("留下灼烧  ");
                    if (el == WanXiang.Battle.Core.Element.Water && sb.ToString().IndexOf("湿") < 0)
                        sb.Append("留下湿  ");
                }

                for (int i = 0; i < foes.Count; i++)
                {
                    var f = foes[i];
                    if (f == null || !f.IsAlive) continue;
                    var rel = WanXiang.Battle.Core.ElementMatrix.Relation(el, f.Element);
                    if (rel == WanXiang.Battle.Core.ElementRelation.Counter) anyCounter = true;
                    if (rel == WanXiang.Battle.Core.ElementRelation.Countered) anyCountered = true;

                    string react = null;
                    if (el == WanXiang.Battle.Core.Element.Fire && f.HasStatus(WanXiang.Battle.Core.StatusCatalog.Wet)) react = "蒸发";
                    else if (el == WanXiang.Battle.Core.Element.Metal && f.HasStatus(WanXiang.Battle.Core.StatusCatalog.Wet)) react = "感电";
                    else if (el == WanXiang.Battle.Core.Element.Metal && f.HasStatus(WanXiang.Battle.Core.StatusCatalog.IceErosion)) react = "碎冰";
                    else if (el == WanXiang.Battle.Core.Element.Wood && f.HasStatus(WanXiang.Battle.Core.StatusCatalog.Burn)) react = "助燃";
                    else if (el == WanXiang.Battle.Core.Element.Earth && f.HasStatus(WanXiang.Battle.Core.StatusCatalog.Burn)) react = "镇";
                    if (react != null && !reactions.Contains(react)) reactions.Add(react);
                }
            }

            // ★ 2026-10-20 用户："可以触发反应的字体是不是要换一个颜色，战记施加的元素的字体也要不同的颜色"
            //   ⛔ 另外原来用的 ✔ △ 🔥 ◆ 在 Deng SDF 里**没有字形**（显示成口口），全部换成文字 + 富文本色。
            var parts = new System.Collections.Generic.List<string>();
            if (anyCounter) parts.Add("<color=#4E9A3F>克制（×1.5）</color>");
            if (anyCountered) parts.Add("<color=#C2432B>被克（×0.75）</color>");
            if (reactions.Count > 0)
                parts.Add("<color=#C98A1E>将触发：" + string.Join("/", reactions.ToArray()) + "</color>");
            string marks = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(marks)) parts.Add("<color=#3E6E8C>" + marks + "</color>");
            if (parts.Count == 0) return null;
            return string.Join("　", parts.ToArray());
        }

        private void HighlightPreviewTargets(WanXiang.Battle.Core.BattleUnit u,
                                             WanXiang.Battle.Core.SkillDef sk)
        {
            if (_stage == null || _play == null || _play.State == null) return;

            bool isRandom;
            WanXiang.Battle.Core.BattleSimulator.PreviewTargets(
                _play.State, u, sk, _previewTargets, out isRandom);

            _previewAllyCells.Clear();
            _previewFoeCells.Clear();
            for (int i = 0; i < _previewTargets.Count; i++)
            {
                var t = _previewTargets[i];
                if (t == null || !t.Pos.IsValid) continue;
                if (t.Side == WanXiang.Battle.Core.TeamSide.Player) _previewAllyCells.Add(t.Pos.Index);
                else _previewFoeCells.Add(t.Pos.Index);
            }
            _stage.HighlightCells(_previewAllyCells, _previewFoeCells);
        }

        /// <summary>连携的九宫格高亮（与 <see cref="HighlightPreviewTargets"/> 同一套格子配色）。</summary>
        private void HighlightComboTargets(WanXiang.Battle.Core.BattleUnit host,
                                           WanXiang.Battle.Core.ComboDef combo)
        {
            if (_stage == null || _play == null || _play.State == null || host == null || combo == null) return;

            WanXiang.Battle.Core.BattleSimulator.PreviewComboTargets(
                _play.State, host, combo, _previewTargets);

            _previewAllyCells.Clear();
            _previewFoeCells.Clear();
            for (int i = 0; i < _previewTargets.Count; i++)
            {
                var t = _previewTargets[i];
                if (t == null || !t.Pos.IsValid) continue;
                if (t.Side == WanXiang.Battle.Core.TeamSide.Player) _previewAllyCells.Add(t.Pos.Index);
                else _previewFoeCells.Add(t.Pos.Index);
            }
            _stage.HighlightCells(_previewAllyCells, _previewFoeCells);
        }

        /// <summary>淡出并收起。</summary>
        private void HideSkillTip()
        {
            // 收起提示 = 同时取消九宫格高亮
            if (_stage != null) _stage.HighlightCells(null, null);
            if (_tipPanel == null || !_tipPanel.gameObject.activeSelf) return;
            _tipShowingSlot = -1;
            _tipTween?.Kill();
            _tipTween = _tipGroup.DOFade(0f, 0.12f).OnComplete(() =>
            {
                if (_tipPanel != null) _tipPanel.gameObject.SetActive(false);
            });
        }
    }
}
