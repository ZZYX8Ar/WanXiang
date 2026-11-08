// ============================================================================
//  万相 · 编辑器工具 · Meta 面板加"道痕/技能"入口（S6）
//  ---------------------------------------------------------------------------
//  **增量改 prefab**（项目规矩：备份 → LoadPrefabContents → 只补缺 → 存回 → 核对）。
//  在 Panel_Meta 的 Root_TopBar 右上角加两个入口按钮：
//    Btn_DaoMark "道痕"  → OpenPanelAsync<DaoMarkPanel>()
//    Btn_Skills  "技能"  → OpenPanelAsync<SkillLoadoutPanel>()
//  已存在则跳过（幂等）。按钮挂接在 MetaPanel.OnCreate 里（transform.Find）。
// ============================================================================
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class MetaPanelEntryBuilder
    {
        private const string PrefabPath = "Assets/Resources/UI/Panel_Meta.prefab";
        private const string KitDir = "Assets/Resources/UI/Kit/";
        private const string FontPath = "Assets/ArtRes/Fonts/Deng SDF.asset";

        [MenuItem("万相/UI/Meta 面板加 道痕/技能 入口（S6）")]
        public static void BuildFromMenu() => Build();

        public static string Build()
        {
            if (!File.Exists(PrefabPath)) return "Panel_Meta.prefab 不存在";

            // ---- 覆盖前备份到工程外 ----
            string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_backups", "ui");
            Directory.CreateDirectory(backupDir);
            File.Copy(PrefabPath, Path.Combine(backupDir,
                "Panel_Meta_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".prefab"), true);

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            string report;
            try
            {
                var top = root.transform.Find("Root_TopBar");
                if (top == null) { report = "找不到 Root_TopBar"; return report; }

                int added = 0;
                added += EnsureEntry(top, font, "Btn_DaoMark", "道痕", -64f);
                added += EnsureEntry(top, font, "Btn_Skills", "技能", -140f);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                report = "Panel_Meta 入口已补：新增 " + added + " 个（已存在则跳过）";
                Debug.Log("[MetaPanelEntry] " + report);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>只补缺：节点已存在就跳过（幂等）。</summary>
        private static int EnsureEntry(Transform top, TMP_FontAsset font, string name, string label, float x)
        {
            if (top.Find(name) != null) return 0;

            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(top, false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(96f, 52f);

            var img = go.AddComponent<Image>();
            img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(KitDir + "btn_secondary.png");
            img.type = Image.Type.Sliced;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var txt = new GameObject("Tmp_Label", typeof(RectTransform));
            var trt = (RectTransform)txt.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var t = txt.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = label;
            t.fontSize = 24;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.black;
            t.raycastTarget = false;
            return 1;
        }
    }
}
