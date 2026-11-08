// ============================================================================
//  万相 · 性情效果表（骨架 / B1 收尾）
//  ---------------------------------------------------------------------------
//  性情与数值**全部由 CSV 驱动**：temperaments.csv 一行 = 一个性情在一个通道上的贡献。
//  想删掉"刚烈"？删那一行即可（Bonus 自动为 0，模拟层不用改）。
//  想改"沉静 +1 灵力"为 "+2"？改 value。
//  想给某个性情加第二个效果？再加一行（同性情、不同通道）。
//
//  ⛔ 本文件在 Battle.Core（noEngineReferences），不碰 TextAsset；解析在 UI 层。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    public sealed class TemperamentProfile
    {
        public Temperament Temper;
        public string Channel;
        public float Value;
        /// <summary>第二数值（如「孤高」的相邻惩罚 -0.03；0 = 无）。</summary>
        public float Value2;
    }

    public static class TemperamentTable
    {
        // key = channel；value = (性情 -> 数值)
        private static readonly Dictionary<string, Dictionary<Temperament, float>> _byChannel =
            new Dictionary<string, Dictionary<Temperament, float>>();
        private static readonly Dictionary<string, Dictionary<Temperament, float>> _byChannel2 =
            new Dictionary<string, Dictionary<Temperament, float>>();

        public static bool Installed { get; private set; }
        public static int RowCount { get; private set; }

        public static void Install(IReadOnlyList<TemperamentProfile> rows)
        {
            _byChannel.Clear();
            _byChannel2.Clear();
            RowCount = 0;
            if (rows == null) { Installed = false; return; }
            for (int i = 0; i < rows.Count; i++)
            {
                var p = rows[i];
                if (p == null || p.Temper == Temperament.None || string.IsNullOrEmpty(p.Channel)) continue;
                Dictionary<Temperament, float> map;
                if (!_byChannel.TryGetValue(p.Channel, out map))
                {
                    map = new Dictionary<Temperament, float>();
                    _byChannel[p.Channel] = map;
                }
                map[p.Temper] = p.Value;      // 同性情同通道重复 ⇒ 后者覆盖
                Dictionary<Temperament, float> map2;
                if (!_byChannel2.TryGetValue(p.Channel, out map2))
                {
                    map2 = new Dictionary<Temperament, float>();
                    _byChannel2[p.Channel] = map2;
                }
                map2[p.Temper] = p.Value2;
                RowCount++;
            }
            Installed = RowCount > 0;
        }

        /// <summary>某阵营在通道上的性情加成总和（加值）。表未装 ⇒ 0。</summary>
        public static float Bonus(BattleState st, TeamSide side, string channel)
        {
            if (st == null || string.IsNullOrEmpty(channel)) return 0f;
            Dictionary<Temperament, float> map;
            if (!_byChannel.TryGetValue(channel, out map)) return 0f;
            var units = st.UnitsOf(side);
            if (units == null) return 0f;
            float total = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || !u.IsAlive || u.Def == null) continue;
                float v;
                if (map.TryGetValue(u.Def.Temperament, out v)) total += v;
            }
            return total;
        }

        /// <summary>单只单位在通道上的性情加值（挂点用）。</summary>
        public static float BonusFor(BattleUnit u, string channel)
        {
            if (u == null || u.Def == null || string.IsNullOrEmpty(channel)) return 0f;
            Dictionary<Temperament, float> map;
            if (!_byChannel.TryGetValue(channel, out map)) return 0f;
            float v;
            return map.TryGetValue(u.Def.Temperament, out v) ? v : 0f;
        }

        /// <summary>某通道的第二数值（如孤高的相邻惩罚）。表未装/无值 ⇒ 0。</summary>
        public static float Bonus2For(BattleUnit u, string channel)
        {
            if (u == null || u.Def == null || string.IsNullOrEmpty(channel)) return 0f;
            Dictionary<Temperament, float> map;
            if (!_byChannel2.TryGetValue(channel, out map)) return 0f;
            float v;
            return map.TryGetValue(u.Def.Temperament, out v) ? v : 0f;
        }

        public static void Clear() { _byChannel.Clear(); _byChannel2.Clear(); Installed = false; RowCount = 0; }
    }
}
