// ============================================================================
//  万相 · 工坊面板最小修复（2026-11-22）
//  ---------------------------------------------------------------------------
//  背景：2026-11-21 全量重排工坊三栏失败（stretch anchor 孙节点被父尺寸变化带乱），
//  已回滚。此补丁只做一件 100% 安全的事：
//    补一个全屏宣纸底图，挡住底下节点地图透出（实机截图确认的最大痛点）
//  ⚠ 绝不碰任何现有节点的 anchor/sizeDelta/位置 —— 那些 stretch 节点互相依赖，动了就乱。
//  遵循硬规矩：工程外备份已有 → LoadPrefabContents → 只补缺 → 存回。幂等。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class FixForgeMinimalPatch
    {
        private const string Path = "Assets/Resources/UI/Panel_Forge.prefab";

        [MenuItem("万相/UI/最小修：工坊底图+返回（2026-11-22）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            if (root == null) { Debug.LogError("[ForgeMin] 加载失败"); return; }
            try
            {
                // ① 补全屏宣纸底图（幂等）—— 只新增，不动任何现有节点
                if (root.transform.Find("Img_ForgeDim") == null)
                {
                    var dim = new GameObject("Img_ForgeDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    dim.transform.SetParent(root.transform, false);
                    dim.transform.SetAsFirstSibling();   // 最底层
                    var rt = (RectTransform)dim.transform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                    var img = dim.GetComponent<Image>();
                    img.color = new Color(0.94f, 0.92f, 0.86f, 1f);
                    img.raycastTarget = false;
                    Debug.Log("[ForgeMin] 已补底图 Img_ForgeDim");
                }
                else Debug.Log("[ForgeMin] 底图已存在，跳过");

                // ② 不动返回按钮：UIPanelChrome 会统一隐藏各面板的 Btn_Back，
                //    改用右上角统一 X（截图确认可见、可点回地图）。prefab 层恢复它无效，属误判。

                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("[ForgeMin] 工坊底图补全完成（不动其他节点）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
