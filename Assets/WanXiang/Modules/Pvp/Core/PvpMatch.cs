// ============================================================================
//  万相 · 异步对战（GDD STEP 3 最后一项：「分享 Base64 代码与他人的队伍对战」）
//  ---------------------------------------------------------------------------
//  这一段是分享码闭环的另一半：分享码负责"把队伍传出去"，这里负责
//  **"把对方的队伍当成敌方阵容真的打一场"**。
//
//  三条设计决定（GDD 只写了"异步对战"四个字，以下都是补位取舍）：
//
//  ① **对局种子与顺序无关**：MatchSeed(码A, 码B) == MatchSeed(码B, 码A)。
//     双方各自在本地跑，必须算**同一场**战斗 —— 否则"我这边赢了、你那边赢了"
//     的战报对不上。做法：两个码的哈希排序后再异或，顺序天然无关。
//
//  ② **PvP 单场不带天时**（weather = null）。天时是 PvE 的节气规则（战场属于
//     谁的地图），两个人的队伍在谁的节气上打没有共识；真要带也得双方都同意
//     同一个节气 —— 那是后续"约战规则"的事，这里先不带，行为上等价于一场干净的
//     阵容对拼（也因此与 battle.selftest 的基准局同构，便于对照）。
//
//  ③ **落位不做镜像**：对方码里的格子编号原样搬到我方的敌方侧。
//     0/1 前排、4 中宫、7/8 后排是**棋盘语义**，镜像会把他"后排的辅助"变成
//     "前排的靶子"，读战报时会被误导。GDD 未规定，取语义一致。
//
//  ⚠ 篡改口径（GDD 7 章风险表）：纯异步对战，篡改只影响自己 ⇒
//    解码做 id 合法性校验（ShareCode.TryDecode 已含版本/数量/越界/重复），
//    **不做签名**。对方码非法时返回 Invalid 战报，绝不带病开打。
// ============================================================================

