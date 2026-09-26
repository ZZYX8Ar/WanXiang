// ============================================================================
//  万相 · 天象与异闻事件表（GDD v1.1 §4.6 / §4.7）
//  ---------------------------------------------------------------------------
//  天象 = 强力增益 + 明确副作用，"不提供纯赚，只提供往哪个方向押宾"（玩家可拒绝拿 1 灵卵）；
//  异闻 = 选项有明确代价，承担叙事职能但叙事必须付在数值上（可拒绝拿 1 灵卵）。
//
//  ⚠ 本文件是**数据**。应用（把"全队攻击 +25%"这类效果真正落进战斗）分两档：
//    · 灵卵类 / 文本类 → RunDriver 立即结算（已接）；
//    · 数值增益类 → 需要跨场的局内成长模型（队伍继承/每回合损血钩子），接 UI 时落，
//      每条的"落点档位"写在字段里（Tier: Eggs / Text / Combat）。
// ============================================================================

namespace WanXiang.Campaign
{
    public enum FieldEventTier
    {
        Eggs = 0,    // 只动灵卵/文本 —— RunDriver 已可结算
        Text = 1,    // 带文本产出（如"本幕路径多显示一个节点信息"）—— 已可结算
        Combat = 2,  // 战斗/成长数值 —— 需要局内成长模型，接 UI 时落
    }

    public struct OmenDef
    {
        public string Id;
        public string Name;
        public string Buff;       // 增益
        public string Drawback;   // 副作用（"明确负面"，不是装饰）
        public string Archetype;  // 适合的流派
    }

    public struct TaleOption
    {
        public string Label;      // 选项
        public string Result;     // 结果
    }

    public struct TaleDef
    {
        public string Id;
        public string Story;      // 典籍轶事
        public TaleOption[] Options;
    }

    public static class FieldEvents
    {
        // ---- 天象（§4.6，三选一，每条增益都配一条负面）----
        public static readonly OmenDef[] Omens =
        {
            new OmenDef
            {
                Id = "chichong", Name = "赤乌当空",
                Buff = "我方全体攻击 +25%，暴击率 +12%",
                Drawback = "我方全体每回合损失 3% 最大生命（不可被治疗抵消）",
                Archetype = "速攻 / 暴击流",
            },
            new OmenDef
            {
                Id = "xuanbing", Name = "玄冰封川",
                Buff = "我方全体减伤 +20%，受击时 20% 概率冻结攻击者 1 回合",
                Drawback = "我方全体速度 -25%，技能 CD 推进 -15%",
                Archetype = "守御 / 控制流",
            },
            new OmenDef
            {
                Id = "fengbo", Name = "风伯开道",
                Buff = "我方全体速度 +30%，首回合伤害 +40%",
                Drawback = "我方全体命中 -12%，且每场战斗第 1 回合无法使用绝技",
                Archetype = "先手 / 连击流",
            },
            new OmenDef
            {
                Id = "tugao", Name = "土膏脉起",
                Buff = "我方全体最大生命 +30%，治疗与护盾效果 +25%",
                Drawback = "我方全体攻击 -15%，造成的灼烧/冰蚀层数减半",
                Archetype = "续航 / 反伤流",
            },
        };

        // ---- 异闻（§4.7，三选一，可拒绝拿 1 灵卵）----
        public static readonly TaleDef[] Tales =
        {
            new TaleDef
            {
                Id = "hanba", Story = "路边跪着一名青衣女子，她所过之处草木尽枯。她说：带我走一程，我给你一样东西。",
                Options = new[]
                {
                    new TaleOption { Label = "收留她", Result = "获得火属性异兽「旱魃」，但全队治疗效果永久 -15%" },
                    new TaleOption { Label = "绕路而行", Result = "全队回复 30% 生命，但本幕下一场战斗敌方攻击 +15%" },
                    new TaleOption { Label = "递水给她", Result = "获得 2 枚灵卵，本节点不获得其他奖励" },
                },
            },
            new TaleDef
            {
                Id = "qingqiu", Story = "雾里有婴儿的哭声。灌灌在你肩头低鸣，像是在提醒什么。",
                Options = new[]
                {
                    new TaleOption { Label = "循声而去", Result = "获得 1 个灵卵，但队伍中生命最低者失去 30% 当前生命" },
                    new TaleOption { Label = "闭目不听", Result = "获得 1 个技能草稿三选一，且本幕免疫混乱" },
                    new TaleOption { Label = "退回原路", Result = "获得 1 枚灵卵，本幕路径上多显示一个节点信息" },
                },
            },
            new TaleDef
            {
                Id = "baize", Story = "一卷残破的《白泽图》挂在枯树上，上面画着一万一千五百二十种鬼。",
                Options = new[]
                {
                    new TaleOption { Label = "撕下书页", Result = "敌方全体的五行与技能在本幕永久可见，我方对其伤害 +8%" },
                    new TaleOption { Label = "整卷带走", Result = "获得 1 只金属性异兽「白泽」，但本幕无法进入灵市" },
                    new TaleOption { Label = "原样不动", Result = "获得 2 枚灵卵" },
                },
            },
            new TaleDef
            {
                Id = "iceWell", Story = "井口结着不化的冰。冰层下有东西在看着你，又像是在等你。",
                Options = new[]
                {
                    new TaleOption { Label = "凿冰取水", Result = "获得 1 个「天象」增益（自选），但全队受到 20% 最大生命的伤害" },
                    new TaleOption { Label = "封印井口", Result = "本幕敌方不会获得任何增益" },
                    new TaleOption { Label = "取走冰块", Result = "获得 3 枚灵卵" },
                },
            },
        };
    }
}
