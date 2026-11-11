// ============================================================================
//  万相 · 编辑器工具 · 技能槽面板 prefab 构建器（S4）
//  ---------------------------------------------------------------------------
//  与 S1/S2 同套路：编辑器生成 prefab（幂等、覆盖前备份），资源全复用。
//  布局（规格 §3）：左兽列表 / 右 4 个技能槽（普攻·奥义·觉醒锁定）+ 底部提示 /
//  候选弹层（Root_Picker，默认隐藏）。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class SkillLoadoutPanelBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_SkillLoadout.prefab";
        private const string KitDir = "Assets/Resources/UI/Kit/";
        private const string FontPath = "Assets/ArtRes/Fonts/Deng SDF.asset";

        private static readonly Color Paper = new Color(0.93f, 0.90f, 0.84f, 0.98f);
        private static readonly Color Ink = Color.black;
        private static readonly Color InkSoft = new Color(0.25f, 0.24f, 0.22f, 1f);

        [MenuItem("万相/UI/生成 技能槽面板（S4）")]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            if (File.Exists(PrefabPath))
            {
                string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_backups", "ui");
                Directory.CreateDirectory(backupDir);
                File.Copy(PrefabPath, Path.Combine(backupDir,
                    "Panel_SkillLoadout_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".prefab"), true);
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            var root = new GameObject("Panel_SkillLoadout", typeof(RectTransform), typeof(Image));
            root.AddComponent<WanXiang.Modules.UI.SkillLoadoutPanel>();
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
            var title = Text("Tmp_Title", top, font, "技能", 46, TextAlignmentOptions.Left, Ink);
            Place(title, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(48f, 18f), new Vector2(0f, -14f));
            var close = Button("Btn_Close", top, font, "×", 34);
            Place((RectTransform)close.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-64f, 0f), new Vector2(-64f, 0f));
            ((RectTransform)close.transform).sizeDelta = new Vector2(64f, 64f);
            close.GetComponent<Image>().sprite = LoadSprite("btn_close");

            // ---------------- 左：兽列表 ----------------
            var scroll = Node("Scroll_Beasts", rootRt, new Vector2(0f, 0f), new Vector2(0.32f, 1f),
                new Vector2(32f, 56f), new Vector2(-16f, -120f));   // 顶栏 120 对齐（首版 206 大空白）
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

            var rowBeast = Node("Row_Beast_Template", content, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 72f));
            var ble = rowBeast.gameObject.AddComponent<LayoutElement>();
            ble.preferredHeight = 72f;
            AddImage(rowBeast, "card_solid", new Color(1f, 1f, 1f, 0.92f), Image.Type.Sliced);
            var rbtn = rowBeast.gameObject.AddComponent<Button>();
            rbtn.targetGraphic = rowBeast.GetComponent<Image>();
            var bn = Text("Tmp_Name", rowBeast, font, "句芒", 26, TextAlignmentOptions.Left, Ink);
            Place(bn, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -6f), new Vector2(-24f, -40f));
            var be = Text("Tmp_Element", rowBeast, font, "木", 20, TextAlignmentOptions.Left, InkSoft);
            Place(be, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(60f, 8f), new Vector2(-24f, 6f));
            rowBeast.gameObject.SetActive(false);

            // ---------------- 右：4 个技能槽 ----------------
            var right = Node("Root_Right", rootRt, new Vector2(0.32f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 56f), new Vector2(-32f, -120f));
            var bn2 = Text("Tmp_BeastName", right, font, "—", 34, TextAlignmentOptions.Left, Ink);
            Place(bn2, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -18f), new Vector2(-24f, -60f));

            string[] slotTypes = { "普攻", "战记", "终结技", "觉醒" };   // 统一口径（废"奥义"）
            for (int i = 0; i < 4; i++)
            {
                var slot = Node("Slot_" + i, right,
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -84f - i * 110f), new Vector2(-24f, -84f - (i + 1) * 110f + 10f));
                AddImage(slot, "card_solid", new Color(1f, 1f, 1f, 0.85f), Image.Type.Sliced);

                var st = Text("Tmp_SlotType", slot, font, slotTypes[i], 22, TextAlignmentOptions.Left, InkSoft);
                Place(st, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(20f, 0f));
                ((RectTransform)st.transform).sizeDelta = new Vector2(90f, 40f);

                var sk = Text("Tmp_SkillName", slot, font, "—", 26, TextAlignmentOptions.Left, Ink);
                Place(sk, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(120f, 0f), new Vector2(-170f, 0f));

                var lockT = Text("Tmp_Lock", slot, font, "固定", 20, TextAlignmentOptions.Right, InkSoft);
                Place(lockT, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(-24f, 0f));
                ((RectTransform)lockT.transform).sizeDelta = new Vector2(126f, 40f);
                lockT.gameObject.SetActive(i != 1);

                if (i == 1)
                {
                    var ch = Button("Btn_Change", slot, font, "更换", 22);
                    Place((RectTransform)ch.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(-14f, 0f), new Vector2(-14f, 0f));
                    ((RectTransform)ch.transform).sizeDelta = new Vector2(110f, 52f);
                }
            }

            // B5-4+：空态引导卡（还没有异兽时的两步引导；SkillLoadoutPanel.Refresh 切显隐）
            var guide = Node("Empty_Guide", right, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, Vector2.zero);
            var guideRt = (RectTransform)guide.transform;
            guideRt.pivot = new Vector2(0f, 1f);
            guideRt.sizeDelta = new Vector2(700f, 220f);
            guideRt.anchoredPosition = new Vector2(24f, -84f);
            AddImage(guide, "card_solid", new Color(1f, 1f, 1f, 0.85f), Image.Type.Sliced);
            Text("Tmp_GuideTitle", guide, font, "还没有异兽", 26, TextAlignmentOptions.Center, Ink);
            var g1 = guide.transform.Find("Tmp_GuideTitle") as RectTransform;
            g1.anchorMin = new Vector2(0f, 1f); g1.anchorMax = new Vector2(1f, 1f);
            g1.pivot = new Vector2(0.5f, 1f); g1.anchoredPosition = new Vector2(0f, -16f); g1.sizeDelta = new Vector2(0f, 36f);
            Text("Tmp_GuideBody", guide, font,
                "① 主界面「出征」选分队打一局\n② 战斗中击败并捕获异兽\n③ 捕获成功后回到这里学战记",
                22, TextAlignmentOptions.Center, InkSoft);
            var g2 = guide.transform.Find("Tmp_GuideBody") as RectTransform;
            g2.anchorMin = new Vector2(0f, 1f); g2.anchorMax = new Vector2(1f, 1f);
            g2.pivot = new Vector2(0.5f, 1f); g2.anchoredPosition = new Vector2(0f, -56f); g2.sizeDelta = new Vector2(0f, 140f);

            var hint = Text("Tmp_Hint", right, font,
                "战记槽数量由品级决定：灵品 1 / 玄·神 2（第二战记槽暂未开放）", 20,
                TextAlignmentOptions.Left, InkSoft);
            Place(hint, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 18f), new Vector2(-24f, 14f));

            // ---------------- 候选弹层（默认隐藏）----------------
            var picker = Node("Root_Picker", rootRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var pmask = picker.gameObject.AddComponent<RectMask2D>();
            var pbg = picker.gameObject.AddComponent<Image>();
            pbg.sprite = LoadSprite("panel_dialog");
            pbg.type = Image.Type.Sliced;
            pbg.color = new Color(0.97f, 0.95f, 0.90f, 0.98f);
            picker.gameObject.SetActive(false);

            var ptitle = Text("Tmp_PickerTitle", picker, font, "选择战记", 30, TextAlignmentOptions.Left, Ink);
            Place(ptitle, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -16f), new Vector2(-24f, -54f));

            var pscroll = Node("Scroll_Pick", picker, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 90f), new Vector2(-24f, -70f));
            var psr = pscroll.gameObject.AddComponent<ScrollRect>();
            var pvp = Node("Viewport", (RectTransform)pscroll, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            pvp.gameObject.AddComponent<RectMask2D>();
            var pvpImg = pvp.gameObject.AddComponent<Image>();
            pvpImg.color = new Color(1f, 1f, 1f, 0f);
            var pcontent = Node("Content", pvp, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f));
            pcontent.pivot = new Vector2(0.5f, 1f);
            var pvlg = pcontent.gameObject.AddComponent<VerticalLayoutGroup>();
            pvlg.spacing = 10f;
            pvlg.childControlHeight = false;
            pvlg.childControlWidth = true;
            pvlg.childForceExpandHeight = false;
            pvlg.childForceExpandWidth = true;
            var pfitter = pcontent.gameObject.AddComponent<ContentSizeFitter>();
            pfitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            psr.viewport = pvp;
            psr.content = pcontent;
            psr.horizontal = false;
            psr.vertical = true;

            var rowSkill = Node("Row_Skill_Template", pcontent, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 92f));
            var sle = rowSkill.gameObject.AddComponent<LayoutElement>();
            sle.preferredHeight = 92f;
            AddImage(rowSkill, "card_solid", new Color(1f, 1f, 1f, 0.92f), Image.Type.Sliced);
            var sbtn = rowSkill.gameObject.AddComponent<Button>();
            sbtn.targetGraphic = rowSkill.GetComponent<Image>();
            var skn = Text("Tmp_PickName", rowSkill, font, "凝神", 26, TextAlignmentOptions.Left, Ink);
            Place(skn, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(60f, -8f), new Vector2(-24f, -44f));
            var skd = Text("Tmp_PickDesc", rowSkill, font, "描述", 20, TextAlignmentOptions.Left, InkSoft);
            Place(skd, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(60f, 10f), new Vector2(-24f, 8f));
            rowSkill.gameObject.SetActive(false);

            var cancel = Button("Btn_Cancel", picker, font, "取消", 26);
            Place((RectTransform)cancel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-180f, 20f), new Vector2(-180f, 20f));
            ((RectTransform)cancel.transform).sizeDelta = new Vector2(150f, 56f);

            // ---------------- 存 prefab ----------------
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[SkillLoadoutPanel] 已生成 " + PrefabPath);
            return PrefabPath;
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
