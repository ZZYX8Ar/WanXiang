// ============================================================================
//  万相 · 天时（GDD 第三章 3.1/3.4，STEP 3）
//  ---------------------------------------------------------------------------
//  「二十四节气是战场规则库」：每个节点自带一条场地天时。本文件只定义
//  天时的**数据形状**，来源是内容链（solar_terms.json 的 fieldBuff 逐条翻译，
//  当前先在编辑器侧 WeatherCatalog 落了代表性子集，见该文件头说明）。
//
//  两类效果，通路分开：
//    ① 原子型 —— 复用 EffectAtom（回复/上状态/真伤），每回合开始/结束
//       由 WeatherResolver 结算。天时**没有施法者**，所以只用
//       PercentOfMaxHp / StatusId 这类不依赖攻击力的字段。
//    ② 规则修正型 —— 禁疗、全场伤害乘数、速度/CD/暴伤/AOE单体/五行伤害
//       这类"改规则"的，做成 WeatherDef 的查询字段，由各结算点取用
//       （BattleState 出手排序 / BattleSimulator 伤害与 CD / WeatherResolver）。
//       每接一条都要过 battle.selftest 指纹回归；还挂不进结算点的修正
//       （命中/追击/复活卵等"事件钩子型"）见 WeatherCatalog 未翻译清单。
//
//  ⭐ 可复现性红线：Weather 为 null 时战斗行为必须与旧版**逐位一致**
//    （weather.selftest 有"空天时指纹零影响"回归项）。
//    ⓪ 每个结算点的天时分支都包在 `st.Weather != null` 判空里；
//    ① 乘数默认值恰为 1/0，乘上去是 IEEE 精确恒等（v*1f == v，无舍入）；
//    ② 无天时路径的浮点运算顺序与旧版完全相同（先五行 → 暴击 → 防御 → 承伤）。
// ============================================================================

namespace WanXiang.Battle.Core
{
    /// <summary>天时作用的对象域。天时是"场地"，用**绝对阵营**（技能原子用的是相对阵营）。</summary>
    public enum WeatherScope
    {
        Both = 0,        // 全场（敌我双方）
        PlayerSide = 1,  // 仅我方
        EnemySide = 2,   // 仅敌方
    }

    /// <summary>一条天时效果：作用域 + 原子。</summary>
    public struct WeatherEffect
    {
        public WeatherScope Scope;
        public EffectAtom Atom;

        /// <summary>只在第 1 回合结算（如立春"战斗开始时"）。默认每回合结算。</summary>
        public bool Once;

        /// <summary>从第 N 回合起才结算（如春分"超过 12 回合回复量翻倍"的第二笔）。0 = 不限。</summary>
        public int MinTurn;

        /// <summary>池内目标筛选：AllAllies/AllEnemies = 全池；SingleLowestHp = 池内生命最低（霜降处决）。</summary>
        public TargetSelector Pick;

        public WeatherEffect(WeatherScope scope, EffectAtom atom, bool once = false,
                             TargetSelector pick = TargetSelector.AllAllies, int minTurn = 0)
        {
            Scope = scope; Atom = atom; Once = once; Pick = pick; MinTurn = minTurn;
        }
    }

    /// <summary>一条场地天时（一个节点的战场规则）。</summary>
    public sealed class WeatherDef
    {
        public string Id;          // 如 solar_lichun / wskill_qingyu
        public string NodeName;    // 节点名（"立春"）或天气技覆盖名（"雨"）
        public string BuffName;    // 天时名（"东风解冻"）
        public Element Element;    // 天气属性（逆天时判定用）

        public WeatherEffect[] TurnStart;   // 每回合开始结算（随 TurnStart 事件）
        public WeatherEffect[] TurnEnd;     // 每回合结束结算（随 TurnEnd 事件）

        // ---- 规则修正型（默认全部中性；每接一条过 battle.selftest 指纹回归） ----
        public bool BanHeal;                // 小雪：全场禁止治疗（护盾不禁）
        public float DamageAllMultiplier = 1f;   // 夏至：全场伤害 ×1.25（造成与受伤同乘 = 门票双方）

        // ---- 速度（大雪 -20%、冬至 +20%、召风 +15%；1 = 不变） ----
        // 单方字段而不是全域一个：谷雨这类"我方限定"的修正未来也落这里。
        public float SpeedMulPlayer = 1f;
        public float SpeedMulEnemy = 1f;

        // ---- 技能 CD 推进速度（小满我方 +30%；1 = 每回合正常 -1） ----
        // 推进速度可以 <1（变慢）。实现用确定性累积器，见 BattleUnit.CdProgressExtra。
        public float CdAdvanceMulPlayer = 1f;
        public float CdAdvanceMulEnemy = 1f;

        // ---- 暴击伤害加成（立秋 +40% 我方、白露 +30% 我方；与 BeastDef.CritDamage 相加） ----
        public float CritDamageBonusPlayer = 0f;
        public float CritDamageBonusEnemy = 0f;

