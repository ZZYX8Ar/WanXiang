// ============================================================================
//  WanXiang · 数据表导出（内置表 → CSV）
//  ---------------------------------------------------------------------------
//  菜单：万相 / 配置 / 导出内置表为 CSV
//
//  ⭐ 为什么要有这个工具（而不是手写 CSV）：
//    天时 24 条 × 45 个字段、首领 14 只、状态 18 条 —— 手抄一遍必然出错，
//    而且"抄错一格"只会表现为"某个数值怪怪的"，很难查。
//    做法是**从当前代码里的内置表导出**，导出后 CSV 与代码逐项一致；
//    之后再改数值只改 CSV（代码里的值退化成"缺表时的兜底"）。
//
//  ⛔ 重复导出会覆盖 Resources/Data 下的同名文件：
//    如果策划已经在 CSV 里调过数值，**别点这个菜单**（会把调整覆盖回代码里的默认值）。
//    需要重新导出时先备份 Resources/Data。
//
//  输出：Assets/Resources/Data/
//    status.csv · boss.csv · weather.csv · enemy_budget.csv ·
//    squad.csv · map.csv · node_weights.csv
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Campaign;

namespace WanXiang.EditorTools
{
    public static class DataTableExporter
    {
        private const string OutDir = "Assets/Resources/Data";

        [MenuItem("万相/配置/导出内置表为 CSV", priority = 210)]
        public static void ExportAll()
        {
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);

            int status = ExportStatus();
            int boss = ExportBoss();
            int weather = ExportWeather();
            ExportBudget();
            ExportSquad();
            ExportMap();
            ExportNodeWeights();
            int relics = ExportRelics();
            int combos = ExportCombos();
            ExportProgression();

            AssetDatabase.Refresh();
            Debug.Log($"[DataTable] 导出完成 → {OutDir}：状态 {status}、首领 {boss}、天时 {weather}、" +
                      $"遗物 {relics}、阵形 {combos}、养成曲线 1 张。");
        }

        // ================================================================
        //  单表
        // ================================================================

