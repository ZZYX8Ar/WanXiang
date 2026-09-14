// ============================================================================
//  万相 · 融合 · 内容目录
//  ---------------------------------------------------------------------------
//  一份目录资产指着全部异兽 / 灵魂配置。导入器每次导入后重建这份资产；
//  自检与预览窗口读它，运行期（走资源系统）也只认它 —— 单一入口。
// ============================================================================

using UnityEngine;

namespace WanXiang.Fusion
{
    [CreateAssetMenu(fileName = "ContentCatalog", menuName = "万相/融合/内容目录")]
    public sealed class ContentCatalogSO : ScriptableObject
    {
        public BeastConfigSO[] Beasts;
        public SoulConfigSO[] Souls;
    }
}
