// ============================================================================
//  万相 · 战斗核心 · 视图帧流（给表现层的"画面快照"）
//  ---------------------------------------------------------------------------
//  表现层（灰盒编辑器窗口 / 未来的 Unity BoardView / 导出成网页回放）都要回答
//  同一个问题：**这一刻场上长什么样？**
//
//  有两种做法，这里明确选后者：
//
//    (a) 表现层自己读事件流，自己算 —— "护盾先吃伤害、怒气 +10、CD 减 1"…
//        ⇒ 等于把战斗规则复制第二份。第一份改了第二份没改，就出现
//          "视图显示的血量和逻辑里的不一样"这种极难察觉的 bug，
//          而且它只在特定顺序下复现。
//
//    (b) 核心层在每次状态变化后**把改变了的单位存一份快照**。
//        ⇒ 表现层只做一件事：把快照画出来。它连"护盾"这个概念都不需要知道。
//
//  取 (b)。代价是每场战斗多几百个几十字节的结构体（可关，见 CaptureFrames），
//  换来的是**表现层不可能和逻辑不一致** —— 这个交易在任何项目里都划算。
//
//  ⚠ 帧流**不参与指纹**。指纹只认 BattleLog 的事件流；帧流是它的衍生物，
//    改帧的内容不应该让"可复现性"测试变红。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>一个单位在某一刻的画面状态。够画一根血条、一条怒气、三个 CD、一排状态图标。</summary>
    public struct UnitSnapshot : System.IEquatable<UnitSnapshot>
    {
        public string UnitId;
        public string Name;
        public Element Element;

        public bool Alive;
        public int Hp;
        public int MaxHp;
        public int Shield;
        public float Rage;

        /// <summary>三个技能的剩余 CD（0=普攻 1=战技 2=绝技）。</summary>
        public int Cd0, Cd1, Cd2;

        /// <summary>状态的紧凑文本（"灼烧×3 同气×2"）。灰盒直接把它画出来；
        /// 做成文本而不是结构化列表，是因为灰盒阶段不需要点状态图标看详情。</summary>
        public string Statuses;

        /// <summary>同属共鸣的攻击加成（0 / 0.08 / 0.16 / 0.25）。GDD 要求共鸣是玩家看得见的收益，
        /// 灰盒至少得把它标出来，否则"凑属性"这件事在画面上完全无迹可寻。</summary>
        public float ResonanceBonus;

        public float HpRatio => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;
        public int Cd(int slot) => slot == 0 ? Cd0 : (slot == 1 ? Cd1 : Cd2);

        public bool Equals(UnitSnapshot o)
        {
            return Alive == o.Alive
                && Hp == o.Hp && MaxHp == o.MaxHp && Shield == o.Shield
                && Rage == o.Rage
                && Cd0 == o.Cd0 && Cd1 == o.Cd1 && Cd2 == o.Cd2
                && Statuses == o.Statuses
                && ResonanceBonus == o.ResonanceBonus
                && Element == o.Element;
        }

        public override bool Equals(object obj) => obj is UnitSnapshot s && Equals(s);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + Hp; h = h * 31 + Shield;
                h = h * 31 + (int)Rage;
                h = h * 31 + Cd0; h = h * 31 + Cd1; h = h * 31 + Cd2;
                h = h * 31 + (int)Element;
                return h;
            }
        }
    }

    /// <summary>
    /// 一帧。只装**相对上一帧变了的**单位 —— 满编 10 个单位、每次伤害只动 1 个，
    /// 存全量的话体积是这里的十倍，而且没有任何收益。
    /// </summary>
    public struct ViewFrame
    {
        /// <summary>这一帧发生在第几条事件之后（BattleLog 的下标）。</summary>
        public int EventIndex;
        public int Turn;

        /// <summary>本帧有变化的单位。顺序沿用 BattleState.AllUnits。</summary>
        public UnitSnapshot[] Changed;
    }

    /// <summary>把 BattleUnit 转成快照的唯一入口。</summary>
    public static class ViewSnapshot
    {
        public static UnitSnapshot Of(BattleUnit u)
        {
            var s = new UnitSnapshot
            {
                UnitId = u.RuntimeId,
                Name = u.DisplayName,
                Element = u.Element,
                Alive = u.IsAlive,
                Hp = u.Hp,
                MaxHp = u.MaxHp,
                Shield = u.Shield,
                Rage = u.Rage,
                Cd0 = u.Cooldowns[0],
                Cd1 = u.Cooldowns[1],
                Cd2 = u.Cooldowns[2],
                Statuses = DescribeStatuses(u),
                ResonanceBonus = u.ResonanceAttackBonus,
            };
            return s;
        }

        public static string DescribeStatuses(BattleUnit u)
        {
            if (u.Statuses.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < u.Statuses.Count; i++)
            {
                var st = u.Statuses[i];
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(st.Def.Name);
                if (st.Stacks > 1) sb.Append('×').Append(st.Stacks);
            }
            return sb.ToString();
        }
    }
}
