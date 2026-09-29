// ============================================================================
//  万相 · 遗物系统（肉鸽式「局内变强」载体）
//  ---------------------------------------------------------------------------
//  设计缘起（用户 2026-09-29 实测反馈）：
//    局内零成长 —— 我方养成在出征前快照冻结，一局之内无论打多少节点属性都不再涨；
//    敌人又只随幕跳变（幕内恒定）。于是「没变强感 / 二幕打不过 / 5v5 先死即败」。
//  遗物就是**局内可积累的被动增益**：每场胜利随机 3 选 1、遗物节点三选一，
//  效果全部映射到已有战斗管线，**不引入战斗核心改动**。
//
//  v1.1 扩充（用户 2026-09-29 追加：「种类太单一了」）：
//    效果类型 4 → 12 种，覆盖 全局 / 专属（五行·定位·异兽）/ 敌方弱化 / 双面代价 / 累计成长 / 限时。
//    全部仍只折叠进 BattleRequest 的**已有字段**：
//      · 全局 +V%            → req.PlayerMul
//      · 五行/定位/异兽 +V%  → req.PlayerMulPer[i]（逐单位倍率；按该兽的五行/定位/id 命中）
//      · 敌方全属性 -V%      → 每个 EnemyEntries[i].StatMul
//      · 敌方指定五行 -V%    → 命中该五行的 EnemyEntries[i].StatMul
//      · 复活 1 次           → req.PlayerReviveOn（复用「复苏」虫卵机制）
//      · 开局灵力 +V         → req.PlayerStartMana
//      · 双面「代价」        → 扣 PlayerStartMana / 关掉 PlayerReviveOn
//      · 累计胜利 / 限时     → 读 run.Wins（本局胜利数）
//    一件遗物 = 一条定义（Id/Name/Desc/Rarity/Effect/Value/Value2/ScopeId），新增只改 Build()。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    /// <summary>遗物稀有度（决定掉落权重）。</summary>
    public enum RelicRarity
    {
        Common = 0,
        Rare = 1,
        Boss = 2,
    }

    /// <summary>遗物效果类型（决定挂到战斗管线的哪一处）。</summary>
    public enum RelicEffect
    {
        None = 0,               // 无数值效果（纯机制遗物，只靠 Mechanic 生效）

        // ---- 全局 ----
        PlayerStatPct,          // 全队 生命/攻击/防御 +Value%
        EnemyWeakPct,           // 敌方全属性 -Value%
        ReviveOnce,             // 我方全体阵亡以 Value% 生命复活 1 次
        StartMana,              // 战斗开局灵力 +Value

        // ---- 专属（作用域 = ScopeId）----
        ElementStatPct,         // 指定五行的我方 +Value%（ScopeId = "Wood"/"Fire"/…）
        RoleStatPct,            // 指定定位的我方 +Value%（ScopeId = "Guard"/"Striker"/…）
        BeastStatPct,           // 指定异兽 +Value%（ScopeId = 异兽 id，如 "jumang"）
        EnemyElementWeakPct,    // 指定五行的敌方 -Value%（ScopeId = "Wood"/…）

        // ---- 条件 / 双面 / 累计 ----
        TeamSizeStatPct,        // 上阵人数 ≤ Value2 时，全队 +Value%（众寡术）
        TradeStatPct,           // 双面：全队 +Value%，代价是战斗开局灵力 -Value2
        NoReviveStatPct,        // 双面：全队 +Value%，代价是【不可复活】
        PerWinStatPct,          // 累计：本局每胜利 1 场 +Value%，封顶 Value2%
        EarlyWinStatPct,        // 限时：本局前 Value2 场战斗内 +Value%（之后失效）
    }

    /// <summary>
    /// 遗物机制（v1.2）：**改变行为**而不是加数值。
    /// 作用于 ScopeId 指定的异兽（ScopeId 为空 = 全队所有兽）。
    /// 技能改写靠「克隆兽」—— req.Player 里的 BeastDef 是深拷贝，改它不会污染内容表（已实测）。
    /// </summary>
    public enum RelicMechanic
    {
        None = 0,

        // ---- 技能改写 ----
        BasicHitsAll,      // 普攻改为攻击【全体敌人】
        BasicExtraHit,     // 普攻段数 +1（Hits 2）
        BasicBurn,         // 普攻额外附带【灼烧】
        BasicPierce,       // 普攻无视护盾
        BasicTrue,         // 普攻改为【真实伤害】（无视护盾与减伤）
        ActiveHealAll,     // 战记改为【治疗全体我方】
        UltimatePierce,    // 奥义无视护盾

        // ---- 授予劫象（BattleTraits）----
        GrantTenacity,
        GrantSharpedge,
        GrantSwiftshadow,
        GrantThickwall,
        GrantDevour,

        // ---- 规则改写（全场）----
        TurnsPlus,         // 回合上限 +Value
        TurnsMinus,        // 回合上限 -Value（高风险高回报）
        UltimateNoRage,    // 奥义不需要元气
        NoHitChance,       // 关闭命中率（全队必中）
        EnemyCautious,     // 敌方 AI 变保守
    }

    /// <summary>单条遗物定义（纯数据）。</summary>
    public sealed class RelicDef
    {
        public string Id;
        public string Name;
        public string Desc;
        public RelicRarity Rarity;
        public RelicEffect Effect;
        public float Value;        // 主数值（百分数或点数）
        public float Value2;       // 副数值：代价点数 / 条件阈值 / 封顶值
        public string ScopeId;     // 作用域：五行名 / 定位名 / 异兽 id
        public RelicMechanic Mechanic;   // 行为改变（None = 纯数值遗物）

        public RelicDef(string id, string name, string desc, RelicRarity r, RelicEffect e,
                        float v, float v2 = 0f, string scope = null,
                        RelicMechanic mech = RelicMechanic.None)
        {
            Id = id; Name = name; Desc = desc; Rarity = r; Effect = e;
            Value = v; Value2 = v2; ScopeId = scope; Mechanic = mech;
        }
    }

    /// <summary>遗物折叠后的中立增益包（不依赖任何 UI / BattleRequest 类型）。</summary>
    public sealed class RelicMods
    {
        public float PlayerMul = 1f;           // 全队倍率 → req.PlayerMul
        public float EnemyMul = 1f;            // 敌方倍率 → EnemyEntries[i].StatMul
        public bool ReviveOn;                  // → req.PlayerReviveOn
        public bool ReviveForbidden;           // 双面代价：强制不可复活
        public float ReviveHpPercent = 0.3f;
        public int StartMana;                  // → req.PlayerStartMana（可为负 = 代价，最终下限 0）

        public readonly Dictionary<Element, float> ElemMul = new Dictionary<Element, float>();
        public readonly Dictionary<RoleType, float> RoleMul = new Dictionary<RoleType, float>();
        public readonly Dictionary<string, float> BeastMul = new Dictionary<string, float>();
        public readonly Dictionary<Element, float> EnemyElemWeak = new Dictionary<Element, float>();

        public float MulFor(Element e) { float v; return ElemMul.TryGetValue(e, out v) ? v : 1f; }
        public float RoleFor(RoleType r) { float v; return RoleMul.TryGetValue(r, out v) ? v : 1f; }
        public float BeastFor(string id)
        { float v; return !string.IsNullOrEmpty(id) && BeastMul.TryGetValue(id, out v) ? v : 1f; }
        public float WeakFor(Element e) { float v; return EnemyElemWeak.TryGetValue(e, out v) ? v : 1f; }

        public void AddElem(Element e, float m)
        { float v; ElemMul.TryGetValue(e, out v); if (v <= 0f) v = 1f; ElemMul[e] = v * m; }

        public void AddRole(RoleType r, float m)
        { float v; RoleMul.TryGetValue(r, out v); if (v <= 0f) v = 1f; RoleMul[r] = v * m; }

        public void AddBeast(string id, float m)
        {
            if (string.IsNullOrEmpty(id)) return;
            float v; BeastMul.TryGetValue(id, out v); if (v <= 0f) v = 1f; BeastMul[id] = v * m;
        }

        public void AddWeak(Element e, float m)
        { float v; EnemyElemWeak.TryGetValue(e, out v); if (v <= 0f) v = 1f; EnemyElemWeak[e] = v * m; }
    }

    public static class RelicCatalog
    {
        /// <summary>全部遗物（内容侧唯一来源；新增只改 Build()）。</summary>
        public static readonly RelicDef[] All;

        private static readonly Dictionary<string, RelicDef> _byId = new Dictionary<string, RelicDef>();

        static RelicCatalog()
        {
            // ⚠ 必须在静态构造函数里 Build：下面的 Elements/Roles/… 是**内联**静态数组，
            //   若写成 `All = Build()` 的字段初始化器，它会按声明顺序先于那些数组执行
            //   ⇒ Build() 里读到 null 数组 ⇒ TypeInitializationException（实测踩过）。
            All = Build();
            foreach (var d in All) _byId[d.Id] = d;
        }

        public static RelicDef Get(string id)
            => string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var d) ? null : d;

        // ==================================================================
        //  构建：基础 + 分族（改数值 / 加族只动这里）
        // ==================================================================
        private static readonly Element[] Elements = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };
        private static readonly string[] ElemGlyph = { "青木", "赤焰", "厚土", "素金", "玄水" };
        private static readonly RoleType[] Roles = { RoleType.Guard, RoleType.Striker, RoleType.Swift, RoleType.Caster, RoleType.Support };
        private static readonly string[] RoleCn = { "御", "攻", "疾", "术", "辅" };
        // 全部 30 只异兽（每只都有专属遗物）。{ id, 中文名, 五行, 定位 }
        private static readonly string[][] Beasts = {
            new[]{ "jumang","句芒","Wood","Support" },   new[]{ "jiuweihu","九尾狐","Wood","Striker" },
            new[]{ "guanguan","灌灌","Wood","Support" }, new[]{ "migu","迷榖","Wood","Guard" },
            new[]{ "lushu","鹿蜀","Wood","Support" },     new[]{ "xingxing","狌狌","Wood","Swift" },
            new[]{ "zhurong","祝融","Fire","Caster" },    new[]{ "zhulong","烛龙","Fire","Striker" },
            new[]{ "bifang","毕方","Fire","Caster" },     new[]{ "feiyi","肥遗","Fire","Striker" },
            new[]{ "hanba","旱魃","Fire","Caster" },      new[]{ "chongming","重明鸟","Fire","Support" },
            new[]{ "houtu","后土","Earth","Guard" },      new[]{ "dijiang","帝江","Earth","Support" },
            new[]{ "taotie","饕餮","Earth","Striker" },   new[]{ "hundun","混沌","Earth","Caster" },
            new[]{ "dangkang","当康","Earth","Support" }, new[]{ "luwu","陆吾","Earth","Guard" },
            new[]{ "rushou","蓐收","Metal","Striker" },   new[]{ "qiongqi","穷奇","Metal","Striker" },
            new[]{ "zheng","狰","Metal","Swift" },        new[]{ "tiangou","天狗","Metal","Guard" },
            new[]{ "qiuyu","犰狳","Metal","Guard" },      new[]{ "baize","白泽","Metal","Support" },
            new[]{ "yuqiang","禺强","Water","Caster" },   new[]{ "yinglong","应龙","Water","Striker" },
            new[]{ "xiangliu","相柳","Water","Striker" }, new[]{ "bashe","巴蛇","Water","Swift" },
            new[]{ "fuzhu","夫诸","Water","Caster" },     new[]{ "xuangui","旋龟","Water","Support" },
        };

        // 按定位轮换的机制（每只异兽的专属印记机制）
        private static readonly RelicMechanic[] MechGuard = { RelicMechanic.GrantThickwall, RelicMechanic.GrantTenacity, RelicMechanic.GrantDevour };
        private static readonly RelicMechanic[] MechStriker = { RelicMechanic.BasicPierce, RelicMechanic.BasicExtraHit, RelicMechanic.BasicTrue };
        private static readonly RelicMechanic[] MechCaster = { RelicMechanic.BasicBurn, RelicMechanic.UltimatePierce, RelicMechanic.BasicHitsAll };
        private static readonly RelicMechanic[] MechSupport = { RelicMechanic.ActiveHealAll, RelicMechanic.BasicHitsAll, RelicMechanic.GrantTenacity };
        private static readonly RelicMechanic[] MechSwift = { RelicMechanic.GrantSwiftshadow, RelicMechanic.BasicExtraHit, RelicMechanic.BasicPierce };

        private static RelicMechanic MechanicFor(string role, int idx)
        {
            RelicMechanic[] pool;
            switch (role)
            {
                case "Guard": pool = MechGuard; break;
                case "Striker": pool = MechStriker; break;
                case "Caster": pool = MechCaster; break;
                case "Support": pool = MechSupport; break;
                default: pool = MechSwift; break;
            }
            return pool[idx % pool.Length];
        }

        private static string MechanicCn(RelicMechanic m)
        {
            switch (m)
            {
                case RelicMechanic.BasicHitsAll: return "群击";
                case RelicMechanic.BasicExtraHit: return "叠击";
                case RelicMechanic.BasicBurn: return "燎原";
                case RelicMechanic.BasicPierce: return "破甲";
                case RelicMechanic.BasicTrue: return "真刃";
                case RelicMechanic.ActiveHealAll: return "春回";
                case RelicMechanic.UltimatePierce: return "贯绝";
                case RelicMechanic.GrantTenacity: return "坚忍";
                case RelicMechanic.GrantSharpedge: return "锋锐";
                case RelicMechanic.GrantSwiftshadow: return "疾踪";
                case RelicMechanic.GrantThickwall: return "厚载";
                case RelicMechanic.GrantDevour: return "吞噬";
                default: return "异术";
            }
        }

        private static string MechanicDesc(RelicMechanic m)
        {
            switch (m)
            {
                case RelicMechanic.BasicHitsAll: return "普攻改为攻击【全体敌人】";
                case RelicMechanic.BasicExtraHit: return "普攻段数 +1";
                case RelicMechanic.BasicBurn: return "普攻额外附带【灼烧】";
                case RelicMechanic.BasicPierce: return "普攻无视护盾";
                case RelicMechanic.BasicTrue: return "普攻改为【真实伤害】（无视护盾与减伤）";
                case RelicMechanic.ActiveHealAll: return "战记改为【治疗全体我方】";
                case RelicMechanic.UltimatePierce: return "奥义无视护盾";
                case RelicMechanic.GrantTenacity: return "获得【坚韧】劫象";
                case RelicMechanic.GrantSharpedge: return "获得【锋锐】劫象";
                case RelicMechanic.GrantSwiftshadow: return "获得【疾影】劫象";
                case RelicMechanic.GrantThickwall: return "获得【厚墙】劫象";
                case RelicMechanic.GrantDevour: return "获得【吞噬】劫象";
                default: return "获得特殊机制";
            }
        }

        // ==================================================================
        //  机制应用（v1.2：改变行为，而不是加数值）
        // ==================================================================

        /// <summary>本局遗物带来的「回合上限」变化（TurnsPlus 加 / TurnsMinus 减）。</summary>
        public static int RuleTurnsDelta(ICollection<string> ownedIds)
        {
            int d = 0;
            if (ownedIds == null) return 0;
            foreach (var id in ownedIds)
            {
                var r = Get(id);
                if (r == null) continue;
                if (r.Mechanic == RelicMechanic.TurnsPlus) d += (int)System.Math.Round(r.Value2);
                else if (r.Mechanic == RelicMechanic.TurnsMinus) d -= (int)System.Math.Round(r.Value2);
            }
            return d;
        }

        /// <summary>本局是否持有某个「全场规则」机制。</summary>
        public static bool HasMechanic(ICollection<string> ownedIds, RelicMechanic m)
        {
            if (ownedIds == null) return false;
            foreach (var id in ownedIds)
            {
                var r = Get(id);
                if (r != null && r.Mechanic == m) return true;
            }
            return false;
        }

        /// <summary>
        /// 把机制作用到一只【克隆兽】上（改它的技能）。返回被授予的劫象 id（无则 null）。
        /// ⚠ 必须传 req.Player 里的克隆体：BeastDef.Clone 是**深拷贝**，改它不会污染内容表（已实测）。
        /// </summary>
        public static string ApplyBeastMechanics(BeastDef b, ICollection<string> ownedIds)
        {
            if (b == null || ownedIds == null) return null;
            string trait = null;
            foreach (var id in ownedIds)
            {
                var d = Get(id);
                if (d == null || d.Mechanic == RelicMechanic.None) continue;
                if (!string.IsNullOrEmpty(d.ScopeId) && d.ScopeId != b.Id) continue;   // 专属：只作用该兽
                switch (d.Mechanic)
                {
                    case RelicMechanic.BasicHitsAll: Retarget(b.Basic, TargetSelector.AllEnemies); break;
                    case RelicMechanic.BasicExtraHit: ExtraHit(b.Basic); break;
                    case RelicMechanic.BasicBurn: AddBurn(b.Basic); break;
                    case RelicMechanic.BasicPierce: Pierce(b.Basic, false); break;
                    case RelicMechanic.BasicTrue: Pierce(b.Basic, true); break;
                    case RelicMechanic.ActiveHealAll: ToHealAll(b.Active); break;
                    case RelicMechanic.UltimatePierce: Pierce(b.Ultimate, false); break;
                    case RelicMechanic.GrantTenacity: trait = BattleTraits.Tenacity; break;
                    case RelicMechanic.GrantSharpedge: trait = BattleTraits.Sharpedge; break;
                    case RelicMechanic.GrantSwiftshadow: trait = BattleTraits.Swiftshadow; break;
                    case RelicMechanic.GrantThickwall: trait = BattleTraits.Thickwall; break;
                    case RelicMechanic.GrantDevour: trait = BattleTraits.Devour; break;
                        // 规则类（回合/元气/命中/AI）不在这里，由 BattleRequestFactory 统一处理
                }
            }
            return trait;
        }

        // ---- 技能改写小工具（EffectAtom 是 struct ⇒ 改了必须写回数组，别只改副本）----

        private static void Retarget(SkillDef sk, TargetSelector t)
        {
            if (sk == null) return;
            sk.PrimaryTarget = t;
            if (sk.Effects == null) return;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                if (sk.Effects[i].Kind != EffectAtomKind.Damage) continue;
                var a = sk.Effects[i];
                a.Target = t;
                sk.Effects[i] = a;
            }
        }

        private static void ExtraHit(SkillDef sk)
        {
            if (sk == null || sk.Effects == null) return;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                if (sk.Effects[i].Kind != EffectAtomKind.Damage) continue;
                var a = sk.Effects[i];
                a.Hits = (a.Hits <= 0 ? 1 : a.Hits) + 1;
                sk.Effects[i] = a;
            }
        }

        private static void Pierce(SkillDef sk, bool trueDamage)
        {
            if (sk == null || sk.Effects == null) return;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                if (sk.Effects[i].Kind != EffectAtomKind.Damage) continue;
                var a = sk.Effects[i];
                a.IgnoreShield = true;
                if (trueDamage) a.TrueDamage = true;
                sk.Effects[i] = a;
            }
        }

        private static void AddBurn(SkillDef sk)
        {
            if (sk == null) return;
            var old = sk.Effects ?? new EffectAtom[0];
            for (int i = 0; i < old.Length; i++)
                if (old[i].Kind == EffectAtomKind.ApplyStatus && old[i].StatusId == StatusCatalog.Burn) return;
            var nw = new EffectAtom[old.Length + 1];
            System.Array.Copy(old, nw, old.Length);
            nw[old.Length] = EffectAtom.Dot(
                sk.PrimaryTarget == TargetSelector.Self ? TargetSelector.SingleFrontMost : sk.PrimaryTarget,
                StatusCatalog.Burn, 0.30f, 1, 2);
            sk.Effects = nw;
        }

        /// <summary>
        /// 把战记整体改写成【治疗全体我方】。
        /// ⚠ 必须处理**所有**原子：战记不一定是伤害技（实测句芒的战记是 Shield 原子），
        ///   只转 Damage 会让「战记改治疗」对辅助/防御型战记完全失效。
        /// 同时清掉伤害/状态/属性字段，避免留下「Heal 却还挂灼烧」这种怪组合。
        /// </summary>
        private static void ToHealAll(SkillDef sk)
        {
            if (sk == null) return;
            sk.PrimaryTarget = TargetSelector.AllAllies;
            if (sk.Effects == null) return;
            for (int i = 0; i < sk.Effects.Length; i++)
            {
                var a = sk.Effects[i];
                bool hadAmount = a.Power > 0f || a.PercentOfMaxHp > 0f;
                a.Kind = EffectAtomKind.Heal;
                a.Target = TargetSelector.AllAllies;
                a.TrueDamage = false;
                a.IgnoreShield = false;
                a.StatusId = null;
                a.StatKey = null;
                if (!hadAmount) a.PercentOfMaxHp = 0.15f;   // 原本无威力（纯状态/护盾）⇒ 给个基础治疗量
                sk.Effects[i] = a;
            }
        }

        private static RelicDef[] Build()
        {
            var L = new List<RelicDef>(170);

            // ---- 族 1：基础数值（12 件，v1.0 原有）----
            L.Add(new RelicDef("r_chiyu", "赤羽符", "全队 生命/攻击/防御 +8%", RelicRarity.Common, RelicEffect.PlayerStatPct, 8));
            L.Add(new RelicDef("r_qingmu", "青木符", "全队 生命/攻击/防御 +6%", RelicRarity.Common, RelicEffect.PlayerStatPct, 6));
            L.Add(new RelicDef("r_jifeng", "疾风铃", "战斗开局灵力 +3（更早放战记）", RelicRarity.Common, RelicEffect.StartMana, 3));
            L.Add(new RelicDef("r_pojun", "破军石", "敌方属性 -6%", RelicRarity.Common, RelicEffect.EnemyWeakPct, 6));
            L.Add(new RelicDef("r_xuanwu", "玄武甲", "全队 生命/防御 +10%", RelicRarity.Common, RelicEffect.PlayerStatPct, 10));
            L.Add(new RelicDef("r_zhenshan", "镇岳印", "全队 生命/攻击/防御 +15%", RelicRarity.Rare, RelicEffect.PlayerStatPct, 15));
            L.Add(new RelicDef("r_hantie", "寒铁魄", "敌方属性 -12%", RelicRarity.Rare, RelicEffect.EnemyWeakPct, 12));
            L.Add(new RelicDef("r_jiuxiao", "九霄令", "战斗开局灵力 +6", RelicRarity.Rare, RelicEffect.StartMana, 6));
            L.Add(new RelicDef("r_changsheng", "长生灯", "我方全体阵亡以 30% 生命复活 1 次", RelicRarity.Rare, RelicEffect.ReviveOnce, 30));
            L.Add(new RelicDef("r_taichu", "太初斧", "全队 生命/攻击/防御 +20%", RelicRarity.Boss, RelicEffect.PlayerStatPct, 20));
            L.Add(new RelicDef("r_hundun", "混沌珠", "敌方属性 -18%", RelicRarity.Boss, RelicEffect.EnemyWeakPct, 18));
            L.Add(new RelicDef("r_lunhui", "轮回镜", "复活 1 次（30% 生命）+ 开局灵力 +4", RelicRarity.Boss, RelicEffect.StartMana, 4));

            // ---- 族 2：五行灵符（5 元素 × 5 档 = 25）—— 指定五行的我方 +V% ----
            {
                float[] vs = { 6, 9, 12, 16, 22 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare, RelicRarity.Boss };
                string[] tier = { "初", "中", "上", "高", "极" };
                for (int e = 0; e < 5; e++)
                    for (int t = 0; t < 5; t++)
                        L.Add(new RelicDef("r_elem_" + Elements[e].ToString().ToLower() + "_" + (t + 1),
                            ElemGlyph[e] + "灵符·" + tier[t],
                            ElemGlyph[e].Substring(1) + "属性我方 生命/攻击/防御 +" + vs[t] + "%",
                            rs[t], RelicEffect.ElementStatPct, vs[t], 0f, Elements[e].ToString()));
            }

            // ---- 族 3：五行克星石（5 × 4 = 20）—— 指定五行的敌方 -V% ----
            {
                float[] vs = { 5, 8, 12, 17 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Boss };
                string[] suff = { "石", "玉", "晶", "髓" };
                for (int e = 0; e < 5; e++)
                    for (int t = 0; t < 4; t++)
                        L.Add(new RelicDef("r_foe_" + Elements[e].ToString().ToLower() + "_" + (t + 1),
                            ElemGlyph[e] + "克星" + suff[t],
                            "敌方" + ElemGlyph[e].Substring(1) + "属性 -" + vs[t] + "%",
                            rs[t], RelicEffect.EnemyElementWeakPct, vs[t], 0f, Elements[e].ToString()));
            }

            // ---- 族 4：定位契印（5 定位 × 4 档 = 20）—— 指定定位的我方 +V% ----
            {
                float[] vs = { 8, 12, 17, 24 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare, RelicRarity.Boss };
                string[] suff = { "契印", "宝印", "神印", "天印" };
                for (int r = 0; r < 5; r++)
                    for (int t = 0; t < 4; t++)
                        L.Add(new RelicDef("r_role_" + Roles[r].ToString().ToLower() + "_" + (t + 1),
                            RoleCn[r] + "者" + suff[t],
                            RoleCn[r] + "定位的我方 生命/攻击/防御 +" + vs[t] + "%",
                            rs[t], RelicEffect.RoleStatPct, vs[t], 0f, Roles[r].ToString()));
            }

            // ---- 族 5：异兽契印（30 只 × 1 = 30）—— 指定异兽 +18%（纯数值）----
            for (int b = 0; b < Beasts.Length; b++)
                L.Add(new RelicDef("r_beast_" + Beasts[b][0], Beasts[b][1] + "之契",
                    "【" + Beasts[b][1] + "】生命/攻击/防御 +18%",
                    RelicRarity.Rare, RelicEffect.BeastStatPct, 18f, 0f, Beasts[b][0]));

            // ---- 族 11：异兽印记（30 只 × 1 = 30）—— 只改该兽的【行为】，不加数值 ----
            {
                var roleIdx = new Dictionary<string, int>();
                for (int b = 0; b < Beasts.Length; b++)
                {
                    string id = Beasts[b][0], cn = Beasts[b][1], role = Beasts[b][3];
                    int k; roleIdx.TryGetValue(role, out k); roleIdx[role] = k + 1;
                    var mech = MechanicFor(role, k);
                    L.Add(new RelicDef("r_mark_" + id, cn + "·" + MechanicCn(mech),
                        "【" + cn + "】" + MechanicDesc(mech),
                        k == 0 ? RelicRarity.Rare : RelicRarity.Boss,
                        RelicEffect.None, 0f, 0f, id, mech));
                }
            }

            // ---- 族 12：通用机制（17 件）—— 改技能 / 授劫象 / 改规则 ----
            //   规则类需要的数值一律放 Value2（Value 留给可选的数值收益，实现双面效果）
            L.Add(new RelicDef("r_mech_all_hits", "兵无常势", "全队普攻改为攻击【全体敌人】", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.BasicHitsAll));
            L.Add(new RelicDef("r_mech_extra_hit", "连环击", "全队普攻段数 +1（更易触发连携/破盾）", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.BasicExtraHit));
            L.Add(new RelicDef("r_mech_burn", "火种", "全队普攻额外附带【灼烧】", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.BasicBurn));
            L.Add(new RelicDef("r_mech_pierce", "破甲锥", "全队普攻无视护盾", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.BasicPierce));
            L.Add(new RelicDef("r_mech_true", "真实之刃", "全队普攻改为【真实伤害】（无视护盾与减伤）", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.BasicTrue));
            L.Add(new RelicDef("r_mech_heal", "仁心", "全队战记改为【治疗全体我方】", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.ActiveHealAll));
            L.Add(new RelicDef("r_mech_ult_pierce", "贯绝", "全队奥义无视护盾", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.UltimatePierce));

            L.Add(new RelicDef("r_trait_tenacity", "不动印", "全队获得【坚韧】劫象", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.GrantTenacity));
            L.Add(new RelicDef("r_trait_sharpedge", "利刃符", "全队获得【锋锐】劫象", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.GrantSharpedge));
            L.Add(new RelicDef("r_trait_swiftshadow", "风踪符", "全队获得【疾影】劫象", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.GrantSwiftshadow));
            L.Add(new RelicDef("r_trait_thickwall", "磐石印", "全队获得【厚墙】劫象", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.GrantThickwall));
            L.Add(new RelicDef("r_trait_devour", "饕餮纹", "全队获得【吞噬】劫象", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.GrantDevour));

            L.Add(new RelicDef("r_rule_turns_plus", "延时符", "本场回合上限 +4（更从容，但也不是纯赚）", RelicRarity.Common, RelicEffect.None, 0f, 4f, null, RelicMechanic.TurnsPlus));
            L.Add(new RelicDef("r_rule_turns_minus", "速决符", "全队 生命/攻击/防御 +25%，但本场回合上限 -4（打不完就输）", RelicRarity.Rare, RelicEffect.PlayerStatPct, 25f, 4f, null, RelicMechanic.TurnsMinus));
            L.Add(new RelicDef("r_rule_no_rage", "无相", "全队奥义【不再需要元气】", RelicRarity.Boss, RelicEffect.None, 0f, 0f, null, RelicMechanic.UltimateNoRage));
            L.Add(new RelicDef("r_rule_no_hit", "澄明", "本场关闭命中率判定 —— 全队必定命中", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.NoHitChance));
            L.Add(new RelicDef("r_rule_enemy_cautious", "乱其心", "敌方打法变为【保守】（不抢放奥义、不激进）", RelicRarity.Rare, RelicEffect.None, 0f, 0f, null, RelicMechanic.EnemyCautious));

            // ---- 族 6：战意累积（6）—— 本局每胜 1 场 +V%，封顶 Value2% ----
            {
                string[] nm = { "战意·渐", "战意·盛", "战意·烈", "战意·狂", "战意·极", "战意·渊" };
                float[] per = { 1.5f, 2f, 2.5f, 3f, 4f, 5f };
                float[] cap = { 12f, 18f, 25f, 36f, 52f, 75f };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare, RelicRarity.Boss, RelicRarity.Boss };
                for (int i = 0; i < 6; i++)
                    L.Add(new RelicDef("r_win_" + (i + 1), nm[i],
                        "本局每胜利 1 场，全队 生命/攻击/防御 +" + per[i] + "%（封顶 +" + cap[i] + "%）",
                        rs[i], RelicEffect.PerWinStatPct, per[i], cap[i], null));
            }

            // ---- 族 7：赌命契（10）—— 双面：全队 +V%，代价开局灵力 -V2 ----
            {
                float[] gain = { 12, 16, 20, 24, 28, 34, 40, 48, 60, 80 };
                float[] cost = { 1, 1, 2, 2, 3, 3, 4, 5, 6, 8 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare,
                                     RelicRarity.Rare, RelicRarity.Rare, RelicRarity.Boss, RelicRarity.Boss, RelicRarity.Boss };
                string[] nm = { "赌命契·小", "赌命契·轻", "赌命契·中", "赌命契·重", "赌命契·险",
                                "赌命契·凶", "赌命契·绝", "赌命契·殒", "赌命契·逆", "赌命契·狂" };
                for (int i = 0; i < 10; i++)
                    L.Add(new RelicDef("r_trade_" + (i + 1), nm[i],
                        "全队 生命/攻击/防御 +" + gain[i] + "%，但战斗开局灵力 -" + cost[i],
                        rs[i], RelicEffect.TradeStatPct, gain[i], cost[i], null));
            }

            // ---- 族 8：弃生契（8）—— 双面：全队 +V%，代价不可复活 ----
            {
                float[] vs = { 18, 22, 26, 30, 34, 40, 48, 60 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare,
                                     RelicRarity.Rare, RelicRarity.Boss, RelicRarity.Boss, RelicRarity.Boss };
                string[] nm = { "弃生契·一", "弃生契·二", "弃生契·三", "弃生契·四",
                                "弃生契·五", "弃生契·六", "弃生契·七", "弃生契·终" };
                for (int i = 0; i < 8; i++)
                    L.Add(new RelicDef("r_norev_" + (i + 1), nm[i],
                        "全队 生命/攻击/防御 +" + vs[i] + "%，但本局【无法复活】",
                        rs[i], RelicEffect.NoReviveStatPct, vs[i], 0f, null));
            }

            // ---- 族 9：先声（8）—— 限时：本局前 Value2 场内 +V%，之后失效 ----
            {
                float[] vs = { 14, 18, 22, 26, 30, 36, 44, 55 };
                int[] span = { 1, 2, 2, 3, 3, 4, 5, 6 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Rare,
                                     RelicRarity.Rare, RelicRarity.Boss, RelicRarity.Boss, RelicRarity.Boss };
                string[] nm = { "先声·一鼓", "先声·二鼓", "先声·三鼓", "先声·四鼓",
                                "先声·五鼓", "先声·六鼓", "先声·七鼓", "先声·八鼓" };
                for (int i = 0; i < 8; i++)
                    L.Add(new RelicDef("r_early_" + (i + 1), nm[i],
                        "本局前 " + span[i] + " 场战斗内，全队 生命/攻击/防御 +" + vs[i] + "%（之后失效）",
                        rs[i], RelicEffect.EarlyWinStatPct, vs[i], span[i], null));
            }

            // ---- 族 10：众寡术（6）—— 上阵人数 ≤ Value2 时全队 +V% ----
            {
                float[] vs = { 10, 16, 22, 30, 40, 55 };
                int[] need = { 4, 4, 3, 3, 2, 2 };
                RelicRarity[] rs = { RelicRarity.Common, RelicRarity.Common, RelicRarity.Rare,
                                     RelicRarity.Rare, RelicRarity.Boss, RelicRarity.Boss };
                string[] nm = { "众寡术·四", "众寡术·四·强", "众寡术·三", "众寡术·三·强", "众寡术·二", "众寡术·二·极" };
                for (int i = 0; i < 6; i++)
                    L.Add(new RelicDef("r_few_" + (i + 1), nm[i],
                        "上阵人数 ≤ " + need[i] + " 时，全队 生命/攻击/防御 +" + vs[i] + "%",
                        rs[i], RelicEffect.TeamSizeStatPct, vs[i], need[i], null));
            }

            return L.ToArray();
        }

        // ==================================================================
        //  抽取
        // ==================================================================

        /// <summary>
        /// 从池里随机抽 <paramref name="count"/> 个（按稀有度权重）。
        /// <paramref name="bossLike"/>=true（精英 / Boss 节点）时提高 Rare/Boss 权重。
        /// 已拥有的遗物尽量不重复出现（池足够时）。
        /// </summary>
        public static List<RelicDef> Roll(int count, int act, bool bossLike, DeterministicRandom rng,
                                          ICollection<string> owned = null)
        {
            var pool = new List<RelicDef>();
            foreach (var d in All)
            {
                if (owned != null && owned.Contains(d.Id)) continue;   // 不重复给已持有
                pool.Add(d);
            }
            if (pool.Count == 0) pool.AddRange(All);   // 全收集完 → 允许重复

            int cCommon = bossLike ? 30 : 60;
            int cRare = bossLike ? 45 : 30;
            int cBoss = bossLike ? 25 : 10;

            var out_ = new List<RelicDef>();
            var used = new HashSet<string>();
            int guard = 0;
            while (out_.Count < count && guard++ < 200)
            {
                int roll = rng.NextInt(0, cCommon + cRare + cBoss);
                RelicRarity want = roll < cCommon ? RelicRarity.Common
                                 : roll < cCommon + cRare ? RelicRarity.Rare : RelicRarity.Boss;
                var cand = PickOfRarity(pool, want, rng, used);
                if (cand == null) cand = PickOfRarity(pool, RelicRarity.Common, rng, used); // 该稀有度抽空 → 降级
                if (cand == null) break;
                used.Add(cand.Id);
                out_.Add(cand);
            }
            return out_;
        }

        private static RelicDef PickOfRarity(List<RelicDef> pool, RelicRarity r,
                                             DeterministicRandom rng, HashSet<string> used)
        {
            int n = 0;
            foreach (var d in pool) if (d.Rarity == r && !used.Contains(d.Id)) n++;
            if (n == 0) return null;
            int k = rng.NextInt(0, n);
            foreach (var d in pool)
            {
                if (d.Rarity != r || used.Contains(d.Id)) continue;
                if (k-- == 0) return d;
            }
            return null;
        }

        // ==================================================================
        //  折叠（纯计算，不碰 BattleRequest / 存档）
        // ==================================================================

        /// <summary>
        /// 把一组已拥有遗物折叠成中立增益包。
        /// <paramref name="teamCount"/> = 本场上阵人数（供「众寡术」判定）；
        /// <paramref name="wins"/> = 本局已胜利场数（供「战意 / 先声」累计判定）。
        /// </summary>
        public static RelicMods Accumulate(ICollection<string> ownedIds, int teamCount, int wins)
        {
            var m = new RelicMods();
            if (ownedIds == null) return m;

            foreach (var id in ownedIds)
            {
                var d = Get(id);
                if (d == null) continue;
                switch (d.Effect)
                {
                    case RelicEffect.PlayerStatPct:
                        m.PlayerMul *= 1f + d.Value / 100f;
                        break;
                    case RelicEffect.EnemyWeakPct:
                        m.EnemyMul *= 1f - d.Value / 100f;
                        break;
                    case RelicEffect.ReviveOnce:
                        m.ReviveOn = true;
                        if (d.Value > 1) m.ReviveHpPercent = d.Value / 100f;
                        break;
                    case RelicEffect.StartMana:
                        m.StartMana += (int)System.Math.Round(d.Value);
                        break;

                    case RelicEffect.ElementStatPct:
                        m.AddElem(ParseElem(d.ScopeId), 1f + d.Value / 100f);
                        break;
                    case RelicEffect.RoleStatPct:
                        m.AddRole(ParseRole(d.ScopeId), 1f + d.Value / 100f);
                        break;
                    case RelicEffect.BeastStatPct:
                        m.AddBeast(d.ScopeId, 1f + d.Value / 100f);
                        break;
                    case RelicEffect.EnemyElementWeakPct:
                        m.AddWeak(ParseElem(d.ScopeId), 1f - d.Value / 100f);
                        break;

                    case RelicEffect.TeamSizeStatPct:
                        if (d.Value2 <= 0f || teamCount <= (int)d.Value2)
                            m.PlayerMul *= 1f + d.Value / 100f;
                        break;
                    case RelicEffect.TradeStatPct:
                        m.PlayerMul *= 1f + d.Value / 100f;              // 收益
                        m.StartMana -= (int)System.Math.Round(d.Value2); // 代价
                        break;
                    case RelicEffect.NoReviveStatPct:
                        m.PlayerMul *= 1f + d.Value / 100f;
                        m.ReviveForbidden = true;
                        break;
                    case RelicEffect.PerWinStatPct:
                    {
                        float pct = d.Value * wins;
                        if (d.Value2 > 0f && pct > d.Value2) pct = d.Value2;   // 封顶
                        m.PlayerMul *= 1f + pct / 100f;
                        break;
                    }
                    case RelicEffect.EarlyWinStatPct:
                        if (d.Value2 <= 0f || wins < (int)d.Value2)
                            m.PlayerMul *= 1f + d.Value / 100f;
                        break;
                }

                // 轮回镜特例：主标签 StartMana，但附带一次复活
                if (id == "r_lunhui") m.ReviveOn = true;
            }

            // 代价结算：灵力不能为负；弃生契覆盖一切复活来源
            if (m.StartMana < 0) m.StartMana = 0;
            if (m.ReviveForbidden) m.ReviveOn = false;
            return m;
        }

        private static Element ParseElem(string s)
        {
            Element e;
            return !string.IsNullOrEmpty(s) && System.Enum.TryParse(s, out e) ? e : Element.None;
        }

        private static RoleType ParseRole(string s)
        {
            RoleType r;
            return !string.IsNullOrEmpty(s) && System.Enum.TryParse(s, out r) ? r : RoleType.Guard;
        }
    }
}
