// ============================================================================
//  万相 · 运行时数据表（CSV 解析）
//  ---------------------------------------------------------------------------
//  ⭐ 分工（2026-11-02 定案）：
//      数值/文本  → Assets/Resources/Data/*.csv（Excel 直接编辑，另存 CSV UTF-8）
//      资源引用   → ScriptableObject（sprite/音频这类不能进 CSV）
//    解析器必须放 UI 层：WanXiang.Battle.Core / Campaign.Core 都是
//    noEngineReferences=true，碰不到 UnityEngine（TextAsset）；Core 只提供
//    XxxTable.Install(...) 这类纯数据入口。
//
//  表清单（缺表 = Warning + 用内置兜底，游戏照常跑）：
//      status.csv        状态定义（18 条）              → StatusCatalog.Install
//      boss.csv          首领面板数值（14 只）          → BossCatalog.Install
//      weather.csv       二十四节气 + 天气技（28 条）    → WeatherCatalog.Install*
//      enemy_budget.csv  敌方强度基准与幕系数           → EnemyBudget.InstallBasics
//      squad.csv         每幕出场规模                   → EnemyBudget.InstallSquad
//      map.csv           每幕层数 / 每层候选数           → MapRules.InstallShape
//      node_weights.csv  节点权重与解禁幕               → MapRules.InstallNode
//
//  ⛔ 约定：
//    ① 每张表首行是列名，**按列名取值**（列顺序可以调，但拼错列名会被忽略并 Warning）；
//    ② 解析失败带「文件 + 行号 + 原因」，绝不静默跳过；
//    ③ 只要有一张表加载过，就不要在运行中再装第二次（静态 _done 守卫）——
//       重复 Install 会把"表里的值"和"已经被覆盖过的当前值"叠在一起。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;

namespace WanXiang.Modules.UI
{
    public static class DataTables
    {
        private const string Dir = "Data/";
        private static bool _done;

        /// <summary>各表实际生效条数（0 = 该表缺失/解析失败，走内置）。</summary>
        public static int StatusCount { get; private set; }
        public static int BossCount { get; private set; }
        public static int WeatherCount { get; private set; }
        public static int BudgetKeys { get; private set; }
        public static int SquadActs { get; private set; }
        public static bool MapInstalled { get; private set; }
        public static int WeightRows { get; private set; }
        public static int RelicCount { get; private set; }
        public static int BeastCount { get; private set; }      // B0：beasts.csv 生效行数
        public static int LabelCount { get; private set; }      // B0：labels.csv 生效行数
        public static int TemperamentCount { get; private set; } // 通道化：temperaments.csv 生效行数
        public static int SkillPoolCount { get; private set; }   // B2：skill_pool.csv 生效行数
        public static int ComboCount { get; private set; }
        public static int ProgressionKeys { get; private set; }
        public static int TrialLawCount { get; private set; }
        public static int MarketKeys { get; private set; }

        /// <summary>解析期收集到的全部问题（行号 + 原因）。空 = 全部正常。</summary>
        public static readonly List<string> Problems = new List<string>();

        /// <summary>
        /// 装表（幂等）。调用点：<see cref="BattleRequestFactory"/> 组队入口（战斗类表）、
        /// <see cref="CampaignPanel"/> 建图之前（地图类表）。重复调用直接返回。
        /// </summary>
        public static void EnsureInstalled()
        {
            if (_done) return;
            _done = true;
            Problems.Clear();

            LoadStatus();
            LoadBoss();
            LoadWeather();
            LoadBudget();
            LoadSquad();
            LoadMap();
            LoadNodeWeights();
            LoadRelics();
            LoadCombos();
            LoadProgression();
            LoadTrialLaws();
            LoadMarket();
            LoadBeasts();
            LoadLabels();
            LoadTemperaments();
            LoadSkillPool();

            if (Problems.Count > 0)
                Debug.LogError("[DataTables] 共 " + Problems.Count + " 处问题：\n" + string.Join("\n", Problems.ToArray()));
            Debug.Log("[DataTables] 装表完成：状态 " + StatusCount + "／首领 " + BossCount +
                      "／天时 " + WeatherCount + "／预算 " + BudgetKeys + " 项／规模 " + SquadActs +
                      " 幕／地图 " + (MapInstalled ? "已装" : "内置") + "／节点权重 " + WeightRows + " 行" +
                      "／遗物 " + RelicCount + "／阵形 " + ComboCount + "／曲线 " + ProgressionKeys + " 键" +
                      "／劫律 " + TrialLawCount + "／灵市 " + MarketKeys + " 键" +
                      "／异兽 " + BeastCount + " 只／标签 " + LabelCount + " 条" +
                      "／性情通道 " + TemperamentCount + " 条／技能池 " + SkillPoolCount + " 条");
        }

