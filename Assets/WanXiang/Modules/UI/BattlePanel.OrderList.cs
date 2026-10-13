// ============================================================================
//  万相 · 战斗面板 · OrderList（从 BattlePanel.cs 拆出：**纯搬家、零行为变更**）
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

        private void BuildOrderList()
        {
            // ⚠ 不能写成 `if (_orderPanel != null) return;` —— prefab 绑定时会直接返回，
            //   把下面"取行内引用"的代码整段跳过（_orderHeads/_orderNames 永远为 null，
            //   行动条就成了空壳）。必须在这里取完引用再 return。（这个坑踩过三次了）
            if (_orderPanel != null && _orderRows != null && _orderRows.Length > 0 && _orderRows[0] != null)
            {
                _orderHeads = new Image[_orderRows.Length];
                _orderNames = new TMP_Text[_orderRows.Length];
                for (int i = 0; i < _orderRows.Length; i++)
                {
                    if (_orderRows[i] == null) continue;
                    var h = _orderRows[i].Find("Head");
                    if (h != null) _orderHeads[i] = h.GetComponent<Image>();
                    var n = _orderRows[i].Find("Tmp_Name");
                    if (n != null) _orderNames[i] = n.GetComponent<TMP_Text>();
                }
                // 标题（运行时按名取，显示"第 N 回合 · 行动顺序"——用户 2026-10-13 要求加回合数）
                if (_orderTitle == null)
                {
                    var t = _orderPanel.Find("Tmp_OrderTitle");
                    if (t != null) _orderTitle = t.GetComponent<TMP_Text>();
                }
                // ★ 状态悬浮框（prefab 节点）：默认收起；模板行也收起来，运行时按需克隆
                if (_statusTip != null) _statusTip.gameObject.SetActive(false);
                if (_statusTipItem != null) _statusTipItem.gameObject.SetActive(false);

                // ⚠ 悬停**不挂在行动顺序的行上**（用户 2026-10-07 定案：触发走世界空间 —— 鼠标直接指棋盘上的异兽）。
                //   原因：UGUI 的 PointerEnter/Exit **不冒泡**（只发给命中对象），挂行上要额外加受击层，且不如直接指单位直观。
                //   行上的 EventTrigger/Img_RowHit 先留着不接线（prefab 结构不动，将来要恢复只补一行绑定）。

                _orderPanel.gameObject.SetActive(false);
                return;
            }
            if (_orderPanel != null) { _orderPanel.gameObject.SetActive(false); return; }   // 只绑了面板没绑行：显示空壳，不崩

            // （原「prefab 没有才自建」的兜底段已删除 —— 该分支不可达，UI 一律在 prefab 里做好）
}

        /// <summary>刷新行动顺序（带守卫：只有变化才重建 UI）。</summary>
        private void RefreshOrderList()
        {
            if (_orderPanel == null || _play == null || _play.State == null) return;
            var stt = _play.State;

            // ⚠ 读 State.TurnOrder（回合开始时定下的那份），不要自己按当前速度重排 ——
            //   否则加速/减速之后，界面显示的顺序会与模拟真正执行的顺序不一致。
            _orderBuf.Clear();
            // ⚠ 显示"**接下来**轮到谁"：以当前事件的 actorId 在本回合序列中的位置为界，
            //   只展示它之后（含它自己）的单位 —— 已行动过的不再列出来（用户实测嫌乱）。
            _orderBuf.AddRange(stt.TurnOrder);
            // ⚠ "当前行动者"两种来源（都实测过，别合并）：
            //   · 等玩家下令 → 待令单位就是"该行动的"（PendingUnit）
            //   · 演出中 → **只在 ActionBegin 更新**的 _orderActorId（不是每条事件的 actorId！）
            //     以前用每条事件的 actorId，回合末一堆 Damage/Heal/RoundResolve（actor 各不相同）
            //     会让高亮/起点反复跳 ⇒ 行动面板"疯狂刷新几下"（用户 2026-10-13 报障）。
            BattleUnit cur = null;
            if (_play.AwaitingCommand && _play.PendingUnit != null)
            {
                cur = _play.PendingUnit;
            }
            else if (!string.IsNullOrEmpty(_orderActorId))
            {
                for (int i = 0; i < _orderBuf.Count; i++)
                    if (_orderBuf[i].RuntimeId == _orderActorId) { cur = _orderBuf[i]; break; }
            }

            // 指纹：顺序（RuntimeId 的 hash 组合）+ 当前行动者 + 回合
            int stamp = 17;
            for (int i = 0; i < _orderBuf.Count; i++)
                stamp = stamp * 31 + (_orderBuf[i].RuntimeId != null ? _orderBuf[i].RuntimeId.GetHashCode() : 0);
            string curId = (cur != null ? cur.RuntimeId : null) + "#" + stt.Turn;
            bool same = stamp == _lastOrderStamp && curId == _lastActorId;
            if (same && _orderPanel.gameObject.activeSelf) return;   // ★ 守卫：没变化就什么都不做
            _lastOrderStamp = stamp;
            _lastActorId = curId;

            // ★ 标题带回合数（用户 2026-10-13："行动面板加一行字显示第几回合"）
            if (_orderTitle != null)
            {
                string title = "第 " + stt.Turn + " 回合 · 行动顺序";
                if (title != _lastOrderTitle) { _lastOrderTitle = title; _orderTitle.text = title; }
            }

            if (_orderBuf.Count == 0) { _orderPanel.gameObject.SetActive(false); return; }
            _orderPanel.gameObject.SetActive(true);

            // 只显示"**还没行动**"的单位：从当前演出者（ev.ActorId）的位置开始。
            // 之前显示整条回合序列，已行动过的还挂在上面 —— 用户实测嫌乱（"越做越奇怪"）。
            // ⚠ start 与 cur 必须**同一来源**：等令时 cur = PendingUnit（九尾狐），
            //   而 start 若还按"上一个已播事件"的 actor 找，就会从别的位置开始切，
            //   把待令单位自己切掉（用户截图：轮到九尾狐，面板却只剩鹿蜀）。
            int start = 0;
            if (cur != null)
                for (int i = 0; i < _orderBuf.Count; i++)
                    if (_orderBuf[i].RuntimeId == cur.RuntimeId) { start = i; break; }

            int shown = 0;
            for (int i = start; i < _orderBuf.Count && shown < OrderRowCount; i++)
            {
                var u = _orderBuf[i];
                if (!u.IsAlive) continue;

                bool mine = u.Side == WanXiang.Battle.Core.TeamSide.Player;
                // ★ 当前行动者**不分敌我都高亮**（用户 2026-10-13：轮到敌人时要能看见是谁在行动，
                //   以前只高亮我方 ⇒ 敌人出手时面板像把敌人清了、看不出谁在打）。
                bool isCur = cur != null && u.RuntimeId == cur.RuntimeId;

                _orderRows[shown].gameObject.SetActive(true);
                if (_orderHeads[shown] != null && _sprites != null && u.Def != null)
                {
                    var sp = _sprites.GetHead(u.Def.Id);
                    _orderHeads[shown].sprite = sp;
                    _orderHeads[shown].color = sp != null
                        ? (isCur ? Color.white : new Color(1f, 1f, 1f, 0.85f))
                        : new Color(0f, 0f, 0f, 0f);
                }
                if (_orderNames[shown] != null)
                {
                    _orderNames[shown].text = (isCur ? "▶ " : "") + u.DisplayName
                        + (mine ? "（我方）" : "（敌方）") + (isCur ? " ← 行动中" : "");
                    _orderNames[shown].color = isCur
                        ? new Color(0.94f, 0.76f, 0.43f, 1f)
                        : (mine ? new Color(0.75f, 0.85f, 0.72f, 1f) : new Color(0.91f, 0.71f, 0.66f, 1f));
                }
                shown++;
            }
            for (int i = shown; i < OrderRowCount; i++)
            {
                if (_orderRows[i] == null) continue;
                // 隐藏行顺手清文本：避免残留内容在将来复用/截图时露出来
                if (_orderNames != null && i < _orderNames.Length && _orderNames[i] != null)
                    _orderNames[i].text = string.Empty;
                if (_orderHeads != null && i < _orderHeads.Length && _orderHeads[i] != null)
                    _orderHeads[i].sprite = null;
                _orderRows[i].gameObject.SetActive(false);
            }
        }

        // ====================================================================
        //  状态悬浮列表（**世界空间悬停异兽** → 该单位的状态 + 效果说明）
        // ====================================================================

        /// <summary>
        /// 核心：把某个单位的**全部状态 + 效果说明**摊成列表。
        /// ⛔ 状态数据必须取自**帧流视图**（<c>_stage.TryGetViewStatusIds</c>），**不能读 `_play.State`** ——
        ///   自动模式下 State 是整场跑完的终局态，面板会显示"打完之后的状态"（冰晶那轮的血泪）。
        /// </summary>
        private void ShowStatusTipFor(string unitId, string displayName)
        {
            if (_statusTip == null || _statusTipRows == null || _statusTipItem == null) return;

            // ★ 先激活：行高按"文本实宽"自适应要求布局是活的（inactive 时 rect 宽度会失准）。
            _statusTip.gameObject.SetActive(true);

            string ids = null;
            if (_stage != null) _stage.TryGetViewStatusIds(unitId, out ids);

            if (_statusTipTitle != null)
                _statusTipTitle.text = string.IsNullOrEmpty(displayName) ? "状态" : displayName;
            ClearTipRows();

            var parts = string.IsNullOrEmpty(ids) ? new string[0] : ids.Split('|');
            if (parts.Length == 0)
            {
                AddTipRow("（无状态）", "该单位目前身上没有任何状态。");
            }
            else
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    var kv = parts[i].Split(':');
                    if (kv.Length < 1 || string.IsNullOrEmpty(kv[0])) continue;
                    var def = WanXiang.Battle.Core.StatusCatalog.Get(kv[0]);   // ⚠ StatusDef 是 struct，不能用 != null 判空
                    int stacks = 1;
                    if (kv.Length > 1) int.TryParse(kv[1], out stacks);
                    string nm = !string.IsNullOrEmpty(def.Name) ? def.Name : kv[0];
                    if (stacks > 1) nm += " ×" + stacks;
                    AddTipRow(nm, def.Description);
                }
            }
            FitStatusTipHeight();   // ★ 面板高度随行数/文本高度向下生长（不再固定 300 高、内容溢出）
        }

        private void HideStatusTip()
        {
            if (_statusTip != null) _statusTip.gameObject.SetActive(false);
        }

        // --------------------------------------------------------------------
        //  悬停载体：**世界空间命中**（用户定案 —— 鼠标直接指棋盘上的异兽）
        //  触发用单位身上 BoxCollider2D 的世界包围盒做点测试；列表本体仍是 UGUI 面板。
        // --------------------------------------------------------------------

        /// <summary>上一次悬停到的单位（变了才重算列表，避免每帧重建）。</summary>
        private string _hoverUnitId;
        private float _hoverDbgTimer;

        private void UpdateUnitStatusHover()
        {
            if (_stage == null || _statusTip == null) return;
            if (State != UIPanelState.Opened)
            {
                if (_hoverUnitId != null) { _hoverUnitId = null; HideStatusTip(); }
                return;
            }

            // ⛔ 相机必须用**舞台自己那个**（BattleStage2D.Camera）：
            //   舞台的相机是代码建的、**没打 MainCamera 标签** ， Camera.main 会拿到 null，
            //   然后这里静默 return —— 表现就是"悬停完全没反应"（本轮真因）。
            var cam = _stage.Camera != null ? _stage.Camera : Camera.main;
            if (cam == null)
            {
                _hoverDbgTimer -= Time.deltaTime;
                if (_hoverDbgTimer <= 0f)
                {
                    _hoverDbgTimer = 2f;
                    Debug.LogWarning("[BattlePanel][悬停] 取不到相机：_stage.Camera 与 Camera.main 都是 null");
                }
                return;
            }

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            string uid, uname; Vector3 anchor;
            bool hit = _stage.TryPickUnit(world, out uid, out uname, out anchor);

            if (hit)
            {
                if (uid != _hoverUnitId)
                {
                    _hoverUnitId = uid;
                    ShowStatusTipFor(uid, uname);   // 悬停诊断日志已清（2026-10-12：Console 被它刷屏）
                }
                else if (!_statusTip.gameObject.activeSelf) _statusTip.gameObject.SetActive(true);
                PlaceTipNear(cam, anchor);
            }
            else if (_hoverUnitId != null)
            {
                _hoverUnitId = null;
                HideStatusTip();
            }
        }

        /// <summary>把面板摆到单位的**头顶上方**（世界 → 屏幕 → 父矩形局部），并夹在屏幕内。</summary>
        private void PlaceTipNear(Camera cam, Vector3 anchorWorld)
        {
            var parentRt = _statusTip.parent as RectTransform;
            if (parentRt == null) return;
            Vector3 sp = cam.WorldToScreenPoint(anchorWorld);
            if (sp.z < 0f) return;   // 在相机背后
            Vector2 screen = new Vector2(Mathf.Clamp(sp.x, 10f, Screen.width - 10f),
                                         Mathf.Clamp(sp.y + 24f, 10f, Screen.height - 10f));
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screen, null, out local)) return;
            // 面板锚点/轴心都是右上 (1,1) ， anchoredPosition 是相对**父矩形右上角**的偏移
            _statusTip.anchoredPosition = new Vector2(
                local.x - parentRt.rect.width * 0.5f,
                local.y - parentRt.rect.height * 0.5f);
        }

        // 行排版常量：名称一行高度 / 行间距 / 描述最小高度
        private const float TipNameH = 26f;
        private const float TipRowGap = 6f;
        private const float TipDescMinH = 22f;

        /// <summary>
        /// 清掉上次克隆出来的行（模板行本身留着，它一直是 inactive）。
        /// ⛔ `Destroy` 是**延迟到帧末**的：若不立刻失活/脱离，同一帧里 <see cref="AddTipRow"/>
        ///   统计"已用行"时会把待销毁的旧行也算进去 ， 新区块整体向下错位（用户报"字乱跑"）。
        /// </summary>
        private void ClearTipRows()
        {
            for (int i = _statusTipRows.childCount - 1; i >= 0; i--)
            {
                var c = _statusTipRows.GetChild(i);
                if (c == _statusTipItem) continue;
                c.gameObject.SetActive(false);   // 立刻失活（不再被计入"已用行"）
                c.SetParent(null, false);        // 立刻脱离父节点（Destroy 帧末才生效，仍占 childCount）
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        /// <summary>
        /// 加一行状态（名称 + 描述），**左上对齐、行高随文本自适应、自上而下堆叠**。
        /// 用户 2026-10-12："从左上角对齐，多的往下加" —— 不再用固定 44 高（长描述会溢出叠行）。
        /// 高度用 <see cref="TMP_Text.GetPreferredValues(string,float,float)"/> 直接按文本量出来，
        /// 不依赖布局系统的重建时机（面板刚激活当帧也能量准）。
        /// </summary>
        private void AddTipRow(string name, string desc)
        {
            // 已排下来的高度（只数真正显示的行；模板行 inactive 不计）
            float y = 0f;
            for (int i = 0; i < _statusTipRows.childCount; i++)
            {
                var c = _statusTipRows.GetChild(i) as RectTransform;
                if (c == null || c == _statusTipItem || !c.gameObject.activeSelf) continue;
                y += c.sizeDelta.y + TipRowGap;
            }

            float rowW = _statusTipRows.rect.width;
            if (rowW < 1f) rowW = 344f;              // 兜底：面板宽 360 - 左右内边距
            float textW = Mathf.Max(rowW - 12f, 80f);

            var row = UnityEngine.Object.Instantiate(_statusTipItem, _statusTipRows);
            row.gameObject.SetActive(true);

            var nmRt = row.Find("Tmp_StatusName") as RectTransform;
            var dsRt = row.Find("Tmp_StatusDesc") as RectTransform;

            float nameH = TipNameH;
            float descH = TipDescMinH;

            if (nmRt != null)
            {
                nmRt.anchorMin = nmRt.anchorMax = new Vector2(0f, 1f);   // 左上
                nmRt.pivot = new Vector2(0f, 1f);
                nmRt.anchoredPosition = Vector2.zero;
                nmRt.sizeDelta = new Vector2(textW, TipNameH);
                var t = nmRt.GetComponent<TMP_Text>();
                if (t != null)
                {
                    t.text = name;
                    t.alignment = TextAlignmentOptions.TopLeft;
                    nameH = Mathf.Max(TipNameH, t.GetPreferredValues(name, textW, 0f).y);
                }
            }

            if (dsRt != null)
            {
                dsRt.anchorMin = dsRt.anchorMax = new Vector2(0f, 1f);   // 左上，接在名称下方
                dsRt.pivot = new Vector2(0f, 1f);
                dsRt.anchoredPosition = new Vector2(0f, -nameH);
                var t2 = dsRt.GetComponent<TMP_Text>();
                if (t2 != null)
                {
                    t2.text = desc;
                    t2.alignment = TextAlignmentOptions.TopLeft;
                    descH = Mathf.Max(TipDescMinH, t2.GetPreferredValues(desc, textW, 0f).y);
                }
                dsRt.sizeDelta = new Vector2(textW, descH);
            }

            row.anchorMin = row.anchorMax = new Vector2(0f, 1f);         // 行顶部锚定 ， 向下生长
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(rowW, nameH + descH + 8f);
            row.anchoredPosition = new Vector2(0f, -y);
        }

        /// <summary>把面板高度调到刚好包住标题 + 所有行（不再固定 300 高、内容溢出）。</summary>
        private void FitStatusTipHeight()
        {
            if (_statusTip == null) return;
            float rowsH = 0f;
            for (int i = 0; i < _statusTipRows.childCount; i++)
            {
                var c = _statusTipRows.GetChild(i) as RectTransform;
                if (c == null || c == _statusTipItem || !c.gameObject.activeSelf) continue;
                rowsH += c.sizeDelta.y + TipRowGap;
            }
            float titleH = _statusTipTitle != null ? Mathf.Max(_statusTipTitle.preferredHeight, 30f) : 30f;
            float target = titleH + rowsH + 28f;                        // 上下留白
            target = Mathf.Min(target, Screen.height - 24f);            // 别高过屏幕
            var sz = _statusTip.sizeDelta;
            _statusTip.sizeDelta = new Vector2(sz.x, Mathf.Max(target, 120f));
        }
    }
}
