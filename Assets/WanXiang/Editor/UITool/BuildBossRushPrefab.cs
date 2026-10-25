// ============================================================================
//  BuildBossRushPrefab —— 生成 Panel_BossRush.prefab（首领试炼，2026-10-25）
//  ---------------------------------------------------------------------------
//  菜单：WanXiang/UI/生成首领试炼面板
//  结构（全部走 UIBuild，风格与其它面板一致：米色纸底 + 墨色字）：
//    Panel_BossRush
//      Img_Paper           米宣纸底
//      Tmp_Title           标题「首领试炼」
//      Root_List           左列（VerticalLayoutGroup + ContentSizeFitter）
//        Item_Boss         模板：Img_Head(40) + Tmp_Name + Tmp_Tag（默认隐藏）
//      Tmp_Team / Tmp_Hint 右下说明
//      Btn_Close           关闭
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

        [MenuItem("WanXiang/UI/生成首领试炼面板", priority = 121)]
        internal static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            UIBuild.SetFont(font);

            var old = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Panel_BossRush.prefab");
            if (old != null) AssetDatabase.DeleteAsset(Dir + "/Panel_BossRush.prefab");

            // ---- 根 ----
            var root = UIBuild.Fixed(null, "Panel_BossRush", new Vector2(0.5f, 0.5f), new Vector2(W, H), Vector2.zero);
            var paper = UIBuild.Img(root, new Color(0.93f, 0.90f, 0.83f, 0.985f), true);

            // ---- 标题 ----
            var titleRt = UIBuild.Node(root, "Tmp_Title", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(48f, -110f), new Vector2(-48f, -36f));
            var title = UIBuild.Tmp(titleRt, "首领试炼", 52f, UIBuild.Ink);

            // ---- 左列表 ----
            var listArea = UIBuild.Node(root, "Root_List", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(48f, 120f), new Vector2(48f + 720f, -130f));
            var vlg = listArea.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            // ---- 列表项模板 ----
            var item = UIBuild.Fixed(listArea, "Item_Boss", new Vector2(0f, 1f), new Vector2(720f, 44f), Vector2.zero);
            UIBuild.Img(item, new Color(0.86f, 0.82f, 0.72f, 0.55f), true);
            var headRt = UIBuild.Fixed(item, "Img_Head", new Vector2(0f, 0.5f), new Vector2(36f, 36f), new Vector2(26f, 0f));
            UIBuild.Img(headRt, new Color(1f, 1f, 1f, 0f));
            var nameRt = UIBuild.Node(item, "Tmp_Name", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(56f, 2f), new Vector2(-140f, -2f));
            var nameTxt = UIBuild.Tmp(nameRt, "首领1", 26f, UIBuild.Ink, TextAlignmentOptions.Left);
            nameTxt.enableWordWrapping = false;
            var tagRt = UIBuild.Node(item, "Tmp_Tag", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-136f, 2f), new Vector2(-12f, -2f));
            UIBuild.Tmp(tagRt, "木", 22f, new Color(0.42f, 0.34f, 0.22f, 1f), TextAlignmentOptions.Right);
            UIBuild.Btn(item);

            // ---- 右侧说明 ----
            var teamRt = UIBuild.Node(root, "Tmp_Team", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(800f, -200f), new Vector2(-48f, -150f));
            var teamTxt = UIBuild.Tmp(teamRt, "", 24f, new Color(0.30f, 0.26f, 0.20f, 1f), TextAlignmentOptions.TopLeft);
            teamTxt.enableWordWrapping = true;

            var hintRt = UIBuild.Node(root, "Tmp_Hint", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(800f, 150f), new Vector2(-48f, 300f));
            var hintTxt = UIBuild.Tmp(hintRt, "", 22f, new Color(0.36f, 0.30f, 0.22f, 1f), TextAlignmentOptions.TopLeft);
            hintTxt.enableWordWrapping = true;

            // ---- 关闭按钮 ----
            var btnCloseRt = UIBuild.MakeBtn(root, "Btn_Close", new Vector2(1f, 0f),
                new Vector2(180f, 64f), new Vector2(-48f, 48f), "关闭", UIBuild.Ink, 28f);
            var btnClose = btnCloseRt.GetComponent<Button>();

            // ---- 组件绑定 ----
            var panel = root.gameObject.AddComponent<BossRushPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("_tmpTitle").objectReferenceValue = title;
            so.FindProperty("_tmpHint").objectReferenceValue = hintTxt;
            so.FindProperty("_tmpTeam").objectReferenceValue = teamTxt;
            so.FindProperty("_rootList").objectReferenceValue = listArea;
            so.FindProperty("_itemTemplate").objectReferenceValue = item;
            so.FindProperty("_btnClose").objectReferenceValue = btnClose;
            so.FindProperty("_sprites").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<SpriteCatalog>("Assets/WanXiang/Config/SpriteCatalog.asset");
            so.FindProperty("_content").objectReferenceValue =
                AssetDatabase.FindAssets("t:ContentCatalogSO").Length > 0
                    ? AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(
                        AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ContentCatalogSO")[0]))
                    : null;
            so.ApplyModifiedPropertiesWithoutUndo();

            item.gameObject.SetActive(false);

            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "UI");
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Dir + "/Panel_BossRush.prefab");
            Object.DestroyImmediate(root.gameObject);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BuildBossRushPrefab] 已生成 " + Dir + "/Panel_BossRush.prefab");
        }
    }
}
