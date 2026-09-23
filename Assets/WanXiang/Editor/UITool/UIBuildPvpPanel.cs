// ============================================================================
//  万相 · 对战面板（Panel_Pvp）生成器
//  ---------------------------------------------------------------------------
//  菜单：WanXiang/UI/生成对战面板
//  结构：标题 / 我的码区（含复制）/ 对手码输入框 / 开始对战 / 战报 / 返回
//  ⚠ TMP_InputField 必须按标准层级建：Viewport(含 Text) + Placeholder，
//    否则会 "InputField 缺少 textComponent" 而无法输入。
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildPvpPanel
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Pvp.prefab";

        [MenuItem("WanXiang/UI/生成对战面板", priority = 110)]
        internal static void Build()
        {
            var root = new GameObject("Panel_Pvp", typeof(RectTransform));
            root.AddComponent<WanXiang.Modules.UI.PvpPanel>();    // ★ 必须挂在根上
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // 顶栏
            var top = UIBuild.Node(root.transform, "Root_TopBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -96f), new Vector2(-30f, -16f));
            UIBuild.Img(top, UIBuild.Silk, true);
            UIBuild.Tmp(UIBuild.Node(top, "Tmp_Title", Vector2.zero, Vector2.one,
                new Vector2(24f, 0f), new Vector2(-24f, 0f)), "好友对战（配对码）", 40, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);

            // 我的码区
            var mine = UIBuild.Node(root.transform, "Root_Mine",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -336f), new Vector2(-30f, -116f));
            UIBuild.Img(mine, UIBuild.Card, true);
            UIBuild.Tmp(UIBuild.Node(mine, "Tmp_MyCode", Vector2.zero, Vector2.one,
                new Vector2(20f, 10f), new Vector2(-220f, -10f)), "我的配对码", 22, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft).enableWordWrapping = true;

            // 我的码输入框（默认填最新一局的码，可手动改）
            var mineInp = UIBuild.Node(mine, "Inp_MyCode",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 14f), new Vector2(-220f, 58f));
            UIBuild.Img(mineInp, Color.white, true);
            var mi = mineInp.gameObject.AddComponent<TMP_InputField>();
            var mvp = UIBuild.Node(mineInp, "Viewport", Vector2.zero, Vector2.one,
                new Vector2(10f, 6f), new Vector2(-10f, -6f));
            mvp.gameObject.AddComponent<RectMask2D>();
            var mtx = UIBuild.Node(mvp, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mtxT = UIBuild.Tmp(mtx, "", 20, UIBuild.Ink, TextAlignmentOptions.MidlineLeft);
            mtxT.enableWordWrapping = false;
            var mph = UIBuild.Node(mvp, "Placeholder", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mphT = UIBuild.Tmp(mph, "我的配对码（可修改）", 20, UIBuild.Dim, TextAlignmentOptions.MidlineLeft);
            mi.textViewport = mvp; mi.textComponent = mtxT; mi.placeholder = mphT;
            mi.targetGraphic = mineInp.GetComponent<Image>();

            var copy = UIBuild.Node(mine, "Btn_CopyMine",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-196f, -30f), new Vector2(-16f, 30f));
            var cimg = UIBuild.Img(copy, UIBuild.Gold, true);
            var cb = copy.gameObject.AddComponent<Button>(); cb.targetGraphic = cimg;
            UIBuild.Tmp(UIBuild.Node(copy, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "复制", 24, UIBuild.Ink);

            // 对手码输入框
            var inpRoot = UIBuild.Node(root.transform, "Inp_OppCode",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -350f), new Vector2(-30f, -276f));
            UIBuild.Img(inpRoot, Color.white, true);
            var input = inpRoot.gameObject.AddComponent<TMP_InputField>();

            var vp = UIBuild.Node(inpRoot, "Viewport", Vector2.zero, Vector2.one,
                new Vector2(12f, 8f), new Vector2(-12f, -8f));
            vp.gameObject.AddComponent<RectMask2D>();

            var txt = UIBuild.Node(vp, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var txtTmp = UIBuild.Tmp(txt, "", 22, UIBuild.Ink, TextAlignmentOptions.MidlineLeft);
            txtTmp.enableWordWrapping = false;

            var ph = UIBuild.Node(vp, "Placeholder", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var phTmp = UIBuild.Tmp(ph, "在此粘贴对方的配对码", 22, UIBuild.Dim, TextAlignmentOptions.MidlineLeft);

            input.textViewport = vp;
            input.textComponent = txtTmp;
            input.placeholder = phTmp;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.targetGraphic = inpRoot.GetComponent<Image>();

            // 对战按钮
            var fight = UIBuild.Node(root.transform, "Btn_Fight",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-160f, -440f), new Vector2(160f, -368f));
            var fimg = UIBuild.Img(fight, UIBuild.Gold, true);
            var fb = fight.gameObject.AddComponent<Button>(); fb.targetGraphic = fimg;
            UIBuild.Tmp(UIBuild.Node(fight, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "开始对战", 30, UIBuild.Ink);

            // 战报区
            var res = UIBuild.Node(root.transform, "Root_Result",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 180f), new Vector2(-30f, -460f));
            UIBuild.Img(res, UIBuild.Card, true);
            var resTmp = UIBuild.Tmp(UIBuild.Node(res, "Tmp_Result", Vector2.zero, Vector2.one,
                new Vector2(20f, 12f), new Vector2(-20f, -12f)), "（战报）", 24, UIBuild.Ink,
                TextAlignmentOptions.TopLeft);
            resTmp.enableWordWrapping = true;

            // 返回
            var back = UIBuild.Node(root.transform, "Btn_Back",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 20f), new Vector2(230f, 88f));
            var bimg = UIBuild.Img(back, UIBuild.Silk, true);
            var bb = back.gameObject.AddComponent<Button>(); bb.targetGraphic = bimg;
            UIBuild.Tmp(UIBuild.Node(back, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "返回", 28, UIBuild.Ink);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log("[PvpBuild] OK 已生成 " + PrefabPath);
        }

        [MenuItem("WanXiang/UI/绑定对战面板字段", priority = 111)]
        internal static void Bind()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[PvpBuild] 载入失败"); return; }
            try
            {
                var panel = root.GetComponent<WanXiang.Modules.UI.PvpPanel>();
                if (panel == null) { Debug.LogError("[PvpBuild] 根上没有 PvpPanel"); return; }
                var so = new SerializedObject(panel);
                SetRef(so, root, "_inputOpp", "Inp_OppCode");
                SetRef(so, root, "_tmpMyCode", "Tmp_MyCode");
                SetRef(so, root, "_btnCopyMine", "Btn_CopyMine");
                SetRef(so, root, "_btnFight", "Btn_Fight");
                SetRef(so, root, "_tmpResult", "Tmp_Result");
                SetRef(so, root, "_btnBack", "Btn_Back");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.Refresh();
                Debug.Log("[PvpBuild] 字段绑定完成");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetRef(SerializedObject so, GameObject root, string field, string nodeName)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[PvpBuild] 字段不存在：" + field); return; }
            var t = FindDeep(root.transform, nodeName);
            p.objectReferenceValue = t != null ? t.gameObject : null;
            if (t == null) Debug.LogWarning("[PvpBuild] 节点缺失：" + nodeName);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
#endif
