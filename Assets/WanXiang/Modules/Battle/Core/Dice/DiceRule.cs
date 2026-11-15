// ============================================================================
//  万相 · 骰子规则（Core 层，零引擎依赖）
//  单一真相源：面数→效果映射在 Resources/Data/dice_faces.csv（解析只在 UI 层）。
//  Hook 只负责"掷一次并写事件"，表现层读事件播动画 —— 两层互不依赖。
// ============================================================================
using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Core.Dice
{
    /// <summary>一次掷骰的结果（值对象，可进 BattleLog.note 供表现层解析）。</summary>
    public readonly struct DiceRoll
    {
        public readonly int Face;          // 1..6
        public readonly int[] Dice;        // 三颗骰子的点数
        public readonly string TableId;
        public readonly string Label;
        public readonly string Text;

        public DiceRoll(int face, int[] dice, string tableId, string label, string text)
        {
            Face = face; Dice = dice; TableId = tableId; Label = label; Text = text;
        }
    }

    /// <summary>骰面效果（表驱动，数值由 UI 层 DataTables 从 dice_faces.csv 注入）。</summary>
    public enum DiceEffect { None = 0, MarkEnemy = 1, MarkPlayer = 2, MarkBoth = 3, HealPlayer = 4 }

    /// <summary>一行骰面定义（= dice_faces.csv 一行）。</summary>
    public sealed class DiceFaceDef
    {
        public int Face;                // 1..6
        public string Grade = "";       // 罚 / 平 / 赏（仅展示）
        public string Label = "";       // "一点·空"
        public DiceEffect Effect = DiceEffect.None;
        public int Value;               // 标记层数 或 回血百分比
        public string Text = "";        // 展示文案
    }

    /// <summary>
    /// 骰面表（唯一取数口）。表缺失 ⇒ 内置兜底 6 行，行为与旧内联逻辑一致，不抛异常。
    /// ⚠ 只在 UI 层 DataTables 里 Install；Core 不读 TextAsset。
    /// </summary>
    public static class DiceFaceTable
    {
        private static readonly Dictionary<int, DiceFaceDef> Map = new Dictionary<int, DiceFaceDef>(6);
        private static bool _installed;

        public static void Install(IList<DiceFaceDef> rows)
        {
            Map.Clear();
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] != null && rows[i].Face >= 1 && rows[i].Face <= 6) Map[rows[i].Face] = rows[i];
            if (Map.Count == 0) { InstallFallback(); return; }
            _installed = true;
        }

        /// <summary>表缺失/空表时用内置 6 行（与设计稿一致）。</summary>
        public static void EnsureInstalled()
        {
            if (_installed && Map.Count > 0) return;
            InstallFallback();
        }

        private static void InstallFallback()
        {
            Map.Clear();
            Map[1] = new DiceFaceDef { Face = 1, Grade = "罚", Label = "一点·空", Effect = DiceEffect.MarkEnemy, Value = 1, Text = "敌方被标记" };
            Map[2] = new DiceFaceDef { Face = 2, Grade = "罚", Label = "二点·紧", Effect = DiceEffect.MarkEnemy, Value = 2, Text = "敌方被标记两层" };
            Map[3] = new DiceFaceDef { Face = 3, Grade = "平", Label = "三点·和", Effect = DiceEffect.MarkBoth, Value = 1, Text = "敌我各被标记一层" };
            Map[4] = new DiceFaceDef { Face = 4, Grade = "平", Label = "四点·转", Effect = DiceEffect.MarkPlayer, Value = 1, Text = "我方被标记一层" };
            Map[5] = new DiceFaceDef { Face = 5, Grade = "赏", Label = "五点·顺", Effect = DiceEffect.HealPlayer, Value = 12, Text = "我方回血 12%" };
            Map[6] = new DiceFaceDef { Face = 6, Grade = "赏", Label = "六点·满", Effect = DiceEffect.HealPlayer, Value = 20, Text = "我方回血 20%" };
            _installed = true;
        }

        /// <summary>取面；表空则兜底。永远返回非 null。</summary>
        public static DiceFaceDef For(int face)
        {
            EnsureInstalled();
            DiceFaceDef d;
            if (Map.TryGetValue(face, out d) && d != null) return d;
            return Map[1];
        }

        public static int Count { get { return Map.Count; } }
        public static bool IsFromCsv { get { return _installed && Count > 0 && !_fallbackOnly; } }
        private static bool _fallbackOnly = true;
    }

    /// <summary>骰子判定的唯一入口：同种子逐位可复现。</summary>
    public static class DiceRule
    {
        public const int DiceCount = 3;
        public const int FaceMin = 1;
        public const int FaceMax = 6;

        /// <summary>掷三颗 d6，返回总点数与逐颗点数。</summary>
        public static DiceRoll Roll(DeterministicRandom rng, string tableId = "gamble_basic")
        {
            var dice = new int[DiceCount];
            int sum = 0;
            for (int i = 0; i < DiceCount; i++)
            {
                dice[i] = FaceMin + (rng != null ? rng.NextInt(0, FaceMax) : 0);
                sum += dice[i];
            }
            // 面数按总点数映射到 1..6（3~18 → 1~6），保证落在 dice_faces.csv 的行里
            int span = CoreMath.Max(1, DiceCount * (FaceMax - FaceMin));
            int raw = 1 + (sum - DiceCount * FaceMin) * (FaceMax - 1) / span;
            int face = CoreMath.Clamp(raw, FaceMin, FaceMax);   // Core 层禁 UnityEngine
            return new DiceRoll(face, dice, tableId, "第 " + face + " 点", "");
        }

        // ★ 2026-11-15 用户实测错位（结果行显示"三骰 3+0+0 → 第 0 点 · 5,3,1"）：
        //   prefix 里带了 '|'（如"枯骨庄家·坐庄|赏·回血"）→ 字段整体错开。
        //   分隔符改用 US（Unit Separator，1F）—— 中文文案里绝不可能出现，彻底避免冲突。
        private const char Sep = '\u001F';

        /// <summary>把结果编码进 BattleLog.note，表现层用 <see cref="Decode"/> 读。</summary>
        public static string Encode(DiceRoll r, string prefix, string outcome = null)
        {
            return "[DICE]" + (prefix ?? "") + Sep + r.TableId + Sep + r.Face + Sep
                 + r.Dice[0] + "," + r.Dice[1] + "," + r.Dice[2] + Sep
                 + (outcome ?? r.Text ?? "");
        }

        public static bool IsDice(string note)
        {
            return !string.IsNullOrEmpty(note) && note.StartsWith("[DICE]");
        }

        /// <summary>表现层解析（不改结算，只读）。</summary>
        public static bool Decode(string note, out string prefix, out string tableId,
                                  out int face, out int[] dice, out string text)
        {
            prefix = tableId = text = ""; face = 0; dice = new int[DiceCount];
            if (!IsDice(note)) return false;
            var p = note.Substring(6).Split(Sep);
            if (p.Length < 5) return false;
            prefix = p[0]; tableId = p[1];
            int.TryParse(p[2], out face);
            var d = p[3].Split(',');
            for (int i = 0; i < DiceCount && i < d.Length; i++) int.TryParse(d[i], out dice[i]);
            text = p[4];
            return true;
        }
    }
}
