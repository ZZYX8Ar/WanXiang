// ============================================================================
//  万相 · 天性运行时（骨架 / B0）
//  ---------------------------------------------------------------------------
//  设计目标：**新增或删除一种天性，只动 CSV + 一行 Register，不改模拟层**。
//
//  机制：
//    · 每个 TraitKind 注册到一个 TraitHook（触发点）。
//    · 模拟层在挂点调用 TraitRuntime.Fire(hook, ref ctx)，由注册表派发。
//    · 未注册的 Kind 自动忽略（删掉 CSV 行也不会报错、不会崩）。
//    · 未知枚举值按 None 处理（老存档读到新版枚举不抛异常）。
//
//  ⚠ B0 阶段模拟层**还没有调用 Fire** ⇒ 行为零变化（自检指纹不变）。
//    B1 才在各挂点接上；届时只需加 `Fire(TraitHook.Xxx, ref ctx)` 一行。
//
//  ⛔ 本文件位于 Battle.Core（noEngineReferences），不得引用 UnityEngine / TextAsset。
// ============================================================================
using System;
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>天性种类。**只追加、不插入**（枚举值落进存档）。</summary>
    public enum TraitKind
    {
        None = 0,
        AdjacentMpBonus = 1,   // 相生相邻额外灵力
        BurnCapUp = 2,         // 灼烧层数上限 +N
        BurnTakenUp = 3,       // 敌方每层灼烧受伤 +X%
        CritRateTeam = 4,      // 全队暴击率 +X%
        ShieldOnHurt = 5,      // 单次受伤超阈值时给护盾
        DodgeWhenAdjacentFoe = 6,
        DamageToGuardUp = 7,
        ExecuteLowHp = 8,      // 对残血目标伤害 +X%
        KillBonusAtk = 9,      // 击杀永久 +X% 攻击（本局）
        PoisonOnCrit = 10,
        TeamShieldStart = 11,
        ReactionDamageDown = 12,
        RageDownOnCrit = 13,   // 预留：暴击时目标怒气 -N
    }

    /// <summary>天性挂点。新增挂点只加枚举 + 模拟层一行 Fire。</summary>
    public enum TraitHook
    {
        None = 0,
        BattleStart = 1,
        TurnStart = 2,
        AdjacencyResolve = 3,
        BeforeDamage = 4,
        OnHurt = 5,
        OnCrit = 6,
        AfterKill = 7,
        QteWindow = 8,
    }

    /// <summary>性情（每只固定、不可换）。</summary>
    public enum Temperament
    {
        None = 0,
        Fierce = 1,    // 刚烈：受击元气 +，闪避 -
        Calm = 2,      // 沉静：每回合灵力 +，普攻 -
        Cunning = 3,   // 狡黠：QTE 窗口 +，相生回复 -
        Kind = 4,      // 温厚：相生回复 +，自身输出 -
        Aloof = 5,     // 孤高：无相邻时全属性 +，有相邻 -
    }

    /// <summary>挂点上下文。Value 是"输入/输出"数值，处理器可读可写。</summary>
    public struct TraitContext
    {
        public BattleState St;
        public BattleUnit Actor;      // 触发这次判定的单位
        public BattleUnit Target;
        public float Value;           // 伤害 / 灵力 / 暴击率 …
        public float Extra;           // 备用（层数、阈值等）
        public string Tag;            // 调试用
    }

    public delegate void TraitHandler(ref TraitContext ctx);

    public static class TraitRuntime
    {
        private sealed class Entry
        {
            public TraitHook Hook;
            public TraitHandler Handler;
        }

        private static readonly Dictionary<TraitKind, Entry> _entries =
            new Dictionary<TraitKind, Entry>();

        /// <summary>注册一种天性。重复注册 = 覆盖（表热重载/测试用）。</summary>
        public static void Register(TraitKind kind, TraitHook hook, TraitHandler handler)
        {
            if (kind == TraitKind.None || handler == null) return;
            _entries[kind] = new Entry { Hook = hook, Handler = handler };
        }

        public static bool IsRegistered(TraitKind kind) => _entries.ContainsKey(kind);

        /// <summary>清空并注册 B0 内置的 4 个示例处理器（B1 会替换成真实效果）。</summary>
        public static void ResetToDefaults()
        {
            _entries.Clear();
            Register(TraitKind.AdjacentMpBonus, TraitHook.AdjacencyResolve,
                (ref TraitContext c) => { c.Value += 1f; });                    // +1 灵力
            Register(TraitKind.CritRateTeam, TraitHook.BeforeDamage,
                (ref TraitContext c) => { c.Value += 0.08f; });                 // +8% 暴击率
            Register(TraitKind.ExecuteLowHp, TraitHook.BeforeDamage,
                (ref TraitContext c) => { c.Value += 0.25f; });                 // +25% 伤害
            Register(TraitKind.BurnCapUp, TraitHook.BattleStart,
                (ref TraitContext c) => { c.Value += 2f; });                    // 灼烧上限 +2
        }

        /// <summary>
        /// 派发：遍历双方存活单位，命中"同 hook + 已注册"的天性就执行。
        /// ⚠ B0 不调用此方法（行为零变化）；B1 起在各挂点调用。
        /// </summary>
        public static void Fire(TraitHook hook, ref TraitContext ctx)
        {
            if (ctx.St == null || hook == TraitHook.None) return;
            FireSide(ctx.St, TeamSide.Player, hook, ref ctx);
            FireSide(ctx.St, TeamSide.Enemy, hook, ref ctx);
        }

        private static void FireSide(BattleState st, TeamSide side, TraitHook hook, ref TraitContext ctx)
        {
            var units = st.UnitsOf(side);
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.IsAlive || u.Def == null) continue;
                var kind = u.Def.Trait.Kind;
                Entry e;
                if (!_entries.TryGetValue(kind, out e)) continue;    // 未注册 ⇒ 静默忽略
                if (e.Hook != hook || e.Handler == null) continue;
                ctx.Actor = u;
                e.Handler(ref ctx);
            }
        }

        /// <summary>按阵营求和（挂点用）。敌我不混算，避免"敌方天性帮我方加暴击"。</summary>
        public static float Sum(BattleState st, TeamSide side, TraitKind kind) => SumSide(st, side, kind);

        /// <summary>按阵营统计某性情的数量。</summary>
        public static int Count(BattleState st, TeamSide side, Temperament t)
        {
            if (st == null || t == Temperament.None) return 0;
            var units = st.UnitsOf(side);
            if (units == null) return 0;
            int n = 0;
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && units[i].IsAlive && units[i].Def != null
                    && units[i].Def.Temperament == t) n++;
            return n;
        }

        /// <summary>查询型：把全场某类天性的 Value 求和（不改状态）。</summary>
        public static float Sum(BattleState st, TraitKind kind)
        {
            if (st == null) return 0f;
            float total = 0f;
            total += SumSide(st, TeamSide.Player, kind);
            total += SumSide(st, TeamSide.Enemy, kind);
            return total;
        }

        private static float SumSide(BattleState st, TeamSide side, TraitKind kind)
        {
            float v = 0f;
            var units = st.UnitsOf(side);
            if (units == null) return 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.IsAlive || u.Def == null) continue;
                if (u.Def.Trait.Kind == kind) v += u.Def.Trait.Value;
            }
            return v;
        }

        static TraitRuntime() { ResetToDefaults(); }
    }
}
