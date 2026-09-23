// ============================================================================
//  万相 · 历程面板（Panel_History）生成器
//  ---------------------------------------------------------------------------
//  菜单：WanXiang/UI/生成历程面板
//  结构：标题 + 空提示 + 滚动列表（Item_Record 模板）+ 编队码区 + 返回
//  覆盖保存（SaveAsPrefabAsset）保留 GUID。
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildHistoryPanel
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_History.prefab";

        [MenuItem("WanXiang/UI/生成历程面板", priority = 108)]
        internal static void Build()
        {
            var root = new GameObject("Panel_History", typeof(RectTransform));
            root.AddComponent<WanXiang.Modules.UI.HistoryPanel>();   // ★ 必须挂在根上
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // 顶栏
            var top = UIBuild.Node(root.transform, "Root_TopBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -96f), new Vector2(-30f, -16f));
            UIBuild.Img(top, UIBuild.Silk, true);
            UIBuild.Tmp(UIBuild.Node(top, "Tmp_Title", Vector2.zero, Vector2.one,
                new Vector2(24f, 0f), new Vector2(-24f, 0f)), "历程", 40, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);

            // 空提示
            var empty = UIBuild.Node(root.transform, "Tmp_Empty",
                new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), new Vector2(30f, 0f), new Vector2(-30f, 0f));
            var et = UIBuild.Tmp(empty, "还没有历程记录 —— 打一局就会出现在这里", 30, UIBuild.Ink2,
                TextAlignmentOptions.Center);
            et.gameObject.name = "Tmp_Empty";

            // 滚动列表
            var scrollGo = UIBuild.Node(root.transform, "Scroll_List",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 180f), new Vector2(-30f, -110f));
            var sc = scrollGo.gameObject.AddComponent<ScrollRect>();
            sc.horizontal = false; sc.vertical = true; sc.movementType = ScrollRect.MovementType.Clamped;
            var vp = UIBuild.Node(scrollGo, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            vp.gameObject.AddComponent<RectMask2D>();
            sc.viewport = vp;
            var content = UIBuild.Node(vp, "Content",
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            sc.content = content;

            // 条目模板
            var item = UIBuild.Node(content, "Item_Record",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -8f), new Vector2(-8f, -8f));
            item.pivot = new Vector2(0.5f, 1f);
            item.sizeDelta = new Vector2(-16f, 124f);
            UIBuild.Img(item, UIBuild.Silk, true);

            UIBuild.Tmp(UIBuild.Node(item, "Tmp_Info", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(20f, -52f), new Vector2(-200f, -10f)), "第 1 幕 · 战力 0", 26, UIBuild.Ink,
                TextAlignmentOptions.MidlineLeft);
            UIBuild.Tmp(UIBuild.Node(item, "Tmp_Beasts", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(20f, 10f), new Vector2(-200f, -58f)), "（上场异兽）", 22, UIBuild.Ink2,
                TextAlignmentOptions.TopLeft);

            var copy = UIBuild.Node(item, "Btn_Copy", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-176f, -28f), new Vector2(-16f, 28f));
            var cimg = UIBuild.Img(copy, UIBuild.Gold, true);
            var cb = copy.gameObject.AddComponent<Button>(); cb.targetGraphic = cimg;
            UIBuild.Tmp(UIBuild.Node(copy, "Tmp_Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "复制编队码", 22, UIBuild.Ink);
            item.gameObject.SetActive(false);

            // 编队码显示区
            var code = UIBuild.Node(root.transform, "Root_Code",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(30f, 100f), new Vector2(-30f, 170f));
            UIBuild.Img(code, UIBuild.Card, true);
            UIBuild.Tmp(UIBuild.Node(code, "Tmp_Code", Vector2.zero, Vector2.one,
                new Vector2(16f, 6f), new Vector2(-16f, -6f)), "（点某局的「复制编队码」后显示在这里）", 20,
                UIBuild.Ink2, TextAlignmentOptions.MidlineLeft);

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
            Debug.Log("[HistoryBuild] OK 已生成 " + PrefabPath);
        }

        [MenuItem("WanXiang/UI/绑定历程面板字段", priority = 109)]
        internal static void Bind()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[HistoryBuild] 载入失败"); return; }
            try
            {
                var panel = root.GetComponent<WanXiang.Modules.UI.HistoryPanel>();
                if (panel == null) { Debug.LogError("[HistoryBuild] 根上没有 HistoryPanel"); return; }
                var so = new SerializedObject(panel);
                SetRef(so, root, "_tmpTitle", "Tmp_Title");
                SetRef(so, root, "_tmpEmpty", "Tmp_Empty");
                SetRef(so, root, "_listContent", "Content");
                SetRef(so, root, "_itemTemplate", "Item_Record");
                SetRef(so, root, "_tmpCode", "Tmp_Code");
                SetRef(so, root, "_btnBack", "Btn_Back");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.Refresh();
                Debug.Log("[HistoryBuild] 字段绑定完成");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetRef(SerializedObject so, GameObject root, string field, string nodeName)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[HistoryBuild] 字段不存在：" + field); return; }
            var t = FindDeep(root.transform, nodeName);
            p.objectReferenceValue = t != null ? t.gameObject : null;
            if (t == null) Debug.LogWarning("[HistoryBuild] 节点缺失：" + nodeName);
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
