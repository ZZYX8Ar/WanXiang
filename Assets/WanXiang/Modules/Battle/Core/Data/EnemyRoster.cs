// ============================================================================
//  万相 · 敌方阵容表（enemies.csv 的 Core 侧查表口）
//  ---------------------------------------------------------------------------
//  为什么需要它：encounters.csv 里的编队引用了 39 个"非玩家异兽"的 id，
//  它们既不在 beasts.csv 也不在 boss.csv。原来 BattleRequestFactory 里
//  `if (def == null) continue;` 会**静默跳过** ⇒ 编队减员、战力暴跌
//  （用户实测"敌人一打就死"：第 4 幕非 Boss 总战力只剩第 1 幕的 1/4）。
//
//  ⛔ Core 不读 TextAsset：解析在 UI 层 DataTables，这里只接 Install(list)。
//  ⚠ 表缺失 ⇒ 空表，调用方回退到旧行为，不抛异常。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>enemies.csv 一行（敌方专属数值，玩家不可用）。</summary>
    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public string Element = "Wood";
        public string Role = "Guard";
        public int Hp, Atk, Def, Spd;

        /// <summary>战力口径与 <see cref="BattlePower"/> 一致：HP + ATK*3 + DEF*2 + SPD。</summary>
        public int Power { get { return Hp + Atk * 3 + Def * 2 + Spd; } }
    }

    public static class EnemyRoster
    {
        private static readonly Dictionary<string, EnemyDef> Map = new Dictionary<string, EnemyDef>(64);

        public static void Install(IList<EnemyDef> rows)
        {
            Map.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && !string.IsNullOrEmpty(rows[i].Id)) Map[rows[i].Id] = rows[i];
        }

        public static EnemyDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnemyDef d;
            return Map.TryGetValue(id, out d) ? d : null;
        }

        public static bool Has(string id) { return Get(id) != null; }
        public static int Count { get { return Map.Count; } }

        /// <summary>把表行转成战斗用的 BeastDef（敌方口径）。</summary>
        public static BeastDef ToBeastDef(EnemyDef e)
        {
            if (e == null) return null;
            var def = new BeastDef
            {
                Id = e.Id,
                DisplayName = e.Name,
                Element = ParseElement(e.Element),
                Role = ParseRole(e.Role),
                Rarity = Rarity.Rare,
                BaseHp = e.Hp,
                BaseAtk = e.Atk,
                BaseDef = e.Def,
                BaseSpeed = e.Spd,
            };
            return def;
        }

        private static Element ParseElement(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "fire": return Element.Fire;
                case "metal": return Element.Metal;
                case "water": return Element.Water;
                case "earth": return Element.Earth;
                case "none": return Element.None;
                default: return Element.Wood;
            }
        }

        private static RoleType ParseRole(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "striker": return RoleType.Striker;
                case "swift": return RoleType.Swift;
                case "caster": return RoleType.Caster;
                case "support": return RoleType.Support;
                default: return RoleType.Guard;
            }
        }
    }
}
