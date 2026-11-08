// ============================================================================
//  万相 · 编辑器工具 · 道痕面板 prefab 构建器（S2）
//  ---------------------------------------------------------------------------
//  与 SquadSelectPrefabBuilder 同一套路：编辑器里生成 prefab 资产（幂等、覆盖前备份），
//  美术可直接在 prefab 里改。资源全部复用现有 UI Kit + Deng SDF。
//
//  布局（规格见 文档/03_表现与美术/万相_新面板_UI规格_v1.md §2）：
//    顶栏（标题 / 计数 / 关闭）
//    左：仓库列表（全部 7 条，未获得的置灰显示"未获得"）
//    右：详情 + 装备/卸下 + 2 个装备槽
//    底：分享码（导出复制；导入输入框留 S3）
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class DaoMarkPanelBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_DaoMark.prefab";
        private const string KitDir = "Assets/Resources/UI/Kit/";
        private const string FontPath = "Assets/ArtRes/Fonts/Deng SDF.asset";

        private static readonly Color Paper = new Color(0.93f, 0.90f, 0.84f, 0.98f);
        private static readonly Color Ink = Color.black;
        private static readonly Color InkSoft = new Color(0.25f, 0.24f, 0.22f, 1f);

        [MenuItem("万相/UI/生成 道痕面板（S2）")]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            if (File.Exists(PrefabPath))
            {
                string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_backups", "ui");
                Directory.CreateDirectory(backupDir);
                File.Copy(PrefabPath, Path.Combine(backupDir,
                    "Panel_DaoMark_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".prefab"), true);
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            var root = new GameObject("Panel_DaoMark", typeof(RectTransform), typeof(Image));
            root.AddComponent<WanXiang.Modules.UI.DaoMarkPanel>();
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var bg = root.GetComponent<Image>();
            bg.sprite = LoadSprite("panel_dialog");
            bg.type = Image.Type.Sliced;
            bg.color = Paper;

            // ---------------- 顶栏 ----------------
            var top = Node("Root_TopBar", rootRt, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -110f), Vector2.zero);
            AddImage(top, "panel_strip", new Color(1f, 1f, 1f, 0.55f), Image.Type.Sliced);
            var title = Text("Tmp_Title", top, font, "道痕", 46, TextAlignmentOptions.Left, Ink);
            Place(title, new Vector2(0f, 0f), new Vector2(0.4f, 1f), new Vector2(48f, 18f), new Vector2(0f, -14f));
            var count = Text("Tmp_Count", top, font, "0 / 7", 30, TextAlignmentOptions.Left, InkSoft);
            Place(count, new Vector2(0.4f, 0f), new Vector2(0.7f, 1f), new Vector2(24f, 18f), new Vector2(0f, -14f));
            var close = Button("Btn_Close", top, font, "×", 34);
            Place((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-64f, 0f), new Vector2(-64f, 0f));
            ((RectTransform)close.transform).sizeDelta = new Vector2(64f, 64f);
            close.GetComponent<Image>().sprite = LoadSprite("btn_close");

            // ---------------- 左：仓库列表 ----------------
            var scroll = Node("Scroll_Grid", rootRt, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(32f, 150f), new Vector2(-16f, -206f));
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            var viewport = Node("Viewport", (RectTransform)scroll, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0f);
            var content = Node("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
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

            var row = Node("Row_Dao_Template", content, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 84f));
            var rowLe = row.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 84f;
            AddImage(row, "card_solid", new Color(1f, 1f, 1f, 0.92f), Image.Type.Sliced);

            var accent = Node("Img_Accent", row, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 10f), new Vector2(14f, -10f));
            accent.gameObject.AddComponent<Image>().color = new Color(0.62f, 0.60f, 0.56f, 1f);

            var rn = Text("Tmp_Name", row, font, "化生", 28, TextAlignmentOptions.Left, Ink);
            Place(rn, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(170f, -12f), new Vector2(-140f, -52f));

            var rs = Text("Tmp_Star", row, font, "★☆☆", 26, TextAlignmentOptions.Left, Ink);
            Place(rs, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(170f, 12f), new Vector2(-140f, -8f));

            var locked = Text("Tmp_Locked", row, font, "未获得", 22, TextAlignmentOptions.Right, InkSoft);
            Place(locked, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(-24f, 0f));
            ((RectTransform)locked.transform).sizeDelta = new Vector2(126f, 40f);

            row.gameObject.SetActive(false);

            // ---------------- 右：详情 + 装备 ----------------
            var right = Node("Root_Right", rootRt, new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 260f), new Vector2(-32f, -206f));

            var dname = Text("Tmp_DetailName", right, font, "—", 34, TextAlignmentOptions.Left, Ink);
            Place(dname, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -20f), new Vector2(-24f, -64f));

            var dstar = Text("Tmp_DetailStar", right, font, "—", 26, TextAlignmentOptions.Left, InkSoft);
            Place(dstar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -70f), new Vector2(-24f, -104f));

            var ddesc = Text("Tmp_DetailDesc", right, font, "选择左侧道痕查看详情", 24,
                TextAlignmentOptions.Left, InkSoft);
            Place(ddesc, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -110f), new Vector2(-24f, -170f));

            var dsrc = Text("Tmp_DetailSource", right, font, "—", 22, TextAlignmentOptions.Left, InkSoft);
            Place(dsrc, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -176f), new Vector2(-24f, -210f));

            var equip = Button("Btn_Equip", right, font, "装备", 28);
            Place((RectTransform)equip.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -224f), new Vector2(24f, -224f));
            ((RectTransform)equip.transform).sizeDelta = new Vector2(200f, 64f);
            equip.GetComponent<Image>().sprite = LoadSprite("btn_primary");

            // ---------------- 右：装备槽 ----------------
            var slots = Node("Root_Equipped", right, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -300f), new Vector2(-24f, -428f));
            for (int i = 0; i < 2; i++)
            {
                var slot = Node("Slot_" + i, slots,
                    new Vector2(i * 0.5f, 1f), new Vector2((i + 1) * 0.5f, 1f),
                    new Vector2(i == 0 ? 0f : 6f, 0f), new Vector2(i == 0 ? -6f : 0f, -120f));
                AddImage(slot, "card_solid", new Color(1f, 1f, 1f, 0.85f), Image.Type.Sliced);
                var st = Text("Tmp_SlotName", slot, font, "空槽位", 22, TextAlignmentOptions.Center, InkSoft);
                Place(st, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(12f, 0f), new Vector2(-12f, 0f));
            }

            // ---------------- 底：分享码 ----------------
            var share = Node("Root_Share", rootRt, new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(16f, 0f), new Vector2(-32f, 250f));
            var code = Text("Tmp_Code", share, font, "—", 20, TextAlignmentOptions.Left, InkSoft);
            Place(code, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 10f), new Vector2(-180f, -10f));
            var copy = Button("Btn_Copy", share, font, "复制", 24);
            Place((RectTransform)copy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-90f, 0f), new Vector2(-90f, 0f));
            ((RectTransform)copy.transform).sizeDelta = new Vector2(150f, 56f);
            copy.GetComponent<Image>().sprite = LoadSprite("btn_secondary");

            var imp = Button("Btn_Import", share, font, "从剪贴板导入", 22);
            Place((RectTransform)imp.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-90f, 12f), new Vector2(-90f, 12f));
            ((RectTransform)imp.transform).sizeDelta = new Vector2(190f, 52f);
            imp.GetComponent<Image>().sprite = LoadSprite("btn_secondary");

            // ---------------- 存 prefab ----------------
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[DaoMarkPanel] 已生成 " + PrefabPath);
            return PrefabPath;
        }

        // ------------------------------------------------------------ 小工具（与 S1 相同套路）
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

        private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private static void Place(TMP_Text t, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            => Place((RectTransform)t.transform, aMin, aMax, offMin, offMax);

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
            var rt = Node(name, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
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
    }
}
