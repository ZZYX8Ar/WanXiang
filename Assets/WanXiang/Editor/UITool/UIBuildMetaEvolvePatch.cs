// ============================================================================
//  万相 · UI 补丁：异兽培养（Panel_Meta）的「进化条件」行
//  ---------------------------------------------------------------------------
//  问题：Tmp_EvolveCond 挂在 Root_Detail 下，却是**底部锚点 + pos.y = -706**
//        ⇒ 整行跑到卡片下方 706px，**永远看不见**（生成器里本该是 96..148）。
//
//  本补丁：把它挪回「底排按钮上方」的位置，并顺手保证它可受击/可见性正常。
//  ⚠ 增量补丁 + 动手前自动备份到 Assets 之外。
//  菜单：WanXiang/UI/补丁：异兽培养的进化条件行
// ============================================================================

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class UIBuildMetaEvolvePatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Meta.prefab";
        private const string NodeName = "Tmp_EvolveCond";

        // Root_Detail 内、底排按钮（y 20..88）上方
        private static readonly Vector2 OffMin = new Vector2(28f, 96f);
        private static readonly Vector2 OffMax = new Vector2(-24f, 148f);

        [MenuItem("WanXiang/UI/补丁：异兽培养的进化条件行", priority = 112)]
        internal static void Patch()
        {
            if (!File.Exists(PrefabPath)) { Debug.LogError("[MetaEvolvePatch] 找不到 " + PrefabPath); return; }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "_prefab_patch_backup_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm") + "_meta"));
            Directory.CreateDirectory(dir);
            string bak = Path.Combine(dir, Path.GetFileName(PrefabPath) + ".bak");
            File.Copy(PrefabPath, bak, true);
            Debug.Log("[MetaEvolvePatch] 已备份 → " + bak);

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null) { Debug.LogError("[MetaEvolvePatch] 载入失败"); return; }
            try
            {
                var t = FindDeep(root.transform, NodeName) as RectTransform;
                if (t == null) { Debug.LogError("[MetaEvolvePatch] 找不到节点 " + NodeName); return; }

                Debug.Log("[MetaEvolvePatch] 修正前：anchor=" + t.anchorMin + "/" + t.anchorMax
                          + " pos=" + t.anchoredPosition + " sizeDelta=" + t.sizeDelta);

                // ★ 底部锚点 + offsetMin/offsetMax：与生成器 UIBuildMetaPanel 保持一致
                t.anchorMin = new Vector2(0f, 0f);
                t.anchorMax = new Vector2(1f, 0f);
                t.pivot = new Vector2(0.5f, 0.5f);
                t.offsetMin = OffMin;
                t.offsetMax = OffMax;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.Refresh();
                Debug.Log("[MetaEvolvePatch] OK：进化条件行已挪回底排按钮上方（y 96..148）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
