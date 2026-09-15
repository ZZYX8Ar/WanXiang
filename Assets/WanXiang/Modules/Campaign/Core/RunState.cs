// ============================================================================
//  万相 · 一局推进状态（GDD 3.2 规则一/三，STEP 3）
//  ---------------------------------------------------------------------------
//  跑一局的"地图账本"：现在在第几幕、本幕走过哪些节点、余气还剩几个节点。
//  它**只记账不结算**——战斗交给 BattleFactory/BattleSimulator，天时在进
//  节点时用 <see cref="ComposeCurrentWeather"/> 合成好递过去。
//
//  节气天时从哪来：本模块不依赖任何编辑器侧目录（WeatherCatalog 在 Editor），
//  构造时注入 `Func<int, WeatherDef>`。未翻译的节气返回 null ⇒ 该场战斗没有
//  本节点天时，但**余气照常生效**（内容缺口不该把地图系统一起拖死）。
//
//  已记录的取舍（GDD 歧义，待策划确认）：
//  ① 余气来源取**上一幕走过的第一个节点**的天时 —— GDD 例"春末带着潮湿的
//     东风走进夏初"，东风解冻=立春=春幕首节点。另一种读法（最后一个节点，
//     即土节点）改一行 `FirstTermOfAct` 的取值即可。
//  ② 守关战（Boss）没有节点天时：Boss 不占节气节点，GDD 也没给 Boss 天时。
//  ③ 余气在本幕**每经过一个节点 -1**，残留 2 个节点（GDD 3.2 规则一）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace WanXiang.Campaign
{
    public sealed class RunState
    {
        /// <summary>一条余气：来源天时（已减半）+ 还剩几个节点生效。
        /// 计数在**进入节点时**消耗、扣到负数才移除 ⇒ 2 表示未来 2 个节点都生效。</summary>
        public struct LingerEntry
        {
            public WanXiang.Battle.Core.WeatherDef Weather;
            public int NodesLeft;
            public int FromTerm;      // 来源节气序号（记账/展示用）
        }

        private readonly ActGraph[] _acts;
        private readonly Func<int, WanXiang.Battle.Core.WeatherDef> _termWeather;
        private readonly int _lingerNodes;
        private readonly List<LingerEntry> _lingers = new List<LingerEntry>(2);

        public RunState(ActGraph[] acts, Func<int, WanXiang.Battle.Core.WeatherDef> termWeather,
                        int lingerNodes = 2)
        {
            _acts = acts ?? throw new ArgumentNullException(nameof(acts));
            _termWeather = termWeather;
            _lingerNodes = System.Math.Max(1, lingerNodes);
            CurrentAct = 1;
        }

        /// <summary>
        /// 当前幕的图。⚠ 通关后（CurrentAct = 幕数+1）会越界，这里**收敛到最后一幕**：
        /// 最后一幕（长夏）是空幕 ⇒ IsEmpty=true ⇒ AtBoss 恒真、EnterNode 恒拒，
        /// 语义与"已通关"一致；配合 <see cref="DefeatBoss"/> 里先判 Finished 的顺序，
        /// 通关后的任何推进调用都被安全拒绝而不是抛异常。
        /// </summary>
        public ActGraph CurrentGraph => _acts[System.Math.Min(CurrentAct, _acts.Length) - 1];

        public int CurrentAct { get; private set; }

        /// <summary>本幕已过的节点数（0..4）。</summary>
        public int VisitedInAct { get; private set; }

        /// <summary>当前所在节点（本幕 Terms 里的下标）；未进节点 = -1。</summary>
        public int CurrentOffset { get; private set; } = -1;

        /// <summary>全程经过的节气序号（按顺序，不含 Boss）。</summary>
        public List<int> Path { get; } = new List<int>(16);

        /// <summary>已击败的守关数。</summary>
        public int BossesDefeated { get; private set; }

        /// <summary>本幕当前层（由当前节点反推；未进节点 = -1，准备进第一层）。</summary>
        public int CurrentLayer => CurrentOffset < 0 ? -1 : CurrentGraph.LayerOf(CurrentOffset);

        /// <summary>
        /// 本场是否已到守关点。⚠ 判据是**层数**（GDD 3.2 规则三：实际只经过 4 个节点
        /// 即抵达守关），不是 NodeCount —— 6 个节点里有 2 个被永久放弃。
        /// </summary>
        public bool AtBoss
        {
            get
            {
                var g = CurrentGraph;
                return g.IsEmpty || VisitedInAct >= g.PathLength;
            }
        }

        /// <summary>一局是否通关（后土已败）。</summary>
        public bool Finished => CurrentAct > _acts.Length;

        /// <summary>幕总数（1~4 幕 + 天阙）。RunDriver 用它判定"现在是天阙"。</summary>
        public int ActCount => _acts.Length;

        public IReadOnlyList<LingerEntry> Lingers => _lingers;

        // ================================================================
        //  推进
        // ================================================================

        /// <summary>
        /// 进入一个节点。<paramref name="offset"/> 必须是当前图的合法下一层节点。
        /// 返回 false = 非法移动（越层/回退/越幕），调用方应提示而不是静默吞掉。
        /// </summary>
        public bool EnterNode(int offset)
        {
            var g = CurrentGraph;
            if (g.IsEmpty) return false;                       // 空幕没有节点（后土）
            if (AtBoss) return false;                          // 节点走满了，该打 Boss
            int expected = CurrentLayer + 1;
            if (CurrentGraph.LayerOf(offset) != expected) return false;

            CurrentOffset = offset;
            VisitedInAct++;
            Path.Add(g.Terms[offset]);

            // 余气消耗：进入节点 = 消耗一格，**扣到负数才移除** ——
            // NodesLeft=2 表示"未来 2 个节点（含本节点）都生效"。若扣到 0 就移除，
            // 余气实际只在第 1 个节点生效，与 GDD"残留 2 个节点"不符（实测踩过）。
            for (int i = _lingers.Count - 1; i >= 0; i--)
            {
                var l = _lingers[i];
                l.NodesLeft--;
                if (l.NodesLeft < 0) _lingers.RemoveAt(i);
                else _lingers[i] = l;
            }
            return true;
        }

        /// <summary>
        /// 击败守关，进入下一幕。新幕的余气在这里生成（上一幕**第一个**经过的
        /// 节点的天时，强度减半、残留 2 个节点 —— 取舍见文件头）。
        /// 返回 false = 本幕节点还没走满 / 已通关（⚠ Finished 判定必须在 AtBoss
        /// 之前 —— 通关后 AtBoss 走收敛逻辑恒真，先判它会让"重复打 Boss"不被拒）。
        /// </summary>
        public bool DefeatBoss()
        {
            if (Finished) return false;
            if (!AtBoss) return false;

            BossesDefeated++;
            CurrentAct++;
            VisitedInAct = 0;
            CurrentOffset = -1;
            _lingers.Clear();

            if (Finished) return true;                         // 后土已败，全剧终

            var prevGraph = _acts[CurrentAct - 2];
            if (prevGraph.IsEmpty || Path.Count == 0) return true;

            // 上一幕的第一个节点 = Path 里倒数第 prevGraph.PathLength 个
            int firstTermOfPrevAct = Path[Path.Count - prevGraph.PathLength];
            var weather = _termWeather?.Invoke(firstTermOfPrevAct);
            if (weather != null)
            {
                _lingers.Add(new LingerEntry
                {
                    Weather = weather.ScaledHalf("linger_" + weather.Id),
                    NodesLeft = _lingerNodes,
                    FromTerm = firstTermOfPrevAct,
                });
            }
            return true;
        }

        /// <summary>
        /// 当前节点（或守关）的战斗天时：节点天时 + 活跃余气，一次合成。
        /// ⚠ 只要站在节点上（CurrentOffset >= 0）就有节点天时 —— 包括**收尾节点**：
        ///   AtBoss 只表示"节点走满、下一场是守关"，收尾节点自己的战斗照样要天时。
        /// 无节点天时且无余气时返回 null（= 本场无天时，BattleFactory 收 null 即可）。
        /// </summary>
        public WanXiang.Battle.Core.WeatherDef ComposeCurrentWeather()
        {
            var node = CurrentOffset >= 0
                ? _termWeather?.Invoke(CurrentGraph.Terms[CurrentOffset])
                : null;

            if (_lingers.Count == 0) return node;

            var lingers = new WanXiang.Battle.Core.WeatherDef[_lingers.Count];
            for (int i = 0; i < _lingers.Count; i++) lingers[i] = _lingers[i].Weather;
            return WeatherComposer.Compose(node, lingers);
        }
    }
}
