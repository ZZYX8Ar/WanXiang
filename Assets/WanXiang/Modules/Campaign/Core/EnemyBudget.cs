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
        public static float Base = 5.00f;

        /// <summary>每道劫律的敌方属性涨幅（BP_JIE_STEP）。</summary>
        public static float JieStep = 0.03f;

        /// <summary>每道劫律的预算上限涨幅。</summary>
        public static float CapJieStep = 0.02f;

        /// <summary>
        /// 幕系数 A（§5.1）。
        /// ⚠ 2026-09-29 按【平衡自检】（1760 局实跑）重标定：原来是 0.85~1.50，
        ///   结果「前三幕 0 遗物就 98~100% 通关、四五幕无遗物只有 43%/10%」——
        ///   坡度是阶梯断崖。现压缩为 0.98~1.36：前三幕加压力、四五幕压断崖，
        ///   让「遗物成长」的收益落在中段而不是一个 6 件的硬开关。
        ///   改这里的数值后，务必重跑《遗物系统设计文档 §11.5》的平衡自检。
        /// </summary>
        // ★ 2026-11-02 外置：数值搬到 Resources/Data/enemy_budget.csv；
        //   这里的数组只是**缺表时的内置兜底**，装表后走 Install* 写入的值。
        //   ⚠ 幕 3 的系数**故意略高于幕 4**：幕 3 的敌方规模是 4（幕 4 是 5），
        //     用系数补偿"少一个敌人"，难度才会平滑上升 —— 别按"系数必须单调"去改它。
        private static readonly float[] _actMul = { 0f, 1.08f, 1.22f, 1.34f, 1.28f, 1.36f };
        private const float DefaultActMul = 1.22f;

        public static float ActMul(int act)
        {
            if (act >= 1 && act < _actMul.Length) return _actMul[act];
            return DefaultActMul;
        }

        /// <summary>节点系数 T。守关 1.35 由 BossSquad 路径自己用。</summary>
        public static float NodeMulElite = 1.18f;   // 规模 +1 且带劫象
        public static float NodeMulEncounter = 1.00f;

        public static float NodeMul(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Elite: return NodeMulElite;
                case NodeKind.Encounter: return NodeMulEncounter;
                default: return 1.00f;               // 非战斗节点没有"敌方强度"
            }
        }

        /// <summary>
        /// 守关节点系数（Boss + 额外特性）。
        /// ⚠ 2026-10-04 由 1.35 → 1.75：守关改「单 Boss 独占中宫」后随从没了（原是随从承担大部分强度），
        ///   实测四幕对神品满编 100% 通关 = 白给（见 Task #72）。上调系数把强度补回 Boss 本体。
        ///   改这里后守关难度整体 +30%，务必重跑 boss.selftest 的平衡观测节。
        /// </summary>
        public static float BossMul = 1.75f;

        /// <summary>劫系数 K（P3 劫循环接入前恒为 1）。</summary>
        public static float JieMul(int jie) => 1f + JieStep * System.Math.Max(0, jie - 1);

        /// <summary>
        /// 单只敌人的属性倍率（作用于 生命/攻击/防御）。
        /// <paramref name="eliteExtraMul"/>：劫律 07「兽强」的精英额外乘数（默认 1）。
        /// </summary>
        public static float UnitMul(int act, NodeKind kind, int jie = 1, float eliteExtraMul = 1f)
            => ActMul(act) * NodeMul(kind) * (kind == NodeKind.Elite ? eliteExtraMul : 1f) * JieMul(jie);

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
        // 行 = 幕（1..5），列 0 = 遭遇、列 1 = 精英。外置表 squad.csv 覆盖。
        private static readonly int[,] _squad = { { 0, 0 }, { 3, 4 }, { 4, 5 }, { 4, 5 }, { 5, 5 }, { 5, 5 } };
        private static readonly int[] _bossSquad = { 0, 4, 5, 5, 5, 5 };

        public static int SquadSize(int act, NodeKind kind)
        {
            int a = act < 1 ? 1 : (act > 5 ? 5 : act);
            return _squad[a, kind == NodeKind.Elite ? 1 : 0];
        }

        /// <summary>守关规模（Boss + 随从）。</summary>
        public static int BossSquadSize(int act)
        {
            int a = act < 1 ? 1 : (act > 5 ? 5 : act);
            return _bossSquad[a];
        }

        // ================================================================
        //  外置表安装入口（解析器在 UI 层，Core 不引用 UnityEngine）
        // ================================================================

        /// <summary>覆盖基准与系数（一次装齐；未提供的传 NaN 表示保持内置）。</summary>
        public static void InstallBasics(float baseBp, float jieStep, float capJieStep,
                                         float bossMul, float nodeElite, float nodeEncounter)
        {
            if (!float.IsNaN(baseBp)) Base = baseBp;
            if (!float.IsNaN(jieStep)) JieStep = jieStep;
            if (!float.IsNaN(capJieStep)) CapJieStep = capJieStep;
            if (!float.IsNaN(bossMul)) BossMul = bossMul;
            if (!float.IsNaN(nodeElite)) NodeMulElite = nodeElite;
            if (!float.IsNaN(nodeEncounter)) NodeMulEncounter = nodeEncounter;
        }

        /// <summary>覆盖某一幕的属性系数。</summary>
        public static void InstallActMul(int act, float mul)
        {
            if (act >= 1 && act < _actMul.Length) _actMul[act] = mul;
        }

        /// <summary>覆盖某一幕的出场规模（0 表示不改）。</summary>
        public static void InstallSquad(int act, int encounter, int elite, int boss)
        {
            if (act < 1 || act > 5) return;
            if (encounter > 0) _squad[act, 0] = encounter;
            if (elite > 0) _squad[act, 1] = elite;
            if (boss > 0) _bossSquad[act] = boss;
        }

        /// <summary>当前生效的幕系数（导出器/自检用）。</summary>
        public static float ActMulOf(int act) => ActMul(act);

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
