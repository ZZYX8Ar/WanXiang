// ============================================================================
//  万相 · 战斗核心 · 基础类型
//  ---------------------------------------------------------------------------
//  这一整个程序集（WanXiang.Battle.Core）刻意设了 noEngineReferences=true：
//  **它看不见 UnityEngine**。理由有三条，都不是洁癖：
//
//    1) 「固定随机种子后战斗结果 100% 可复现」是 GDD 7 章给 STEP 1 定的第一条验收标准。
//       而不可复现的最大来源就是顺手用了 UnityEngine.Random / Time / 帧序有关的 API。
//       把引擎挡在门外，这类错误在**编译期**就暴露，不用等到线上对不上账。
//    2) 纯计算可以在编辑器 Edit 模式下同步跑完，不需要 Play 模式。
//       这台机器的播放器循环不可靠（见项目记忆），少一个依赖就少一类假故障。
//    3) 将来要出 IL2CPP 包时，热更 DLL 里能少带一点东西。
//
//  代价：不能用 Mathf / Debug / Vector2Int，得自己写。见 CoreMath。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>
    /// 五行。数值一旦定下就不要动 —— 它会被写进存档与分享码（Base64）。
    /// </summary>
    public enum Element
    {
        None = 0,
        Wood = 1,   // 木 · 春 · 句芒
        Fire = 2,   // 火 · 夏 · 祝融
        Earth = 3,  // 土 · 长夏 · 后土
        Metal = 4,  // 金 · 秋 · 蓐收
        Water = 5,  // 水 · 冬 · 禺强
    }

    /// <summary>职业定位。决定默认站位与基础面板的分配倾向。</summary>
    public enum RoleType
    {
        None = 0,
        Guard = 1,    // 御：血厚攻低，前排
        Striker = 2,  // 攻：脆但爆发，后排
        Caster = 3,   // 术：群体与场地，中/后排
        Support = 4,  // 辅：治疗护盾，中/后排
        Swift = 5,    // 疾：速度与频次，任意行
    }

    /// <summary>稀有度。影响基础面板的乘数。</summary>
    public enum Rarity
    {
        None = 0,
        Rare = 1,    // 灵品
        Epic = 2,    // 玄品
        Legend = 3,  // 神品
    }

    /// <summary>技能类型。一个单位固定三个：普攻 / 战技 / 绝技。</summary>
    public enum SkillType
    {
        Basic = 0,     // 普攻：无 CD，普通攻击
        Active = 1,    // 战技：短 CD
        Ultimate = 2,  // 绝技：长 CD，玩家的"手动干预点"
    }

    /// <summary>阵营。</summary>
    public enum TeamSide
    {
        Player = 0,
        Enemy = 1,
    }

    /// <summary>一局战斗的终局形态。</summary>
    public enum BattleOutcome
    {
        Ongoing = 0,
        PlayerWin = 1,
        EnemyWin = 2,
        Draw = 3,      // 达到回合上限仍未分出胜负
    }

    /// <summary>
    /// 原子效果类型。GDD 6.4 明确要求用组合式描述，不要给 30 只怪各写一份逻辑：
    /// 「第一版可先实现 5 个原子效果覆盖全部 24 条天时」。
    /// 技能与天时共用这一套原子。
    /// </summary>
    public enum EffectAtomKind
    {
        None = 0,
        Damage = 1,         // 造成伤害（可按倍率 / 段数 / 目标形状）
        Heal = 2,           // 治疗
        Shield = 3,         // 附加护盾
        ApplyStatus = 4,    // 施加状态（增益或减益）
        RemoveStatus = 5,   // 驱散状态
        StatModifier = 6,   // 属性增减（本条战斗内长期有效）
        Revive = 7,         // 复活
    }

    /// <summary>效果的选目标方式。棋盘只有 9 格，形状收敛成这几种就够。</summary>
    public enum TargetSelector
    {
        Self = 0,
        SingleLowestHp = 1,   // 生命最低的友/敌
        SingleHighestHp = 2,
        SingleHighestAtk = 3,
        AllEnemies = 4,
        AllAllies = 5,
        RandomEnemy = 6,
        RandomEnemyMultiHit = 7,
        AdjacentToSelf = 8,   // 自身相邻格（含对角与否由参数决定）
        AllOthers = 9,        // 除自己以外的全场（混沌的「径过」用这个）
    }

    /// <summary>
    /// 枚举 → 中文名的唯一入口。
    /// <para>
    /// ⚠ 它是 <c>public</c> 的，因为**表现层（编辑器工具与运行时视图）也要用它**。
    /// 曾经它是 internal，结果编辑器工具各自写了一份 <c>ElementText</c> / <c>OutcomeText</c> ——
    /// 这种"两行的小重复"是最容易分叉的一类：改了一处就出现"日志里写木、棋盘上画火"。
    /// </para>
    /// </summary>
    public static class Cn
    {
        public static string Of(Element e)
        {
            switch (e)
            {
                case Element.Wood: return "木";
                case Element.Fire: return "火";
                case Element.Earth: return "土";
                case Element.Metal: return "金";
                case Element.Water: return "水";
                default: return "无";
            }
        }

        public static string Of(RoleType r)
        {
            switch (r)
            {
                case RoleType.Guard: return "御";
                case RoleType.Striker: return "攻";
                case RoleType.Caster: return "术";
                case RoleType.Support: return "辅";
                case RoleType.Swift: return "疾";
                default: return "—";
            }
        }

        public static string Of(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return "灵品";
                case Rarity.Epic: return "玄品";
                case Rarity.Legend: return "神品";
                default: return "—";
            }
        }

        public static string Of(SkillType t)
        {
            switch (t)
            {
                case SkillType.Basic: return "普攻";
                case SkillType.Active: return "战技";
                case SkillType.Ultimate: return "绝技";
                default: return "—";
            }
        }

        public static string Of(TeamSide s) => s == TeamSide.Player ? "我方" : "敌方";

        public static string Of(BattleOutcome o)
        {
            switch (o)
            {
                case BattleOutcome.PlayerWin: return "我方胜";
                case BattleOutcome.EnemyWin: return "敌方胜";
                case BattleOutcome.Draw: return "平局";
                default: return "进行中";
            }
        }

        public static string Of(ElementRelation r)
        {
            switch (r)
            {
                case ElementRelation.Counter: return "克";
                case ElementRelation.Countered: return "被克";
                case ElementRelation.Same: return "同属";
                default: return "无关";
            }
        }
    }
}
