// ============================================================================
//  WanXiang · UI 框架 · 面板动效
//  ---------------------------------------------------------------------------
//  面板级别的通用动效。刻意只改 CanvasGroup 与 RectTransform 这两个"表现层"
//  属性，不碰任何业务状态 —— 这样动效中途被打断（面板被上层盖住、玩家狂点关闭）
//  时，不会留下半截脏数据。这是 UI 动效与"动画系统"的职责分界：
//    动效负责"看起来舒服"，状态由数据层决定，两者不要互相污染。
//
//  ⚠ 所有动效都必须经 UIPanelBase.TrackTween 登记：
//      TrackTween(UIPanelAnimations.PopIn(CanvasGroup, Rect));
//    漏了这一步，面板关闭后动效仍会继续跑，下次打开时带着残留状态出现。
//
//  为什么用 DOTween 而不是自己写插值：
//    缓动曲线（Ease）是动效手感的核心。手写的 Mathf.Lerp 基本都是线性的，
//    界面会有明显"机械感"；而 OutBack / OutQuint 这类带回弹与减速的曲线，
//    手写成本高且容易做过头。DOTween 提供 30 余种缓动，这是它最大的价值。
// ============================================================================

using DG.Tweening;
using UnityEngine;

namespace WanXiang.Framework.UI
{
    /// <summary>
    /// 面板常用动效。返回值交给 <c>UIPanelBase.TrackTween</c> 登记，
    /// 由框架在面板关闭时统一回收。
    /// </summary>
    public static class UIPanelAnimations
    {
        /// <summary>
        /// 默认动效时长（秒）。
        /// 0.18–0.24s 是 UI 动效的舒适区：短于此感觉"闪一下"，长于此显得拖沓。
        /// 弹窗的退场可以更短（0.15s 左右），因为玩家已经决定要关它了。
        /// </summary>
        public const float DefaultDuration = 0.2f;

        /// <summary>
        /// 淡入。最基础、最安全的入场动效，适合绝大多数面板。
        /// <code>TrackTween(UIPanelAnimations.FadeIn(CanvasGroup));</code>
        /// </summary>
        public static Tween FadeIn(CanvasGroup group, float duration = DefaultDuration)
        {
            if (group == null) return null;
            group.alpha = 0f;
            return group.DOFade(1f, duration).SetEase(Ease.OutQuad);
        }

        /// <summary>
        /// 退场淡出。注意：本框架的关闭是同步的（<c>InternalClose</c> 立即 SetActive(false)），
        /// 所以这个动效只在"关闭前主动调用"时可见，例如：
        /// <code>TrackTween(UIPanelAnimations.FadeOut(CanvasGroup)); await UniTask.Delay(160); CloseSelf();</code>
        /// </summary>
        public static Tween FadeOut(CanvasGroup group, float duration = 0.16f)
        {
            if (group == null) return null;
            return group.DOFade(0f, duration).SetEase(Ease.InQuad);
        }

        /// <summary>
        /// 弹入：淡入 + 从 0.92 倍缩放回原大小，带轻微回弹。
        /// 适合弹窗、奖励弹窗这类需要"抓一下注意力"的场景。
        ///
        /// 起始缩放取 0.92 而不是 0：从 0 缩放回来会有一瞬间"从无到有"的突兀感，
        /// 且在缩放极小时描边与文字会糊成一团。
        /// </summary>
        public static Sequence PopIn(
            CanvasGroup group,
            RectTransform rect,
            float duration = 0.26f)
        {
            if (group == null || rect == null) return null;

            group.alpha = 0f;
            rect.localScale = Vector3.one * 0.92f;

            var seq = DOTween.Sequence();
            seq.Append(group.DOFade(1f, duration * 0.6f));          // 透明度稍快，视觉上更"利落"
            seq.Join(rect.DOScale(Vector3.one, duration).SetEase(Ease.OutBack));
            return seq;
        }

        /// <summary>
        /// 滑入：从指定屏幕方向偏移处滑到目标位，可叠加淡入。
        /// 适合侧边面板（背包、图鉴、任务列表）。
        /// </summary>
        /// <param name="offset">起始偏移（以锚点坐标计）。例如从右侧滑入传 <c>new Vector2(400f, 0f)</c>。</param>
        public static Sequence SlideIn(
            RectTransform rect,
            Vector2 offset,
            float duration = 0.24f,
            CanvasGroup group = null)
        {
            if (rect == null) return null;

            Vector2 target = rect.anchoredPosition;
            rect.anchoredPosition = target + offset;

            var seq = DOTween.Sequence();
            seq.Append(rect.DOAnchorPos(target, duration).SetEase(Ease.OutQuint));
            if (group != null)
            {
                group.alpha = 0f;
                seq.Join(group.DOFade(1f, duration * 0.8f));
            }
            return seq;
        }
    }
}
