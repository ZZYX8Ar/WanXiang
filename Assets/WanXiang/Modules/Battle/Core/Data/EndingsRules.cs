// ============================================================================
//  万相 · 结局规则（B4-2 骨架，Battle/Core/Data）
//  ---------------------------------------------------------------------------
//  设计：《万相_结局与节点_重构设计_v1》§4.3 —— 判定顺序固定、互斥（一次只出一个）。
//  数据：endings.csv（id/name/condition/priority/reward_kind/reward_value/hint/is_true）
//  条件表达式：`key>=num` 与 `key`（布尔）用 `&` 连接；`default` 恒真。
//    支持的 key（评估上下文由调用方给，Core 不依赖 RunState）：
//      marks>=N   持有印记数（GrantedItems 中 item_tu_* 计数）
//      houtu      已通过「后土试炼」
//      n>=N       挑战难度档位
//      captures>=N 本局捕获数
//      no_relic   全程未装备遗物
//      realm>=N   境（旧体系进度）
//  印记授予来源（Spring 第 3 选项 / Elite 残碑 / 事件 grant_item）→ B4-3 接。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public sealed class EndingDef
    {
        public string Id;
        public string Name;
        public string Condition;
        public int Priority;
        public string RewardKind;
        public string RewardValue;
        public string Hint;
        public bool IsTrue;
    }

    public static class EndingsRules
    {
        private static readonly List<EndingDef> _all = new List<EndingDef>();
        public static IReadOnlyList<EndingDef> All => _all;
        public static int Count => _all.Count;

        public static void Install(List<EndingDef> rows)
        {
            _all.Clear();
            if (rows != null) _all.AddRange(rows);
            // priority 升序（1 最高）——评估时按此顺序取第一个满足者
            _all.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>评估上下文（调用方从 RunState 折算；Core 不依赖 UI 层）。</summary>
        public struct Ctx
        {
            public int MarkCount;      // 已持印记数（item_tu_* 计数）
            public bool HoutuCleared;  // 已通过后土试炼
            public int N;              // 挑战难度档位
            public int Captures;       // 本局捕获数
            public int RelicCount;     // 已装备遗物数
            public int Realm;          // 境
        }

        /// <summary>终局判定：按 priority 升序取**第一个**满足条件的结局；全不满足则 null（调用方兜底 end_return）。</summary>
        public static EndingDef Evaluate(in Ctx c)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (EvalCondition(_all[i].Condition, c)) return _all[i];
            }
            return null;
        }

        /// <summary>条件表达式求值（`a>=1&b` / `default`；不认识的 key 视为不满足，不抛异常）。</summary>
        public static bool EvalCondition(string cond, in Ctx c)
        {
            if (string.IsNullOrEmpty(cond) || cond == "default") return true;
            var parts = cond.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p == "default") continue;
                if (p == "houtu") { if (!c.HoutuCleared) return false; continue; }
                if (p == "no_relic") { if (c.RelicCount != 0) return false; continue; }
                int ge = p.IndexOf(">=");
                if (ge > 0)
                {
                    string key = p.Substring(0, ge).Trim();
                    int need;
                    if (!int.TryParse(p.Substring(ge + 2).Trim(), out need)) return false;
                    int val = ValueOf(key, c);
                    if (val < 0) return false;              // 未知 key = 不满足（不抛）
                    if (val < need) return false;
                }
                else return false;                          // 单词条件（非 bool/非 default）不认识 → 不满足
            }
            return true;
        }

        private static int ValueOf(string key, in Ctx c)
        {
            switch (key)
            {
                case "marks": return c.MarkCount;
                case "n": return c.N;
                case "captures": return c.Captures;
                case "realm": return c.Realm;
                default: return -1;                     // 未知 key
            }
        }
    }
}
