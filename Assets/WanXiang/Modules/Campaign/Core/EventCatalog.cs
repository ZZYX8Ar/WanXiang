// ============================================================================
//  不期而遇 · 事件表（2026-11-01）
//  ---------------------------------------------------------------------------
//  用户："参考明日方舟的不期而遇，里面有一些选择"；同时把旧的天象/铸魂台/灵泉
//  并进这个节点。这里只做**数据 + 结算**，UI 由 CampaignPanel 之后接。
//  ⛔ 每个选项必须把代价写在 Text 里（StS 铁律：没有免费午餐）。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Campaign
{
    /// <summary>选项结果的种类（结算器按它改 RunState / 起雾）。</summary>
    public enum EventEffect
    {
        Nothing = 0,
        GainExp,          // Value = 经验
        GainEggs,         // Value = 灵卵
        HealTeamPct,      // Value = 回复百分比（100 = 满血）
        DamageTeamPct,    // Value = 全队损失百分比
        StartFog,         // Value = 起雾节数（复用现有瘴雾）
        GrantMaterial,    // Value = 进化素材数量
        GrantRelic,       // ScopeId = 遗物 id
        GrantMastery,     // 随机一只上阵异兽的随机技能 专精 +1
        Evolve,           // 随机一只未进化的上阵异兽 进化（路线随机）
        TurnDelta,        // Value = 回合上限变化（正/负）
        TurnLimit,        // Value = 本场"必须在 N 回合内通过"（写成限时，而不是减回合）
        PickEgg,          // 拾一枚灵兽蛋（Value 不用；2 个节点后出壳，见 RunState.EggHatchIn）
        SpendEggs,        // 代价专用：扣灵卵（Value = 枚数）
        GrantGrowth,      // 本局永久成长 +Value%（BattleRequestFactory 消费 RunState.GrowthPct）
        Gamble,           // 按 ScopeId 指向的随机表掷一次（gamble_tables.csv）
    }

    public sealed class EventChoice
    {
        public string Text;            // 必须含代价（"扣 10% 全队生命"）
        public EventEffect Effect;
        public int Value;
        public string ScopeId;         // GrantRelic 用 / Gamble 的随机表 id

        /// <summary>通用代价：先应用 CostEffect，再应用 Effect（文字里必须写清代价）。</summary>
        public EventEffect CostEffect = EventEffect.Nothing;
        public int CostValue;
    }

    public sealed class EventDef
    {
        public string Id;
        public string Title;
        public string Story;
        public EventChoice[] Choices;
    }

    public static class EventCatalog
    {
        private static EventDef[] _table = Build();

        /// <summary>外部（CSV）注入事件表；传 null 恢复内置。</summary>
        public static void Install(EventDef[] table)
        {
            if (table != null && table.Length > 0) _table = table;
        }

        public static EventDef[] All => _table;

        public static EventDef Get(string id)
        {
            if (string.IsNullOrEmpty(id) || All == null) return null;
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        private static EventDef[] Build()
        {
            var L = new List<EventDef>(10);

            L.Add(new EventDef
            {
                Id = "ev_fog_guest", Title = "雾中来客",
                Story = "雾里有人影招手。它说前面三条路它都走过，只是现在看不清了。",
                Choices = new[]
                {
                    new EventChoice { Text = "听它的（未来 3 节不可见并重掷；雾中首节点奖励 +20%，立即得 1 进化素材）",
                                      Effect = EventEffect.StartFog, Value = 3 },
                    new EventChoice { Text = "不信，绕开（安全，但本幕少一次素材机会）", Effect = EventEffect.Nothing },
                    new EventChoice { Text = "追问它的来历（扣 10% 全队生命，换 1 个规则遗物）",
                                      Effect = EventEffect.DamageTeamPct, Value = 10, ScopeId = "r_rule_no_hit" },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_forge", Title = "熔炉余温",
                Story = "炉子还热着。风箱自己会动，像是在等谁往里扔点什么。",
                Choices = new[]
                {
                    new EventChoice { Text = "投一件素材进去（专精待接，先折算 60 灵卵）", Effect = EventEffect.GainEggs, Value = 60 },
                    new EventChoice { Text = "把手伸进去（全队 -10% 生命，换 40 灵卵）",
                                      Effect = EventEffect.DamageTeamPct, Value = 10 },
                    new EventChoice { Text = "什么都不做（拿 20 灵卵）", Effect = EventEffect.GainEggs, Value = 20 },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_dry_boat", Title = "旱地行舟",
                Story = "一条船搁在干裂的河床上。船夫说：风还在，只是你得决定要不要借。",
                Choices = new[]
                {
                    // ★ 2026-11-01 用户："减回合不如写成『在 N 回合内通过』"。改成限时条款：
                    //   玩家看的是"20 回合内打完"，比"上限 -10"直观，也更有挑战感。
                    new EventChoice { Text = "借风疾行（限时：本场须在 20 回合内通过；奖励 +60%）", Effect = EventEffect.TurnLimit, Value = 20 },
                    new EventChoice { Text = "稳扎稳打（本场回合上限 +6，奖励不变）", Effect = EventEffect.TurnDelta, Value = 6 },
                    new EventChoice { Text = "弃舟步行（全队回复 25% 生命）", Effect = EventEffect.HealTeamPct, Value = 25 },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_grave", Title = "无名墓碑",
                Story = "碑上没有名字，只有一道很深的爪痕。土是新的。",
                Choices = new[]
                {
                    new EventChoice { Text = "挖开（得 1 规则遗物，但全队 -15% 生命）",
                                      Effect = EventEffect.DamageTeamPct, Value = 15, ScopeId = "r_trait_sharpedge" },
                    new EventChoice { Text = "拜一拜（全队回复 20% 生命）", Effect = EventEffect.HealTeamPct, Value = 20 },
                    new EventChoice { Text = "走开", Effect = EventEffect.Nothing },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_bridge", Title = "断桥",
                Story = "桥断了一半。对岸有你要的东西，但水声不太对。",
                Choices = new[]
                {
                    new EventChoice { Text = "涉水过去（全队 -12% 生命，+120 经验）",
                                      Effect = EventEffect.DamageTeamPct, Value = 12 },
                    new EventChoice { Text = "绕远路（多打一场，+150 经验）", Effect = EventEffect.GainExp, Value = 150 },
                    new EventChoice { Text = "回头（无事发生）", Effect = EventEffect.Nothing },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_bet", Title = "赌局",
                Story = "三颗骰子，一只空碗。庄家不看你，只看碗。",
                Choices = new[]
                {
                    new EventChoice { Text = "押 30 灵卵（赢则翻三倍，输则清空）", Effect = EventEffect.GainEggs, Value = 60 },
                    new EventChoice { Text = "押 1 素材（赢了得 2 个素材）", Effect = EventEffect.GrantMaterial, Value = 1 },
                    new EventChoice { Text = "不赌", Effect = EventEffect.Nothing },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_hurt_beast", Title = "受伤的异兽",
                Story = "它缩在石头后面，腿上有旧伤。你伸手，它没有躲。",
                Choices = new[]
                {
                    new EventChoice { Text = "救治（消耗 1 素材，得 1 只随机异兽）",
                                      Effect = EventEffect.GrantMaterial, Value = -1 },
                    new EventChoice { Text = "留些吃的（全队回复 15% 生命）", Effect = EventEffect.HealTeamPct, Value = 15 },
                    new EventChoice { Text = "不管", Effect = EventEffect.Nothing },
                }
            });

            L.Add(new EventDef
            {
                Id = "ev_echo", Title = "旧日回声",
                Story = "山谷把你的声音重复了三次，第三次听起来像别人的。",
                Choices = new[]
                {
                    new EventChoice { Text = "听第一声（+150 经验）", Effect = EventEffect.GainExp, Value = 150 },
                    new EventChoice { Text = "听第二声（全队回复 30% 生命）", Effect = EventEffect.HealTeamPct, Value = 30 },
                    new EventChoice { Text = "听第三声（+1 进化素材，全队 -8% 生命）",
                                      Effect = EventEffect.DamageTeamPct, Value = 8 },
                }
            });

            return L.ToArray();
        }
    }
}
