// ============================================================================
//  敌方遭遇（EncounterDef）—— 回合制 v2.1 P4
//  ---------------------------------------------------------------------------
//  一个遭遇 = 敌方阵容 + AI 策略组。
//  当前用**代码内置表**（加遭遇只加一行数据）；后续要策划可配时，
//  把 For() 换成读 ScriptableObject 资产即可，调用方不变（与 PassiveCatalog 同法）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public sealed class EncounterDef
    {
        public string Id;              // 稳定标识（存档/节点地图引用它）
        public string Title;           // 展示名
        public string Note;            // 设计说明
        /// <summary>敌方阵容：ContentLibrary 的异兽下标，按 Cells 顺序摆放。</summary>
        public int[] EnemyIndices;
        /// <summary>这队敌人的 AI 打法。</summary>
        public AiProfile Profile;
    }

    public static class EncounterCatalog
    {
        private static readonly EncounterDef[] Table =
        {
            new EncounterDef
            {
                Id = "enc_grove", Title = "林间游荡",
                Note = "初遇：均衡的巡逻队，教会玩家基本节奏",
                EnemyIndices = new[] { 0, 1 },
                Profile = AiProfile.Balanced,
            },
            new EncounterDef
            {
                Id = "enc_pack", Title = "兽群突袭",
                Note = "激进：扑上来就把资源全打出去，考验玩家的前排硬度",
                EnemyIndices = new[] { 2, 3 },
                Profile = AiProfile.Aggressive,
            },
            new EncounterDef
            {
                Id = "enc_turtle", Title = "龟壳阵",
                Note = "稳健：能拖就拖，逼玩家主动进攻（后排暴露给切后排的技能）",
                EnemyIndices = new[] { 4, 5 },
                Profile = AiProfile.Cautious,
            },
        };

        public static EncounterDef For(string id)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return Table[0];          // 找不到就用初遇（不崩，给日志里能看到的现象）
        }

        public static System.Collections.Generic.IReadOnlyList<EncounterDef> All => Table;
    }
}