        private static int ExportStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("id,name,isDebuff,maxStacks,dotFlatPerStack,preventsAction,preventsSkill,preventsHeal," +
                          "damageTakenPerStack,attackPerStack,speedPerStack,healShieldPerStack,consumeOnHit,lingering,description");
            var all = StatusCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                sb.AppendLine(string.Join(",",
                    Esc(d.Id), Esc(d.Name), B(d.IsDebuff), I(d.MaxStacks), F(d.DotFlatPerStack),
                    B(d.PreventsAction), B(d.PreventsSkill), B(d.PreventsHeal),
                    F(d.DamageTakenDeltaPerStack), F(d.AttackDeltaPerStack), F(d.SpeedDeltaPerStack),
                    F(d.HealShieldDeltaPerStack), B(d.ConsumeStackOnHit), B(d.IsLingeringCounter),
                    Esc(d.Description)));
            }
            Write("status.csv", sb);
            return all.Count;
        }

        private static int ExportBoss()
        {
            var sb = new StringBuilder();
            sb.AppendLine("id,name,element,role,rarity,hp,atk,def,speed,critRate,critDamage,flavorStatus");
            var all = BossCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                sb.AppendLine(string.Join(",",
                    Esc(d.Id), Esc(d.DisplayName), d.Element.ToString(), d.Role.ToString(), d.Rarity.ToString(),
                    I(d.BaseHp), I(d.BaseAtk), I(d.BaseDef), I(d.BaseSpeed),
                    F(d.CritRate), F(d.CritDamage), Esc(d.FlavorStatus)));
            }
            Write("boss.csv", sb);
            return all.Count;
        }

        private static int ExportWeather()
        {
            var sb = new StringBuilder();
            sb.Append("kind,solarIndex,id,weatherId,nodeName,buffName,element,");
            sb.Append("banHeal,damageAllMultiplier,damageTakenMul,speedMulPlayer,speedMulEnemy,");
            sb.Append("cdAdvanceMulPlayer,cdAdvanceMulEnemy,critDamageBonusPlayer,critDamageBonusEnemy,");
            sb.Append("aoeDamageMul,singleDamageMul,woodDamageMul,fireDamageMul,earthDamageMul,metalDamageMul,waterDamageMul,");
            sb.Append("firstTurnDamageMul,fireUnitDotTakenMul,shieldGainMul,healOverflowShieldRatio,");
            sb.Append("attackBurnOn,attackBurnPower,attackBurnTurns,pursuitOnCrit,pursuitPower,");
            sb.Append("killOverflowShield,killOverflowShieldRatio,reviveEggOn,reviveEggDelayTurns,reviveEggHpPercent,");
            sb.Append("hasteEveryNTurns,hasteCdReduction,freezeOnHitChance,freezeOnHitTurns,");
            sb.Append("debuffDurationMinusOne,immuneConfuseSilence,extraBasicAttackOnTurnEnd,iceMeltOnFireSkill,");
            sb.AppendLine("turnStart,turnEnd");

            var indices = WeatherCatalog.SolarIndices;
            for (int i = 0; i < indices.Count; i++)
            {
                var w = WeatherCatalog.GetSolarTerm(indices[i]);
                if (w == null) continue;
                sb.AppendLine(WeatherRow("solar", indices[i], w));
            }
            var skills = WeatherCatalog.AllSkills;
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i] == null || skills[i].Brings == null) continue;
                sb.AppendLine(WeatherRow("skill", 0, skills[i].Brings, skills[i].Id, skills[i].Name));
            }

            Write("weather.csv", sb);
            return indices.Count + skills.Count;
        }

        private static string WeatherRow(string kind, int solarIndex, WeatherDef w,
                                         string skillId = null, string skillName = null)
        {
            var cells = new List<string>
            {
                kind, I(solarIndex), Esc(skillId ?? w.Id), Esc(w.Id),
                Esc(skillName ?? w.NodeName), Esc(w.BuffName),
                w.Element.ToString(),
                B(w.BanHeal), F(w.DamageAllMultiplier), F(w.DamageTakenMul),
                F(w.SpeedMulPlayer), F(w.SpeedMulEnemy),
                F(w.CdAdvanceMulPlayer), F(w.CdAdvanceMulEnemy),
                F(w.CritDamageBonusPlayer), F(w.CritDamageBonusEnemy),
                F(w.AoeDamageMul), F(w.SingleDamageMul),
                F(w.WoodDamageMul), F(w.FireDamageMul), F(w.EarthDamageMul), F(w.MetalDamageMul), F(w.WaterDamageMul),
                F(w.FirstTurnDamageMul), F(w.FireUnitDotTakenMul), F(w.ShieldGainMul), F(w.HealOverflowShieldRatio),
                B(w.AttackBurnOn), F(w.AttackBurnPower), I(w.AttackBurnTurns),
                B(w.PursuitOnCrit), F(w.PursuitPower),
                B(w.KillOverflowShield), F(w.KillOverflowShieldRatio),
                B(w.ReviveEggOn), I(w.ReviveEggDelayTurns), F(w.ReviveEggHpPercent),
                I(w.HasteEveryNTurns), I(w.HasteCdReduction),
                F(w.FreezeOnHitChance), I(w.FreezeOnHitTurns),
                B(w.DebuffDurationMinusOne), B(w.ImmuneConfuseSilence),
                B(w.ExtraBasicAttackOnTurnEnd), B(w.IceMeltOnFireSkill),
                Esc(EncodeEffects(w.TurnStart)), Esc(EncodeEffects(w.TurnEnd)),
            };
            return string.Join(",", cells);
        }

        /// <summary>
        /// 天时原子的紧凑编码（多段用 `;`，段内用 `|`）：
        /// scope|kind|target|power|pct|status|stacks|turns|trueDmg|ignoreShield|once|minTurn|pick|statKey|delta
        /// 解析器 DataTables.DecodeEffects 是它的逆运算 —— **两边必须同时改**。
        /// </summary>
        private static string EncodeEffects(WeatherEffect[] list)
        {
            if (list == null || list.Length == 0) return "";
            var parts = new List<string>(list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                var e = list[i];
                var a = e.Atom;
                if (a.Hits > 1 || a.ElementOverride != Element.None)
                    Debug.LogWarning("[DataTable] 天时原子带有 hits/elementOverride，CSV 编码暂不支持，已丢弃：" +
                                     (e.Atom.Kind) + " hits=" + a.Hits + " element=" + a.ElementOverride);
                parts.Add(string.Join("|",
                    e.Scope.ToString(), a.Kind.ToString(), a.Target.ToString(),
                    F(a.Power), F(a.PercentOfMaxHp), Esc(a.StatusId ?? ""), I(a.StatusStacks), I(a.StatusTurns),
                    B(a.TrueDamage), B(a.IgnoreShield),
                    B(e.Once), I(e.MinTurn), e.Pick.ToString(),
                    Esc(a.StatKey ?? ""), F(a.StatDelta)));
            }
            return string.Join(";", parts);
        }

        private static void ExportBudget()
        {
            var sb = new StringBuilder();
            sb.AppendLine("key,value");
            sb.AppendLine("base," + F(EnemyBudget.Base));
            sb.AppendLine("jie_step," + F(EnemyBudget.JieStep));
            sb.AppendLine("cap_jie_step," + F(EnemyBudget.CapJieStep));
            sb.AppendLine("boss_mul," + F(EnemyBudget.BossMul));
            sb.AppendLine("node_elite," + F(EnemyBudget.NodeMulElite));
            sb.AppendLine("node_encounter," + F(EnemyBudget.NodeMulEncounter));
            for (int act = 1; act <= 5; act++)
                sb.AppendLine("act" + act + "," + F(EnemyBudget.ActMul(act)));
            Write("enemy_budget.csv", sb);
        }

        private static void ExportSquad()
        {
            var sb = new StringBuilder();
            sb.AppendLine("act,encounter,elite,boss");
            for (int act = 1; act <= 5; act++)
            {
                sb.AppendLine(string.Join(",", I(act),
                    I(EnemyBudget.SquadSize(act, NodeKind.Encounter)),
                    I(EnemyBudget.SquadSize(act, NodeKind.Elite)),
                    I(EnemyBudget.BossSquadSize(act))));
            }
            Write("squad.csv", sb);
        }

        private static void ExportMap()
        {
            var sb = new StringBuilder();
            sb.AppendLine("key,value");
            sb.AppendLine("layers," + I(MapRules.Layers));
            sb.AppendLine("width," + I(MapRules.Width));
            Write("map.csv", sb);
        }

        private static void ExportNodeWeights()
        {
            var sb = new StringBuilder();
            sb.AppendLine("kind,weight,minAct");
            var ws = MapRules.Weights;
            for (int i = 0; i < ws.Count; i++)
            {
                int minAct = 1;
                MapRules.MinActs.TryGetValue(ws[i].Key, out minAct);
                sb.AppendLine(string.Join(",", ws[i].Key.ToString(), I(ws[i].Value), I(minAct)));
            }
            Write("node_weights.csv", sb);
        }

        // ================================================================
        //  遗物 / 阵形 / 养成曲线
        // ================================================================

        private static int ExportRelics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("id,name,desc,rarity,effect,value,value2,scopeId,mechanic");
            var all = RelicCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null) continue;
                sb.AppendLine(string.Join(",",
                    Esc(d.Id), Esc(d.Name), Esc(d.Desc), d.Rarity.ToString(), d.Effect.ToString(),
                    F(d.Value), F(d.Value2), Esc(d.ScopeId), d.Mechanic.ToString()));
            }
            Write("relics.csv", sb);
            return all.Length;
        }

        private static int ExportCombos()
        {
            var sb = new StringBuilder();
            sb.AppendLine("id,name,note,cells,cellsAlt,effect,power,power2");
            var all = ComboRules.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (d == null) continue;
                sb.AppendLine(string.Join(",",
                    Esc(d.Id), Esc(d.Name), Esc(d.Note),
                    Esc(JoinCells(d.Cells)), Esc(JoinCells(d.CellsAlt)),
                    d.Effect.ToString(), F(d.Power), F(d.Power2)));
            }
            Write("combos.csv", sb);
            return all.Count;
        }

        private static string JoinCells(int[] cells)
        {
            if (cells == null || cells.Length == 0) return "";
            var parts = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++) parts[i] = cells[i].ToString(CultureInfo.InvariantCulture);
            return string.Join(";", parts);
        }

        private static void ExportProgression()
        {
            // ⛔ 不要用反射按 key 找字段：key 是 snake_case（level_max），字段是 PascalCase（LevelMax），
            //   反射会全部落空、静默写成 0（第一次导出就踩了）。这里逐项显式写，编译期就能查出漏项。
            var sb = new StringBuilder();
            sb.AppendLine("key,value,note");
            void Row(string key, string value, string note) => sb.AppendLine(key + "," + value + "," + note);

            Row("level_max", I(WanXiang.Run.ProgressionTuning.LevelMax), "主等级上限");
            Row("exp_base", I(WanXiang.Run.ProgressionTuning.ExpBase), "Lv1到2所需经验");
            Row("exp_step", I(WanXiang.Run.ProgressionTuning.ExpStep), "每级额外经验");
            Row("level_atk", F(WanXiang.Run.ProgressionTuning.LevelAtk), "每级攻击加成(0.06=+6%)");
            Row("level_hp", F(WanXiang.Run.ProgressionTuning.LevelHp), "每级生命加成(0.07=+7%)");
            Row("mastery_cap", I(WanXiang.Run.ProgressionTuning.MasteryCapNormal), "专精上限（未进化）");
            Row("mastery_cap_evolved", I(WanXiang.Run.ProgressionTuning.MasteryCapEvolved), "专精上限（已进化）");
            Row("mastery_lv1_mul", F(WanXiang.Run.ProgressionTuning.MasteryLv1Mul), "专精1的技能倍率");
            Row("mastery_lv3_mul", F(WanXiang.Run.ProgressionTuning.MasteryLv3Mul), "专精3的额外倍率（与专精1叠乘）");
            Row("evolve_stat_mul", F(WanXiang.Run.ProgressionTuning.EvolveStatMul), "进化后基础攻血倍率");
            Row("evolve_material_cost", I(WanXiang.Run.ProgressionTuning.EvolveMaterialCost), "进化消耗素材");
            Row("revive_hp_pct", I(WanXiang.Run.ProgressionTuning.ReviveHpPct), "通关后阵亡单位进下一场的血量%");
            Row("exp_win_normal", I(WanXiang.Run.ProgressionTuning.ExpWinNormal), "普通战经验");
            Row("exp_win_elite", I(WanXiang.Run.ProgressionTuning.ExpWinElite), "精英战经验");
            Row("exp_win_realm", I(WanXiang.Run.ProgressionTuning.ExpWinRealm), "秘境经验");
            Row("exp_win_boss", I(WanXiang.Run.ProgressionTuning.ExpWinBoss), "幕末首领经验");
            Row("material_win_elite", I(WanXiang.Run.ProgressionTuning.MaterialWinElite), "精英掉落素材");
            Row("material_win_boss", I(WanXiang.Run.ProgressionTuning.MaterialWinBoss), "首领掉落素材");
            Write("progression.csv", sb);
        }

        // ================================================================
        //  写盘工具
        // ================================================================

        private static void Write(string fileName, StringBuilder sb)
        {
            string path = Path.Combine(OutDir, fileName);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[DataTable] 写出 " + path);
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string B(bool v) => v ? "1" : "0";
    }
}
