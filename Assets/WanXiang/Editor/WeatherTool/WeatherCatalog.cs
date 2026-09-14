// ============================================================================
//  万相 · 天时内容目录（编辑器侧，STEP 3）
//  ---------------------------------------------------------------------------
//  把 GDD 3.3 的 fieldBuff 自然语言逐条翻译成 WeatherDef。本批已翻
//  **16 条节气 + 4 条天气技**；剩余 8 条是"事件钩子型"（复活卵/追击/反弹/
//  额外普攻……），机制不在"场地修正 + 原子"的表达范围内，保留 GDD 原文
//  （Untreated）待逐条建钩子接入。
//
//  ⚠ 节气序号以 solar_terms.json 为准：**夏至=10、冬至=22**（曾把夏至挂到
//    22 上，已修正 —— 加新条目前先对照 JSON，别按"一年里第几个节气"的直觉猜）。
//
//  翻译时的取舍（都已记录在各条注释）：
//  - 小暑"受 3% 灼烧"：本质是固定 % 真伤，用 Damage(TrueDamage) 原子而不是
//    灼烧状态 —— 灼烧状态的 dot 在施加瞬间按施法者攻击折算，天时无施法者。
//  - 春分"超 12 回合回复翻倍"落成 MinTurn=13 的第二笔同额回复（等价、各自留痕）。
//  - 大暑"火单位灼烧减半"作用于所有 DoT（结算路径同一条），待策划确认。
//  - 大雪"速度差 ×1.5"对排序是恒等变换，等追击/闪避阈值类机制出现再消费。
//  - 白露/召风的"命中率"、立冬"受击冻结"等需要命中判定或事件钩子，待接。
//
//  运行期内容链（SO / 热更）待接：本目录先服务自检与灰盒，与
//  BattleSampleContent 同属"脚手架"性质，不进核心层。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Editor.WeatherTool
{
    public static class WeatherCatalog
    {
        /// <summary>按节气序号（GDD 3.3 的 01..24）取天时；未翻译的返回 null。</summary>
        public static WeatherDef GetSolarTerm(int index)
        {
            foreach (var w in SolarTerms)
                if (w.SolarIndex == index) return w.Weather;
            return null;
        }

        public static WeatherSkillDef GetWeatherSkill(string id)
        {
            foreach (var s in WeatherSkills)
                if (s.Id == id) return s;
            return null;
        }

        // ====================================================================
        //  二十四节气（本批 7 条）
        // ====================================================================

        private struct TermEntry
        {
            public int SolarIndex;
            public WeatherDef Weather;
            public string Untreated;   // 未翻译时保留 GDD 原文
        }

        private static readonly TermEntry[] SolarTerms =
        {
            // 01 立春：战斗开始时，我方全体 2 层「生机」（每层每回合回复 2% 最大生命）
            new TermEntry
            {
                SolarIndex = 1,
                Weather = new WeatherDef
                {
                    Id = "solar_lichun", NodeName = "立春", BuffName = "东风解冻",
                    Element = Element.Wood,
                    TurnStart = new[]
                    {
                        new WeatherEffect(WeatherScope.PlayerSide,
                            EffectAtom.Status(TargetSelector.AllAllies, StatusCatalog.Vigor, 2, 2),
                            once: true),
                    },
                },
            },

            // 02 雨水：每回合结束全体回复 3% 最大生命；治疗溢出量的 50% 转化为护盾
            //     （溢出转护盾走 WeatherResolver 的 GrantOverflowShield，只作用在天时回复上）
            new TermEntry
            {
                SolarIndex = 2,
                Weather = new WeatherDef
                {
                    Id = "solar_yushui", NodeName = "雨水", BuffName = "润物无声",
                    Element = Element.Wood,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Heal(TargetSelector.AllAllies, 0f, 0.03f)),
                    },
                    HealOverflowShieldRatio = 0.5f,
                },
            },

            // 04 春分：敌我双方每回合各回复 2% 最大生命；超过 12 回合回复量翻倍
            //     （翻倍落成 MinTurn=13 的第二笔同额回复，而不是把第一笔改条件 ——
            //      两条独立效果在指纹里也各自留痕，语义与 GDD "回复量翻倍"等价）
            new TermEntry
            {
                SolarIndex = 4,
                Weather = new WeatherDef
                {
                    Id = "solar_chunfen", NodeName = "春分", BuffName = "昼夜均分",
                    Element = Element.Wood,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Heal(TargetSelector.AllAllies, 0f, 0.02f)),
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Heal(TargetSelector.AllAllies, 0f, 0.02f),
                            minTurn: 13),
                    },
                },
            },

            // 06 谷雨（春末土节点）：每回合结束全队 +1 层「谷」（每层 +1% 全属性，上限 10）
            new TermEntry
            {
                SolarIndex = 6,
                Weather = new WeatherDef
                {
                    Id = "solar_guyu", NodeName = "谷雨", BuffName = "雨生百谷",
                    Element = Element.Earth,
                    TurnEnd = new[]
                    {
                        // turns=99 ≈ 本场持续；层数上限由 StatusCatalog.MaxStacks 管
                        new WeatherEffect(WeatherScope.PlayerSide,
                            EffectAtom.Status(TargetSelector.AllAllies, StatusCatalog.Grain, 1, 99)),
                    },
                },
            },

            // 08 小满：我方技能 CD 推进速度 +30%（确定性累积器，见 BattleUnit.CdProgressExtra）
            new TermEntry
            {
                SolarIndex = 8,
                Weather = new WeatherDef
                {
                    Id = "solar_xiaoman", NodeName = "小满", BuffName = "麦气充盈",
                    Element = Element.Fire,
                    CdAdvanceMulPlayer = 1.3f,
                },
            },

            // 10 夏至：全场造成的伤害 +25%，受到的伤害 +25%（等价全场伤害 ×1.25）
            //     ⚠ 索引对照 solar_terms.json：夏至=10、冬至=22（此前目录挂反已修正）
            new TermEntry
            {
                SolarIndex = 10,
                Weather = new WeatherDef
                {
                    Id = "solar_xiazhi", NodeName = "夏至", BuffName = "日长至·极阳",
                    Element = Element.Fire,
                    DamageAllMultiplier = 1.25f,
                },
            },

            // 11 小暑：每回合开始，全场单位受 3% 最大生命的灼烧（无视护盾与减伤）
            //     取舍：真伤原子而非灼烧状态（天时无施法者，见文件头）
            new TermEntry
            {
                SolarIndex = 11,
                Weather = new WeatherDef
                {
                    Id = "solar_xiaoshu", NodeName = "小暑", BuffName = "温风熏灼",
                    Element = Element.Fire,
                    TurnStart = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Damage(TargetSelector.AllAllies, 0f, trueDamage: true)
                                .WithPercentOfMaxHp(0.03f)),
                    },
                },
            },

            // 12 大暑（夏末土节点）：火属性伤害 -30%，土属性伤害 +30%；火单位灼烧减半
            //     取舍：dot 减半作用于所有持续伤害（不止灼烧），见 WeatherDef.FireUnitDotTakenMul 注释
            new TermEntry
            {
                SolarIndex = 12,
                Weather = new WeatherDef
                {
                    Id = "solar_dashu", NodeName = "大暑", BuffName = "土润溽暑",
                    Element = Element.Earth,
                    FireDamageMul = 0.7f,
                    EarthDamageMul = 1.3f,
                    FireUnitDotTakenMul = 0.5f,
                },
            },

            // 13 立秋：我方暴击伤害 +40%（与单位自身暴伤相加）
            new TermEntry
            {
                SolarIndex = 13,
                Weather = new WeatherDef
                {
                    Id = "solar_liqiu", NodeName = "立秋", BuffName = "金风肃杀",
                    Element = Element.Metal,
                    CritDamageBonusPlayer = 0.4f,
                },
            },

            // 15 白露：我方暴击伤害 +30%；命中率 -10% 待接（战斗核心尚无命中/闪避判定）
            new TermEntry
            {
                SolarIndex = 15,
                Weather = new WeatherDef
                {
                    Id = "solar_bailu", NodeName = "白露", BuffName = "露凝为霜",
                    Element = Element.Metal,
                    CritDamageBonusPlayer = 0.3f,
                },
            },

            // 16 秋分：AOE 技能伤害 -40%，单体技能伤害 +25%（按原子目标形状判定）
            new TermEntry
            {
                SolarIndex = 16,
                Weather = new WeatherDef
                {
                    Id = "solar_qiufen", NodeName = "秋分", BuffName = "收敛肃降",
                    Element = Element.Metal,
                    AoeDamageMul = 0.6f,
                    SingleDamageMul = 1.25f,
                },
            },

            // 18 霜降（秋末土节点）：每回合结束，敌方生命值最低的单位受 5% 最大生命的真实伤害
            new TermEntry
            {
                SolarIndex = 18,
                Weather = new WeatherDef
                {
                    Id = "solar_shuangjiang", NodeName = "霜降", BuffName = "霜华满地",
                    Element = Element.Earth,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.EnemySide,
                            EffectAtom.Damage(TargetSelector.SingleLowestHp, 0f, trueDamage: true)
                                .WithPercentOfMaxHp(0.05f),
                            pick: TargetSelector.SingleLowestHp),
                    },
                },
            },

            // 20 小雪：全场禁止治疗；所有护盾效果 +50%
            new TermEntry
            {
                SolarIndex = 20,
                Weather = new WeatherDef
                {
                    Id = "solar_xiaoxue", NodeName = "小雪", BuffName = "虹藏不见",
                    Element = Element.Water,
                    BanHeal = true,
                    ShieldGainMul = 1.5f,
                },
            },

            // 21 大雪：全场速度 -20%；出手顺序速度差按 1.5 倍计算
            //     取舍：速度差 ×1.5 对**排序**是恒等变换（所有间隔同比例放大不改次序），
            //     它真正起作用的场合（追击/闪避阈值类机制）还没落地，故先只接速度乘数；
            //     等那类机制出现时再消费 SpeedDiffMultiplier（届时加字段即可）。
            new TermEntry
            {
                SolarIndex = 21,
                Weather = new WeatherDef
                {
                    Id = "solar_daxue", NodeName = "大雪", BuffName = "闭塞成冬",
                    Element = Element.Water,
                    SpeedMulPlayer = 0.8f,
                    SpeedMulEnemy = 0.8f,
                },
            },

            // 22 冬至：全场速度 +20%；首回合内，先手方造成的伤害 +50%
            //     （先手方 = 第 1 回合出手序列第一个单位所在阵营，序列生成时落笔）
            new TermEntry
            {
                SolarIndex = 22,
                Weather = new WeatherDef
                {
                    Id = "solar_dongzhi", NodeName = "冬至", BuffName = "日短至·极阴",
                    Element = Element.Water,
                    SpeedMulPlayer = 1.2f,
                    SpeedMulEnemy = 1.2f,
                    FirstTurnDamageMul = 1.5f,
                },
            },

            // 24 大寒（冬末土节点）：每回合结束全场叠加 1 层「冰蚀」
            //     （每层每回合 1.5% 最大生命伤害）；火技融冰的命中钩子待接
            new TermEntry
            {
                SolarIndex = 24,
                Weather = new WeatherDef
                {
                    Id = "solar_dahan", NodeName = "大寒", BuffName = "寒气之逆极",
                    Element = Element.Water,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Status(TargetSelector.AllAllies,
                                StatusCatalog.IceErosion, 1, 99)
                                .WithPercentOfMaxHp(0.015f)),
                    },
                },
            },

            // ---- 仍待接的 8 条：机制不在"场地修正/原子"的表达范围内（事件钩子型），
            //      接入时按 GDD 原文补（solar_terms.json）。 ----
            new TermEntry { SolarIndex = 3,  Untreated = "惊蛰：我方阵亡后留「虫卵」2 回合后 30% 生命复活（死亡钩子 + 复活原子，STEP 1 已留显式留痕）" },
            new TermEntry { SolarIndex = 5,  Untreated = "清明：我方减益剩余回合 -1；免疫混乱与沉默（StatusCatalog 尚无这两个状态，免疫通路待建）" },
            new TermEntry { SolarIndex = 7,  Untreated = "立夏：我方攻击附带 20% 攻击力的火属性灼烧（攻击命中钩子）" },
            new TermEntry { SolarIndex = 9,  Untreated = "芒种：我方暴击追加 50% 攻击力追击，每次行动限 1 次（暴击钩子）" },
            new TermEntry { SolarIndex = 14, Untreated = "处暑：击杀时溢出伤害 100% 转全队护盾（击杀钩子）" },
            new TermEntry { SolarIndex = 17, Untreated = "寒露：每 3 回合我方全体获得「凝神」（下一次技能 CD 立即 -2；需要新状态 + CD 即减机制）" },
            new TermEntry { SolarIndex = 19, Untreated = "立冬：受击时 30% 概率被冻结 1 回合（受击钩子 + 概率，冻结状态已有）" },
            new TermEntry { SolarIndex = 23, Untreated = "小寒：每回合结束我方速度最高者获得一次额外普攻（回合末行动钩子）" },
        };

        // ====================================================================
        //  天气技（GDD 3.4 逆天改势）——覆盖 3 回合后回归原天时
        // ====================================================================

        private static readonly WeatherSkillDef[] WeatherSkills =
        {
            // 祷雨（水）：全场回复 2%/回合 + 冰蚀 +1/回合 + 火伤 -20%
            new WeatherSkillDef
            {
                Id = "wskill_daoyu", Name = "祷雨",
                Brings = new WeatherDef
                {
                    Id = "weather_rain", NodeName = "雨", BuffName = "祷雨",
                    Element = Element.Water,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Heal(TargetSelector.AllAllies, 0f, 0.02f)),
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Status(TargetSelector.AllAllies,
                                StatusCatalog.IceErosion, 1, 99)
                                .WithPercentOfMaxHp(0.015f)),
                    },
                    FireDamageMul = 0.8f,
                },
            },

            // 祈晴（火）：全场 2%/回合灼烧 + 火伤 +25%。
            // Element=Fire 必填 —— 逆天时判定靠它（水节气用它 ⇒ 反噬）
            new WeatherSkillDef
            {
                Id = "wskill_qiqing", Name = "祈晴",
                Brings = new WeatherDef
                {
                    Id = "weather_sunny", NodeName = "晴", BuffName = "祈晴",
                    Element = Element.Fire,
                    TurnEnd = new[]
                    {
                        new WeatherEffect(WeatherScope.Both,
                            EffectAtom.Damage(TargetSelector.AllAllies, 0f, trueDamage: true)
                                .WithPercentOfMaxHp(0.02f)),
                    },
                    FireDamageMul = 1.25f,
                },
            },

            // 召风（金）：全场速度 +15%；命中率 -8% 与"远程单位伤害 +15%"待接
            //（命中判定与"远程"兵种概念都还不存在）。Element 必填保逆天时判定可用。
            new WeatherSkillDef
            {
                Id = "wskill_zhaofeng", Name = "召风",
                Brings = new WeatherDef
                {
                    Id = "weather_wind", NodeName = "风", BuffName = "召风",
                    Element = Element.Metal,
                    SpeedMulPlayer = 1.15f,
                    SpeedMulEnemy = 1.15f,
                },
            },

            // 移山（土）：AOE 伤害减免 30%（全场 AOE ×0.7）；受击反弹 10% 待接（受击钩子）
            new WeatherSkillDef
            {
                Id = "wskill_yishan", Name = "移山",
                Brings = new WeatherDef
                {
                    Id = "weather_dust", NodeName = "尘", BuffName = "移山",
                    Element = Element.Earth,
                    AoeDamageMul = 0.7f,
                },
            },
        };
    }

    /// <summary>EffectAtom 是 struct，补一个链式填 PercentOfMaxHp 的写法（只在本目录用）。</summary>
    internal static class WeatherAtomExt
    {
        public static EffectAtom WithPercentOfMaxHp(this EffectAtom atom, float percent)
        {
            atom.PercentOfMaxHp = percent;
            return atom;
        }
    }
}
