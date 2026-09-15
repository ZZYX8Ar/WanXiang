// ============================================================================
//  万相 · 一局编排（GDD v1.1 §4.2 / §4.3 / §7.2：「把战斗单元组装成一局游戏」）
//  ---------------------------------------------------------------------------
//  节点图（SolarTermGraph）+ 推进状态（RunState）到这里还只是"地图"，本文件是
//  把它们和战斗/事件接起来的那根轴：
//
//      选一个节点 → 看类型：
//        · 遭遇/精英 → 合成天时（含余气）→ 组敌方阵容 → 跑一场战斗
//        · 灵市/孵穴/异闻/铸魂台/天象 → 立即结算产出（灵卵/回血/免费融合…）
//      → 赢了推进、输了这局结束 → 本幕节点走满就打守关 → 四幕走完打天阙
//
//  v1.1 的结构纠正（本文件是它的落地处）：
//  ① **单局 13~17 场**：每幕一/二层各恰 1 场战斗（另一个选项非战斗）、三层固定精英、
//     四层固定非战斗 ⇒ 每幕 2~3 战；加 4 守关 + 1 天阙。
//     v1.0 的"16 常规 + 5 守关 = 21"是把 16 个节点全当战斗节点算的，GDD 明确纠正。
//  ② **七种节点类型**：战斗只出自遭遇/精英；灵市/孵穴/异闻/铸魂台/天象是非战斗节点。
//  ③ **天阙**：四幕守关打完后打后土（×1.50）+ **玩家队伍前 3 只的镜像**
//     —— 这就是"打你自己的队伍"的终局战（劫律 19 起 5 只镜像）。
//
//  设计取舍（沿袭并新增，均记录在案）：
//  ① 每场战斗独立（血量不跨场继承）；② 平局按失败处理；
//  ③ 敌方阵容由 ICampaignContent 注入（核心层不认识内容表）；
//  ④ 每场种子 = 局种子 ^ hash(幕:节点:序号) ⇒ 同局种子逐场可复现；
//  ⑤ **非战斗节点的"三选一/二选一"当前取确定性默认**（孵穴取灵卵、异闻取拒绝）——
//     选项 UI 与天象/异闻表是 P5 的事；产出先记账（局内灵卵），保证结构先跑通。
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
        Completed = 1,   // 天阙·后土已败
        Defeated = 2,    // 中途败北（含平局）
    }

    /// <summary>一步的类型：战斗出自遭遇/精英/守关/天阙，其余是非战斗节点。</summary>
    public enum RunStepKind
    {
        Encounter = 0,   // 遭遇战
        Elite = 1,       // 精英战
        Boss = 2,        // 守关战（幕 1~4）
        Finale = 3,      // 天阙（后土 + 玩家镜像）
        NonBattle = 4,   // 灵市 / 孵穴 / 异闻 / 铸魂台 / 天象
    }

    /// <summary>
    /// 一步的记账（战斗与非战斗统一在这里 —— 复盘时要看的是"这一局经历了什么"，
    /// 不只是"打了几架"）。
    /// </summary>
    public struct RunStep
    {
        public int Index;          // 第几步（含非战斗，从 1 起）
        public int Act;
        public int TermIndex;      // 节气序号（01..24）；守关/天阙 = -1
        public NodeKind Kind;      // 节点类型（守关/天阙无节点类型，值无意义）
        public RunStepKind StepKind;
        public string WeatherId;   // 合成天时 id（null = 无天时）
        public string WeatherName;

        // ---- 战斗步 ----
        public BattleOutcome Outcome;
        public int Turns;
        public int EventCount;
        public uint Fingerprint;

        // ---- 非战斗步 ----
        public string Output;      // 产出/事件文本（灵卵、回血、免费融合…）

        public bool IsBattle => StepKind != RunStepKind.NonBattle;

        public string StepCn()
        {
            switch (StepKind)
            {
                case RunStepKind.Encounter: return "遭遇";
                case RunStepKind.Elite: return "精英";
                case RunStepKind.Boss: return "守关";
                case RunStepKind.Finale: return "天阙";
                default: return NodeKinds.Cn(Kind);
            }
        }

        public string Describe()
        {
            string node = TermIndex > 0
                ? $"{TermIndex:00} {WeatherName}"
                : (StepKind == RunStepKind.Finale ? "天阙·后土" : $"守关·{ActBossCn(Act)}");
            if (!IsBattle)
                return $"#{Index,2} 幕{Act} {node}（{StepCn()}）｜{Output}";
            return $"#{Index,2} 幕{Act} {node}｜{Cn.Of(Outcome)}｜{Turns} 回合｜指纹 0x{Fingerprint:X8}";
        }

        private static string ActBossCn(int act)
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
    /// ⚠ 必须**确定性**：同入参必须给同一套阵容，否则"一局可复现"在编排层就断了。
    /// </summary>
    public interface ICampaignContent
    {
        /// <summary>遭遇/精英战（规模与倍率按 §5.1/§5.5 的表来）。</summary>
        DeployEntry[] EnemiesFor(int act, int termIndex, NodeKind kind, ulong seed);

        /// <summary>守关战（Boss + 随从，倍率 ×1.35）。</summary>
        DeployEntry[] BossSquadFor(int act, ulong seed);

        /// <summary>
        /// 天阙阵容：后土 + **玩家队伍前 N 只的镜像**（N = 3，劫律 19「天阙低垂」起 5）。
        /// 镜像 = 同一份 BeastDef 摆到敌方侧（BattleFactory 会克隆，不会串改我方）。
        /// </summary>
        DeployEntry[] FinaleFor(DeployEntry[] playerSquad, int mirrors, ulong seed);
    }

    /// <summary>
    /// 一轮的图/经济侧调参（劫律折算产物；默认 = 无劫律的 v1.1 基准）。
    /// 定义在 Campaign 层、由 Trials 层从 <c>TrialState</c> 转换 —— 依赖方向不能反。
    /// </summary>
    public sealed class RunTuning
    {
        public int LingerNodes = 2;           // 01 余气不散 → 3
        public float NestHealPercent = 0.40f; // 16 香火断绝 → 0.20
        public int FinaleMirrors = 3;         // 19 天阙低垂 → 5
        public float EliteExtraMul = 1f;      // 07 兽强 → 1.15
        public bool NarrowPath;               // 09 路窄：每层候选 2 → 1
        public bool ForceEliteFirst;          // 17 兽王当立：每幕首节点强制精英
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

    /// <summary>一局的推进器。逐步调用 <see cref="PlayNextStep"/>，或一次 <see cref="Play"/> 跑完。</summary>
    public sealed class RunDriver
    {
        private readonly BattleConfig _cfg;
        private readonly ICampaignContent _content;
        private readonly NodeChooser _chooser;
        private readonly ulong _seed;
        private readonly RunTuning _tuning;

        public RunDriver(BattleConfig cfg, ActGraph[] acts,
                         Func<int, WeatherDef> termWeather, ICampaignContent content,
                         ulong seed, NodeChooser chooser = null, RunTuning tuning = null)
        {
            _cfg = cfg ?? BattleConfig.Default;
            _seed = seed;
            _content = content;
            _chooser = chooser ?? RunChoosers.Seeded(seed);
            _tuning = tuning ?? new RunTuning();
            State = new RunState(acts, termWeather, _tuning.LingerNodes);
        }

        /// <summary>天阙镜像数（劫律 19「天阙低垂」可到 5）。</summary>
        public int FinaleMirrors => _tuning.FinaleMirrors;

        public RunState State { get; }

        public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;

        /// <summary>全部步骤（战斗 + 非战斗节点，按经历顺序）。</summary>
        public List<RunStep> Steps { get; } = new List<RunStep>(20);

        /// <summary>战斗步（守关/天阙/遭遇/精英）—— 老的"逐场"口径，摘要与自检仍用它。</summary>
        public IEnumerable<RunStep> Battles
        {
            get
            {
                foreach (var s in Steps) if (s.IsBattle) yield return s;
            }
        }

        /// <summary>战斗场数（含守关与天阙）。v1.1 的合法区间是 13~17。</summary>
        public int BattleCount
        {
            get
            {
                int n = 0;
                foreach (var s in Steps) if (s.IsBattle) n++;
                return n;
            }
        }

        /// <summary>局内灵卵（遭遇 +1 / 精英 +2 / 孵穴 +2 / 异闻拒绝 +1；灵市消费待 P4）。</summary>
        public int RunEggs { get; private set; }

        /// <summary>本步的随机种子：局种子 ^ hash(幕:节点:序号)。</summary>
        public ulong SeedFor(int act, int termIndex, int index)
            => _seed ^ CoreMath.Fnv1a($"step:{act}:{termIndex}:{index}");

        /// <summary>
        /// 推进一步（打一场或结算一个非战斗节点）。返回 null = 本局已结束。
        /// </summary>
        public RunStep? PlayNextStep(DeployEntry[] playerSquad)
        {
            if (Outcome != RunOutcome.InProgress) return null;
            if (State.Finished) { Outcome = RunOutcome.Completed; return null; }

            bool atBoss = State.AtBoss;
            bool isFinale = atBoss && State.CurrentAct == State.ActCount;
            int act = State.CurrentAct;
            int termIndex = -1;
            WeatherDef weather = null;
            NodeKind kind = NodeKind.Encounter;
            RunStepKind stepKind;

            if (atBoss)
            {
                stepKind = isFinale ? RunStepKind.Finale : RunStepKind.Boss;
            }
            else
            {
                int layer = State.CurrentLayer + 1;
                bool firstNodeOfAct = State.VisitedInAct == 0;   // ⚠ EnterNode 之前取，进去就变 1 了
                // 劫律 09「路窄」：分叉层的候选从 2 降到 1（只走第一个）
                int offset = _tuning.NarrowPath
                    ? State.CurrentGraph.Layers[layer][0]
                    : _chooser(State.CurrentGraph, layer);
                if (!State.EnterNode(offset)) return null;      // 选路非法 = 编排 bug，别静默转圈
                kind = State.CurrentGraph.KindOf(offset);
                // 劫律 17「兽王当立」：每幕的第一个节点强制精英（RunDriver 层覆写，
                // 图数据不动 —— 别的读图方（窗口预览）不受本局劫律影响）
                if (_tuning.ForceEliteFirst && firstNodeOfAct)
                    kind = NodeKind.Elite;
                termIndex = State.CurrentGraph.Terms[offset];
                weather = State.ComposeCurrentWeather();
                stepKind = NodeKinds.IsBattle(kind)
                    ? (kind == NodeKind.Elite ? RunStepKind.Elite : RunStepKind.Encounter)
                    : RunStepKind.NonBattle;
            }

            int index = Steps.Count + 1;
            var step = new RunStep
            {
                Index = index,
                Act = act,
                TermIndex = termIndex,
                Kind = kind,
                StepKind = stepKind,
                WeatherId = weather?.Id,
                WeatherName = weather?.BuffName ?? "（无天时）",
            };

            if (stepKind == RunStepKind.NonBattle)
            {
                step.Output = ResolveNonBattle(kind, SeedFor(act, termIndex, index));
                Steps.Add(step);
                return step;
            }

            // ---- 战斗步 ----
            DeployEntry[] enemies;
            if (isFinale)
                enemies = _content?.FinaleFor(playerSquad, _tuning.FinaleMirrors, SeedFor(act, termIndex, index))
                          ?? new DeployEntry[0];
            else if (atBoss)
                enemies = _content?.BossSquadFor(act, SeedFor(act, -1, index)) ?? new DeployEntry[0];
            else
                enemies = _content?.EnemiesFor(act, termIndex, kind, SeedFor(act, termIndex, index))
                          ?? new DeployEntry[0];

            var st = BattleFactory.Create(_cfg, SeedFor(act, termIndex, index),
                                          playerSquad, enemies, weather);
            var result = BattleSimulator.Run(st);

            step.Outcome = result.Outcome;
            step.Turns = result.Turns;
            step.EventCount = result.EventCount;
            step.Fingerprint = result.Fingerprint;
            Steps.Add(step);

            if (result.Outcome != BattleOutcome.PlayerWin)
            {
                Outcome = RunOutcome.Defeated;                  // 平局也算没打过（取舍②）
                return step;
            }

            if (atBoss)
            {
                State.DefeatBoss();
                if (State.Finished) Outcome = RunOutcome.Completed;
            }
            else
            {
                // v1.1 §4.3：遭遇 +1 灵卵、精英 +2（守关/天阙的奖励走结算，不进局内钱包）
                RunEggs += stepKind == RunStepKind.Elite ? 2 : 1;
            }
            return step;
        }

        /// <summary>
        /// 非战斗节点的确定性结算。⚠ **三选一/二选一的选项 UI 是 P5**（天象/异闻表也在那时接），
        /// 现在取"不引入数值副作用"的默认并把全部可选项写进 Output —— 结构先跑通，别假装商店能逛。
        /// </summary>
        private string ResolveNonBattle(NodeKind kind, ulong seed)
        {
            switch (kind)
            {
                case NodeKind.Nest:
                    RunEggs += 2;      // 默认取灵卵（另一项"回血 40%"对"每场独立血量"的模型没意义）
                    return $"孵穴：取 2 枚灵卵（另一选项：回复全队 {_tuning.NestHealPercent:P0} 生命"
                         + " —— 与每场独立血量冲突，待策划定）";

                case NodeKind.Tale:
                    RunEggs += 1;      // 默认拒绝（异闻表 §4.7 待 P5 接入）
                    return "异闻：拒绝，拿 1 枚灵卵（三选一待接 §4.7）";

                case NodeKind.Shop:
                    return "灵市：货架 3 异兽 + 2 灵魂 + 1 次重铸（局内消费待 P4）";

                case NodeKind.Forge:
                    return "铸魂台：免费融合 ×1 + 赠 1 随机灵魂（融合管线就绪，入口待 UI）";

                case NodeKind.Omen:
                {
                    // §4.6 表已落（4 条，含明确副作用）；数值增益的**应用**需要
                    // 局内成长模型（跨场队伍继承 + 每回合损血钩子），接 UI 时落。
                    var omen = FieldEvents.Omens[(int)(seed % (ulong)FieldEvents.Omens.Length)];
                    return $"天象·{omen.Name}：{omen.Buff}｜副作用：{omen.Drawback}"
                         + $"（适合 {omen.Archetype}；数值应用待接）";
                }

                default:
                    return $"（未支持的节点类型 {kind}）";
            }
        }

        /// <summary>一次跑到底。返回终局形态。</summary>
        public RunOutcome Play(DeployEntry[] playerSquad)
        {
            while (Outcome == RunOutcome.InProgress)
                if (PlayNextStep(playerSquad) == null) break;
            return Outcome;
        }

        /// <summary>整局摘要（窗口与自检报告共用一份文本）。</summary>
        public string Summary()
        {
            int battles = 0, nonBattle = 0;
            foreach (var s in Steps)
            {
                if (s.IsBattle) battles++;
                else nonBattle++;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"局种子 {_seed}｜结果 {OutcomeCn(Outcome)}｜共 {Steps.Count} 步"
                        + $"（战斗 {battles} + 非战斗 {nonBattle}，局内灵卵 {RunEggs}）");
            for (int i = 0; i < Steps.Count; i++) sb.AppendLine("  " + Steps[i].Describe());
            return sb.ToString();
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
