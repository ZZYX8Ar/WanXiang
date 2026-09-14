// ============================================================================
//  万相 · 融合核心 · 融合命名
//  ---------------------------------------------------------------------------
//  GDD 6.2 给了命名样例：「句芒·霜魄」—— 宿主名 · 灵魂魄名。
//  命名是内容的一部分：它必须**确定**（同一对宿主灵魂永远同名），
//  所以不放任何随机 —— 魄名在灵魂派生时就已经定死（见 SoulForge）。
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Fusion
{
    public static class FusionNaming
    {
        /// <summary>融合名 = 宿主名 · 魂魄名。</summary>
        public static string Compose(BeastDef host, SoulDef soul)
        {
            string epithet = string.IsNullOrEmpty(soul.Epithet) ? "无名" : soul.Epithet;
            return $"{host.DisplayName}·{epithet}";
        }

        /// <summary>魂名（图鉴 / Roster 列表用）：来源异兽名 + 之魂。</summary>
        public static string SoulDisplayName(string sourceBeastName)
            => string.IsNullOrEmpty(sourceBeastName) ? "无名之魂" : sourceBeastName + "之魂";
    }
}
