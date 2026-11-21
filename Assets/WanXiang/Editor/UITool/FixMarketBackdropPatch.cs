// ============================================================================
//  万相 · 市集面板补背景底（2026-11-21）
//  ---------------------------------------------------------------------------
//  实机玩（真实流程：地图垫底+市集）截图确认：市集没有全屏底图，节点地图的
//  文字/立绘/按钮从卡片缝隙与边缘透出，和市集内容叠在一起，画面很乱。
//  修法：只补一个全屏宣纸底图（最底层），不动任何现有节点。遵循 prefab 硬规矩。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class FixMarketBackdropPatch
    {
        private const string Path = "Assets/Resources/UI/Panel_Market.prefab";

        [MenuItem("万相/UI/补底：市集面板背景（2026-11-21）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            if (root == null) { Debug.LogError("[MarketBackdrop] 加载失败"); return; }
            try
            {
                if (root.transform.Find("Img_MarketDim") == null)
                {
                    var dim = new GameObject("Img_MarketDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    dim.transform.SetParent(root.transform, false);
                    dim.transform.SetAsFirstSibling();

                    var rt = (RectTransform)dim.transform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;

                    var img = dim.GetComponent<Image>();
                    img.color = new Color(0.94f, 0.92f, 0.86f, 1f);   // 宣纸色不透明，遮住底下地图
                    img.raycastTarget = false;
                }

                // ★ 顺手修：顶栏"刷新（本幕首次免费）"框宽240 vs 文字需260 → "费）"换行孤字
                var refresh = root.transform.Find("Root_TopBar/Tmp_RefreshCost") as RectTransform;
                if (refresh != null)
                {
                    refresh.sizeDelta = new Vector2(290f, refresh.sizeDelta.y);
                    Debug.Log("[MarketBackdrop] 刷新文字框加宽 240→290");
                }

                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("[MarketBackdrop] 已补全屏宣纸底图 Img_MarketDim");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
