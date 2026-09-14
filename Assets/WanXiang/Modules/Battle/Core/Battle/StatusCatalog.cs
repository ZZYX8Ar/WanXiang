// ============================================================================
//  万相 · 战斗核心 · 状态（增益 / 减益）目录
//  ---------------------------------------------------------------------------
//  状态用**数据**描述，不是每种状态一个子类。理由同 EffectAtom：
//  GDD 里 30 只怪 + 24 条天时提到的状态有几十种，一状态一子类会立刻失控，
//  而它们的行为其实只有几个维度：
//
//      每回合扣血（灼烧 / 冰蚀 / 蚀）      → DotFlatPerStack
//      不能行动 / 不能被治疗（冻结）        → PreventsAction / PreventsHeal
//      改受伤 / 改攻击 / 改速度 / 改治疗     → DamageTakenDelta / AttackDelta / ...
//      层数与上限（同气 5 层、湿 5 层…）    → MaxStacks
//
//  ⭐ 一个必须交代的实现取舍：**持续伤害存的是"数值"而不是"公式"**。
//  GDD 里灼烧写的是「每层每回合造成 4% **施加者**攻击的伤害」—— 它依赖施加者，
//  而施加者可能已经死了。做法有两种：
//     (a) 状态里存施加者引用，每回合去读它的攻击力 ⇒ 施加者死了要处理悬空引用
//     (b) **施加瞬间**把倍率折算成具体数值存下来 ⇒ 之后与施加者无关
//  取 (b)。它更接近玩家直觉（"这一层烧就是这么多"），也免掉了悬空引用这一类
//  只在特定顺序下才复现的 bug。代价是"施加者攻击力中途提高不会加强已有灼烧"，
//  这个差异已记录，若策划要求实时联动，改 StatusInstance 一处即可。
// ============================================================================

namespace WanXiang.Battle.Core
{
    public struct StatusDef
    {
        public string Id;
        public string Name;
        public bool IsDebuff;

        /// <summary>每层每回合造成的固定伤害（在施加瞬间由施加者的攻击力 / 目标最大生命折算而来）。</summary>
        public float DotFlatPerStack;

        /// <summary>层数上限。1 表示不可叠层。</summary>
        public int MaxStacks;

        /// <summary>无法行动（冻结 / 眩晕 / 眠）。</summary>
        public bool PreventsAction;

        /// <summary>
        /// 无法**释放技能**（沉默）：普攻仍可打。与 <see cref="PreventsAction"/> 分开 ——
        /// GDD 里「沉默」是封技能而不是封行动，混成一个标志会让沉默强得离谱。
        /// </summary>
        public bool PreventsSkill;

        /// <summary>无法被治疗（冻结 / 禁疗）。</summary>
        public bool PreventsHeal;

        /// <summary>每层对"受到伤害"的增减（+0.08 表示受伤害 +8%）。</summary>
        public float DamageTakenDeltaPerStack;

        /// <summary>每层对攻击力的增减。</summary>
        public float AttackDeltaPerStack;

        /// <summary>每层对速度的增减。</summary>
        public float SpeedDeltaPerStack;

        /// <summary>每层对"造成的治疗与护盾"的增减。</summary>
        public float HealShieldDeltaPerStack;

        /// <summary>每次受击时是否移除 1 层（护盾类常见）。</summary>
        public bool ConsumeStackOnHit;

        /// <summary>
        /// 是否是**常驻计数器**（「尾」「食」「秋决」这类"本场累积、不按回合衰减"的层数）。
        /// 常驻计数器的 TurnsRemaining 恒为 -1，不参与回合末减层。
        /// </summary>
        public bool IsLingeringCounter;

        public string Description;
    }

    /// <summary>
    /// 状态目录。**只收 STEP 1 自检与灰盒需要的那一批**，其余随内容逐步补。
    /// 加一条新的不需要动战斗循环 —— 这正是把它做成数据的目的。
    /// </summary>
    public static class StatusCatalog
    {
        // ---- 减益 ----
        public const string Burn        = "burn";         // 灼烧（火）
        public const string IceErosion  = "ice_erosion";  // 冰蚀（大寒节点）
        public const string Corrode     = "corrode";      // 蚀（犰狳 / 蛇）
        public const string Wet         = "wet";          // 湿（受水伤 +）
        public const string Frost       = "frost";        // 霜（速度 -）
        public const string Freeze      = "freeze";       // 冻结（不能行动、不能被治疗）
        public const string ArmorBreak  = "armor_break";  // 裂甲 / 破防
        public const string Marked      = "marked";       // 斩标 / 易伤
        public const string Confuse     = "confuse";      // 混乱（不能行动）—— 清明免疫它
        public const string Silence     = "silence";      // 沉默（不能放技能）—— 清明免疫它

