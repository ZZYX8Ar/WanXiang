// ============================================================================
//  万相 · 一局编排（GDD 第 7 章 STEP 3「把战斗单元组装成一局游戏」）
//  ---------------------------------------------------------------------------
//  节点图（SolarTermGraph）+ 推进状态（RunState）到这里还只是"地图"，本文件是
//  把它们和战斗接起来的那根轴：
//
//      选一个节点 → 合成该节点的天时（含余气）→ 组敌方阵容 → 跑一场战斗
//      → 赢了推进、输了这局结束 → 本幕节点走满就打守关 → 守关赢了下幕
//
//  设计取舍（都记录在案）：
//  ① **每场战斗独立**：血量/状态不跨场继承（每场新建 BattleState）。GDD 没规定
//     跨场继承，取"每场一场硬仗"，与 battle.selftest 的判据口径一致。待策划确认。
//  ② **平局按失败处理**：达到回合上限仍没打完不能算通关（肉鸽里"没打过"就是没打过）。
//  ③ 敌方阵容由 <see cref="ICampaignContent"/> 注入：核心层不认识任何具体内容表，
//     编辑器侧给样本/配置表，离线冒烟给手工单位 —— 同一份编排逻辑两侧都能跑。
//  ④ 每场战斗的随机种子 = 局种子 ^ hash(幕:节点:序号) ⇒ **同一局种子逐场可复现**，
//     且不同节点的随机流互不干扰（改一个节点的内容不会串了整个随机序列）。
// ============================================================================

