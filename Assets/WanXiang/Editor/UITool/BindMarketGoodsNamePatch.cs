// ============================================================================
//  万相 · 市集商品名绑定补丁（2026-11-21）
//  ---------------------------------------------------------------------------
//  实机玩发现商品卡显示"Goods 0"占位 —— 代码新增 _tmpGoodsName 字段但 prefab 未绑定。
//  遵循 prefab 硬规矩：工程外备份已做 → LoadPrefabContents → 只补绑定 → 存回 → 核对。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class BindMarketGoodsNamePatch
    {
        private const string Path = "Assets/Resources/UI/Panel_Market.prefab";

        [MenuItem("万相/UI/补绑：市集商品名（2026-11-21）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            if (root == null) { Debug.LogError("[MarketBind] 加载失败"); return; }
            try
            {
                var panel = root.GetComponent<WanXiang.Modules.UI.MarketPanel>();
                if (panel == null) { Debug.LogError("[MarketBind] 无 MarketPanel 组件"); return; }

                var so = new SerializedObject(panel);
                var prop = so.FindProperty("_tmpGoodsName");
                if (prop == null) { Debug.LogError("[MarketBind] 找不到 _tmpGoodsName 字段"); return; }

                int bound = 0;
                for (int i = 0; i < 6; i++)
                {
                    var t = root.transform.Find("Root_Goods/Goods_" + i + "/Tmp_GoodsName_" + i);
                    if (t == null) { Debug.LogWarning("[MarketBind] 缺 Tmp_GoodsName_" + i); continue; }
                    var text = t.GetComponent<TMPro.TMP_Text>();
                    if (text == null) continue;
                    if (i >= prop.arraySize) prop.arraySize = i + 1;
                    prop.GetArrayElementAtIndex(i).objectReferenceValue = text;
                    bound++;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("[MarketBind] 商品名绑定完成 " + bound + "/6");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
