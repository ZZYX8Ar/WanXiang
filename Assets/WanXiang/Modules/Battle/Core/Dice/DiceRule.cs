// ============================================================================
//  万相 · 骰子规则（Core 层，零引擎依赖）
//  单一真相源：面数→效果映射在 Resources/Data/dice_faces.csv（解析只在 UI 层）。
//  Hook 只负责"掷一次并写事件"，表现层读事件播动画 —— 两层互不依赖。
// ============================================================================
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

    /// <summary>骰面效果（表驱动枚举，数值由 UI 层 DataTables 注入，Core 只认 id）。</summary>
    public enum DiceEffect { None = 0, MarkEnemy = 1, MarkPlayer = 2, MarkBoth = 3, HealPlayer = 4 }

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

        /// <summary>把结果编码进 BattleLog.note，表现层用 <see cref="Decode"/> 读。</summary>
        public static string Encode(DiceRoll r, string prefix)
        {
            return "[DICE]" + prefix + "|" + r.TableId + "|" + r.Face + "|"
                 + r.Dice[0] + "," + r.Dice[1] + "," + r.Dice[2] + "|" + r.Text;
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
            var p = note.Substring(6).Split('|');
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
