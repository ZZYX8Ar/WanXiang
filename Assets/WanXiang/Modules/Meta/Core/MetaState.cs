// ============================================================================
//  万相 · 局外进度（GDD 1.3「局外层：长期 · 囤积」）
//  ---------------------------------------------------------------------------
//  GDD 对局外层只有一行：「孵蛋获得新宿主、解锁新灵魂、扩充图鉴、分享 Base64
//  代码与他人的队伍对战」。**具体机制 GDD 没给** ⇒ 这里按项目一贯的补位方式做：
//  数据驱动 + 全程确定性 + 一处可调数值，所有取舍写在注释里。
//
//  ⭐ 两条铁律（与战斗核心同源）：
//    ① **禁系统时间**：孵蛋结果由 MetaState.Seed 决定，同种子同结果；
//    ② **不掷隐式随机**：随机流只从 (Seed ^ hash(孵次)) 派生 ——
//       连"第几次孵"都进哈希，所以"先孵 A 再孵 B"与"先孵 B 再孵 A"结果不同，
//       但同一操作序列永远复现。
//
//  ⚠ 内容无关于核心层：本模块不认识 30 只异兽的任何名字，只知道
//    "池子里有几只、各自什么稀有度"（MetaContent）。窗口/自检负责把下标映射成 BeastDef。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;

namespace WanXiang.Meta
{
    /// <summary>局外看得见的内容规模（由编辑器侧把内容目录喂进来）。</summary>
    public struct MetaContent
    {
        public int HostCount;
        public int SoulCount;
        /// <summary>逐只宿主/灵魂的稀有度（长度 = HostCount / SoulCount），决定孵蛋权重。</summary>
        public Rarity[] HostRarities;
        public Rarity[] SoulRarities;

        public bool IsValid => HostCount > 0 && SoulCount > 0
                            && HostRarities != null && HostRarities.Length == HostCount
                            && SoulRarities != null && SoulRarities.Length == SoulCount;
    }

    /// <summary>开场送的宿主数：**必须够打一局**（5 只上阵），否则局外层一开局就卡死。</summary>
    public static class MetaDefaults
    {
        public const int StarterHosts = 5;
        public const int StarterSouls = 5;

        /// <summary>孵蛋的稀有度权重（GDD 没给数值 ⇒ 占位；待策划给表后改这一处）。</summary>
        public static int WeightOf(Rarity r)
        {
            switch (r)
            {
                case Rarity.Legend: return 10;   // 神品 10%
                case Rarity.Epic: return 30;     // 玄品 30%
                case Rarity.Rare: return 60;     // 灵品 60%
                default: return 0;
            }
        }

        /// <summary>
        /// 一局结算的灵卵收益（GDD 没给数值 ⇒ 占位）：
        /// 普通节点 +1 / 守关 +3 / 通关额外 +5 / 首次抵达新幕 +2。
        /// </summary>
        public const int EggsPerNode = 1;
        public const int EggsPerBoss = 3;
        public const int EggsPerClear = 5;
        public const int EggsPerNewAct = 2;

        // ---- L4 祭坛（§8.3：数值类局外封顶 +8%）----
        public const float AltarStep = 0.016f;   // 每条 +1.6%，5 条 = +8%
        public const int AltarCost = 6;          // 升级单价（占位；GDD 没给，待策划）
        public const float MetaGainCap = 0.08f;  // 数值类局外增益总上限（MetaGainCap）
    }

    /// <summary>
    /// 局外存档状态。**可变**，但只在局外层改（战斗里不碰它）。
    /// 用 List 而不是 HashSet 存解锁：顺序要可复现、要能编码进存档码。
    /// </summary>
    public sealed class MetaState
    {
        /// <summary>局外种子：孵蛋随机流的根。禁系统时间，由玩家/存档决定。</summary>
        public ulong Seed;

        public int Eggs;              // 灵卵余额（局内货币的局外镜像，暂留）
        /// <summary>
        /// 墨铊余额 —— **局外养成的主货币**（局内也能获得，跨局积累）。
        /// 用户明确：灵卵是局内货币，局外养成的钱应该是墨铊。
        /// </summary>
        public int Ink;

        // ---- 异兽培养（阶段③）：局外永久成长，替代原"祭坛" ----
        //   ⚠ 用"下标对齐的三个 List"而不是 Dictionary：静态序列化更简单。
        //   ⚠ 当前暂未写入存档（MetaSaveCode 仍是 v2）—— 见 TODO(存档)。
        public readonly List<string> BeastIds = new List<string>(32);
        public readonly List<int> BeastLevels = new List<int>(32);
        public readonly List<bool> BeastEvolved = new List<bool>(32);

