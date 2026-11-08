// ============================================================================
//  万相 · 道痕规则（B2-b / 纯规则层）
//  ---------------------------------------------------------------------------
//  道痕 = "这一局做过什么，就在队上留一道痕"（设计 v2 §7）。
//  本文件只做两件事：
//    ① 表：dao_marks.csv（来源 + 三段星级阈值 + 效果文案）
//    ② 规则：本局统计 → 可获得的道痕与星级（**纯函数，可单测**）
//
//  ⛔ 不碰存档、不碰 UI、不改战斗。接线（局末发放 + 装备 2 槽 + 分享码）留给后续批次。
//  ⚠ 表缺失 ⇒ 表为空但规则可用（返回空列表，不报错）。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>道痕来源：由局内统计的哪个数值驱动。</summary>
    public enum DaoMarkSource
    {
        None = 0,
        Reactions = 1,      // 触发元素反应次数
        ActsCleared = 2,    // 通关幕数（同元素/零阵亡用）
        Deaths = 3,         // 阵亡数（越少越好，用 0 判定）
        FastWins = 4,       // 3 回合内结束的场次
        BossKills = 5,      // 击败首领数
        Captures = 6,       // 捕获数
        MaxHit = 7,         // 单次最大伤害
        SameElementActs = 8,// 同元素队伍的通关幕数（非纯色队 = 0）
    }

    public sealed class DaoMarkDef
    {
        public string Id;
        public string Name;
        public DaoMarkSource Source = DaoMarkSource.None;
        public int T1, T2, T3;          // 三段星级阈值（达到 T1=★1，T2=★2，T3=★3）
        public string EffectDesc;
        /// <summary>生效通道（与 TraitChannels 同一套语义，便于战斗挂点统一取）。</summary>
        public string Channel;
        /// <summary>★1/★2/★3 的数值（比例或位次，由通道决定）。</summary>
        public float V1, V2, V3;

        /// <summary>按星级取数值；★0 返回 0。</summary>
        public float ValueOf(int star)
            => star >= 3 ? (V3 > 0f ? V3 : V2 > 0f ? V2 : V1)
             : star == 2 ? (V2 > 0f ? V2 : V1)
             : star == 1 ? V1 : 0f;
    }

    /// <summary>一局结束时的原始统计（由调用方填，规则层不认识 RunState）。</summary>
    public struct RunStats
    {
        public int Reactions;
        public int ActsCleared;
        public int Deaths;
        public int FastWins;
        public int BossKills;
        public int Captures;
        public int MaxHit;
        public bool SameElementTeam;     // 全队同元素
    }

    public static class DaoMarkRules
    {
        private static readonly List<DaoMarkDef> _all = new List<DaoMarkDef>();

        public static IReadOnlyList<DaoMarkDef> All => _all;
        public static int Count => _all.Count;
        public static bool Installed { get; private set; }

        public static void Install(IReadOnlyList<DaoMarkDef> rows)
        {
            _all.Clear();
            if (rows != null)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i] != null && !string.IsNullOrEmpty(rows[i].Id)) _all.Add(rows[i]);
            Installed = _all.Count > 0;
        }

        public static void Clear() { _all.Clear(); Installed = false; }

        public static DaoMarkDef For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _all.Count; i++) if (_all[i].Id == id) return _all[i];
            return null;
        }

        /// <summary>
        /// 纯函数：把"已装备的道痕（id + 星级）"折算成某通道的总加成。
        /// ⚠ 同通道多条相加；未知 id / ★0 自动跳过。
        /// </summary>
        public static float EquippedBonus(IReadOnlyList<KeyValuePair<string, int>> equipped, string channel)
        {
            if (equipped == null || string.IsNullOrEmpty(channel)) return 0f;
            float total = 0f;
            for (int i = 0; i < equipped.Count; i++)
            {
                var d = For(equipped[i].Key);
                if (d == null || d.Channel != channel) continue;
                total += d.ValueOf(equipped[i].Value);
            }
            return total;
        }

        /// <summary>纯函数：数值 → 星级（0 = 不达标）。</summary>
        public static int StarsFor(int value, int t1, int t2, int t3)
        {
            if (t3 > 0 && value >= t3) return 3;
            if (t2 > 0 && value >= t2) return 2;
            if (t1 > 0 && value >= t1) return 1;
            return 0;
        }

        /// <summary>
        /// 结算：本局统计 → 可获得的道痕列表（id + 星级，已过滤 ★0）。
        /// 顺序 = 表顺序，稳定可测。
        /// </summary>
        public static List<KeyValuePair<string, int>> Evaluate(in RunStats s)
        {
            var result = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                int v = RawValueOf(d.Source, s);
                int stars = StarsFor(v, d.T1, d.T2, d.T3);
                if (stars > 0) result.Add(new KeyValuePair<string, int>(d.Id, stars));
            }
            return result;
        }

        /// <summary>来源 → 原始数值。新增来源只加一个 case（单一收口）。</summary>
        public static int RawValueOf(DaoMarkSource src, in RunStats s)
        {
            switch (src)
            {
                case DaoMarkSource.Reactions: return s.Reactions;
                case DaoMarkSource.ActsCleared: return s.ActsCleared;
                case DaoMarkSource.Deaths: return s.Deaths <= 0 ? 1 : 0;   // 零阵亡 = 1
                case DaoMarkSource.FastWins: return s.FastWins;
                case DaoMarkSource.BossKills: return s.BossKills;
                case DaoMarkSource.Captures: return s.Captures;
                case DaoMarkSource.MaxHit: return s.MaxHit;
                case DaoMarkSource.SameElementActs: return s.SameElementTeam ? s.ActsCleared : 0;
                default: return 0;
            }
        }
    }
}