        /// <summary>
        /// 编辑器专用：清掉"已装过"标记后重新装一遍（改了 CSV 想立刻验证时用）。
        /// ⚠ 只允许在编辑器/自检里调；运行中重复装表没有意义，还会把统计数字累加。
        /// </summary>
        public static void ReloadForEditor()
        {
            _done = false;
            StatusCount = BossCount = WeatherCount = BudgetKeys = SquadActs = WeightRows = 0;
            RelicCount = ComboCount = ProgressionKeys = TrialLawCount = MarketKeys = 0;
            BeastCount = LabelCount = TemperamentCount = SkillPoolCount = 0;
            MapInstalled = false;
            EnsureInstalled();
        }

        // ================================================================
        //  status.csv
        // ================================================================

        // ================================================================
        //  B0 · beasts.csv / labels.csv（异兽身份卡 + 通用显示名）
        //  ⚠ 表不存在 / 行为空 ⇒ 保持内置默认，绝不报错（骨架期不影响旧玩法）
        // ================================================================
        private static void LoadBeasts()
        {
            var rows = Read("beasts", out var header);
            if (rows == null) return;
            var list = new List<WanXiang.Battle.Core.BeastProfile>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "beasts.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                var bp = new WanXiang.Battle.Core.BeastProfile
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Element = r.Enum("element", Element.Wood),
                    Role = r.Enum("role", RoleType.None),
                    Rarity = r.Enum("rarity", Rarity.None),
                    Temperament = r.Enum("temperament", Temperament.None),
                    TierScore = r.Int("tier_score", 0),
                    Hp = r.Int("hp", 0), Atk = r.Int("atk", 0),
                    Def = r.Int("def", 0), Spd = r.Int("spd", 0),
                    TraitId = r.Str("trait_id"),
                    TraitKind = r.Enum("trait_kind", TraitKind.None),
                    Channel = r.Str("channel"),
                    TraitName = r.Str("trait_name"),
                    TraitValue = r.Float("value", 0f),
                    TraitValue2 = r.Float("value2", 0f),
                    Ultimate = r.Str("ultimate"),
                };
                list.Add(bp);
            }
            if (list.Count == 0) return;                       // 空表 ⇒ 回退内置
            WanXiang.Battle.Core.BeastTable.Install(list);
            BeastCount = list.Count;
        }

        private static void LoadLabels()
        {
            var rows = Read("labels", out var header);
            if (rows == null) return;
            int n = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "labels.csv", i + 2, Problems);
                string g = r.Str("group"), id = r.Str("id"), name = r.Str("name");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
                WanXiang.Battle.Core.LabelTable.Install(g, id, name);
                n++;
            }
            LabelCount = n;
        }

        private static void LoadTemperaments()
        {
            var rows = Read("temperaments", out var header);
            if (rows == null) return;
            var list = new List<WanXiang.Battle.Core.TemperamentProfile>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "temperaments.csv", i + 2, Problems);
                var p = new WanXiang.Battle.Core.TemperamentProfile
                {
                    Temper = r.Enum("temper", Temperament.None),
                    Channel = r.Str("channel"),
                    Value = r.Float("value", 0f),
                    Value2 = r.Float("value2", 0f),
                };
                if (p.Temper == Temperament.None || string.IsNullOrEmpty(p.Channel)) continue;
                list.Add(p);
            }
            if (list.Count == 0) return;
            WanXiang.Battle.Core.TemperamentTable.Install(list);
            TemperamentCount = list.Count;
        }

        // ================================================================
        //  B2 · skill_pool.csv（技能池：战记槽可换的候选）
        //  ⚠ 表缺失 ⇒ 池为空，规则类仍可用（不报错）
        // ================================================================
        private static void LoadSkillPool()
        {
            var rows = Read("skill_pool", out var header);
            if (rows == null) return;
            var list = new List<WanXiang.Battle.Core.SkillPoolEntry>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "skill_pool.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                list.Add(new WanXiang.Battle.Core.SkillPoolEntry
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Element = r.Enum("element", Element.None),
                    Type = r.Enum("type", SkillType.Active),
                    Tag = r.Str("tag"),
                    PowerMul = r.Float("powerMul", 1f),
                    HitsDelta = r.Int("hitsDelta", 0),
                    Target = r.Str("target"),
                    Desc = r.Str("desc"),
                });
            }
            if (list.Count == 0) return;
            WanXiang.Battle.Core.SkillSlotRules.Install(list);
            SkillPoolCount = list.Count;
        }

        private static void LoadStatus()
        {
            var rows = Read("status", out var header);
            if (rows == null) return;
            var list = new List<StatusDef>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "status.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                var d = new StatusDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    IsDebuff = r.Bool("isDebuff", true),
                    MaxStacks = r.Int("maxStacks", 1),
                    DotFlatPerStack = r.Float("dotFlatPerStack", 0f),
                    PreventsAction = r.Bool("preventsAction", false),
                    PreventsSkill = r.Bool("preventsSkill", false),
                    PreventsHeal = r.Bool("preventsHeal", false),
                    DamageTakenDeltaPerStack = r.Float("damageTakenPerStack", 0f),
                    AttackDeltaPerStack = r.Float("attackPerStack", 0f),
                    SpeedDeltaPerStack = r.Float("speedPerStack", 0f),
                    HealShieldDeltaPerStack = r.Float("healShieldPerStack", 0f),
                    ConsumeStackOnHit = r.Bool("consumeOnHit", false),
                    IsLingeringCounter = r.Bool("lingering", false),
                    Description = r.Str("description"),
                };
                list.Add(d);
            }
            StatusCatalog.Install(list);
            StatusCount = list.Count;
        }

        // ================================================================
        //  boss.csv
        // ================================================================

        private static void LoadBoss()
        {
            var rows = Read("boss", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "boss.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                var d = new BossDef
                {
                    Id = id,
                    DisplayName = r.Str("name", id),
                    Element = r.Enum("element", Element.None),
                    Role = r.Enum("role", RoleType.None),
                    Rarity = r.Enum("rarity", Rarity.Legend),
                    BaseHp = r.Int("hp", 1),
                    BaseAtk = r.Int("atk", 1),
                    BaseDef = r.Int("def", 0),
                    BaseSpeed = r.Int("speed", 100),
                    CritRate = r.Float("critRate", 0.15f),
                    CritDamage = r.Float("critDamage", 0.5f),
                    FlavorStatus = r.Str("flavorStatus"),
                };
                if (!BossCatalog.Install(d))
                {
                    r.Fail("首领 id 不存在（表里不能新增首领，只能改已有）");
                    continue;
                }
                ok++;
            }
            BossCount = ok;
        }

        // ================================================================
        //  weather.csv
        // ================================================================

        private static void LoadWeather()
        {
            var rows = Read("weather", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "weather.csv", i + 2, Problems);
                string kind = r.Str("kind", "solar").Trim().ToLowerInvariant();
                var w = new WeatherDef
                {
                    Id = r.Str("weatherId", r.Str("id")),
                    NodeName = r.Str("nodeName"),
                    BuffName = r.Str("buffName"),
                    Element = r.Enum("element", Element.None),

                    BanHeal = r.Bool("banHeal", false),
                    DamageAllMultiplier = r.Float("damageAllMultiplier", 1f),
                    DamageTakenMul = r.Float("damageTakenMul", 1f),
                    SpeedMulPlayer = r.Float("speedMulPlayer", 1f),
                    SpeedMulEnemy = r.Float("speedMulEnemy", 1f),
                    CdAdvanceMulPlayer = r.Float("cdAdvanceMulPlayer", 1f),
                    CdAdvanceMulEnemy = r.Float("cdAdvanceMulEnemy", 1f),
                    CritDamageBonusPlayer = r.Float("critDamageBonusPlayer", 0f),
                    CritDamageBonusEnemy = r.Float("critDamageBonusEnemy", 0f),
                    AoeDamageMul = r.Float("aoeDamageMul", 1f),
                    SingleDamageMul = r.Float("singleDamageMul", 1f),
                    WoodDamageMul = r.Float("woodDamageMul", 1f),
                    FireDamageMul = r.Float("fireDamageMul", 1f),
                    EarthDamageMul = r.Float("earthDamageMul", 1f),
                    MetalDamageMul = r.Float("metalDamageMul", 1f),
                    WaterDamageMul = r.Float("waterDamageMul", 1f),
                    FirstTurnDamageMul = r.Float("firstTurnDamageMul", 1f),
                    FireUnitDotTakenMul = r.Float("fireUnitDotTakenMul", 1f),
                    ShieldGainMul = r.Float("shieldGainMul", 1f),
                    HealOverflowShieldRatio = r.Float("healOverflowShieldRatio", 0f),

                    AttackBurnOn = r.Bool("attackBurnOn", false),
                    AttackBurnPower = r.Float("attackBurnPower", 0f),
                    AttackBurnTurns = r.Int("attackBurnTurns", 2),
                    PursuitOnCrit = r.Bool("pursuitOnCrit", false),
                    PursuitPower = r.Float("pursuitPower", 0.5f),
                    KillOverflowShield = r.Bool("killOverflowShield", false),
                    KillOverflowShieldRatio = r.Float("killOverflowShieldRatio", 0f),
                    ReviveEggOn = r.Bool("reviveEggOn", false),
                    ReviveEggDelayTurns = r.Int("reviveEggDelayTurns", 2),
                    ReviveEggHpPercent = r.Float("reviveEggHpPercent", 0.3f),
                    HasteEveryNTurns = r.Int("hasteEveryNTurns", 0),
                    HasteCdReduction = r.Int("hasteCdReduction", 2),
                    FreezeOnHitChance = r.Float("freezeOnHitChance", 0f),
                    FreezeOnHitTurns = r.Int("freezeOnHitTurns", 1),
                    DebuffDurationMinusOne = r.Bool("debuffDurationMinusOne", false),
                    ImmuneConfuseSilence = r.Bool("immuneConfuseSilence", false),
                    ExtraBasicAttackOnTurnEnd = r.Bool("extraBasicAttackOnTurnEnd", false),
                    IceMeltOnFireSkill = r.Bool("iceMeltOnFireSkill", false),

                    TurnStart = DecodeEffects(r.Str("turnStart"), r),
                    TurnEnd = DecodeEffects(r.Str("turnEnd"), r),
                };
                if (string.IsNullOrEmpty(w.Id)) { r.Fail("weatherId 为空"); continue; }

                if (kind == "skill")
                {
                    string sid = r.Str("id");
                    if (string.IsNullOrEmpty(sid)) { r.Fail("天气技 id 为空"); continue; }
                    WeatherCatalog.InstallWeatherSkill(new WeatherSkillDef
                    {
                        Id = sid,
                        Name = string.IsNullOrEmpty(w.NodeName) ? sid : w.NodeName,
                        Brings = w,
                    });
                    ok++;
                }
                else
                {
                    int idx = r.Int("solarIndex", 0);
                    if (idx <= 0 || idx > 24) { r.Fail("solarIndex 必须在 1..24"); continue; }
                    WeatherCatalog.InstallSolarTerm(idx, w);
                    ok++;
                }
            }
            WeatherCount = ok;
        }

        /// <summary>
        /// 天时原子的解码（与 DataTableExporter.EncodeEffects 严格互逆）：
        /// scope|kind|target|power|pct|status|stacks|turns|trueDmg|ignoreShield|once|minTurn|pick|statKey|delta
        /// </summary>
        private static WeatherEffect[] DecodeEffects(string cell, Row row)
        {
            if (string.IsNullOrWhiteSpace(cell)) return null;
            var parts = cell.Split(';');
            var list = new List<WeatherEffect>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i])) continue;
                var f = parts[i].Split('|');
                if (f.Length < 13)
                {
                    row.Fail("原子编码字段不足（需要 13 段以上）：" + parts[i]);
                    continue;
                }
                var a = new EffectAtom
                {
                    Kind = ParseEnum(f[1], EffectAtomKind.Damage),
                    Target = ParseEnum(f[2], TargetSelector.AllAllies),
                    Power = Num(f[3], 0f),
                    PercentOfMaxHp = Num(f[4], 0f),
                    StatusId = string.IsNullOrEmpty(f[5]) ? null : f[5],
                    StatusStacks = (int)Num(f[6], 0f),
                    StatusTurns = (int)Num(f[7], 0f),
                    TrueDamage = Num(f[8], 0f) != 0f,
                    IgnoreShield = Num(f[9], 0f) != 0f,
                    Hits = 1,
                };
                if (f.Length > 13)
                {
                    a.StatKey = string.IsNullOrEmpty(f[13]) ? null : f[13];
                    a.StatDelta = f.Length > 14 ? Num(f[14], 0f) : 0f;
                }
                var e = new WeatherEffect(
                    ParseEnum(f[0], WeatherScope.Both), a,
                    once: Num(f[10], 0f) != 0f,
                    pick: ParseEnum(f[12], TargetSelector.AllAllies),
                    minTurn: (int)Num(f[11], 0f));
                list.Add(e);
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        // ================================================================
        //  enemy_budget.csv / squad.csv
        // ================================================================

        private static void LoadBudget()
        {
            var rows = Read("enemy_budget", out var header);
            if (rows == null) return;
            float baseBp = float.NaN, jie = float.NaN, cap = float.NaN, boss = float.NaN;
            float elite = float.NaN, enc = float.NaN;
            var actMuls = new float[6];
            int seen = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "enemy_budget.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                float v = r.Float("value", float.NaN);
                if (string.IsNullOrEmpty(key) || float.IsNaN(v)) { r.Fail("key/value 缺失"); continue; }
                switch (key)
                {
                    case "base": baseBp = v; break;
                    case "jie_step": jie = v; break;
                    case "cap_jie_step": cap = v; break;
                    case "boss_mul": boss = v; break;
                    case "node_elite": elite = v; break;
                    case "node_encounter": enc = v; break;
                    default:
                        if (key.StartsWith("act") && key.Length == 4 &&
                            int.TryParse(key.Substring(3), out int act) && act >= 1 && act <= 5)
                            actMuls[act] = v;
                        else r.Fail("未知 key：" + key);
                        break;
                }
                seen++;
            }

            EnemyBudget.InstallBasics(baseBp, jie, cap, boss, elite, enc);
            for (int act = 1; act <= 5; act++)
                if (actMuls[act] > 0f) EnemyBudget.InstallActMul(act, actMuls[act]);
            BudgetKeys = seen;
        }

        private static void LoadSquad()
        {
            var rows = Read("squad", out var header);
            if (rows == null) return;
            int count = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "squad.csv", i + 2, Problems);
                int act = r.Int("act", 0);
                if (act < 1 || act > 5) { r.Fail("act 必须在 1..5"); continue; }
                EnemyBudget.InstallSquad(act, r.Int("encounter", 0), r.Int("elite", 0), r.Int("boss", 0));
                count++;
            }
            SquadActs = count;
        }

        // ================================================================
        //  map.csv / node_weights.csv
        // ================================================================

        private static void LoadMap()
        {
            var rows = Read("map", out var header);
            if (rows == null) return;
            int layers = 0, width = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "map.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                int v = r.Int("value", 0);
                if (key == "layers") layers = v;
                else if (key == "width") width = v;
                else r.Fail("未知 key：" + key);
            }
            if (layers <= 0 && width <= 0) return;
            // ⛔ 层数下限 4：低于 4 层时 BuildRoute 会退化成缺省图（见 SolarTermGraph 的注释）
            if (layers > 0 && layers < 4) { Problems.Add("map.csv：layers=" + layers + " 太小（≥4），已忽略"); layers = 0; }
            if (width > 0 && (width < 1 || width > 5)) { Problems.Add("map.csv：width=" + width + " 越界（1..5），已忽略"); width = 0; }
            MapRules.InstallShape(layers, width);
            MapInstalled = layers > 0 || width > 0;
        }

        private static void LoadNodeWeights()
        {
            var rows = Read("node_weights", out var header);
            if (rows == null) return;
            int count = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "node_weights.csv", i + 2, Problems);
                string kindText = r.Str("kind");
                if (!Enum.TryParse(kindText, true, out NodeKind kind))
                {
                    r.Fail("未知节点类型：" + kindText);
                    continue;
                }
                int weight = r.Int("weight", 0);
                int minAct = r.Int("minAct", 0);
                if (weight <= 0 && minAct <= 0) { r.Fail("weight 与 minAct 至少要有一个 > 0"); continue; }
                MapRules.InstallNode(kind, weight, minAct);
                count++;
            }
            WeightRows = count;
        }

        // ================================================================
        //  relics.csv / combos.csv / progression.csv
        // ================================================================

        private static void LoadRelics()
        {
            var rows = Read("relics", out var header);
            if (rows == null) return;
            var list = new List<RelicDef>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "relics.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                list.Add(new RelicDef(
                    id,
                    r.Str("name", id),
                    r.Str("desc"),
                    r.Enum("rarity", RelicRarity.Common),
                    r.Enum("effect", RelicEffect.None),
                    r.Float("value", 0f),
                    r.Float("value2", 0f),
                    r.Str("scopeId"),
                    r.Enum("mechanic", RelicMechanic.None)));
            }
            if (list.Count == 0) { Problems.Add("relics.csv：没有有效行，保持内置池"); return; }
            RelicCatalog.Install(list);
            RelicCount = list.Count;
        }

        private static void LoadCombos()
        {
            var rows = Read("combos", out var header);
            if (rows == null) return;
            var list = new List<ComboDef>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "combos.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                var cells = ParseCells(r.Str("cells"), r);
                if (cells == null || cells.Length == 0) { r.Fail("cells 为空或格式不对（应为 0;2;4）"); continue; }
                list.Add(new ComboDef
                {
                    Id = id,
                    Name = r.Str("name", id),
                    Note = r.Str("note"),
                    Cells = cells,
                    CellsAlt = ParseCells(r.Str("cellsAlt"), r),
                    Effect = r.Enum("effect", ComboEffect.FireStorm),
                    Power = r.Float("power", 1f),
                    Power2 = r.Float("power2", 0f),
                });
            }
            if (list.Count == 0) { Problems.Add("combos.csv：没有有效行，保持内置 10 组"); return; }
            ComboRules.Install(list);
            ComboCount = list.Count;
        }

        private static int[] ParseCells(string text, Row row)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parts = text.Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cells = new List<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    row.Fail("格号不是整数：" + parts[i]);
                    return null;
                }
                if (v < 0 || v > 8) { row.Fail("格号越界（0..8）：" + v); return null; }
                cells.Add(v);
            }
            return cells.ToArray();
        }

        private static void LoadProgression()
        {
            var rows = Read("progression", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "progression.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) { r.Fail("key 为空"); continue; }
                float v = r.Float("value", float.NaN);
                if (float.IsNaN(v)) { r.Fail("value 不是数字"); continue; }
                if (!WanXiang.Run.ProgressionTuning.TrySet(key, v))
                {
                    r.Fail("未知 key：" + key + "（见 文档/02_数据与配置/万相_数据表_说明.md）");
                    continue;
                }
                ok++;
            }
            ProgressionKeys = ok;
        }

        // ================================================================
        //  trial_laws.csv / market.csv
        // ================================================================

        private static void LoadTrialLaws()
        {
            var rows = Read("trial_laws", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "trial_laws.csv", i + 2, Problems);
                string id = r.Str("id");
                if (string.IsNullOrEmpty(id)) { r.Fail("id 为空"); continue; }
                if (System.Array.IndexOf(WanXiang.Trials.TrialLaws.Order, id) < 0)
                {
                    r.Fail("未知劫律 id：" + id + "（只能是 01..20，见 TrialLaws.Order）");
                    continue;
                }
                string key = r.Str("effect_key", "keep").Trim().ToLowerInvariant();
                if (System.Array.IndexOf(WanXiang.Trials.TrialLaws.KnownEffectKeys, key) < 0)
                {
                    r.Fail("未知 effect_key：" + key + "（写 none 表示只改文案，写 keep 表示沿用内置效果）");
                    continue;
                }
                WanXiang.Trials.TrialLaws.Install(id, r.Str("name"), r.Str("desc"), key, r.Float("effect_value", 0f));
                ok++;
            }
            TrialLawCount = ok;
        }

        private static void LoadMarket()
        {
            var rows = Read("market", out var header);
            if (rows == null) return;
            int ok = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = new Row(header, rows[i], "market.csv", i + 2, Problems);
                string key = r.Str("key").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) { r.Fail("key 为空"); continue; }
                float v = r.Float("value", float.NaN);
                if (float.IsNaN(v)) { r.Fail("value 不是数字"); continue; }
                if (!WanXiang.Run.MarketPricing.TrySet(key, v))
                {
                    r.Fail("未知 key：" + key + "（见 文档/02_数据与配置/万相_数据表_说明.md）");
                    continue;
                }
                ok++;
            }
            MarketKeys = ok;
        }

        // ================================================================
        //  读表 / 解析工具
        // ================================================================

        private static List<string> Read(string name, out List<string> header)
        {
            header = null;
            var ta = Resources.Load<TextAsset>(Dir + name);
            if (ta == null)
            {
                Debug.LogWarning("[DataTables] 找不到 Resources/" + Dir + name + ".csv —— 该表用内置数值");
                return null;
            }
            var lines = SplitLines(ta.text);
            if (lines.Count < 2)
            {
                Problems.Add(name + ".csv：只有表头或为空");
                return null;
            }
            header = SplitLine(lines[0]);
            for (int i = 0; i < header.Count; i++) header[i] = header[i].Trim();

            var body = new List<string>(lines.Count - 1);
            for (int i = 1; i < lines.Count; i++)
                if (!string.IsNullOrWhiteSpace(lines[i])) body.Add(lines[i]);
            return body;
        }

        private static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            if (text == null) return list;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);   // Excel 存 UTF-8 会带 BOM
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    // CSV 里的 "" 是转义引号；这里只跟踪引号状态，引号本身留给 SplitLine 处理
                    inQuotes = !inQuotes;
                    sb.Append(c);
                }
                else if ((c == '\n' || c == '\r') && !inQuotes)
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    list.Add(sb.ToString());
                    sb.Length = 0;
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>RFC4180 单行拆分（支持引号包裹、"" 转义、引号内的逗号）。</summary>
        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            if (line == null) return cells;
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        private static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return Enum.TryParse(s.Trim(), true, out T v) ? v : fallback;
        }

        private static float Num(string s, float fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v : fallback;
        }

        /// <summary>一行数据 + 列名索引；取值失败时把「文件 + 行号 + 原因」写进 problems。</summary>
        private sealed class Row
        {
            private readonly List<string> _header;
            private readonly List<string> _cells;
            private readonly string _file;
            private readonly int _line;
            private readonly List<string> _problems;

            public Row(List<string> header, string raw, string file, int line, List<string> problems)
            {
                _header = header;
                _cells = SplitLine(raw);
                _file = file;
                _line = line;
                _problems = problems;
            }

            public void Fail(string why) => _problems.Add(_file + " 第 " + _line + " 行：" + why);

            private string Raw(string col)
            {
                int idx = _header.IndexOf(col);
                if (idx < 0) { Fail("缺少列「" + col + "」"); return ""; }
                if (idx >= _cells.Count) return "";
                return _cells[idx].Trim();
            }

            public string Str(string col, string fallback = "")
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                // 导出器把换行写成字面 \n，这里还原
                return v.Replace("\\n", "\n");
            }

            public int Int(string col, int fallback)
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;
            }

            public float Float(string col, float fallback)
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : fallback;
            }

            public bool Bool(string col, bool fallback)
            {
                string v = Raw(col).ToLowerInvariant();
                if (v == "") return fallback;
                if (v == "1" || v == "true" || v == "yes" || v == "y") return true;
                if (v == "0" || v == "false" || v == "no" || v == "n") return false;
                Fail("布尔值无法识别：" + v);
                return fallback;
            }

            public T Enum<T>(string col, T fallback) where T : struct
                => ParseEnum(Raw(col), fallback);
        }
    }
}
