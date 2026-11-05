// ============================================================================
//  万相 · UI 预制体保护（2026-11-05）
//  ---------------------------------------------------------------------------
//  背景（用户报障）："我自己调整了一下面板，后面让你改某个面板就恢复原样了。"
//
//  本工程的 UI 预制体有三类工具，**危险性完全不同**：
//    ① 批量生成 `UIPanelPrefabBuilder`（菜单 万相/UI/生成面板预制体）
//       —— 17 个面板**从代码从零重建**。但 `UIBuild.SavePrefab` 里已加"文件已存在就跳过"，
//          所以它**不会再覆盖手工修改**（只对你删掉过的面板重建）。
//    ② 补丁类 `*Patch.cs`（UIBuildMarketPatch / PatchHomeBossRushButton / …）
//       —— 走 `PrefabUtility.LoadPrefabContents` → 只补缺 → 存回，**保留手改**。
//          ⚠ 唯一的坑：不幂等的补丁重复跑会给同节点加第二份子物体。
//    ③ 从零重建型 `BuildBossRushPrefab` / `BuildUnitInspectPrefab`
//       —— 直接 `SaveAsPrefabAsset` 覆盖同名文件 ⇒ **手改全丢**。
//
//  所以这里的职责只有一个：**任何"覆盖式保存"之前，先把现有 prefab 备份到工程外**，
//  并且把备份路径打在 Console 里。这样即使用户的手改被覆盖，也能一条命令拿回来。
//
//  ⛔ 不弹模态框（EditorUtility.DisplayDialog 会卡死 Play 模式 / MCP 调用）。
// ============================================================================

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class PrefabGuard
    {
        /// <summary>备份根目录（工程外，和美术备份放一起）。</summary>
        private const string BackupDir = @"D:\Unity_Project\MYRIAD\_backups\_prefab";

        /// <summary>
        /// 覆盖式保存：prefab 已存在 → 先备份到 `_backups/_prefab`，再存。
        /// <para>返回备份路径（没备份则为空串）。</para>
        /// </summary>
        public static string SaveWithBackup(GameObject root, string prefabPath)
        {
            string backup = Backup(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[PrefabGuard] 已保存 " + prefabPath + (string.IsNullOrEmpty(backup)
                ? "（原本不存在，无备份）" : "（旧版已备份 → " + backup + "）"));
            return backup;
        }

        /// <summary>把现有 prefab 复制到工程外；不存在则返回空串。</summary>
        public static string Backup(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath)) return "";
            try
            {
                if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);
                string name = Path.GetFileNameWithoutExtension(prefabPath) + ".prefab.bak_" +
                              DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string dst = Path.Combine(BackupDir, name);
                File.Copy(prefabPath, dst, true);
                return dst;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrefabGuard] 备份失败（继续保存，但手改可能丢失）：" + ex.Message);
                return "";
            }
        }

        /// <summary>手动快照：把 Resources/UI 下所有面板备份一份（动一批东西之前先点它）。</summary>
        [MenuItem("万相/UI/备份所有面板预制体", priority = 900)]
        public static void BackupAllPanels()
        {
            const string dir = "Assets/Resources/UI";
            if (!Directory.Exists(dir)) { Debug.LogWarning("[PrefabGuard] 没有 " + dir); return; }
            var files = Directory.GetFiles(dir, "Panel_*.prefab", SearchOption.TopDirectoryOnly);
            int n = 0;
            for (int i = 0; i < files.Length; i++)
                if (!string.IsNullOrEmpty(Backup(files[i].Replace('\\', '/')))) n++;
            AssetDatabase.Refresh();
            Debug.Log("[PrefabGuard] 已备份 " + n + " 个面板 → " + BackupDir);
        }

        /// <summary>列出最近 20 个备份，方便找回被覆盖的手改。</summary>
        [MenuItem("万相/UI/列出面板备份", priority = 901)]
        public static void ListBackups()
        {
            if (!Directory.Exists(BackupDir)) { Debug.Log("[PrefabGuard] 还没有备份"); return; }
            var files = Directory.GetFiles(BackupDir, "*.prefab.bak_*");
            Array.Sort(files, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[PrefabGuard] 备份（新 → 旧，最多 20 条）：");
            for (int i = 0; i < files.Length && i < 20; i++)
                sb.AppendLine("  " + Path.GetFileName(files[i]) + "   " +
                              File.GetLastWriteTime(files[i]).ToString("MM-dd HH:mm"));
            Debug.Log(sb.ToString());
        }
    }
}
