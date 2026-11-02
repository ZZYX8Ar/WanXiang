// ============================================================================
//  技能改写表（2026-11-01）
//  ---------------------------------------------------------------------------
//  设计稿 §8 给了 50 只异兽的新技能，但内容表（ContentCatalog）里还是旧技能。
//  这里用"覆盖表"的方式逐步替换：**不改 asset，只在组队时覆盖克隆体**，
//  这样没改到的兽完全不受影响，也不会污染内容目录。
//  ⚠ 目前只落了 5 只（每种元素 1 只）作为样板；其余按同一格式继续补。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Campaign
{
    public sealed class SkillOverride
    {
        public bool Enemy;            // true = 敌方专用覆盖（同 id 敌我技能可以不同）
        public string BeastId;
        public int Slot;              // 0 普攻 / 1 战技 / 2 绝技
        public string Name;
        public string Desc;           // 写清【挂】【消耗】【引爆】与层数
        public float PowerMul = 1f;   // 相对原技能的倍率
        public int HitsDelta;         // 段数变化
    }

    public static class SkillOverrideCatalog
    {
        private static readonly SkillOverride[] All =
        {
            // ---- 木：句芒（术·春生叠层）----
            new SkillOverride { BeastId = "jumang", Slot = 0, Name = "春藤",
                Desc = "单体 0.6×攻击；【挂】木 1 层（消耗充能）。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "jumang", Slot = 1, Name = "草木萌发",
                Desc = "全体【挂】木 2 层（消耗 2 充能）。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "jumang", Slot = 2, Name = "春生",
                Desc = "引爆木附着：全队回复 1.0×攻击；若触发润生（木+水）额外 +50%。", PowerMul = 1.00f },

            // ---- 木：鹿蜀（锋·消耗流核心）----
            new SkillOverride { BeastId = "lushu", Slot = 0, Name = "角抵",
                Desc = "单体 0.9×攻击；【挂】木 1 层。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "lushu", Slot = 1, Name = "踏枝",
                Desc = "单体 1.5×攻击。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "lushu", Slot = 2, Name = "枯荣一线",
                Desc = "【消耗】目标全部木附着：伤害 1.8×攻击 ×(1+0.35×层数)。", PowerMul = 1.20f },

            // ---- 火：祝融（术·焚天）----
            new SkillOverride { BeastId = "zhurong", Slot = 0, Name = "引火",
                Desc = "单体 0.6×攻击；【挂】火 1 层。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "zhurong", Slot = 1, Name = "焚野",
                Desc = "全体【挂】火 2 层（消耗 2 充能）。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "zhurong", Slot = 2, Name = "赤地千里",
                Desc = "引爆火附着：配水=蒸发（真伤）/ 配金=锻击（裂甲）。", PowerMul = 1.10f },

            // ---- 火：毕方（锋·处决）----
            new SkillOverride { BeastId = "bifang", Slot = 0, Name = "独足",
                Desc = "单体 1.0×攻击。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "bifang", Slot = 1, Name = "燎羽",
                Desc = "单体 1.6×攻击 + 裂甲 2 回合。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "bifang", Slot = 2, Name = "焚尽",
                Desc = "处决：目标生命 <30% 时直接斩杀（首领改为 +50% 伤害）。", PowerMul = 1.15f },

            // ---- 水：禺强（术·玄冥）----
            new SkillOverride { BeastId = "yuqiang", Slot = 0, Name = "玄波",
                Desc = "单体 0.6×攻击；【挂】水 1 层。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "yuqiang", Slot = 1, Name = "北溟",
                Desc = "全体【挂】水 2 层（消耗 2 充能）。", PowerMul = 1.00f },
            new SkillOverride { BeastId = "yuqiang", Slot = 2, Name = "玄冥之息",
                Desc = "【消耗】目标全部水附着：全体伤害 1.4×攻击 ×(1+0.35×层)；消耗后冻结 1 回合。", PowerMul = 1.15f },
        };

        private static Dictionary<string, SkillOverride> _byKey;

        private static SkillOverride[] _table = All;
        private static Dictionary<string, SkillOverride> _enemyByKey;

        /// <summary>外部（UI 层从 JSON/CSV 导入后）注入覆盖表；传 null 则恢复内置样板。</summary>
        public static void Install(SkillOverride[] table)
        {
            _table = (table != null && table.Length > 0) ? table : All;
            _byKey = null;
            _enemyByKey = null;
            // 无 UnityEngine 依赖（纯 core 程序集），日志交给调用方
        }

        public static SkillOverride Get(string beastId, int slot)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, SkillOverride>();
                for (int i = 0; i < _table.Length; i++)
                    if (!_table[i].Enemy) _byKey[_table[i].BeastId + ":" + _table[i].Slot] = _table[i];
            }
            return _byKey.TryGetValue(beastId + ":" + slot, out var v) ? v : null;
        }

        /// <summary>敌方专用覆盖（没有就回退我方表，再没有返回 null）。</summary>
        public static SkillOverride GetEnemy(string beastId, int slot)
        {
            if (_enemyByKey == null)
            {
                _enemyByKey = new Dictionary<string, SkillOverride>();
                for (int i = 0; i < _table.Length; i++)
                    if (_table[i].Enemy) _enemyByKey[_table[i].BeastId + ":" + _table[i].Slot] = _table[i];
            }
            var k = beastId + ":" + slot;
            return _enemyByKey.TryGetValue(k, out var v) ? v : null;
        }

        public static int Count => _table.Length;
    }
}
