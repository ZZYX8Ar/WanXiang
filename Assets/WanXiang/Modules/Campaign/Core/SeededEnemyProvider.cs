// ============================================================================
//  万相 · 确定性敌方阵容供给（STEP 3）
//  ---------------------------------------------------------------------------
//  给 <see cref="RunDriver"/> 用的默认内容来源：从"每幕一个池子"里按种子抽人。
//
//  为什么不用 HashSet/去重表来"抽不重复的 5 只"：那是集合语义，而集合的枚举顺序
//  是实现细节（.NET 版本一变就可能不同）—— 可复现性最怕这个。这里用**数组内
//  交换收缩**：抽到的下标与末尾交换、可用区收缩一，抽 n 只恰好 O(n)。
//
//  阵型固定为 { 0, 1, 4, 7, 8 }（前排两格 + 中宫 + 后排两角），与基准局一致：
//  中宫有人 ⇒ 中宫平息/中宫减伤这些棋盘规则每场都真的在跑。
//
//  v1.1 §5 升级：**规模表 + 属性倍率 + 劫象 + 预算自检**（GDD 明说算法"与工程逐位对齐，
//  唯一改动是把固定 5 格换成规模表"）：
//    · 规模 EnemyBudget.SquadSize：遭遇 3/4/4/5、精英 4/5/5/5、守关 4/5/5/5（Boss+随从）
//    · 属性倍率 m_unit = A×T×K 只缩放生命/攻/防（速度与暴击不缩放 —— §5.1 铁律）
//    · 精英战恰好 1 只带「劫象」（BattleTraits.Pool 按种子抽 1 条）
//    · 预算自检：Σ(RARITY×ROLE 计价) ≤ BP_cap（护栏不是硬失败，Ratio 供自检断言）
// ============================================================================

