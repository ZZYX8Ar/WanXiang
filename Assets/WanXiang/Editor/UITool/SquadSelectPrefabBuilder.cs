// ============================================================================
//  万相 · 编辑器工具 · 分队选择面板 prefab 构建器（S1）
//  ---------------------------------------------------------------------------
//  为什么用代码建：项目规矩是"UI 一律 prefab、禁止运行时拼"。
//  这里不是在运行时拼 UI，而是**编辑器里生成 prefab 资产**，生成后美术可直接在
//  prefab 里改配色/尺寸/换图，符合"用户要能看见并改"的要求。
//
//  幂等：重复执行会覆盖同名 prefab（覆盖前自动把旧的备份到工程外 _backups/）。
//  资源全部复用：Resources/UI/Kit/{panel_dialog,panel_strip,btn_primary,btn_secondary,
//  btn_close,card_solid}.png + ArtRes/Fonts/Deng SDF。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class SquadSelectPrefabBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_SquadSelect.prefab";
        private const string KitDir = "Assets/Resources/UI/Kit/";
        private const string FontPath = "Assets/ArtRes/Fonts/Deng SDF.asset";

        // 纸面暖白 / 墨色文字（Linear 空间下深色要纯黑才压得住）
        private static readonly Color Paper = new Color(0.93f, 0.90f, 0.84f, 0.98f);
        private static readonly Color Ink = Color.black;
        private static readonly Color InkSoft = new Color(0.25f, 0.24f, 0.22f, 1f);

        [MenuItem("万相/UI/生成 分队选择面板（S1）")]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            // ---- 覆盖前备份到工程外（相对 Unity 工程上一层）----
            if (File.Exists(PrefabPath))
            {
                string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_backups", "ui");
                Directory.CreateDirectory(backupDir);
                string dst = Path.Combine(backupDir,
                    "Panel_SquadSelect_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".prefab");
                File.Copy(PrefabPath, dst, true);
            }

            var root = new GameObject("Panel_SquadSelect", typeof(RectTransform), typeof(Image));
            // 面板脚本必须挂在 prefab 根上，OpenAsync<T> 才能取到组件
            root.AddComponent<WanXiang.Modules.UI.SquadSelectPanel>();
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var bg = root.GetComponent<Image>();
            bg.sprite = LoadSprite("panel_dialog");
            bg.type = Image.Type.Sliced;
            bg.color = Paper;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            // ---------------- 顶栏 ----------------
            var top = Node("Root_TopBar", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -110f), Vector2.zero);
            AddImage(top, "panel_strip", new Color(1f, 1f, 1f, 0.55f), Image.Type.Sliced);

            var title = Text("Tmp_Title", top, font, "选择分队", 46, TextAlignmentOptions.Left, Ink);
            Place(title, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(48f, 18f), new Vector2(0f, -14f));
            var sub = Text("Tmp_SubTitle", top, font, "起手 5 只决定这局的流派；难度在开始前可调", 22,
                TextAlignmentOptions.Left, InkSoft);
            Place(sub, new Vector2(0f, 0f), new Vector2(0.7f, 0f), new Vector2(50f, 8f), new Vector2(0f, 46f));

            var close = Button("Btn_Close", top, font, "×", 34);
            Place((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-64f, 0f), new Vector2(-64f, 0f));
            var closeRt = (RectTransform)close.transform;
            closeRt.sizeDelta = new Vector2(64f, 64f);
            close.GetComponent<Image>().sprite = LoadSprite("btn_close");

            // ---------------- 难度条 ----------------
            var diff = Node("Root_Difficulty", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -190f), new Vector2(0f, -120f));
            var minus = Button("Btn_Minus", diff, font, "-", 36);   // Deng SDF 无 U+2212，必须用 ASCII 减号
            Place((RectTransform)minus.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(40f, 0f), new Vector2(40f, 0f));
            ((RectTransform)minus.transform).sizeDelta = new Vector2(56f, 56f);

            var level = Text("Tmp_Level", diff, font, "N1", 34, TextAlignmentOptions.Center, Ink);
            Place(level, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(124f, 0f), new Vector2(124f, 0f));
            ((RectTransform)level.transform).sizeDelta = new Vector2(120f, 56f);
            level.enableWordWrapping = false;

            var plus = Button("Btn_Plus", diff, font, "+", 36);
            Place((RectTransform)plus.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(200f, 0f), new Vector2(200f, 0f));
            ((RectTransform)plus.transform).sizeDelta = new Vector2(56f, 56f);

            var rule = Text("Tmp_Rule", diff, font, "—", 24, TextAlignmentOptions.Left, InkSoft);
            Place(rule, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(280f, 6f), new Vector2(-24f, -6f));

            // ---------------- 列表 ----------------
            var scroll = Node("Scroll_Grid", rootRt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(32f, 150f), new Vector2(-32f, -206f));
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            var viewport = Node("Viewport", (RectTransform)scroll, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            var content = Node("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;

            // ---- 行模板（运行时克隆；prefab 里 SetActive(false)）----
            var row = Node("Row_Squad_Template", content, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 140f));
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 140f;
            AddImage(row, "card_solid", new Color(1f, 1f, 1f, 0.92f), Image.Type.Sliced);

            var accent = Node("Img_Accent", row, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 10f), new Vector2(14f, -10f));   // 14px 竖条（原 8px 在边框里看不出来）
            var accentImg = accent.gameObject.AddComponent<Image>();
            accentImg.color = new Color(0.55f, 0.68f, 0.48f, 1f);

            // 三行垂直堆叠：名字 / 起手 / 描述 —— 统一从 x=170 起排，完全避开 9-slice 左边框
            var rn = Text("Tmp_Name", row, font, "回春", 30, TextAlignmentOptions.Left, Ink);
            Place(rn, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, -34f), new Vector2(170f, -34f));
            var rnRt = (RectTransform)rn.transform;
            rnRt.pivot = new Vector2(0f, 0.5f);                 // pivot 靠左 ⇒ x 表示左边缘，不会有一半跑出卡片
            rnRt.sizeDelta = new Vector2(360f, 44f);
            rn.enableWordWrapping = false;

            var rs = Text("Tmp_Starter", row, font, "句芒 / 灌灌 / 九尾狐", 22, TextAlignmentOptions.Left, InkSoft);
            Place(rs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(170f, -80f), new Vector2(-220f, -50f));

            var rd = Text("Tmp_Desc", row, font, "每回合回复…", 22, TextAlignmentOptions.Left, InkSoft);
            Place(rd, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(170f, -126f), new Vector2(-220f, -84f));

            var ru = Text("Tmp_Unlock", row, font, "通关第 1 幕解锁", 20, TextAlignmentOptions.Right,
                new Color(0.65f, 0.25f, 0.20f, 1f));
            Place(ru, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-270f, 0f), new Vector2(-270f, 0f));
            ((RectTransform)ru.transform).sizeDelta = new Vector2(220f, 40f);
            ru.enableWordWrapping = false;

            var pick = Button("Btn_Pick", row, font, "用这个", 26);
            Place((RectTransform)pick.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-176f, 0f), new Vector2(-176f, 0f));
            ((RectTransform)pick.transform).sizeDelta = new Vector2(152f, 60f);
            pick.GetComponent<Image>().sprite = LoadSprite("btn_secondary");

            row.gameObject.SetActive(false);

            var empty = Text("Tmp_Empty", content, font, "没有可选分队（数据缺失）", 24,
                TextAlignmentOptions.Center, InkSoft);
            var emptyRt = (RectTransform)empty.transform;
            emptyRt.sizeDelta = new Vector2(0f, 80f);

            // ---------------- 底栏 ----------------
            var bottom = Node("Root_Bottom", rootRt, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 140f));
            AddImage(bottom, "panel_strip", new Color(1f, 1f, 1f, 0.55f), Image.Type.Sliced);

            var preview = Text("Tmp_TeamPreview", bottom, font, "起手：—", 24, TextAlignmentOptions.Left, InkSoft);
            Place(preview, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(48f, 10f), new Vector2(-260f, -10f));

            var start = Button("Btn_Start", bottom, font, "开始远征", 30);
            Place((RectTransform)start.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-140f, 0f), new Vector2(-140f, 0f));
            ((RectTransform)start.transform).sizeDelta = new Vector2(220f, 68f);
            start.GetComponent<Image>().sprite = LoadSprite("btn_primary");

            // ---------------- 存 prefab ----------------
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            int nodes = CountNodes(PrefabPath);
            Debug.Log("[SquadSelectPrefab] 已生成 " + PrefabPath + "（节点 " + nodes + " 个）");
            return PrefabPath + " nodes=" + nodes;
        }

        // ------------------------------------------------------------ 小工具
        private static Sprite LoadSprite(string name)
            => AssetDatabase.LoadAssetAtPath<Sprite>(KitDir + name + ".png");

        private static RectTransform Node(string name, RectTransform parent,
            Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            return rt;
        }

        // 重载：TMP_Text 直接传（内部取 transform）
        private static void Place(TMP_Text t, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            => Place((RectTransform)t.transform, aMin, aMax, offMin, offMax);

        private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private static Image AddImage(RectTransform rt, string sprite, Color color, Image.Type type)
        {
            var img = rt.gameObject.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.sprite = LoadSprite(sprite);
            img.type = type;
            img.color = color;
            return img;
        }

        private static TMP_Text Text(string name, RectTransform parent, TMP_FontAsset font,
            string content, float size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        private static Button Button(string name, RectTransform parent, TMP_FontAsset font,
            string label, float size)
        {
            var rt = Node(name, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = LoadSprite("btn_secondary");
            img.type = Image.Type.Sliced;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var txt = Text("Tmp_Label", rt, font, label, size, TextAlignmentOptions.Center, Ink);
            var trt = (RectTransform)txt.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        private static int CountNodes(string path)
        {
            var text = File.ReadAllText(path);
            int n = 0, idx = 0;
            while ((idx = text.IndexOf("m_Name:", idx)) >= 0) { n++; idx += 7; }
            return n;
        }
    }
}
