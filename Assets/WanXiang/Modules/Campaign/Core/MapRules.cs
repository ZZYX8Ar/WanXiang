// ============================================================================
//  万相 · 地图生成规则（可外置）
//  ---------------------------------------------------------------------------
//  2026-11-02：把「每幕几层 / 每层几候选 / 节点权重 / 何时解禁」从
//  SolarTermGraph 里抽出来，允许 Resources/Data/map.csv + node_weights.csv 覆盖。
//
//  ⛔ 为什么单独一个类而不是继续写在 SolarTermGraph 里：
//    SolarTermGraph 的生成器是**纯函数 + 种子**，规则一变指纹就变；
//    把"可调参数"集中到一处，才能一眼看出"改了哪些东西会让地图不一样"。
//
//  ⛔ 默认值必须与 2026-11-02 的硬编码值逐项一致（18 层 / 每层 3 候选 / 权重
//    34-11-9-10-12-9-15-5 / 工坊与秘境第 2 幕解禁）。改默认值 = 改所有老种子的地图。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>路线图生成规则（外置表 map.csv / node_weights.csv 的落点）。</summary>
    public static class MapRules
    {
        /// <summary>随机生成的最后一幕（第 5 幕走固定 finale 图，不吃这些规则）。</summary>
        public const int LastRandomAct = 4;

        /// <summary>每幕层数（含末层守关）。</summary>
        public static int Layers = 18;

        /// <summary>普通层每层候选数（守关层恒为 1）。</summary>
        public static int Width = 3;

        /// <summary>节点权重（顺序即抽取顺序，决定同权重时的落点，别随意重排）。</summary>
        private static readonly List<KeyValuePair<NodeKind, int>> _weights = new List<KeyValuePair<NodeKind, int>>
        {
            new KeyValuePair<NodeKind, int>(NodeKind.Encounter, 34),
            new KeyValuePair<NodeKind, int>(NodeKind.Elite, 11),
            new KeyValuePair<NodeKind, int>(NodeKind.Shop, 9),
            new KeyValuePair<NodeKind, int>(NodeKind.Nest, 10),
            new KeyValuePair<NodeKind, int>(NodeKind.Tale, 12),
            new KeyValuePair<NodeKind, int>(NodeKind.Forge, 9),
            new KeyValuePair<NodeKind, int>(NodeKind.Question, 15),
            new KeyValuePair<NodeKind, int>(NodeKind.Realm, 5),
        };

        /// <summary>每种节点从第几幕开始解禁（1 = 第 1 幕就有）。</summary>
        private static readonly Dictionary<NodeKind, int> _minAct = new Dictionary<NodeKind, int>
        {
            { NodeKind.Encounter, 1 }, { NodeKind.Elite, 1 }, { NodeKind.Shop, 1 },
            { NodeKind.Nest, 1 }, { NodeKind.Tale, 1 },
            // 工坊第 2 幕才解禁（第 1 幕就出熔炼会让难度曲线崩）；
            // 秘境第 2 幕起解禁（第 1 幕玩家还没学会格挡，进去是纯挫败）。
            { NodeKind.Forge, 2 }, { NodeKind.Realm, 2 },
            { NodeKind.Question, 1 },
        };

        /// <summary>已从外置表安装的权重条数（0 = 用内置表）。</summary>
        public static int InstalledWeights { get; private set; }

        /// <summary>覆盖层数与每层候选数（≤0 表示不改）。</summary>
        public static void InstallShape(int layers, int width)
        {
            if (layers > 0) Layers = layers;
            if (width > 0) Width = width;
        }

        /// <summary>覆盖一种节点的权重与解禁幕（weight ≤0 表示不改权重；minAct ≤0 表示不改解禁幕）。</summary>
        public static void InstallNode(NodeKind kind, int weight, int minAct)
        {
            if (weight > 0)
            {
                bool found = false;
                for (int i = 0; i < _weights.Count; i++)
                {
                    if (_weights[i].Key != kind) continue;
                    _weights[i] = new KeyValuePair<NodeKind, int>(kind, weight);
                    found = true;
                    break;
                }
                if (!found) _weights.Add(new KeyValuePair<NodeKind, int>(kind, weight));
                InstalledWeights++;
            }
            if (minAct > 0) _minAct[kind] = minAct;
        }

        /// <summary>该幕是否解禁这种节点。</summary>
        public static bool UnlockedIn(NodeKind kind, int act)
        {
            int min;
            if (!_minAct.TryGetValue(kind, out min)) min = 1;
            return act >= min && act <= LastRandomAct;
        }

        /// <summary>按权重＋该幕的解禁表抽一种节点（供 SolarTermGraph.PickKind 调用）。</summary>
        public static NodeKind Pick(DeterministicRandom rng, int act)
        {
            int total = 0;
            for (int i = 0; i < _weights.Count; i++)
                if (UnlockedIn(_weights[i].Key, act)) total += _weights[i].Value;
            if (total <= 0) return NodeKind.Encounter;

            int roll = rng.NextInt(0, total);
            for (int i = 0; i < _weights.Count; i++)
            {
                var kv = _weights[i];
                if (!UnlockedIn(kv.Key, act)) continue;
                if (roll < kv.Value) return kv.Key;
                roll -= kv.Value;
            }
            return NodeKind.Encounter;   // 兜底（理论不可达）
        }

        /// <summary>该幕解禁的全部节点类型（自检的"本幕解锁类型全覆盖"要用）。</summary>
        public static List<NodeKind> UnlockedList(int act)
        {
            var list = new List<NodeKind>();
            for (int i = 0; i < _weights.Count; i++)
                if (UnlockedIn(_weights[i].Key, act)) list.Add(_weights[i].Key);
            return list;
        }

        /// <summary>权重全表（导出器/自检用）。</summary>
        public static IReadOnlyList<KeyValuePair<NodeKind, int>> Weights => _weights;

        /// <summary>解禁幕表（导出器/自检用）。</summary>
        public static IReadOnlyDictionary<NodeKind, int> MinActs => _minAct;
    }
}
