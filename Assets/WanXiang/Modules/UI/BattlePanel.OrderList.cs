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
                _orderPanel.gameObject.SetActive(false);
                return;
            }
            if (_orderPanel != null) { _orderPanel.gameObject.SetActive(false); return; }   // 只绑了面板没绑行：显示空壳，不崩

            var go = new GameObject("Root_OrderList", typeof(RectTransform));
            _orderPanel = (RectTransform)go.transform;
            _orderPanel.SetParent(transform, false);
            _orderPanel.anchorMin = _orderPanel.anchorMax = new Vector2(1f, 1f);
            _orderPanel.pivot = new Vector2(1f, 1f);
            _orderPanel.sizeDelta = new Vector2(330f, 380f);
            _orderPanel.anchoredPosition = new Vector2(-24f, -120f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.11f, 0.09f, 0.07f, 0.62f);

            // 标题
            var trt = new GameObject("Tmp_Title", typeof(RectTransform)).GetComponent<RectTransform>();
            trt.SetParent(_orderPanel, false);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -6f);
            trt.sizeDelta = new Vector2(-16f, 30f);
            var title = trt.gameObject.AddComponent<TextMeshProUGUI>();
            title.text = "行动顺序（按速度）";
            title.fontSize = 20;
            title.color = new Color(0.96f, 0.94f, 0.88f, 1f);
            title.alignment = TextAlignmentOptions.Center;
            title.raycastTarget = false;

            // 8 行：头像 + 名字
            _orderRows = new RectTransform[OrderRowCount];
            _orderHeads = new Image[OrderRowCount];
            _orderNames = new TMP_Text[OrderRowCount];
            for (int i = 0; i < OrderRowCount; i++)
            {
                var row = new GameObject("Row_" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(_orderPanel, false);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -38f - i * 41f);
                row.sizeDelta = new Vector2(-14f, 38f);
                _orderRows[i] = row;

                var head = new GameObject("Head", typeof(RectTransform)).GetComponent<RectTransform>();
                head.SetParent(row, false);
                head.anchorMin = head.anchorMax = new Vector2(0f, 0.5f);
                head.pivot = new Vector2(0f, 0.5f);
                head.anchoredPosition = new Vector2(6f, 0f);
                head.sizeDelta = new Vector2(36f, 36f);
                _orderHeads[i] = head.gameObject.AddComponent<Image>();
                _orderHeads[i].raycastTarget = false;
                _orderHeads[i].preserveAspect = true;

                var nm = new GameObject("Tmp_Name", typeof(RectTransform)).GetComponent<RectTransform>();
                nm.SetParent(row, false);
                nm.anchorMin = Vector2.zero;
                nm.anchorMax = Vector2.one;
                nm.offsetMin = new Vector2(50f, 0f);
                nm.offsetMax = new Vector2(-6f, 0f);
                _orderNames[i] = nm.gameObject.AddComponent<TextMeshProUGUI>();
                _orderNames[i].fontSize = 19;
                _orderNames[i].alignment = TextAlignmentOptions.MidlineLeft;
                _orderNames[i].raycastTarget = false;
            }
            _orderPanel.gameObject.SetActive(false);
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
            // ⚠ "当前行动者"的语义**分两种**（都实测过，别合并）：
            //   · 演出中 → 跟随画面（当前事件的 actorId）
            //   · 等玩家下令 → **待令单位就是"该行动的"** —— 此时 _play.Current 还停留在
            //     上一单位的收尾事件（ActionEnd）上，若按它找 cur，高亮永远慢一拍（用户实测）。
            BattleUnit cur = null;
            if (_play.AwaitingCommand)
            {
                cur = _play.PendingUnit;
            }
            else
            {
                var ev = _play.Current;   // BattleEvent 是 struct，无需 null 检查
                if (!string.IsNullOrEmpty(ev.ActorId))
                    for (int i = 0; i < _orderBuf.Count; i++)
                        if (_orderBuf[i].RuntimeId == ev.ActorId) { cur = _orderBuf[i]; break; }
            }

            // 指纹：顺序（RuntimeId 的 hash 组合）+ 当前行动者
            int stamp = 17;
            for (int i = 0; i < _orderBuf.Count; i++)
                stamp = stamp * 31 + (_orderBuf[i].RuntimeId != null ? _orderBuf[i].RuntimeId.GetHashCode() : 0);
            string curId = (cur != null ? cur.RuntimeId : null) + "#" + stt.Turn;
            bool same = stamp == _lastOrderStamp && curId == _lastActorId;
            if (same && _orderPanel.gameObject.activeSelf) return;   // ★ 守卫：没变化就什么都不做
            _lastOrderStamp = stamp;
            _lastActorId = curId;

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
                // 用户要求：**只高亮我方**。敌方出手不必高亮（玩家只需知道"还没轮到我"），
                // 高亮跳到敌人身上反而误导成"该我操作了"。
                bool isCur = mine && cur != null && u.RuntimeId == cur.RuntimeId;

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
    }
}
