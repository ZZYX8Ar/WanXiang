// ============================================================================
//  阵形连携（ComboDef / ComboRules）—— 2026-10-16 全替换重构
//  ---------------------------------------------------------------------------
//  ⛔ 旧版是"50 条异兽专属连携"（每只兽一条、要指定搭档元素）——用户定案**全替换**：
//    "改成特定的九宫格阵形才能触发的，跟这个异兽没有关系，这样就不需要设计
//     那么多的异兽连携技了。"
//
//  ★ 新口径（用户 2026-10-16 原话逐条落实）：
//    ① 触发 = **九宫格阵形**（与是哪只异兽无关）：阵形要求的格全被己方**存活**单位占据即成阵。
//    ② 主兽 = 站【中宫】的那只，由它发动、由它出**大招能量（元气）**：
//       · 发动条件：元气 >= UltimateRageCost（能放大招的满能量）
//       · 成功：扣掉整份大招能量（SpendRage(UltimateRageCost)）
//       · 失败：扣 50% 大招能量
//    ③ 消耗灵力 = **存活单位数**（"每存活一个就要多消耗一个灵力，5 个都存活就是 5 个灵力"）。
//    ④ 威力随存活数提升：PowerMul = 0.4 + 0.12 × 存活数（5 只 = 100%）。
//    ⑤ 释放判 QTE（多段按键，见 BattleSimulator.Steps）：
//       命中 >= 一半段数 = 成功；全 Perfect = 额外加成；否则**触发失败**——
//       扣 1 灵力 + 50% 主兽大招能量，并记一条「触发失败」事件。
//    ⑥ 每种阵形**每场限一次**（沿用 UsedCombos）。
//
//  ⚠ 结算口径的唯一真源是 BattleSimulator.Combo.cs 的 ExecuteCombo；
//    预览口径是同一个文件的 PreviewComboTargets —— **两处必须一一对应**，
//    改一处不改另一处 = 九宫格高亮与实际打到的目标对不上（已踩过）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>
    /// 连携效果类型（沿用 v1.3 的 18 种，效果实现不动）。每加一种，必须同时改
    /// BattleSimulator.ExecuteCombo（结算）与 PreviewComboTargets（高亮）。
    /// </summary>
    public enum ComboEffect
    {
        // ---- v2.1 原有 3 种 ----
        WoodPulse = 0,       // 全体敌方伤害 + 我方全体回复（按主兽攻击力）
        FireStorm = 1,       // 全体敌方伤害
        MetalBreak = 2,      // 最前排单体重击

        // ---- v1.3 新增 15 种 ----
        RandomTwoHit = 3,    // 对随机 2 名敌人各造成一次伤害
        RandomThreeHit = 4,  // 对随机 3 名敌人各造成一次伤害
        FrontShred = 5,      // 最前排单体伤害 + 降低其防御
        FrontTrue = 6,       // 最前排单体伤害 + 按目标最大生命追加真实伤害
        HighestAtkStrike = 7,// 对敌方最高攻击单位重击
        LowestHpExecute = 8, // 对当前生命最低的敌人伤害（若其带灼烧则伤害提升）
        AllAoeStatus = 9,    // 全体敌方伤害 + 全体铺减益
        AllAoeBurnStack = 10,// 全体敌方伤害 + 按命中人数叠加灼烧
        AllAoeDrainMp = 11,  // 全体敌方伤害 + 各自扣灵力转为我方
        AllAoeStripBuff = 12,// 全体敌方伤害 + 剥离每名目标 1 个增益
        TeamHealShield = 13, // 我方全体回复 + 附加护盾
        TeamShieldReflect = 14, // 我方全体附加护盾（护盾在，受击反伤）
        TeamHealCleanse = 15,// 我方全体回复 + 驱散全部减益
        TeamHealLostHp = 16, // 我方全体按已损失生命的比例回复
        RandomTwoAdvance = 17,// 对随机 2 名敌人伤害 + 推进自身行动条
    }

    /// <summary>
    /// 一条**阵形连携**。与异兽无关 —— 只看九宫格占位。
    /// </summary>
    public sealed class ComboDef
    {
        public string Id;
        /// <summary>阵形名（如「锋矢阵」）。</summary>
        public string Name;
        /// <summary>效果说明（一句话）。</summary>
        public string Note;

        /// <summary>
        /// 成阵格号（3x3 索引 0..8；4 = 中宫）。这些格**全部**被己方存活单位占据才成阵；
        /// 允许其它格另有单位（不要求恰好等于）。
        ///     0 1 2      col2 = 前排（靠中线）
        ///     3 4 5      row0 = 上
        ///     6 7 8
        /// </summary>
        public int[] Cells;

        /// <summary>备用形状（如"任一条对角线"）。为 null = 无备用。</summary>
        public int[] CellsAlt;

        public ComboEffect Effect;
        /// <summary>伤害/效果系数（× 主兽攻击）。</summary>
        public float Power = 1.0f;
        /// <summary>附加参数：减防幅度 / 回复百分比 / 护盾系数等，按 Effect 解释。0 = 用该效果的默认值。</summary>
        public float Power2;
    }

    public static class ComboRules
    {
        /// <summary>QTE 段数（阵形连携统一 3 段按键；命中 >= 2 段算成功）。</summary>
        public const int QteSegments = 3;

        // ================================================================
        //  阵形表（2026-10-16：50 条异兽专属 → 4 条阵形）
        //  ★ 全部以中宫为核心（主兽站中宫发动）；按格数从多到少排序 ——
        //    AvailableFor 取第一条可用的，大阵优先（满阵 > 小阵）。
        // ================================================================
        private static readonly ComboDef[] Table =
        {
            // ---- 满阵（5 格，5 只全上才可能成阵）----
            new ComboDef { Id = "fm_scale", Name = "鱼鳞阵",
                Note = "中宫+四角满阵：对全体敌人造成伤害，并为我方全体回复 15% 生命",
                Cells = new[] { 0, 2, 4, 6, 8 },
                Effect = ComboEffect.WoodPulse, Power = 1.00f },

            new ComboDef { Id = "fm_cloud", Name = "云龙阵",
                Note = "中宫+四正满阵：对全体敌人造成伤害，并为其铺 2 层霜寒",
                Cells = new[] { 1, 3, 4, 5, 7 },
                Effect = ComboEffect.AllAoeStatus, Power = 0.90f },

            // ---- 四格 ----
            new ComboDef { Id = "fm_wedge", Name = "锋矢阵",
                Note = "中宫+前排三格：对全体敌人造成 80% 伤害（突击）",
                Cells = new[] { 2, 4, 5, 8 },
                Effect = ComboEffect.FireStorm, Power = 0.80f },

            // ---- 三格 ----
            new ComboDef { Id = "fm_goose", Name = "雁行阵",
                Note = "中宫+任一条对角线：对随机 3 名敌人各造成一次伤害",
                Cells = new[] { 0, 4, 8 }, CellsAlt = new[] { 2, 4, 6 },
                Effect = ComboEffect.RandomThreeHit, Power = 0.75f },
        };

        public static System.Collections.Generic.IReadOnlyList<ComboDef> All => Table;

        public static ComboDef For(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }

        // ================================================================
        //  阵形判定 / 资源口径（结算与 UI 共用，改口径只改这里）
        // ================================================================

        /// <summary>成阵判定：combo 要求的格全部被该方**存活**单位占据（允许额外占位）。</summary>
        public static bool FormationMatched(BattleState st, TeamSide side, ComboDef c)
        {
            if (st == null || c == null) return false;
            return MatchedCells(st, side, c.Cells)
                || (c.CellsAlt != null && c.CellsAlt.Length > 0 && MatchedCells(st, side, c.CellsAlt));
        }

        private static bool MatchedCells(BattleState st, TeamSide side, int[] cells)
        {
            if (cells == null || cells.Length == 0) return false;
            var ours = st.UnitsOf(side);
            for (int i = 0; i < cells.Length; i++)
            {
                bool ok = false;
                for (int j = 0; j < ours.Count; j++)
                {
                    var u = ours[j];
                    if (u == null || !u.IsAlive) continue;
                    if (u.Pos.IsValid && u.Pos.Index == cells[i]) { ok = true; break; }
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>该方存活单位数（灵力消耗 / 威力系数都按它算）。</summary>
        public static int AliveCount(BattleState st, TeamSide side)
        {
            if (st == null) return 0;
            int n = 0;
            var list = st.UnitsOf(side);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].IsAlive) n++;
            return n;
        }

        /// <summary>灵力消耗 = 存活单位数（用户："每存活一个就要多消耗一个灵力"）。</summary>
        public static int MpCostFor(BattleState st, TeamSide side) => AliveCount(st, side);

        /// <summary>威力系数：0.4 + 0.12 × 存活数（5 只 = 100%）。</summary>
        public static float PowerMulFor(int alive)
        {
            float m = 0.4f + 0.12f * alive;
            return m > 1f ? 1f : m;
        }

        /// <summary>该方的灵力池（我方 TeamMp / 敌方 EnemyMp）。</summary>
        private static int MpPoolOf(BattleState st, TeamSide side)
            => side == TeamSide.Player ? st.TeamMp : st.EnemyMp;

        // ================================================================
        //  可发动判定（UI 与结算共用）
        // ================================================================

        /// <summary>
        /// 主兽（站中宫者）此刻能不能发动 <paramref name="c"/>（只是"条件齐不齐"，不含 QTE）。
        /// 口径与 <see cref="BattleSimulator.ExecuteCombo"/> 的条件段**逐条一致**。
        /// </summary>
        public static bool CanFire(BattleState st, BattleUnit actor, ComboDef c, out string why)
        {
            why = null;
            if (st == null || actor == null || c == null || !actor.IsAlive || actor.Def == null)
            { why = "没有待令单位"; return false; }
            if (!actor.CanCastSkills) { why = actor.DisplayName + " 被封印，无法发动"; return false; }
            if (!actor.Pos.IsCenter) { why = "须站九宫格中宫（中间格）才能发动连携"; return false; }
            if (st.UsedCombos.Contains(c.Id)) { why = "本场已发动过「" + c.Name + "」"; return false; }
            if (!FormationMatched(st, actor.Side, c))
            { why = MissingShapeText(st, actor.Side); return false; }

            var cfg = st.Config;
            if (cfg.UltimateNeedsRage && actor.Rage < cfg.UltimateRageCost)
            {
                why = actor.DisplayName + " 的大招能量未满（" + (int)actor.Rage + "/"
                    + (int)cfg.UltimateRageCost + "）";
                return false;
            }
            int mp = MpCostFor(st, actor.Side);
            if (MpPoolOf(st, actor.Side) < mp)
            {
                why = "全队灵力不足 " + mp + " 点（当前 " + MpPoolOf(st, actor.Side) + "，按存活单位数消耗）";
                return false;
            }
            return true;
        }

        /// <summary>该单位此刻可以发动哪些阵形（条件齐 + 未用过）。UI 按钮用。</summary>
        public static System.Collections.Generic.List<ComboDef> AvailableFor(BattleState st, BattleUnit actor)
        {
            var list = new System.Collections.Generic.List<ComboDef>();
            if (st == null || actor == null || !actor.IsAlive || actor.CanCastSkills == false) return list;
            if (!actor.Pos.IsCenter) return list;     // 主兽须站中宫（短路，省得逐条判）

            for (int i = 0; i < Table.Length; i++)
            {
                string why;
                if (CanFire(st, actor, Table[i], out why)) list.Add(Table[i]);
            }
            return list;
        }

        /// <summary>
        /// 为什么不能发动（返回 null = 可以发动）。给界面显示"差什么"，让玩家不用猜。
        /// 优先级：① 有没有哪条能发（能就 null）→ ② 形状已够、只差资源的那条（最可操作）
        /// → ③ 都没成阵时，挑"缺格最少"的阵形报缺哪几格。
        /// </summary>
        public static string WhyNot(BattleState st, BattleUnit actor)
        {
            if (actor == null || actor.Def == null) return "没有待令单位";
            if (!actor.IsAlive) return "单位已阵亡";

            // ① 有能发的 → null
            for (int i = 0; i < Table.Length; i++)
            {
                string okWhy;
                if (CanFire(st, actor, Table[i], out okWhy)) return null;
            }
            // ② 形状已够、只差资源（元气/灵力/已用过）
            for (int i = 0; i < Table.Length; i++)
            {
                var c = Table[i];
                if (!FormationMatched(st, actor.Side, c)) continue;
                string why;
                CanFire(st, actor, c, out why);
                if (!string.IsNullOrEmpty(why)) return why;
            }
            // ③ 都没成阵 → 报最接近的那条缺哪几格
            return MissingShapeText(st, actor.Side);
        }

        // ================================================================
        //  文案（提示面板用）
        // ================================================================

        /// <summary>格号 → 中文位（0=后上 … 4=中宫 … 8=前下）。</summary>
        public static string CellName(int idx)
        {
            if (idx == 4) return "中宫";
            int row = idx / 3, col = idx % 3;
            string c = col == 2 ? "前" : (col == 1 ? "中" : "后");
            string r = row == 0 ? "上" : (row == 1 ? "中" : "下");
            return c + r;
        }

        /// <summary>阵形的形状文案（如「中宫·前上·前中·前下」；有备用形状会补"或…"）。</summary>
        public static string ShapeText(ComboDef c)
        {
            if (c == null) return "";
            string s = CellsText(c.Cells);
            if (c.CellsAlt != null && c.CellsAlt.Length > 0) s += "　或　" + CellsText(c.CellsAlt);
            return s;
        }

        private static string CellsText(int[] cells)
        {
            if (cells == null || cells.Length == 0) return "（无）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append('·');
                sb.Append(CellName(cells[i]));
            }
            return sb.ToString();
        }

        /// <summary>缺形状的提示：挑"缺格最少"的一条阵形（含备用形状），报它缺哪几格。</summary>
        private static string MissingShapeText(BattleState st, TeamSide side)
        {
            ComboDef best = null;
            int bestMiss = int.MaxValue;
            string bestMissing = null;

            for (int i = 0; i < Table.Length; i++)
            {
                var c = Table[i];
                if (c == null) continue;
                int m1;
                string s1 = MissingCells(st, side, c.Cells, out m1);
                if (m1 < bestMiss) { bestMiss = m1; best = c; bestMissing = s1; }
                if (c.CellsAlt != null && c.CellsAlt.Length > 0)
                {
                    int m2;
                    string s2 = MissingCells(st, side, c.CellsAlt, out m2);
                    if (m2 < bestMiss) { bestMiss = m2; best = c; bestMissing = s2; }
                }
            }
            if (best == null) return "当前不满足任何阵形";
            return "「" + best.Name + "」还缺 " + bestMissing + " 格有人";
        }

        private static string MissingCells(BattleState st, TeamSide side, int[] cells, out int missCount)
        {
            missCount = int.MaxValue;
            if (cells == null || cells.Length == 0) return null;
            missCount = 0;
            var ours = st.UnitsOf(side);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                bool ok = false;
                for (int j = 0; j < ours.Count; j++)
                {
                    var u = ours[j];
                    if (u == null || !u.IsAlive) continue;
                    if (u.Pos.IsValid && u.Pos.Index == cells[i]) { ok = true; break; }
                }
                if (!ok)
                {
                    missCount++;
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(CellName(cells[i]));
                }
            }
            return sb.Length == 0 ? "0" : sb.ToString();
        }
    }
}
