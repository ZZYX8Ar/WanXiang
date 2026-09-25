// ============================================================================
//  万相 · UI 补丁：滚动区受击层（全项目一次修）
//  ---------------------------------------------------------------------------
//  症状：面板里列表**划不动**（拖拽/滚轮没反应）。
//  真因：ScrollRect 自己不画任何 Graphic —— 它能滚是因为事件**冒泡**上来的，
//        而冒泡的前提是射线先命中某个 raycastTarget。Viewport 只有 RectMask2D，
//        没有图形 ⇒ 拖在卡片空白处 / 行间隙 / 底色上时**射线落空**，事件到不了 ScrollRect。
//        只有恰好按在子按钮/碎片格上才滚得动 —— 用户观感就是"划不动"。
//
//  修法：给每个 ScrollRect 的 Viewport 补一层 **全透明但可受击** 的 Image
//        （alpha = 0 不影响受击：Unity 只看 raycastTarget）。
//
//  ⚠ 增量补丁：只为**缺受击层**的面板动手，且动手前自动备份到 Assets 之外。
//  菜单：WanXiang/UI/补丁：修复所有面板滚动区受击层
// ============================================================================

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class UIBuildScrollRaycastPatch
    {
        private const string UiPrefabDir = "Assets/Resources/UI";

        [MenuItem("WanXiang/UI/补丁：修复所有面板滚动区受击层", priority = 111)]
        internal static void PatchAll()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabDir });
            if (guids == null || guids.Length == 0)
            {
                Debug.LogWarning("[ScrollPatch] " + UiPrefabDir + " 下没有 prefab");
                return;
            }

            string backupDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "_prefab_patch_backup_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm") + "_scroll"));

            int panels = 0, viewports = 0;
            var report = new List<string>();

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var probe = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (probe == null) continue;
                if (!NeedsFix(probe, out int missing)) continue;   // 只为有问题的动手

                Directory.CreateDirectory(backupDir);
                File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path) + ".bak"), true);

                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Debug.LogError("[ScrollPatch] 载入失败：" + path); continue; }
                try
                {
                    int n = 0;
                    foreach (var sr in root.GetComponentsInChildren<ScrollRect>(true))
                        if (FixViewport(sr)) n++;

                    if (n > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        panels++; viewports += n;
                        report.Add("  " + Path.GetFileName(path) + " → 补 " + n + " 个滚动区");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.Refresh();
            foreach (var l in report) Debug.Log("[ScrollPatch] " + l);
            Debug.Log("[ScrollPatch] 完成：修正 " + panels + " 个 prefab / " + viewports
                      + " 个滚动区。备份目录 → " + backupDir);
        }

        /// <summary>该 prefab 是否有 Viewport 缺受击层。</summary>
        private static bool NeedsFix(GameObject root, out int missing)
        {
            missing = 0;
            foreach (var sr in root.GetComponentsInChildren<ScrollRect>(true))
            {
                if (sr.viewport == null) continue;
                var img = sr.viewport.GetComponent<Image>();
                if (img == null || !img.raycastTarget) missing++;
            }
            return missing > 0;
        }

        /// <summary>给 Viewport 补/修受击层。返回是否改动了。</summary>
        private static bool FixViewport(ScrollRect sr)
        {
            if (sr.viewport == null) return false;
            var img = sr.viewport.GetComponent<Image>();
            if (img != null && img.raycastTarget) return false;

            if (img == null)
            {
                img = sr.viewport.gameObject.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);   // 全透明，但可受击
            }
            img.raycastTarget = true;
            return true;
        }
    }
}
#endif
