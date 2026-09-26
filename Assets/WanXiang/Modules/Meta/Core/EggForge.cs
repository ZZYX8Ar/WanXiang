// ============================================================================
//  万相 · 孵蛋（GDD 1.3 局外层「孵蛋获得新宿主、解锁新灵魂」）
//  ---------------------------------------------------------------------------
//  一次孵蛋做三件事：**扣灵卵 → 按稀有度权重从未解锁池里确定性地抽一只 → 记进存档**。
//  全程零系统时间、零隐式随机（见 MetaState 文件头的两条铁律）。
//
//  抽取算法（刻意朴素，便于策划理解与复现）：
//    1) 把"未解锁"的候选按稀有度分组，权重 = MetaDefaults.WeightOf(稀有度)；
//    2) 用一个从 (Seed ^ hash("hatch:孵次:池子")) 派生的确定性随机数落在总权重上；
//    3) 命中的那一格就是结果 —— 同存档同池子 ⇒ 必同结果。
//
//  ⚠ 池子空了怎么半：**不扣灵卵、返回 Empty**（而不是"白孵一次"）。
//    孵化是花灵卵买进度，买不到就不该扣钱。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Meta
{
    /// <summary>孵蛋的对象：宿主（异兽）还是灵魂。</summary>
    public enum EggKind { Host = 0, Soul = 1 }

    /// <summary>一次孵蛋的结果。</summary>
    public struct HatchResult
    {
        public bool Ok;
        public bool Empty;        // 池子已满（无可孵），未扣灵卵
        public int Index;         // 孵出的内容下标（Ok 时有效）
        public Rarity Rarity;
        public int EggsSpent;
        public string Note;       // 人类可读（窗口/日志直接用）

        public static HatchResult Fail(string why) => new HatchResult { Ok = false, Note = why };
        public static HatchResult PoolEmpty() => new HatchResult { Empty = true, Note = "图鉴已满：没有可孵的内容了" };
    }

    public static class EggForge
    {
        /// <summary>一次孵蛋的灵卵价格（占位数值，待策划给表）。</summary>
        public const int EggPrice = 10;

        /// <summary>
        /// 孵一次。<paramref name="state"/> 会被就地修改（扣灵卵 / 记孵次 / 解锁）。
        /// 失败时**不改动状态** —— 宁可什么都不做，也别留下"扣了钱没出货"的脏档。
        /// </summary>
        public static HatchResult Hatch(MetaState state, MetaContent content, EggKind kind)
        {
            if (state == null) return HatchResult.Fail("没有局外存档");
            if (!content.IsValid) return HatchResult.Fail("内容目录无效（宿主/灵魂数量或稀有度表不匹配）");

            // ★ 2026-09-26 用户定案：**魂魄只能从灵市购买**，其余路径暂时关闭。
            //   把这条写成显式拒绝，而不是"悄悄还能孵出来" —— 否则它就是个隐形入口，
            //   与策略冲突（且日后无人记得）。宿主蛋不受影响。
            if (kind == EggKind.Soul)
                return HatchResult.Fail("魂魄只能从灵市购买（孵蛋获取已暂时关闭）");

            var unlocked = kind == EggKind.Host ? state.UnlockedHosts : state.UnlockedSouls;
            int total = kind == EggKind.Host ? content.HostCount : content.SoulCount;
            var rarities = kind == EggKind.Host ? content.HostRarities : content.SoulRarities;

            // 候选 = 未解锁的全部下标（升序，顺序即语义）
            var candidates = new List<int>(total - unlocked.Count);
            for (int i = 0; i < total; i++)
                if (!unlocked.Contains(i)) candidates.Add(i);
            if (candidates.Count == 0) return HatchResult.PoolEmpty();

            if (state.Eggs < EggPrice)
                return HatchResult.Fail($"灵卵不足（需要 {EggPrice}，现有 {state.Eggs}）");

            // 权重表：候选 → 权重（稀有度缺失/为 0 时按最低权重兜底，避免"抽不到"）
            var weights = new int[candidates.Count];
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int w = MetaDefaults.WeightOf(rarities[candidates[i]]);
                if (w <= 0) w = 1;
                weights[i] = w;
                totalWeight += w;
            }

            // 确定性抽取：随机流的盐里带"孵次 + 池子类型"，所以同一存档的每次孵化都不重样，
            // 而同一序列（第 1 次、第 2 次…）永远复现。
            var rng = new DeterministicRandom(
                state.Seed ^ CoreMath.Fnv1a($"hatch:{state.HatchCount}:{kind}:{candidates.Count}"));
            int roll = rng.NextInt(0, totalWeight);
            int pick = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (roll < weights[i]) { pick = i; break; }
                roll -= weights[i];
            }

            int index = candidates[pick];
            state.Eggs -= EggPrice;
            state.HatchCount++;
            unlocked.Add(index);

            return new HatchResult
            {
                Ok = true,
                Index = index,
                Rarity = rarities[index],
                EggsSpent = EggPrice,
                Note = $"孵出 {Cn.Of(rarities[index])}｜[{index}]（灵卵 -{EggPrice}，余 {state.Eggs}）",
            };
        }
    }
}
