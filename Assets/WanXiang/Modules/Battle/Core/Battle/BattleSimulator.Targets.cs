// ============================================================================
//  万相 · 战斗核心 · Targets（从 BattleSimulator.cs 拆出：**纯搬家、零行为变更**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域，成员可访问性与语义完全不变。
//  ⚠ 只挪位置，没改任何一行逻辑。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public static partial class BattleSimulator
    {

        /// <summary>
        /// 效果的"目标池"：伤害类 / 减益类打**对面**，其余（治疗/护盾/增益）打**自己这边**。
        /// ⚠ 结算是与预览（<see cref="PreviewTargets"/>）**共用这一份**口径，别在两处各写一遍。
        /// </summary>
        private static TeamSide PoolFor(in EffectAtom atom, BattleUnit src)
        {
            bool debuffLike = atom.Kind == EffectAtomKind.Damage
                           || (atom.Kind == EffectAtomKind.ApplyStatus
                               && StatusCatalog.Get(atom.StatusId).IsDebuff);
            return debuffLike ? BattleState.Opponent(src.Side) : src.Side;
        }
        /// <summary>
        /// 【预览】该技能会作用于哪些单位 —— **只读、不消费随机数、不改任何状态**
        /// （供战斗面板做九宫格高亮："打谁 / 给谁加盾"一眼可见）。
        /// 确定性选择器直接复用 <see cref="ResolveTargets"/>；
        /// 随机类（RandomEnemy / RandomEnemyMultiHit）无法预测 ⇒ 返回该池**全部存活单位**
        /// 并把 <paramref name="isRandom"/> 置 true，由调用方提示"随机"。
        /// ⚠ 这里**绝不能掷骰**：那会推进战斗随机流、改掉后续每一次结算
        ///   （同 ComputeDamage 的 forPreview 教训）。
        /// </summary>
        /// <summary>
        /// 普攻（Cd==0）的**有效技能**：把伤害效果的目标强制为「最前排」
        /// （v2.1 P4：棋盘的"前排/后排"必须决定谁先承伤，站位才有策略意义；
        ///  特殊技能保留各自规则，如刺客类打"生命最低"/后排）。
        ///
        /// ⚠ 结算与预览（<see cref="PreviewTargets"/> ⇒ 面板九宫格高亮）**必须共用这一份**。
        ///   曾经结算改了、预览没改 ⇒ 高亮按原始技能算（生命最低者）、实际打最前排，
        ///   玩家看到"高亮在一格、伤害飘在另一格，而且目标会随血量跳"（用户实测报障）。
        /// ⚠ Clone() 是深拷贝 Effects（已确认），改副本不会污染技能定义。
        /// </summary>
        public static SkillDef EffectiveSkill(SkillDef skill)
        {
            if (skill == null || skill.Cd != 0) return skill;

            var patched = skill.Clone();
            patched.PrimaryTarget = TargetSelector.SingleFrontMost;
            if (patched.Effects != null)
            {
                for (int i = 0; i < patched.Effects.Length; i++)
                {
                    var a = patched.Effects[i];
                    if (a.Kind == EffectAtomKind.Damage)
                    {
                        a.Target = TargetSelector.SingleFrontMost;
                        patched.Effects[i] = a;
                    }
                }
            }
            return patched;
        }
        public static void PreviewTargets(BattleState st, BattleUnit src, SkillDef sk,
                                          List<BattleUnit> into, out bool isRandom)
        {
            into.Clear();
            isRandom = false;
            if (st == null || src == null || sk == null || sk.Effects == null) return;

            // ★ 必须套用与结算**同一份**的"普攻→最前排"改写，否则高亮与实际打的不是同一格
            sk = EffectiveSkill(sk);

            var tmp = new List<BattleUnit>(8);
            for (int a = 0; a < sk.Effects.Length; a++)
            {
                var atom = sk.Effects[a];
                var pool = PoolFor(atom, src);

                if (atom.Target == TargetSelector.RandomEnemy
                    || atom.Target == TargetSelector.RandomEnemyMultiHit)
                {
                    isRandom = true;
                    st.CollectAlive(pool, tmp);
                }
                else
                {
                    // 确定性选择器（"生命最低/最前排/全体/自身"等）—— 纯比较，不掷骰
                    ResolveTargets(st, src, atom.Target, pool, tmp);
                }

                for (int i = 0; i < tmp.Count; i++)
                    if (!into.Contains(tmp[i])) into.Add(tmp[i]);
            }
        }
        private static void ResolveTargets(BattleState st, BattleUnit src, TargetSelector sel,
                                           TeamSide pool, List<BattleUnit> into)
        {
            into.Clear();
            switch (sel)
            {
                case TargetSelector.Self:
                    into.Add(src);
                    break;

                case TargetSelector.AllEnemies:
                    st.CollectAlive(BattleState.Opponent(src.Side), into);
                    break;

                case TargetSelector.AllAllies:
                    st.CollectAlive(src.Side, into);
                    break;

                case TargetSelector.AllOthers:
                    st.CollectAliveAll(into);
                    for (int i = into.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(into[i], src)) into.RemoveAt(i);
                    break;

                case TargetSelector.AdjacentToSelf:
                {
                    if (!src.Pos.IsValid) break;
                    var slots = st.SlotsOf(src.Side);
                    var pairs = BoardLayout.AdjacentPairs;   // 直接扫 12 对，比手写"上下左右"更不容易错
                    for (int k = 0; k < pairs.Length; k++)
                    {
                        int i = pairs[k][0], j = pairs[k][1];
                        int other;
                        if (i == src.Pos.Index) other = j;
                        else if (j == src.Pos.Index) other = i;
                        else continue;
                        var u = slots[other];
                        if (u != null && u.IsAlive) into.Add(u);
                    }
                    break;
                }

                case TargetSelector.SingleLowestHp:
                    PickOne(st, pool, into, PickLowestHp);
                    break;

                case TargetSelector.SingleHighestHp:
                    PickOne(st, pool, into, PickHighestHp);
                    break;

                case TargetSelector.SingleHighestAtk:
                    PickOne(st, pool, into, PickHighestAtk);
                    break;

                // ---- 按站位选目标：自包含实现（需要 src 参与比较，PickOne 的 mode 传不了）----
                case TargetSelector.SingleFrontMost:
                    PickByRank(st, pool, into, src, true);
                    break;

                case TargetSelector.SingleBackMost:
                    PickByRank(st, pool, into, src, false);
                    break;

                case TargetSelector.RandomEnemy:
                case TargetSelector.RandomEnemyMultiHit:
                {
                    st.CollectAlive(BattleState.Opponent(src.Side), into);
                    if (into.Count == 0) break;
                    var pick = into[st.Random.NextInt(0, into.Count)];
                    into.Clear();
                    into.Add(pick);
                    break;
                }

                default:
                    break;
            }
        }
        /// <summary>
        /// 选一个目标。平局按站位索引从左到右，再按阵营、实例 id ——
        /// 与出手序列同一套全序，保证"生命值一样时打谁"不会因运行而变。
        /// </summary>
        /// <summary>
        /// 按站位选一个目标（v2.1 回合制 P4）。
        /// <para>
        /// ⚠ 判据用 **Row 大小**，不是"与施法者的行距" —— 实测发现双方**共用同一套格位**
        /// （Player 现在用编阵传入的 PlayerCells；Enemy 走 BattleRequest.Cells）。
        /// 项目已有明确语义：<c>BattleStage2D</c> 里 <c>sortingOrder = Pos.Index / 3</c>
        /// 且注释写明「row 0=后 1=中 2=前」，双方镜像绘制 ⇒ **Row 大 = 前排**，双方对称。
        /// </para>
        /// </summary>
        private static void PickByRank(BattleState st, TeamSide side, List<BattleUnit> into,
                                       BattleUnit src, bool frontMost)
        {
            st.CollectAlive(side, into);
            if (into.Count <= 1) return;

            int best = 0;
            for (int i = 1; i < into.Count; i++)
                if (RanksCloser(into[i], into[best], src, frontMost)) best = i;

            var pick = into[best];
            into.Clear();
            into.Add(pick);
        }
        private static bool RanksCloser(BattleUnit a, BattleUnit b, BattleUnit src, bool frontMost)
        {
            // ★ 前后排 = **列**（FrontRank），不是行。
            //   横版对阵时中线是竖直线，"离中线多远"由 X（列）决定；
            //   此前用 Row（上下方向）判前后排是错的 —— 注释里"Row 0 靠中线"的推理
            //   把 CellPos 里 row 控制的 Y 当成了离中线距离。用户指定：第一列（0/3/6）为前排。
            int ra = a.Pos.IsValid ? a.Pos.FrontRankFor(a.Side) : int.MaxValue;
            int rb = b.Pos.IsValid ? b.Pos.FrontRankFor(b.Side) : int.MaxValue;
            if (ra != rb) return frontMost ? ra < rb : ra > rb;   // 前排 = 列小；后排 = 列大

            // 同列：行小的先（上→下，稳定）—— 一个格子只能站一只，
            // 所以"同排同行"不可能出现；同列的目标因此**不会随血量变化而跳**。
            // ⚠ 原先这里还有一条「同列同排时取生命比例高的」，但它是不可达的死分支，
            //   留着会让人误以为"目标会随血量跳"（用户曾据此报障）。已删。
            int ca = a.Pos.IsValid ? a.Pos.Row : int.MaxValue;
            int cb = b.Pos.IsValid ? b.Pos.Row : int.MaxValue;
            if (ca != cb) return ca < cb;
            return true;
        }
        private static void PickOne(BattleState st, TeamSide side, List<BattleUnit> into, int mode)
        {
            st.CollectAlive(side, into);
            if (into.Count <= 1) return;

            int best = 0;
            for (int i = 1; i < into.Count; i++)
                if (Better(into[i], into[best], mode)) best = i;

            var pick = into[best];
            into.Clear();
            into.Add(pick);
        }
        private static bool Better(BattleUnit a, BattleUnit b, int mode)
        {
            if (mode == PickLowestHp)
            {
                if (a.HpPercent < b.HpPercent) return true;
                if (a.HpPercent > b.HpPercent) return false;
            }
            else if (mode == PickHighestHp)
            {
                if (a.HpPercent > b.HpPercent) return true;
                if (a.HpPercent < b.HpPercent) return false;
            }
            else if (mode == PickHighestAtk)
            {
                if (a.Attack > b.Attack) return true;
                if (a.Attack < b.Attack) return false;
            }

            int pa = a.Pos.IsValid ? a.Pos.Index : BoardLayout.CellCount;
            int pb = b.Pos.IsValid ? b.Pos.Index : BoardLayout.CellCount;
            if (pa != pb) return pa < pb;

            if (a.Side != b.Side) return a.Side < b.Side;

            return string.CompareOrdinal(a.RuntimeId, b.RuntimeId) < 0;
        }
    }
}
