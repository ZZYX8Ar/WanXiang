// ============================================================================
//  万相 · 战斗核心 · 运行时单位
//  ---------------------------------------------------------------------------
//  BattleUnit 与 BeastDef 的关系：
//      BeastDef  = 静态定义（"句芒是什么"），全工程共享一份，只读
//      BattleUnit = 本场战斗中"这一只句芒"的运行时状态，每场新建
//
//  之所以分得这么干净，是因为融合系统（GDD 6.2 的 Fuse）要在运行时造出
//  "宿主 + 灵魂"的新单位：它的外形来自宿主、五行与技能可能被灵魂改写。
//  如果运行时直接改 BeastDef，第二场战斗就会读到被上一场改脏的定义。
//  ⇒ **BeastDef 永远只读，所有可变状态都在 BattleUnit 上。**
// ============================================================================

namespace WanXiang.Battle.Core
{
    public struct StatusInstance
    {
        public string Id;
        public int Stacks;

        /// <summary>剩余回合。-1 表示常驻计数器（「尾」「食」这类本场累积的层数）。</summary>
        public int TurnsRemaining;

        /// <summary>每层每回合的固定伤害（施加瞬间折算好，见 StatusCatalog 的说明）。</summary>
        public float DotFlatPerStack;

        public StatusDef Def => StatusCatalog.Get(Id);

        public override string ToString() =>
            Stacks > 1 ? $"{Def.Name}×{Stacks}" : Def.Name;
    }

    /// <summary>一条属性修正（来自技能的 StatModifier 原子）。</summary>
    public struct StatModifierInstance
    {
        public string StatKey;   // "Attack" / "Speed" / "DamageTaken" / "HealShield"
        public float Delta;
        public int TurnsRemaining;   // 0 = 本场永久
    }

    public sealed class BattleUnit
    {
        // ---- 身份 ----
        public readonly TeamSide Side;
        public readonly BeastDef Def;

        /// <summary>棋盘格。空位用 GridPos.Invalid。</summary>
        public GridPos Pos;

        /// <summary>场上的唯一实例 id。日志与指纹都用它，避免同名单位混淆。</summary>
        public string RuntimeId { get; }

        /// <summary>显示名。融合后会被改写（例：句芒·霜魄）。</summary>
        public string DisplayName { get; set; }

        /// <summary>五行。融合时可能被灵魂覆写（GDD 6.2：element = soul.elementOverride ?? body.element）。</summary>
        public Element Element { get; set; }

        // ---- 生命 ----
        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public int Shield { get; private set; }

        public bool IsAlive => Hp > 0;
        public float HpPercent => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;

        // ---- 怒气与冷却 ----
        public float Rage;
        /// <summary>三个技能的剩余冷却，下标与 SkillType 对应（0=普攻 1=战技 2=绝技）。</summary>
        public readonly int[] Cooldowns = new int[3];

        /// <summary>怒气的加成乘数（相克相冲会给 -10%）。</summary>
        public float RageGainMultiplier = 1f;

        /// <summary>怒气上限。由 <see cref="BattleState.Place"/> 从配置写入 ——
        /// 不让 BattleUnit 自己写死 100，否则改配置会出现"上限 120 但永远到不了"这种哑亏。</summary>
        public float RageCap = 100f;

        // ---- 状态与修正 ----
        public readonly System.Collections.Generic.List<StatusInstance> Statuses
            = new System.Collections.Generic.List<StatusInstance>(8);
        public readonly System.Collections.Generic.List<StatModifierInstance> Modifiers
            = new System.Collections.Generic.List<StatModifierInstance>(4);

        /// <summary>
        /// 本场累积的"永久增益"（饕餮「无餍」击杀永久 +8% 攻击与生命、
        /// 犰狳/巴蛇的"永久生命上限"、蓐收「秋决」的暴伤层数）。与 Modifiers 分开，
        /// 因为它们**不参与回合末减层**，且会改 MaxHp。
        /// </summary>
        public float PermanentAttackBonus;
        public float PermanentMaxHpBonus;

        // ---- 棋盘与羁绊带来的、由 BattleState / BoardRules 写入的派生量 ----
        //  它们不放 Modifiers 里，是因为它们的来源不是"技能施加的临时修正"，
        //  而是"你站在哪、你和谁同队"这些结构性事实。混进 Modifiers 会带来
        //  "回合末减层把中宫减伤减掉了"这类荒谬 bug。

