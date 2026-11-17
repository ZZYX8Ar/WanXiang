// ============================================================================
//  万相 · 编队拖拽 / 滚动 / 幽灵预览（2026-11-17 代码骨架重构）
//  从 FormationPanel.cs 原样搬入，逻辑零改动。
// ============================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    public sealed partial class FormationPanel
    {
        private int _holdBeast = -1;
        public void RosterHoldBegin(int beastIndex)
        {
            _holdBeast = -1;
            CancelInvoke(nameof(MarkRosterHold));
            _holdBeastPending = beastIndex;
            Invoke(nameof(MarkRosterHold), 0.28f);
        }
        public void RosterHoldEnd() { CancelInvoke(nameof(MarkRosterHold)); _holdBeast = -1; _holdBeastPending = -1; }
        public bool IsRosterHold(int beastIndex) => _holdBeast >= 0 && _holdBeast == beastIndex;

        private int _holdBeastPending = -1;
        private void MarkRosterHold() { _holdBeast = _holdBeastPending; }
        private bool _dragActive;

        /// <summary>给 9 个格子挂上点击/拖拽代理（幂等，面板常驻时只挂一次）。</summary>
        private void WireCells()
        {
            if (_cells == null) return;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == null) continue;
                var item = _cells[i].gameObject.GetComponent<CellDragItem>();
                if (item == null) item = _cells[i].gameObject.AddComponent<CellDragItem>();
                item.Owner = this;
                item.CellIndex = i;
            }
        }

        /// <summary>单击卡池条目 = 选中（高亮）；再点格子放置。双击仍可直接上阵。</summary>
        /// <summary>是否已经从卡池"点选"了某只兽（格子单击据此决定是放置还是等双击）。</summary>
        public bool HasSelectedBeast => _selectedBeast >= 0;

        public void SelectRosterItem(int beastIndex, Image outline)
        {
            SelectBeast(beastIndex, outline);
            CommitLayout();
        }

        /// <summary>点击卡池条目：已上阵 → 下阵；未上阵 → 放进第一个空推荐位。</summary>
        public void OnRosterClicked(int beastIndex)
        {
            for (int cell = 0; cell < _deployed.Length; cell++)
            {
                if (_deployed[cell] == beastIndex) { _deployed[cell] = -1; CommitLayout(); return; }
            }
            foreach (var slot in Slots)
            {
                if (_deployed[slot] < 0) { _deployed[slot] = beastIndex; CommitLayout(); return; }
            }
            Debug.Log("[FormationPanel] 九宫格已满，先点击格子下阵一只。");
        }

        /// <summary>拖拽卡池项时临时关掉 ScrollRect，避免拖拽被滚动抢走。</summary>
        public void SetRosterScrollEnabled(bool on)
        {
            if (_scrollRoster != null) _scrollRoster.enabled = on;
        }

        // ---- 卡池"列表内=滚动 / 拖出=上阵"（见 RosterDragItem）----
        /// <summary>把拖拽转发给卡池 ScrollRect（列表内拖动 = 滚动）。</summary>
        public void RosterScrollInit(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnInitializePotentialDrag(e);
        }
        public void RosterScrollBegin(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnBeginDrag(e);
        }
        public void RosterScrollDrag(PointerEventData e)
        {
            if (_scrollRoster != null && _scrollRoster.enabled) _scrollRoster.OnDrag(e);
        }
        public void RosterScrollEnd(PointerEventData e)
        {
            if (_scrollRoster != null) _scrollRoster.OnEndDrag(e);
        }

        /// <summary>空手点击格子：有兽 → 下阵。</summary>
        public void OnCellClicked(int cell)
        {
            if (_selectedBeast >= 0) { PlaceSelectedIntoCell(cell); return; }
            OnCellClickedCore(cell);
        }

        private void OnCellClickedCore(int cell)
        {
            if (cell < 0 || cell >= _deployed.Length || _deployed[cell] < 0) return;
            _deployed[cell] = -1;
            // ★ 下阵时把详情面板收掉（用户 2026-10-15："保留最后一次悬浮的"是指**还在场上的**；
            //   这只已经下场了，再留着它的说明会误导）。
            //   ⚠ 这是 HideBeastDetail 唯一的调用点 —— 除了"下阵"，
            //     任何路径都不该清空详情（滑到空格/移出格子都保留）。
            HideBeastDetail();
            CommitLayout();
        }

        /// <summary>
        /// 拖拽开始。beastIndex / fromCell 二选一有值：从卡池拖 or 从格子拖。
        /// 从空格子拖起 = 什么都没有，直接取消。
        /// </summary>
        public void OnDragBegin(int beastIndex, int fromCell, PointerEventData e)
        {
            _dragActive = false;
            _dragBeast = beastIndex;
            _dragFromCell = fromCell;

            int showIndex = beastIndex >= 0 ? beastIndex : (fromCell >= 0 ? _deployed[fromCell] : -1);
            if (showIndex < 0) { _dragBeast = -1; _dragFromCell = -1; return; }

            ShowGhost(showIndex, e.position);
            _dragActive = true;
        }

        public void OnDragMove(PointerEventData e)
        {
            if (!_dragActive) return;
            MoveGhost(e.position);

            // 悬停高亮：拖到哪个格子，哪个格子亮起来
            int hover = FindCellAt(e.position);
            if (_imgCellHi != null)
            {
                bool on = hover >= 0;
                _imgCellHi.gameObject.SetActive(on);
                if (on) _imgCellHi.transform.position = _cells[hover].position;
            }
        }

        public void OnDragEnd(int beastIndex, int fromCell, PointerEventData e)
        {
            if (_imgCellHi != null) _imgCellHi.gameObject.SetActive(false);
            HideGhost();
            if (!_dragActive) { _dragBeast = -1; _dragFromCell = -1; return; }
            _dragActive = false;

            int target = FindCellAt(e.position);
            if (fromCell >= 0)
            {
                // 格子 → 格子：移动 / 交换
                if (target >= 0 && target != fromCell)
                {
                    int moved = _deployed[fromCell];
                    _deployed[fromCell] = _deployed[target];
                    _deployed[target] = moved;
                }
            }
            else if (beastIndex >= 0 && target >= 0)
            {
                // 卡池 → 格子：上阵。★ 拖拽路径此前**没有任何校验**（点选路径 OnRosterClicked 有），
                //   所以会出现"超过 5 只还能上""同一只可重复上"（用户实测）。这里补齐两道。
                for (int c = 0; c < _deployed.Length; c++)
                {
                    if (_deployed[c] == beastIndex)
                    {
                        Debug.Log("[FormationPanel] 该异兽已在阵中，不能重复上阵。");
                        _dragBeast = -1; _dragFromCell = -1;
                        CommitLayout();
                        return;
                    }
                }
                // ★ 落到**占用格** = 直接替换（换一只上去、原兽回卡池）—— 不算新增，满员也允许；
                //   只有落到**空格**时才受"最多 5 只"限制（用户 2026-10-12 报"不同异兽替换不了"）。
                bool occupied = _deployed[target] >= 0;
                int used = 0;
                for (int c = 0; c < _deployed.Length; c++) if (_deployed[c] >= 0) used++;
                if (!occupied && used >= MaxDeploy)
                {
                    Debug.Log("[FormationPanel] 最多上阵 " + MaxDeploy + " 只，请先下阵一只。");
                    _dragBeast = -1; _dragFromCell = -1;
                    CommitLayout();
                    return;
                }
                _deployed[target] = beastIndex;   // 空格=上阵；占用格=替换（原兽自动回卡池）
            }

            _dragBeast = -1;
            _dragFromCell = -1;
            CommitLayout();
        }

        /// <summary>
        /// 旧档继承：把存档里记录的队伍按 id 摆回九宫格。
        /// id 对不上目录的（版本变动/阵容重排）跳过；一只都没摆上 → 返回 false 走自动布阵。
        /// </summary>
        private bool TryInheritTeam()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.Team == null || run.Team.Count == 0) return false;

            for (int i = 0; i < _deployed.Length; i++) _deployed[i] = -1;
            int placed = 0;
            foreach (var id in run.Team)
            {
                int beastIndex = IndexOfBeast(id);
                if (beastIndex < 0) continue;
                int slot = NextEmptySlot();
                if (slot < 0) break;
                _deployed[slot] = beastIndex;
                placed++;
            }
            if (placed == 0) return false;
            CommitLayout();
            Debug.Log("[FormationPanel] 已继承存档队伍：" + placed + " 只。");
            return true;
        }

        /// <summary>出征前把当前队伍写回旅程（下次进来直接摆好）。</summary>
        private void SaveTeamToRun()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _all == null) return;
            run.Team.Clear();
            foreach (var idx in _deployed)
                if (idx >= 0 && idx < _all.Length) run.Team.Add(_all[idx].Id);
            WanXiang.Run.RunSave.SaveCurrent();
        }

        private int IndexOfBeast(string id)
        {
            if (_all == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _all.Length; i++)
                if (_all[i] != null && _all[i].Id == id) return i;
            return -1;
        }

        private int NextEmptySlot()
        {
            foreach (var slot in Slots)
                if (_deployed[slot] < 0) return slot;
            return -1;
        }

        private void CommitLayout()
        {
            RefreshCells();
            RefreshBonds();
            RefreshShapePreview();
        }

        /// <summary>屏幕坐标 → 九宫格格位（-1 = 不在任何格子上）。</summary>
        private int FindCellAt(Vector2 screenPos)
        {
            if (_cells == null) return -1;

            // ⛔ 2026-11-01 修：原来固定传 null（只对 Screen Space - Overlay 成立）。
            //   本面板若挂在 Screen Space - Camera 的 Canvas 下，null 相机 ⇒ 永远判不中 ⇒
            //   "拖到九宫格没反应"（用户实测）。这里按格子所在 Canvas 取相机。
            Camera cam = null;
            var anyCell = _cells.Length > 0 ? _cells[0] : null;
            if (anyCell != null)
            {
                var cv = anyCell.GetComponentInParent<Canvas>();
                if (cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = cv.worldCamera != null ? cv.worldCamera : Camera.main;
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(_cells[i], screenPos, cam))
                    return i;
            }
            return -1;
        }

        private void ShowGhost(int beastIndex, Vector2 screenPos)
        {
            if (_ghost == null)
            {
                _ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                var grt = (RectTransform)_ghost.transform;
                grt.SetParent(transform, false);
                grt.sizeDelta = new Vector2(150f, 150f);
                var cg = _ghost.GetComponent<CanvasGroup>();
                cg.blocksRaycasts = false;      // 幽灵不接事件，别挡住格子的判定
                cg.alpha = 0.85f;
                _ghostImg = _ghost.GetComponent<Image>();
                _ghostImg.raycastTarget = false;
                _ghostImg.preserveAspect = true;
                _ghost.transform.SetAsLastSibling();
            }
            _ghost.SetActive(true);

            var sprite = (_sprites != null && _all != null && beastIndex < _all.Length)
                ? _sprites.GetHead(_all[beastIndex].Id) : null;
            if (sprite != null)
            {
                _ghostImg.sprite = sprite;
                _ghostImg.color = Color.white;
            }
            else
            {
                _ghostImg.sprite = null;
                _ghostImg.color = new Color(0.78f, 0.83f, 0.72f, 0.9f);
            }
            MoveGhost(screenPos);
        }

        private void MoveGhost(Vector2 screenPos)
        {
            if (_ghost != null) ((RectTransform)_ghost.transform).position = screenPos;
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}