        // ---- 技能形态伤害（秋分：AOE -40%、单体 +25%；按原子的目标形状判定） ----
        public float AoeDamageMul = 1f;
        public float SingleDamageMul = 1f;

        // ---- 五行伤害乘数（大暑：火 -30% 土 +30%；祷雨：火 -20%；祈晴：火 +25%） ----
        // "X 属性伤害"指伤害的五行归属（ResolveElement 的结果），与攻防双方属性无关。
        public float WoodDamageMul = 1f;
        public float FireDamageMul = 1f;
        public float EarthDamageMul = 1f;
        public float MetalDamageMul = 1f;
        public float WaterDamageMul = 1f;

        // ---- 首回合先手方伤害乘数（冬至 +50%；先手方 = 第 1 回合出手序列的第一个单位所在阵营） ----
        public float FirstTurnDamageMul = 1f;

        // ---- 火属性单位受到的持续伤害乘数（大暑"火单位灼烧减半"） ----
        // 取舍：作用于**所有** DoT 而不只灼烧 —— 本系统里按最大生命 % 折算的 DoT
        // （冰蚀/蚀）与灼烧在结算路径上是同一条，为一条规则开两个口不值得；GDD 大暑
        // 场景下场上主要是灼烧/冰蚀，误差可接受。已记录，待策划确认。
        public float FireUnitDotTakenMul = 1f;

        // ---- 护盾获取乘数（小雪"护盾效果 +50%"） ----
        public float ShieldGainMul = 1f;

        // ---- 治疗溢出转护盾（雨水：溢出量的 50% 转化为护盾；0 = 不转） ----
        // 只作用在天时自己的回复原子上（WeatherResolver 结算处），技能治疗不受影响。
        public float HealOverflowShieldRatio = 0f;

        /// <summary>
        /// 余气版：原子数值减半（GDD 3.2 规则一：强度减半，残留 2 个节点）。
        /// 规则修正型里**离散的**（禁疗）不继承 —— 全有全无的规则没有"半禁"；
        /// **乘数类的**按"1 + (m-1)/2"向中性收敛一半（×1.25 的余气是 ×1.125，不是 ×0.625）。
        /// </summary>
        public WeatherDef ScaledHalf(string newId)
        {
            return new WeatherDef
            {
                Id = newId,
                NodeName = NodeName,
                BuffName = BuffName + "·余气",
                Element = Element,
                TurnStart = ScaleAtoms(TurnStart),
                TurnEnd = ScaleAtoms(TurnEnd),
                BanHeal = false,                    // 余气不继承禁疗（取舍见上）
                DamageAllMultiplier = HalfToward1(DamageAllMultiplier),
                SpeedMulPlayer = HalfToward1(SpeedMulPlayer),
                SpeedMulEnemy = HalfToward1(SpeedMulEnemy),
                CdAdvanceMulPlayer = HalfToward1(CdAdvanceMulPlayer),
                CdAdvanceMulEnemy = HalfToward1(CdAdvanceMulEnemy),
                CritDamageBonusPlayer = CritDamageBonusPlayer * 0.5f,
                CritDamageBonusEnemy = CritDamageBonusEnemy * 0.5f,
                AoeDamageMul = HalfToward1(AoeDamageMul),
                SingleDamageMul = HalfToward1(SingleDamageMul),
                WoodDamageMul = HalfToward1(WoodDamageMul),
                FireDamageMul = HalfToward1(FireDamageMul),
                EarthDamageMul = HalfToward1(EarthDamageMul),
                MetalDamageMul = HalfToward1(MetalDamageMul),
                WaterDamageMul = HalfToward1(WaterDamageMul),
                FirstTurnDamageMul = HalfToward1(FirstTurnDamageMul),
                FireUnitDotTakenMul = HalfToward1(FireUnitDotTakenMul),
                ShieldGainMul = HalfToward1(ShieldGainMul),
                HealOverflowShieldRatio = HealOverflowShieldRatio * 0.5f,
            };
        }

        /// <summary>乘数向 1 收敛一半：1 + (m-1)/2。×1.25 ⇒ ×1.125；×0.8 ⇒ ×0.9。</summary>
        private static float HalfToward1(float m) => 1f + (m - 1f) * 0.5f;

        private static WeatherEffect[] ScaleAtoms(WeatherEffect[] list)
        {
            if (list == null) return null;
            var copy = new WeatherEffect[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var a = list[i].Atom;
                a.PercentOfMaxHp *= 0.5f;           // 按 %maxHp 的原子减半
                a.StatusStacks = CoreMath.Max(1, a.StatusStacks / 2);   // 叠层数减半（至少 1）
                copy[i] = new WeatherEffect(list[i].Scope, a);
            }
            return copy;
        }
    }

    /// <summary>天气技（GDD 3.4 逆天改势）：祷雨/祈晴/召风/移山，覆盖当前天时 3 回合。</summary>
    public sealed class WeatherSkillDef
    {
        public string Id;          // wskill_qiyu
        public string Name;        // 祈晴
        public WeatherDef Brings;  // 覆盖后的天时
    }
}