        /// <summary>中宫土德：站在中宫时受到的伤害 -8%。<see cref="BattleState.Place"/> 按配置写入。</summary>
        public float CenterDamageReduction;

        /// <summary>同属共鸣的攻击加成（+8% / +16% / +25%）。<see cref="BoardRules.ApplyResonance"/> 每回合重算。</summary>
        public float ResonanceAttackBonus;

        /// <summary>
        /// 同属共鸣带来的 CD 缩减（5 只同属时为 -2）。
        /// ⚠ STEP 1 的替身用法：GDD 说这个 -2 只作用在「共鸣技」上，而共鸣技需要一个
        ///   第四技能位，本轮没做。暂时把它加在**绝技**上，让"5 只同属"这档在灰盒里
        ///   看得见效果。做共鸣技时把 <see cref="BattleSimulator"/> 里的落点挪走即可。
        /// </summary>
        public int ResonanceCdDelta;

        /// <summary>是否解锁同属共鸣技（4 只同属）。STEP 1 只记录状态，共鸣技本身未实现。</summary>
        public bool ResonanceUnlocked;

        public BattleUnit(TeamSide side, BeastDef def, string runtimeId)
        {
            Side = side;
            Def = def;
            RuntimeId = runtimeId;
            DisplayName = def.DisplayName;
            Element = def.Element;
            MaxHp = def.BaseHp;
            Hp = MaxHp;
            Shield = 0;
            Pos = new GridPos(GridPos.Invalid);
        }

        // ================================================================
        //  派生属性（每次读取现算，不缓存）
        //  ----------------------------------------------------------------
        //  ⚠ 刻意**不做缓存**：缓存会引入"改了状态忘了标脏"这类 bug，
        //    而这种 bug 的表现是"伤害偶尔不对"，极难复现。一场 5v5、30 回合的
        //    战斗里这些计算加起来也就几十万次浮点乘加，不值得为它冒险。
        // ================================================================

        public float Attack
        {
            get
            {
                float v = Def.BaseAtk * (1f + PermanentAttackBonus) * (1f + ResonanceAttackBonus);
                for (int i = 0; i < Modifiers.Count; i++)
                    if (Modifiers[i].StatKey == StatKeys.Attack) v *= (1f + Modifiers[i].Delta);
                for (int i = 0; i < Statuses.Count; i++)
                {
                    var s = Statuses[i];
                    float d = s.Def.AttackDeltaPerStack;
                    if (d != 0f) v *= (1f + d * s.Stacks);
                }
                return v < 1f ? 1f : v;
            }
        }

        public float Speed
        {
            get
            {
                float v = Def.BaseSpeed;
                for (int i = 0; i < Modifiers.Count; i++)
                    if (Modifiers[i].StatKey == StatKeys.Speed) v *= (1f + Modifiers[i].Delta);
                for (int i = 0; i < Statuses.Count; i++)
                {
                    var s = Statuses[i];
                    float d = s.Def.SpeedDeltaPerStack;
                    if (d != 0f) v *= (1f + d * s.Stacks);
                }
                return v < 1f ? 1f : v;
            }
        }

        /// <summary>受到伤害的乘数（&gt;1 表示更痛）。含中宫土德减伤。</summary>
        public float DamageTakenMultiplier
        {
            get
            {
                float v = 1f;
                // 中宫土德：GDD 2.5「站在中宫的单位受到的所有伤害 -8%」。
                // 每次现算而不是 Place 时写死，是因为融合/换位会改 Pos，写死就会留个脏值。
                if (Pos.IsCenter && CenterDamageReduction > 0f) v *= (1f - CenterDamageReduction);
                for (int i = 0; i < Modifiers.Count; i++)
                    if (Modifiers[i].StatKey == StatKeys.DamageTaken) v *= (1f + Modifiers[i].Delta);
                for (int i = 0; i < Statuses.Count; i++)
                {
                    var s = Statuses[i];
                    float d = s.Def.DamageTakenDeltaPerStack;
                    if (d != 0f) v *= (1f + d * s.Stacks);
                }
                return v < 0f ? 0f : v;
            }
        }

        /// <summary>对"造成的治疗与护盾"的乘数。</summary>
        public float HealShieldMultiplier
        {
            get
            {
                float v = 1f;
                for (int i = 0; i < Modifiers.Count; i++)
                    if (Modifiers[i].StatKey == StatKeys.HealShield) v *= (1f + Modifiers[i].Delta);
                for (int i = 0; i < Statuses.Count; i++)
                {
                    var s = Statuses[i];
                    float d = s.Def.HealShieldDeltaPerStack;
                    if (d != 0f) v *= (1f + d * s.Stacks);
                }
                return v < 0f ? 0f : v;
            }
        }

