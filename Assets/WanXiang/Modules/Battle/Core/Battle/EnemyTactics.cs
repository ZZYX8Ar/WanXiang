// ============================================================================
//  敌方战术档案（EnemyTactics）—— 2026-10-16
//  ---------------------------------------------------------------------------
//  为什么要这一层（用户："给敌人重新设计得更有趣一些，他们也可以给玩家上 debuff"）：
//
//  现状体检（实测 50 只兽的技能原子）：
//    · ApplyStatus 共 37 处，**全部指向敌方**（所以"敌人给玩家上减益"机制上早就通了）
//    · 但只用了 **4 种**状态：灼烧 / 霜 / 湿 / 裂甲
//    · 状态库里另外 **9 种减益从未被任何技能使用**：冻结 / 根缚 / 沉默 / 混乱 /
//      蚀 / 瘴气 / 感电 / 斩标 / 冰蚀
//    ⇒ 敌人"每回合就是打你一下 + 挂个灼烧"，没有控制、没有易伤、没有打断。
//
//  本层的做法：**给敌人挂"战术性格"** —— 按兽 id 确定性分配一条战术，
//  把一个「施加状态」原子叠到它某个技能的末尾（走的是现成的 EffectAtom.Status，
//  ⛔ 不新增任何机制类型，所以战斗循环一行都不用改）。
//
//  ★ 三个设计约束（都是刻意选的）：
//   ① **确定性**：同一只兽永远同一个战术（按 id 哈希）⇒ 玩家能学会、可针对，
//     编队面板也能提前显示（不是开打才知道）。不按种子随机，避免"看不懂对面要干嘛"。
//   ② **硬控只挂在绝技上**：冻结/根缚/混乱都是 PreventsAction（一回合不能动），
//     挂普攻就是每回合被摁死；挂绝技（要攒满元气，约 4~5 回合一次）才有博弈空间。
//   ③ **软减益挂普攻/战技**：减攻（瘴气）、易伤（感电）、掉血（蚀）是持续压迫，
//     频率高但不打断行动，累积感最强。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>战术挂在哪个技能上。</summary>
    public enum TacticSlot
    {
        Basic = 0,
        Active = 1,
        Ultimate = 2,
    }

    /// <summary>一条敌方战术（= 一种"敌人性格"）。</summary>
    public sealed class TacticDef
    {
        public string Id;
        /// <summary>战术名（如「缚灵」），会显示在敌人的详情面板里。</summary>
        public string Name;
        /// <summary>施加的状态 id（取自 StatusCatalog）。</summary>
        public string StatusId;
        public int Stacks;
        /// <summary>持续回合（1 = 只影响对手的下一个行动）。</summary>
        public int Turns;
        public TacticSlot Slot;
        /// <summary>施放目标（默认按技能自身的主目标解析）。</summary>
        public TargetSelector Target;
        /// <summary>给玩家看的说明（详情面板/tooltip 用）。</summary>
        public string Note;
    }

    public static class EnemyTactics
    {
        /// <summary>概率分母：按兽 id 哈希取模，小于这个值才发战术（其余是"朴素"敌人）。</summary>
        public const uint CoverageMod = 5u;      // 5 取模里 0~2 发战术 ⇒ 约 60% 的敌人有性格
        public const uint CoverageMax = 3u;

        /// <summary>残血反扑：敌人 HP 低于这个比例时永久加攻。</summary>
        public const float EnrageHpPercent = 0.35f;
        /// <summary>残血反扑的加攻幅度。</summary>
        public const float EnrageAttackBonus = 0.30f;

        /// <summary>
        /// 总开关（默认开）。关掉它 = 回到"敌人没有战术性格"的老规则 ——
        /// 只给指纹对照实验用（验证"指纹变化确实只来自这一层"），别在运行期改。
        /// </summary>
        public static bool Enabled = true;

        private static readonly TacticDef[] Table =
        {
            // ---- 软减益：挂普攻/战技，频率高但不打断 ----
            new TacticDef { Id = "miasma", Name = "瘴疠", StatusId = StatusCatalog.Miasma,
                Stacks = 1, Turns = 3, Slot = TacticSlot.Basic,
                Note = "普攻附带瘴气：每层使目标攻击 -3%（可叠 10 层）" },

            new TacticDef { Id = "corrode", Name = "蚀骨", StatusId = StatusCatalog.Corrode,
                Stacks = 1, Turns = 3, Slot = TacticSlot.Basic,
                Note = "普攻附带蚀：逐回合持续掉血" },

            new TacticDef { Id = "shock", Name = "引雷", StatusId = StatusCatalog.Shock,
                Stacks = 1, Turns = 3, Slot = TacticSlot.Active,
                Note = "战技附带感电：目标每层受金/水属性伤害 +6%（可被再次引爆）" },

            new TacticDef { Id = "silence", Name = "封口", StatusId = StatusCatalog.Silence,
                Stacks = 1, Turns = 1, Slot = TacticSlot.Active,
                Note = "战技附带沉默：目标一回合内无法释放技能（普攻仍可）" },

            // ---- 硬控 / 重压：只挂绝技（要攒满元气，约 4~5 回合一次）----
            new TacticDef { Id = "bind", Name = "缚灵", StatusId = StatusCatalog.Root,
                Stacks = 1, Turns = 1, Slot = TacticSlot.Ultimate,
                Note = "绝技附带根缚：目标本回合无法行动" },

            new TacticDef { Id = "confuse", Name = "蛊惑", StatusId = StatusCatalog.Confuse,
                Stacks = 1, Turns = 1, Slot = TacticSlot.Ultimate,
                Note = "绝技附带混乱：目标本回合无法行动（清明天时免疫）" },

            new TacticDef { Id = "freeze", Name = "冰封", StatusId = StatusCatalog.Freeze,
                Stacks = 1, Turns = 1, Slot = TacticSlot.Ultimate,
                Note = "绝技附带冻结：目标无法行动、无法被治疗，且受伤 +20%" },

            new TacticDef { Id = "marked", Name = "斩标", StatusId = StatusCatalog.Marked,
                Stacks = 1, Turns = 2, Slot = TacticSlot.Ultimate,
                Note = "绝技附带斩标：目标受到的所有伤害 +20%（2 回合）" },
        };

        public static IReadOnlyList<TacticDef> All => Table;

        public static TacticDef ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        /// <summary>
        /// 这只兽有没有战术（确定性：只看 beastId，不含种子 —— 玩家要能学会）。
        /// 返回 null = 朴素敌人（仍然只有原本的伤害/灼烧）。
        /// </summary>
        public static TacticDef Of(string beastId)
        {
            if (string.IsNullOrEmpty(beastId)) return null;
            // ⚠ 全程用 **uint** 算：FNV 哈希是 32 位无符号，强转 int 会得到负数 ⇒ 取模出负下标越界。
            uint h = CoreMath.Fnv1a("tactic:" + beastId);
            if (h % CoverageMod >= CoverageMax) return null;     // 约 40% 是朴素敌人
            return Table[(int)((h / CoverageMod) % (uint)Table.Length)];
        }

        /// <summary>
        /// 该战术对应的效果原子（复用现成 ApplyStatus，战斗循环不用改）。
        /// ⚠ 目标阵营不用管：`PoolFor` 看到"ApplyStatus + 减益"会自动取**对侧**作为目标池，
        ///   所以这里只决定"对侧里的哪一只" —— 取生命最低的（控制/易伤都往软柿子上捏）。
        /// </summary>
        public static EffectAtom AtomOf(TacticDef t)
        {
            if (t == null) return default;
            var target = t.Target == TargetSelector.Self ? TargetSelector.SingleLowestHp : t.Target;
            return EffectAtom.Status(target, t.StatusId, t.Stacks, t.Turns);
        }

        /// <summary>
        /// 给敌方兽叠上战术（**返回新对象**，不改原 def —— 敌我共用同一份图鉴定义，
        /// 绝不能就地改，否则玩家自己的那只兽也会中招）。
        /// </summary>
        public static BeastDef Apply(BeastDef def, TacticDef t)
        {
            if (def == null || t == null) return def;
            var clone = def.Clone();
            switch (t.Slot)
            {
                case TacticSlot.Basic: clone.Basic = WithTactic(clone.Basic, t); break;
                case TacticSlot.Active: clone.Active = WithTactic(clone.Active, t); break;
                default: clone.Ultimate = WithTactic(clone.Ultimate, t); break;
            }
            clone.TacticName = t.Name;
            clone.TacticNote = t.Note;
            return clone;
        }

        /// <summary>把战术原子追加到某个技能的效果末尾（技能为空则跳过）。</summary>
        private static SkillDef WithTactic(SkillDef sk, TacticDef t)
        {
            if (sk == null) return null;
            var arr = sk.Effects;
            int n = arr != null ? arr.Length : 0;
            var next = new EffectAtom[n + 1];
            for (int i = 0; i < n; i++) next[i] = arr[i];
            next[n] = AtomOf(t);
            var clone = new SkillDef
            {
                Id = sk.Id, Name = sk.Name, Type = sk.Type, Cd = sk.Cd,
                Element = sk.Element, PrimaryTarget = sk.PrimaryTarget,
                Description = sk.Description,
                Effects = next,
            };
            return clone;
        }

        // ================================================================
        //  残血反扑
        // ================================================================

        /// <summary>
        /// 敌方残血反扑（每回合开始调）：HP 跌破 <see cref="EnrageHpPercent"/> 的敌人**一次性**永久加攻。
        /// ⛔ 只判敌方、只触发一次（<c>BattleUnit.Enraged</c> 标记）——
        ///   否则每回合无限叠加，回合数一长敌人攻击力会爆掉。
        /// </summary>
        public static void EnrageWoundedEnemies(BattleState st)
        {
            if (st == null || !Enabled) return;
            var foes = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < foes.Count; i++)
            {
                var u = foes[i];
                if (u == null || !u.IsAlive || u.Enraged) continue;
                if (u.HpPercent > EnrageHpPercent) continue;
                u.Enraged = true;
                u.PermanentAttackBonus += EnrageAttackBonus;
                st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: u.RuntimeId,
                           targetId: u.RuntimeId, amount: 1,
                           note: "残血反扑：" + u.DisplayName + " 攻击 +" + (int)(EnrageAttackBonus * 100f) + "%");
            }
        }
    }
}