// ============================================================================
//  万相 · 劫 · 难度循环（GDD v1.1 §7 —— 本版相对 v1.0 最大的新增）
//  ---------------------------------------------------------------------------
//  v1.0 的结局是「击败后土」，v1.1 的结局是**「我决定不再续劫」**。
//  通关不是游戏结束，通关只是你第一次拿到了"可以停下来"的资格。
//
//  两条正交的难度轴（它们服务不同的事）：
//      境 · Realm —— 局外难度等级（社交性的：「我打到第几境」可以拿出去说）
//      劫 · Trial —— 局内轮次（过程性的：「这一局我走了多远」）
//  组合公式：生效劫律数 = min(20, (境-1) + (劫-1))。
//  一个数字就说清这一局的难度，不需要为「5 境 × 无穷劫」设计两套难度。
//
//  天阙三选一（每轮末）：登天阙 / 续劫 / 归元 —— 没有「主动放弃」选项的肉鸽，
//  会让玩家在必败时被迫浪费时间（GDD 原话）；归元收益刻意 = 正常结算的一半。
//
//  ⚠ 劫律不是数值膨胀，是**规则级修改**：第 20 道「万相归一」把前 19 条全部叠加，
//    玩家必须为每一道劫律重新思考阵容。
//
//  ⚠ 抽劫律的取舍：GDD 说"抽 1 道新劫律"，本实现取**按序生效**（第 N 条 = 表里
//    第 N 条）—— 不引入额外随机流，20 条的叠加顺序与 GDD 的"劫律 14 起"这类
//    序数描述一致。若策划要随机池，只改 <see cref="ActiveLawIds"/> 一处。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Trials
{
    /// <summary>二十道劫律（§7.5）。顺序即生效顺序；第 20 条是总加码。</summary>
    public static class TrialLaws
    {
        public const string LingeringQi = "01";      // 余气不散：残留 2 → 3
        public const string StubbornQi = "02";       // 节气难违：覆盖 3 → 2 回合
        public const string HeavenPunish = "03";     // 逆天之罚：反噬 15% → 25%
        public const string EndlessClash = "04";     // 相冲不止：相冲真伤 2% → 3.5%
        public const string CenterFall = "05";       // 中宫失守：中宫减伤 8% → 0%
        public const string FewHands = "06";         // 人稀：上阵 5 → 4
        public const string BeastStrong = "07";      // 兽强：精英属性额外 +15%
        public const string GreedyMarket = "08";     // 商贾贪婪：灵市价格 +50%
        public const string NarrowPath = "09";       // 路窄：分叉层 2 → 1 候选
        public const string NoRevive = "10";         // 回天无力：复活只到 60% 血
        public const string BurnDeep = "11";         // 灼烧入骨：灼烧伤害 +30%
        public const string IceLast = "12";          // 冰蚀不化：1.5% → 2.5%
        public const string Silenced = "13";         // 噤声：控制技 CD +1
        public const string BossCrown = "14";        // 守关加冠：Boss 多 1 特性
        public const string ChaosWuxing = "15";      // 五行失序：共鸣门槛 2/4/5 → 3/5/5
        public const string ColdAltar = "16";        // 香火断绝：孵穴回复 40% → 20%
        public const string BeastKing = "17";        // 兽王当立：每幕首节点强制精英
        public const string NoEscape = "18";         // 无路可退：精英不可跳过
        public const string LowSky = "19";           // 天阙低垂：镜像 3 → 5
        public const string AllIsOne = "20";         // 万相归一：前 19 条全生效 + 敌方每回合 +1%

        /// <summary>按生效顺序的完整表（前 19 条按序叠加，第 20 条是总闸）。</summary>
        public static readonly string[] Order =
        {
            LingeringQi, StubbornQi, HeavenPunish, EndlessClash, CenterFall,
            FewHands, BeastStrong, GreedyMarket, NarrowPath, NoRevive,
            BurnDeep, IceLast, Silenced, BossCrown, ChaosWuxing,
            ColdAltar, BeastKing, NoEscape, LowSky, AllIsOne,
        };

        // ================================================================
        //  外置表（Resources/Data/trial_laws.csv）
        //  ------------------------------------------------------------------
        //  ⭐ 2026-11-02：名字/描述/生效数值都允许表覆盖，但 **Order 不改** ——
        //    劫律的生效顺序是"第 N 道劫"的语义，重排会让老存档的劫数对不上。
        //  ⛔ effect_key 只认这里列出的键；写错会让该劫变成"只有文案、没有效果"，
        //    解析器会直接报行号（避免又出现"文案承诺了但没实现"的假字）。
        // ================================================================

        private static readonly System.Collections.Generic.Dictionary<string, string> _nameOverride
            = new System.Collections.Generic.Dictionary<string, string>();

        private static readonly System.Collections.Generic.Dictionary<string, string> _descOverride
            = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>id → (效果键, 数值)。表里写了的覆盖内置。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, (string key, float val)> _effect
            = new System.Collections.Generic.Dictionary<string, (string key, float val)>();

        /// <summary>内置效果表（= 2026-11-02 之前硬编码在 TrialTuning.From 里的那一份）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, (string key, float val)> _builtinEffect
            = new System.Collections.Generic.Dictionary<string, (string key, float val)>
        {
            { LingeringQi, ("linger_nodes", 3f) },
            { StubbornQi,  ("none", 0f) },            // 02 未接线（节气残留 3→2 需天气系统配合）
            { HeavenPunish,("backlash_extra", 0.25f) },
            { EndlessClash,("counter_true", 0.035f) },
            { CenterFall,  ("center_reduction", 0f) },
            { FewHands,    ("none", 0f) },            // 06 未接线（上阵 5→4 需编队校验）
            { BeastStrong, ("elite_extra_mul", 1.15f) },
            { GreedyMarket,("none", 0f) },            // 08 未接线（灵市 +50% 需定价接劫律）
            { NarrowPath,  ("narrow_path", 1f) },
            { NoRevive,    ("soul_fade", 1f) },
            { BurnDeep,    ("burn_taken_mul", 1.30f) },
            { IceLast,     ("ice_dot_mul", 1.67f) },
            { Silenced,    ("none", 0f) },            // 13 未接线（技能 CD 已于 2026-11-01 删除）
            { BossCrown,   ("boss_traits", 1f) },
            { ChaosWuxing, ("resonance_shift", 1f) },
            { ColdAltar,   ("nest_heal", 0.20f) },
            { BeastKing,   ("force_elite", 1f) },
            { NoEscape,    ("none", 0f) },            // 18 未接线
            { LowSky,      ("finale_mirrors", 5f) },
            { AllIsOne,    ("all_is_one", 1f) },
        };

        /// <summary>已从表里安装的条数（0 = 全内置）。</summary>
        public static int InstalledFromTable { get; private set; }

        /// <summary>表里认识的效果键（解析器用它校验，写错立即报行号）。</summary>
        public static readonly string[] KnownEffectKeys =
        {
            "none", "linger_nodes", "backlash_extra", "counter_true", "center_reduction",
            "elite_extra_mul", "narrow_path", "soul_fade", "burn_taken_mul", "ice_dot_mul",
            "boss_traits", "resonance_shift", "nest_heal", "force_elite", "finale_mirrors", "all_is_one",
        };

        /// <summary>覆盖一条劫律的名字/描述/效果（表里没写的保持内置）。</summary>
        public static void Install(string id, string name, string desc, string effectKey, float effectValue)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!string.IsNullOrEmpty(name)) _nameOverride[id] = name;
            if (!string.IsNullOrEmpty(desc)) _descOverride[id] = desc;
            if (!string.IsNullOrEmpty(effectKey) && effectKey != "keep")
            {
                _effect[id] = (effectKey.Trim().ToLowerInvariant(), effectValue);
            }
            InstalledFromTable++;
        }

        /// <summary>取一条劫律的生效效果（表优先，其次内置）。</summary>
        public static (string key, float val) EffectOf(string id)
        {
            if (_effect.TryGetValue(id, out var e)) return e;
            if (_builtinEffect.TryGetValue(id, out var b)) return b;
            return ("none", 0f);
        }

        /// <summary>劫律描述（表里没写 = 空串；UI 可只显示名字）。</summary>
        public static string Desc(string id)
            => !string.IsNullOrEmpty(id) && _descOverride.TryGetValue(id, out var d) ? d : "";

        public static string Name(string id)
        {
            if (!string.IsNullOrEmpty(id) && _nameOverride.TryGetValue(id, out var n)) return n;
            switch (id)
            {
                case LingeringQi: return "余气不散";
                case StubbornQi: return "节气难违";
                case HeavenPunish: return "逆天之罚";
                case EndlessClash: return "相冲不止";
                case CenterFall: return "中宫失守";
                case FewHands: return "人稀";
                case BeastStrong: return "兽强";
                case GreedyMarket: return "商贾贪婪";
                case NarrowPath: return "路窄";
                case NoRevive: return "回天无力";
                case BurnDeep: return "灼烧入骨";
                case IceLast: return "冰蚀不化";
                case Silenced: return "噤声";
                case BossCrown: return "守关加冠";
                case ChaosWuxing: return "五行失序";
                case ColdAltar: return "香火断绝";
                case BeastKing: return "兽王当立";
                case NoEscape: return "无路可退";
                case LowSky: return "天阙低垂";
                case AllIsOne: return "万相归一";
                default: return id;
            }
        }

        public static bool Has(IReadOnlyList<string> active, string id)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i] == id) return true;
            return false;
        }
    }

    /// <summary>一轮的图/经济侧调参（由生效劫律折算；默认值 = 无劫律的 v1.1 基准）。</summary>
    public sealed class TrialTuning
    {
        public int LingerNodes = 2;          // 01 余气不散 → 3
        public float BacklashExtra = 0.15f;  // 03 逆天之罚 → 0.25
        public float CounterTrueDamage = 0.02f; // 04 相冲不止 → 0.035
        public float CenterReduction = 0.08f;   // 05 中宫失守 → 0
        public float EliteExtraMul = 1f;        // 07 兽强 → 1.15
        public float BurnTakenMul = 1f;         // 11 灼烧入骨 → 1.30
        public float NestHealPercent = 0.40f;   // 16 香火断绝 → 0.20
        public int FinaleMirrors = 3;           // 19 天阙低垂 → 5
        public bool SoulFade = false;           // 10 回天无力（复活只到 60%）
        public bool AllIsOne = false;           // 20 万相归一（敌方每回合攻 +1%）
        public float IceErosionDotMul = 1f;     // 12 冰蚀不化（1.5% → 2.5%）
        public int ResonanceCountShift = 0;     // 15 五行失序（门槛 +1）
        public int BossTraitCount = 0;          // 14 守关加冠（Boss 特性条数）
        public bool NarrowPath;                 // 09 路窄
        public bool ForceEliteFirst;            // 17 兽王当立

        /// <summary>把生效劫律折算成调参（战斗类劫律另见 <see cref="ApplyTo"/>）。</summary>
        public static TrialTuning From(IReadOnlyList<string> laws)
        {
            var t = new TrialTuning();
            if (laws == null) return t;
            for (int i = 0; i < laws.Count; i++)
            {
                var (key, val) = TrialLaws.EffectOf(laws[i]);
                switch (key)
                {
                    case "linger_nodes": t.LingerNodes = (int)val; break;
                    case "backlash_extra": t.BacklashExtra = val; break;
                    case "counter_true": t.CounterTrueDamage = val; break;
                    case "center_reduction": t.CenterReduction = val; break;
                    case "elite_extra_mul": t.EliteExtraMul = val; break;
                    case "narrow_path": t.NarrowPath = val != 0f; break;
                    case "soul_fade": t.SoulFade = val != 0f; break;
                    case "burn_taken_mul": t.BurnTakenMul = val; break;
                    case "ice_dot_mul": t.IceErosionDotMul = val; break;
                    case "boss_traits": t.BossTraitCount = (int)val; break;
                    case "resonance_shift": t.ResonanceCountShift = (int)val; break;
                    case "nest_heal": t.NestHealPercent = val; break;
                    case "force_elite": t.ForceEliteFirst = val != 0f; break;
                    case "finale_mirrors": t.FinaleMirrors = (int)val; break;
                    case "all_is_one": t.AllIsOne = val != 0f; break;
                    case "none": break;    // 只写文案、没有数值效果的劫律（未接线，见 _builtinEffect 注释）
                    default: break;        // 未知键已被解析器拦下，这里只求不崩
                }
            }
            return t;
        }
    }

    /// <summary>
    /// 境 × 劫 的局内状态。一场对局的全部难度信息都在这里 ——
    /// <see cref="BuildRoundConfig"/> 与 <see cref="BuildTuning"/> 是它对外的两个出口。
    /// </summary>
    public sealed class TrialState
    {
        public const int MaxRealm = 5;
        public const int MaxLaws = 20;

        /// <summary>境（1..5）。登天阙成功才推进。</summary>
        public int Realm { get; private set; } = 1;

        /// <summary>劫（局内轮次，1 起）。每次续劫 +1。</summary>
        public int Trial { get; private set; } = 1;

        public TrialState(int realm = 1, int trial = 1)
        {
            Realm = System.Math.Max(1, realm);
            Trial = System.Math.Max(1, trial);
        }

        /// <summary>生效劫律数 = min(20, (境-1) + (劫-1)) —— 一个数字说清这一局的难度。</summary>
        public int ActiveLawCount => System.Math.Min(MaxLaws, (Realm - 1) + (Trial - 1));

        /// <summary>生效的劫律 id（按序前 N 条）。</summary>
        public List<string> ActiveLawIds()
        {
            var ids = new List<string>(ActiveLawCount);
            for (int i = 0; i < ActiveLawCount; i++) ids.Add(TrialLaws.Order[i]);
            return ids;
        }

        /// <summary>「劫数加护」：我方全体全属性 +2%/劫（局内累积、可叠加）。</summary>
        public float JieGuardMul => 1f + 0.02f * (Trial - 1);

        /// <summary>
        /// 战斗侧：把战斗类劫律写进这一局的独占配置（BattleConfig 本来就是"一场战斗独占"，
        /// 劫律只是往里多写几行 —— 不引入新的全局态）。
        /// </summary>
        public BattleConfig BuildRoundConfig()
        {
            var cfg = BattleConfig.Default;
            var laws = ActiveLawIds();
            var tuning = TrialTuning.From(laws);

            cfg.BacklashExtraDamage = tuning.BacklashExtra;                  // 03
            cfg.AdjacencyCounterTrueDamagePercent = tuning.CounterTrueDamage; // 04
            cfg.CenterDamageReduction = tuning.CenterReduction;              // 05
            cfg.BurnTakenMul = tuning.BurnTakenMul;                          // 11
            cfg.IceErosionDotMul = tuning.IceErosionDotMul;                  // 12
            cfg.ResonanceCountShift = tuning.ResonanceCountShift;            // 15
            cfg.ReviveHpScale = tuning.SoulFade ? 0.6f : 1f;                 // 10
            cfg.AllIsOne = tuning.AllIsOne;                                  // 20
            return cfg;
        }

        /// <summary>图/经济侧：余气残留、精英加成、孵穴回复、天阙镜像数。</summary>
        public TrialTuning BuildTuning() => TrialTuning.From(ActiveLawIds());

        /// <summary>转成 Campaign 层的 <c>RunTuning</c>（依赖方向：Trials → Campaign，不能反）。</summary>
        public WanXiang.Campaign.RunTuning ToRunTuning()
        {
            var tuning = TrialTuning.From(ActiveLawIds());
            return new WanXiang.Campaign.RunTuning
            {
                LingerNodes = tuning.LingerNodes,
                NestHealPercent = tuning.NestHealPercent,
                FinaleMirrors = tuning.FinaleMirrors,
                EliteExtraMul = tuning.EliteExtraMul,
                NarrowPath = tuning.NarrowPath,
                ForceEliteFirst = tuning.ForceEliteFirst,
            };
        }

        // ================================================================
        //  天阙抉择（§7.3）与结算（§7.7）
        // ================================================================

        public enum Verdict { Ascend, Continue, Return }

        /// <summary>一次抉择的结算结果（灵卵增量 + 状态迁移），纯函数便于自检。</summary>
        public struct Settlement
        {
            public Verdict Verdict;
            public bool AscendSuccess;   // 仅登天阙有意义
            public int EggsFromSettle;   // 本步结算给多少灵卵（不含局内剩余）
            public int NewRealm;
            public int NewTrial;
            public string Note;
        }

        /// <summary>
        /// 天阙三选一。<paramref name="runEggs"/> = 局内剩余灵卵；
        /// <paramref name="actsReached"/> = 本轮到达的幕数（1..5）；
        /// <paramref name="ascendWin"/> = 登天阙是否战胜后土。
        /// </summary>
        public Settlement Resolve(Verdict choice, int runEggs, int actsReached, bool ascendWin)
        {
            var s = new Settlement { Verdict = choice, AscendSuccess = choice == Verdict.Ascend && ascendWin };

            switch (choice)
            {
                case Verdict.Ascend:
                    if (ascendWin)
                    {
                        // §7.7：灵卵 = 剩余 + 5+3×劫数 + 幕数（幕数对所有结局都结算，
                        // 所以这里统一加上 —— 剩余的搬运在 RunDriver/Meta 层）
                        s.EggsFromSettle = 5 + 3 * (Trial - 1) + actsReached;
                        s.NewRealm = System.Math.Min(MaxRealm, Realm + 1);
                        s.NewTrial = 1;
                        s.Note = $"登天阙成功：解锁第 {s.NewRealm} 境";
                    }
                    else
                    {
                        s.EggsFromSettle = actsReached;              // 失败不扣资产，成本是时间
                        s.NewRealm = Realm;
                        s.NewTrial = 1;
                        s.Note = "登天阙失败：按幕数结算";
                    }
                    break;

                case Verdict.Continue:
                    s.EggsFromSettle = 0;                            // 灵卵保留在局内（结算另算）
                    s.NewRealm = Realm;
                    s.NewTrial = Trial + 1;
                    s.Note = $"续劫：第 {s.NewTrial} 劫，全体获得「劫数加护」+2%（敌方 +3%，净差 +1%/劫）";
                    break;

                case Verdict.Return:
                    s.EggsFromSettle = -runEggs / 2;                 // 归元：剩余 ×0.5（向下取整）
                    s.NewRealm = Realm;
                    s.NewTrial = 1;
                    s.Note = "归元：主动结束，灵卵结算减半";
                    break;
            }
            return s;
        }

        // ================================================================
        //  灵市价格（§8.4）：price = base × ActShopMul(幕) × (1 + 0.10 × 劫律数)
        //  ActShopMul = [1.00, 1.20, 1.40, 1.60]；劫律 08「商贾贪婪」再 ×1.5、刷新翻倍。
        //  基础价：灵兽 3 / 玄兽 6 / 神兽 12 / 灵·玄魂 2/4 / 神魂 8 / 重铸 2 / 刷新 1 或 2。
        // ================================================================

        public static float ShopActMul(int act)
        {
            switch (act)
            {
                case 1: return 1.00f;
                case 2: return 1.20f;
                case 3: return 1.40f;
                default: return 1.60f;
            }
        }

        public static int ShopPrice(int basePrice, int act, int activeLawCount, bool greedy = false)
        {
            float p = basePrice * ShopActMul(act) * (1f + 0.10f * activeLawCount);
            if (greedy) p *= 1.5f;                       // 劫律 08
            return CoreMath.Max(1, CoreMath.RoundDamage(p));
        }

        /// <summary>全灭结算（§7.7：剩余 ×0.5 + 幕数）。</summary>
        public static Settlement DefeatSettlement(int runEggs, int actsReached)
        {
            return new Settlement
            {
                Verdict = Verdict.Return,
                EggsFromSettle = -runEggs / 2 + actsReached,
                Note = $"全灭：剩余灵卵 ×0.5 + 幕数 {actsReached}",
            };
        }
    }
}