using System;
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>一局的终局形态。</summary>
    public enum RunOutcome
    {
        InProgress = 0,
        Completed = 1,   // 后土已败
        Defeated = 2,    // 中途败北（含平局）
    }

    /// <summary>一场战斗的记账（窗口展示、复盘、自检都用它）。</summary>
    public struct BattleRecord
    {
        public int Index;          // 第几战（从 1 起）
        public int Act;
        public int TermIndex;      // 节气序号（GDD 01..24）；守关战 = -1
        public bool IsBoss;
        public string WeatherId;   // 本场合成天时 id（null = 无天时）
        public string WeatherName;
        public BattleOutcome Outcome;
        public int Turns;
        public int EventCount;
        public uint Fingerprint;

        public string Describe()
        {
            string node = IsBoss ? $"守关·{ActNameCn(Act)}" : $"{TermIndex:00} {WeatherName}";
            return $"#{Index,2} 幕{Act} {node}｜{Cn.Of(Outcome)}｜{Turns} 回合｜指纹 0x{Fingerprint:X8}";
        }

        private static string ActNameCn(int act)
        {
            switch (act)
            {
                case 1: return "句芒";
                case 2: return "祝融";
                case 3: return "蓐收";
                case 4: return "禺强";
                default: return "后土";
            }
        }
    }

    /// <summary>
    /// 敌方阵容来源。核心层不认识内容表 —— 编辑器侧给样本/配置，离线冒烟给手工单位。
    /// ⚠ 必须**确定性**：同 (act, termIndex, isBoss, seed) 必须给同一套阵容，
    ///   否则"一局可复现"这条判据在编排层就断了。
    /// </summary>
    public interface ICampaignContent
    {
        DeployEntry[] EnemiesFor(int act, int termIndex, bool isBoss, ulong seed);
    }

    /// <summary>选路策略：给定幕与层，选一个节点下标。玩家交互未接时用确定性替身。</summary>
    public delegate int NodeChooser(ActGraph graph, int layer);

    public static class RunChoosers
    {
        /// <summary>总走第一个分支（最保守的替身，用于对照实验）。</summary>
        public static NodeChooser First => (graph, layer) => graph.Layers[layer][0];

        /// <summary>按局种子伪随机选路：不同种子走不同路径，同种子完全一致。</summary>
        public static NodeChooser Seeded(ulong seed) => (graph, layer) =>
        {
            var rng = new DeterministicRandom(seed ^ CoreMath.Fnv1a($"path:{graph.Act}:{layer}"));
            var options = graph.Layers[layer];
            return options[rng.NextInt(0, options.Length)];
        };
    }

    /// <summary>一局的推进器。逐场调用 <see cref="PlayOneBattle"/>，或一次 <see cref="Play"/> 跑完。</summary>
    public sealed class RunDriver
    {
        private readonly BattleConfig _cfg;
        private readonly ICampaignContent _content;
        private readonly NodeChooser _chooser;
        private readonly ulong _seed;

        public RunDriver(BattleConfig cfg, ActGraph[] acts,
                         Func<int, WeatherDef> termWeather, ICampaignContent content,
                         ulong seed, NodeChooser chooser = null)
        {
            _cfg = cfg ?? BattleConfig.Default;
            _seed = seed;
            _content = content;
            _chooser = chooser ?? RunChoosers.Seeded(seed);
            State = new RunState(acts, termWeather);
        }

        public RunState State { get; }

        public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

        public List<BattleRecord> Records { get; } = new List<BattleRecord>(24);

        /// <summary>本场的随机种子：局种子 ^ hash(幕:节点:序号)。</summary>
        public ulong SeedFor(int act, int termIndex, int index)
            => _seed ^ CoreMath.Fnv1a($"battle:{act}:{termIndex}:{index}");

        /// <summary>
        /// 打下一场。返回 null = 已无可打（通关或已败北）。
        /// 节点战：先 EnterNode（消耗余气计数）再打；守关战：不打节点、无天时。
        /// </summary>
        public BattleRecord? PlayOneBattle(DeployEntry[] playerSquad)
        {
            if (Outcome != RunOutcome.InProgress) return null;
            if (State.Finished) { Outcome = RunOutcome.Completed; return null; }

            bool isBoss = State.AtBoss;
            int act = State.CurrentAct;
            int termIndex = -1;
            WeatherDef weather = null;

            if (!isBoss)
            {
                int layer = State.CurrentLayer + 1;
                int offset = _chooser(State.CurrentGraph, layer);
                if (!State.EnterNode(offset)) return null;      // 选路非法 = 编排 bug，别静默转圈
                termIndex = State.CurrentGraph.Terms[offset];
                weather = State.ComposeCurrentWeather();
            }

            int index = Records.Count + 1;
            var enemies = _content?.EnemiesFor(act, termIndex, isBoss, SeedFor(act, termIndex, index))
                          ?? new DeployEntry[0];

            var st = BattleFactory.Create(_cfg, SeedFor(act, termIndex, index),
                                          playerSquad, enemies, weather);
            var result = BattleSimulator.Run(st);

            var rec = new BattleRecord
            {
                Index = index,
                Act = act,
                TermIndex = termIndex,
                IsBoss = isBoss,
                WeatherId = weather?.Id,
                WeatherName = weather?.BuffName ?? "（无天时）",
                Outcome = result.Outcome,
                Turns = result.Turns,
                EventCount = result.EventCount,
                Fingerprint = result.Fingerprint,
            };
            Records.Add(rec);

            if (result.Outcome != BattleOutcome.PlayerWin)
            {
                Outcome = RunOutcome.Defeated;                  // 平局也算没打过（取舍②）
                return rec;
            }

            if (isBoss)
            {
                State.DefeatBoss();
                if (State.Finished) Outcome = RunOutcome.Completed;
            }
            return rec;
        }

        /// <summary>一次跑到底。返回终局形态。</summary>
        public RunOutcome Play(DeployEntry[] playerSquad)
        {
            while (Outcome == RunOutcome.InProgress)
                if (PlayOneBattle(playerSquad) == null) break;
            return Outcome;
        }

        /// <summary>整局摘要（窗口与自检报告共用一份文本）。</summary>
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"局种子 {_seed}｜结果 {OutcomeCn(Outcome)}｜共 {Records.Count} 战"
                        + $"（常规 {CountNormal()} + 守关 {CountBoss()}）");
            for (int i = 0; i < Records.Count; i++) sb.AppendLine("  " + Records[i].Describe());
            return sb.ToString();
        }

        private int CountNormal()
        {
            int n = 0;
            for (int i = 0; i < Records.Count; i++) if (!Records[i].IsBoss) n++;
            return n;
        }

        private int CountBoss()
        {
            int n = 0;
            for (int i = 0; i < Records.Count; i++) if (Records[i].IsBoss) n++;
            return n;
        }

        private static string OutcomeCn(RunOutcome o)
        {
            switch (o)
            {
                case RunOutcome.Completed: return "通关";
                case RunOutcome.Defeated: return "败北";
                default: return "进行中";
            }
        }
    }
}
