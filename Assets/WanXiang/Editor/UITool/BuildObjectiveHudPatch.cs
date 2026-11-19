// ============================================================================
//  万相 · 战斗目标 HUD 增量补丁（2026-11-19）
//  ---------------------------------------------------------------------------
//  严格遵循项目 prefab 硬规矩：
//  工程外备份 → LoadPrefabContents → 只补缺(Ensure) → SerializedObject 重绑 → 存回
//  ⚠ 幂等：已存在节点一律不动，保留用户手改的美术。
// ============================================================================
#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class BuildObjectiveHudPatch
    {
        private const string CampaignPath = "Assets/Resources/UI/Panel_Campaign.prefab";
        private const string BattlePath = "Assets/Resources/UI/Panel_Battle.prefab";

        [MenuItem("WanXiang/UI/补丁：战斗目标 HUD（节点地图+战斗）", priority = 105)]
        internal static void Patch()
        {
            var font = UIBuild.Font;
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ArtRes/Fonts/Deng SDF.asset");
            UIBuild.SetFont(font);

            PatchOne(CampaignPath, true);
            PatchOne(BattlePath, false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ObjectiveHudPatch] 全部完成");
        }

        private static void PatchOne(string path, bool isCampaign)
        {
            if (!System.IO.File.Exists(path)) { Debug.LogError("[ObjectiveHudPatch] 找不到 " + path); return; }
            Backup(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { Debug.LogError("[ObjectiveHudPatch] 载入失败 " + path); return; }
            try
            {
                if (isCampaign) EnsureCampaign(root);
                else EnsureBattle(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[ObjectiveHudPatch] OK " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ---- 节点地图：节点卡右上角图标 + 信息面板目标行 ----
        private static void EnsureCampaign(GameObject root)
        {
            // ① 节点卡模板 Item_Node 内加目标图标（右上角）
            var itemNode = FindDeep(root.transform, "Item_Node");
            if (itemNode != null)
            {
                if (FindDeep(itemNode, "Img_TargetIcon") == null)
                {
                    var icon = NewNode(itemNode, "Img_TargetIcon");
                    var rt = (RectTransform)icon.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-8f, -8f);
                    rt.sizeDelta = new Vector2(48f, 48f);
                    var img = icon.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.color = new Color(0.90f, 0.72f, 0.32f, 1f);   // 目标金，实例化后按 icon 换 sprite
                    icon.SetActive(false);   // 默认隐藏，无目标的节点不显示
                    Debug.Log("[ObjectiveHudPatch] + Item_Node/Img_TargetIcon");
                }
                // ② Tmp_NodeText 右内边距加大，给图标让位（原 offsetMax.x=-20 → -104）
                var txt = FindDeep(itemNode, "Tmp_NodeText");
                if (txt != null)
                {
                    var trt = (RectTransform)txt;
                    if (trt.offsetMax.x > -100f)
                    {
                        trt.offsetMax = new Vector2(-104f, trt.offsetMax.y);
                        Debug.Log("[ObjectiveHudPatch] Tmp_NodeText 右内边距让位 → -104");
                    }
                }
            }

            // ③ 节点信息面板 Root_NodeInfo 内加目标说明行
            var info = FindDeep(root.transform, "Root_NodeInfo");
            if (info != null && FindDeep(info, "Tmp_NodeGoal") == null)
            {
                var t = NewText(info, "Tmp_NodeGoal", "额外目标：无", 20f,
                                TextAlignmentOptions.Left, new Color(0.36f, 0.27f, 0.16f, 1f));
                var rt = (RectTransform)t.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -232f);
                rt.sizeDelta = new Vector2(-32f, 76f);
                t.gameObject.SetActive(false);   // 无目标时隐藏
                Debug.Log("[ObjectiveHudPatch] + Root_NodeInfo/Tmp_NodeGoal");
            }

            Bind(root, isCampaign: true);
        }

        // ---- 战斗界面：天时面板下方目标条 ----
        private static void EnsureBattle(GameObject root)
        {
            if (FindDeep(root.transform, "Root_TargetPanel") != null)
            {
                Debug.Log("[ObjectiveHudPatch] Root_TargetPanel 已存在，跳过（保留手改）");
                Bind(root, isCampaign: false);
                return;
            }
            var panel = NewNode(root.transform, "Root_TargetPanel");
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -104f);
            rt.sizeDelta = new Vector2(900f, 56f);

            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.98f, 0.95f, 0.88f, 0.92f);
            bg.raycastTarget = false;

            var t = NewText(panel.transform, "Tmp_TargetText", "额外目标：无", 22f,
                            TextAlignmentOptions.Center, new Color(0.36f, 0.27f, 0.16f, 1f));
            Fill(t, l: 16f, r: 16f);
            panel.SetActive(false);   // 无目标时隐藏
            Debug.Log("[ObjectiveHudPatch] + Root_TargetPanel/Tmp_TargetText");
            Bind(root, isCampaign: false);
        }

        private static void Bind(GameObject root, bool isCampaign)
        {
            var panel = root.GetComponent<Framework.UI.UIPanelBase>();
            if (panel == null) { Debug.LogWarning("[ObjectiveHudPatch] 根节点无 UIPanelBase，跳过绑定"); return; }
            var so = new SerializedObject(panel);
            if (isCampaign)
            {
                var itemNode = FindDeep(root.transform, "Item_Node");
                var infoRoot = FindDeep(root.transform, "Root_NodeInfo");
                var icon = itemNode != null ? FindDeep(itemNode, "Img_TargetIcon") : null;
                var goal = infoRoot != null ? FindDeep(infoRoot, "Tmp_NodeGoal") : null;
                SetRef(so, "_targetIconTemplate", icon != null ? icon.gameObject : null);
                SetRef(so, "_tmpNodeGoal", goal != null ? goal.GetComponent<TMP_Text>() : null);
            }
            else
            {
                var rootP = FindDeep(root.transform, "Root_TargetPanel");
                var txt = rootP != null ? FindDeep(rootP, "Tmp_TargetText") : null;
                SetRef(so, "_rootTargetPanel", rootP != null ? rootP.gameObject : null);
                SetRef(so, "_tmpTargetText", txt != null ? txt.GetComponent<TMP_Text>() : null);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[ObjectiveHudPatch] 绑定完成 " + (isCampaign ? "Campaign" : "Battle"));
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[ObjectiveHudPatch] 字段不存在：" + field); return; }
            p.objectReferenceValue = value;
        }

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
                Debug.Log("[ObjectiveHudPatch] 备份 → " + dst);
            }
            catch (System.Exception ex) { Debug.LogWarning("[ObjectiveHudPatch] 备份失败：" + ex.Message); }
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
            tmp.text = text; tmp.fontSize = size; tmp.alignment = align; tmp.color = color;
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
