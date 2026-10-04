// ============================================================================
//  万相 · 首领内容表（Battle.Core）
//  ---------------------------------------------------------------------------
//  14 个首领 = 幕一~幕四各 3 + 幕五 2。每个幕一个专属池，由局种子确定性抽 1 个
//  （BossFor）。首领是**专属内容**，id 以 b_ 开头，绝不进玩家图鉴 / 招募 / 灵市。
//
//  机制 = 数据：AttachHooks 按 boss id 把《首领战设计文档 §9》的积木拼起来，
//  这里只声明"谁用哪几个积木、什么参数"，不直接写战斗逻辑（逻辑全在 BossHooks.cs）。
//
//  ⚠ 数值只是"能跑起来"的锚点（设计文档 §11 给的是相对系数；实际强度由
//     EnemyBudget.BossUnitMul(act) 再乘一次）。平衡与轮回解锁是 P5 的事。
// ============================================================================

using System;
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>一只首领的静态定义（id 必须以 b_ 开头，触发 BossCatalog 的专属路径）。</summary>
    public sealed class BossDef
    {
        public string Id;
        public string DisplayName;
        public Element Element;
        public RoleType Role;
        public Rarity Rarity = Rarity.Legend;
        public int BaseHp, BaseAtk, BaseDef, BaseSpeed;
        public float CritRate = 0.15f, CritDamage = 0.50f;

        public BeastDef ToBeastDef()
        {
            var d = new BeastDef
            {
                Id = Id,
                DisplayName = DisplayName,
                Element = Element,
                Role = Role,
                Rarity = Rarity,
                BaseHp = BaseHp,
                BaseAtk = BaseAtk,
                BaseDef = BaseDef,
                BaseSpeed = BaseSpeed,
                CritRate = CritRate,
                CritDamage = CritDamage,
            };
            var sk = BossSkills.Default(Element);
            d.Basic = sk[0]; d.Active = sk[1]; d.Ultimate = sk[2];
            return d;
        }
    }

    /// <summary>
    /// 首领默认技组（让首领在自动战斗里会动）。元素随首领，伤害按攻击力倍率。
    /// ★ 2026-10-04：**三招全部改群体攻击（AllEnemies）** —— 守关改「单 Boss 独占中宫」后，
    ///   首领只剩自己一只，必须靠 AOE 才能压制玩家整队（用户定案："给首领技能加 AOE"）。
    ///   单体技组下首领每回合只削一个目标 ⇒ 五只满编队可以无视它慢慢打。
    ///
    /// ⛔ **不能只改目标选择器而保留原倍率** —— 单体倍率 × 5 个目标 = 总输出直接翻 5 倍。
    ///   实测（boss.selftest [7/7]）：1.00/0.80/2.00 全 AOE 后，四幕「无遗物」胜率全部掉到 **0%**。
    ///   ⇒ 原则：**压低单体倍率换取群体形态**。实测三档（boss.selftest [7/7]，四幕「无遗物」胜率）：
    ///     · 1.00/0.80/2.00 全 AOE（= 原倍率）⇒ 四幕全 0%（**必然过强**，总输出翻 5 倍）；
    ///     · 0.20/0.80/0.40（只改形态/总输出守恒）⇒ 偏软（第2幕中等 100%、第3幕 98%）：
    ///       **集火 > 分摊** —— 把致命伤害摊平，治疗奶得回来，反而杀不掉人；
    ///     · 0.40/0.80/0.80（本档）⇒ 取中间值，兼顾"能压制全队"与"不秒全队"。
    ///   ⚠ 第2幕（火 · 烬蛟「附烧」）偏难的原因**不是伤害**：附烧是**每命中一次**就叠一层，
    ///     AOE 普攻 ⇒ 每回合给全队叠灼烧 ⇒ 被放大 5 倍。要治它得把"命中类钩子"改成每行动只触发一次，
    ///     属独立任务（不在本次改动内）。
    /// </summary>
    public static class BossSkills
    {
        public static SkillDef[] Default(Element element)
        {
            return new[]
            {
                new SkillDef { Id = "b_basic", Name = "普攻", Type = SkillType.Basic, Cd = 0,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = new[] { EffectAtom.Damage(TargetSelector.AllEnemies, 0.40f) },
                    Description = "全体攻击" },
                new SkillDef { Id = "b_active", Name = "战技", Type = SkillType.Active, Cd = 2,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = new[] { EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f) },
                    Description = "全体攻击" },
                new SkillDef { Id = "b_ult", Name = "绝技", Type = SkillType.Ultimate, Cd = 3,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = new[] { EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f) },
                    Description = "全体重击" },
            };
        }
    }

    /// <summary>14 个首领的内容表 + 确定性抽取 + 钩子装配。</summary>
    public static class BossCatalog
    {
        // ---- 召唤物原型（id 以 summon_ 开头，ApplyPlaceholderStats 跳过、保留弱面板） ----
        /// <summary>
        /// 造一个召唤物原型。
        /// ⚠ <paramref name="id"/> **要能查到立绘**，否则 `BattleStage2D` 会走"按 catalog 序号
        ///   随便分配一张"的兜底 ⇒ 画面上显示成别的异兽（用户实测"双子只有一个"就是这个）。
        ///   所以召唤物**直接复用首领的 b_ id**（如 b_suren 素刃），既能查到立绘、
        ///   又因为不是 IsBoss 前缀判断的重点而不会跟着放大。
        /// </summary>
        private static BeastDef Summon(string id, string name, Element el, RoleType role,
                                       int hp, int atk, int def, int spd)
        {
            var d = new BeastDef
            {
                Id = id, DisplayName = name, Element = el, Role = role, Rarity = Rarity.None,
                BaseHp = hp, BaseAtk = atk, BaseDef = def, BaseSpeed = spd,
                CritRate = 0.10f, CritDamage = 0.40f,
            };
            var sk = BossSkills.Default(el);
            d.Basic = sk[0]; d.Active = sk[1]; d.Ultimate = sk[2];
            return d;
        }

        private static readonly BeastDef TengNu   = Summon("summon_tengnu", "藤奴", Element.Wood, RoleType.Striker, 700, 90, 40, 95);
        private static readonly BeastDef FenShen  = Summon("summon_fenshen", "半血分身", Element.Fire, RoleType.Striker, 900, 140, 50, 100);
        private static readonly BeastDef RongYing = Summon("summon_rongying", "熔岩幼体", Element.Fire, RoleType.Striker, 800, 120, 50, 90);
        private static readonly BeastDef JingYing = Summon("summon_jingying", "镜影", Element.Water, RoleType.Striker, 850, 130, 50, 105);
        private static readonly BeastDef ZaoHua   = Summon("summon_zaohua", "造化兽", Element.Wood, RoleType.Striker, 800, 140, 50, 100);
        private static readonly BeastDef ShouHu   = Summon("summon_shouhu", "守卫卵", Element.Earth, RoleType.Guard, 500, 40, 80, 60);
        // ⚠ id 用 **b_suren**（素刃的真实立绘 id），不是 summon_subai：
        //   summon_ 前缀在 SpriteCatalog 里查不到图 ⇒ 表现层会"按序号随便分配"⇒ 双子看起来只有一个。
        // ⚠ id 用 **b_suren**（素刃的真实立绘 id，见 TwinBeastId），不是 summon_subai：
        //   summon_ 前缀在 SpriteCatalog 里查不到图 ⇒ 表现层"按序号随便分配"⇒ 双子看起来只有一个。
        private static readonly BeastDef SuBai   = Summon(TwinBeastId, "素刃", Element.Metal, RoleType.Striker, 1820, 266, 84, 110); // 白魍双子（×0.7 基准）

        // 玄溟「冰晶重生」用的冰晶：**面板只是占位** —— 实际 HP 由
        // IceCrystalRebirthHook 用 GrowMaxHp 对齐到「玄溟满血 × 25%」，
        // 这样它在不同幕/劫数下的比例都是恒定的。
        // id 用 summon_ 前缀 ⇒ 表现层走五行色块（无专属立绘时不做"乱分配"）。
        private static readonly BeastDef IceCrystal = Summon("summon_icecrystal", "冰晶", Element.Water, RoleType.Guard, 800, 100, 60, 90);

        // ---- 14 个首领定义 ----
        private static readonly BossDef[] AllBosses =
        {
            // 幕一 · 春（木）
            new BossDef { Id="b_fuman",  DisplayName="腐木之君·蝮魇", Element=Element.Wood,  Role=RoleType.Guard,    BaseHp=3000, BaseAtk=300, BaseDef=140, BaseSpeed=85 },
            new BossDef { Id="b_manman", DisplayName="缠丝女萝·蔓娘", Element=Element.Wood,  Role=RoleType.Swift,    BaseHp=2600, BaseAtk=340, BaseDef=110, BaseSpeed=120 },
            new BossDef { Id="b_wangliang", DisplayName="瘴林之影·魍魉", Element=Element.Wood, Role=RoleType.Striker, BaseHp=2400, BaseAtk=360, BaseDef=100, BaseSpeed=135 },
            // 幕二 · 夏（火）
            new BossDef { Id="b_chiba",  DisplayName="炎狱之君·赤魃", Element=Element.Fire,  Role=RoleType.Striker,  BaseHp=2800, BaseAtk=380, BaseDef=120, BaseSpeed=105 },
            new BossDef { Id="b_jinjiao", DisplayName="焚天凶骸·烬蛟", Element=Element.Fire,  Role=RoleType.Striker,  BaseHp=2700, BaseAtk=360, BaseDef=115, BaseSpeed=110 },
            new BossDef { Id="b_jiaozhi", DisplayName="熔岩行尸·燋彘", Element=Element.Fire,  Role=RoleType.Guard,    BaseHp=3200, BaseAtk=320, BaseDef=160, BaseSpeed=80 },
            // 幕三 · 秋（金）
            // ⚠ 白魍的 id 用 **b_bairen**（白刃），不是 b_baiwang ——
            //   设计上"肃杀之君·白魍"就是**双子本身**（白刃 + 素刃），没有第三个"白魍本体"立绘。
            //   b_baiwang 从来不是立绘 CSV 里的 id（那份表只有 b_bairen / b_suren）。
            //   历史坑：曾用 b_baiwang 当 id，SpriteCatalog 查不到图 ⇒ 表现层"按序号随便分配"
            //   ⇒ 最后靠 cp b_bairen.png 补了个副本，结果两张立绘一模一样（用户发现）。
            new BossDef { Id="b_bairen", DisplayName="白魍·白刃", Element=Element.Metal, Role=RoleType.Striker, BaseHp=2600, BaseAtk=380, BaseDef=120, BaseSpeed=110 },
            new BossDef { Id="b_shai",   DisplayName="千机傀儡·铩",   Element=Element.Metal, Role=RoleType.Guard,    BaseHp=3300, BaseAtk=340, BaseDef=170, BaseSpeed=80 },
            new BossDef { Id="b_shuangfeng", DisplayName="断刃游侠·霜锋", Element=Element.Metal, Role=RoleType.Swift, BaseHp=2500, BaseAtk=420, BaseDef=110, BaseSpeed=140 },
            // 幕四 · 冬（水）
            new BossDef { Id="b_xuanming", DisplayName="凝冰之君·玄溟", Element=Element.Water, Role=RoleType.Guard,  BaseHp=3400, BaseAtk=340, BaseDef=150, BaseSpeed=90 },
            new BossDef { Id="b_mingkun", DisplayName="深渊鲸落·溟鲲", Element=Element.Water, Role=RoleType.Guard,  BaseHp=3500, BaseAtk=330, BaseDef=150, BaseSpeed=85 },
            new BossDef { Id="b_shuangying", DisplayName="冰渊镜魔·霜影", Element=Element.Water, Role=RoleType.Swift, BaseHp=2700, BaseAtk=360, BaseDef=120, BaseSpeed=115 },
            // 幕五 · 终局
            new BossDef { Id="b_guixu",  DisplayName="归墟之主",       Element=Element.Earth, Role=RoleType.Guard,  BaseHp=4200, BaseAtk=460, BaseDef=180, BaseSpeed=100 },
            new BossDef { Id="b_hongmeng", DisplayName="混沌之母·鸿蒙", Element=Element.None,  Role=RoleType.Striker, BaseHp=4000, BaseAtk=440, BaseDef=170, BaseSpeed=105 },
        };

        /// <summary>每幕专属池（下标 = 幕-1）。</summary>
        public static readonly string[][] Pool =
        {
            new[]{ "b_fuman", "b_manman", "b_wangliang" },
            new[]{ "b_chiba", "b_jinjiao", "b_jiaozhi" },
            new[]{ "b_bairen", "b_shai", "b_shuangfeng" },
            new[]{ "b_xuanming", "b_mingkun", "b_shuangying" },
            new[]{ "b_guixu", "b_hongmeng" },
        };

        public static IReadOnlyList<BossDef> All => AllBosses;

        public static bool IsBoss(string id) => id != null && id.StartsWith("b_");

        /// <summary>
        /// 白魍双子的落位：**格 1 = 后排中**（中宫格 4 的正后方）。
        ///
        /// ⛔ 格号行语义（别搞反）：`row = Pos.Index / 3`，**row0 = 后排（远离玩家）、
        ///   row2 = 前排（贴近玩家）**（见 BattleStage2D 的 `sortingOrder = Pos.Index / 3; // row 0=后 1=中 2=前`）。
        ///   ⇒ 后排中的格号是 **1**；**格 7 是前排中**（离玩家最近），别把它当后排。
        /// ⛔ 唯一口径：BossCatalog 挂钩子、BossSelfTest 断言都读这个常量，别各写一个数字。
        /// </summary>
        public const int TwinCell = 1;

        /// <summary>
        /// **十字格**（中宫格 4 的上下左右 = 1 / 7 / 3 / 5）。
        /// ★ 玄溟「冰晶重生」的 4 枚冰晶落位 —— 围住中宫，玩家必须用 AOE 才清得干净。
        /// ⚠ 格号行语义见 <see cref="TwinCell"/> 的注释（row0=后排）。
        /// </summary>
        public static readonly int[] CrossCells = { 1, 7, 3, 5 };

        /// <summary>
        /// 白魍双子的**单位 id**（= 立绘 id）。唯一口径：<see cref="SuBai"/> 原型、表现层放大集合、
        /// 布阵（BossSquadFor 预放第二只）都读这个常量，别再各写一份字符串。
        /// </summary>
        public const string TwinBeastId = "b_suren";

        /// <summary>
        /// 该首领的**双子搭档**原型（与本体一起出场的第二只 Boss）；没有搭档返回 null。
        /// ★ 用户 2026-10-03 口径："他们两个都是 Boss 啊，不是召唤出来的，他们是一起出现的"
        ///   ⇒ 双子由布阵阶段（BossSquadFor）直接放进敌方阵容，
        ///     这样**编队预览也能看到两只**（此前只在开场钩子里部署 ⇒ 预览只有 1 只，用户报障）。
        /// </summary>
        public static BeastDef TwinFor(string bossId) => bossId == "b_bairen" ? SuBai : null;

        public static BossDef Get(string id)
        {
            for (int i = 0; i < AllBosses.Length; i++)
                if (AllBosses[i].Id == id) return AllBosses[i];
            return null;
        }

        /// <summary>
        /// 轮回解锁：<paramref name="ascension"/> 低于这个数时，每幕**只开池子前 2 个**首领，
        /// 第 3 个留到更高轮回才出现（12 只 → 先见 8 只）。
        /// ⚠ 幕五只有 2 个，本来就 ≤ 2，不受影响。
        /// </summary>
        public const int UnlockAscension = 2;

        /// <summary>
        /// 该幕在当前轮回下**实际可抽**的首领数（低轮回收窄内容，避免第一轮就见完）。
        /// <paramref name="ascension"/>：当前轮回数（局内 Ascension）；&lt;2 = 只开前 2 个。
        /// </summary>
        public static int UnlockedCount(int act, int ascension)
        {
            int a = CoreMath.Max(1, CoreMath.Min(Pool.Length, act)) - 1;
            var pool = Pool[a];
            if (pool == null) return 0;
            return ascension < UnlockAscension ? CoreMath.Min(2, pool.Length) : pool.Length;
        }

        /// <summary>
        /// 该幕在当前轮回下可抽的首领 id 集合（顺序 = 池序，稳定）。
        /// 自检据此断言「低轮回抽出 ⊆ 高轮回抽出」。
        /// </summary>
        public static string[] UnlockedPool(int act, int ascension)
        {
            int a = CoreMath.Max(1, CoreMath.Min(Pool.Length, act)) - 1;
            var pool = Pool[a];
            if (pool == null) return new string[0];
            int n = UnlockedCount(act, ascension);
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = pool[i];
            return result;
        }

        /// <summary>
        /// 确定性抽首领：同局（同 seed）同幕固定，换局变化。
        /// <paramref name="ascension"/>：当前轮回数 —— 低于 <see cref="UnlockAscension"/> 时
        /// 只在池子前 2 个里摇（内容随轮回逐步放出）。默认 0 = 低轮回口径；
        /// 要显式"全开"请传 <see cref="UnlockAscension"/> 或更大。
        /// </summary>
        public static string BossFor(int act, ulong seed, int ascension = 0)
        {
            int a = CoreMath.Max(1, CoreMath.Min(Pool.Length, act)) - 1;
            var pool = Pool[a];
            if (pool == null || pool.Length == 0) return null;
            int n = UnlockedCount(act, ascension);
            if (n <= 0) return null;
            var rng = new DeterministicRandom(CoreMath.Fnv1a("boss:" + seed.ToString() + ":" + act));
            return pool[rng.NextInt(0, n)];
        }

        /// <summary>抽到的首领直接构造成可上阵的 BeastDef。</summary>
        public static BeastDef BossForBeastDef(int act, ulong seed, int ascension = 0)
        {
            var id = BossFor(act, seed, ascension);
            return id == null ? null : BuildBoss(id);
        }

        /// <summary>
        /// 给 SeededEnemyProvider 用的 bossForAct 工厂（捕获本局种子，保证同局确定性）。
        /// <paramref name="ascension"/>：当前轮回数（决定池子开几个）。
        /// </summary>
        public static Func<int, BeastDef> BossForActFunc(ulong seed, int ascension = 0)
            => a => BossForBeastDef(a, seed, ascension);

        public static BeastDef BuildBoss(string id)
        {
            var def = Get(id);
            return def == null ? null : def.ToBeastDef();
        }

        // ====================================================================
        //  钩子装配：按 boss id 把机制积木拼起来
        // ====================================================================

        /// <summary>扫描敌方阵容里所有 b_ 单位，逐个挂钩子（BattleFactory.Create 部署后调用）。</summary>
        public static void AttachAllBossHooks(BattleState st)
        {
            if (st == null) return;
            var enemies = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < enemies.Count; i++)
            {
                var u = enemies[i];
                if (u != null && u.Def != null && IsBoss(u.Def.Id))
                    AttachHooks(st, u);
            }
        }

        /// <summary>给某个首领单位挂上它的机制钩子。新增首领只改这里。</summary>
        public static void AttachHooks(BattleState st, BattleUnit boss)
        {
            if (boss == null || boss.Def == null) return;
            switch (boss.Def.Id)
            {
                // ---- 幕一 · 木 ----
                case "b_fuman":   // 蝮魇：蔓生(召唤) + 生机(召唤物增伤略) + 枯荣(半血吞召唤物回血)
                    st.Hooks.Add(new SummonHook(2, new[]{ TengNu, TengNu }, new[]{ 3, 5 }));
                    st.Hooks.Add(new PhaseHook(boss, 0.50f, (s, b) =>
                    {
                        var foes = s.UnitsOf(TeamSide.Enemy);
                        int consumed = 0;
                        for (int i = 0; i < foes.Count; i++)
                        {
                            var u = foes[i];
                            if (u != null && u != b && u.IsAlive && u.Def != null &&
                                u.Def.Id != null && u.Def.Id.StartsWith("summon_"))
                            { u.SetHp(0); consumed++; }
                        }
                        if (consumed > 0)
                        {
                            int h = b.Heal(CoreMath.RoundDamage(b.MaxHp * 0.08f * consumed));
                            if (h > 0) s.Log.Add(s.Turn, BattleEventKind.RoundResolve,
                                note: $"{b.DisplayName} 枯荣：吞噬 {consumed} 召唤物回血 {h}");
                        }
                    }));
                    break;

                case "b_manman":  // 蔓娘：荆棘反伤 25% + 汲养 50%
                    st.Hooks.Add(new DamageReflectHook(0.25f));
                    st.Hooks.Add(new LifestealHook(boss, 0.50f));
                    break;

                case "b_wangliang": // 魍魉：隐遁（首击 -60%）；瘴气光环(P5 加状态，暂以减速近似略)
                    st.Hooks.Add(new FirstHitReduceHook(0.60f));
                    break;

                // ---- 幕二 · 火 ----
                case "b_chiba":   // 赤魃：烈焰分身(死亡召唤) + 火种同源(共享生命)
                    st.Hooks.Add(new DeathSummonHook(boss, FenShen, 2));
                    st.Hooks.Add(new SharedLifeHook(boss, 0.50f));
                    break;

                case "b_jinjiao":  // 烬蛟：灼烧叠层(命中附烧) + 焚身(≤40%攻翻倍→用成长近似) + 余烬(击杀成长)
                    st.Hooks.Add(new BurnOnHitHook(boss));
                    st.Hooks.Add(new DevourGrowthHook(boss, 0.02f, 0.02f));
                    break;

                case "b_jiaozhi":  // 燋彘：硬壳+破壳窗口(20%/+50%) + 熔核(死亡召唤幼体)
                    st.Hooks.Add(new CrustHook(boss, 0.20f, 0.50f));
                    st.Hooks.Add(new DeathSummonHook(boss, RongYing, 2));
                    break;

                // ---- 幕三 · 金 ----
                case "b_bairen":   // 白魍双子：**两只一起出场**，各自独立 Boss 面板与技能
                    // ★ 机制（用户 2026-10-03 明确口径）：
                    //   · 白刃(b_bairen) 与素刃(b_suren) **开场同时在场**（不是"召唤物"）；
                    //   · 两只都是 Boss 级（身量与本体同级放大，见 BattleStage2D.BossScaleIds）；
                    //   · 其中一只倒下后，若 **2 回合内**未能击杀另一只 ⇒ 倒下的那只以 50% 血复活；
                    //     双杀（窗口内两只都死）⇒ 真正陨落，战斗结束；
                    //   · 另有共鸣分摊 30%（伤害在两只之间摊）。
                    // 站位：白刃在中宫(4)，素刃落 格1（**后排中** = 中宫正后方，用户口径
                    //   "一个站中间就行，另外一个随便" → 放后排不至于挡住本体）。
                    //   ⛔ 别用格7：那是前排（贴玩家），会被前排随从遮挡。
                    st.Hooks.Add(new TwinSpawnHook(boss, SuBai, TwinCell, 0.30f, 0.50f, 2));
                    break;

                case "b_shai":     // 铩：机关护盾+破壳窗口(25%/+40%) + 组装(护盾未破攻+15%)
                    st.Hooks.Add(new CrustHook(boss, 0.25f, 0.40f));
                    st.Hooks.Add(new AssembleHook(boss, 0.15f));
                    break;

                case "b_shuangfeng": // 霜锋：剜心(每2回合斩最低血+30%) + 残刃(每25%掉血全屏斩，近似为阶段AOE)
                    st.Hooks.Add(new LowestHpNukeHook(boss, 2, 1.30f));
                    st.Hooks.Add(new PhaseHook(boss, 0.75f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    st.Hooks.Add(new PhaseHook(boss, 0.50f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    st.Hooks.Add(new PhaseHook(boss, 0.25f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    break;

                // ---- 幕四 · 水 ----
                case "b_xuanming": // 玄溟：寒狱镜面(反弹30%) + 冰晶重生
                    //   冰晶重生（用户 2026-10-03 定案，**替代旧的碎冰/冰核假死**）：
                    //   本体被打死 ⇒ 退场，同时在十字格(1/7/3/5)生成 4 枚冰晶（每枚 = 玄溟满血 ×25%）；
                    //   玩家须在 3 回合内打光 ⇒ 真死；超时则按 50% → 25% → 0% 依次复活（第 3 次陨落）。
                    //   ⇒ 4 枚散在中宫四周，**逼迫玩家拿出 AOE**，单体爆发清不干净。
                    st.Hooks.Add(new DamageReflectHook(0.30f));
                    st.Hooks.Add(new IceCrystalRebirthHook(boss, IceCrystal, CrossCells,
                                                          3, new float[] { 0.5f, 0.25f, 0f }, 0.25f));
                    break;

                case "b_mingkun":  // 溟鲲：潮汐(-2灵力/回合) + 吞舟(每2回合斩最高血) + 鲸落(死亡AOE30%)
                    st.Hooks.Add(new MpDrainHook(2));
                    st.Hooks.Add(new LowestHpNukeHook(boss, 2, 1.35f));
                    st.Hooks.Add(new DeathAoeHook(boss, 0.30f));
                    break;

                case "b_shuangying": // 霜影：镜像分身(召唤) + 寒渊(我方-20%速度)
                    st.Hooks.Add(new SummonHook(3, new[]{ JingYing, JingYing }, new[]{ 2, 6 }));
                    st.Hooks.Add(new SpeedAuraHook(0.20f));
                    break;

                // ---- 幕五 · 终局 ----
                case "b_guixu":    // 归墟之主：五行轮转 + 吞噬成长 + 终焉之卵(30%召3卵+随从回血) + 硬性DPS(12回合)
                    st.Hooks.Add(new AttributeRotateHook(boss,
                        new[]{ Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water }, 3));
                    st.Hooks.Add(new DevourGrowthHook(boss, 0.04f, 0.03f));
                    st.Hooks.Add(new PhaseHook(boss, 0.30f, (s, b) =>
                    {
                        SummonHook.DeploySummon(s, ShouHu, 2);
                        SummonHook.DeploySummon(s, ShouHu, 3);
                        SummonHook.DeploySummon(s, ShouHu, 5);
                    }));
                    st.Hooks.Add(new AllyPresenceRegenHook(boss, 0.05f));
                    st.Hooks.Add(new DpsTimeoutHook(12));
                    break;

                case "b_hongmeng": // 鸿蒙：混元(免疫克制) + 造化(召唤) + 混沌护持(首击-40%) + 硬性DPS(15回合)
                    st.Hooks.Add(new IgnoreCounterHook(boss));
                    st.Hooks.Add(new SummonHook(3, new[]{ ZaoHua }, new[]{ 2 }));
                    st.Hooks.Add(new FirstHitReduceHook(0.40f));
                    st.Hooks.Add(new DpsTimeoutHook(15));
                    break;
            }
        }

        /// <summary>霜锋残刃：对全体我方造成 power×boss攻击 的伤害。</summary>
        private static void AoeNuke(BattleState st, BattleUnit boss, float power)
        {
            if (boss == null || !boss.IsAlive) return;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int d = BattleSimulator.ComputeDamage(st, boss, u, boss.Element, power, false, false);
                int dealt = u.TakeDamage(d);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: boss.RuntimeId, targetId: u.RuntimeId,
                           amount: d, element: boss.Element, note: $"{boss.DisplayName} 残刃");
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 死于残刃");
            }
        }
    }
}
