// ============================================================================
//  万相 · 战斗数值旋钮表（Core 侧只做注入，零引擎依赖）
//  ---------------------------------------------------------------------------
//  数值唯一源：`Resources/Data/battle_tuning.csv`（key,value）。
//  ⛔ 改倍率只改 CSV（Excel 另存 UTF-8），不要动这个文件、不要改代码。
//  解析在 UI 层 DataTables.LoadBattleTuning()；表缺失 ⇒ 全部回退 1f（正式数值）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class BattleTuning
    {
        private static float _playerDamageMul = 1f;
        // ★ 2026-11-18：敌方"同列威胁优先"开关（0=原版按位置，1=打同列攻击最高的）。
        //   只影响同一前排列内的 tie-break，坦克仍挡刀 —— 不改变前后排策略。
        private static bool _enemySmartTargeting = false;

        /// <summary>
        /// 玩家造成伤害倍率（普攻/技能/真伤/追击通用；敌方不受影响）。
        /// 来自 battle_tuning.csv 的 player_damage_mul 行，默认 1f。
        /// </summary>
        public static float PlayerDamageMul
        {
            get { return _playerDamageMul; }
            internal set { _playerDamageMul = value > 0f ? value : 1f; }
        }

        /// <summary>由 UI 层 DataTables 调用（唯一写入口）。</summary>
        public static bool EnemySmartTargeting
        {
            get { return _enemySmartTargeting; }
            internal set { _enemySmartTargeting = value; }
        }

        public static void Install(float playerDamageMul, bool enemySmartTargeting = false)
        {
            _playerDamageMul = playerDamageMul > 0f ? playerDamageMul : 1f;
            _enemySmartTargeting = enemySmartTargeting;
        }

        /// <summary>测试结束：重置为正式数值（表里没配时也走这个）。</summary>
        public static void ResetToDefaults()
        {
            _playerDamageMul = 1f;
            _enemySmartTargeting = false;
        }
    }
}
