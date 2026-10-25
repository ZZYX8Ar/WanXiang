// ============================================================================
//  BuildBossRushPrefab —— 生成 Panel_BossRush.prefab（首领试炼 v2，2026-10-25）
//  ---------------------------------------------------------------------------
//  v2 布局（用户反馈："布局很糟糕""要能填对战码""不该点横条就进战斗"）：
//    Panel_BossRush
//      Img_Paper
//      Tmp_Title               标题
//      Root_List               左列竖排（24 行 × 34px，够放 14 主线 + 10 秘境）
//        Item_Boss             模板：Img_Head 28 + Tmp_Name + Tmp_Tag + Btn_Challenge
//      Root_Code               右上：标题 + Inp_Code + Btn_LoadCode
//      Tmp_Team / Tmp_Hint     右下说明
//      Btn_Close
// ============================================================================
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Modules.UI;
using WanXiang.Battle.Presentation;
using WanXiang.Fusion;

namespace WanXiang.EditorTools
{
    internal static class BuildBossRushPrefab
    {
        private const string Dir = "Assets/Resources/UI";
        private const float W = 1600f, H = 900f;

        /// <summary>取 UI/Kit 下的九宫格图并设成 Sliced（与图鉴/局外成长同一套按钮美术）。</summary>
        private static Sprite Kit(string name)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Kit/" + name + ".png");
            if (sp == null) Debug.LogWarning("[BuildBossRush] 缺 Kit 图：" + name);
            return sp;
        }

        private static void Sliced(RectTransform rt, Sprite sp, Color tint)
        {
            var img = UIBuild.Img(rt, tint, true);
            if (sp != null) { img.sprite = sp; img.type = Image.Type.Sliced; img.color = tint; }
        }

        /// <summary>把圆角图设成九宫格（border 24）并重导入 —— 与手绘 UI 的圆角/描边一致。</summary>
        private static void EnsureRoundedImport()
        {
            string[] names = { "round_card", "round_btn", "round_btn_dark" };
            foreach (var n in names)
            {
                string path = "Assets/Resources/UI/Kit/" + n + ".png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) { Debug.LogWarning("[BuildBossRush] 找不到 " + path); continue; }
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spriteBorder = new Vector4(24f, 24f, 24f, 24f);
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
        }

        [MenuItem("WanXiang/UI/生成首领试炼面板", priority = 121)]
        internal static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            if (font == null) font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            UIBuild.SetFont(font);
            EnsureRoundedImport();

            var old = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Panel_BossRush.prefab");
            if (old != null) AssetDatabase.DeleteAsset(Dir + "/Panel_BossRush.prefab");

            // ---- 根：**铺满**（与 PvP / 秘境面板同一套 MIO 版式）----
            var root = new GameObject("Panel_BossRush", typeof(RectTransform)).GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;

            // 全屏压暗底（弹窗语义：底下主界面要退后）
            var dim = UIBuild.Stretch(root, "Img_Dim", 0, 0, 0, 0);
            UIBuild.Img(dim, new Color(0.04f, 0.05f, 0.07f, 0.62f), true);

            // 顶栏：丝绢底 + 墨字标题（MIO 标准）
            var top = UIBuild.Node(root, "Root_TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(30f, -96f), new Vector2(-30f, -16f));
            UIBuild.Img(top, UIBuild.Silk, true);
            var title = UIBuild.Tmp(UIBuild.Node(top, "Tmp_Title", Vector2.zero, Vector2.one,
                new Vector2(24f, 0f), new Vector2(-24f, 0f)), "首领试炼", 40f, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);

            // ---------------- 左列：首领列表 ----------------
            var listCard = UIBuild.Node(root, "Root_ListCard", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(30f, 30f), new Vector2(30f + 900f, -116f));
            Sliced(listCard, Kit("round_card"), Color.white);
            var listArea = UIBuild.Node(listCard, "Root_List", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 16f), new Vector2(-16f, -16f));
            var vlg = listArea.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var item = UIBuild.Fixed(listArea, "Item_Boss", new Vector2(0f, 1f), new Vector2(860f, 34f), Vector2.zero);
            Sliced(item, Kit("round_card"), new Color(1f, 1f, 1f, 0.90f));
            var headRt = UIBuild.Fixed(item, "Img_Head", new Vector2(0f, 0.5f), new Vector2(26f, 26f), new Vector2(14f, 0f));
            UIBuild.Img(headRt, new Color(1f, 1f, 1f, 0f));
            var nameRt = UIBuild.Node(item, "Tmp_Name", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(48f, 0f), new Vector2(-250f, 0f));
            var nameTxt = UIBuild.Tmp(nameRt, "首领", 22f, UIBuild.Ink, TextAlignmentOptions.Left);
            nameTxt.enableWordWrapping = false;
            var tagRt = UIBuild.Node(item, "Tmp_Tag", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-244f, 0f), new Vector2(-130f, 0f));
            UIBuild.Tmp(tagRt, "", 18f, new Color(0.45f, 0.36f, 0.24f, 1f), TextAlignmentOptions.Right);

