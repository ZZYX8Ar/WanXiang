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

        /// <summary>战技 / 绝技附带的专属状态 id（<c>StatusCatalog</c>；空 = 纯伤害）。见 <see cref="BossSkills.Default(Element,string)"/>。</summary>
        public string FlavorStatus;

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
            var sk = BossSkills.Default(Element, FlavorStatus);
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
        public static SkillDef[] Default(Element element) { return Default(element, null); }

        /// <param name="flavor">战技/绝技附带的状态 id（<c>StatusCatalog</c>；空 = 纯伤害）。
        /// ★ 2026-10-06：用户"首领只会普攻、三招看起来一模一样"⇒ 让**每只首领的战技/绝技带上专属状态**
        ///   （木=根缚、火=灼烧、金=裂甲、水=侵蚀/冰霜、鸿蒙=沉默 …），这样打出来**一眼能看出是谁**。</param>
        public static SkillDef[] Default(Element element, string flavor)
        {
            var basics = new[] { EffectAtom.Damage(TargetSelector.AllEnemies, 0.40f) };
            var act = new System.Collections.Generic.List<EffectAtom>
                { EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f) };
            var ult = new System.Collections.Generic.List<EffectAtom>
                { EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f) };
            string actDesc = "全体攻击", ultDesc = "全体重击";

            if (!string.IsNullOrEmpty(flavor))
            {
                string sn = StatusCatalog.Get(flavor).Name;
                if (string.IsNullOrEmpty(sn)) sn = flavor;
                // ⛔⛔ **专属状态只挂「绝技」**，战技保持纯全体伤害。
                //   实测证据（2026-10-06，逐只首领 24 局 × 两档遗物）：
                //     · 战技**几乎每回合都能放**（`UseCooldown = false` ⇒ 填的 Cd=2 根本不生效，
                //       只有 EnemyMp 在限速：+2/回合、战技要 3）；
                //     · 于是任何 ≥2 回合的状态都被**永久覆盖** ⇒ 全队常驻根缚/灼烧/易伤。
                //     · 铁证：把玩家成长倍率从 1.60 拉到 2.20（巨幅加强）胜率**纹丝不动仍是 0%**
                //       —— 机制不可解，加数值救不回来。
                //   绝技受"元气满"门槛限制（`UltimateNeedsRage` + `RageMax=100`）⇒ 天然稀有，
                //   这才是"招牌技能"该出现的位置：不常来，一来就是大事。
                ult.Add(EffectAtom.Status(TargetSelector.SingleFrontMost, flavor, 2, 3));
                ultDesc = "全体重击 + 单体" + sn + " ×2";
            }

            return new[]
            {
                new SkillDef { Id = "b_basic", Name = "普攻", Type = SkillType.Basic, Cd = 0,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = basics, Description = "全体攻击" },
                new SkillDef { Id = "b_active", Name = "战技", Type = SkillType.Active, Cd = 2,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = act.ToArray(), Description = actDesc },
                new SkillDef { Id = "b_ult", Name = "绝技", Type = SkillType.Ultimate, Cd = 3,
                    Element = element, PrimaryTarget = TargetSelector.AllEnemies,
                    Effects = ult.ToArray(), Description = ultDesc },
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
            new BossDef { Id="b_fuman",  DisplayName="腐木之君·蝮魇", Element=Element.Wood,  Role=RoleType.Guard,    BaseHp=3000, BaseAtk=300, BaseDef=140, BaseSpeed=85,  FlavorStatus=StatusCatalog.Root },
            new BossDef { Id="b_manman", DisplayName="缠丝女萝·蔓娘", Element=Element.Wood,  Role=RoleType.Swift,    BaseHp=2600, BaseAtk=340, BaseDef=110, BaseSpeed=120, FlavorStatus=StatusCatalog.Root },
            new BossDef { Id="b_wangliang", DisplayName="瘴林之影·魍魉", Element=Element.Wood, Role=RoleType.Striker, BaseHp=2400, BaseAtk=360, BaseDef=100, BaseSpeed=135, FlavorStatus=StatusCatalog.Miasma },
            // 幕二 · 夏（火）
            new BossDef { Id="b_chiba",  DisplayName="炎狱之君·赤魃", Element=Element.Fire,  Role=RoleType.Striker,  BaseHp=2800, BaseAtk=380, BaseDef=120, BaseSpeed=105, FlavorStatus=StatusCatalog.Burn },
            new BossDef { Id="b_jinjiao", DisplayName="焚天凶骸·烬蛟", Element=Element.Fire,  Role=RoleType.Striker,  BaseHp=2700, BaseAtk=360, BaseDef=115, BaseSpeed=110, FlavorStatus=StatusCatalog.Burn },
            new BossDef { Id="b_jiaozhi", DisplayName="熔岩行尸·燋彘", Element=Element.Fire,  Role=RoleType.Guard,    BaseHp=3200, BaseAtk=320, BaseDef=160, BaseSpeed=80,  FlavorStatus=StatusCatalog.Burn },
            // 幕三 · 秋（金）
            // ⚠ 白魍的 id 用 **b_bairen**（白刃），不是 b_baiwang ——
            //   设计上"肃杀之君·白魍"就是**双子本身**（白刃 + 素刃），没有第三个"白魍本体"立绘。
            //   b_baiwang 从来不是立绘 CSV 里的 id（那份表只有 b_bairen / b_suren）。
            //   历史坑：曾用 b_baiwang 当 id，SpriteCatalog 查不到图 ⇒ 表现层"按序号随便分配"
            //   ⇒ 最后靠 cp b_bairen.png 补了个副本，结果两张立绘一模一样（用户发现）。
            new BossDef { Id="b_bairen", DisplayName="白魍·白刃", Element=Element.Metal, Role=RoleType.Striker, BaseHp=2600, BaseAtk=380, BaseDef=120, BaseSpeed=110, FlavorStatus=StatusCatalog.ArmorBreak },
            new BossDef { Id="b_shai",   DisplayName="千机傀儡·铩",   Element=Element.Metal, Role=RoleType.Guard,    BaseHp=3300, BaseAtk=340, BaseDef=170, BaseSpeed=80,  FlavorStatus=StatusCatalog.ArmorBreak },
            new BossDef { Id="b_shuangfeng", DisplayName="断刃游侠·霜锋", Element=Element.Metal, Role=RoleType.Swift, BaseHp=2500, BaseAtk=420, BaseDef=110, BaseSpeed=140, FlavorStatus=StatusCatalog.Marked },
            // 幕四 · 冬（水）
            new BossDef { Id="b_xuanming", DisplayName="凝冰之君·玄溟", Element=Element.Water, Role=RoleType.Guard,  BaseHp=3400, BaseAtk=340, BaseDef=150, BaseSpeed=90,  FlavorStatus=StatusCatalog.Freeze },
            new BossDef { Id="b_mingkun", DisplayName="深渊鲸落·溟鲲", Element=Element.Water, Role=RoleType.Guard,  BaseHp=3500, BaseAtk=330, BaseDef=150, BaseSpeed=85,  FlavorStatus=StatusCatalog.Wet },
            new BossDef { Id="b_shuangying", DisplayName="冰渊镜魔·霜影", Element=Element.Water, Role=RoleType.Swift, BaseHp=2700, BaseAtk=360, BaseDef=120, BaseSpeed=115, FlavorStatus=StatusCatalog.Frost },
            // 幕五 · 终局
            new BossDef { Id="b_guixu",  DisplayName="归墟之主",       Element=Element.Earth, Role=RoleType.Guard,  BaseHp=4200, BaseAtk=460, BaseDef=180, BaseSpeed=100, FlavorStatus=StatusCatalog.ArmorBreak },
            new BossDef { Id="b_hongmeng", DisplayName="混沌之母·鸿蒙", Element=Element.None,  Role=RoleType.Striker, BaseHp=4000, BaseAtk=440, BaseDef=170, BaseSpeed=105, FlavorStatus=StatusCatalog.Silence },
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
                // ---- 幕一 · 木（2026-10-06 按《首领战设计文档 v1.0》补齐：生机 / 根缚 / 缠丝 / 瘴气 / 瘴爆 / 散瘴）----

                case "b_fuman":   // 蝮魇：蔓生 + 生机 + 根缚 + 枯荣（教学首领）
                    st.Hooks.Add(new BossOpeningMpHook(3));       // 开局给灵力，否则前两回合只能普攻
                    st.Hooks.Add(new SummonHook(2, new[]{ TengNu, TengNu }, new[]{ 3, 5 }));
                    // 生机（被动）：场上每存活 1 只召唤物，伤害 +6%（可叠）⇒ 清场 = 直接削弱它
                    st.Hooks.Add(new OutgoingDamageBonusHook(boss,
                        (s, d) => 0.04f * CountSummons(s, boss)));
                    st.Hooks.Add(new RootHook(4));                // 根缚：每 3 回合缚住 1 人 1 回合
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

                case "b_manman":  // 蔓娘：荆棘 25% + 缠丝（连击加码）+ 汲养 50% + 根缚
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    // 荆棘 + 缠丝二合一：基础反弹 25%，被**同一单位**连续攻击时第 2 次起 +15%/次
                    st.Hooks.Add(new ThornStreakHook(0.15f, 0.08f));
                    st.Hooks.Add(new LifestealHook(boss, 0.50f));
                    st.Hooks.Add(new RootHook(3));                // 根缚：每 2 回合
                    break;

                case "b_wangliang": // 魍魉：瘴气 + 隐遁 + 瘴爆 + 散瘴（拖越久你越弱）
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new MiasmaHook(1));               // 每回合末全体 +1 层瘴气（每层攻 −3%）
                    st.Hooks.Add(new FirstHitReduceHook(0.60f));   // 隐遁
                    st.Hooks.Add(new ScatterMiasmaHook(boss, 3));  // 散瘴：清自身负面 + 瘴气翻倍
                    st.Hooks.Add(new MiasmaBurstHook(boss, 0.02f));// 瘴爆：死亡时按层数 ×4% 最大生命
                    break;

                // ---- 幕二 · 火 ----

                case "b_chiba":   // 赤魃：燎原 + 焚身爆裂 + 分阶段分身 + 火种同源
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new SummonHook(4, new[]{ FenShen }, new[]{ 2 }));
                    // 燎原（被动）：场上每 1 层灼烧，伤害 +3%
                    st.Hooks.Add(new OutgoingDamageBonusHook(boss,
                        (s, d) => 0.03f * TotalPlayerStacks(s, StatusCatalog.Burn)));
                    st.Hooks.Add(new SummonDeathBurstHook(boss, 0.12f));   // 分身崩解 → 全体 25% 最大生命
                    st.Hooks.Add(new PhaseHook(boss, 0.75f, (s, b) => DeployToFreeCell(s, b.Side, FenShen)));
                    st.Hooks.Add(new PhaseHook(boss, 0.50f, (s, b) => DeployToFreeCell(s, b.Side, FenShen)));
                    st.Hooks.Add(new PhaseHook(boss, 0.25f, (s, b) => DeployToFreeCell(s, b.Side, FenShen)));
                    st.Hooks.Add(new SharedLifeHook(boss, 0.50f));          // 火种同源
                    break;

                case "b_jinjiao":  // 烬蛟：灼烧叠层 + 引燃 + 余烬 + 焚身（≤40% 叠层翻倍）
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new BurnOnHitPhaseHook(boss, 0.40f, 0.02f));
                    st.Hooks.Add(new DevourGrowthHook(boss, 0.02f, 0.02f));   // 余烬（近似）
                    st.Hooks.Add(new BurnDetonateHook(boss, 5, 0.04f));       // 引燃：总层 ≥8 → 引爆
                    break;

                case "b_jiaozhi":  // 燋彘：硬壳 + 破壳窗口 + 岩浆喷发 + 熔核
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new CrustHook(boss, 0.20f, 0.50f));
                    st.Hooks.Add(new PeriodicNukeHook(boss, 3, 0.08f, "岩浆喷发"));
                    st.Hooks.Add(new DeathSummonHook(boss, RongYing, 2));
                    break;

                // ---- 幕三 · 金 ----

                case "b_bairen":   // 白魍双子：同命 + 共鸣 + 连环斩 + 金身
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new TwinSpawnHook(boss, SuBai, TwinCell, 0.30f, 0.50f, 2));
                    st.Hooks.Add(new StreakDamageHook(boss, 0.10f));      // 连环斩：压同一目标越打越痛
                    st.Hooks.Add(new FirstHitReduceHook(0.70f));          // 金身：每回合首次受伤 −70%
                    break;

                case "b_shai":     // 铩：机关护盾 + 齿轮反击 + 组装 + 过载
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new CrustHook(boss, 0.25f, 0.40f));
                    st.Hooks.Add(new ShieldBreakReflectHook(boss, 1.50f));  // 齿轮反击
                    st.Hooks.Add(new AssembleHook(boss, 0.15f));
                    break;

                case "b_shuangfeng": // 霜锋：连环斩 + 剜心 + 残刃
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new StreakDamageHook(boss, 0.10f));      // 连环斩
                    st.Hooks.Add(new LowestHpNukeHook(boss, 2, 1.30f));   // 剜心
                    st.Hooks.Add(new PhaseHook(boss, 0.75f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    st.Hooks.Add(new PhaseHook(boss, 0.50f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    st.Hooks.Add(new PhaseHook(boss, 0.25f, (s, b) => AoeNuke(s, b, 0.30f), false));
                    break;

                // ---- 幕四 · 水 ----

                case "b_xuanming": // 玄溟：寒狱镜面 + 冰封 + 冰晶重生
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new DamageReflectHook(0.30f));
                    st.Hooks.Add(new FreezeStealMpHook(boss, 3, 3));      // 冰封：冻结 + 偷灵力
                    st.Hooks.Add(new IceCrystalRebirthHook(boss, IceCrystal, CrossCells,
                                                          3, new float[] { 0.5f, 0.25f, 0f }, 0.25f));
                    break;

                case "b_mingkun":  // 溟鲲：潮汐 + 吞舟 + 鲸落 + 深潜
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new MpDrainHook(2));
                    st.Hooks.Add(new HighestHpNukeHook(boss, 2, 0.20f));  // 吞舟：生命最高者 35% 当前生命
                    st.Hooks.Add(new DeathAoeHook(boss, 0.30f));
                    st.Hooks.Add(new LastStandHook(boss, 0.35f));         // 深潜：≤35% 每回合额外行动
                    break;

                case "b_shuangying": // 霜影：镜像分身 + 虚实 + 寒渊 + 镜碎
                    st.Hooks.Add(new BossOpeningMpHook(3));
                    st.Hooks.Add(new SummonHook(3, new[]{ JingYing, JingYing }, new[]{ 2, 6 }));
                    st.Hooks.Add(new RandomImmunityHook());               // 虚实：每回合 1 个单位免疫
                    st.Hooks.Add(new SpeedAuraHook(0.20f));
                    st.Hooks.Add(new CloneDamageShareHook(boss, 0.30f));  // 镜碎
                    break;

                // ---- 幕五 · 终局 ----

                case "b_guixu":    // 归墟之主：五行轮转 + 吞噬 + 终焉之卵 + 硬性DPS
                    st.Hooks.Add(new BossOpeningMpHook(4));
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

                case "b_hongmeng": // 鸿蒙：混元 + 造化 + 混沌护持 + 湮灭
                    st.Hooks.Add(new BossOpeningMpHook(4));
                    st.Hooks.Add(new IgnoreCounterHook(boss));
                    st.Hooks.Add(new SummonHook(3, new[]{ ZaoHua }, new[]{ 2 }));
                    st.Hooks.Add(new FirstHitReduceHook(0.40f));
                    st.Hooks.Add(new DpsTimeoutHook(15));
                    break;
            }
        }


        /// <summary>场上存活的召唤物 / 分身数量（id 以 summon_ 开头，不含首领自己）。生机用。</summary>
        private static int CountSummons(BattleState st, BattleUnit boss)
        {
            int n = 0;
            var list = st.UnitsOf(boss.Side);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u != null && u != boss && u.IsAlive && u.Def != null && u.Def.Id != null &&
                    u.Def.Id.StartsWith("summon_")) n++;
            }
            return n;
        }

        /// <summary>我方全体身上某状态的层数总和（燎原 / 引燃的"全场灼烧总层数"口径）。</summary>
        private static int TotalPlayerStacks(BattleState st, string statusId)
        {
            int n = 0;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) n += list[i].GetStacks(statusId);
            return n;
        }

        /// <summary>把 proto 部署到该侧第一个空格（阶段分裂 / 增援用）。返回是否成功。</summary>
        private static bool DeployToFreeCell(BattleState st, TeamSide side, BeastDef proto)
        {
            if (proto == null) return false;
            for (int c = 0; c < BoardLayout.CellCount; c++)
            {
                if (st.SlotAt(side, c) != null) continue;
                SummonHook.DeploySummon(st, proto, c);
                return st.SlotAt(side, c) != null;
            }
            return false;
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
