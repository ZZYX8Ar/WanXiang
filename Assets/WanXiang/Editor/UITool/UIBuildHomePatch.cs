// ============================================================================
//  万相 · UI 补丁：主城（Panel_Home）的「局外成长」按钮
//  ---------------------------------------------------------------------------
//  背景：`HomePanel.HideRetiredEntries()` 里写着
//        "MetaPanel 目前只有 UI 骨架 ⇒ 先隐藏，等实现再接回来"。
//        现在 MetaPanel 已接到局外养成（MetaStore + 墨铊），把入口接回来。
//
//  本补丁做两件事（增量，保留手工修改）：
//    ① prefab 里补出 Btn_Meta（参照 Btn_Codex 的位置，放在它下方）
//    ② 把 HomePanel 的 _btnMeta 字段绑定到这个节点
// ============================================================================

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class UIBuildHomePatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Home.prefab";

        [MenuItem("WanXiang/UI/补丁：主城局外成长按钮", priority = 104)]
        internal static void Patch()
        {
            if (!System.IO.File.Exists(PrefabPath))
            {
                Debug.LogError("[HomePatch] 找不到 " + PrefabPath);
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[HomePatch] 载入 prefab 失败"); return; }
            try
            {
                bool created = EnsureMetaButton(root);
                BindField(root);

                if (created)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    AssetDatabase.Refresh();
                    Debug.Log("[HomePatch] OK 已补丁 " + PrefabPath);
                }
                else
                {
                    Debug.Log("[HomePatch] 按钮已存在，仅重做绑定");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>补出 Btn_Meta（参照 Btn_Codex 的位置，放它下方）。</summary>
        private static bool EnsureMetaButton(GameObject root)
        {
            if (FindDeep(root.transform, "Btn_Meta") != null) return false;

            var codex = FindDeep(root.transform, "Btn_Codex");
            Transform parent = codex != null ? codex.parent : root.transform;

            var go = new GameObject("Btn_Meta", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            if (codex != null)
            {
                var cr = (RectTransform)codex;
                rt.anchorMin = cr.anchorMin; rt.anchorMax = cr.anchorMax; rt.pivot = cr.pivot;
                rt.sizeDelta = cr.sizeDelta;
                rt.anchoredPosition = cr.anchoredPosition + new Vector2(0f, -100f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(220f, 72f);
                rt.anchoredPosition = new Vector2(0f, -180f);
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
            tmp.text = "局外成长";
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.raycastTarget = false;
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;

            Debug.Log("[HomePatch] + Btn_Meta（" + (codex != null ? "参照 Btn_Codex 下方 100px" : "屏幕中下") + "）");
            return true;
        }

        private static void BindField(GameObject root)
        {
            var panel = root.GetComponent<WanXiang.Modules.UI.HomePanel>();
            if (panel == null) { Debug.LogError("[HomePatch] 根上没有 HomePanel 组件"); return; }
            var so = new SerializedObject(panel);
            var p = so.FindProperty("_btnMeta");
            if (p == null) { Debug.LogError("[HomePatch] 找不到 _btnMeta 字段"); return; }
            var t = FindDeep(root.transform, "Btn_Meta");
            p.objectReferenceValue = t != null ? t.gameObject : null;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[HomePatch] _btnMeta ← " + (t != null ? "Btn_Meta ✓" : "null ✗"));
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
