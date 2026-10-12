// ============================================================================
//  万相 · 悬浮提示面板跟随鼠标
//  ---------------------------------------------------------------------------
//  2026-10-11 用户："这个悬浮面板能不能跟着鼠标移动啊，而不是一直固定，
//  这样就不会闪烁了"
//
//  ⛔ 闪烁的真正根因（两层，都要治）：
//    ① 提示面板是 UGUI Image，`raycastTarget` **默认 true** ， 鼠标一移到面板上
//       就触发来源控件的 PointerExit → 面板隐藏 → 鼠标又落回来源 → PointerEnter
//       → 面板显示 …… 无限循环 = 闪烁。
//    ② 面板固定位置且紧贴来源控件 ， 更容易被鼠标"踩"到。
//  ✅ 治法：① 面板及子节点全部 `raycastTarget=false`；② 面板跟随鼠标 + 位移偏移。
//
//  ⛔⛔ 第一版跟随"看不见"的坑（务必记住）：
//     面板的 anchor 是 **(0,1) 左上角**（pivot 也是 (0,1)），而
//     `ScreenPointToLocalPointInRectangle` 返回的是**相对父节点中心**的坐标。
//     我直接把后者赋给 `anchoredPosition` —— 但 anchoredPosition 是相对 **anchor**
//     的偏移 ， 基准差了一个"父节点半尺寸"，鼠标在左半边时面板被算到屏幕外。
//     ✅ 正确做法：先把 anchoredPosition 的基准**统一成父节点中心**
//        （把 anchorMin/Max 设为 0.5,0.5），再赋 local 坐标；或直接用 position。
//        本组件采用前者（同时兼顾 pivot 带来的展开方向），并做屏幕边缘钳制。
// ============================================================================

using UnityEngine;

namespace WanXiang.Modules.UI
{
    /// <summary>让提示面板跟随鼠标（锚点无关 + 边缘钳制）。挂到提示面板根节点上。</summary>
    [DisallowMultipleComponent]
    public sealed class TipFollowMouse : MonoBehaviour
    {
        [SerializeField] private Vector2 _offset = new Vector2(28f, -28f);  // 相对鼠标的偏移
        [SerializeField] private float _edgePad = 14f;                       // 距屏幕边缘留白

        private RectTransform _rt;
        private RectTransform _parentRt;
        private Canvas _canvas;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
            _parentRt = _rt != null ? _rt.parent as RectTransform : null;
            // ⛔ 统一 anchoredPosition 的基准为"父节点中心"（否则基准是原 anchor，会算错）
            if (_rt != null)
            {
                _rt.anchorMin = new Vector2(0.5f, 0.5f);
                _rt.anchorMax = new Vector2(0.5f, 0.5f);
            }
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

            // 边缘钳制：pivot 决定面板往哪个方向展开
            Vector2 size = _rt.rect.size;
            Vector2 half = _parentRt.rect.size * 0.5f;
            Vector2 pv = _rt.pivot;
            float minX = -half.x + size.x * pv.x + _edgePad;
            float maxX = half.x - size.x * (1f - pv.x) - _edgePad;
            float minY = -half.y + size.y * pv.y + _edgePad;
            float maxY = half.y - size.y * (1f - pv.y) - _edgePad;
            if (maxX > minX) pos.x = Mathf.Clamp(pos.x, minX, maxX);
            if (maxY > minY) pos.y = Mathf.Clamp(pos.y, minY, maxY);

            _rt.anchoredPosition = pos;
        }
    }
}