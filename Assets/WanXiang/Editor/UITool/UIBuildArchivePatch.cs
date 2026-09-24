// ============================================================================
//  万相 · UI 补丁：残卷阁（Panel_Archive）与结算面板（Panel_Result）
//  ---------------------------------------------------------------------------
//  背景：这两个面板的新控件（五行 tab / 网格卡片模板 / 剧情弹层 / 掉落明细）
//        必须落在 **prefab** 里（用户铁律：不做运行时构建 UI，否则美术改不了）。
//
//  本补丁是**增量**的（Ensure 语义：只补缺失的节点，已存在的保留其布局与配色），
//  并且**动手前先在工程外自动备份**（时间戳目录），符合协作铁律。
//
//  菜单：
//    WanXiang/UI/补丁：残卷阁（补齐卡面与剧情层）
//    WanXiang/UI/补丁：结算面板掉落行
// ============================================================================

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class UIBuildArchivePatch
    {
        private const string ArchivePath = "Assets/Resources/UI/Panel_Archive.prefab";
        private const string ResultPath = "Assets/Resources/UI/Panel_Result.prefab";

        [MenuItem("WanXiang/UI/补丁：残卷阁（补齐卡面与剧情层）", priority = 109)]
        internal static void PatchArchive()
        {
            Patch(ArchivePath, root =>
            {
                var comp = root.GetComponent<ArchivePanel>();
                if (comp == null) { Debug.LogError("[ArchivePatch] 根上没有 ArchivePanel 组件"); return; }
                UIPanelPrefabBuilder.EnsureArchiveNodes((RectTransform)root.transform, comp);
                Debug.Log("[ArchivePatch] 残卷阁节点已补齐并绑定（标题/tab/网格/卡片模板/剧情弹层/提示）");
            });
        }

        [MenuItem("WanXiang/UI/补丁：结算面板掉落行", priority = 110)]
        internal static void PatchResultDrops()
        {
            Patch(ResultPath, root =>
            {
                var comp = root.GetComponent<ResultPanel>();
                if (comp == null) { Debug.LogError("[ArchivePatch] 根上没有 ResultPanel 组件"); return; }
                UIPanelPrefabBuilder.EnsureResultDropsNode((RectTransform)root.transform, comp);
                Debug.Log("[ArchivePatch] 结算面板已补 Tmp_Drops（跳过/确认之间的空档）并绑定 _tmpDrops");
            });
        }

        private static void Patch(string path, System.Action<GameObject> apply)
        {
            if (!File.Exists(path)) { Debug.LogError("[ArchivePatch] 找不到 " + path); return; }

            // ★ 铁律：动手前先备份到**工程外**（Assets 之外，Unity 不会收编）
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "_prefab_patch_backup_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm")));
            Directory.CreateDirectory(dir);
            string bak = Path.Combine(dir, Path.GetFileName(path) + ".bak");
            File.Copy(path, bak, true);
            Debug.Log("[ArchivePatch] 已备份 → " + bak);

            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogError("[ArchivePatch] 载入 prefab 失败：" + path); return; }
            try
            {
                apply(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.Refresh();
                Debug.Log("[ArchivePatch] OK 已补丁 " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
