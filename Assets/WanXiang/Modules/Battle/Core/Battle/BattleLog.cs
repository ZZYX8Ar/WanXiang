// ============================================================================
//  万相 · 战斗核心 · 战斗事件流
//  ---------------------------------------------------------------------------
//  它同时服务三件事，所以值得单独一个类：
//
//    1) **可复现性的证据**。跑两次战斗，比对事件流的指纹，一致才算"可复现"。
//       只比对"谁赢了"是不够的 —— 两条完全不同的过程可以碰巧同一个结局。
//    2) **灰盒表现层的数据源**。伤害数字、出手序列、五行连线都从这里取，
//       View 不允许自己去问 BattleState"刚才发生了什么"。
//    3) **复盘**。出了问题能贴出一条有因果顺序的时间线，而不是只有最终帧。
//
//  ⚠ 事件的**顺序**是语义的一部分：相邻格结算、出手顺序、状态跳伤都必须按
//    固定顺序入队。所以这里是 List 而不是任何 Set/Dictionary。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public enum BattleEventKind
    {
        BattleStart = 0,
        TurnStart = 1,
        RoundResolve = 2,      // 回合开头的场地/棋盘结算（相邻格、天时、持续伤害）
        ActionBegin = 3,       // 某单位开始行动
        SkillCast = 4,
        Damage = 5,
        Heal = 6,
        Shield = 7,
        StatusApplied = 8,
        StatusRemoved = 9,
        Death = 10,
        Revive = 11,
        StatChange = 12,
        ActionEnd = 13,
        TurnEnd = 14,
        BattleEnd = 15,

        /// <summary>
        /// 暴击标记。**单独一条事件**而不是塞进 Damage 的 Note ——
        /// 表现层要据此放大伤害数字、加"暴击"字样，去解析文案字符串是错的做法。
        /// 它排在 Damage **之前**，这样表现层可以按事件顺序先收到暴击标记再播伤害。
        /// </summary>
        Crit = 16,
    }

    public struct BattleEvent
    {
        public int Turn;
        public BattleEventKind Kind;
        public string ActorId;      // 施动者（可为空，如回合结算）
        public string TargetId;     // 受动者（可为空）
        public string SkillName;    // 技能名（可为空）

        /// <summary>
        /// 技能槽（普攻/战技/绝技）。只有 SkillCast 事件有实际意义。
        /// 表现层要用它决定横幅样式、以及"是不是该播绝技大演出"——
        /// 去解析技能名字符串是错的做法。
        /// </summary>
        public SkillType Skill;

        public int Amount;          // 伤害/治疗/护盾数值（其它事件为 0）
        public Element Element;
        public string Note;         // 人类可读的补充说明（"相克 ×1.50"、"中宫调息"…）

        /// <summary>
        /// 参与指纹的字段。**刻意排除 Note** —— Note 是给人看的措辞，
        /// 改一句文案不应该让"可复现性"测试变红。它只覆盖会影响战局的量。
        /// </summary>
        public uint Fingerprint()
        {
            uint h = (uint)Kind;
            h = CoreMath.Combine(h, (uint)Turn);
            h = CoreMath.Combine(h, CoreMath.Fnv1a(ActorId));
            h = CoreMath.Combine(h, CoreMath.Fnv1a(TargetId));
            h = CoreMath.Combine(h, (uint)Amount);
            h = CoreMath.Combine(h, (uint)Element);
            h = CoreMath.Combine(h, (uint)Skill);
            return h;
        }

        public override string ToString()
        {
            string who = string.IsNullOrEmpty(ActorId) ? "" : ActorId;
            string whom = string.IsNullOrEmpty(TargetId) ? "" : " → " + TargetId;
            string amt = Amount != 0 ? $" [{Amount}]" : "";
            string sk = string.IsNullOrEmpty(SkillName) ? "" : $" {SkillName}";
            string note = string.IsNullOrEmpty(Note) ? "" : $"（{Note}）";
            return $"T{Turn,2} {Kind,-14} {who}{whom}{sk}{amt}{note}";
        }
    }

    public sealed class BattleLog
    {
        private readonly System.Collections.Generic.List<BattleEvent> _events
            = new System.Collections.Generic.List<BattleEvent>(512);

        private uint _fingerprint = 2166136261u;

        /// <summary>容量上限。防止某个死循环把内存吃光 —— 到顶后静默丢弃后续事件，
        /// 但**指纹照常累积**（否则指纹会随截断与否而变，反而破坏可复现性判据）。</summary>
        public int Capacity { get; set; } = 20000;

        public bool Truncated { get; private set; }
        public int Count => _events.Count;

        public System.Collections.Generic.IReadOnlyList<BattleEvent> Events => _events;

        /// <summary>滚动指纹。两次战斗跑完比对这个值即可判定过程是否一致。</summary>
        public uint Fingerprint => _fingerprint;

        /// <summary>
        /// 每入队一条事件就回调一次。<see cref="BattleState"/> 用它来抓视图帧 ——
        /// 挂在事件流上而不是散在各处结算代码里，就不会出现"改了状态却忘了抓帧"。
        /// ⚠ 它**不影响指纹**，只是旁路通知。
        /// </summary>
        public System.Action OnEventAdded;

        public void Add(BattleEvent e)
        {
            _fingerprint = CoreMath.Combine(_fingerprint, e.Fingerprint());
            if (_events.Count >= Capacity) { Truncated = true; return; }
            _events.Add(e);
            OnEventAdded?.Invoke();
        }

        public void Add(int turn, BattleEventKind kind, string actorId = null, string targetId = null,
                        string skillName = null, int amount = 0, Element element = Element.None,
                        string note = null, SkillType skill = SkillType.Basic)
        {
            Add(new BattleEvent
            {
                Turn = turn, Kind = kind, ActorId = actorId, TargetId = targetId,
                SkillName = skillName, Amount = amount, Element = element, Note = note,
                Skill = skill,
            });
        }

        public void Clear()
        {
            _events.Clear();
            _fingerprint = 2166136261u;
            Truncated = false;
        }

        /// <summary>按事件类型统计条数，供自检报告用（例：确认"相克相冲真的发生了"）。</summary>
        public int CountOf(BattleEventKind kind)
        {
            int n = 0;
            for (int i = 0; i < _events.Count; i++)
                if (_events[i].Kind == kind) n++;
            return n;
        }

        /// <summary>把事件流导出成可读文本（自检报告与控制台都用它）。</summary>
        public string Dump(int maxLines = int.MaxValue)
        {
            var sb = new System.Text.StringBuilder();
            int n = CoreMath.Min(_events.Count, maxLines);
            for (int i = 0; i < n; i++) sb.AppendLine(_events[i].ToString());
            if (n < _events.Count) sb.AppendLine($"…（共 {_events.Count} 条，只显示前 {n} 条）");
            if (Truncated) sb.AppendLine($"⚠ 事件数超过上限 {Capacity}，后续事件未记录（指纹仍在累积）");
            return sb.ToString();
        }
    }
}
