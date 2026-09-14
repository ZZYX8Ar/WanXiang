// ============================================================================
//  万相 · 战斗核心 · 五行矩阵
//  ---------------------------------------------------------------------------
//  对应 GDD 第二章 2.2 / 2.3。
//
//  ⭐ 关键设计：5x5 的 25 个系数**不抄表**，而是由两个环 + 四个系数推导出来。
//
//     相克环：木克土 · 土克水 · 水克火 · 火克金 · 金克木
//     相生环：木生火 · 火生土 · 土生金 · 金生水 · 水生木
//
//     攻方 → 守方 的系数只有四种可能：
//       守方是攻方所克   → 1.50
//       守方是攻方所被克 → 0.75
//       同属             → 0.90
//       其它             → 1.00
//
//     于是 25 个数是 **5 个环上事实 + 4 个系数** 的函数。抄 25 个数字时抄错一格
//     不会有人发现（它只表现为"某个对位伤害怪怪的"），但环抄错会立刻崩掉一大片。
//     从环推导，等于把 25 个校验点压成 10 个。
//
//  同时提供 <see cref="SelfCheck"/>：拿 GDD 里那张表逐格比对推导结果，
//  一次性确认"我们理解的规则"和"文档写的那张表"是同一件事。
//  自检在编辑器命令 battle.selftest 里跑，不过就红。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>五行伤害系数。四个值都可由策划调，改完立刻能在战斗中观察到（GDD 7 章验收③）。</summary>
    public struct ElementCoefficients
    {
        public float Counter;    // 攻方克守方
        public float Countered;  // 攻方被守方克
        public float Same;       // 同属
        public float Neutral;    // 无关系

        /// <summary>GDD 2.3 的默认值：1.50 / 0.75 / 0.90 / 1.00。</summary>
        public static ElementCoefficients Default => new ElementCoefficients
        {
            Counter = 1.50f,
            Countered = 0.75f,
            Same = 0.90f,
            Neutral = 1.00f,
        };
    }

    public static class ElementMatrix
    {
        /// <summary>五行全集，顺序固定 —— 顺序变了指纹就变了。</summary>
        public static readonly Element[] All =
        {
            Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water,
        };

        /// <summary>
        /// 相克环：每一项克其**下一项**（木→土→水→火→金→木）。
        /// 注意不是"木火土金水"那个顺序 —— 相克是隔一项，所以环的顺序本身就是隔一项的。
        /// </summary>
        public static readonly Element[] CounterRing =
        {
            Element.Wood, Element.Earth, Element.Water, Element.Fire, Element.Metal,
        };

        /// <summary>相生环：每一项生其**下一项**（木→火→土→金→水→木）。</summary>
        public static readonly Element[] GenerateRing =
        {
            Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water,
        };

        private static int RingIndexOf(Element[] ring, Element e)
        {
            for (int i = 0; i < ring.Length; i++)
                if (ring[i] == e) return i;
            return -1;
        }

        /// <summary>attacker 是否克制 defender。</summary>
        public static bool Counters(Element attacker, Element defender)
        {
            if (attacker == Element.None || defender == Element.None) return false;
            int i = RingIndexOf(CounterRing, attacker);
            return i >= 0 && CounterRing[(i + 1) % 5] == defender;
        }

        /// <summary>from 是否生 to。注意**同属性不算相生** —— GDD 2.4 明确"相生只认相生不认相克"，
        /// 且 2.5 的同属共鸣是另一条独立规则，两条不能混。</summary>
        public static bool Generates(Element from, Element to)
        {
            if (from == Element.None || to == Element.None) return false;
            if (from == to) return false;
            int i = RingIndexOf(GenerateRing, from);
            return i >= 0 && GenerateRing[(i + 1) % 5] == to;
        }

        /// <summary>derive：从环推导攻-守关系。这是唯一真相源，不要在别处另写一套。</summary>
        public static ElementRelation Relation(Element attacker, Element defender)
        {
            if (attacker == Element.None || defender == Element.None) return ElementRelation.Neutral;
            if (attacker == defender) return ElementRelation.Same;
            if (Counters(attacker, defender)) return ElementRelation.Counter;
            if (Counters(defender, attacker)) return ElementRelation.Countered;
            return ElementRelation.Neutral;
        }

        /// <summary>
        /// 取伤害系数。**推导，不查表。**
        /// 相生关系不参与伤害计算 —— GDD 2.3 的设计说明：相生只产出羁绊增益，
        /// 避免"同一体系既给伤害又给生存"导致数值失控。
        /// </summary>
        public static float Coefficient(Element attacker, Element defender, ElementCoefficients c)
        {
            switch (Relation(attacker, defender))
            {
                case ElementRelation.Counter: return c.Counter;
                case ElementRelation.Countered: return c.Countered;
                case ElementRelation.Same: return c.Same;
                default: return c.Neutral;
            }
        }

        /// <summary>GDD 2.3 那张表，逐格抄录，只用于 SelfCheck 比对，**不参与运行**。</summary>
        private static readonly float[] DocTable =
        {
            //        木     火     土     金     水
            /* 木 */ 0.90f, 1.00f, 1.50f, 0.75f, 1.00f,
            /* 火 */ 1.00f, 0.90f, 1.00f, 1.50f, 0.75f,
            /* 土 */ 0.75f, 1.00f, 0.90f, 1.00f, 1.50f,
            /* 金 */ 1.50f, 0.75f, 1.00f, 0.90f, 1.00f,
            /* 水 */ 1.00f, 1.50f, 0.75f, 1.00f, 0.90f,
        };

        /// <summary>
        /// 自检：把推导结果与文档那张表逐格比对，并检查两个环的代数性质。
        /// 返回 null 表示全过；否则返回第一条失败原因（中文，能直接贴进报告）。
        /// </summary>
        public static string SelfCheck(ElementCoefficients c)
        {
            // ① 与文档表逐格比对
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    float derived = Coefficient(All[i], All[j], c);
                    float doc = DocTable[i * 5 + j];
                    if (CoreMath.Abs(derived - doc) > 1e-6f)
                    {
                        return $"矩阵[{Cn.Of(All[i])}→{Cn.Of(All[j])}]：推导得 {derived:F2}，" +
                               $"但 GDD 2.3 写的是 {doc:F2}";
                    }
                }
            }

            // ② 每个属性必须恰好克 1 个、生 1 个、被克 1 个、被生 1 个
            foreach (var e in All)
            {
                int nCounter = 0, nCounteredBy = 0, nGenerate = 0, nGeneratedBy = 0;
                foreach (var o in All)
                {
                    if (Counters(e, o)) nCounter++;
                    if (Counters(o, e)) nCounteredBy++;
                    if (Generates(e, o)) nGenerate++;
                    if (Generates(o, e)) nGeneratedBy++;
                }
                if (nCounter != 1 || nCounteredBy != 1 || nGenerate != 1 || nGeneratedBy != 1)
                {
                    return $"{Cn.Of(e)} 的环上度数不对：克 {nCounter}、被克 {nCounteredBy}、" +
                           $"生 {nGenerate}、被生 {nGeneratedBy}（各应为 1）";
                }
            }

            // ③ 相生与相克不能同时成立（否则规则自相矛盾）
            foreach (var a in All)
            {
                foreach (var b in All)
                {
                    if (Counters(a, b) && Generates(a, b))
                        return $"{Cn.Of(a)} 同时对 {Cn.Of(b)} 既相克又相生";
                }
            }

            // ④ 环本身必须是一笔画闭合的 5 元置换（没有重复项）
            if (!IsPermutation(CounterRing)) return "相克环里有重复项";
            if (!IsPermutation(GenerateRing)) return "相生环里有重复项";

            return null;
        }

        private static bool IsPermutation(Element[] ring)
        {
            if (ring.Length != 5) return false;
            foreach (var e in All)
            {
                int n = 0;
                foreach (var r in ring) if (r == e) n++;
                if (n != 1) return false;
            }
            return true;
        }
    }

    public enum ElementRelation
    {
        Neutral = 0,
        Counter = 1,    // 攻克守
        Countered = 2,  // 攻被守克
        Same = 3,
    }
}