        /// <summary>「同气」层数带来的技能效果加成（每层 +2%，GDD 2.5）。</summary>
        public float QiSkillBonus(float perStack)
        {
            return GetStacks(StatusCatalog.Qi) * perStack;
        }

        public bool CanAct
        {
            get
            {
                if (!IsAlive) return false;
                for (int i = 0; i < Statuses.Count; i++)
                    if (Statuses[i].Def.PreventsAction) return false;
                return true;
            }
        }

        public bool CanBeHealed
        {
            get
            {
                for (int i = 0; i < Statuses.Count; i++)
                    if (Statuses[i].Def.PreventsHeal) return false;
                return true;
            }
        }

        // ================================================================
        //  状态操作
        // ================================================================

        public int GetStacks(string statusId)
        {
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Id == statusId) return Statuses[i].Stacks;
            return 0;
        }

        public bool HasStatus(string statusId) => GetStacks(statusId) > 0;

        /// <summary>
        /// 施加状态。已存在则叠层（受 MaxStacks 限制），**刷新时长取较大值**
        /// —— GDD 里"可叠加层数，不可刷新时长"（立夏·炎气初升）就是这个语义。
        /// </summary>
        public void ApplyStatus(string statusId, int stacks, int turns, float dotFlatPerStack = 0f)
        {
            var def = StatusCatalog.Get(statusId);
            for (int i = 0; i < Statuses.Count; i++)
            {
                if (Statuses[i].Id != statusId) continue;
                var s = Statuses[i];
                s.Stacks = CoreMath.Min(s.Stacks + stacks, CoreMath.Max(1, def.MaxStacks));
                if (turns > s.TurnsRemaining) s.TurnsRemaining = turns;
                if (dotFlatPerStack > s.DotFlatPerStack) s.DotFlatPerStack = dotFlatPerStack;
                Statuses[i] = s;
                return;
            }
            Statuses.Add(new StatusInstance
            {
                Id = statusId,
                Stacks = CoreMath.Min(stacks, CoreMath.Max(1, def.MaxStacks)),
                TurnsRemaining = def.IsLingeringCounter ? -1 : turns,
                DotFlatPerStack = dotFlatPerStack,
            });
        }

        /// <summary>移除状态。statusId 传 null 表示清除全部减益（驱散）。返回移除的层数总和。</summary>
        public int RemoveStatus(string statusId)
        {
            int removed = 0;
            for (int i = Statuses.Count - 1; i >= 0; i--)
            {
                bool match = statusId == null
                    ? Statuses[i].Def.IsDebuff
                    : Statuses[i].Id == statusId;
                if (!match) continue;
                removed += Statuses[i].Stacks;
                Statuses.RemoveAt(i);
            }
            return removed;
        }

        /// <summary>回合末状态结算：常驻计数器不减层，其余减 1，到 0 移除。</summary>
        public void TickStatusDurations()
        {
            for (int i = Statuses.Count - 1; i >= 0; i--)
            {
                var s = Statuses[i];
                if (s.TurnsRemaining < 0) continue;         // 常驻计数器
                s.TurnsRemaining--;
                if (s.TurnsRemaining <= 0) Statuses.RemoveAt(i);
                else Statuses[i] = s;
            }
        }

        public void AddModifier(string statKey, float delta, int turns)
        {
            Modifiers.Add(new StatModifierInstance
            {
                StatKey = statKey, Delta = delta, TurnsRemaining = turns,
            });
        }

        public void TickModifiers()
        {
            for (int i = Modifiers.Count - 1; i >= 0; i--)
            {
                var m = Modifiers[i];
                if (m.TurnsRemaining <= 0) continue;        // 0 = 永久
                m.TurnsRemaining--;
                if (m.TurnsRemaining <= 0) Modifiers.RemoveAt(i);
                else Modifiers[i] = m;
            }
        }

        // ================================================================
        //  生命 / 护盾
        // ================================================================

