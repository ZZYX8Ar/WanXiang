// ============================================================================
//  万相 · 天时内容目录（编辑器侧，STEP 3 第一批翻译）
//  ---------------------------------------------------------------------------
//  把 GDD 3.3 的 fieldBuff 自然语言逐条翻译成 WeatherDef。**本批先翻 7 条
//  代表性节气 + 4 条天气技**，建立"数据 → 战斗结算 → 自检"的完整通路；
//  其余 17 条保留 GDD 原文（Untreated），后续按同一模式补齐。
//
//  翻译时的取舍（都已记录在各条注释）：
//  - 小暑"受 3% 灼烧"：本质是固定 % 真伤，用 Damage(TrueDamage) 原子而不是
//    灼烧状态 —— 灼烧状态的 dot 在施加瞬间按施法者攻击折算，天时无施法者。
//  - 夏至只翻 DamageAllMultiplier；冬至/大雪的速度修正、小雪护盾 +50%、
//    谷雨上限等规则修正后续逐条接（每接一条过指纹回归）。
//  - 天气技"祷雨/祈晴"的属性伤害修正（火伤 ±20/25%）待接，但 **Element
//    字段必须先填** —— 逆天时判定靠它（水节气用祈晴 ⇒ 火被水克 ⇒ 反噬）。
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

            // 20 小雪：全场禁止治疗（护盾效果 +50% 是规则修正，待接，见文件头）
            new TermEntry
            {
                SolarIndex = 20,
                Weather = new WeatherDef
                {
                    Id = "solar_xiaoxue", NodeName = "小雪", BuffName = "虹藏不见",
                    Element = Element.Water,
                    BanHeal = true,
                },
            },

            // 22 夏至：全场造成的伤害 +25%，受到的伤害 +25%（等价全场伤害 ×1.25）
            new TermEntry
            {
                SolarIndex = 22,
                Weather = new WeatherDef
                {
                    Id = "solar_xiazhi", NodeName = "夏至", BuffName = "日长至·极阳",
                    Element = Element.Fire,
                    DamageAllMultiplier = 1.25f,
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

            // ---- 未翻译的 17 条：接入时按上面同一模式补（GDD 原文见 solar_terms.json） ----
            new TermEntry { SolarIndex = 2,  Untreated = "每回合结束全体回复 3% 最大生命；治疗溢出量的 50% 转化为护盾" },
            new TermEntry { SolarIndex = 3,  Untreated = "我方单位阵亡后留下「虫卵」，2 回合后以 30% 生命复活" },
            new TermEntry { SolarIndex = 4,  Untreated = "敌我双方每回合各回复 2% 最大生命；超过 12 回合回复量翻倍" },
            new TermEntry { SolarIndex = 5,  Untreated = "我方所有减益剩余回合 -1；免疫混乱与沉默" },
            new TermEntry { SolarIndex = 7,  Untreated = "我方所有攻击附带 20% 攻击力的火属性灼烧" },
            new TermEntry { SolarIndex = 8,  Untreated = "我方技能 CD 推进速度 +30%" },
            new TermEntry { SolarIndex = 9,  Untreated = "我方暴击时追加一次 50% 攻击力的追击" },
            new TermEntry { SolarIndex = 10, Untreated = "大雪/冬至等速度修正类" },
            new TermEntry { SolarIndex = 12, Untreated = "火属性伤害 -30%，土属性伤害 +30%；火单位灼烧减半" },
            new TermEntry { SolarIndex = 13, Untreated = "我方暴击伤害 +40%" },
            new TermEntry { SolarIndex = 14, Untreated = "击杀时溢出伤害的 100% 转化为全队护盾" },
            new TermEntry { SolarIndex = 15, Untreated = "我方命中率 -10%，暴击伤害 +30%" },
            new TermEntry { SolarIndex = 16, Untreated = "AOE 技能伤害 -40%，单体技能伤害 +25%" },
            new TermEntry { SolarIndex = 17, Untreated = "每 3 回合我方全体获得「凝神」" },
            new TermEntry { SolarIndex = 19, Untreated = "受击时 30% 概率被冻结 1 回合" },
            new TermEntry { SolarIndex = 21, Untreated = "全场速度 -20%；出手顺序速度差按 1.5 倍计算" },
            new TermEntry { SolarIndex = 23, Untreated = "每回合结束我方速度最高的单位获得一次额外普攻" },
        };

        // ====================================================================
        //  天气技（GDD 3.4 逆天改势）——覆盖 3 回合后回归原天时
        // ====================================================================

        private static readonly WeatherSkillDef[] WeatherSkills =
        {
            // 祷雨（水）：全场回复 2%/回合 + 冰蚀 +1/回合；火伤 -20% 规则修正待接
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
                },
            },

            // 祈晴（火）：全场 2%/回合灼烧；火伤 +25% 规则修正待接。
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
                },
            },

            // 召风（金）与移山（土）：效果全是规则修正（速度/命中/AOE/反弹），通路待接；
            // 先落属性占位，保证逆天时判定可用。
            new WeatherSkillDef
            {
                Id = "wskill_zhaofeng", Name = "召风",
                Brings = new WeatherDef
                {
                    Id = "weather_wind", NodeName = "风", BuffName = "召风",
                    Element = Element.Metal,
                },
            },
            new WeatherSkillDef
            {
                Id = "wskill_yishan", Name = "移山",
                Brings = new WeatherDef
                {
                    Id = "weather_dust", NodeName = "尘", BuffName = "移山",
                    Element = Element.Earth,
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