        /// <summary>某异兽的局外等级（0 = 未培养）。</summary>
        public int BeastLevelOf(string id)
        {
            int i = BeastIds.IndexOf(id);
            return i >= 0 && i < BeastLevels.Count ? BeastLevels[i] : 0;
        }
        public int HatchCount;        // 已孵次数（进哈希 ⇒ 每次孵化结果不同且可复现）
        public int RunsPlayed;
        public int RunsCompleted;
        public int BestActReached;

        public readonly List<int> UnlockedHosts = new List<int>(32);
        public readonly List<int> UnlockedSouls = new List<int>(32);

        // ---- L4 祭坛（GDD v1.1 §8.2/§8.3）：唯一"数字变大"的线，刻意封顶 +8% ----
        // 五行各一条，每条 1 级 +1.6%，5 条全点 = 8.0%。
        // 参照物：局内一次神品融合 ≈ +15%、5 同属共鸣 +25% —— 局外永远小于局内一项决策。
        public readonly int[] AltarLevels = new int[5];

        /// <summary>祭坛总加成（0 ~ 0.08）。</summary>
        public float AltarBonusTotal
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < AltarLevels.Length; i++) sum += AltarLevels[i] * MetaDefaults.AltarStep;
                return sum;
            }
        }

        /// <summary>某五行的祭坛加成（对应属性的攻击/治疗/减伤）。</summary>
        public float AltarBonusFor(int elementIndex)
            => (elementIndex >= 0 && elementIndex < AltarLevels.Length)
               ? AltarLevels[elementIndex] * MetaDefaults.AltarStep : 0f;

        /// <summary>祭坛升级：扣灵卵、逐级封顶（每条 1 级；总上限 +8% 由"每条 1 级"天然保证）。</summary>
        public bool UpgradeAltar(int elementIndex, int eggs)
        {
            if (elementIndex < 0 || elementIndex >= AltarLevels.Length) return false;
            if (AltarLevels[elementIndex] >= 1) return false;        // 已满级
            if (eggs < MetaDefaults.AltarCost) return false;
            // ★ 局外养成花【墨铊】（用户明确：灵卵是局内货币）
            Ink -= MetaDefaults.AltarCost;
            AltarLevels[elementIndex] = 1;
            return true;
        }

        // ---- L3 图鉴（§8.2）：见闻度被动。效果是"信息类"（降低认知负担），骨架先落 ----
        public readonly List<int> CodexEntries = new List<int>(40);
        public void MarkCodex(int entryId)
        {
            if (!CodexEntries.Contains(entryId)) CodexEntries.Add(entryId);
        }

        /// <summary>每解锁 5 条图鉴 → 1 个被动，共 6 个（被动效果待接 UI/战斗）。</summary>
        public int CodexPassivesUnlocked => System.Math.Min(6, CodexEntries.Count / 5);

        // ---- L5 起手（§8.2）：更好的开局（初始池/灵卵/草稿/起始季节）。效果挂账 ----
        public readonly List<int> RitesUnlocked = new List<int>(8);

        public MetaState(ulong seed)
        {
            Seed = seed;
        }

        /// <summary>新档：送够开局的宿主与灵魂，灵卵为 0（"囤积"要靠打出来）。</summary>
        public static MetaState NewGame(ulong seed, MetaContent content)
        {
            var st = new MetaState(seed);
            int hosts = System.Math.Min(MetaDefaults.StarterHosts, content.HostCount);
            int souls = System.Math.Min(MetaDefaults.StarterSouls, content.SoulCount);
            for (int i = 0; i < hosts; i++) st.UnlockedHosts.Add(i);
            for (int i = 0; i < souls; i++) st.UnlockedSouls.Add(i);
            return st;
        }

        public bool HostUnlocked(int index) => UnlockedHosts.Contains(index);
        public bool SoulUnlocked(int index) => UnlockedSouls.Contains(index);
        public int LockedHostCount(int hostCount) => hostCount - UnlockedHosts.Count;
        public int LockedSoulCount(int soulCount) => soulCount - UnlockedSouls.Count;

        /// <summary>
        /// 出战阵容：从已解锁宿主里取前 5 只。
        /// ⚠ 不足 5 只时用**未解锁池**补齐（教学期兜底：解锁数 < 5 也要能打，
        ///   否则"局外没囤够"会变成"游戏不能玩"）。补齐顺序恒为下标升序。
        /// </summary>
        public int[] BuildSquadHosts(MetaContent content, int size = 5)
        {
            var result = new List<int>(size);
            for (int i = 0; i < UnlockedHosts.Count && result.Count < size; i++)
                result.Add(UnlockedHosts[i]);
            for (int i = 0; i < content.HostCount && result.Count < size; i++)
                if (!result.Contains(i)) result.Add(i);
            return result.ToArray();
        }
    }
}
