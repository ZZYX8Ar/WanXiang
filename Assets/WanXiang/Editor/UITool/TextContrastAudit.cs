// 万相 · UI 文字对比度审计（2026-11-16 用户要求：先看底图颜色再定文字颜色）
// 菜单：万相/UI/审计·文字对比度（只读，不改任何 prefab）
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class TextContrastAudit
    {
        [MenuItem("万相/UI/审计·文字对比度")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== UI 文字/底图对比度审计 ===");
            sb.AppendLine("判据：浅底(相对亮度>0.6) 配深字(亮度<0.45)，或反之一；");
            sb.AppendLine("      同向且差值<0.30 判为「疑似看不清」。");
            sb.AppendLine();

            int total = 0, suspect = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/UI" });
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                // 每个 TMP 找它最近的有 Image 的祖先当"底"
                var texts = go.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    var tx = texts[i];
                    if (tx == null) continue;
                    Image bg = null;
                    var p = tx.transform.parent;
                    while (p != null && bg == null) { bg = p.GetComponent<Image>(); p = p.parent; }
                    if (bg == null) continue;
                    total++;

                    Color ink = tx.color;
                    Color paper = bg.color;
                    // 底图 sprite 是浅色 kit 时，即使 color 白也按浅底算（这里用 color 亮度近似）
                    float li = Lum(ink), lp = Lum(paper);
                    if (ink.a > 0.05f && paper.a > 0.05f && Mathf.Abs(li - lp) < 0.30f)
                    {
                        suspect++;
                        sb.AppendLine("⚠ " + path + " :: " + PathOf(tx.transform, go.transform));
                        sb.AppendLine("    字 " + Hex(ink) + " 亮" + li.ToString("0.00")
                                    + " / 底 " + Hex(paper) + "(sprite:"
                                    + (bg.sprite != null ? bg.sprite.name : "null") + ") 亮" + lp.ToString("0.00"));
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("检查 " + total + " 处文字，疑似看不清 " + suspect + " 处。");
            Debug.Log(sb.ToString());
        }

        private static float Lum(Color c)
            => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static string Hex(Color c)
            => string.Format("#{0:X2}{1:X2}{2:X2}", (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255));

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }
    }
}
