// ============================================================================
//  Editor Tool —— 构建 / 重新绑定「幕开场面板」Panel_ActIntro
//  ---------------------------------------------------------------------------
//  为什么要有这个菜单：
//    Panel_ActIntro.prefab 是**程序化创建**的（不像旧面板是 UIPanelPrefabBuilder 生成的），
//    它需要在 prefab 根上挂 `ActIntroPanel` 组件、并把 5 个引用填好。
//    这步必须在编辑器里做；做成菜单的好处是 —— 以后**重建 prefab 后点一次就能重新绑**，
//    不用我每次远程改（也方便你自己改完美术后一键复原绑定）。
//
//  用法：菜单 `WanXiang/UI/构建开场面板`，或 `.../校验开场面板绑定` 看当前绑定状态。
// ============================================================================
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Editor.UITool
{
    public static class ActIntroPrefabTool
    {
        private const string Path = "Assets/Resources/UI/Panel_ActIntro.prefab";

        [MenuItem("WanXiang/UI/构建开场面板", priority = 120)]
        public static void Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null) { Debug.LogError("[ActIntro] 找不到 " + Path); return; }

            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var panelType = FindType("WanXiang.Modules.UI.ActIntroPanel");
                if (panelType == null) { Debug.LogError("[ActIntro] 找不到 ActIntroPanel 类型"); return; }

                var comp = root.GetComponent(panelType);
                if (comp == null) comp = root.AddComponent(panelType);

                var so = new SerializedObject(comp);
                int bound = 0;
                bound += Bind(so, root, "_imgPaper", "Img_Paper", typeof(Image));
                bound += Bind(so, root, "_tmpTitleMain", "Tmp_TitleMain", typeof(TMPro.TMP_Text));
                bound += Bind(so, root, "_tmpTitleSub", "Tmp_TitleSub", typeof(TMPro.TMP_Text));
                bound += Bind(so, root, "_rootLeaves", "Root_Leaves", typeof(RectTransform));
                bound += Bind(so, root, "_itemLeaf", "Item_Leaf", typeof(Image));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("[ActIntro] 已挂组件并绑定 " + bound + "/5 个引用 ⇒ " + Path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.Refresh();
        }

        [MenuItem("WanXiang/UI/校验开场面板绑定", priority = 121)]
        public static void Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (prefab == null) { Debug.LogError("[ActIntro] 找不到 prefab"); return; }
            var panelType = FindType("WanXiang.Modules.UI.ActIntroPanel");
            if (panelType == null) { Debug.LogError("[ActIntro] 找不到 ActIntroPanel 类型"); return; }
            var comp = prefab.GetComponent(panelType);
            if (comp == null) { Debug.LogWarning("[ActIntro] prefab 上还没有 ActIntroPanel 组件 ⇒ 跑一次「构建开场面板」"); return; }

            var so = new SerializedObject(comp);
            string[] fields = { "_imgPaper", "_tmpTitleMain", "_tmpTitleSub", "_rootLeaves", "_itemLeaf" };
            int ok = 0;
            foreach (var f in fields)
            {
                var p = so.FindProperty(f);
                bool good = p != null && p.objectReferenceValue != null;
                if (good) ok++;
                Debug.Log("[ActIntro] " + f + " = " + (good ? p.objectReferenceValue.name : "❌ 空"));
            }
            Debug.Log("[ActIntro] 绑定 " + ok + "/5" + (ok == 5 ? " ✓ 正常" : " ⇒ 需要重跑「构建开场面板」"));
        }

        private static int Bind(SerializedObject so, GameObject root, string field, string node, Type compType)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning("[ActIntro] 字段不存在：" + field); return 0; }
            var t = FindDeep(root.transform, node);
            if (t == null) { Debug.LogWarning("[ActIntro] 节点不存在：" + node); return 0; }
            // ⛔ 组件类型必须与字段类型一致，否则 Unity **静默丢弃**（本工程踩过）
            prop.objectReferenceValue = t.GetComponent(compType);
            return prop.objectReferenceValue != null ? 1 : 0;
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

        private static Type FindType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
