// ============================================================================
//  万相 · 结算面板修复（2026-11-22）
//  ---------------------------------------------------------------------------
//  实机截图确认：Tmp_Drops 掉落文字与 Img_RewardEgg 灵卵图重叠（掉落文字 x≈1094~1434 y≈336~428）与 Img_RewardEgg（灵卵图
//      x≈1100~1420 y≈222~542）完全重叠 → 文字压在灵卵图上
//  修法：把掉落文字移到灵卵图上方空白区。
//  遵循硬规矩：工程外备份已有 → LoadPrefabContents → 只改几何/补缺 → 存回。
//  ⚠ 幂等：可重复运行。
// ============================================================================
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.EditorTools
{
    public static class FixResultPanelPatch
    {
        private const string Path = "Assets/Resources/UI/Panel_Result.prefab";

        [MenuItem("万相/UI/修复：结算面板底图+掉落文字（2026-11-22）")]
        public static void Patch()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            if (root == null) { Debug.LogError("[ResultFix] 加载失败"); return; }
            try
            {
                // ⚠ 底图不补：结算卡片[260,1660]外的透出，真实流程下是战斗全屏背景(山水图)，
                //   直接开结算看到的主界面按钮透出是测试假象。补底图会盖住战斗背景，画蛇添足。
                // 掉落文字移到灵卵图上方空白区（原位置与灵卵图重叠）
                //    灵卵图屏幕 y=[222,542]；移到其上方 y≈[560,652]，x 保持偏右对齐灵卵
                var drops = root.transform.Find("Tmp_Drops") as RectTransform;
                if (drops != null)
                {
                    // anchor(0.5,0) pivot(0.5,0)：anchoredPosition.y 是底边距屏幕底的高度
                    // 目标底边≈565（灵卵图顶542 + 余量），比原271高
                    drops.anchoredPosition = new Vector2(300f, 565f);
                    Debug.Log("[ResultFix] 掉落文字已移到灵卵图上方 y=565");
                }

                PrefabUtility.SaveAsPrefabAsset(root, Path);
                Debug.Log("[ResultFix] 掉落文字位置修复完成");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
