// 一次性补丁：给 Panel_Home 加「首领试炼」入口按钮（2026-10-25）
// 菜单：WanXiang/UI/补丁·主界面首领试炼入口
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    internal static class PatchHomeBossRushButton
    {
        [MenuItem("WanXiang/UI/补丁·主界面首领试炼入口", priority = 122)]
        internal static void Patch()
        {
            const string path = "Assets/Resources/UI/Panel_Home.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogError("[PatchHome] 打不开 " + path); return; }

            var panel = root.GetComponent<HomePanel>();
            if (panel == null) { Debug.LogError("[PatchHome] 根上没有 HomePanel"); PrefabUtility.UnloadPrefabContents(root); return; }

            var tpl = root.transform.Find("Btn_History");
            if (tpl == null)
            {
                // 深一层找
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Btn_History") { tpl = t; break; }
            }
            if (tpl == null) { Debug.LogError("[PatchHome] 找不到 Btn_History 作模板"); PrefabUtility.UnloadPrefabContents(root); return; }

            var existing = tpl.parent.Find("Btn_BossRush");
            // 已存在也重新摆位（重跑补丁时生效）
            Transform btnT;
            if (existing != null)
            {
                btnT = existing;
                var ert = (RectTransform)existing;
                ert.anchoredPosition = ((RectTransform)tpl).anchoredPosition + new Vector2(150f, 0f);
            }
            else
            {
                var clone = Object.Instantiate(tpl.gameObject, tpl.parent);
                clone.name = "Btn_BossRush";
                var rt = (RectTransform)clone.transform;
                var src = (RectTransform)tpl;
                rt.anchoredPosition = src.anchoredPosition + new Vector2(150f, 0f);   // 横向排到右侧，不要往下压底排文字
                btnT = clone.transform;
            }

            // 文案
            foreach (var tmp in btnT.GetComponentsInChildren<TMP_Text>(true))
            { tmp.text = "首领试炼"; break; }

            var btn = btnT.GetComponent<Button>();
            if (btn == null) btn = btnT.gameObject.AddComponent<Button>();

            var so = new SerializedObject(panel);
            var prop = so.FindProperty("_btnBossRush");
            if (prop != null) { prop.objectReferenceValue = btn; so.ApplyModifiedPropertiesWithoutUndo(); }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PatchHome] 已给主界面加「首领试炼」按钮并绑定 _btnBossRush");
        }
    }
}
