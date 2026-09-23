// ============================================================================
//  万相 · 战斗核心 · 数据定义
//  ---------------------------------------------------------------------------
//  ⚠ 关于「技能效果」的落地范围，这里必须说清楚，否则下一个人会以为缺了东西：
//
//  GDD 第四章给了 30 只异兽、每只 3 个技能，一共 90 条技能描述，全是自然语言
//  （"对单体造成 100% 攻击的伤害；同时为生命最低的队友附加相当于 60% 攻击的护盾"）。
//  把这 90 条逐条翻译成代码，**不是 STEP 1 该做的事** ——
//  GDD 第 7 章给 STEP 1 划的范围是：
//
//      九宫格坐标 → 速度排序与出手序列 → CD 推进与技能释放 →
//      五行系数结算 → 相邻格相生相冲 → 羁绊判定 → 胜负判定
//
//  它要的是**机制骨架**，不是内容量。而且 7.1 的验证顺序第 0 步就是
//  "灰盒 + 1 只木怪 vs 1 只火怪，看 20 遍" —— 用最简单的单位验证战斗节奏。
//
//  所以本文件承载的是**通用技能模型**（EffectAtom 组合式描述），
//  GDD 6.4 自己也建议这么做：「节气 Buff 用 EffectAtom 组合式描述，
//  不写 24 份独立逻辑。第一版可先实现 5 个原子效果覆盖全部 24 条天时。」
//
//  技能的**逐条翻译**是后续工作（放在 BattleConfig 的 SkillLibrary 里逐步补），
//  补的时候不需要动战斗循环 —— 这正是选组合式描述的目的。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>色区（融合 Shader 的五个参数）。用 hex 存，不引 UnityEngine.Color。</summary>
    public struct PaletteHex
    {
        public string BodyMain;    // 主体色：毛发/甲壳主色块，占 45-55%，来自宿主
        public string BodyAccent;  // 纹样色：斑纹/羽尖/角饰，占 20-30%，来自宿主，可被灵魂偏色 ±30°
        public string EnergyGlow;  // 元气辉光：技能特效色/体表辉光/内描边，占 10-15%，来自灵魂（权重 1.0）
        public string EyeCore;     // 睛色：眼部发光核心，占 1-3%，来自灵魂
        public string Outline;     // 描边色：恒为 #2A2118，任何情况下不参与融合

        public const string InkOutline = "#2A2118";

        public int Red(string hex) => Parse(hex, 0);
        public int Green(string hex) => Parse(hex, 1);
        public int Blue(string hex) => Parse(hex, 2);

        /// <summary>从 "#RRGGBB" 取第 idx 个字节（0=R,1=G,2=B）。解析失败返回 0，
        /// **不抛异常** —— 色值是美术数据，坏一个不该让战斗起不来。</summary>
        private static int Parse(string hex, int idx)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 1 + idx * 2 + 2) return 0;
            int start = 1 + idx * 2;
            return Hex(hex[start]) * 16 + Hex(hex[start + 1]);
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }
    }

    /// <summary>特性。特性是常驻的被动，STEP 1 只承载名字与描述，行为按需逐条接。</summary>
    public struct TraitDef
    {
        public string Name;
        public string Description;
    }

    /// <summary>
    /// 一个原子效果。技能与场地天时共用这一套 —— 这是"不写 24 份独立逻辑"的前提。
    /// 字段看着多，但每个字段都有明确的唯一用途，组合起来能覆盖绝大多数描述。
    ///
    /// ⚠ [System.Serializable] 是必需的：SkillConfigSO.Effects 是 EffectAtom[]，
    ///    没有这个特性 Unity 不会序列化数组内容 —— 资产里存下来永远是空的，
    ///    表现为"技能配了但战斗零伤害"。这个坑踩过一次，别再删。
    /// </summary>
    [System.Serializable]
    public struct EffectAtom
    {
        public EffectAtomKind Kind;
        public TargetSelector Target;

        /// <summary>伤害倍率（相对施法者攻击力）。0.60 表示 60% 攻击。</summary>
        public float Power;

        /// <summary>百分比参数。治疗/护盾按施法者攻击力算倍率时用 Power；
        /// 按"最大生命 x%"算时用这个（如"回复 8% 最大生命" → 0.08）。</summary>
        public float PercentOfMaxHp;

        /// <summary>属性覆盖。None 表示跟随施法者五行。</summary>
        public Element ElementOverride;

        /// <summary>段数。1 = 单段。多段技能每段独立结算（暴击独立判定）。</summary>
        public int Hits;

        /// <summary>施加/驱散的状态 id。</summary>
        public string StatusId;
        public int StatusStacks;
        public int StatusTurns;

        /// <summary>改属性用的键与增量（如 "Attack" / -0.10 表示攻击 -10%）。</summary>
        public string StatKey;
        public float StatDelta;
        public int StatTurns;   // 0 = 本场永久

        /// <summary>真实伤害：无视护盾与减伤（GDD 里"真实伤害"的措辞一律走这个）。</summary>
        public bool TrueDamage;

        /// <summary>无视护盾（但**不**无视减伤）—— 与 TrueDamage 分开，语义不同。</summary>
        public bool IgnoreShield;

        // ---- 便于书写组合的静态构造器 ----

        public static EffectAtom Damage(TargetSelector t, float power, int hits = 1,
                                        Element elementOverride = Element.None,
                                        bool trueDamage = false)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.Damage, Target = t, Power = power, Hits = hits,
                ElementOverride = elementOverride, TrueDamage = trueDamage,
            };
        }

        public static EffectAtom Heal(TargetSelector t, float power = 0f, float percentOfMaxHp = 0f)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.Heal, Target = t, Power = power,
                PercentOfMaxHp = percentOfMaxHp,
            };
        }

        public static EffectAtom Shield(TargetSelector t, float power = 0f, float percentOfMaxHp = 0f)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.Shield, Target = t, Power = power,
                PercentOfMaxHp = percentOfMaxHp,
            };
        }

        public static EffectAtom Status(TargetSelector t, string statusId, int stacks = 1, int turns = 2)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.ApplyStatus, Target = t,
                StatusId = statusId, StatusStacks = stacks, StatusTurns = turns,
            };
        }

        /// <summary>
        /// 带持续伤害的状态。**Power 在这里的含义变成"每层每回合按施法者攻击力的倍率"**
        /// （GDD 里灼烧写的是「每层每回合造成 4% 施加者攻击的伤害」）。
        ///
        /// 注意这不是"读值时现算"，而是在**施加瞬间**折算成一个固定数值存进状态里，
        /// 理由见 StatusCatalog 的文件头（悬空引用问题）。
        /// </summary>
        public static EffectAtom Dot(TargetSelector t, string statusId, float powerPerStackOfCasterAtk,
                                     int stacks = 1, int turns = 2, float percentOfTargetMaxHp = 0f)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.ApplyStatus, Target = t,
                StatusId = statusId, StatusStacks = stacks, StatusTurns = turns,
                Power = powerPerStackOfCasterAtk, PercentOfMaxHp = percentOfTargetMaxHp,
            };
        }

        public static EffectAtom Dispel(TargetSelector t, string statusId = null)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.RemoveStatus, Target = t, StatusId = statusId,
            };
        }

        public static EffectAtom Buff(TargetSelector t, string statKey, float delta, int turns = 0)
        {
            return new EffectAtom
            {
                Kind = EffectAtomKind.StatModifier, Target = t,
                StatKey = statKey, StatDelta = delta, StatTurns = turns,
            };
        }
    }

    /// <summary>一个技能。一个单位固定三个：普攻（CD 0）/ 战技 / 绝技。</summary>
    public sealed class SkillDef
    {
        public string Id;
        public string Name;
        public SkillType Type;
        public int Cd;                       // 0 = 无冷却（普攻）
        public Element Element;              // 技能自身五行，影响伤害系数的取用
        public TargetSelector PrimaryTarget; // 效果没显式指定目标时用它
        public EffectAtom[] Effects;
        public string Description;           // 保留 GDD 原文，便于对照

        public bool IsUltimate => Type == SkillType.Ultimate;
        public override string ToString() => $"{Name}({Cn.Of(Type)},CD{Cd})";

        /// <summary>用于 BattleFactory 给每个上阵单位发一份独立副本（融合会改写技能）。</summary>
        public SkillDef Clone()
        {
            return new SkillDef
            {
                Id = Id, Name = Name, Type = Type, Cd = Cd, Element = Element,
                PrimaryTarget = PrimaryTarget,
                Effects = Effects == null ? null : (EffectAtom[])Effects.Clone(),
                Description = Description,
            };
        }
    }

    /// <summary>
    /// 一只异兽的静态定义。这是「宿主」层：外形 + 技能骨架。
    /// 融合时灵魂会改写 element / trait / skills（GDD 6.2 的 Fuse），
    /// 所以运行时单位持有的是一份**可变副本**，不是这里这份定义。
    /// </summary>
    public sealed class BeastDef
    {
        public string Id;              // 拼音，如 jumang
        public string DisplayName;     // 句芒
        public Element Element;
        public RoleType Role;
        public Rarity Rarity;

        public string Source;          // 典籍出处
        public string Quote;           // 典籍原文
        public string Lore;            // 设定描述
        public string Codex;           // 图鉴词条
        public TraitDef Trait;

        public SkillDef Basic;
        public SkillDef Active;
        public SkillDef Ultimate;
        /// <summary>觉醒技（第 4 槽）：异兽进化后由局外装备的终结技类技能。未装备时为空。</summary>
        public SkillDef Awaken;

        public PaletteHex Palette;

        // ---- 基础面板。GDD 没给数值表，这些是 STEP 1 的**占位数值**， ----
        // ---- 由 BattleConfig 按职业/稀有度生成，等策划给正式表后替换。 ----
        public int BaseHp;
        public int BaseAtk;
        public int BaseDef;
        public int BaseSpeed;
        public float CritRate;
        public float CritDamage;

        public SkillDef[] AllSkills => new SkillDef[] { Basic, Active, Ultimate, Awaken };

        /// <summary>
        /// 深一层的副本。**上阵时每个单位拿一份**，不是共享同一份定义 ——
        /// 融合（GDD 6.2）会改写 element / trait / skills，
        /// 若共享定义，改一只怪会把"同id的所有怪"一起改掉，而且会污染到下一场战斗。
        /// </summary>
        public BeastDef Clone()
        {
            return new BeastDef
            {
                Id = Id, DisplayName = DisplayName, Element = Element,
                Role = Role, Rarity = Rarity,
                Source = Source, Quote = Quote, Lore = Lore, Codex = Codex, Trait = Trait,
                Basic = Basic?.Clone(), Active = Active?.Clone(), Ultimate = Ultimate?.Clone(), Awaken = Awaken?.Clone(),
                Palette = Palette,
                BaseHp = BaseHp, BaseAtk = BaseAtk, BaseDef = BaseDef, BaseSpeed = BaseSpeed,
                CritRate = CritRate, CritDamage = CritDamage,
            };
        }

        public override string ToString() => $"{DisplayName}[{Cn.Of(Element)}{Cn.Of(Role)}]";
    }
}
