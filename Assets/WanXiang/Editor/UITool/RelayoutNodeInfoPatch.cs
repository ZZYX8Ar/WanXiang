// ============================================================================
//  万相 · 节点信息浮层重排（2026-11-21）
//  ---------------------------------------------------------------------------
//  修 P0：Root_NodeInfo 内 6 组文字互相重叠（横跨anchor + 负sizeDelta导致）。
//  遵循 prefab 硬规矩：工程外备份已做 → LoadPrefabContents → 只改几何 → 存回 → 核对。
//  结构：定点anchor(0.5,1) + 正sizeDelta + 明确y偏移，纵向顺序排列。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class RelayoutNodeInfoPatch
    {
        private const string CampaignPath = "Assets/Resources/UI/Panel_Campaign.prefab";

        [MenuItem("万相/UI/重排：节点信息浮层（2026-11-21）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(CampaignPath);
            if (root == null) { Debug.LogError("[NodeInfoRelayout] 加载 prefab 失败"); return; }
            try
            {
                var info = root.transform.Find("Root_NodeInfo") as RectTransform;
                if (info == null) { Debug.LogError("[NodeInfoRelayout] 找不到 Root_NodeInfo"); return; }

                // 布局参数：相对浮层顶部(0.5,1) 的 y 偏移与尺寸，纵向顺序排列零重叠
                // 图标 140×140 → 节点名 → 类型 → 天气 → 额外目标 → 按钮
                // ★ 2026-11-21 二次修正：实测 preferredHeight ——
                // 节点名字号34→26（原10字prefH=272撑爆54高）；天时字号24→18、高40→130（原60字prefH=109）。
                // 只改 RectTransform 不够，字号也必须匹配实际文案长度，否则 TMP Overflow 会溢出压住下方。
                Fix(info, "Img_NodeIcon",      new Vector2(0f, -90f),  new Vector2(140f, 140f));
                Fix(info, "Tmp_NodeName",      new Vector2(0f, -200f), new Vector2(412f, 60f));
                Fix(info, "Tmp_NodeType",      new Vector2(0f, -265f), new Vector2(412f, 40f));
                Fix(info, "Tmp_Weather",       new Vector2(0f, -360f), new Vector2(412f, 130f));
                Fix(info, "Tmp_NodeGoal",      new Vector2(0f, -480f), new Vector2(380f, 90f));
                SetFontSize(info, "Tmp_NodeName", 26);
                SetFontSize(info, "Tmp_Weather",  18);

                int changed = 0;
                foreach (var name in new[] { "Img_NodeIcon", "Tmp_NodeName", "Tmp_NodeType", "Tmp_Weather", "Tmp_NodeGoal" })
                {
                    var n = info.Find(name) as RectTransform;
                    if (n != null) changed++;
                    else Debug.LogWarning("[NodeInfoRelayout] 缺节点：" + name);
                }

                PrefabUtility.SaveAsPrefabAsset(root, CampaignPath);
                Debug.Log("[NodeInfoRelayout] 完成，重排 " + changed + "/5 个节点（定点anchor+正尺寸）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetFontSize(RectTransform parent, string name, int size)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null) return;
            var tmp = rt.GetComponent<TMPro.TMP_Text>();
            if (tmp != null) tmp.fontSize = size;
        }

        private static void Fix(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);     // 与锚点一致，避免偏半个控件
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
#endif
