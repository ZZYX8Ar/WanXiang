// ============================================================================
//  万相 · 战斗表现层 · 绘制原语
//  ---------------------------------------------------------------------------
//  灰盒阶段**没有任何美术资源**，所以"怎么画"这件事只能是程序化的：
//  几个矩形 + 几段线 + 几行字。这个类把那些矩形/线段/文字的写法收在一处，
//  让编辑器灰盒窗口与运行时棋盘视图画出**同一套东西**。
//
//  ⚠ 为什么可以共用：两边都是 IMGUI（EditorWindow.OnGUI 与 MonoBehaviour.OnGUI
//    用的是同一套 GUI.DrawTexture / GUI.Label）。所以只要不碰 EditorGUI.*，
//    同一份绘制代码在两处都能跑。这个约束是有意的 —— 一旦某处用了 EditorGUI，
//    就会出现"编辑器里看着对、游戏里是另一个样"，而这类偏差最难自查。
//
//  ⚠ 关于"为什么不用 Sprite / Canvas / 预制体"：灰盒的唯一目的是"看 20 遍
//    判断好不好看"。做成预制体意味着先欠下 Prefab/图集/分辨率适配三笔债，
//    等美术定了方向全部要重做。IMGUI 画方块的试错成本是零，改一行就重画。
//    **正式表现层会用预制体 —— 那时这个类只保留色板与线段判据。**
// ============================================================================

using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public static class BattleHud
    {
        // ================================================================
        //  尺寸
        // ================================================================

        /// <summary>
        /// 当前字体缩放。编辑器窗口设为 1，运行时按分辨率算。
        /// 它只影响字号，几何尺寸由调用方按自己的布局给 —— 两者混在一起做，
        /// 就会出现"字变大了格子没变大"这种只有特定分辨率才暴露的问题。
        /// </summary>
        public static float Scale { get; private set; } = 1f;

        public static GUIStyle Title { get; private set; }   // 小标题
        public static GUIStyle Name { get; private set; }    // 单位名
        public static GUIStyle Body { get; private set; }    // 正文
        public static GUIStyle Small { get; private set; }   // 次级文字（淡墨）

        /// <summary>
        /// 在 OnGUI 开头调一次。**必须在 OnGUI 里调** —— GUIStyle 要从 GUI.skin 派生，
        /// 而 GUI.skin 只在 GUI 事件中有效。缩放变了会重建一次样式。
        /// </summary>
        public static void Begin(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (Title != null && Mathf.Abs(scale - Scale) < 0.001f) return;

            Scale = scale;
            int F(float baseSize) => Mathf.Max(7, Mathf.RoundToInt(baseSize * scale));

            // GUI.skin 在 OnGUI 之外是 null；那时退化成裸 GUIStyle（字号仍生效，
            // 只是不继承皮肤的对齐/内边距）。真发生说明有人没在 OnGUI 里调 Begin。
            var baseStyle = GUI.skin != null ? GUI.skin.label : null;
            GUIStyle Like() => baseStyle != null ? new GUIStyle(baseStyle) : new GUIStyle();

            Title = Like();
            Title.fontSize = F(12);
            Title.fontStyle = FontStyle.Bold;
            Title.normal.textColor = BattlePalette.Ink;

            Name = Like();
            Name.fontSize = F(11);
            Name.normal.textColor = BattlePalette.Ink;

            Body = Like();
            Body.fontSize = F(10);
            Body.wordWrap = true;
            Body.normal.textColor = BattlePalette.Ink;

            Small = Like();
            Small.fontSize = F(10);
            Small.wordWrap = true;
            Small.normal.textColor = BattlePalette.InkSoft;
        }

        // ================================================================
        //  矩形与线段
        // ================================================================

        /// <summary>填一个纯色矩形。用内置的 1×1 白纹理加 GUI.color 染色，
        /// 不自己 new Texture2D —— 内置的那张不会泄漏，也不会在域重载时变野指针。</summary>
        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>描边（四条边各一个矩形）。</summary>
        public static void Border(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>
        /// 画一段线。九宫格的正交相邻只可能是水平或竖直的，所以不需要
        /// Handles / GL —— 几段矩形就够，还避开了"在非重绘时机调 Handles 会报错"的坑。
        /// dashed 用短段拼出来（相克的"断续感"就是从这里来的）。
        /// </summary>
        public static void Segment(Vector2 a, Vector2 b, Color color, float thickness, bool dashed)
        {
            const float dash = 7f, gap = 5f;
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len <= 0.01f) return;

            float ux = dx / len, uy = dy / len;
            float t = 0f;
            int guard = 0;                       // 防呆：参数写错时不至于画出几万段
            while (t < len && guard++ < 128)
            {
                float seg = dashed ? Mathf.Min(dash, len - t) : len - t;
                float x0 = a.x + ux * t, y0 = a.y + uy * t;
                float x1 = a.x + ux * (t + seg), y1 = a.y + uy * (t + seg);

                Fill(new Rect(Mathf.Min(x0, x1) - thickness * 0.5f,
                              Mathf.Min(y0, y1) - thickness * 0.5f,
                              Mathf.Abs(x1 - x0) + thickness,
                              Mathf.Abs(y1 - y0) + thickness), color);

                if (!dashed) break;
                t += dash + gap;
            }
        }

        // ================================================================
        //  九宫格坐标
        // ================================================================

        /// <summary>九宫格 index → 该格矩形。</summary>
        public static Rect CellRect(Rect area, int index, float cellW, float cellH, float gap)
        {
            int r = index / BoardLayout.Columns, c = index % BoardLayout.Columns;
            return new Rect(area.x + c * (cellW + gap), area.y + r * (cellH + gap), cellW, cellH);
        }

        /// <summary>九宫格 index → 该格中心（画连线用）。</summary>
        public static Vector2 CellCentre(Rect area, int index, float cellW, float cellH, float gap)
        {
            int r = index / BoardLayout.Columns, c = index % BoardLayout.Columns;
            return new Vector2(area.x + c * (cellW + gap) + cellW * 0.5f,
                               area.y + r * (cellH + gap) + cellH * 0.5f);
        }
    }
}