        // ---- 增益 ----
        public const string Qi          = "qi";           // 同气（相生相邻产出，每层 +2% 技能效果）
        public const string Vigor       = "vigor";        // 生机（立春）
        public const string Grain       = "grain";        // 谷（谷雨，每层 +1% 全属性）
        public const string Bounty      = "bounty";       // 穰（当康，治疗护盾 +）
        public const string Haste       = "haste";        // 凝神（寒露：下一次技能 CD 立即 -2）

        private static readonly System.Collections.Generic.List<StatusDef> _all =
            new System.Collections.Generic.List<StatusDef>
        {
            new StatusDef { Id = Burn, Name = "灼烧", IsDebuff = true, MaxStacks = 10,
                DotFlatPerStack = 0f, Description = "每层每回合造成伤害（数值在施加时按施加者攻击力折算）" },

            new StatusDef { Id = IceErosion, Name = "冰蚀", IsDebuff = true, MaxStacks = 20,
                DotFlatPerStack = 0f, Description = "每层每回合造成 1.5% 最大生命伤害" },

            new StatusDef { Id = Corrode, Name = "蚀", IsDebuff = true, MaxStacks = 5,
                DotFlatPerStack = 0f, Description = "持续腐蚀，逐回合掉血" },

            new StatusDef { Id = Wet, Name = "湿", IsDebuff = true, MaxStacks = 5,
                DamageTakenDeltaPerStack = 0.08f, Description = "每层受水属性伤害 +8%" },

            new StatusDef { Id = Frost, Name = "霜", IsDebuff = true, MaxStacks = 5,
                SpeedDeltaPerStack = -0.08f, Description = "每层速度 -8%" },

            new StatusDef { Id = Freeze, Name = "冻结", IsDebuff = true, MaxStacks = 1,
                PreventsAction = true, PreventsHeal = true, DamageTakenDeltaPerStack = 0.20f,
                Description = "无法行动、无法被治疗，受到伤害 +20%" },

            new StatusDef { Id = ArmorBreak, Name = "裂甲", IsDebuff = true, MaxStacks = 8,
                DamageTakenDeltaPerStack = 0.08f, Description = "护甲与减伤下降" },

            new StatusDef { Id = Marked, Name = "斩标", IsDebuff = true, MaxStacks = 1,
                DamageTakenDeltaPerStack = 0.20f, Description = "受到的所有伤害 +20%" },

            // 清明「气清景明」免疫的就是这两条（GDD 3.3 第 5 条）
            new StatusDef { Id = Confuse, Name = "混乱", IsDebuff = true, MaxStacks = 1,
                PreventsAction = true, Description = "无法行动（清明可免疫）" },

            new StatusDef { Id = Silence, Name = "沉默", IsDebuff = true, MaxStacks = 1,
                PreventsSkill = true, Description = "无法释放技能，普攻仍可（清明可免疫）" },

            new StatusDef { Id = Qi, Name = "同气", IsDebuff = false, MaxStacks = 5,
                Description = "相生相邻产出。每层 +2% 技能效果" },

            new StatusDef { Id = Vigor, Name = "生机", IsDebuff = false, MaxStacks = 5,
                Description = "每层每回合回复 2% 最大生命" },

            new StatusDef { Id = Grain, Name = "谷", IsDebuff = false, MaxStacks = 10,
                AttackDeltaPerStack = 0.01f, Description = "每层 +1% 全属性" },

            new StatusDef { Id = Bounty, Name = "穰", IsDebuff = false, MaxStacks = 6,
                HealShieldDeltaPerStack = 0.05f, Description = "每层使治疗与护盾效果 +5%" },

            // 寒露「寒露凝华」：CD 即减在施加时一次性兑现（见 BattleSimulator 的寒露落点），
            // 状态本身是**可读的凭据**（日志/UI 看得到"谁拿到了凝神"），不参与回合末减层以外的事。
            new StatusDef { Id = Haste, Name = "凝神", IsDebuff = false, MaxStacks = 1,
                Description = "下一次技能 CD 立即减少 2 回合（施加时已兑现）" },
        };

        /// <summary>查找。找不到返回一个"未知状态"定义（名字即 id、不可叠）而不是抛异常 ——
        /// 状态是内容数据，写错一个不该让整场战斗起不来。</summary>
        public static StatusDef Get(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == id) return _all[i];
            return new StatusDef { Id = id, Name = id, MaxStacks = 1, IsDebuff = true,
                Description = "（未登记的状态）" };
        }

        public static System.Collections.Generic.IReadOnlyList<StatusDef> All => _all;

        /// <summary>一个便于书写天时/技能的内联构造：按"目标最大生命 x%"折算持续伤害。</summary>
        public static float DotFromMaxHp(float percent, int maxHp) => maxHp * percent;
    }
}