using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Pvp
{
    /// <summary>PvP 需要的内容：异兽表、灵魂表、技能解析器（与融合管线同一份）。</summary>
    public struct PvpContent
    {
        public BeastDef[] Beasts;
        public SoulDef[] Souls;
        public SkillResolver Resolver;

        public bool IsValid => Beasts != null && Beasts.Length > 0
                            && Souls != null && Souls.Length > 0
                            && Resolver != null;
    }

    /// <summary>一场异步对战的战报（我方视角）。双方各跑一遍必然得到同一份。</summary>
    public struct PvpReport
    {
        public bool Valid;
        public string Error;         // Valid=false 时的原因
        public ulong Seed;
        public BattleOutcome Outcome;   // **我方视角**：PlayerWin 表示我赢
        public int Turns;
        public int EventCount;
        public uint Fingerprint;
        public string MySquad;       // 我方阵容摘要
        public string OppSquad;      // 对方阵容摘要

        /// <summary>可直接贴进聊天框的战报指纹行 —— 双方对照时逐字相同即"同一场"。</summary>
        public string Digest()
        {
            if (!Valid) return $"无效对局：{Error}";
            string result = Outcome == BattleOutcome.PlayerWin ? "我方胜"
                          : Outcome == BattleOutcome.EnemyWin ? "对方胜" : "平局";
            return $"[万相战报] {result}｜{Turns} 回合｜种子 {Seed}｜指纹 0x{Fingerprint:X8}"
                 + $"\n  我方：{MySquad}\n  对方：{OppSquad}";
        }
    }

    public static class PvpMatch
    {
        /// <summary>
        /// 对局种子：与参数顺序**无关**（双方各算一次必须相等）。
        /// 做法：两个码的 FNV-1a 取小/大排序后混合 —— 排序消掉了顺序信息，
        /// 再掺入两个哈希保证不同对局不会撞种子。
        /// </summary>
        public static ulong MatchSeed(string codeA, string codeB)
        {
            ulong ha = CoreMath.Fnv1a(codeA ?? "");
            ulong hb = CoreMath.Fnv1a(codeB ?? "");
            ulong lo = ha < hb ? ha : hb;
            ulong hi = ha < hb ? hb : ha;
            // SplitMix 式混合：避免 (lo,hi) 相邻导致种子相邻
            ulong x = lo ^ (hi + 0x9E3779B97F4A7C15UL + (lo << 6) + (lo >> 2));
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return x;
        }

        /// <summary>
        /// 打一场：<paramref name="mine"/> 为我方阵容，<paramref name="theirs"/> 为对方阵容。
        /// 非法内容/阵容时返回 Valid=false（绝不带病开打）。
        /// </summary>
        public static PvpReport Play(SharePayload mine, SharePayload theirs, PvpContent content,
                                     string myCode = null, string oppCode = null)
        {
            if (!content.IsValid)
                return new PvpReport { Valid = false, Error = "内容目录无效（异兽/灵魂/技能解析器不全）" };
            if (mine.UnitCount < 1 || mine.UnitCount > ShareCode.MaxUnits)
                return new PvpReport { Valid = false, Error = $"我方阵容人数非法（{mine.UnitCount}）" };
            if (theirs.UnitCount < 1 || theirs.UnitCount > ShareCode.MaxUnits)
                return new PvpReport { Valid = false, Error = $"对方阵容人数非法（{theirs.UnitCount}）" };

            var playerEntries = BuildSquad(mine, content, TeamSide.Player, out string mySummary, out string err1);
            if (playerEntries == null) return new PvpReport { Valid = false, Error = "我方：" + err1 };
            var enemyEntries = BuildSquad(theirs, content, TeamSide.Enemy, out string oppSummary, out string err2);
            if (enemyEntries == null) return new PvpReport { Valid = false, Error = "对方：" + err2 };

            ulong seed = MatchSeed(myCode ?? CodeOf(mine), oppCode ?? CodeOf(theirs));
            var st = BattleFactory.Create(BattleConfig.Default, seed, playerEntries, enemyEntries,
                                          null);   // 取舍②：PvP 单场不带天时
            var result = BattleSimulator.Run(st);

            return new PvpReport
            {
                Valid = true,
                Seed = seed,
                Outcome = result.Outcome,
                Turns = result.Turns,
                EventCount = result.EventCount,
                Fingerprint = result.Fingerprint,
                MySquad = mySummary,
                OppSquad = oppSummary,
            };
        }

        /// <summary>按分享码字符串直接打一场（内部解码 + 合法性校验）。</summary>
        public static PvpReport PlayByCode(string myCode, string oppCode,
                                           int beastCount, int soulCount, PvpContent content)
        {
            if (!ShareCode.TryDecode(myCode, beastCount, soulCount, out var mine))
                return new PvpReport { Valid = false, Error = "我方分享码非法（版本/数量/下标/落位校验未过）" };
            if (!ShareCode.TryDecode(oppCode, beastCount, soulCount, out var theirs))
                return new PvpReport { Valid = false, Error = "对方分享码非法（版本/数量/下标/落位校验未过）" };
            return Play(mine, theirs, content, myCode, oppCode);
        }

        // ================================================================

        /// <summary>
        /// 把分享载荷落成上阵指令：融合（宿主 + 灵魂）→ 站位。
        /// 融合规则与 PvE 完全同源（FusionRules.Fuse，零随机）⇒ 对方码里那只
        /// "水宿主 + 火灵魂"在这里长出来的，跟对方本地看到的一模一样。
        /// </summary>
        private static DeployEntry[] BuildSquad(SharePayload p, PvpContent content,
                                                TeamSide side, out string summary, out string error)
        {
            summary = null;
            error = null;
            int n = p.UnitCount;
            var entries = new DeployEntry[n];
            var text = new System.Text.StringBuilder();

            for (int i = 0; i < n; i++)
            {
                var slot = p.GetSlot(i);
                if (slot.BeastIndex < 0 || slot.BeastIndex >= content.Beasts.Length)
                {
                    error = $"异兽下标 {slot.BeastIndex} 越界（内容共 {content.Beasts.Length} 只）";
                    return null;
                }
                if (slot.SoulIndex < 0 || slot.SoulIndex >= content.Souls.Length)
                {
                    error = $"灵魂下标 {slot.SoulIndex} 越界（内容共 {content.Souls.Length} 个）";
                    return null;
                }

                var fused = FusionRules.Fuse(content.Beasts[slot.BeastIndex],
                                             content.Souls[slot.SoulIndex], content.Resolver);
                entries[i] = side == TeamSide.Player
                    ? DeployEntry.Player(fused, slot.BoardSlot)
                    : DeployEntry.Enemy(fused, slot.BoardSlot);

                if (i > 0) text.Append('、');
                text.Append(fused.DisplayName).Append('(').Append(slot.BoardSlot).Append(')');
            }
            summary = text.ToString();
            return entries;
        }

        private static string CodeOf(SharePayload p) => $"payload:{p.Seed}:{p.UnitCount}";
        /// <summary>
        /// 把一侧的分享码解析成上场 entries（给战斗场景/回放用）。
        /// 失败时返回 null 并通过 err 给出原因。
        /// </summary>
        public static DeployEntry[] BuildSquadOf(string code, PvpContent content, TeamSide side, out string err)
        {
            var all = content.Beasts;
            int beastCount = all != null ? all.Length : 0;
            int soulCount = content.Souls != null ? content.Souls.Length : 0;
            err = null;
            if (!ShareCode.TryDecode(code, beastCount, soulCount, out var payload))
            { err = "配对码解码失败"; return null; }
            return BuildSquad(payload, content, side, out _, out err);
        }

        /// <summary>两只码的对战种子（与 Play 内部同一公式，保证回放一致）。</summary>
        public static ulong SeedOf(string myCode, string oppCode)
            => MatchSeed(myCode ?? CodeOf(new SharePayload()), oppCode ?? CodeOf(new SharePayload()));
}
    }