        /// <summary>
        /// 扣血。返回**实际**损失的生命（不含被护盾吃掉的部分）—— 调用方需要它来判断击杀。
        /// 护盾按"先扣盾再扣血"处理，与主流同类产品一致。
        /// </summary>
        /// <param name="ignoreShield">跳过护盾。对应 GDD 里"无视护盾但**不**无视减伤"的效果
        /// （与 <see cref="TakeTrueDamage"/> 不同，后者连减伤也一起无视）。</param>
        public int TakeDamage(int amount, bool ignoreShield = false)
        {
            if (amount <= 0 || !IsAlive) return 0;

            int toShield = ignoreShield ? 0 : CoreMath.Min(Shield, amount);
            Shield -= toShield;
            int toHp = amount - toShield;
            if (toHp > Hp) toHp = Hp;
            Hp -= toHp;

            // 受击消耗层（若有）
            for (int i = Statuses.Count - 1; i >= 0; i--)
            {
                if (!Statuses[i].Def.ConsumeStackOnHit) continue;
                var s = Statuses[i];
                s.Stacks--;
                if (s.Stacks <= 0) Statuses.RemoveAt(i); else Statuses[i] = s;
            }
            return toHp;
        }

        /// <summary>
        /// 真实伤害：**既无视护盾也无视减伤**。GDD 里"真实伤害"的措辞（相克相冲的
        /// 2% 最大生命、天时的场地伤害）一律走这里。
        /// ⚠ 刻意不触发 <see cref="StatusDef.ConsumeStackOnHit"/> ——
        ///   那条规则是给"挡一次伤害"这类护盾型状态用的，真实伤害绕过护盾，也就应当绕过它。
        /// </summary>
        public int TakeTrueDamage(int amount)
        {
            if (amount <= 0 || !IsAlive) return 0;
            int toHp = amount > Hp ? Hp : amount;
            Hp -= toHp;
            return toHp;
        }

        public int Heal(int amount)
        {
            if (amount <= 0 || !IsAlive) return 0;
            int before = Hp;
            Hp = CoreMath.Min(MaxHp, Hp + amount);
            return Hp - before;
        }

        public int AddShield(int amount)
        {
            if (amount <= 0 || !IsAlive) return 0;
            Shield += amount;
            return amount;
        }

        /// <summary>直接改生命上限（吞噬 / 巴蛇的"永久生命上限"）。当前生命按同比例保留。</summary>
        public void GrowMaxHp(int delta)
        {
            if (delta == 0) return;
            float ratio = HpPercent;
            MaxHp = CoreMath.Max(1, MaxHp + delta);
            Hp = CoreMath.Max(1, (int)(MaxHp * ratio));
        }

        public void AddRage(float amount)
        {
            if (amount == 0f) return;
            Rage = CoreMath.Clamp(Rage + amount * RageGainMultiplier, 0f, RageCap);
        }

        /// <summary>扣怒气（放绝技时）。</summary>
        public void SpendRage(float amount)
        {
            if (amount <= 0f) return;
            Rage = CoreMath.Clamp(Rage - amount, 0f, RageCap);
        }

        /// <summary>是否满足释放条件（CD 与怒气）。</summary>
        public bool CanCast(SkillType type, BattleConfig cfg)
        {
            int idx = (int)type;
            if (Cooldowns[idx] > 0) return false;
            if (type == SkillType.Ultimate && cfg.UltimateNeedsRage && Rage < cfg.RageMax) return false;
            return true;
        }

        /// <summary>进入冷却。所有技能统一 +1 是因为"释放当回合不放回"（GDD 的 CD 3 表示隔 3 回合）。</summary>
        public void PutOnCooldown(SkillType type, int cd, int resonanceCdDelta = 0)
        {
            int v = cd + resonanceCdDelta;
            if (v < 0) v = 0;
            Cooldowns[(int)type] = v;
        }

        public void TickCooldowns()
        {
            for (int i = 0; i < Cooldowns.Length; i++)
                if (Cooldowns[i] > 0) Cooldowns[i]--;
        }

        /// <summary>取某个技能的定义。</summary>
        public SkillDef GetSkill(SkillType type)
        {
            switch (type)
            {
                case SkillType.Basic: return Def.Basic;
                case SkillType.Active: return Def.Active;
                case SkillType.Ultimate: return Def.Ultimate;
                default: return null;
            }
        }

        public override string ToString() =>
            $"{DisplayName}#{RuntimeId}({Cn.Of(Side)},{(Pos.IsValid ? Pos.ToString() : "未上阵")})";
    }

    public static class StatKeys
    {
        public const string Attack = "Attack";
        public const string Speed = "Speed";
        public const string DamageTaken = "DamageTaken";
        public const string HealShield = "HealShield";
        public const string CritRate = "CritRate";
        public const string CritDamage = "CritDamage";
    }
}
