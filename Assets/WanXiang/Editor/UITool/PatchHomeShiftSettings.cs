// 一次性补丁：主界面「设置」右移，给「首领试炼」腾位置（2026-10-25）
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    internal static class PatchHomeShiftSettings
    {
        [MenuItem("WanXiang/UI/补丁·主界面设置右移", priority = 123)]
        internal static void Patch()
        {
            const string path = "Assets/Resources/UI/Panel_Home.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            Transform t = null;
            foreach (var c in root.GetComponentsInChildren<Transform>(true))
                if (c.name == "Hot_Settings") { t = c; break; }
            if (t == null) { Debug.LogError("[PatchShift] 找不到 Hot_Settings"); PrefabUtility.UnloadPrefabContents(root); return; }

            var rt = (RectTransform)t;
            float w = rt.rect.width > 1f ? rt.rect.width : 120f;
            rt.anchoredPosition = rt.anchoredPosition + new Vector2(w + 28f, 0f);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[PatchShift] Hot_Settings 右移 " + (w + 28f) + "px，新位置=" + rt.anchoredPosition);
        }
    }
}
