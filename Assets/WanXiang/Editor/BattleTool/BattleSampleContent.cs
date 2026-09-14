// ============================================================================
//  万相 · 编辑器工具 · 战斗自检用的灰盒内容
//  ---------------------------------------------------------------------------
//  ⚠ 这不是"游戏内容"，是**验证脚手架**。它和 Docs/Design/data/beasts.json 里
//    那 30 只正式异兽没有任何关系 —— 那 30 只的技能是 GDD 的自然语言描述，
//    逐条翻译是后续工作（见 Defs.cs 文件头对范围的说明）。
//
//  这里按**职业**造 5 套技能组，覆盖战斗核心里全部会被用到的原子与目标形状：
//
//      御 Guard   → 单体伤害 / 自身护盾 / 群体伤害 + 破防
//      攻 Striker → 单体伤害 / 打最高攻 / 多段乱击
//      术 Caster  → 单体伤害 / 群体持续伤害 / 群体伤害 + 湿
//      辅 Support → 单体伤害 / 单体治疗 / 群体治疗 + 群体护盾
//      疾 Swift   → 单体伤害 / 二连 / 单体高倍 + 霜
//
//  这样做的好处：自检不需要依赖任何一张配置表，**改技能翻译不会让自检变红**。
//  正式技能进表之后，这个文件应该只留下"造标准单位"的那一个函数。
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Editor.BattleTool
{
    public static class BattleSampleContent
    {
        /// <summary>造一只灰盒异兽。同 role 的技能组固定，Element 只影响五行系数。</summary>
        public static BeastDef Make(string id, string name, Element element, RoleType role,
                                    Rarity rarity = Rarity.Rare)
        {
            var def = new BeastDef
            {
                Id = id,
                DisplayName = name,
                Element = element,
                Role = role,
                Rarity = rarity,
                Source = "（脚手架，非正式内容）",
                Quote = "",
                Lore = "灰盒自检单位。",
                Codex = "灰盒自检单位。",
                Trait = new TraitDef { Name = "无", Description = "灰盒单位没有特性。" },
            };

            BuildSkills(def);
            return def;
        }

        /// <summary>五行同职业的 5 只，名字形如「木甲」。自检里造一整队用。</summary>
        public static BeastDef[] TeamOf(Element element, RoleType role, int count, string idPrefix)
        {
            var list = new BeastDef[count];
            for (int i = 0; i < count; i++)
                list[i] = Make($"{idPrefix}{i}", $"{CnNameOf(element)}{i + 1}", element, role);
            return list;
        }

        public static string CnNameOf(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木甲";
                case Element.Fire: return "火甲";
                case Element.Earth: return "土甲";
                case Element.Metal: return "金甲";
                case Element.Water: return "水甲";
                default: return "无甲";
            }
        }

        // ================================================================
        //  灰盒场景
        //  ----------------------------------------------------------------
        //  自检命令与灰盒窗口共用这三套阵容 —— 两处各写一份必然分叉，
        //  然后就会出现"自检跑的局面和眼睛看的不一样"。
        // ================================================================

        public const int ScenarioCount = 3;

        public static string ScenarioName(int index)
        {
            switch (index)
            {
                case 0: return "1v1 · 木 vs 火（GDD 7.1 第 0 步）";
                case 1: return "3v3 · 相生 vs 相冲";
                default: return "5v5 · 基准局（四条规则全走过）";
            }
        }

        public static BattleState BuildScenario(int index, ulong seed, BattleConfig cfg = null)
        {
            cfg = cfg ?? BattleConfig.Default;

            if (index == 0)
            {
                return BattleFactory.Create(cfg, seed,
                    new[] { DeployEntry.Player(Make("w7", "木七", Element.Wood, RoleType.Striker), 4) },
                    new[] { DeployEntry.Enemy(Make("f7", "火七", Element.Fire, RoleType.Striker), 4) });
            }

            if (index == 1)
            {
                // 我方刻意排成一条相生链（木→火→土），敌方刻意排成一对相冲
                return BattleFactory.Create(cfg, seed,
                    new[]
                    {
                        DeployEntry.Player(Make("p1", "木甲", Element.Wood,  RoleType.Guard),  0),
                        DeployEntry.Player(Make("p2", "火乙", Element.Fire,  RoleType.Caster), 1),
                        DeployEntry.Player(Make("p3", "土丙", Element.Earth, RoleType.Support), 4),
                    },
                    new[]
                    {
                        DeployEntry.Enemy(Make("e1", "敵木", Element.Wood,  RoleType.Striker), 0),
                        DeployEntry.Enemy(Make("e2", "敵金", Element.Metal, RoleType.Guard),   1),
                        DeployEntry.Enemy(Make("e3", "敵水", Element.Water, RoleType.Caster),  4),
                    });
            }

            // 5v5 基准局：我方三对相生无相冲；敌方相生相冲掺杂，且有一对相冲落在中宫上被平息
            return BattleFactory.Create(cfg, seed,
                new[]
                {
                    DeployEntry.Player(Make("pw", "木甲", Element.Wood,  RoleType.Guard),   0),
                    DeployEntry.Player(Make("ps", "木乙", Element.Wood,  RoleType.Striker), 1),
                    DeployEntry.Player(Make("pc", "火丙", Element.Fire,  RoleType.Caster),  4),
                    DeployEntry.Player(Make("pp", "土丁", Element.Earth, RoleType.Support), 7),
                    DeployEntry.Player(Make("pf", "金戊", Element.Metal, RoleType.Swift),   8),
                },
                new[]
                {
                    DeployEntry.Enemy(Make("eg", "敵金", Element.Metal, RoleType.Guard),    0),
                    DeployEntry.Enemy(Make("es", "敵水", Element.Water, RoleType.Striker),  1),
                    DeployEntry.Enemy(Make("ec", "敵火", Element.Fire,  RoleType.Caster),   4),
                    DeployEntry.Enemy(Make("ep", "敵土", Element.Earth, RoleType.Support),  7),
                    DeployEntry.Enemy(Make("ef", "敵水", Element.Water, RoleType.Swift),    8),
                });
        }

        private static void BuildSkills(BeastDef def)
        {
            Element el = def.Element;
            string sid = def.Id;

            switch (def.Role)
            {
                case RoleType.Guard:
                    def.Basic = Skill(sid + "_b", "扛击", SkillType.Basic, 0, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.00f));
                    def.Active = Skill(sid + "_a", "磐石", SkillType.Active, 3, el,
                        EffectAtom.Shield(TargetSelector.Self, 0.80f));
                    def.Ultimate = Skill(sid + "_u", "撼地", SkillType.Ultimate, 6, el,
                        EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f),
                        EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.ArmorBreak, 2, 2));
                    break;

                case RoleType.Striker:
                    def.Basic = Skill(sid + "_b", "劈斩", SkillType.Basic, 0, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.00f));
                    def.Active = Skill(sid + "_a", "斩首", SkillType.Active, 3, el,
                        EffectAtom.Damage(TargetSelector.SingleHighestAtk, 1.80f));
                    def.Ultimate = Skill(sid + "_u", "乱刃", SkillType.Ultimate, 5, el,
                        EffectAtom.Damage(TargetSelector.RandomEnemyMultiHit, 0.60f, hits: 5));
                    break;

                case RoleType.Caster:
                    def.Basic = Skill(sid + "_b", "术击", SkillType.Basic, 0, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.90f));
                    def.Active = Skill(sid + "_a", "燎原", SkillType.Active, 3, el,
                        EffectAtom.Dot(TargetSelector.AllEnemies, StatusCatalog.Burn, 0.04f, 1, 3));
                    def.Ultimate = Skill(sid + "_u", "倾覆", SkillType.Ultimate, 6, el,
                        EffectAtom.Damage(TargetSelector.AllEnemies, 1.30f),
                        EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.Wet, 1, 3));
                    break;

                case RoleType.Support:
                    def.Basic = Skill(sid + "_b", "点刺", SkillType.Basic, 0, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.70f));
                    def.Active = Skill(sid + "_a", "回春", SkillType.Active, 3, el,
                        EffectAtom.Heal(TargetSelector.SingleLowestHp, 0.90f));
                    def.Ultimate = Skill(sid + "_u", "庇护", SkillType.Ultimate, 6, el,
                        EffectAtom.Heal(TargetSelector.AllAllies, 0.50f),
                        EffectAtom.Shield(TargetSelector.AllAllies, 0.50f),
                        EffectAtom.Dispel(TargetSelector.AllAllies));
                    break;

                case RoleType.Swift:
                default:
                    def.Basic = Skill(sid + "_b", "疾刺", SkillType.Basic, 0, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.80f));
                    def.Active = Skill(sid + "_a", "连击", SkillType.Active, 3, el,
                        EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.20f, hits: 2));
                    def.Ultimate = Skill(sid + "_u", "绝影", SkillType.Ultimate, 5, el,
                        EffectAtom.Damage(TargetSelector.RandomEnemy, 1.40f),
                        EffectAtom.Status(TargetSelector.RandomEnemy, StatusCatalog.Frost, 2, 3));
                    break;
            }
        }

        private static SkillDef Skill(string id, string name, SkillType type, int cd, Element el,
                                      params EffectAtom[] effects)
        {
            return new SkillDef
            {
                Id = id,
                Name = name,
                Type = type,
                Cd = cd,
                Element = el,
                PrimaryTarget = effects.Length > 0 ? effects[0].Target : TargetSelector.SingleLowestHp,
                Effects = effects,
                Description = "（脚手架技能）",
            };
        }
    }
}