using System;
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    public sealed class SeededEnemyProvider : ICampaignContent
    {
        /// <summary>默认阵型：0/1 前排，4 中宫，7/8 后排。</summary>
        public static readonly int[] DefaultFormation = { 0, 1, 4, 7, 8 };

        /// <summary>
        /// 守关 Boss 站位：**中宫（格 4）**（用户 2026-10-03 定案"boss 站在九宫格中间"）。
        /// ⚠ 别用 DefaultFormation[0]（=格 0 前排）：Boss 要在视觉与战术上都是"压阵点"，
        ///   站前排会显得跟普通敌人一样大、且被前排遮挡。
        /// </summary>
        public const int BossCell = 4;

        /// <summary>
        /// 守关随从格位：**前排左(6) / 前排右(8)**。
        /// ⛔ 格号行语义：`row = Pos.Index / 3`，**row0=后排、row2=前排**（贴玩家）。
        ///   ⚠ 随从放**前排**是为了给后排腾位置：双子（<c>BossCatalog.TwinCell</c>=格1 后排中）
        ///   已经在后排 ⇒ 若随从也占后排两侧(0/2)，后排会挤满 3 只立绘互相遮挡。
        ///   随从在前排还有个合理性：它们先挡在我方与本体之间。
        /// </summary>
        public const int BossSideCellL = 6;
        public const int BossSideCellR = 8;

        /// <summary>
        /// 随从相对 Boss 的额外倍率（v1.4.1）：随从只吃 Boss 的 55%。
        /// 依据用户"带两个随从"的定案 —— 随从是场面与机制目标，强度主体仍是 Boss 本体。
        /// </summary>
        public const float BossSidekickMul = 0.55f;

        private readonly Func<int, BeastDef[]> _poolForAct;
        private readonly Func<int, BeastDef> _bossForAct;
        private readonly int[] _formation;
        private readonly int _finaleMirrors;
        private readonly float _eliteExtraMul;
        private readonly int _bossTraitCount;

        /// <param name="poolForAct">某一幕的可选池（按 1..5 传入幕号）。返回 null/空 = 该幕无敌人（空阵容，自检里会看得见）。</param>
        /// <param name="bossForAct">守关战的主将（可空；为空则守关也只是普通阵容）。</param>
        /// <param name="finaleMirrors">天阙镜像数（劫律 19「天阙低垂」3 → 5）。</param>
        /// <param name="eliteExtraMul">精英属性额外乘数（劫律 07「兽强」= 1.15）。</param>
        public SeededEnemyProvider(Func<int, BeastDef[]> poolForAct,
                                   Func<int, BeastDef> bossForAct = null,
                                   int[] formation = null,
                                   int finaleMirrors = 3,
                                   float eliteExtraMul = 1f,
                                   int bossTraitCount = 0)
        {
            _poolForAct = poolForAct ?? (_ => null);
            _bossForAct = bossForAct;
            _formation = formation ?? DefaultFormation;
            _finaleMirrors = finaleMirrors;
            _eliteExtraMul = eliteExtraMul;
            _bossTraitCount = bossTraitCount;
        }

        /// <summary>最近一次供给的预算占用率（自检断言用；1.0 = 占满上界）。</summary>
        public float LastBudgetRatio { get; private set; }

        /// <summary>遭遇 / 精英（GDD §5.3 的 EnemiesFor；isBoss 拆成独立入口后按类型给规模与倍率）。</summary>
        public DeployEntry[] EnemiesFor(int act, int termIndex, NodeKind kind, ulong seed)
        {
            var pool = _poolForAct(act);
            if (pool == null || pool.Length == 0) return Array.Empty<DeployEntry>();

            var rng = new DeterministicRandom(seed ^ CoreMath.Fnv1a($"enemy:{act}:{termIndex}"));
            int slots = CoreMath.Min(_formation.Length,
                                     System.Math.Min(EnemyBudget.SquadSize(act, kind), pool.Length));
            float mul = EnemyBudget.UnitMul(act, kind, 1, _eliteExtraMul);

            var picked = new List<BeastDef>(slots);
            var idx = new int[pool.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            int remaining = pool.Length;
            while (picked.Count < slots && remaining > 0)
            {
                int k = rng.NextInt(0, remaining);
                picked.Add(pool[idx[k]]);
                int tmp = idx[k];
                idx[k] = idx[remaining - 1];
                idx[remaining - 1] = tmp;
                remaining--;
            }

            var result = new DeployEntry[picked.Count];
            for (int i = 0; i < picked.Count; i++)
                result[i] = DeployEntry.Enemy(picked[i], _formation[i]).WithMul(mul);

            // 精英战：恰好 1 只带「劫象」（按种子从池里抽 1 条；§5.5 —— 同一场战斗
            // 在不同种子里有不同的解法，而不是单纯加数值）
            if (kind == NodeKind.Elite && result.Length > 0)
            {
                int who = rng.NextInt(0, result.Length);
                int what = rng.NextInt(0, BattleTraits.Pool.Length);
                result[who] = result[who].WithTrait(BattleTraits.Pool[what]);
            }

            LastBudgetRatio = EnemyBudget.Ratio(result, EnemyBudget.Cap(act, kind));
            return result;
        }

        /// <summary>
        /// 守关：**Boss 独自镇守中宫**（格 4），不配随从。
        ///
        /// ★ v1.4 改版（用户定案 2026-10-03，两次调整）：
        ///   ① 先从"Boss + 3~4 随从"改成"单 Boss"（稀释首领存在感）；
        ///   ② 实测单 Boss **太简单**（四幕对神品满编全 100% 白给）⇒ 用户定案"实在不行就是带两个随从"。
        ///   最终口径：**Boss（中宫）+ 2 个随从**。
        ///   · 随从**只给 2 个**（原 BossSquadSize 是 4/5，太挤且抢戏）；
        ///   · 随从**不是凑数**：固定落**前排左/右**（格 6/8）—— 给后排腾位置（双子占格 1 后排中）；
        ///   · 随从用**半倍率**（BossUnitMul × 0.55）—— 压力主要来自 Boss 本体与机制，
        ///     随从只是让战场不空、给 Boss 机制提供目标。
        /// </summary>
        public DeployEntry[] BossSquadFor(int act, ulong seed)
        {
            var pool = _poolForAct(act);
            var boss = _bossForAct?.Invoke(act);
            if (boss == null) return Array.Empty<DeployEntry>();

            // 守关倍率：Boss 本体吃满 BossUnitMul，随从只吃一半
            float bossMul = EnemyBudget.BossUnitMul(act);
            float sideMul = bossMul * BossSidekickMul;

            var list = new List<DeployEntry>(4) { DeployEntry.Enemy(boss, BossCell).WithMul(bossMul) };

            // ★ 双子搭档（白魍）：**布阵阶段就放进敌方阵容**（不是开场钩子临时生成）——
            //   用户 2026-10-03："他们两个都是 Boss 啊，不是召唤出来的，他们是一起出现的"。
            //   放在这里的好处：编队预览也能看到两只（此前只在 OnBattleStart 部署 ⇒ 预览只有 1 只）。
            //   钩子（同命/共鸣）仍由 BattleCatalog.AttachAllBossHooks 挂，职责不变。
            var twin = WanXiang.Battle.Core.BossCatalog.TwinFor(boss.Id);
            if (twin != null)
                list.Add(DeployEntry.Enemy(twin, WanXiang.Battle.Core.BossCatalog.TwinCell).WithMul(bossMul));

            // 2 个随从：固定落**前排**左/右（格 6/8）—— 后排让给双子（格 1），避免后排挤 3 只立绘。
            if (pool != null && pool.Length > 0)
            {
                int[] sideCells = { BossSideCellL, BossSideCellR };
                var rng = new DeterministicRandom(seed ^ CoreMath.Fnv1a($"boss-side:{act}"));
                var idx = new int[pool.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                int remaining = pool.Length;
                for (int n = 0; n < sideCells.Length && remaining > 0; n++)
                {
                    int k = rng.NextInt(0, remaining);
                    var pick = pool[idx[k]];
                    int tmp = idx[k];
                    idx[k] = idx[remaining - 1];
                    idx[remaining - 1] = tmp;
                    remaining--;
                    if (pick == null || pick == boss) { n--; continue; }   // 跳过首领自己
                    list.Add(DeployEntry.Enemy(pick, sideCells[n]).WithMul(sideMul));
                }
            }

            var result = list.ToArray();

            // 劫律 14「守关加冠」：Boss 额外获得 1~2 条特性（从劫象池按种子抽，可重复）。
            // GDD：守关的强度来自 Boss 的特性与 ×1.35 倍率，不来自人数堆满预算。
            if (_bossTraitCount > 0)
            {
                var rng = new DeterministicRandom(seed ^ CoreMath.Fnv1a($"boss-trait:{act}"));
                for (int k = 0; k < _bossTraitCount; k++)
                {
                    int what = rng.NextInt(0, BattleTraits.Pool.Length);
                    result[0] = result[0].WithTrait(BattleTraits.Pool[what]);
                }
            }

            LastBudgetRatio = EnemyBudget.Ratio(result, EnemyBudget.BossCap(act));
            return result;
        }

        /// <summary>
        /// 天阙（v1.1 §5.5/§5.6 #17）：**后土站中宫**（御·中宫）+
        /// 玩家队伍前 3 只的镜像（劫律 19 起 5 只，这里先做默认 3 只）。
        /// 镜像 = 同一份 BeastDef 摆到敌方侧 —— BattleFactory 会为每个上阵指令克隆，
        /// 所以我方/敌方的同名单位不会互相串改。
        /// </summary>
        public DeployEntry[] FinaleFor(DeployEntry[] playerSquad, int mirrors = 3, ulong seed = 0UL)
        {
            var boss = _bossForAct?.Invoke(5);                    // 幕 5 = 后土
            var list = new List<DeployEntry>(5);
            int[] mirrorSlots = { 0, 1, 7, 8, 2 };                // 中宫留给后土，镜像依次落位
            int mirrorIndex = 0;

            if (boss != null) list.Add(DeployEntry.Enemy(boss, 4));
            if (playerSquad != null)
            {
                for (int i = 0; i < playerSquad.Length && mirrorIndex < mirrors; i++)
                {
                    var def = playerSquad[i].Def;
                    if (def == null) continue;
                    list.Add(DeployEntry.Enemy(def, mirrorSlots[mirrorIndex]));
                    mirrorIndex++;
                }
            }
            return list.ToArray();
        }
    }
}
