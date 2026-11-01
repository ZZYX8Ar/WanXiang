// ============================================================================
//  元素反应 · 10 组（2026-10-31 重做）
//  ---------------------------------------------------------------------------
//  用户："弄成参考的 10 反应，把原来的全部替换掉"。
//  参考 InkAbyssWalker：五行 **两两配对 = 10 组**，没有遗漏，任何两个元素撞上都有反馈。
//
//  机制（新）：单位身上有一个**元素附着**（AuraEl + 层数 1~3）：
//    · 打上去的元素与附着相同 → 附着层数 +1（最多 3）
//    · 不同 → 触发这两元素的反应，消耗旧附着层数，然后把**新元素**挂上去（1 层）
//  这比旧版"伤害元素 × 目标身上某个状态"更完整：不再需要「湿/冰蚀」当引信，
//  任何元素都能和任何元素起反应（旧的状态系统仍保留给技能/首领用）。
//
//  ⛔ 全部走既有原语（Heal / AddShield / ApplyStatus / TakeTrueDamage），
//    不引入额外随机流 ⇒ 同种子仍可复现。
// ============================================================================
using System;

namespace WanXiang.Battle.Core
{
    public static class ElementReactions
    {
        /// <summary>附着层数上限（2026-10-31 用户："最高 4 层行不行" ⇒ 3 → 4）。</summary>
        public const int MaxStacks = 4;
        /// <summary>对 (target, incoming) 结算一次附着/反应。返回触发的反应（None = 只附着）。</summary>
        public static ReactionKind Resolve(BattleState st, BattleUnit src, BattleUnit dst, Element incoming)
        {
            if (st == null || dst == null || !dst.IsAlive) return ReactionKind.None;
            if (incoming == Element.None) return ReactionKind.None;

            // ⛔ 2026-11-01 用户："附着太容易上了" ⇒ **我方必须先用元素充能**：
            //   充能层数就是这次能挂多少层；一次出手耗尽；没充能就完全不附着。
            //   敌方仍按 1 层处理（Boss/小怪不需要充能，否则它们永远挂不上）。
            int applyStacks = 1;
            if (src != null && src.Side == TeamSide.Player)
            {
                if (src.ChargeStacks <= 0) return ReactionKind.None;
                applyStacks = Math.Min(MaxStacks, src.ChargeStacks);
                src.ChargeStacks = 0;                 // 一次出手耗尽
            }

            // ① 空附着：直接挂上（按充能层数）
            if (dst.AuraEl == Element.None || dst.AuraStacks <= 0)
            {
                SetAura(dst, incoming, applyStacks);
                return ReactionKind.None;
            }

            // ② 同元素：叠层（上限 MaxStacks）
            if (dst.AuraEl == incoming)
            {
                SetAura(dst, incoming, Math.Min(MaxStacks, dst.AuraStacks + applyStacks));
                return ReactionKind.None;
            }

            // ③ 异元素：反应 —— **反应多少层 = min(这次充能, 目标身上层数)**
            int consumed = Math.Max(1, Math.Min(applyStacks, dst.AuraStacks));
            int stacks = consumed;
            int leftover = Math.Max(0, applyStacks - consumed);
            Element a = dst.AuraEl, b = incoming;
            var kind = Pair(a, b);
            Apply(st, src, dst, kind, stacks, a, b);

            // 反应后：旧附着被消耗，把**剩余充能**挂成新元素（没剩就不挂）
            SetAura(dst, incoming, leftover);
            return kind;
        }

        private static void SetAura(BattleUnit u, Element el, int stacks)
        {
            u.AuraEl = el;
            u.AuraStacks = Math.Max(0, Math.Min(MaxStacks, stacks));
        }

