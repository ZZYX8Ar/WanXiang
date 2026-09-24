// ============================================================================
//  万相 · UI 补丁：主城（Panel_Home）的「残卷阁」入口
//  ---------------------------------------------------------------------------
//  背景：v6 新增剧情碎片 / 觉醒材料系统（残卷阁 = 主界面剧情面板）。
//        异闻完成掉「相关异兽的剧情碎片」，集齐后在残卷阁领取觉醒材料。
//        需要主城给一个入口打开 ArchivePanel。
//
//  本补丁做两件事（增量，保留手工修改 —— 不重建 prefab）：
//    ① prefab 里补出 Btn_Archive（参照 Btn_Meta 的位置，放在它下方；无 Meta 则参照 Btn_Codex）
//    ② 把 HomePanel 的 _btnArchive 字段绑定到这个节点
//
//  ⚠ 复用 HomePanel：栏目由 HideRetiredEntries 控制显隐，残卷阁是局外主城入口，不隐藏。
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildArchiveEntryPatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Home.prefab";
        private const string NodeName = "Btn_Archive";

        [MenuItem("WanXiang/UI/补丁：主城残卷阁入口", priority = 108)]
        internal static void Patch()
        {
            if (!System.IO.File.Exists(PrefabPath))
            {
                Debug.LogError("[ArchiveEntryPatch] 找不到 " + PrefabPath);
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[ArchiveEntryPatch] 载入 prefab 失败"); return; }
            try
            {
                bool created = EnsureArchiveButton(root);
                BindField(root);

                if (created)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    AssetDatabase.Refresh();
                    Debug.Log("[ArchiveEntryPatch] OK 已补丁 " + PrefabPath);
                }
                else
                {
                    Debug.Log("[ArchiveEntryPatch] 按钮已存在，仅重做绑定");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>补出 Btn_Archive（参照 Btn_Meta 的位置，放它下方）。</summary>
        private static bool EnsureArchiveButton(GameObject root)
        {
            if (FindDeep(root.transform, NodeName) != null) return false;

            var anchorNode = FindDeep(root.transform, "Btn_Meta");
            string anchorName = "Btn_Meta";
            if (anchorNode == null) { anchorNode = FindDeep(root.transform, "Btn_Codex"); anchorName = "Btn_Codex"; }

            Transform parent = anchorNode != null ? anchorNode.parent : root.transform;

            var go = new GameObject(NodeName, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            if (anchorNode != null)
            {
                var ar = (RectTransform)anchorNode;
                rt.anchorMin = ar.anchorMin; rt.anchorMax = ar.anchorMax; rt.pivot = ar.pivot;
                rt.sizeDelta = ar.sizeDelta;
                rt.anchoredPosition = ar.anchoredPosition + new Vector2(0f, -100f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(220f, 72f);
                rt.anchoredPosition = new Vector2(0f, -280f);
            }

            var img = go.AddComponent<Image>();
            img.color = new Color(0.79f, 0.63f, 0.39f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = "残卷阁";
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.raycastTarget = false;
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;

            Debug.Log("[ArchiveEntryPatch] + " + NodeName + "（参照 " + anchorName + " 下方 100px）");
            return true;
        }

        private static void BindField(GameObject root)
        {
            var panel = root.GetComponent<WanXiang.Modules.UI.HomePanel>();
            if (panel == null) { Debug.LogError("[ArchiveEntryPatch] 根上没有 HomePanel 组件"); return; }
            var so = new SerializedObject(panel);
            var p = so.FindProperty("_btnArchive");
            if (p == null) { Debug.LogError("[ArchiveEntryPatch] 找不到 _btnArchive 字段"); return; }
            var t = FindDeep(root.transform, NodeName);
            p.objectReferenceValue = t != null ? t.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[ArchiveEntryPatch] _btnArchive ← " + (t != null ? (NodeName + " ✓") : "null ✗"));
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
