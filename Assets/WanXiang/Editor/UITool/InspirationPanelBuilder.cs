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
        private const string KitDir = "Assets/Resources/UI/Kit/";   // 真实 kit 目录（首版指错→LoadSprite 全 null→纯色）

        private static readonly Color Paper = new Color(0.93f, 0.90f, 0.84f, 0.98f);
        private static readonly Color Ink = Color.black;

        [UnityEditor.MenuItem("万相/UI/重建灵感树面板", priority = 0)]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/ArtRes/UI/Fonts/SourceHanSerifCN-Bold SDF.asset");
            if (font == null) font = TMP_Settings.defaultFontAsset;

            var root = new GameObject("Panel_Inspiration", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(1920f, 1080f);   // 横屏（竖屏尺寸是首版排版事故根因）
            AddImage(root, "panel_dialog", Color.white, Image.Type.Sliced);
            root.AddComponent<WanXiang.Modules.UI.InspirationPanel>();   // 面板组件（自检核对根组件）

            // 顶栏：标题 + 悟道点余额 + 关闭
            var top = Node("Root_TopBar", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -120f), new Vector2(0f, 0f));   // 顶栏在顶部**下方** 120（首版写反→跑到屏外）
            AddImage(top, "panel_strip", Color.white, Image.Type.Sliced);
            // 三个控件各自定位（首版全拉满顶栏 → 互相重叠、关闭按钮居中）
            var titleBox = PlaceBox("Slot_Title", top.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(420f, 64f), new Vector2(40f, 0f));
            Text("Tmp_Title", titleBox, font, "灵感树", 34, TextAlignmentOptions.Left, Ink);
            var pointsBox = PlaceBox("Slot_Points", top.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(360f, 64f), Vector2.zero);
            Text("Tmp_Points", pointsBox, font, "悟道点 0", 28, TextAlignmentOptions.Center, Ink);
            var closeBox = PlaceBox("Slot_Close", top.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(72f, 64f), new Vector2(-40f, 0f));
            var close = Button("Btn_Close", closeBox, font, "×", 34);

            // 机制说明条（用户 11-12："不知道干嘛的"）
            var hintBox = Node("Root_Hint", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(60f, -152f), new Vector2(-60f, -126f));
            var hint = Text("Tmp_Hint", hintBox.gameObject, font,
                "悟道点 = 难度×2 + 通关+10 + 首通幕×5 ｜ 节点只改规则、不加数值 ｜ 每支从下层往上逐级解锁",
                20, TextAlignmentOptions.Center, Ink);

            // 三列 × 5 层：branch 列容器（InspirationPanel 在运行时按 tier 填按钮文本/状态）
            string[] branches = { "Branch_travel", "Branch_battle", "Branch_forge" };
            string[] titles = { "行旅", "战阵", "炉火" };
            for (int b = 0; b < 3; b++)
            {
                // 定点 anchor + sizeDelta（横跨式 anchor 配 offset 极易写错——首版事故根因之二）
                var col = Node(branches[b], rootRt,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                var colRt = (RectTransform)col.transform;
                colRt.pivot = new Vector2(0f, 1f);          // 左上定点（首版中心 pivot + 底部锚 → 列掉出屏底）
                colRt.sizeDelta = new Vector2(590f, 812f);
                colRt.anchoredPosition = new Vector2(60f + b * 620f, -166f);
                Text("Tmp_BranchName", col.gameObject, font, titles[b], 30, TextAlignmentOptions.Center, Ink);
                var colHead = (RectTransform)col.transform.Find("Tmp_BranchName");
                colHead.anchorMin = new Vector2(0f, 1f); colHead.anchorMax = new Vector2(1f, 1f);
                colHead.pivot = new Vector2(0.5f, 1f);
                colHead.anchoredPosition = new Vector2(0f, 0f); colHead.sizeDelta = new Vector2(0f, 46f);

                var grid = Node("Grid", colRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(8f, 8f), new Vector2(-8f, -50f));
                for (int t = 5; t >= 1; t--)   // T5 顶 → T1 底（树状从下往上点）
                {
                    int row = 5 - t;
                    var btn = Button("Btn_T" + t, grid, font, "T" + t, 24);
                    var rt = (RectTransform)btn.transform;
                    rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -row * 148f);
                    rt.sizeDelta = new Vector2(0f, 138f);
                    btn.GetComponent<Image>().sprite = LoadSprite("card_solid");
                    // 节点两行文本由 InspirationPanel.RefreshNode 写 Tmp_Label（名+消耗 / 描述）
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

        /// <summary>单点 anchor + 左上/中心/右上 pivot 的精确定位盒（子控件可拉满此盒）。</summary>
        private static GameObject PlaceBox(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = pivot; rt.sizeDelta = size; rt.anchoredPosition = pos;
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