        /// <summary>两元素 → 反应种类（顺序无关）。⛔ 用**枚举名**判断，不要用 int 值
        /// （实际 Element 顺序是 木1 火2 土3 金4 水5，早期按直觉写的 int 表把整张映射错位了一轮）。</summary>
        public static ReactionKind Pair(Element x, Element y)
        {
            bool Is(Element a, Element b, Element p1, Element p2) =>
                (a == p1 && b == p2) || (a == p2 && b == p1);

            if (Is(x, y, Element.Wood, Element.Water)) return ReactionKind.Sheng;      // 润生
            if (Is(x, y, Element.Fire, Element.Earth)) return ReactionKind.Bulwark;    // 熔盾
            if (Is(x, y, Element.Fire, Element.Water)) return ReactionKind.Steam;      // 蒸发
            if (Is(x, y, Element.Fire, Element.Metal)) return ReactionKind.Forge;      // 锻击
            if (Is(x, y, Element.Water, Element.Earth)) return ReactionKind.Erode;     // 侵蚀
            if (Is(x, y, Element.Metal, Element.Water)) return ReactionKind.Quench;    // 淬刃
            if (Is(x, y, Element.Wood, Element.Fire)) return ReactionKind.Kindle;      // 燎原
            if (Is(x, y, Element.Wood, Element.Earth)) return ReactionKind.TakeRoot;   // 扎根
            if (Is(x, y, Element.Wood, Element.Metal)) return ReactionKind.Execute;    // 肃杀
            if (Is(x, y, Element.Metal, Element.Earth)) return ReactionKind.Bastion;   // 坚壁
            return ReactionKind.None;
        }

        private static void Apply(BattleState st, BattleUnit src, BattleUnit dst,
                                  ReactionKind kind, int stacks, Element auraEl, Element inEl)
        {
            var side = src != null ? src.Side : TeamSide.Player;
            float atk = src != null ? src.Attack : 100f;

            switch (kind)
            {
                // ---- 进攻向（打在被附着者身上）----
                case ReactionKind.Steam:      // 蒸发：按最大生命真伤
                {
                    int dmg = TrueDmg(dst, 0.060f * stacks);
                    EmitDamage(st, src, dst, dmg, Element.Fire, "元素反应·蒸发（" + stacks + " 层）", kind);
                    break;
                }
                case ReactionKind.Forge:      // 锻击：**普通伤害**（吃护盾/防御）+ 裂甲
                {
                    // ⛔ 2026-10-31 用户："怎么这么多反应都是真伤" —— 只有蒸发保留真伤（招牌），
                    //   其余走普通伤害：能被护盾挡、被防御削，才有"选哪种反应"的意义。
                    int dmg = dst.TakeDamage(CoreMath.RoundDamage(atk * 1.10f * stacks));
                    dst.ApplyStatus(StatusCatalog.ArmorBreak, 1, 2);
                    EmitDamage(st, src, dst, dmg, Element.Metal, "元素反应·锻击（裂甲）", kind);
                    break;
                }
                case ReactionKind.Erode:      // 侵蚀：普通伤害 + 易伤
                {
                    int dmg = dst.TakeDamage(CoreMath.RoundDamage(atk * 0.70f * stacks));
                    dst.ApplyStatus(StatusCatalog.Marked, 1, 2);
                    EmitDamage(st, src, dst, dmg, Element.Water, "元素反应·侵蚀（易伤）", kind);
                    break;
                }
                case ReactionKind.Execute:    // 肃杀：普通伤害，残血加成
                {
                    float mul = 1.5f * stacks * (dst.HpPercent < 0.30f ? 1.5f : 1f);
                    int dmg = dst.TakeDamage(CoreMath.RoundDamage(atk * mul));
                    EmitDamage(st, src, dst, dmg, Element.Metal, "元素反应·肃杀", kind);
                    break;
                }
                case ReactionKind.Kindle:     // 燎原：灼烧 +2 层
                {
                    dst.ApplyStatus(StatusCatalog.Burn, 2, 3);
                    st.Log.Add(st.Turn, BattleEventKind.StatusApplied, actorId: src != null ? src.RuntimeId : null,
                               targetId: dst.RuntimeId, amount: dst.GetStacks(StatusCatalog.Burn),
                               note: "元素反应·燎原（灼烧 +2）", reaction: kind);
                    break;
                }

                // ---- 防守向（作用于施法者一方）----
                case ReactionKind.Sheng:      // 润生：全队治疗
                {
                    HealTeam(st, side, CoreMath.RoundDamage(atk * 0.8f * stacks), "元素反应·润生");
                    EmitBuff(st, src, side, "元素反应·润生（治疗）", kind);
                    break;
                }
                case ReactionKind.Bulwark:    // 熔盾：全队护盾
                {
                    ShieldTeam(st, side, CoreMath.RoundDamage(atk * 0.6f * stacks));
                    EmitBuff(st, src, side, "元素反应·熔盾（护盾）", kind);
                    break;
                }
                case ReactionKind.TakeRoot:   // 扎根：治疗 + 护盾
                {
                    HealTeam(st, side, CoreMath.RoundDamage(atk * 0.5f * stacks), "元素反应·扎根");
                    ShieldTeam(st, side, CoreMath.RoundDamage(atk * 0.3f * stacks));
                    EmitBuff(st, src, side, "元素反应·扎根（治疗+护盾）", kind);
                    break;
                }
                case ReactionKind.Quench:     // 感电（旧名沿用）：伤害 + 麻痹
                {
                    // 用户 2026-10-31："有些反应和旧的一样，直接套旧名字不就行了" ——
                    // 金+水 就是旧的「感电」，效果对齐：普通伤害 + 挂「感电」2 回合。
                    int dmg = dst.TakeDamage(CoreMath.RoundDamage(atk * 0.80f * stacks));
                    dst.ApplyStatus(StatusCatalog.Shock, 1, 2);
                    EmitDamage(st, src, dst, dmg, Element.Metal, "元素反应·感电（麻痹）", kind);
                    break;
                }
                case ReactionKind.Bastion:    // 坚壁：全队护盾 + 清 1 个减益
                {
                    ShieldTeam(st, side, CoreMath.RoundDamage(atk * 0.35f * stacks));
                    if (src != null)
                    {
                        if (src.HasStatus(StatusCatalog.Frost)) src.RemoveStatus(StatusCatalog.Frost);
                        else if (src.HasStatus(StatusCatalog.ArmorBreak)) src.RemoveStatus(StatusCatalog.ArmorBreak);
                    }
                    EmitBuff(st, src, side, "元素反应·坚壁（护盾+净除）", kind);
                    break;
                }
            }
        }

