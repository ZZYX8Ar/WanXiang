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

        /// <summary>本次拖动是否已切换成"拖到棋盘上阵"（false = 还在列表内滚动）。</summary>
        private bool _deploying;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Owner != null && BeastIndex >= 0) Owner.OnRosterClicked(BeastIndex);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Owner == null || BeastIndex < 0) return;
            // ★ 卡池条目既要能拖到棋盘、又要能滑动列表 —— 用一个规则分开：
            //   指针**在卡池区域内**拖动 = 滚动列表；**拖出卡池区域**（拖向左侧九宫格）= 上阵。
            //   （此前是一拖就 SetRosterScrollEnabled(false) + 上阵 ⇒ 列表永远滑不动，用户 2026-10-12 报。）
            _deploying = false;
            Owner.RosterScrollBegin(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner == null) return;
            if (!_deploying)
            {
                if (Owner.IsPointerOutsideRoster(eventData.position))
                {
                    // 拖出卡池 ⇒ 切换为上阵：先结束滚动，再开幽灵
                    Owner.RosterScrollEnd(eventData);
                    Owner.SetRosterScrollEnabled(false);
                    Owner.OnDragBegin(BeastIndex, -1, eventData);
                    _deploying = true;
                }
                else
                {
                    Owner.RosterScrollDrag(eventData);   // 列表内拖动 = 滚动
                }
            }
            if (_deploying) Owner.OnDragMove(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Owner == null) return;
            if (_deploying)
            {
                Owner.OnDragEnd(BeastIndex, -1, eventData);
                Owner.SetRosterScrollEnabled(true);      // 恢复滚动
                _deploying = false;
            }
            else
            {
                Owner.RosterScrollEnd(eventData);
            }
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
