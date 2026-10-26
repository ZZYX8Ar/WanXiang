// 生成 Panel_UnitInspect（战斗内双击查看面板）
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class BuildUnitInspectPrefab
    {
        private const string Dir = "Assets/Resources/UI";

        [MenuItem("WanXiang/UI/生成单位查看面板", priority = 123)]
        internal static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            UIBuild.SetFont(font);

            var old = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Panel_UnitInspect.prefab");
            if (old != null) AssetDatabase.DeleteAsset(Dir + "/Panel_UnitInspect.prefab");

            var root = new GameObject("Panel_UnitInspect", typeof(RectTransform)).GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;

            var dim = UIBuild.Stretch(root, "Img_Dim", 0, 0, 0, 0);
            var dimImg = UIBuild.Img(dim, new Color(0.03f, 0.04f, 0.06f, 0.55f), true);
            var dimBtn = dim.gameObject.AddComponent<Button>();     // 点空白也能关
            dimBtn.targetGraphic = dimImg;

            // 卡片
            var card = UIBuild.Fixed(root, "Root_Card", new Vector2(0.5f, 0.5f), new Vector2(940f, 580f), Vector2.zero);
            var cardImg = UIBuild.Img(card, Color.white, true);
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Kit/panel_dialog.png");
            if (sp != null) { cardImg.sprite = sp; cardImg.type = Image.Type.Sliced; }

            // 元素色条
            var bar = UIBuild.Node(card, "Img_Accent", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -76f), new Vector2(-24f, -70f));
            var barImg = UIBuild.Img(bar, new Color(0.8f, 0.6f, 0.3f, 1f));

            var nameRt = UIBuild.Node(card, "Tmp_Name", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -140f), new Vector2(-90f, -84f));
            var nameTxt = UIBuild.Tmp(nameRt, "单位", 40f, UIBuild.Ink, TextAlignmentOptions.MidlineLeft);

            var statsRt = UIBuild.Node(card, "Tmp_Stats", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -196f), new Vector2(-32f, -146f));
            var statsTxt = UIBuild.Tmp(statsRt, "", 26f, new Color(0.30f, 0.26f, 0.20f, 1f), TextAlignmentOptions.TopLeft);
            statsTxt.enableWordWrapping = true;

            var statusRt = UIBuild.Node(card, "Tmp_Status", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -252f), new Vector2(-32f, -202f));
            var statusTxt = UIBuild.Tmp(statusRt, "", 24f, new Color(0.36f, 0.30f, 0.22f, 1f), TextAlignmentOptions.TopLeft);
            statusTxt.enableWordWrapping = true;

            var skillsRt = UIBuild.Node(card, "Tmp_Skills", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(32f, 128f), new Vector2(-32f, -262f));
            var skillsTxt = UIBuild.Tmp(skillsRt, "", 24f, new Color(0.22f, 0.19f, 0.14f, 1f), TextAlignmentOptions.TopLeft);
            skillsTxt.enableWordWrapping = true;

            var mechRt = UIBuild.Node(card, "Tmp_Mech", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(32f, 32f), new Vector2(-120f, 120f));
            var mechTxt = UIBuild.Tmp(mechRt, "", 24f, new Color(0.45f, 0.28f, 0.18f, 1f), TextAlignmentOptions.TopLeft);
            mechTxt.enableWordWrapping = true;

            var closeRt = UIBuild.Fixed(card, "Btn_Close", new Vector2(1f, 1f), new Vector2(64f, 64f), new Vector2(-52f, -44f));
            var closeImg = UIBuild.Img(closeRt, Color.white, true);
            var closeSp = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/UI/Kit/btn_close.png");
            if (closeSp != null) { closeImg.sprite = closeSp; closeImg.type = Image.Type.Sliced; }
            var closeBtn = closeRt.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;

            var panel = root.gameObject.AddComponent<WanXiang.Modules.UI.UnitInspectPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("_tmpName").objectReferenceValue = nameTxt;
            so.FindProperty("_tmpStats").objectReferenceValue = statsTxt;
            so.FindProperty("_tmpStatus").objectReferenceValue = statusTxt;
            so.FindProperty("_tmpSkills").objectReferenceValue = skillsTxt;
            so.FindProperty("_tmpMech").objectReferenceValue = mechTxt;
            so.FindProperty("_accentBar").objectReferenceValue = barImg;
            so.FindProperty("_btnClose").objectReferenceValue = closeBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = Dir + "/Panel_UnitInspect.prefab";
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
            Object.DestroyImmediate(root.gameObject);
            AssetDatabase.Refresh();
            Debug.Log("[BuildUnitInspect] 已生成 " + path);
        }
    }
}
