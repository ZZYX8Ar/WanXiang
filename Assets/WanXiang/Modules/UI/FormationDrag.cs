// ============================================================================
//  万相 · 布阵拖拽代理
//  ---------------------------------------------------------------------------
//  两个小组件，只做"事件翻译"，逻辑全在 FormationPanel 里（布阵数据在那边）：
//    RosterDragItem —— 卡池条目：点击 = 上阵到推荐空位；拖拽 = 拖到格子上阵
//    CellDragItem   —— 九宫格格子：点击 = 下阵；拖拽 = 移动 / 交换
//
//  为什么不用 IDropHandler：掉落目标要靠射线找，而 EventSystem 的射线在
//  "拖拽幽灵挡在指针下面"时容易自咬（幽灵自己接住了 Drop）。
//  我们改用"松手时用坐标圈出命中的格子"，直接、可控、不依赖射线顺序。
// ============================================================================

using UnityEngine;
using UnityEngine.EventSystems;

namespace WanXiang.Modules.UI
{
    public sealed class RosterDragItem : MonoBehaviour,
        IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FormationPanel Owner;
        public int BeastIndex = -1;

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.RosterScrollInit(eventData);   // 清滚动惯性
        }

        /// <summary>
        /// ★ 上阵 = **双击**（用户 2026-10-12 定案：拖动一律用于滚动，否则"滑列表时会误上阵"）。
        ///   单击不动作；下阵走"单击九宫格格子"。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner == null || BeastIndex < 0) return;
            // ★ 上阵 = **双击**（用户 2026-10-12 定案：拖动一律用于滚动，否则"滑列表时会误上阵"）。
            //   ★ 单击 = 看战斗详情（用户 2026-10-14：卡池单击无既有语义，用来驱动中上详情区）。
            if (eventData.clickCount < 2)
            {
                Owner.ShowBeastDetailByIndex(BeastIndex);
                return;
            }
            Owner.OnRosterClicked(BeastIndex);
        }

        // 拖动 = 滚动列表（把拖拽事件转发给卡池 ScrollRect）。
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.RosterScrollBegin(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.RosterScrollDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.RosterScrollEnd(eventData);
        }
    }

    public sealed class CellDragItem : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FormationPanel Owner;
        public int CellIndex = -1;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null && CellIndex >= 0) Owner.OnCellClicked(CellIndex);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Owner != null && CellIndex >= 0) Owner.OnDragBegin(-1, CellIndex, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.OnDragMove(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Owner != null && CellIndex >= 0) Owner.OnDragEnd(-1, CellIndex, eventData);
        }
    }
}
