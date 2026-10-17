// ============================================================================
//  UiInk —— 让"我在浅纸上想要什么颜色"能直接写成 sRGB（2026-10-17）
//  ---------------------------------------------------------------------------
//  本工程是 **Linear** 色彩空间（QualitySettings.activeColorSpace = Linear）。
//  UGUI 的 `Graphic.color` 在 Linear 下被当作**线性值**，显示时转 sRGB ⇒
//  **暗部被大幅提亮**：
//      · 0.165 线性 ≈ 0.44 sRGB  —— 你以为写了"深褐"，画出来是"中褐"
//      · 0.784 线性 ≈ 0.90 sRGB  —— 朱砂红被洗成"浅粉"
//  于是"设计稿上明明看得清"的颜色，实机全是发白的一层。
//
//  ⭐ 正确姿势：**按 sRGB 想、按 sRGB 写，进来过一次转换**。
//      var c = UiInk.Srgb(0.16f, 0.13f, 0.10f);   // 这就是屏幕上想要的深褐
//      tmp.color = c;
//
//  ⛔ 不要靠"把数值再调低一点"去试 —— 那是在和 gamma 曲线猜数字，
//     换一台机器/换色彩空间就又不对了。
//  ⛔ 纯黑 (`Color.black`) 与纯白不需要转换（0 和 1 在两种空间相同）。
// ============================================================================

using UnityEngine;

namespace WanXiang.Modules.UI
{
    internal static class UiInk
    {
        /// <summary>把"想要的 sRGB 颜色"转成当前色彩空间下该喂给 UGUI 的值。</summary>
        public static Color Srgb(float r, float g, float b, float a = 1f)
        {
            var c = new Color(r, g, b, a);
            // Color.linear 会把 sRGB 值转到线性空间；Gamma 空间下则原样使用。
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        /// <summary>同上，用于十六进制写法（如 "#8A3B30"）。</summary>
        public static Color Hex(string hex, float a = 1f)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.white;
            float R = System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
            float G = System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
            float B = System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            return Srgb(R, G, B, a);
        }
    }
}
