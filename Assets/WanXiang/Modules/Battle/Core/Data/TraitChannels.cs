// ============================================================================
//  万相 · 效果通道（骨架 / B1 收尾）
//  ---------------------------------------------------------------------------
//  设计目标：模拟层**不认识任何具体天性**，只认识"通道"。
//
//    CSV 一行写 channel → TraitRuntime.Bonus(st, side, channel) → 挂点取值
//
//  于是：
//    · 改数值 = 改 CSV 的 value
//    · 删天性 = 删 CSV 行（通道没人贡献 ⇒ 自动为 0）
//    · 加天性 = CSV 一行（可绑到已有通道，无需改代码）
//    · 加全新机制 = 这里加一个常量 + 挂点一行 Bonus(...)
//
//  ⛔ 禁止在 BattleSimulator / BoardRules 里出现 `TraitKind.Xxx` 判断。
// ============================================================================
namespace WanXiang.Battle.Core
{
    public static class TraitChannels
    {
        public const string MpRegen = "mp_regen";                   // 回合灵力（天性 + 沉静共用一个通道）
        public const string CritRate = "crit_rate";                 // 暴击率加值
        public const string ExecuteDamage = "execute_damage";       // 残血目标伤害倍率
        public const string GuardDamage = "guard_damage";           // 对御职/带盾目标伤害倍率
        public const string RageDownOnCrit = "rage_down_on_crit";   // 暴击削怒气
        public const string AuraCap = "aura_cap";                   // 附着/灼烧上限加成
        public const string RegenMul = "regen_mul";                 // 相生回复倍率
        public const string RageOnHit = "rage_on_hit";              // 受击额外元气
        public const string QteWindow = "qte_window";               // QTE 判定窗口倍率
        public const string AdjacentPower = "adjacent_power";       // 相邻/孤高的属性修正

        // 尚无挂点（注册了但不生效，等 B1 收尾）
        public const string ShieldOnHurt = "shield_on_hurt";
        public const string DodgeAdjacentFoe = "dodge_adjacent_foe";
        public const string PoisonOnCrit = "poison_on_crit";

        /// <summary>
        /// 老数据兼容：CSV 没写 channel 时按 Kind 给一个默认通道。
        /// ⚠ 只是一张"兜底表"，不是硬编码清单 —— CSV 的 channel 列优先。
        /// </summary>
        public static string Of(TraitKind kind)
        {
            switch (kind)
            {
                case TraitKind.AdjacentMpBonus: return MpRegen;
                case TraitKind.BurnCapUp: return AuraCap;
                case TraitKind.BurnTakenUp: return ExecuteDamage;
                case TraitKind.CritRateTeam: return CritRate;
                case TraitKind.ShieldOnHurt: return ShieldOnHurt;
                case TraitKind.DodgeWhenAdjacentFoe: return DodgeAdjacentFoe;
                case TraitKind.DamageToGuardUp: return GuardDamage;
                case TraitKind.ExecuteLowHp: return ExecuteDamage;
                case TraitKind.KillBonusAtk: return AdjacentPower;
                case TraitKind.PoisonOnCrit: return PoisonOnCrit;
                case TraitKind.TeamShieldStart: return ShieldOnHurt;
                case TraitKind.ReactionDamageDown: return RegenMul;
                case TraitKind.RageDownOnCrit: return RageDownOnCrit;
                default: return null;
            }
        }
    }
}
