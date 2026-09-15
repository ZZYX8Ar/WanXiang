// ============================================================================
//  万相 · 战斗核心 · 开局装配
//  ---------------------------------------------------------------------------
//  把"一组 (异兽定义, 站位)"变成一场可以跑的 battle。它做的事比看上去多：
//
//    1) **为每个上阵单位克隆一份 BeastDef**
//       融合（GDD 6.2）会改写 element / skills / trait。如果多个单位共享同一份
//       定义对象，改一只就把同 id 的全改了，而且会"泄漏"到下一场战斗。
//       克隆的代价是 30 只怪 × 几个对象，可以忽略。
//
//    2) **填占位面板**
//       GDD 没给数值表（只在第二章给了属性倾向）。BattleConfig.ApplyPlaceholderStats
//       按"职业 + 稀有度"生成一版，等策划给正式表后只改那一处。
//
//    3) **给出稳定的 RuntimeId**（P0..P4 / E0..E4）
//       日志与指纹都用它。用"名字 + 下标"会撞（同名怪很容易出现），
//       用对象哈希会让两次运行的 id 不同 —— 都会毁掉可复现性判据。
//
//  ⚠ 站位非法或撞格**不抛异常**：站位是内容配置，配错一个不该让整场战斗起不来。
//    记一条事件、跳过它，最后在自检报告里能看见。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>一条上阵指令。</summary>
    public struct DeployEntry
    {
        public BeastDef Def;
        public TeamSide Side;
        public int PosIndex;

        /// <summary>
        /// 属性倍率（GDD v1.1 §5.1 的 m_unit = A×T×K）。
        /// ⚠ 只作用于 **生命/攻击/防御** —— 速度与暴击率不参与缩放，
        /// 否则高幕数会出现"敌人永远先手"的单点崩坏，玩家的速度构筑会被一次性抹平。
        /// </summary>
        public float StatMul;

        /// <summary>劫象 / 额外特性 id（见 <see cref="BattleTraits"/>；null = 无）。</summary>
        public string TraitId;

        public DeployEntry(BeastDef def, TeamSide side, int posIndex)
        {
            Def = def; Side = side; PosIndex = posIndex;
            StatMul = 1f; TraitId = null;
        }

        public DeployEntry WithMul(float mul)
        {
            var e = this; e.StatMul = mul; return e;
        }

        public DeployEntry WithTrait(string traitId)
        {
            var e = this; e.TraitId = traitId; return e;
        }

        public static DeployEntry Of(BeastDef def, TeamSide side, int posIndex)
            => new DeployEntry(def, side, posIndex);

        public static DeployEntry Player(BeastDef def, int posIndex)
            => new DeployEntry(def, TeamSide.Player, posIndex);

        public static DeployEntry Enemy(BeastDef def, int posIndex)
            => new DeployEntry(def, TeamSide.Enemy, posIndex);
    }

    public static class BattleFactory
    {
        /// <summary>开一场 5v5（或任何阵容）。返回的 BattleState 已经 FinishSetup，可以直接 Run。</summary>
        /// <param name="weather">场地天时（GDD 3.1，STEP 3）。null = 无天时，行为与旧版逐位一致。</param>
        public static BattleState Create(BattleConfig cfg, ulong seed,
                                         DeployEntry[] player, DeployEntry[] enemy,
                                         WeatherDef weather = null)
        {
            var st = new BattleState(cfg, seed);
            if (weather != null) st.Weather = new WeatherRuntime(weather);
            Deploy(st, player, "P");
            Deploy(st, enemy, "E");
            st.FinishSetup();
            return st;
        }

        /// <summary>只给一份混合名单的便捷重载。</summary>
        public static BattleState Create(BattleConfig cfg, ulong seed, DeployEntry[] both)
        {
            var st = new BattleState(cfg, seed);
            int p = 0, e = 0;
            if (both != null)
            {
                var tmp = new DeployEntry[1];
                for (int i = 0; i < both.Length; i++)
                {
                    tmp[0] = both[i];
                    if (both[i].Side == TeamSide.Player) Deploy(st, tmp, "P", ref p);
                    else Deploy(st, tmp, "E", ref e);
                }
            }
            st.FinishSetup();
            return st;
        }

        /// <summary>把一批指令放上棋盘。返回成功上阵的数量。</summary>
        public static int Deploy(BattleState st, DeployEntry[] entries, string idPrefix)
        {
            int n = 0;
            return Deploy(st, entries, idPrefix, ref n);
        }

        /// <summary>
        /// 敌方属性倍率（§5.1）。只动 生命/攻击/防御；速度与暴击率**不缩放**（见 DeployEntry.StatMul）。
        /// </summary>
        private static void ApplyStatMul(BeastDef def, float mul)
        {
            def.BaseHp = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseHp * mul));
            def.BaseAtk = CoreMath.Max(1, CoreMath.RoundDamage(def.BaseAtk * mul));
            def.BaseDef = CoreMath.RoundDamage(def.BaseDef * mul);
        }

        private static int Deploy(BattleState st, DeployEntry[] entries, string idPrefix, ref int counter)
        {
            if (entries == null) return counter;

            for (int i = 0; i < entries.Length; i++)
            {
                var def = entries[i].Def;
                if (def == null)
                {
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                               note: $"上阵失败：第 {i} 项没有异兽定义");
                    continue;
                }

                // 克隆 + 填占位面板。顺序不能反 —— 面板要填在副本上。
                var copy = def.Clone();
                st.Config.ApplyPlaceholderStats(copy);
                if (System.Math.Abs(entries[i].StatMul - 1f) > 0.0001f)
                    ApplyStatMul(copy, entries[i].StatMul);
                if (!string.IsNullOrEmpty(entries[i].TraitId))
                    BattleTraits.Apply(copy, entries[i].TraitId);

                var unit = new BattleUnit(entries[i].Side, copy, $"{idPrefix}{counter}");
                // 「复苏」劫象：阵亡时以 30% 生命复活 1 次（复用惊蛰虫卵的孵化机制）
                if (entries[i].TraitId == BattleTraits.Thickwall)
                    unit.NoHeal = true;                          // 厚壁：无法被治疗
                if (entries[i].TraitId == BattleTraits.Revive)
                {
                    unit.EggUsed = false;
                    unit.EggTurnsLeft = 1;
                    unit.EggReviveHpPercent = 0.30f;
                    unit.EggFromTrait = true;
                    unit.HealTakenMul = 0.7f;                    // 复苏：治疗效果 -30%
                }
                var pos = new GridPos(entries[i].PosIndex);

                if (!st.Place(unit, pos))
                {
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                               note: $"上阵失败：{copy.DisplayName} 的格子 {entries[i].PosIndex} " +
                                     (pos.IsValid ? "已被占用" : "不是合法格（0..8）"));
                    continue;
                }
                counter++;
            }
            return counter;
        }
    }
}
