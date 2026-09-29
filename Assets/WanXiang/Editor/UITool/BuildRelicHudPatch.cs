// ============================================================================
//  万相 · UI 补丁：遗物 HUD（节点地图 + 战斗面板左上角遗物条 + 悬浮说明）
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 补丁：遗物 HUD（节点地图+战斗）
//
//  ⚠ 这是**增量补丁**，不是重建：
//    LoadPrefabContents → 只补缺失的节点（已存在则原样保留）→ 重绑字段 → 存回。
//    用户在这两个 prefab 上的一切手工调整（位置/配色/字体）都不会被动。
//  动手前先把原 prefab 备份到 **Assets 之外**（工程上级目录 _prefab_patch_backup_*）。
//
//  补的节点：
//    Root_Relics（HorizontalLayoutGroup，左上角）→ Item_Relic（模板，隐藏）→ Tmp_RelicName
//    Root_RelicTip（浮层，隐藏）→ Tmp_RelicTip
// ============================================================================

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.EditorTools
{
    public static class BuildRelicHudPatch
    {
        private const string CampaignPath = "Assets/Resources/UI/Panel_Campaign.prefab";
        private const string BattlePath = "Assets/Resources/UI/Panel_Battle.prefab";

        [MenuItem("WanXiang/UI/补丁：遗物 HUD（节点地图+战斗）", priority = 104)]
        internal static void Patch()
        {
            var font = UIBuild.Font;
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            UIBuild.SetFont(font);

            // 节点地图：顶部栏（96 高）之下、节点长卷（-140）之上的空档
            PatchOne(CampaignPath, new Vector2(24f, -118f), new Vector2(760f, 34f),
                     new Vector2(150f, 30f), 20f);
            // 战斗：顶部栏左侧（天时横幅居中、两侧留白）
            PatchOne(BattlePath, new Vector2(20f, -16f), new Vector2(440f, 44f),
                     new Vector2(150f, 36f), 22f);
        }

        private static void PatchOne(string path, Vector2 pos, Vector2 size, Vector2 chipSize, float fontSize)
        {
            if (!System.IO.File.Exists(path))
            {
                Debug.LogError("[RelicHudPatch] 找不到 " + path);
                return;
            }

            Backup(path);

            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError("[RelicHudPatch] 载入失败 " + path);
                return;
            }
            try
            {
                EnsureRelics(root, pos, size, chipSize, fontSize);
                EnsureTip(root, fontSize);
                Bind(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.Refresh();
                Debug.Log("[RelicHudPatch] OK " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- 备份到 Assets 之外（工程上级目录），不污染仓库 ----
        private static void Backup(string path)
        {
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    Application.dataPath, "..", "..",
                    "_prefab_patch_backup_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm")));
                System.IO.Directory.CreateDirectory(dir);
                string dst = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path) + ".bak");
                System.IO.File.Copy(path, dst, true);
                Debug.Log("[RelicHudPatch] 已备份 → " + dst);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RelicHudPatch] 备份失败（继续补丁）：" + e.Message);
            }
        }

        private static void EnsureRelics(GameObject root, Vector2 pos, Vector2 size, Vector2 chipSize, float fontSize)
        {
            var rel = FindDeep(root.transform, "Root_Relics");
            if (rel == null)
            {
                var go = NewNode(root.transform, "Root_Relics");
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = size;
                rt.anchoredPosition = pos;
                var hl = go.AddComponent<HorizontalLayoutGroup>();
                hl.childAlignment = TextAnchor.MiddleLeft;
                hl.spacing = 8f;
                hl.childControlWidth = true; hl.childControlHeight = true;
                hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
                rel = rt;
                Debug.Log("[RelicHudPatch] + Root_Relics");
            }

            if (FindDeep(rel, "Item_Relic") == null)
            {
                var chip = NewNode(rel, "Item_Relic");
                var crt = (RectTransform)chip.transform;
                crt.sizeDelta = chipSize;
                var le = chip.AddComponent<LayoutElement>();
                le.preferredWidth = chipSize.x; le.preferredHeight = chipSize.y;
                chip.AddComponent<Image>().color = new Color(0.86f, 0.82f, 0.74f, 1f);

                var lbl = NewText(chip.transform, "Tmp_RelicName", "遗物", fontSize,
                                  TextAlignmentOptions.Center, new Color(0.16f, 0.13f, 0.09f, 1f));
                Fill(lbl);
                chip.SetActive(false);
                Debug.Log("[RelicHudPatch] + Item_Relic");
            }
        }

        private static void EnsureTip(GameObject root, float fontSize)
        {
            if (FindDeep(root.transform, "Root_RelicTip") != null) return;

            var go = NewNode(root.transform, "Root_RelicTip");
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(420f, 156f);
            rt.anchoredPosition = new Vector2(24f, -160f);
            go.AddComponent<Image>().color = new Color(0.11f, 0.09f, 0.07f, 0.95f);

            var t = NewText(go.transform, "Tmp_RelicTip", "", fontSize,
                            TextAlignmentOptions.TopLeft, new Color(0.97f, 0.94f, 0.88f, 1f));
            Fill(t, 16f, 12f, 16f, 12f);
            t.enableWordWrapping = true;
            go.SetActive(false);
            Debug.Log("[RelicHudPatch] + Root_RelicTip");
        }

        private static void Bind(GameObject root)
        {
            var panel = root.GetComponent<UIPanelBase>();
            if (panel == null) { Debug.LogError("[RelicHudPatch] 根节点没有 UIPanelBase 派生脚本"); return; }

            var rel = FindDeep(root.transform, "Root_Relics");
            var chip = FindDeep(root.transform, "Item_Relic");
            var tip = FindDeep(root.transform, "Root_RelicTip");
            var tipTxt = FindDeep(root.transform, "Tmp_RelicTip");

            var so = new SerializedObject(panel);
            SetRef(so, "_rootRelics", rel);
            SetRef(so, "_relicTemplate", chip != null ? chip.gameObject : null);
            SetRef(so, "_relicTipRoot", tip != null ? tip.gameObject : null);
            SetRef(so, "_relicTipText", tipTxt != null ? tipTxt.GetComponent<TMP_Text>() : null);
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[RelicHudPatch] 字段绑定：_rootRelics/_relicTemplate/_relicTipRoot/_relicTipText");
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[RelicHudPatch] 字段不存在：" + field); return; }
            p.objectReferenceValue = value;
        }

        // ---- 小工具 ----
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

        private static GameObject NewNode(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, string text, float size,
                                               TextAlignmentOptions align, Color color)
        {
            var go = NewNode(parent, name);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            if (UIBuild.Font != null) tmp.font = UIBuild.Font;
            return tmp;
        }

        private static void Fill(TextMeshProUGUI t, float l = 0, float b = 0, float r = 0, float tt = 0)
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -tt);
        }
    }
}
#endif
