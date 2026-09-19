// ============================================================================
//  万相 · 战斗核心 · 战斗配置（唯一可调数值入口）
//  ---------------------------------------------------------------------------
//  GDD 第 7 章 STEP 1 验收标准③：「调整 ElementMatrixConfig 的四个系数
//  能立刻在战斗中观察到变化」。要做到"立刻可观察"，前提是所有能调的数值
//  只有**这一个**入口 —— 散落在各 System 里写死的数字，改起来就没人敢改。
//
//  另外 GDD 技术风险清单第 4 条要求："所有系数集中放在 ElementMatrixConfig 里，
//  保证可调"。这里把"矩阵系数"扩成"整套战斗系数"，理由一样。
//
//  ⚠ 面板数值的来源说明：GDD 给了每只怪的技能倍率与 CD，**没给基础属性面板**
//  （生命/攻击/防御/速度）。所以这里按"职业 + 稀有度"用公式生成占位面板，
//  等策划给正式数值表后替换 BaseStats 部分即可，公式与战局逻辑无关。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>开局基础面板：按职业给一组基准值，再乘稀有度系数。</summary>
    public struct RoleBaseline
    {
        public int Hp;
        public int Atk;
        public int Def;
        public int Speed;
    }

    public sealed class BattleConfig
    {
        // ---------------------------------------------------------------- 五行
        /// <summary>五行伤害系数。GDD 2.3：1.50 克 / 0.75 被克 / 0.90 同属 / 1.00 无关系。</summary>
        public ElementCoefficients Elements = ElementCoefficients.Default;

        /// <summary>
        /// 相生**不参与**伤害计算（GDD 2.3 设计说明）。留一个开关只是为了做对照实验，
        /// 正式玩法下必须保持 false —— 相生如果也给伤害，同一体系既加输出又加生存，数值会失控。
        /// </summary>
        public bool GenerateAffectsDamage = false;

        // ---------------------------------------------------------------- 棋盘规则（GDD 2.5）
        /// <summary>相生相邻：双方每回合开始回复 3% 最大生命。</summary>
        public float AdjacencyGenerateRegenPercent = 0.03f;
        /// <summary>相生相邻：各获得 1 层「同气」。</summary>
        public int AdjacencyGenerateQiPerTurn = 1;
        /// <summary>「同气」每层 +2% 技能效果。</summary>
        public float QiEffectPerStack = 0.02f;
        /// <summary>「同气」上限 5 层。</summary>
        public int QiMaxStacks = 5;

        /// <summary>相克相冲：双方每回合开始各受 2% 最大生命的真实伤害（无视护盾）。</summary>
        public float AdjacencyCounterTrueDamagePercent = 0.02f;
        /// <summary>相克相冲：怒气获取 -10%。</summary>
        public float AdjacencyCounterRageDelta = -0.10f;

        /// <summary>中宫土德：受到的所有伤害 -8%。</summary>
        public float CenterDamageReduction = 0.08f;

        /// <summary>
        /// 中宫是否**平息涉及自身**的相冲。
        ///
        /// ⚠ GDD 2.5 原文是「相邻四格的单位（无论什么属性）都不会触发相冲」，
        ///   这句有两种读法：
        ///     (a) 站中宫的单位，与它相邻的四格之间不产生相冲（中宫是"和事佬"）
        ///     (b) 中宫周围那四格上的单位，彼此之间不产生相冲（调停半径更大）
        ///   正文的举例是"把矛盾的属性放在中宫旁边，冲突被平息"，
        ///   而中宫旁边那四格彼此并不相邻、本来就不会触发相冲 ——
        ///   按 (b) 读会让这条规则几乎没有作用面。**故取 (a)**。
        ///   这条歧义已记录，待策划确认；改这里一个布尔值即可切换，不用动战斗循环。
        /// </summary>
        public bool CenterSuppressesAdjacentCounter = true;

        /// <summary>
        /// 同属共鸣的计数口径。
        ///
        /// ⚠ GDD 2.5 只写「同属性单位达到 2 / 4 / 5 只」，没说数的是谁：
        ///     (a) **开局上阵名单**（默认）—— 死了也算数，符合"羁绊是阵容属性"的直觉
        ///     (b) 场上存活 —— 死一个就掉档，更戏剧化但也更惩罚
        ///   取 (a)。改这个布尔值即可切换，不用动 BoardRules 的逻辑。
        /// </summary>
        public bool ResonanceCountsAliveOnly = false;

        /// <summary>
        /// 「同气」的持续回合数。GDD 只写了"上限 5 层"，没写掉层规则。
        /// 取 2 回合的语义是：相邻 = 每回合续期，所以**贴着就一直是 5 层**；
        /// 一旦把人挪开，余气还能留 1 个回合再散。见 BoardRules 的说明。
        /// </summary>
        public int QiDurationTurns = 2;

        /// <summary>
        /// STEP 1 自动战斗是否自动放绝技。正式玩法下绝技是玩家的手动干预点
        /// （GDD 1.3「玩家在关键回合手动触发绝技」），此处为 true 只是为了让
        /// 灰盒自检能跑完整套技能循环。做 UI 时置 false。
        /// </summary>
        public bool AutoCastUltimate = true;

        /// <summary>AI 策略组（只影响敌方单位的自动决策；v2.1 P4）。</summary>
        public AiProfile AiProfile = AiProfile.Balanced;

        /// <summary>「生机」每层每回合回复最大生命的百分比（GDD 立春节点）。</summary>
        public float VigorRegenPerStack = 0.02f;

        /// <summary>同属共鸣：同属性 2 / 4 / 5 只时的攻击加成与共鸣技 CD 变化。</summary>
        public struct ResonanceTier
        {
            public int Count;
            public float AttackBonus;
            public bool UnlockResonanceSkill;
            public int ResonanceCdDelta;
        }

        public static readonly ResonanceTier[] ResonanceTiers =
        {
            new ResonanceTier { Count = 2, AttackBonus = 0.08f, UnlockResonanceSkill = false, ResonanceCdDelta = 0 },
            new ResonanceTier { Count = 4, AttackBonus = 0.16f, UnlockResonanceSkill = true,  ResonanceCdDelta = 0 },
            new ResonanceTier { Count = 5, AttackBonus = 0.25f, UnlockResonanceSkill = true,  ResonanceCdDelta = -2 },
        };

        // ---------------------------------------------------------------- 伤害公式
        /// <summary>
        /// 防御减伤常数 K：减伤率 = def / (def + K)。
        /// 取 100 是因为占位面板的防御在 20~60 区间，K=100 时减伤落在 17%~38%，
        /// 属于"看得见但不主宰"的区间 —— 与 GDD"属性克制是优势不是胜负手"同一取向。
        /// </summary>
        public float DefenseConstant = 100f;

        /// <summary>
        /// 先手连击门槛（GDD v1.1 §3.6，INITIATIVE_RATIO=1.50）：
        /// 每回合生成出手序列前判定，某单位速度 ≥ 敌方最高速度 × 本值 ⇒ 本回合常规行动后
        /// 额外获得一次行动；**每方每回合最多 1 次**（防高速队无限连）。
        /// 「大雪 · 闭塞成冬」把它降到 1.20（见 WeatherDef.InitiativeRatioOverride）。
        /// </summary>
        public float InitiativeRatio = 1.50f;

        /// <summary>大雪节点的连击门槛（INITIATIVE_RATIO_SNOW）。</summary>
        public float InitiativeRatioSnow = 1.20f;

        /// <summary>
        /// 伤害抖动幅度（GDD v1.1 §3.1 的 Rand ∈ [0.95, 1.05]，即 ±5%）。
        /// 用途不是"随机性"，而是让同种子回放的日志不像复读机 —— 全程走
        /// DeterministicRandom，同种子逐位可复现。设为 0 = 关掉抖动（对照实验用）。
        /// </summary>
        public float DamageJitter = 0.05f;

        /// <summary>「逆天时」反噬：覆盖引入的属性被节气相克时我方该属性的额外承伤
        /// （默认 0.15；劫律 03「逆天之罚」把它提到 0.25）。</summary>
        public float BacklashExtraDamage = 0.15f;

        /// <summary>灼烧类持续伤害的全局乘数（默认 1；劫律 11「灼烧入骨」= 1.30）。
        /// 只作用于灼烧状态（<see cref="StatusCatalog.Burn"/>），冰蚀独立。</summary>
        public float BurnTakenMul = 1f;

        /// <summary>劫律 12「冰蚀不化」：冰蚀的每层伤害乘数（默认 1；劫律 → 1.67 ≈ 1.5%→2.5%）。</summary>
        public float IceErosionDotMul = 1f;

        /// <summary>劫律 10「回天无力」：所有复活效果只恢复到标称血量的 60%（乘在百分比上）。</summary>
        public float ReviveHpScale = 1f;

        /// <summary>劫律 15「五行失序」：同属共鸣门槛整体 +1（2/4/5 → 3/5/5，第三档 5 封顶失效）。</summary>
        public int ResonanceCountShift = 0;

        /// <summary>劫律 20「万相归一」：敌方每回合攻击 +1%（血不涨 —— 偏差已记录）。</summary>
        public bool AllIsOne;

        /// <summary>
        /// 复制一份但关掉伤害抖动 —— **专供受控对照实验**。
        /// 为什么必须有它：自检里"×0.7 就是 ×0.7"这类乘区断言，
        /// 会被 ±5% 抖动直接带偏（实测三处对照全红）。抖动的存在意义是
        /// 让回放日志不像复读机，而不是给断言的对照添噪声。
        /// </summary>
        public BattleConfig WithoutJitter()
        {
            var c = (BattleConfig)MemberwiseClone();
            c.DamageJitter = 0f;
            return c;
        }

        /// <summary>闪避/命中：STEP 1 先不做命中率随机，避免在验证节奏时引入额外噪声。</summary>
        public bool EnableHitChance = false;

        /// <summary>暴击伤害基准。GDD 秋幕节点写"暴击伤害 +40%"，说明暴伤是可被天时改的乘区。</summary>
        public float CritDamageDefault = 0.50f;   // 即 150% 伤害

        /// <summary>单场回合上限。到上限仍未分胜负记为 Draw —— 对应 GDD 春分节点"超过 12 回合"的措辞，
        /// 取 30 是给长线阵容留空间，同时保证跑不飞的沙盒能收敛。</summary>
        public int MaxTurns = 30;

        /// <summary>回合之间的语义间隔（秒）。STEP 1 逻辑不 sleep，这个值只供表现层读。</summary>
        public float TurnIntervalSeconds = 0.30f;

        // ---------------------------------------------------------------- 怒气与绝技
        /// <summary>每次普攻获得的怒气。</summary>
        public float RagePerBasicAttack = 20f;
        /// <summary>每次受击获得的怒气。</summary>
        public float RageWhenHit = 10f;
        /// <summary>怒气满值。绝技需要在怒气满时才可释放（或 CD 到 + 怒气满，二者取一并列在注释里）。</summary>
        public float RageMax = 100f;

        /// <summary>
        /// 绝技是否同时受怒气约束。**必须为 true**，理由不是"手感"而是自洽性：
        ///
        ///   GDD 2.5 的「相克相冲」有一条效果是**怒气获取 -10%**。如果怒气对绝技没有约束，
        ///   这条效果就永远是装饰 —— 玩家感受不到，策划调它也没有意义。
        ///   反过来说，只要怒气能挡住绝技，站位（挨着谁）就真的影响了"你能不能放大招"，
        ///   棋盘规则与技能循环才咬合上。
        ///
        /// ⚠ 另一个理由是节奏：CD 在开局都是 0，若绝技只看 CD，**每一场战斗的第一回合
        ///   就会全员放绝技**（1v1 会在 2.5 回合内打完）。首次跑自检时就是这个问题，
        ///   修掉之后 1v1 从 2.5 回合变成 5~7 回合。
        /// </summary>
        public bool UltimateNeedsRage = true;

        /// <summary>释放绝技消耗的怒气（= 怒气满值，也就是"清零"）。</summary>
        public float UltimateRageCost = 100f;

        // ---------------------------------------------------------------- 职业基础面板（占位）
        public RoleBaseline Guard   = new RoleBaseline { Hp = 1800, Atk = 80,  Def = 60, Speed = 90 };
        public RoleBaseline Striker = new RoleBaseline { Hp = 1000, Atk = 160, Def = 25, Speed = 110 };
        public RoleBaseline Caster  = new RoleBaseline { Hp = 1150, Atk = 145, Def = 30, Speed = 100 };
        public RoleBaseline Support = new RoleBaseline { Hp = 1300, Atk = 110, Def = 35, Speed = 95 };
        // v1.1 定版：速度 130 → 150。3.6 节「先手连击」的门槛是 1.50×，
        // 疾对术（基准 100）= 150/100 = 1.50 刚好跨过门槛；130 时只有 1.30，
        // 速度这个属性在回合制里几乎不产生价值（早 0.1 秒行动还是一回合）。
        public RoleBaseline Swift   = new RoleBaseline { Hp = 1100, Atk = 130, Def = 28, Speed = 150 };

        /// <summary>稀有度对面板的乘数。灵品 1.00 / 玄品 1.15 / 神品 1.30。</summary>
        // ---- 稀有度倍率（§3.4：灵/玄/神 = 1.00 / 1.15 / 1.30）----
        // v1.1 把它们从 switch 常量提成数据 —— §3.7 的立场是"调整平衡只改这几行"，
        // 且测试里模拟"局内成长后的队伍"（神品 ×1.9）也要动它。
        public float RareMultiplier = 1.00f;
        public float EpicMultiplier = 1.15f;
        public float LegendMultiplier = 1.30f;

        public float RarityMultiplier(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return RareMultiplier;
                case Rarity.Epic: return EpicMultiplier;
                case Rarity.Legend: return LegendMultiplier;
                default: return 1.00f;
            }
        }

        public RoleBaseline BaselineFor(RoleType role)
        {
            switch (role)
            {
                case RoleType.Guard: return Guard;
                case RoleType.Striker: return Striker;
                case RoleType.Caster: return Caster;
                case RoleType.Support: return Support;
                case RoleType.Swift: return Swift;
                default: return Caster;
            }
        }

        /// <summary>
        /// 用职业基准 + 稀有度乘数算出一只怪的占位面板。
        /// ⚠ 这是**过渡实现**：GDD 没给数值表。等策划给出 30 只怪的面板后，
        ///   改成"读表"即可，调用方（BattleUnit）不用动。
        /// </summary>
        public void ApplyPlaceholderStats(BeastDef def)
        {
            var b = BaselineFor(def.Role);
            float m = RarityMultiplier(def.Rarity);
            def.BaseHp = (int)(b.Hp * m);
            def.BaseAtk = (int)(b.Atk * m);
            def.BaseDef = (int)(b.Def * m);
            def.BaseSpeed = (int)(b.Speed * m);
            def.CritRate = def.Role == RoleType.Striker ? 0.25f : 0.15f;
            def.CritDamage = CritDamageDefault;
            // 疾类天生有额外速度，体现"疾"这个定位
            if (def.Role == RoleType.Swift) def.BaseSpeed = (int)(def.BaseSpeed * 1.15f);
        }

        public static BattleConfig Default => new BattleConfig();

        /// <summary>
        /// 复制一份。用途：一场战斗必须**独占**自己的配置 ——
        /// 否则"改完系数先看这一局、再重跑"时，玩家拖动的滑块会直接把
        /// 正在播放的这一局的系数改掉，画面与已算好的帧不一致。
        ///
        /// 所有字段都是值类型（含 <see cref="RoleBaseline"/> 与 <see cref="ElementCoefficients"/>
        /// 两个 struct），所以逐位复制就够，不需要手写字段列表。
        /// 将来若加入引用类型字段（比如技能表），这里必须改成深拷贝。
        /// </summary>
        public BattleConfig Clone() => (BattleConfig)MemberwiseClone();
    }
}
