// ============================================================================
//  万相 · 悬浮提示面板跟随鼠标
//  ---------------------------------------------------------------------------
//  2026-10-11 用户："这个悬浮面板能不能跟着鼠标移动啊，而不是一直固定，
//  这样就不会闪烁了"
//
//  ⛔ 闪烁的真正根因（两层，都要治）：
//    ① 提示面板是 UGUI Image，`raycastTarget` **默认 true** ⇒ 鼠标一移到面板上
//       就触发来源控件的 PointerExit → 面板隐藏 → 鼠标又落回来源 → PointerEnter
//       → 面板显示 …… 无限循环 = 闪烁。
//    ② 面板固定位置、且紧贴来源控件 ⇒ 更容易被鼠标"踩"到。
//  ✅ 治法：① 面板及其子节点**全部 raycastTarget=false**（鼠标事件穿透）；
//          ② 面板跟随鼠标并**偏移一段距离**，永远不与来源控件/鼠标重叠。
// ============================================================================

using UnityEngine;

namespace WanXiang.Modules.UI
{
    /// <summary>让提示面板跟随鼠标（带偏移与屏幕边缘钳制）。挂到提示面板根节点上。</summary>
    [DisallowMultipleComponent]
    public sealed class TipFollowMouse : MonoBehaviour
    {
        [SerializeField] private Vector2 _offset = new Vector2(26f, -26f);   // 相对鼠标的偏移
        [SerializeField] private float _edgePad = 12f;                        // 距屏幕边缘留白

        private RectTransform _rt;
        private RectTransform _parentRt;
        private Canvas _canvas;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            _parentRt = _rt != null ? _rt.parent as RectTransform : null;
        }

        private void OnEnable() { Apply(); }

        private void LateUpdate() { Apply(); }

        private void Apply()
        {
            if (_rt == null) return;
            if (_parentRt == null && _rt.parent != null) _parentRt = _rt.parent as RectTransform;
            if (_parentRt == null) return;

            Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _canvas.worldCamera : null;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRt, Input.mousePosition, cam, out local))
                return;

            Vector2 pos = local + _offset;

            // 边缘钳制：别让面板跑出父容器
            Vector2 size = _rt.rect.size;
            Vector2 half = _parentRt.rect.size * 0.5f;
            Vector2 pivot = _rt.pivot;
            // 面板左下/右上相对父容器中心的范围
            float minX = -half.x + size.x * pivot.x + _edgePad;
            float maxX = half.x - size.x * (1f - pivot.x) - _edgePad;
            float minY = -half.y + size.y * pivot.y + _edgePad;
            float maxY = half.y - size.y * (1f - pivot.y) - _edgePad;
            if (maxX > minX) pos.x = Mathf.Clamp(pos.x, minX, maxX);
            if (maxY > minY) pos.y = Mathf.Clamp(pos.y, minY, maxY);

            _rt.anchoredPosition = pos;
        }
    }
}