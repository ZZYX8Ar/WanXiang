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
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FormationPanel Owner;
        public int BeastIndex = -1;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null && BeastIndex >= 0) Owner.OnRosterClicked(BeastIndex);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Owner == null || BeastIndex < 0) return;
            // ★ 关键：卡池在 ScrollRect 里，拖拽事件会同时被 ScrollRect 消费 ⇒ 拖不动。
            //   开始拖时先让 ScrollRect 让位，拖完再恢复（用户实测"右边异兽不能拖动"）。
            Owner.SetRosterScrollEnabled(false);
            Owner.OnDragBegin(BeastIndex, -1, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.OnDragMove(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Owner != null && BeastIndex >= 0) Owner.OnDragEnd(BeastIndex, -1, eventData);
            if (Owner != null) Owner.SetRosterScrollEnabled(true);     // 恢复滚动
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
