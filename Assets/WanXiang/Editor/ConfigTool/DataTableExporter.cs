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

            AssetDatabase.Refresh();
            Debug.Log($"[DataTable] 导出完成 → {OutDir}：状态 {status} 条、首领 {boss} 只、天时 {weather} 条。");
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
