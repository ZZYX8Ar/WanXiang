// ============================================================================
//  WanXiang · UI 框架 · 遮罩
//  ---------------------------------------------------------------------------
//  遮罩要同时干三件事，缺一件都会出问题：
//    1. 视觉：把下层压暗，让玩家知道"现在该看这一层"
//    2. 射线拦截：阻止玩家点到被遮住的下层按钮
//    3. 点击关闭：半屏弹窗点空白处关闭（全屏面板通常不需要）
//
//  第 2 点是新手最容易漏的。常见错误是给面板挂个 CanvasGroup 就以为拦住了 ——
//  但 CanvasGroup 只覆盖面板自身矩形，玩家点面板外的区域照样能点到下层按钮。
//  这就是"打开确认框时点空白处，结果触发了背景里的抽卡按钮"这类事故的成因。
//
//  关于 alpha = 0 仍然拦射线：Unity 中 Graphic 的射线检测只看 raycastTarget 与
//  几何体，不看 alpha（除非显式设置 Image.alphaHitTestMinimumThreshold）。
//  所以全屏面板可以用"完全透明的遮罩"拦住下层点击，而不会把画面压黑。
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanXiang.Framework.UI
{
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public sealed class UIPanelMask : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>透明度变化速度（alpha / 秒）。0.6 → 0 大约需要 0.1 秒。</summary>
        private const float FadeSpeed = 6f;

        private Image _image;
        private Action _onClick;
        private CancellationToken _destroyToken;

        private float _targetAlpha;
        private bool _raycast;
        private bool _animating;

        /// <summary>遮罩当前的目标透明度（动画结束后即为实际值）。</summary>
        public float TargetAlpha => _targetAlpha;

        private void Awake()
        {
            EnsureInit();

            // ⚠ 这里必须是 false，不能是 true。
            //
            // UISystem.CreateLayer 建遮罩时已经写过 `raycastTarget = false`，
            // 但紧接着 AddComponent<UIPanelMask>() 会触发本 Awake，把它盖成 true。
            // 而 UpdateLayerMask 只在"该层开/关面板之后"才被调用 ——
            // 于是一个从没开过面板的覆盖层（Normal / Popup / Overlay）会长期挂着一张
            // **隐形的全屏遮罩**，而它们的 Canvas 排序又在 Main 之上：
            // 结果是主界面所有点击都被吃掉，症状是"按钮点了一点反应都没有"。
            //
            // 正确状态：默认不拦射线（该层没面板时让点击穿过去），
            // 需要拦时由 SetTargetAlpha / SetAlphaImmediate 打开。
            _image.raycastTarget = false;
        }

        /// <summary>
        /// 惰性取得组件引用。
        ///
        /// 之所以不能只依赖 Awake：在编辑器非播放状态下 AddComponent 不会调用 Awake，
        /// 而我们的 UISystem 是纯 C# 类（不是 MonoBehaviour），有可能在编辑器工具、
        /// 导表预览、单元测试里被驱动。少了这一层，那些场景会直接空引用。
        /// </summary>
        private void EnsureInit()
        {
            if (_image != null) return;

            _image = GetComponent<Image>();
            if (_image == null)
            {
                // 遮罩必须是 Image（不能用 RawImage 之外的其它 Graphic 替代，
                // 因为我们需要它的 raycastTarget 来拦截射线）
                _image = gameObject.AddComponent<Image>();
            }

            _destroyToken = this.GetCancellationTokenOnDestroy();
        }

        /// <summary>
        /// 设置遮罩状态。框架每次开关面板后调用一次，不需要业务关心。
        /// </summary>
        /// <param name="alpha">目标透明度。0 = 视觉上完全透明但仍可拦截射线。</param>
        /// <param name="raycast">是否拦截射线。该层没有面板时必须为 false，否则会挡住下层正常操作。</param>
        /// <param name="onClick">点击遮罩的回调。null 表示点击不关闭任何东西。</param>
        public void SetTargetAlpha(float alpha, bool raycast, Action onClick)
        {
            EnsureInit();

            _targetAlpha = Mathf.Clamp01(alpha);
            _raycast = raycast;
            _onClick = onClick;

            // 遮罩 GameObject 常驻（避免频繁 SetActive 造成 Canvas 重建），
            // 只切换 raycastTarget 与颜色。
            _image.raycastTarget = raycast;

            if (!_animating)
            {
                AnimateAsync().Forget();
            }
        }

        /// <summary>立刻跳过动画设置透明度（用于场景切换等需要瞬时生效的时机）。</summary>
        public void SetAlphaImmediate(float alpha, bool raycast, Action onClick = null)
        {
            EnsureInit();

            _targetAlpha = Mathf.Clamp01(alpha);
            _raycast = raycast;
            _onClick = onClick;
            _image.raycastTarget = raycast;
            ApplyAlpha(_targetAlpha);
        }

        private async UniTaskVoid AnimateAsync()
        {
            _animating = true;
            try
            {
                while (true)
                {
                    float current = _image.color.a;
                    if (Mathf.Abs(current - _targetAlpha) <= 0.002f) break;

                    float next = Mathf.MoveTowards(
                        current, _targetAlpha, FadeSpeed * Time.unscaledDeltaTime);
                    ApplyAlpha(next);

                    // unscaledDeltaTime + Update 时序：即使游戏被 Time.timeScale = 0 暂停
                    // （暂停菜单、加载中），遮罩淡入淡出依然正常工作。
                    await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
                }
                ApplyAlpha(_targetAlpha);
            }
            catch (OperationCanceledException)
            {
                // 对象销毁导致的取消，正常路径
            }
            finally
            {
                _animating = false;
            }
        }

        private void ApplyAlpha(float a)
        {
            var c = _image.color;
            c.a = a;
            _image.color = c;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_raycast) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _onClick?.Invoke();
        }
    }
}