            var chRt = UIBuild.Fixed(item, "Btn_Challenge", new Vector2(1f, 0.5f), new Vector2(112f, 26f), new Vector2(-64f, 0f));
            Sliced(chRt, Kit("round_btn"), Color.white);
            var chBtn = chRt.gameObject.AddComponent<Button>();
            chBtn.targetGraphic = chRt.GetComponent<Image>();
            UIBuild.Tmp(UIBuild.Node(chRt, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "挑战", 18f, new Color(0.97f, 0.94f, 0.86f, 1f));

            // ---------------- 右上：对战码 ----------------
            var codeBox = UIBuild.Node(root, "Root_Code", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(960f, -300f), new Vector2(-30f, -116f));
            Sliced(codeBox, Kit("round_card"), Color.white);

            var codeLabelRt = UIBuild.Node(codeBox, "Tmp_CodeLabel", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(16f, -42f), new Vector2(-16f, -8f));
            UIBuild.Tmp(codeLabelRt, "对战码（留空 = 用当前旅程队伍）", 20f, UIBuild.Ink, TextAlignmentOptions.Left);

            var inpRt = UIBuild.Node(codeBox, "Inp_Code", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(16f, 16f), new Vector2(-152f, 60f));
            UIBuild.Img(inpRt, Color.white, true);
            var input = inpRt.gameObject.AddComponent<TMP_InputField>();
            var vp = UIBuild.Node(inpRt, "Viewport", Vector2.zero, Vector2.one, new Vector2(10f, 6f), new Vector2(-10f, -6f));
            vp.gameObject.AddComponent<RectMask2D>();
            var tx = UIBuild.Node(vp, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var txT = UIBuild.Tmp(tx, "", 18f, UIBuild.Ink, TextAlignmentOptions.MidlineLeft);
            txT.enableWordWrapping = false;
            var ph = UIBuild.Node(vp, "Placeholder", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var phT = UIBuild.Tmp(ph, "粘贴好友对战码…", 18f, UIBuild.Dim, TextAlignmentOptions.MidlineLeft);
            input.textViewport = vp; input.textComponent = txT; input.placeholder = phT;
            input.targetGraphic = inpRt.GetComponent<Image>();

            var loadRt = UIBuild.Fixed(codeBox, "Btn_LoadCode", new Vector2(1f, 0f), new Vector2(120f, 44f), new Vector2(-136f, 60f));
            Sliced(loadRt, Kit("round_btn_dark"), Color.white);
            var loadBtn = loadRt.gameObject.AddComponent<Button>();
            loadBtn.targetGraphic = loadRt.GetComponent<Image>();
            UIBuild.Tmp(UIBuild.Node(loadRt, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "载入", 20f, new Color(0.97f, 0.94f, 0.86f, 1f));

            // ---------------- 右下：说明 ----------------
            var teamRt = UIBuild.Node(root, "Tmp_Team", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(960f, -380f), new Vector2(-46f, -312f));
            var teamTxt = UIBuild.Tmp(teamRt, "", 22f, new Color(0.28f, 0.24f, 0.18f, 1f), TextAlignmentOptions.TopLeft);
            teamTxt.enableWordWrapping = true;

            var hintRt = UIBuild.Node(root, "Tmp_Hint", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(960f, 150f), new Vector2(-46f, 340f));
            var hintTxt = UIBuild.Tmp(hintRt, "", 20f, new Color(0.36f, 0.30f, 0.22f, 1f), TextAlignmentOptions.TopLeft);
            hintTxt.enableWordWrapping = true;

            var closeRt = UIBuild.Fixed(root, "Btn_Close", new Vector2(1f, 0f), new Vector2(64f, 64f), new Vector2(-48f, 44f));
            Sliced(closeRt, Kit("btn_close"), Color.white);
            var closeBtn = closeRt.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeRt.GetComponent<Image>();

            // ---------------- 绑定 ----------------
            var panel = root.gameObject.AddComponent<BossRushPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("_tmpTitle").objectReferenceValue = title;
            so.FindProperty("_tmpHint").objectReferenceValue = hintTxt;
            so.FindProperty("_tmpTeam").objectReferenceValue = teamTxt;
            so.FindProperty("_rootList").objectReferenceValue = listArea;
            so.FindProperty("_itemTemplate").objectReferenceValue = item;
            so.FindProperty("_btnClose").objectReferenceValue = closeBtn;
            so.FindProperty("_inputCode").objectReferenceValue = input;
            so.FindProperty("_btnLoadCode").objectReferenceValue = loadBtn;
            so.FindProperty("_sprites").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<SpriteCatalog>("Assets/WanXiang/Config/SpriteCatalog.asset");
            var guids = AssetDatabase.FindAssets("t:ContentCatalogSO");
            if (guids.Length > 0)
                so.FindProperty("_content").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
            so.ApplyModifiedPropertiesWithoutUndo();

            item.gameObject.SetActive(false);

            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "UI");
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Dir + "/Panel_BossRush.prefab");
            Object.DestroyImmediate(root.gameObject);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildBossRushPrefab] v2 已生成 " + Dir + "/Panel_BossRush.prefab");
        }
    }
}
