// ============================================================================
//  万相 · 融合 · 异兽内容导入器
//  ---------------------------------------------------------------------------
//  菜单：万相/融合/① 导入异兽内容。把 GDD 数据（Docs/Design/data/beasts.json，
//  由 extract_gdd_data.py 生成、自校验通过）变成 Unity 配置资产：
//
//      Assets/WanXiang/Config/Skills/Skill_<id>.asset      ×90（占位效果 + GDD 真值）
//      Assets/WanXiang/Config/Beasts/Beast_<id>.asset      ×30
//      Assets/WanXiang/Config/Souls/Soul_<id>.asset        ×30（SoulForge 派生）
//      Assets/WanXiang/Config/ContentCatalog.asset         ×1（内容单一入口）
//
//  设计要点：
//  1) **可重跑**：同 id 资产更新内容而不是新建；内容删了的残留资产只报告不删除
//     （删资产是不可逆操作，留给人判断）。
//  2) **灵魂走纯规则**：先组装 BeastDef，再调 SoulForge.Derive，最后把 SoulDef
//     写回 SoulConfigSO —— 资产内容 = 规则输出，不存在两份逻辑。
//  3) **技能效果是占位**：按「职业 + 技能类型」生成效果原子（与灰盒脚手架同口径），
//     GDD 的 90 条描述原文保留在 description，逐条翻译是后续内容工作（Defs.cs 文件头）。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Editor.FusionTool
{
    public static class BeastContentImporter
    {
        // 与工程根平级的 GDD 数据（D:/Unity_Project/MYRIAD/WanXiang/Docs/...）。
        private const string JsonRelPath = "Docs/Design/data/beasts.json";
        private const string SkillsDir = "Assets/WanXiang/Config/Skills";
        private const string BeastsDir = "Assets/WanXiang/Config/Beasts";
        private const string SoulsDir = "Assets/WanXiang/Config/Souls";
        private const string CatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";

        [MenuItem("万相/融合/① 导入异兽内容（beasts.json → 配置资产）")]
        public static void ImportFromMenu()
        {
            var lines = Import();
            foreach (var l in lines) Debug.Log("[内容导入] " + l);
            EditorUtility.DisplayDialog("内容导入",
                $"导入完成：{_skillCount} 技能 / {_beastCount} 异兽 / {_soulCount} 灵魂。\n" +
                "详情见 Console（搜 [内容导入]）。", "好");
        }

        private static int _skillCount, _beastCount, _soulCount;

        /// <summary>执行导入，返回逐行报告（自检 / 批处理也用它）。</summary>
        public static string[] Import()
        {
            var report = new List<string>();
            _skillCount = _beastCount = _soulCount = 0;

            string jsonPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JsonRelPath));
            if (!File.Exists(jsonPath))
            {
                report.Add($"❌ 找不到 GDD 数据：{jsonPath}");
                return report.ToArray();
            }

            object root;
            try { root = MiniJson.Parse(File.ReadAllText(jsonPath, Encoding.UTF8)); }
            catch (System.FormatException e)
            {
                report.Add($"❌ JSON 解析失败：{e.Message}");
                return report.ToArray();
            }

            var beastsJson = MiniJson.Arr(MiniJson.Obj(root)?["beasts"]);
            if (beastsJson == null || beastsJson.Count == 0)
            {
                report.Add("❌ beasts.json 里没有 beasts 数组");
                return report.ToArray();
            }

            EnsureDir(SkillsDir);
            EnsureDir(BeastsDir);
            EnsureDir(SoulsDir);

            var beastAssets = new List<BeastConfigSO>(beastsJson.Count);
            var soulAssets = new List<SoulConfigSO>(beastsJson.Count);
            var seenIds = new HashSet<string>();

            for (int i = 0; i < beastsJson.Count; i++)
            {
                var jo = MiniJson.Obj(beastsJson[i]);
                if (jo == null) { report.Add($"⚠ 第 {i} 项不是对象，跳过"); continue; }

                string id = MiniJson.Str(GetValue(jo, "id"));
                if (string.IsNullOrEmpty(id)) { report.Add($"⚠ 第 {i} 项缺 id，跳过"); continue; }
                if (!seenIds.Add(id)) { report.Add($"⚠ 重复 id：{id}，后者跳过"); continue; }

                // ---- 先组装纯数据 BeastDef（导灵魂要用它） ----
                var def = BuildBeastDef(jo, report);
                if (def == null) continue;

                // ---- 技能资产 ×3 ----
                var basic = UpsertSkill(def.Basic, jo, 0, report);
                var active = UpsertSkill(def.Active, jo, 1, report);
                var ultimate = UpsertSkill(def.Ultimate, jo, 2, report);

                // ---- 异兽资产 ----
                var beastSo = LoadOrCreate<BeastConfigSO>($"{BeastsDir}/Beast_{id}.asset");
                FillBeastSo(beastSo, def, basic, active, ultimate);
                EditorUtility.SetDirty(beastSo);
                beastAssets.Add(beastSo);
                _beastCount++;

                // ---- 灵魂资产（走 SoulForge，资产 = 规则输出） ----
                SoulDef soul = SoulForge.Derive(def, i);
                var soulSo = LoadOrCreate<SoulConfigSO>($"{SoulsDir}/Soul_{id}.asset");
                FillSoulSo(soulSo, soul, active);
                EditorUtility.SetDirty(soulSo);
                soulAssets.Add(soulSo);
                _soulCount++;
            }

            // ---- 目录 ----
            var catalog = LoadOrCreate<ContentCatalogSO>(CatalogPath);
            catalog.Beasts = beastAssets.ToArray();
            catalog.Souls = soulAssets.ToArray();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Add($"✅ 导入完成：{_skillCount} 技能 / {_beastCount} 异兽 / {_soulCount} 灵魂 / 目录 1 份");
            report.Add($"   目录：{CatalogPath}");
            return report.ToArray();
        }

        // ====================================================================
        //  JSON → BeastDef（不填基础面板：占位面板由 BattleConfig 统一生成）
        // ====================================================================

        private static BeastDef BuildBeastDef(Dictionary<string, object> jo, List<string> report)
        {
            string id = MiniJson.Str(GetValue(jo, "id"));
            Element element = ParseEnum<Element>(MiniJson.Str(GetValue(jo, "element")));
            RoleType role = ParseEnum<RoleType>(MiniJson.Str(GetValue(jo, "role")));
            Rarity rarity = ParseEnum<Rarity>(MiniJson.Str(GetValue(jo, "rarity")));

            var trait = MiniJson.Obj(GetValue(jo, "trait"));
            var skills = MiniJson.Arr(GetValue(jo, "skills"));
            var palette = MiniJson.Obj(GetValue(jo, "palette"));

            if (skills == null || skills.Count < 3)
            {
                report.Add($"⚠ {id} 技能不足 3 条，跳过");
                return null;
            }

            var def = new BeastDef
            {
                Id = id,
                DisplayName = MiniJson.Str(GetValue(jo, "displayName")),
                Element = element,
                Role = role,
                Rarity = rarity,
                Source = MiniJson.Str(GetValue(jo, "source")),
                Quote = MiniJson.Str(GetValue(jo, "quote")),
                Lore = MiniJson.Str(GetValue(jo, "lore")),
                Codex = MiniJson.Str(GetValue(jo, "codex")),
                Trait = new TraitDef
                {
                    Name = MiniJson.Str(GetValue(trait, "name")),
                    Description = MiniJson.Str(GetValue(trait, "description")),
                },
            };

            // 技能：GDD 真值（名 / 类型 / CD / 描述）+ 占位效果（按职业 + 类型）。
            for (int s = 0; s < 3; s++)
            {
                var js = MiniJson.Obj(skills[s]);
                SkillType type = ParseEnum<SkillType>(MiniJson.Str(GetValue(js, "type")));
                int cd = (int)MiniJson.Num(GetValue(js, "cd"));
                string name = MiniJson.Str(GetValue(js, "name"));
                string desc = MiniJson.Str(GetValue(js, "description"));

                string slotSuffix = s == 0 ? "_b" : s == 1 ? "_a" : "_u";
                var skill = new SkillDef
                {
                    Id = id + slotSuffix,
                    Name = name,
                    Type = type,
                    Cd = type == SkillType.Basic ? 0 : cd,
                    Element = element,
                    PrimaryTarget = TargetSelector.SingleLowestHp,
                    Effects = PlaceholderEffects(role, type),
                    Description = desc,
                };

                if (s == 0) def.Basic = skill;
                else if (s == 1) def.Active = skill;
                else def.Ultimate = skill;
            }

            def.Palette = new PaletteHex
            {
                BodyMain = MiniJson.Str(GetValue(palette, "bodyMain")),
                BodyAccent = MiniJson.Str(GetValue(palette, "bodyAccent")),
                EnergyGlow = MiniJson.Str(GetValue(palette, "energyGlow")),
                EyeCore = MiniJson.Str(GetValue(palette, "eyeCore")),
                Outline = MiniJson.Str(GetValue(palette, "outline")),
            };
            return def;
        }

        private static object GetValue(Dictionary<string, object> obj, string key)
            => obj != null && obj.TryGetValue(key, out var v) ? v : null;

        private static T ParseEnum<T>(string name) where T : struct
        {
            return System.Enum.TryParse(name, out T result) ? result : default;
        }

        // ====================================================================
        //  占位效果：按「职业 + 技能类型」。与灰盒脚手架同口径，
        //  目的是让融合的行为差异（伤害量级 / 段数 / 目标）立即可观察。
        // ====================================================================

        private static EffectAtom[] PlaceholderEffects(RoleType role, SkillType type)
        {
            switch (type)
            {
                case SkillType.Basic:
                    switch (role)
                    {
                        case RoleType.Support: return A(EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.70f));
                        case RoleType.Caster: return A(EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.90f));
                        case RoleType.Swift: return A(EffectAtom.Damage(TargetSelector.SingleLowestHp, 0.80f));
                        default: return A(EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.00f));
                    }
                case SkillType.Active:
                    switch (role)
                    {
                        case RoleType.Guard: return A(EffectAtom.Shield(TargetSelector.Self, 0.80f));
                        case RoleType.Striker: return A(EffectAtom.Damage(TargetSelector.SingleHighestAtk, 1.80f));
                        case RoleType.Caster: return A(EffectAtom.Dot(TargetSelector.AllEnemies, StatusCatalog.Burn, 0.04f, 1, 3));
                        case RoleType.Support: return A(EffectAtom.Heal(TargetSelector.SingleLowestHp, 0.90f));
                        default: return A(EffectAtom.Damage(TargetSelector.SingleLowestHp, 1.20f, hits: 2));
                    }
                case SkillType.Ultimate:
                default:
                    switch (role)
                    {
                        case RoleType.Guard:
                            return A(EffectAtom.Damage(TargetSelector.AllEnemies, 0.80f),
                                     EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.ArmorBreak, 2, 2));
                        case RoleType.Striker: return A(EffectAtom.Damage(TargetSelector.RandomEnemyMultiHit, 0.60f, hits: 5));
                        case RoleType.Caster:
                            return A(EffectAtom.Damage(TargetSelector.AllEnemies, 1.30f),
                                     EffectAtom.Status(TargetSelector.AllEnemies, StatusCatalog.Wet, 1, 3));
                        case RoleType.Support:
                            return A(EffectAtom.Heal(TargetSelector.AllAllies, 0.50f),
                                     EffectAtom.Shield(TargetSelector.AllAllies, 0.50f),
                                     EffectAtom.Dispel(TargetSelector.AllAllies));
                        default:
                            return A(EffectAtom.Damage(TargetSelector.RandomEnemy, 1.40f),
                                     EffectAtom.Status(TargetSelector.RandomEnemy, StatusCatalog.Frost, 2, 3));
                    }
            }
        }

        private static EffectAtom[] A(params EffectAtom[] atoms) => atoms;

        // ====================================================================
        //  资产读写
        // ====================================================================

        private static SkillConfigSO UpsertSkill(SkillDef skill, Dictionary<string, object> jo,
                                                 int slot, List<string> report)
        {
            if (skill == null) return null;
            var so = LoadOrCreate<SkillConfigSO>($"{SkillsDir}/Skill_{skill.Id}.asset");
            so.SkillId = skill.Id;
            so.SkillName = skill.Name;
            so.Type = skill.Type;
            so.Cd = skill.Cd;
            so.Element = skill.Element;
            so.PrimaryTarget = skill.PrimaryTarget;
            so.Effects = skill.Effects;
            so.Description = skill.Description;
            EditorUtility.SetDirty(so);
            _skillCount++;
            return so;
        }

        private static void FillBeastSo(BeastConfigSO so, BeastDef def,
                                        SkillConfigSO basic, SkillConfigSO active, SkillConfigSO ultimate)
        {
            so.BeastId = def.Id;
            so.DisplayName = def.DisplayName;
            so.Element = def.Element;
            so.Role = def.Role;
            so.Rarity = def.Rarity;
            so.Source = def.Source;
            so.Quote = def.Quote;
            so.Lore = def.Lore;
            so.Codex = def.Codex;
            so.TraitName = def.Trait.Name;
            so.TraitDescription = def.Trait.Description;
            so.Basic = basic;
            so.Active = active;
            so.Ultimate = ultimate;
            so.BodyMainHex = def.Palette.BodyMain;
            so.BodyAccentHex = def.Palette.BodyAccent;
            so.EnergyGlowHex = def.Palette.EnergyGlow;
            so.EyeCoreHex = def.Palette.EyeCore;
            so.OutlineHex = def.Palette.Outline;
        }

        private static void FillSoulSo(SoulConfigSO so, SoulDef soul, SkillConfigSO injectedSkill)
        {
            so.SoulId = soul.Id;
            so.DisplayName = soul.DisplayName;
            so.Epithet = soul.Epithet;
            so.SourceBeastId = soul.SourceBeastId;
            so.Rarity = soul.Rarity;
            so.ElementOverride = soul.ElementOverride;
            so.HueShiftDegrees = soul.HueShiftDegrees;
            so.GlowHex = soul.GlowHex;
            so.EyeHex = soul.EyeHex;
            so.TraitName = soul.TraitInjection.Name;
            so.TraitDescription = soul.TraitInjection.Description;
            so.SkillOverrides = new[]
            {
                new SoulSkillOverride { Slot = SkillType.Active, Skill = injectedSkill },
            };
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureDir(string assetDir)
        {
            // 只用 AssetDatabase 一条实现（混用 Directory.CreateDirectory 会生成 "Xxx 1" 目录）。
            if (AssetDatabase.IsValidFolder(assetDir)) return;

            var parts = assetDir.Split('/');
            string cur = parts[0];   // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
