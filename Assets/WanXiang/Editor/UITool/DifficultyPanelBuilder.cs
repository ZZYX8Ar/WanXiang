// ============================================================================
//  万相 · 难度选择弹窗构建（Editor/UITool）
//  参考明日方舟肉鸽：18 档逐行列出（名称/数值/新规则/解锁状态），点行选中。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class DifficultyPanelBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Difficulty.prefab";
        private const string KitDir = "Assets/Resources/UI/Kit/";
        private static readonly Color Ink = Color.black;

        [MenuItem("万相/UI/重建难度弹窗", priority = 2)]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/ArtRes/UI/Fonts/SourceHanSerifCN-Bold SDF.asset");
            if (font == null) font = TMP_Settings.defaultFontAsset;

            var root = new GameObject("Panel_Difficulty", typeof(RectTransform));
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(1100f, 860f);
            AddImage(root, "panel_dialog", Color.white, Image.Type.Sliced);
            root.AddComponent<WanXiang.Modules.UI.DifficultyPanel>();

            var top = Node("Root_TopBar", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -100f), new Vector2(0f, 0f));
            AddImage(top, "panel_strip", Color.white, Image.Type.Sliced);
            var title = Text("Tmp_Title", top, font, "选择难度", 34, TextAlignmentOptions.Left, Ink);
            Box(title, new Vector2(0f, 0f), new Vector2(0.6f, 1f), new Vector2(40f, 10f), new Vector2(0f, -10f));
            var close = Button("Btn_Close", top, font, "×", 30);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.anchoredPosition = new Vector2(-30f, 0f);
            crt.sizeDelta = new Vector2(64f, 56f);

            var hint = Text("Tmp_UnlockHint", root, font, "通关 1 次解锁下一档", 20, TextAlignmentOptions.Left, Ink);
            Box(hint, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -140f), new Vector2(-40f, -106f));

            var scrollGo = Node("Scroll_List", rootRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(32f, 28f), new Vector2(-32f, -152f));
            var sr = scrollGo.AddComponent<ScrollRect>();
            var scrollRt = (RectTransform)scrollGo.transform;
            var vpGo = Node("Viewport", scrollRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>(); vpImg.color = new Color(1f, 1f, 1f, 0f);
            var contentGo = Node("Content", (RectTransform)vpGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.pivot = new Vector2(0.5f, 1f);
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f; vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = (RectTransform)vpGo.transform;
            sr.content = contentRt;
            sr.horizontal = false; sr.vertical = true;

            var rowGo = Node("Row_Diff_Template", contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(0f, 86f));
            var rowLe = rowGo.AddComponent<LayoutElement>(); rowLe.preferredHeight = 86f;
            AddImage(rowGo, "card_solid", Color.white, Image.Type.Sliced);
            var rowImg = rowGo.GetComponent<Image>();
            var rowBtn = rowGo.AddComponent<Button>(); rowBtn.targetGraphic = rowImg;

            var lv = Text("Tmp_Level", rowGo, font, "N1", 26, TextAlignmentOptions.Left, Ink);
            Box(lv, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -44f), new Vector2(140f, -8f));
            var tl = Text("Tmp_Title", rowGo, font, "入门", 20, TextAlignmentOptions.Left, Ink);
            Box(tl, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(150f, -44f), new Vector2(-300f, -8f));
            var st = Text("Tmp_State", rowGo, font, "已解锁", 18, TextAlignmentOptions.Right, Ink);
            Box(st, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-284f, -44f), new Vector2(-24f, -8f));
            var nums = Text("Tmp_Nums", rowGo, font, "生命×1.00　攻×1.00　防×1.00", 16, TextAlignmentOptions.Left, Ink);
            Box(nums, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(150f, -66f), new Vector2(-300f, -44f));
            var rule = Text("Tmp_RuleText", rowGo, font, "无新规则", 15, TextAlignmentOptions.Left, Ink);
            Box(rule, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(150f, -84f), new Vector2(-300f, -66f));
            rowGo.SetActive(false);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            int n = loaded ? loaded.GetComponentsInChildren<Transform>(true).Length : 0;
            Debug.Log("[DifficultyPanel] 已生成 " + PrefabPath + "（节点 " + n + "）");
            return PrefabPath + " nodes=" + n;
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
            var img = go.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.9f);
            go.AddComponent<Button>();
            Text("Tmp_Label", go, font, label, size, TextAlignmentOptions.Center, Ink);
            return go;
        }

        private static void Box(TMP_Text t, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
        }
    }
}
