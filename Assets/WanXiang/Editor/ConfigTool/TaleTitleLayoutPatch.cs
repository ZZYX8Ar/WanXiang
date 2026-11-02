// ============================================================================
//  WanXiang · Panel_Tale 标题版式修正（一次性补丁，可重跑）
//  ---------------------------------------------------------------------------
//  菜单：万相 / 工具 / 修 Panel_Tale 标题版式
//
//  问题（2026-11-02 用户截图）：标题 `Tmp_Title` 是 `Img_TitleScroll` 的**子节点**，
//  两者正好重叠 —— 卷轴中央有一条花纹带横穿"扎营"两个字，36 号字又偏细，
//  1920 下读不清。
//
//  做法：把 `Tmp_Title` 从卷轴里搬出来，挂到面板根下、放在卷轴**正下方**的空白纸面上，
//  44 号 + 墨色。卷轴保留为纯装饰。
//
//  ⛔ 只动这一个节点的层级/位置/字号，其它一律不碰（增量补丁，不重建）。
// ============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WanXiang.EditorTools
{
    public static class TaleTitleLayoutPatch
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Tale.prefab";
        private const string BackupDir = @"D:\Unity_Project\MYRIAD\_backups";

        [MenuItem("万相/工具/修 Panel_Tale 标题版式", priority = 300)]
        public static void Patch()
        {
            if (!File.Exists(PrefabPath)) { Debug.LogError("[TalePatch] 找不到 " + PrefabPath); return; }

            // 1) 先备份到工程外（项目规矩）
            if (Directory.Exists(BackupDir))
            {
                string name = "Panel_Tale.prefab.bak_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(PrefabPath, Path.Combine(BackupDir, name), true);
                Debug.Log("[TalePatch] 备份 → " + name);
            }

            // 2) 增量改
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var scroll = root.transform.Find("Img_TitleScroll");
                var title = scroll != null ? scroll.Find("Tmp_Title") : null;
                if (title == null) { Debug.LogWarning("[TalePatch] 已经是新结构（没找到卷轴里的标题），跳过"); return; }

                var rt = (RectTransform)title;
                rt.SetParent(root.transform, false);

                // 顶部居中锚点，位置放在卷轴下方（卷轴 y=-80、高 120 ⇒ 其下沿约 -140）
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -210f);
                rt.sizeDelta = new Vector2(920f, 80f);

                var tmp = title.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    tmp.fontSize = 44f;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = new Color(0.16f, 0.13f, 0.10f, 1f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                Debug.Log("[TalePatch] 保存 " + (ok ? "成功" : "失败"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.Refresh();
        }
    }
}
