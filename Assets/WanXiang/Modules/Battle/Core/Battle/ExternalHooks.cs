// ============================================================================
//  外部层回合始钩子（ExternalHooks）—— 2026-10-16 秘境接入用
//  ---------------------------------------------------------------------------
//  为什么需要它（分层纪律）：
//    `WanXiang.Battle.Core` 的 asmdef **只引用自己**，且 `noEngineReferences = true`。
//    所以战斗核心**不能**反过来调用 `WanXiang.Campaign`（秘境在那层）——
//    直接写 `WanXiang.Campaign.RealmRuntime...` 会得到 CS0234。
//
//  解法（与项目既有的 BossHook 同思路，但更轻）：
//    核心层只留一个**空的委托槽**，由上层在启动时填进去。
//    未注册 ⇒ `Hook` 为 null ⇒ 那一行 `Hook?.Invoke(st)` 什么都不做
//    ⇒ **零事件、零开销、指纹逐位不变**。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public static class ExternalHooks
    {
        /// <summary>
        /// 回合始的外部钩子（秘境场地效果等）。
        /// ⛔ **只能注册一个**：需要多个就自己串起来（见 Campaign 侧的注册方法）。
        /// ⚠ 注册方负责保证幂等 —— 重复注册会覆盖而不是叠加。
        /// </summary>
        public static System.Action<BattleState> TurnStart;

        /// <summary>回合末的外部钩子（留作后续扩展，当前无人使用）。</summary>
        public static System.Action<BattleState> TurnEnd;

        /// <summary>安全调用（判空短路 = 零开销）。</summary>
        public static void RunTurnStart(BattleState st)
        {
            var h = TurnStart;
            if (h != null) h(st);
        }

        public static void RunTurnEnd(BattleState st)
        {
            var h = TurnEnd;
            if (h != null) h(st);
        }
    }
}