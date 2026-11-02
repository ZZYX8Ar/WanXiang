// ============================================================================
//  万相 · 养成曲线（可外置）
//  ---------------------------------------------------------------------------
//  2026-11-02：把散在 RunState / BattleRequestFactory / CampaignPanel 的成长数值
//  集中到这里，允许 Resources/Data/progression.csv 覆盖。
//
//  ⛔ 为什么值得集中：
//    原来「+6% 攻击/级」写在 RunState.LevelAtkMul，同一条公式又在 BattleRequestFactory
//    里抄了一遍 `1f + 0.06f * (lv-1)` —— 改一处漏一处，玩家看到的面板和实际战斗就会不一致。
//    现在两边都读这里，公式只有一个来源。
//
//  ⛔ 默认值必须与 2026-11-02 的硬编码值逐项一致（改了 = 改了所有存档的成长手感）。
// ============================================================================

namespace WanXiang.Run
{
    public static class ProgressionTuning
    {
        // ---- 共享主等级 ----
        public static int LevelMax = 20;
        /// <summary>Lv1→2 所需经验。</summary>
        public static int ExpBase = 300;
        /// <summary>每升一级额外需要的经验（线性）。</summary>
        public static int ExpStep = 180;
        /// <summary>每级攻击加成（0.06 = +6%/级）。</summary>
        public static float LevelAtk = 0.06f;
        /// <summary>每级生命加成。</summary>
        public static float LevelHp = 0.07f;

        // ---- 专精 ----
        public static int MasteryCapNormal = 2;
        public static int MasteryCapEvolved = 3;
        /// <summary>专精 Lv1 的技能倍率。</summary>
        public static float MasteryLv1Mul = 1.12f;
        /// <summary>专精 Lv3 的额外倍率（与 Lv1 叠乘 = 1.344）。</summary>
        public static float MasteryLv3Mul = 1.20f;

        // ---- 进化 ----
        /// <summary>进化后的基础攻/血倍率。</summary>
        public static float EvolveStatMul = 1.10f;
        /// <summary>进化消耗的素材数。</summary>
        public static int EvolveMaterialCost = 1;

        // ---- 结算 ----
        /// <summary>通关后阵亡单位进下一场的血量百分比。</summary>
        public static int ReviveHpPct = 50;
        public static int ExpWinNormal = 100;
        public static int ExpWinElite = 250;
        public static int ExpWinRealm = 180;
        public static int ExpWinBoss = 500;
        /// <summary>精英战掉落的进化素材。</summary>
        public static int MaterialWinElite = 1;
        /// <summary>幕末首领掉落的进化素材。</summary>
        public static int MaterialWinBoss = 2;

        /// <summary>已从外置表安装的键数（0 = 全部用内置值）。</summary>
        public static int InstalledKeys { get; private set; }

        /// <summary>
        /// 按 key 写一个值（parser 逐行调用）。返回 false = 未知 key（由调用方报行号）。
        /// ⛔ 加减字段时必须同步 DataTableExporter.ExportProgression 与 万相_数据表_说明.md。
        /// </summary>
        public static bool TrySet(string key, float v)
        {
            switch (key)
            {
                case "level_max": LevelMax = (int)v; break;
                case "exp_base": ExpBase = (int)v; break;
                case "exp_step": ExpStep = (int)v; break;
                case "level_atk": LevelAtk = v; break;
                case "level_hp": LevelHp = v; break;

                case "mastery_cap": MasteryCapNormal = (int)v; break;
                case "mastery_cap_evolved": MasteryCapEvolved = (int)v; break;
                case "mastery_lv1_mul": MasteryLv1Mul = v; break;
                case "mastery_lv3_mul": MasteryLv3Mul = v; break;

                case "evolve_stat_mul": EvolveStatMul = v; break;
                case "evolve_material_cost": EvolveMaterialCost = (int)v; break;

                case "revive_hp_pct": ReviveHpPct = (int)v; break;
                case "exp_win_normal": ExpWinNormal = (int)v; break;
                case "exp_win_elite": ExpWinElite = (int)v; break;
                case "exp_win_realm": ExpWinRealm = (int)v; break;
                case "exp_win_boss": ExpWinBoss = (int)v; break;
                case "material_win_elite": MaterialWinElite = (int)v; break;
                case "material_win_boss": MaterialWinBoss = (int)v; break;

                default: return false;
            }
            InstalledKeys++;
            return true;
        }
    }
}
