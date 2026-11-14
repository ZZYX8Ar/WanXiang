// ============================================================================
//  万相 · 节点类型图标生成器（2026-11-14）
//  ---------------------------------------------------------------------------
//  用户：每个节点类型要有区别（战斗/精英/Boss/休整/市集/异闻/工坊/未知/秘境/险路/
//        命运所指/博戏/歇脚/遗物），现在全用同一张季节徽分不出来。
//  用 Unity 直接光栅化（4x 超采样 → 128px），风格对齐现有季节徽：墨绿双环 + 米白底 + 同色符号。
//  菜单：万相/UI/生成节点图标
// ============================================================================
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class NodeIconGen
    {
        private const int Out = 128;
        private const int SS = 4;                  // 超采样倍数
        private static readonly Color Ring = new Color32(86, 132, 76, 255);
        private static readonly Color Paper = new Color32(246, 242, 232, 255);
        private const string Dir = "Assets/Resources/UI/Campaign";

        [MenuItem("万相/UI/生成节点图标")]
        public static void Generate()
        {
            Directory.CreateDirectory(Dir);
            Make("Encounter", d => { Line(d, 38, 34, 90, 94, 7); Line(d, 90, 34, 38, 94, 7); Line(d, 30, 60, 52, 60, 6); Line(d, 76, 60, 98, 60, 6); });
            Make("Elite", d => { Line(d, 46, 34, 82, 96, 8); Line(d, 38, 62, 58, 62, 6); Star(d, 88, 40, 13); });
            Make("Boss", d => { Line(d, 38, 52, 38, 88, 7); Line(d, 90, 52, 90, 88, 7); Line(d, 38, 88, 90, 88, 7); Line(d, 38, 52, 52, 66, 6); Line(d, 52, 66, 64, 44, 6); Line(d, 64, 44, 76, 66, 6); Line(d, 76, 66, 90, 52, 6); Dot(d, 46, 78, 4); Dot(d, 64, 78, 4); Dot(d, 82, 78, 4); });
            Make("Nest", d => { Arc(d, 64, 84, 34, 30, 0, 180, 7); Arc(d, 64, 84, 26, 22, 0, 180, 5); Arc(d, 64, 66, 12, 14, 0, 360, 5); });
            Make("Shop", d => { Line(d, 50, 44, 78, 44, 6); Line(d, 46, 50, 82, 50, 6); Line(d, 46, 50, 34, 92, 6); Line(d, 82, 50, 94, 92, 6); Line(d, 34, 92, 94, 92, 6); Arc(d, 64, 74, 8, 8, 0, 360, 5); });
            Make("Tale", d => { Rect(d, 38, 40, 90, 92, 6); Line(d, 64, 40, 64, 92, 5); Line(d, 44, 56, 58, 56, 4); Line(d, 70, 56, 84, 56, 4); Line(d, 44, 70, 58, 70, 4); Line(d, 70, 70, 84, 70, 4); Line(d, 44, 84, 58, 84, 4); });
            Make("Forge", d => { Rect(d, 54, 34, 90, 50, 6); Line(d, 48, 50, 64, 68, 7); Rect(d, 32, 78, 96, 92, 6); });
            Make("Question", d => { Arc(d, 64, 50, 18, 16, 200, 20, 8); Line(d, 64, 66, 64, 76, 8); Dot(d, 64, 90, 5); });
            Make("Realm", d => { Spiral(d, 64, 64, 6, 40, 300, 6); });
            Make("Urgent", d => { Line(d, 64, 32, 82, 62, 6); Line(d, 82, 62, 74, 62, 6); Line(d, 74, 62, 88, 92, 6); Line(d, 88, 92, 40, 92, 6); Line(d, 40, 92, 54, 60, 6); Line(d, 54, 60, 46, 60, 6); Line(d, 46, 60, 64, 32, 6); });
            Make("Crossroad", d => { Line(d, 64, 92, 64, 68, 7); Line(d, 64, 68, 40, 40, 7); Line(d, 64, 68, 88, 40, 7); Tri(d, 40, 30, 7); Tri(d, 88, 30, 7); });
            Make("Gamble", d => { Rect(d, 40, 40, 88, 88, 6); Dot(d, 52, 52, 5); Dot(d, 64, 64, 5); Dot(d, 76, 76, 5); });
            Make("Respite", d => { Arc(d, 64, 78, 28, 22, 0, 180, 7); Line(d, 32, 96, 96, 96, 6); Arc(d, 58, 40, 6, 10, 90, 270, 4); Arc(d, 74, 40, 6, 10, 90, 270, 4); });
            Make("Relic", d => { Rect(d, 36, 54, 92, 92, 6); Arc(d, 64, 54, 28, 22, 180, 360, 6); Rect(d, 58, 64, 70, 78, 5); });
            AssetDatabase.Refresh();
            Debug.Log("[NodeIconGen] 节点图标已生成 14 张 → " + Dir);
        }

        private delegate void Painter(float[,] m);

        private static void Make(string kind, Painter p)
        {
            int n = Out * SS;
            var mask = new float[n, n];
            p(mask);
            var tex = new Texture2D(Out, Out, TextureFormat.RGBA32, false);
            var px = new Color[Out * Out];
            for (int y = 0; y < Out; y++)
                for (int x = 0; x < Out; x++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            a += Mathf.Clamp01(mask[x * SS + sx, y * SS + sy]);
                    a /= (SS * SS);
                    px[y * Out + x] = new Color(Ring.r, Ring.g, Ring.b, a);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(Dir, "node_kind_" + kind + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ---- 基础绘制（把形状写入 mask；坐标以 128 为基准，内部乘 SS）----
        private static void Plot(float[,] m, int cx, int cy, float w)
        {
            int n = m.GetLength(0);
            float half = w * 0.5f * SS;
            int r = Mathf.CeilToInt(half);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(half - dist + 0.5f);
                    if (a > m[x, y]) m[x, y] = a;
                }
        }

        private static void Line(float[,] m, float x0, float y0, float x1, float y1, float w)
        {
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * SS * 1.2f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Plot(m, Mathf.RoundToInt((x0 + (x1 - x0) * t) * SS), Mathf.RoundToInt((y0 + (y1 - y0) * t) * SS), w);
            }
        }

        private static void Dot(float[,] m, float cx, float cy, float r)
            => Arc(m, cx, cy, r, r, 0, 360, r * 2f);

        private static void Arc(float[,] m, float cx, float cy, float rx, float ry, float a0, float a1, float w)
        {
            int steps = Mathf.Max(24, Mathf.CeilToInt((Mathf.Abs(a1 - a0) + 10) * 1.6f));
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                Plot(m, Mathf.RoundToInt((cx + rx * Mathf.Cos(a)) * SS),
                         Mathf.RoundToInt((cy + ry * Mathf.Sin(a)) * SS), w);
            }
        }

        private static void Rect(float[,] m, float x0, float y0, float x1, float y1, float w)
        {
            Line(m, x0, y0, x1, y0, w); Line(m, x1, y0, x1, y1, w);
            Line(m, x1, y1, x0, y1, w); Line(m, x0, y1, x0, y0, w);
        }

        private static void Tri(float[,] m, float cx, float cy, float r)
        {
            Line(m, cx, cy - r, cx - r, cy + r * 0.7f, 5);
            Line(m, cx - r, cy + r * 0.7f, cx + r, cy + r * 0.7f, 5);
            Line(m, cx + r, cy + r * 0.7f, cx, cy - r, 5);
        }

        private static void Star(float[,] m, float cx, float cy, float r)
        {
            var pts = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = (-90 + i * 72) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a));
            }
            for (int i = 0; i < 5; i++) Line(m, pts[i].x, pts[i].y, pts[(i + 2) % 5].x, pts[(i + 2) % 5].y, 4);
        }

        private static void Spiral(float[,] m, float cx, float cy, float r0, float r1, float turnDeg, float w)
        {
            int steps = 160;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float a = t * turnDeg * Mathf.Deg2Rad;
                float r = Mathf.Lerp(r0, r1, t);
                Plot(m, Mathf.RoundToInt((cx + r * Mathf.Cos(a)) * SS),
                         Mathf.RoundToInt((cy + r * Mathf.Sin(a)) * SS), w);
            }
        }
    }
}
