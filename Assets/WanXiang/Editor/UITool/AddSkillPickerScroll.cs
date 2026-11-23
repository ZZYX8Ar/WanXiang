// 临时脚本：给技能选择Viewport补ScrollRect（内容超出可视区需可滚动）
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AddSkillPickerScroll
{
    [MenuItem("万相/UI/修复：技能选择滚动")]
    public static void Patch()
    {
        const string path = "Assets/Resources/UI/Panel_SkillLoadout.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) { Debug.LogError("[SkillScroll] 加载失败"); return; }
        try
        {
            // 找Picker下的Viewport（用深查找，避免路径写错）
            RectTransform viewport = null;
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                if (rt.name == "Viewport" && rt.parent != null && rt.parent.name.Contains("Pick"))
                { viewport = rt; break; }
            if (viewport == null)
                foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                    if (rt.name == "Viewport") { viewport = rt; break; }
            if (viewport == null) { Debug.LogError("[SkillScroll] 找不到Viewport"); return; }

            var scroll = viewport.GetComponent<ScrollRect>();
            if (scroll == null)
            {
                scroll = viewport.gameObject.AddComponent<ScrollRect>();
                Debug.Log("[SkillScroll] 已添加ScrollRect");
            }
            scroll.viewport = viewport;
            scroll.content = viewport.Find("Content") as RectTransform;
            if (scroll.content == null)
                foreach (var rt in viewport.GetComponentsInChildren<RectTransform>(true))
                    if (rt.name == "Content") { scroll.content = rt; break; }
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;

            Debug.Log("[SkillScroll] 配置完成 content=" + (scroll.content != null ? scroll.content.name : "NULL"));
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