        private static int TrueDmg(BattleUnit dst, float ratio)
        {
            int raw = CoreMath.RoundDamage(dst.MaxHp * ratio);
            return dst.TakeTrueDamage(raw);
        }

        private static void HealTeam(BattleState st, TeamSide side, int amount, string note)
        {
            if (amount <= 0) return;
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                u.Heal(amount);
            }
        }

        private static void ShieldTeam(BattleState st, TeamSide side, int amount)
        {
            if (amount <= 0) return;
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                u.AddShield(amount, st.Turn + 1);
            }
        }

        private static void EmitDamage(BattleState st, BattleUnit src, BattleUnit dst, int dealt,
                                       Element el, string note, ReactionKind kind)
        {
            st.Log.Add(st.Turn, BattleEventKind.Damage,
                       actorId: src != null ? src.RuntimeId : null, targetId: dst.RuntimeId,
                       amount: dealt, element: el, note: note, reaction: kind);
            if (dealt > 0 && !dst.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: dst.RuntimeId,
                           note: dst.DisplayName + " 阵亡（" + note + "）");
        }

        private static void EmitBuff(BattleState st, BattleUnit src, TeamSide side, string note, ReactionKind kind)
        {
            st.Log.Add(st.Turn, BattleEventKind.StatusApplied,
                       actorId: src != null ? src.RuntimeId : null,
                       targetId: src != null ? src.RuntimeId : null,
                       note: note, reaction: kind);
        }
    }
}
