// ============================================================================
//  万相 · 灵感树面板构建（B5-4，Editor/UITool）
//  ---------------------------------------------------------------------------
//  Panel_Inspiration：三列（行旅/战阵/炉火）× 5 层 + 顶部悟道点余额 + 关闭。
//  节点按钮由 InspirationPanel.OnOpenAsync 动态取（结构层只建 15 个按钮槽）。
//  全量重建（S1 同模式，面板未交付玩家手改前可随时重跑）。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class InspirationPanelBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Inspiration.prefab";
        private const string KitDir = "Assets/ArtRes/UI/Kit/";

        private static readonly Color Paper = new Color(0.93f, 0.90f, 0.84f, 0.98f);
        private static readonly Color Ink = Color.black;

        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/ArtRes/UI/Fonts/SourceHanSerifCN-Bold SDF.asset");
            if (font == null) font = TMP_Settings.defaultFontAsset;

            var root = new GameObject("Panel_Inspiration", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(1080f, 1920f);
            AddImage(root, "panel_bg", Paper, Image.Type.Sliced);
            root.AddComponent<WanXiang.Modules.UI.InspirationPanel>();   // 面板组件（自检核对根组件）

            // 顶栏：标题 + 悟道点余额 + 关闭
            var top = Node("Root_TopBar", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 120f));
            AddImage(top, "panel_strip", new Color(1f, 1f, 1f, 0.55f), Image.Type.Sliced);
            Text("Tmp_Title", top, font, "灵感树", 34, TextAlignmentOptions.Left, Ink);
            Text("Tmp_Points", top, font, "悟道点 0", 28, TextAlignmentOptions.Center, Ink);
            var close = Button("Btn_Close", top, font, "×", 34);
            ((RectTransform)close.transform).sizeDelta = new Vector2(64f, 64f);

            // 三列 × 5 层：branch 列容器（InspirationPanel 在运行时按 tier 填按钮文本/状态）
            string[] branches = { "Branch_travel", "Branch_battle", "Branch_forge" };
            string[] titles = { "行旅", "战阵", "炉火" };
            for (int b = 0; b < 3; b++)
            {
                var col = Node(branches[b], rootRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(20f + b * 350f, -60f), new Vector2(340f, 60f));
                Text("Tmp_BranchName", col.gameObject, font, titles[b], 30, TextAlignmentOptions.Center, Ink);

                var grid = Node("Grid", (RectTransform)col.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(0f, -40f), new Vector2(0f, -40f));
                for (int t = 1; t <= 5; t++)
                {
                    var btn = Button("Btn_T" + t, grid, font, "T" + t, 20);
                    var rt = (RectTransform)btn.transform;
                    rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -60f - (t - 1) * 120f);
                    rt.sizeDelta = new Vector2(-16f, 104f);
                    btn.GetComponent<Image>().sprite = LoadSprite("btn_secondary");
                    // 名称与消耗两行文本（InspirationPanel 刷新）
                    Text("Tmp_Name", btn, font, "——", 22, TextAlignmentOptions.Center, Ink);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            int nodes = CountNodes(PrefabPath);
            Debug.Log("[InspirationPanel] 已生成 " + PrefabPath + "（节点 " + nodes + " 个）");
            return PrefabPath + " nodes=" + nodes;
        }

        private static GameObject AddImage(GameObject go, string spriteName, Color color, Image.Type type)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(KitDir + spriteName + ".png");
            if (sp != null) { img.sprite = sp; img.type = type; }
            img.color = color;
            return go;
        }

        private static GameObject Node(string name, RectTransform parent,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
            return go;
        }

        private static TMP_Text Text(string name, GameObject parent, TMP_FontAsset font,
            string content, float size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.text = content; t.fontSize = size;
            t.alignment = align; t.color = color; t.raycastTarget = false;
            return t;
        }

        private static GameObject Button(string name, GameObject parent, TMP_FontAsset font,
            string label, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent.transform, false);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
            go.AddComponent<Button>();
            Text("Tmp_Label", go, font, label, size, TextAlignmentOptions.Center, Ink);
            return go;
        }

        private static Sprite LoadSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ArtRes/UI/Kit/" + name + ".png");

        private static int CountNodes(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return root ? root.GetComponentsInChildren<Transform>(true).Length : 0;
        }
    }
}
