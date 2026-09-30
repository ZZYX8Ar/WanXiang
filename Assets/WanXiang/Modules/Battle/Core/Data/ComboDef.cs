// ============================================================================
//  连携技（ComboDef / ComboRules）—— 回合制 v2.1 P4 → v1.3 扩表
//  ---------------------------------------------------------------------------
//  设计（v2.1 §5 表）：两只特定异兽同时在场上 → 解锁双人合击。
//  消耗：双方各出 Value 点灵力；每场每条限用一次。
//  ★ 发动约束（用户 2026-09-27 定案）：连携主兽【必须站在九宫格中宫（中间格）】才能发动。
//
//  ⚠ v1.3 变更（用户 2026-09-29 定案）：
//    ① ComboEffect 由 3 种（WoodPulse/FireStorm/MetalBreak）扩到 **18 种**。
//       原来 50 条连携只能套用同样的 3 种效果，会非常单调。
//    ② 连携表由 3 条扩到 **50 条**（每只异兽各一条，见《新增异兽设计文档 v1.0》）。
//    ③ 伙伴判定放宽：原来只能"伙伴是什么**属性**"，现在**同时支持**
//       PartnerElement（属性）与 PartnerBeastId（指定异兽）。
//       判定顺序：先查指定异兽，再查属性 —— 两者都空 = 任意队友。
//
//  ⚠ 结算口径的唯一真源是 BattleSimulator.Combo.cs 的 ExecuteCombo；
//    预览口径是同一个文件的 PreviewComboTargets —— **两处必须一一对应**，
//    改一处不改另一处 = 九宫格高亮与实际打到的目标对不上（已踩过）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>
    /// 连携效果类型。每加一种，必须同时改
    /// BattleSimulator.ExecuteCombo（结算）与 PreviewComboTargets（高亮）。
    /// </summary>
    public enum ComboEffect
    {
        // ---- v2.1 原有 3 种 ----
        WoodPulse = 0,       // 全体敌方伤害 + 我方全体回复（按主兽攻击力）
        FireStorm = 1,       // 全体敌方伤害
        MetalBreak = 2,      // 最前排单体重击

        // ---- v1.3 新增 15 种 ----
        RandomTwoHit = 3,    // 对随机 2 名敌人各造成一次伤害
        RandomThreeHit = 4,  // 对随机 3 名敌人各造成一次伤害
        FrontShred = 5,      // 最前排单体伤害 + 降低其防御
        FrontTrue = 6,       // 最前排单体伤害 + 按目标最大生命追加真实伤害
        HighestAtkStrike = 7,// 对敌方最高攻击单位重击
        LowestHpExecute = 8, // 对当前生命最低的敌人伤害（若其带灼烧则伤害提升）
        AllAoeStatus = 9,    // 全体敌方伤害 + 全体铺减益
        AllAoeBurnStack = 10,// 全体敌方伤害 + 按命中人数叠加灼烧
        AllAoeDrainMp = 11,  // 全体敌方伤害 + 各自扣灵力转为我方
        AllAoeStripBuff = 12,// 全体敌方伤害 + 剥离每名目标 1 个增益
        TeamHealShield = 13, // 我方全体回复 + 附加护盾
        TeamShieldReflect = 14, // 我方全体附加护盾（护盾在，受击反伤）
        TeamHealCleanse = 15,// 我方全体回复 + 驱散全部减益
        TeamHealLostHp = 16, // 我方全体按已损失生命的比例回复
        RandomTwoAdvance = 17,// 对随机 2 名敌人伤害 + 推进自身行动条
    }

    public sealed class ComboDef
    {
        public string Id;
        public string Name;
        public string Note;
        /// <summary>主兽的 BeastDef.Id（由他发动）。</summary>
        public string HostId;
        /// <summary>伙伴需要达到的元素（"任何木属性异兽"）。为 None 时改看 PartnerBeastId。</summary>
        public Element PartnerElement;
        /// <summary>
        /// 伙伴的指定异兽 id。非空时**优先**于 PartnerElement 判定
        /// （v1.3 起用于"必须以某只兽为搭档"的连携）；两者都空 = 任意队友即可。
        /// </summary>
        public string PartnerBeastId;
        /// <summary>双方各出的灵力（结算时共扣 MpCost × 2）。</summary>
        public int MpCost = 2;
        public ComboEffect Effect;
        /// <summary>伤害系数（× 主兽攻击）。</summary>
        public float Power = 1.2f;
        /// <summary>附加参数：减防幅度 / 回复百分比 / 护盾系数等，按 Effect 解释。0 = 用该效果的默认值。</summary>
        public float Power2;
    }

    public static class ComboRules
    {
        // ================================================================
        //  连携表（50 条 = 每只异兽各一条）
        //  原版 3 条在 v2.1 就已实现（句芒 / 祝融 / 蓐收），其余 47 条为 v1.3 新增。
        //  ⚠ 数值必须与《新增异兽设计文档 v1.0》§4b / §5 逐条对齐；
        //    改这里请同时跑 ComboSelfTest（编辑器菜单）核对。
        // ================================================================
        private static readonly ComboDef[] Table =
        {
            // ---------------- 原版 3 条（v2.1 P4） ----------------
            new ComboDef { Id = "combo_qingyang", Name = "青阳共鸣",
                Note = "对全体敌方造成木伤并为我方全体回复 15% 生命",
                HostId = "jumang", PartnerElement = Element.Wood,
                Effect = ComboEffect.WoodPulse, Power = 1.2f },
            new ComboDef { Id = "combo_yanhai", Name = "炎海焚天",
                Note = "对全体敌方造成火伤",
                HostId = "zhurong", PartnerElement = Element.Fire,
                Effect = ComboEffect.FireStorm, Power = 1.1f },
            new ComboDef { Id = "combo_jinge", Name = "金戈断流",
                Note = "对最前排单体造成 220% 金属性伤害",
                HostId = "rushou", PartnerElement = Element.Metal,
                Effect = ComboEffect.MetalBreak, Power = 2.2f },

            // ---------------- 旧 30 只 · 新增 27 条 ----------------
            // 木 · 青丘
            new ComboDef { Id = "combo_jiuweihu", Name = "青丘·幻狐",
                Note = "对随机 3 名敌人各造成 185% 木伤并附加【幻惑】（命中 -15%，1 回合）",
                HostId = "jiuweihu", PartnerElement = Element.Wood,
                Effect = ComboEffect.RandomThreeHit, Power = 1.85f },
            new ComboDef { Id = "combo_guanguan", Name = "青丘·醒神",
                Note = "为我方全体回复 14% 生命并驱散 1 个负面状态",
                HostId = "guanguan", PartnerElement = Element.Wood,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.0f },
            new ComboDef { Id = "combo_migu", Name = "青丘·四照",
                Note = "为我方全体附加护盾（=防御 180%），并在 2 回合内减伤 12%",
                HostId = "migu", PartnerElement = Element.Wood,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.05f },
            new ComboDef { Id = "combo_lushu", Name = "青丘·宜子",
                Note = "为我方全体回复 15% 生命，木属性队友额外 +8% 攻击",
                HostId = "lushu", PartnerElement = Element.Wood,
                Effect = ComboEffect.TeamHealShield, Power = 1.0f },
            new ComboDef { Id = "combo_xingxing", Name = "青丘·善走",
                Note = "对随机 2 名敌人各造成 170% 木伤，并使自身行动条前进 25%",
                HostId = "xingxing", PartnerElement = Element.Wood,
                Effect = ComboEffect.RandomTwoAdvance, Power = 1.70f },

            // 火 · 炎狱
            new ComboDef { Id = "combo_zhulong", Name = "炎狱·烛九阴",
                Note = "对单体造成 190% 火伤并附加 2 层【灼烧】",
                HostId = "zhulong", PartnerElement = Element.Fire,
                Effect = ComboEffect.LowestHpExecute, Power = 1.90f },
            new ComboDef { Id = "combo_bifang", Name = "炎狱·讹火",
                Note = "对全体敌人造成 130% 火伤，并各叠加 1 层【灼烧】",
                HostId = "bifang", PartnerElement = Element.Fire,
                Effect = ComboEffect.AllAoeBurnStack, Power = 1.32f },
            new ComboDef { Id = "combo_feiyi", Name = "炎狱·赤地",
                Note = "对随机 3 名敌人各造成 180% 火伤",
                HostId = "feiyi", PartnerElement = Element.Fire,
                Effect = ComboEffect.RandomThreeHit, Power = 1.80f },
            new ComboDef { Id = "combo_hanba", Name = "炎狱·不雨",
                Note = "对全体敌人造成 135% 火伤，并使其攻击 -10%（2 回合）",
                HostId = "hanba", PartnerElement = Element.Fire,
                Effect = ComboEffect.AllAoeStatus, Power = 1.35f },
            new ComboDef { Id = "combo_chongming", Name = "炎狱·重明",
                Note = "为我方全体回复 14% 生命，并在 1 回合内免疫灼烧伤害",
                HostId = "chongming", PartnerElement = Element.Fire,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.0f },

            // 土 · 厚土
            new ComboDef { Id = "combo_houtu", Name = "厚土·社稷",
                Note = "为我方全体附加护盾（=最大生命 20%），并减伤 15%（2 回合）",
                HostId = "houtu", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.10f },
            new ComboDef { Id = "combo_dijiang", Name = "厚土·浑敦",
                Note = "为我方全体回复 16% 生命并驱散全部负面状态",
                HostId = "dijiang", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.0f },
            new ComboDef { Id = "combo_taotie", Name = "厚土·贪食",
                Note = "对单体造成 195% 土伤；若击杀则我方全体灵力 +2",
                HostId = "taotie", PartnerElement = Element.Earth,
                Effect = ComboEffect.AllAoeDrainMp, Power = 1.95f },
            new ComboDef { Id = "combo_hundun", Name = "厚土·无别",
                Note = "对全体敌人造成 134% 土伤，并各扣除 2 点灵力",
                HostId = "hundun", PartnerElement = Element.Earth,
                Effect = ComboEffect.AllAoeDrainMp, Power = 1.34f },
            new ComboDef { Id = "combo_dangkang", Name = "厚土·大穰",
                Note = "为我方全体回复 15% 生命，并各获得 1 层【丰饶】（攻击 +8%）",
                HostId = "dangkang", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamHealShield, Power = 1.0f },
            new ComboDef { Id = "combo_luwu", Name = "厚土·昆仑",
                Note = "为我方全体附加护盾（=防御 200%）；自身 2 回合内防御 +30%",
                HostId = "luwu", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.05f },

            // 金 · 素金
            new ComboDef { Id = "combo_qiongqi", Name = "素金·穷奇",
                Note = "对单体造成 190% 金伤并剥离其 1 个增益",
                HostId = "qiongqi", PartnerElement = Element.Metal,
                Effect = ComboEffect.AllAoeStripBuff, Power = 1.90f },
            new ComboDef { Id = "combo_zheng", Name = "素金·破军",
                Note = "对随机 2 名敌人各造成 172% 金伤，并降低其 20% 防御（2 回合）",
                HostId = "zheng", PartnerElement = Element.Metal,
                Effect = ComboEffect.RandomTwoHit, Power = 1.72f },
            new ComboDef { Id = "combo_tiangou", Name = "素金·御凶",
                Note = "为我方全体附加护盾（=最大生命 18%），并减伤 12%（2 回合）",
                HostId = "tiangou", PartnerElement = Element.Metal,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.05f },
            new ComboDef { Id = "combo_qiuyu", Name = "素金·蜷缩",
                Note = "为我方全体附加护盾（=防御 230%），自身普攻本场无视 30% 护盾",
                HostId = "qiuyu", PartnerElement = Element.Metal,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.15f },
            new ComboDef { Id = "combo_baize", Name = "素金·万鬼录",
                Note = "为我方全体回复 13% 生命并驱散全部负面状态",
                HostId = "baize", PartnerElement = Element.Metal,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.0f },

            // 水 · 玄渊
            new ComboDef { Id = "combo_yuqiang", Name = "玄渊·北冥",
                Note = "对全体敌人造成 138% 水伤，并各扣除 2 点灵力",
                HostId = "yuqiang", PartnerElement = Element.Water,
                Effect = ComboEffect.AllAoeDrainMp, Power = 1.38f },
            new ComboDef { Id = "combo_yinglong", Name = "玄渊·决水",
                Note = "对单体造成 188% 水伤；若目标身上有负面状态则伤害 +30%",
                HostId = "yinglong", PartnerElement = Element.Water,
                Effect = ComboEffect.LowestHpExecute, Power = 1.88f },
            new ComboDef { Id = "combo_xiangliu", Name = "玄渊·九山",
                Note = "对随机 2 名敌人各造成 185% 水伤并附加 1 层【中毒】",
                HostId = "xiangliu", PartnerElement = Element.Water,
                Effect = ComboEffect.RandomTwoHit, Power = 1.85f },
            new ComboDef { Id = "combo_bashe", Name = "玄渊·吞象",
                Note = "对单体造成 175% 水伤并使其沉默 1 回合",
                HostId = "bashe", PartnerElement = Element.Water,
                Effect = ComboEffect.FrontShred, Power = 1.75f },
            new ComboDef { Id = "combo_fuzhu", Name = "玄渊·漫灌",
                Note = "对全体敌人造成 132% 水伤并降低其 20% 速度（1 回合）",
                HostId = "fuzhu", PartnerElement = Element.Water,
                Effect = ComboEffect.AllAoeStatus, Power = 1.32f },
            new ComboDef { Id = "combo_xuangui", Name = "玄渊·旋守",
                Note = "为我方全体回复 14% 生命并附加免控 1 回合",
                HostId = "xuangui", PartnerElement = Element.Water,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.0f },

            // ---------------- 新 20 只 · 20 条 ----------------
            // 木 · 青丘（新）
            new ComboDef { Id = "combo_mengji", Name = "青丘·疾影",
                Note = "对随机 2 名敌人各造成 170% 木属性伤害，并使其速度 -20%（1 回合）",
                HostId = "mengji", PartnerElement = Element.Wood,
                Effect = ComboEffect.RandomTwoAdvance, Power = 1.70f },
            new ComboDef { Id = "combo_zhuhuai", Name = "青丘·裂木",
                Note = "对最前排单体造成 205% 木属性伤害，无视目标 30% 防御",
                HostId = "zhuhuai", PartnerElement = Element.Wood,
                Effect = ComboEffect.FrontShred, Power = 2.05f },
            new ComboDef { Id = "combo_tianma", Name = "青丘·云径",
                Note = "为我方全体回复 12% 生命并附加护盾（=施法者攻击 60%），木属性队友额外 +5% 攻击",
                HostId = "tianma", PartnerElement = Element.Wood,
                Effect = ComboEffect.TeamHealShield, Power = 1.10f },
            new ComboDef { Id = "combo_xiegou", Name = "青丘·缠藤",
                Note = "对全体敌人造成 125% 木属性伤害并附加【缠绕】1 回合",
                HostId = "xiegou", PartnerElement = Element.Wood,
                Effect = ComboEffect.AllAoeStatus, Power = 1.25f },

            // 火 · 炎狱（新）
            new ComboDef { Id = "combo_jiuying", Name = "炎狱·九焰",
                Note = "对全体敌人造成 135% 火属性伤害，每命中 1 名目标额外叠加 1 层灼烧",
                HostId = "jiuying", PartnerElement = Element.Fire,
                Effect = ComboEffect.AllAoeBurnStack, Power = 1.35f },
            new ComboDef { Id = "combo_zhen", Name = "炎狱·鸩羽",
                Note = "对当前生命最低的敌人造成 160% 火属性伤害，若目标带灼烧则伤害 +40%",
                HostId = "zhen", PartnerElement = Element.Fire,
                Effect = ComboEffect.LowestHpExecute, Power = 1.60f },
            new ComboDef { Id = "combo_huoshu", Name = "炎狱·赤裘",
                Note = "为我方全体附加护盾（=施法者攻击 105%），被护盾保护的队友受击时给攻击者叠 1 层灼烧",
                HostId = "huoshu", PartnerElement = Element.Fire,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.05f },
            new ComboDef { Id = "combo_chiwen", Name = "炎狱·噬焰",
                Note = "对敌方最高攻击单位造成 195% 火属性伤害，并将其灼烧层数转移给相邻敌人",
                HostId = "chiwen", PartnerElement = Element.Fire,
                Effect = ComboEffect.HighestAtkStrike, Power = 1.95f },

            // 土 · 厚土（新）
            new ComboDef { Id = "combo_tulou", Name = "厚土·坠石",
                Note = "对最前排单体造成 200% 土属性伤害并降低其 25% 防御（2 回合）",
                HostId = "tulou", PartnerElement = Element.Earth,
                Effect = ComboEffect.FrontTrue, Power = 2.00f },
            new ComboDef { Id = "combo_changyou", Name = "厚土·先声",
                Note = "对随机 2 名敌人各造成 165% 土属性伤害，并使自身行动条前进 20%",
                HostId = "changyou", PartnerElement = Element.Earth,
                Effect = ComboEffect.RandomTwoAdvance, Power = 1.65f },
            new ComboDef { Id = "combo_haozhi", Name = "厚土·棘阵",
                Note = "为我方全体附加护盾（=施法者最大生命 20%），护盾存在期间受击反弹 20% 伤害",
                HostId = "haozhi", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.00f },
            new ComboDef { Id = "combo_shangao", Name = "厚土·安壤",
                Note = "为我方全体回复 15% 已损失生命，并清除全部负面状态",
                HostId = "shangao", PartnerElement = Element.Earth,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.00f },

            // 金 · 素金（新）
            new ComboDef { Id = "combo_xiezhi", Name = "素金·明断",
                Note = "对全体敌人造成 130% 金属性伤害，并剥离每名目标 1 个增益",
                HostId = "xiezhi", PartnerElement = Element.Metal,
                Effect = ComboEffect.AllAoeStripBuff, Power = 1.30f },
            new ComboDef { Id = "combo_pixiu", Name = "素金·纳金",
                Note = "对单体造成 210% 金属性伤害；若击杀则我方全体灵力 +3",
                HostId = "pixiu", PartnerElement = Element.Metal,
                Effect = ComboEffect.AllAoeDrainMp, Power = 2.10f },
            new ComboDef { Id = "combo_baiyuan", Name = "素金·剑势",
                Note = "对单体造成 175% 金属性伤害，连续命中同一目标时每次 +15%（最多 3 次）",
                HostId = "baiyuan", PartnerElement = Element.Metal,
                Effect = ComboEffect.HighestAtkStrike, Power = 1.75f },
            new ComboDef { Id = "combo_suanni", Name = "素金·镇山",
                Note = "为我方全体附加护盾（=施法者防御 220%），并在 2 回合内减伤 15%",
                HostId = "suanni", PartnerElement = Element.Metal,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.10f },

            // 水 · 玄渊（新）
            new ComboDef { Id = "combo_chiru", Name = "玄渊·涌流",
                Note = "对全体敌人造成 128% 水属性伤害并各扣除 2 点灵力，转为我方灵力",
                HostId = "chiru", PartnerElement = Element.Water,
                Effect = ComboEffect.AllAoeDrainMp, Power = 1.28f },
            new ComboDef { Id = "combo_shen", Name = "玄渊·蜃楼",
                Note = "为我方全体回复 13% 生命并附加免控 1 回合，水属性队友额外获得 10% 速度",
                HostId = "shen", PartnerElement = Element.Water,
                Effect = ComboEffect.TeamHealCleanse, Power = 1.00f },
            new ComboDef { Id = "combo_lingli", Name = "玄渊·穿流",
                Note = "为我方全体附加护盾（=施法者最大生命 22%），自身普攻在本场无视护盾",
                HostId = "lingli", PartnerElement = Element.Water,
                Effect = ComboEffect.TeamShieldReflect, Power = 1.15f },
            new ComboDef { Id = "combo_jiaoren", Name = "玄渊·珠潮",
                Note = "对随机 2 名敌人各造成 168% 水属性伤害，每段命中使自身叠加 1 层【潮】",
                HostId = "jiaoren", PartnerElement = Element.Water,
                Effect = ComboEffect.RandomTwoHit, Power = 1.68f },
        };

        public static System.Collections.Generic.IReadOnlyList<ComboDef> All => Table;

        public static ComboDef For(string id)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        /// <summary>该异兽（作为主兽）拥有的连携。</summary>
        public static ComboDef ForHost(string beastId)
        {
            if (string.IsNullOrEmpty(beastId)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].HostId == beastId) return Table[i];
            return null;
        }

        /// <summary>
        /// 该单位此刻可以发动的连携（他是主兽 + 主兽站中宫 + 场上有对应伙伴 + 双方灵力够）。
        /// </summary>
        public static System.Collections.Generic.List<ComboDef> AvailableFor(BattleState st, BattleUnit actor)
        {
            var list = new System.Collections.Generic.List<ComboDef>();
            if (actor == null || !actor.IsAlive || !actor.CanCastSkills) return list;

            for (int i = 0; i < Table.Length; i++)
            {
                var c = Table[i];
                if (actor.Def == null || actor.Def.Id != c.HostId) continue;      // 必须主兽发动
                if (!actor.Pos.IsCenter) continue;                                // ★ 主兽须站中宫
                if (st.UsedCombos.Contains(c.Id)) continue;                       // 每场每条限一次
                if (st.TeamMp < c.MpCost) continue;                               // 自己的灵力要够

                BattleUnit partner = FindPartner(st, actor, c);
                if (partner == null) continue;
                if (st.TeamMp < c.MpCost * 2) continue;                           // 伙伴的灵力也要够
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// 为什么不能发动（返回 null = 可以发动）。给界面显示"差什么"，让玩家不用猜。
        /// </summary>
        public static string WhyNot(BattleState st, BattleUnit actor)
        {
            if (actor == null || actor.Def == null) return "没有待令单位";
            if (!actor.IsAlive) return "单位已阵亡";

            var matched = ForHost(actor.Def.Id);
            if (matched == null)
                return actor.DisplayName + " 暂无连携技";

            if (!actor.Pos.IsCenter)
                return actor.DisplayName + " 是「" + matched.Name + "」主兽，但须站在九宫格中宫（中间格）才能发动连携";

            if (st.UsedCombos.Contains(matched.Id)) return "本场已发动过「" + matched.Name + "」";
            var partner = FindPartner(st, actor, matched);
            if (partner == null)
                return "需要一名" + PartnerName(matched) + "伙伴在场";
            if (st.TeamMp < matched.MpCost * 2)
                return "全队灵力不足 " + (matched.MpCost * 2) + " 点（当前 " + st.TeamMp + "）";
            return null;      // 可以发动
        }

        private static string PartnerName(ComboDef c)
        {
            return !string.IsNullOrEmpty(c.PartnerBeastId)
                ? "「" + c.PartnerBeastId + "」"
                : ElementName(c.PartnerElement) + "属性";
        }

        private static string ElementName(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return "任意属性";
            }
        }

        /// <summary>
        /// 找连携伙伴：优先按指定异兽，其次按属性，最后（两者都空）取任意其他存活友方。
        /// </summary>
        public static BattleUnit FindPartner(BattleState st, BattleUnit actor, ComboDef c)
        {
            bool byBeast = !string.IsNullOrEmpty(c.PartnerBeastId);
            bool byElement = c.PartnerElement != Element.None;
            var ours = st.UnitsOf(actor.Side);

            for (int i = 0; i < ours.Count; i++)
            {
                var u = ours[i];
                if (u == actor || !u.IsAlive || u.Def == null) continue;
                if (byBeast)
                {
                    if (u.Def.Id == c.PartnerBeastId) return u;
                    continue;
                }
                if (!byElement) return u;                       // 任意队友
                if (u.Def.Element == c.PartnerElement) return u;
            }
            return null;
        }
    }
}
