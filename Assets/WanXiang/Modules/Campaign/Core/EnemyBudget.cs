// ============================================================================
//  万相 · 敌方强度预算（GDD v1.1 §5.1~§5.5）
//  ---------------------------------------------------------------------------
//  「敌方的强度绝不能手写」—— 21 手工配置表在第二种难度就会作废。
//  本作是「池 + 预算 + 种子」三段式：
//      池   每幕一个 8~10 只候选池（内容侧维护，唯一的"内容编辑"工作）
//      预算 一条把 幕数 + 节点类型 + 劫数 换算成强度的算式（本文件）
//      种子 从池里抽谁由确定性随机决定（SeededEnemyProvider，可复现/可分享/可复盘）
//
//      m_unit  = A(幕) × T(节点类型) × K(劫)      单只敌人的属性倍率
//      K       = 1 + 0.03 × 劫数                  每道劫律 +3%
//      BP_cap  = 5.00 × A × T × (1 + 0.02 × 劫数)  阵容计价上界（自检护栏）
//      cost(u) = RARITY_COST × ROLE_COST
//
//  ⭐ 两条铁律（§5.1 原文）：
//    ① 缩放只作用于 **生命/攻击/防御** —— 速度与暴击率不参与，
//       否则高幕数会出现"敌人永远先手"的单点崩坏，玩家的速度构筑会被一次性抹平；
//    ② BP 只管"属性倍率"与"计价上界"，**不管稀有度与职业的强弱** ——
//       那些已由面板与稀有度倍率表达，重复计价会让数值失控。
// ============================================================================

using WanXiang.Battle.Core;

namespace WanXiang.Campaign
{
    public static class EnemyBudget
    {
        /// <summary>预算基准（§5.2：由最紧的一场倒推 —— 第 2 幕·小暑精英占用 98.8%）。</summary>
        public const float Base = 5.00f;

        /// <summary>每道劫律的敌方属性涨幅（BP_JIE_STEP）。</summary>
        public const float JieStep = 0.03f;

        /// <summary>每道劫律的预算上限涨幅。</summary>
        public const float CapJieStep = 0.02f;

        /// <summary>幕系数 A（§5.1）。</summary>
        public static float ActMul(int act)
        {
            switch (act)
            {
                case 1: return 0.85f;   // 教学幕，容错高
                case 2: return 1.00f;   // 基准幕
                case 3: return 1.15f;   // 开始筛阵容
                case 4: return 1.30f;   // 终局压力测试
                case 5: return 1.50f;   // 天阙（后土 + 玩家镜像）
                default: return 1.00f;
            }
        }

        /// <summary>节点系数 T。守关 1.35 由 BossSquad 路径自己用。</summary>
        public static float NodeMul(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Elite: return 1.18f;   // 规模 +1 且带劫象
                case NodeKind.Encounter: return 1.00f;
                default: return 1.00f;               // 非战斗节点没有"敌方强度"
            }
        }

        /// <summary>守关节点系数（Boss + 额外特性）。</summary>
        public const float BossMul = 1.35f;

        /// <summary>劫系数 K（P3 劫循环接入前恒为 1）。</summary>
        public static float JieMul(int jie) => 1f + JieStep * System.Math.Max(0, jie - 1);

        /// <summary>单只敌人的属性倍率（作用于 生命/攻击/防御）。</summary>
        public static float UnitMul(int act, NodeKind kind, int jie = 1)
            => ActMul(act) * NodeMul(kind) * JieMul(jie);

        /// <summary>守关的单只属性倍率。</summary>
        public static float BossUnitMul(int act, int jie = 1)
            => ActMul(act) * BossMul * JieMul(jie);

        /// <summary>阵容计价上界。</summary>
        public static float Cap(int act, NodeKind kind, int jie = 1)
            => Base * ActMul(act) * NodeMul(kind) * (1f + CapJieStep * System.Math.Max(0, jie - 1));

        /// <summary>守关的预算上界。</summary>
        public static float BossCap(int act, int jie = 1)
            => Base * ActMul(act) * BossMul * (1f + CapJieStep * System.Math.Max(0, jie - 1));

        /// <summary>
        /// 规模表（§5.5）：遭遇 幕1=3、幕2/3=4、幕4=5；精英 幕1=4、幕2~4=5。
        /// 守关 幕1=4（Boss+3）、幕2~4=5（Boss+4）见 <see cref="BossSquadSize"/>。
        /// </summary>
        public static int SquadSize(int act, NodeKind kind)
        {
            bool elite = kind == NodeKind.Elite;
            switch (act)
            {
                case 1: return elite ? 4 : 3;
                case 2: return elite ? 5 : 4;
                case 3: return elite ? 5 : 4;
                case 4: return elite ? 5 : 5;
                default: return elite ? 5 : 5;
            }
        }

        /// <summary>守关规模（Boss + 随从）。</summary>
        public static int BossSquadSize(int act) => act <= 1 ? 4 : 5;

        /// <summary>稀有度计价（与属性倍率同源 —— 一只神品的真实战力就是面板 ×1.30）。</summary>
        public static float RarityCost(Rarity r)
        {
            switch (r)
            {
                case Rarity.Legend: return 1.30f;
                case Rarity.Epic: return 1.15f;
                default: return 1.00f;
            }
        }

        /// <summary>职业计价权重（只用于预算自检，不是属性倍率）。</summary>
        public static float RoleCost(RoleType role)
        {
            switch (role)
            {
                case RoleType.Guard: return 1.30f;   // 高血量低输出，占预算多
                case RoleType.Striker: return 0.90f; // 脆但伤害高，占预算少
                case RoleType.Swift: return 0.95f;   // 速度是隐性战力
                default: return 1.00f;               // 术/辅基准
            }
        }

        /// <summary>一只敌人的计价。</summary>
        public static float Cost(BeastDef def)
            => def == null ? 0f : RarityCost(def.Rarity) * RoleCost(def.Role);

        /// <summary>一支阵容的计价总和。</summary>
        public static float TotalCost(DeployEntry[] squad)
        {
            float sum = 0f;
            if (squad == null) return 0f;
            for (int i = 0; i < squad.Length; i++) sum += Cost(squad[i].Def);
            return sum;
        }

        /// <summary>
        /// 预算自检（§5.1 的断言：Σ cost ≤ BP_cap，否则 Editor 自检报警 —— 是**护栏不是硬失败**，
        /// 提供方照样出货，报警由自检读 <see cref="Ratio"/> 触发）。
        /// </summary>
        public static float Ratio(DeployEntry[] squad, float cap)
            => cap <= 0f ? 0f : TotalCost(squad) / cap;
    }
}
