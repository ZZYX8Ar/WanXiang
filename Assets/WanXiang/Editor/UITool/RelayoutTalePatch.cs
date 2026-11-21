// ============================================================================
//  万相 · 异闻面板重排（2026-11-21）
//  ---------------------------------------------------------------------------
//  修 P0：标题压引文、正文压选项1、选项内三文本叠字。
//  遵循 prefab 硬规矩：工程外备份已做 → LoadPrefabContents → 只改几何 → 存回。
//  结构：六段纵向分区；每个选项内"选项文本 vs 花费"左右分区。
//  ⚠ TaleTitleLayoutPatch 是幂等的（标题已在根下会跳过），不会干扰本补丁。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class RelayoutTalePatch
    {
        private const string TalePath = "Assets/Resources/UI/Panel_Tale.prefab";

        [MenuItem("万相/UI/重排：异闻面板（2026-11-21）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(TalePath);
            if (root == null) { Debug.LogError("[TaleRelayout] 加载 prefab 失败"); return; }
            try
            {
                // 面板根 1240×940，中心(0,0)。各段相对根顶部(0.5,1)排列，纵向不重叠。
                // 标题(y=-95) → 引文(y=-165) → 正文(y=-290,高190) → 选项1/2/3 → 拒绝
                Fix(root.transform, "Img_TitleScroll",  new Vector2(0f, -78f),  new Vector2(1120f, 120f));
                Fix(root.transform, "Tmp_Title",        new Vector2(0f, -175f), new Vector2(920f, 60f));
                Fix(root.transform, "Tmp_ClassicQuote", new Vector2(0f, -240f), new Vector2(1100f, 52f));
                Fix(root.transform, "Tmp_StoryText",    new Vector2(0f, -365f), new Vector2(1100f, 170f));

                Fix(root.transform, "Opt_0", new Vector2(0f, -510f), new Vector2(1080f, 92f));
                Fix(root.transform, "Opt_1", new Vector2(0f, -614f), new Vector2(1080f, 92f));
                Fix(root.transform, "Opt_2", new Vector2(0f, -718f), new Vector2(1080f, 92f));

                Fix(root.transform, "Btn_Decline", new Vector2(0f, -876f), new Vector2(420f, 72f));

                // 每个选项内部：选项文本(左,宽900) 与 花费(右,宽150) 左右分区，不再全宽叠放
                for (int i = 0; i < 3; i++)
                {
                    var opt = root.transform.Find("Opt_" + i) as RectTransform;
                    if (opt == null) { Debug.LogWarning("[TaleRelayout] 缺 Opt_" + i); continue; }
                    FixChild(opt, "Tmp_OptionText", new Vector2(-75f, 0f), new Vector2(870f, 72f));
                    FixChild(opt, "Tmp_Cost",       new Vector2(455f, 0f), new Vector2(150f, 72f));
                    // Tmp_Label 是空的命名残留，隐藏它避免叠字
                    var label = opt.Find("Tmp_Label") as RectTransform;
                    if (label != null) { var go = label.gameObject; if (go.activeSelf) go.SetActive(false); }
                }

                PrefabUtility.SaveAsPrefabAsset(root, TalePath);
                Debug.Log("[TaleRelayout] 完成：六段纵向分区 + 选项左右分区");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Fix(Transform root, string name, Vector2 pos, Vector2 size)
        {
            var rt = root.Find(name) as RectTransform;
            if (rt == null) { Debug.LogWarning("[TaleRelayout] 缺节点：" + name); return; }
            Set(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), pos, size);
        }

        private static void FixChild(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null) return;
            Set(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
        }

        private static void Set(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
#endif
