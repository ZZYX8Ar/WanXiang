// ============================================================================
//  万相 · 首领战机制钩子框架（Battle.Core）
//  ---------------------------------------------------------------------------
//  设计文档《首领战设计文档》§9~§10 的结论：14 个首领只用了 10 个可复用积木，
//  所以这里**先做积木、再做首领**—— 一套通用的战斗生命周期钩子 + 一堆可组合的机制，
//  首领只是"哪几个积木按什么参数拼"的数据（见 BossCatalog.cs）。
//
//  钩子只在生命周期点分发，且**空列表短路**：普通战斗 st.Hooks 为空，
//  BattleSimulator 的调用点全都 `if (st.Hooks.Count > 0)` 跳过，逐位不变（可复现性红线）。
//
//  生命周期点（在 BattleSimulator 里调用）：
//    · RunStart      —— 开场（免疫克制 / 双子部署）
//    · TurnStart     —— 回合始（首击标记清零 / 属性轮转 / 召唤计时）
//    · TurnEnd       —— 回合末（硬性DPS灭团 / 双子复活 / 组装成长 / 破壳窗口结算）
//    · ModifyIncoming—— 一段伤害结算**前**（首击减伤改写 incoming）
//    · OnDamageDealt —— 结算**后**（伤害反弹 / 分摊 / 假死伤害累计）
//    · OnUnitKilled  —— 单位阵亡时（共享生命 / 双杀记录 / 击杀成长）
//
//  ⚠ 确定性红线：所有触发都按回合 / 血量阈值，不引入任何额外随机流；
//    召唤/分身的属性若需随机，复用 st.Random（在同一战斗随机流里，仍是确定性）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>首领机制钩子。所有方法默认空实现，子类只重写自己关心的。</summary>
    public abstract class BattleHook
    {
        public virtual void OnBattleStart(BattleState st) { }
        public virtual void OnTurnStart(BattleState st) { }
        public virtual void OnTurnEnd(BattleState st) { }
        public virtual void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el) { }
        public virtual void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg) { }
        public virtual void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim) { }
    }

    /// <summary>把生命周期事件分发给 st.Hooks。每个方法自己短路空列表。</summary>
    public static class BattleHooks
    {
        public static void RunStart(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnBattleStart(st);
        }
        public static void TurnStart(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnTurnStart(st);
        }
        public static void TurnEnd(BattleState st)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnTurnEnd(st);
        }
        public static void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].ModifyIncoming(st, src, dst, ref dmg, el);
        }
        public static void DamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnDamageDealt(st, src, dst, dmg, dealt, trueDmg);
        }
        public static void UnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (st.Hooks.Count == 0) return;
            for (int i = 0; i < st.Hooks.Count; i++) st.Hooks[i].OnUnitKilled(st, killer, victim);
        }
    }

    // ============================================================================
    //  机制积木（§9 的 10 个 + 几个数值型辅助）
    // ============================================================================

    /// <summary>首击减伤：每回合每单位第一次受击伤害 ×(1-reduce)。白魍金身 / 魍魉隐遁 / 鸿蒙混沌护持 都靠它。</summary>
    public sealed class FirstHitReduceHook : BattleHook
    {
        private readonly float _reduce;
        private readonly HashSet<string> _hitThisTurn = new HashSet<string>();
        public FirstHitReduceHook(float reduce) { _reduce = reduce; }
        public override void OnTurnStart(BattleState st) { _hitThisTurn.Clear(); }
        public override void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (dmg <= 0) return;
            if (_hitThisTurn.Add(dst.RuntimeId))
                dmg = CoreMath.RoundDamage(dmg * (1f - _reduce));
        }
    }

    /// <summary>伤害反弹：受到的非零伤害有 ratio 比例反弹给攻击者（蔓娘荆棘 / 玄溟寒狱镜面 / 鸿蒙混沌护持用 ratio 版）。</summary>
    public sealed class DamageReflectHook : BattleHook
    {
        private readonly float _ratio;
        public DamageReflectHook(float ratio) { _ratio = ratio; }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (src == null || src == dst || !src.IsAlive || dealt <= 0) return;
            int r = CoreMath.RoundDamage(dealt * _ratio);
            if (r <= 0) return;
            int got = src.TakeDamage(r);
            if (got > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{dst.DisplayName}反弹 {got} 伤害给 {src.DisplayName}");
            if (!src.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: src.RuntimeId,
                           note: $"{src.DisplayName} 被反弹打死");
        }
    }

    /// <summary>伤害分摊：该单位受到的伤害有 ratio 比例转移给 link（白魍共鸣用）。</summary>
    public sealed class DamageSplitHook : BattleHook
    {
        private readonly BattleUnit _link;
        private readonly float _ratio;
        public DamageSplitHook(BattleUnit link, float ratio) { _link = link; _ratio = ratio; }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_link == null || !_link.IsAlive || _link == dst || dealt <= 0) return;
            int s = CoreMath.RoundDamage(dealt * _ratio);
            if (s <= 0) return;
            int got = _link.TakeDamage(s);
            if (got > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{dst.DisplayName} 分摊 {got} 给 {_link.DisplayName}");
        }
    }

    /// <summary>召唤：每 interval 回合给敌方补一批召唤物（蝮魇蔓生 / 赤魃分身 / 霜影镜影 等）。</summary>
    public sealed class SummonHook : BattleHook
    {
        private readonly int _interval;
        private readonly BeastDef[] _protos;
        private readonly int[] _cells;
        public SummonHook(int interval, BeastDef[] protos, int[] cells)
        {
            _interval = CoreMath.Max(1, interval);
            _protos = protos;
            _cells = cells;
        }
        public override void OnTurnStart(BattleState st)
        {
            if (st.Turn % _interval != 0) return;
            if (_protos == null || _cells == null) return;
            int n = CoreMath.Min(_protos.Length, _cells.Length);
            for (int i = 0; i < n; i++)
            {
                var proto = _protos[i];
                int cell = _cells[i];
                if (proto == null || cell < 0 || cell >= BoardLayout.CellCount) continue;
                if (st.SlotAt(TeamSide.Enemy, cell) != null) continue;   // 该格被占就跳过，不挤位
                DeploySummon(st, proto, cell);
            }
        }
        /// <summary>把一只召唤物放上敌方棋盘（克隆 + 占位面板 + 上阵）。</summary>
        public static void DeploySummon(BattleState st, BeastDef proto, int cell)
        {
            if (proto == null || cell < 0 || cell >= BoardLayout.CellCount) return;
            if (st.SlotAt(TeamSide.Enemy, cell) != null) return;
            var def = proto.Clone();
            st.Config.ApplyPlaceholderStats(def);          // summon_ 前缀会被跳过，保留弱面板
            var e = DeployEntry.Enemy(def, cell);
            int before = st.UnitsOf(TeamSide.Enemy).Count;
            BattleFactoryDeploy(st, e);
            if (st.UnitsOf(TeamSide.Enemy).Count > before)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"召唤 {def.DisplayName}（格 {cell}）");
        }
        private static void BattleFactoryDeploy(BattleState st, DeployEntry e)
        {
            BattleFactory.Deploy(st, new[] { e }, "E");
        }
    }

    /// <summary>阶段触发：boss 血量首次（或每次）跌破阈值时执行一次回调（枯荣 / 焚身 / 深潜 等）。</summary>
    public sealed class PhaseHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _threshold;
        private readonly Action<BattleState, BattleUnit> _onCross;
        private readonly bool _once;
        private bool _fired;
        public PhaseHook(BattleUnit boss, float threshold, Action<BattleState, BattleUnit> onCross, bool once = true)
        {
            _boss = boss; _threshold = threshold; _onCross = onCross; _once = once;
        }
        public override void OnTurnStart(BattleState st)
        {
            if (_fired && _once) return;
            if (_boss == null || !_boss.IsAlive) return;
            if (_boss.HpPercent > _threshold) return;
            _fired = true;
            _onCross(st, _boss);
        }
    }

    /// <summary>硬性 DPS 检查：到第 turnLimit 回合 boss 还活着 ⇒ 全场 AOE 直接灭团（归墟之主 / 鸿蒙）。</summary>
    public sealed class DpsTimeoutHook : BattleHook
    {
        private readonly int _turnLimit;
        public DpsTimeoutHook(int turnLimit) { _turnLimit = turnLimit; }
        public override void OnTurnEnd(BattleState st)
        {
            if (st.Turn < _turnLimit) return;
            if (st.AliveCountOf(TeamSide.Enemy) == 0) return;   // 已经打过了
            if (st.AliveCountOf(TeamSide.Player) == 0) return;  // 已经没人才不重复判
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) list[i].TakeTrueDamage(list[i].Hp + 1);
            st.Outcome = BattleOutcome.EnemyWin;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"未在第 {_turnLimit} 回合前击杀首领 ⇒ 灭团");
        }
    }

    /// <summary>属性轮转：每 interval 回合把 boss 五行按 cycle 循环切换（归墟之主五行轮转）。</summary>
    public sealed class AttributeRotateHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly Element[] _cycle;
        private readonly int _interval;
        public AttributeRotateHook(BattleUnit boss, Element[] cycle, int interval)
        {
            _boss = boss; _cycle = cycle; _interval = CoreMath.Max(1, interval);
        }
        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            if (st.Turn % _interval != 0) return;
            int idx = (st.Turn / _interval - 1) % _cycle.Length;
            if (idx < 0) idx = 0;
            var next = _cycle[idx];
            if (_boss.Element == next) return;
            _boss.Element = next;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 五行轮转为 {Cn.Of(next)}");
        }
    }

    /// <summary>同回合双杀：a/b 任一死亡记录，回合末若仅死一个且另一个存活 ⇒ 复活死者（白魍双子同命）。</summary>
    /// <summary>
    /// 双子同命（用户 2026-10-03 定案机制）：
    /// 一只倒下后**不是立刻复活**，而是开启一个 <c>windowTurns</c> 回合的击杀窗口 ——
    ///   · 窗口内把另一只也打倒 ⇒ 双杀成立，两只真正陨落（战斗结束）；
    ///   · 窗口结束仍没杀掉另一只 ⇒ 倒下的那只以 <c>revivePct</c> 血复活。
    /// 旧实现是"同回合单杀即复活"（窗口 0 回合），玩家几乎来不及反应，且不符合"几回合内"的口径。
    /// </summary>
    public sealed class KillLinkHook : BattleHook
    {
        private readonly BattleUnit _a;
        private readonly BattleUnit _b;
        private readonly float _revivePct;
        private readonly int _windowTurns;
        private BattleUnit _pendingDead;    // 已倒下、等待窗口结算的那一只（null = 无挂起）
        private int _deadTurn = -1;

        public KillLinkHook(BattleUnit a, BattleUnit b, float revivePct, int windowTurns = 2)
        {
            _a = a; _b = b; _revivePct = revivePct;
            _windowTurns = windowTurns < 0 ? 0 : windowTurns;
        }

        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (victim != _a && victim != _b) return;

            if (_pendingDead == null)
            {
                // 第一只倒下：开启击杀窗口，暂不复活
                _pendingDead = victim;
                _deadTurn = st.Turn;
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"双子同命：{victim.DisplayName} 倒下 —— {_windowTurns} 回合内击杀另一只可双杀，否则复活");
            }
            else
            {
                // 窗口内另一只也倒下 ⇒ 双杀成立，两只都真死
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"双子同命：双杀成立 —— {victim.DisplayName} 与 {_pendingDead.DisplayName} 同命陨落");
                _pendingDead = null;
                _deadTurn = -1;
            }
        }

        public override void OnTurnEnd(BattleState st)
        {
            if (_pendingDead == null) return;
            if (st.Turn - _deadTurn < _windowTurns) return;   // 窗口尚未结束，继续等玩家双杀

            var other = _pendingDead == _a ? _b : _a;
            if (other != null && other.IsAlive && !_pendingDead.IsAlive)
            {
                _pendingDead.ReviveAtHp(CoreMath.Max(1, (int)(_pendingDead.MaxHp * _revivePct)));
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"双子同命：{_pendingDead.DisplayName} 以 {(_revivePct * 100f):F0}% 血复活");
            }
            _pendingDead = null;
            _deadTurn = -1;
        }
    }

    /// <summary>假死复活：首次阵亡化为无敌冰核，_invuln 期间累计受到 ≥ dmgFrac×MaxHp 才真死，否则 revivePct 复活（玄溟碎冰重生）。</summary>
    public sealed class PhantomDeathHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _dmgFrac;
        private readonly float _revivePct;
        private bool _used;
        private bool _phantom;
        private int _accum;
        public PhantomDeathHook(BattleUnit boss, float dmgFrac, float revivePct)
        {
            _boss = boss; _dmgFrac = dmgFrac; _revivePct = revivePct;
        }
        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_used || victim != _boss || _boss.IsAlive) return;
            // 化冰核：拉回 1 点血 + 本回合无敌，等下回合结算
            _boss.Invulnerable = true;
            _boss.SetHp(CoreMath.Max(1, (int)(_boss.MaxHp * 0.01f)));
            _phantom = true;
            _accum = 0;
            // ★ HUD 持续提示：让玩家知道"没死透，要破核"（用户 2026-10-03 报"打死了又立马复活"）
            _boss.StateHint = "冰核！本回合打出 " + NeedDmg() + " 伤害可击碎（否则 " + (_revivePct * 100f).ToString("F0") + "% 血复活）";
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 碎冰重生：化为冰核（1 回合无敌）");
        }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (!_phantom || dst != _boss) return;
            _accum += dmg;   // 用"本应造成的伤害"累计（无敌已吸收）
            int need = NeedDmg();
            _boss.StateHint = "冰核 " + _accum + " / " + need + "（" + (_accum >= need ? "可击碎！" : "还差 " + (need - _accum)) + "）";
        }
        public override void OnTurnEnd(BattleState st)
        {
            if (!_phantom) return;
            _phantom = false;
            _boss.Invulnerable = false;
            _boss.StateHint = null;   // 冰核期结束 ⇒ 清掉 HUD 提示
            if (_accum >= _boss.MaxHp * _dmgFrac)
            {
                _used = true;
                _boss.SetHp(0);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 冰核被击碎 ⇒ 真死");
            }
            else
            {
                _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * _revivePct)));
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 未被破核 ⇒ 以 {(_revivePct * 100f):F0}% 血复活");
            }
        }

        /// <summary>破核所需累计伤害（= MaxHp × dmgFrac），提示与判定共用同一口径。</summary>
        private int NeedDmg() => CoreMath.Max(1, (int)(_boss.MaxHp * _dmgFrac));
    }

    /// <summary>共享生命：首领死亡时若敌方还有存活的召唤物（id 以 summon_ 开头）⇒ 以 revivePct 复活（赤魃火种同源）。</summary>
    public sealed class SharedLifeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _revivePct;
        private bool _used;
        public SharedLifeHook(BattleUnit boss, float revivePct)
        {
            _boss = boss; _revivePct = revivePct;
        }
        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_used || victim != _boss || _boss.IsAlive) return;
            var enemies = st.UnitsOf(TeamSide.Enemy);
            bool anySummon = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                var u = enemies[i];
                if (u != null && u.IsAlive && u != _boss && u.Def != null &&
                    u.Def.Id != null && u.Def.Id.StartsWith("summon_")) { anySummon = true; break; }
            }
            if (!anySummon) return;   // 火种全灭 ⇒ 真死
            _used = true;
            _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * _revivePct)));
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 火种同源：以 {(_revivePct * 100f):F0}% 血复活");
        }
    }

    /// <summary>击杀成长：场上有单位阵亡（非首领自身）⇒ 首领永久 +攻击 / +生命上限（归墟吞噬 / 烬蛟余烬）。</summary>
    public sealed class DevourGrowthHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _atkPerKill;
        private readonly float _hpPerKill;
        public DevourGrowthHook(BattleUnit boss, float atkPerKill, float hpPerKill)
        {
            _boss = boss; _atkPerKill = atkPerKill; _hpPerKill = hpPerKill;
        }
        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_boss == null || !_boss.IsAlive || victim == _boss) return;
            _boss.PermanentAttackBonus += _atkPerKill;
            if (_hpPerKill > 0f) _boss.GrowMaxHp((int)(_boss.MaxHp * _hpPerKill));
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 吞噬成长：攻击+{_atkPerKill * 100f:F0}%/生命+{_hpPerKill * 100f:F0}%");
        }
    }

    /// <summary>组装：回合末若仍持有护盾 ⇒ 下回合攻击 +atkPerTurn（可叠，无上限；千机傀儡铩）。</summary>
    public sealed class AssembleHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _atkPerTurn;
        public AssembleHook(BattleUnit boss, float atkPerTurn) { _boss = boss; _atkPerTurn = atkPerTurn; }
        public override void OnTurnEnd(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || _boss.Shield <= 0) return;
            _boss.PermanentAttackBonus += _atkPerTurn;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 组装：攻击+{_atkPerTurn * 100f:F0}%（护盾未破）");
        }
    }

    /// <summary>破壳窗口：每回合始获得 shieldFrac×MaxHp 护盾；护盾被打破的当回合受伤 +breakBonus（熔岩行尸/铩）。</summary>
    public sealed class CrustHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _shieldFrac;
        private readonly float _breakBonus;
        private bool _hadShield;
        public CrustHook(BattleUnit boss, float shieldFrac, float breakBonus)
        {
            _boss = boss; _shieldFrac = shieldFrac; _breakBonus = breakBonus;
        }
        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            _boss.AddShield((int)(_boss.MaxHp * _shieldFrac));
            _hadShield = true;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 获得护盾（{_shieldFrac * 100f:F0}% 最大生命）");
        }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_hadShield && dst == _boss && _boss.Shield <= 0)
            {
                _hadShield = false;
                _boss.AddModifier(StatKeys.DamageTaken, _breakBonus, 1);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 破壳窗口：本回合受伤+{_breakBonus * 100f:F0}%");
            }
        }
    }

    /// <summary>免疫克制：开场令 boss 不吃五行相克（混沌之母·鸿蒙混元）。</summary>
    public sealed class IgnoreCounterHook : BattleHook
    {
        private readonly BattleUnit _boss;
        public IgnoreCounterHook(BattleUnit boss) { _boss = boss; }
        public override void OnBattleStart(BattleState st)
        {
            if (_boss != null) _boss.IgnoreElementCounter = true;
        }
    }

    /// <summary>
    /// 双子同命钩子装配：确保 twinCell 上有第二只 Boss，并给两者挂 KillLink（同命）+ 共鸣分摊。
    /// ★ 双子现在**由布阵阶段（SeededEnemyProvider.BossSquadFor）预放进阵容** —— 这样编队预览
    ///   就能看到两只（用户 2026-10-03："他们两个都是 Boss，不是召唤出来的，他们是一起出现的"）。
    ///   本钩子因此退化为"查场 + 挂钩子"，只在格子为空时兜底部署（兼容旧路径）。
    /// ⚠ 别再把"格子已被占"直接当失败返回 —— 那会连钩子都不挂（曾经的真 bug）。
    /// </summary>
    public sealed class TwinSpawnHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly BeastDef _twinProto;
        private readonly int _twinCell;
        private readonly float _linkRatio;
        private readonly float _revivePct;
        private readonly int _windowTurns;
        private bool _done;
        public TwinSpawnHook(BattleUnit boss, BeastDef twinProto, int twinCell, float linkRatio,
                             float revivePct, int windowTurns = 2)
        {
            _boss = boss; _twinProto = twinProto; _twinCell = twinCell;
            _linkRatio = linkRatio; _revivePct = revivePct; _windowTurns = windowTurns;
        }
        public override void OnBattleStart(BattleState st)
        {
            if (_done || _boss == null || !_boss.IsAlive) return;
            _done = true;
            if (_twinCell < 0 || _twinCell >= BoardLayout.CellCount) return;

            var twin = st.SlotAt(TeamSide.Enemy, _twinCell);
            if (twin == null)
            {
                // 兜底：布阵没预放时在此部署（旧路径 / 手工构造的测试战斗）
                if (_twinProto == null) return;
                SummonHook.DeploySummon(st, _twinProto, _twinCell);
                twin = st.SlotAt(TeamSide.Enemy, _twinCell);
            }
            if (twin == null) return;

            // 共鸣：分摊 30%；双子同命：一只倒下后 windowTurns 回合内未双杀则 50% 复活
            st.Hooks.Add(new DamageSplitHook(_boss, _linkRatio));
            st.Hooks.Add(new DamageSplitHook(twin, _linkRatio));
            st.Hooks.Add(new KillLinkHook(_boss, twin, _revivePct, _windowTurns));
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 与双子 {twin.DisplayName} 同时入场（共鸣分摊 / 同命）");
        }
    }

    // ============================================================================
    //  数值型 / 次要机制积木（首领专属，不走通用属性系统）
    // ============================================================================

    /// <summary>汲养：boss 每次造成非零伤害，按 ratio 回血（蔓娘）。</summary>
    public sealed class LifestealHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _ratio;
        public LifestealHook(BattleUnit boss, float ratio) { _boss = boss; _ratio = ratio; }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || !_boss.IsAlive || src != _boss || dealt <= 0) return;
            int h = _boss.Heal(CoreMath.RoundDamage(dealt * _ratio));
            if (h > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 汲养回复 {h}");
        }
    }

    /// <summary>附烧：boss 每次命中玩家 ⇒ 给目标叠 1 层灼烧（烬蛟灼烧叠层）。</summary>
    public sealed class BurnOnHitHook : BattleHook
    {
        private readonly BattleUnit _boss;
        public BurnOnHitHook(BattleUnit boss) { _boss = boss; }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || src != _boss || dst == null || !dst.IsAlive) return;
            if (dst.Side == _boss.Side) return;   // 不烧自己人
            dst.ApplyStatus(StatusCatalog.Burn, 1, 2, _boss.Attack * 0.04f);
        }
    }

    /// <summary>斩杀最低血：每 interval 回合对当前生命最低的我方单位额外造成 power×boss攻击 的伤害（霜锋剜心 / 溟鲲吞舟）。</summary>
    public sealed class LowestHpNukeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        private readonly float _power;
        public LowestHpNukeHook(BattleUnit boss, int interval, float power)
        {
            _boss = boss; _interval = CoreMath.Max(1, interval); _power = power;
        }
        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || st.Turn % _interval != 0) return;
            var list = st.UnitsOf(TeamSide.Player);
            BattleUnit lowest = null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                if (lowest == null || u.Hp < lowest.Hp) lowest = u;
            }
            if (lowest == null) return;
            int d = BattleSimulator.ComputeDamage(st, _boss, lowest, _boss.Element, _power, false, false);
            int dealt = lowest.TakeDamage(d);
            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: lowest.RuntimeId,
                       amount: d, element: _boss.Element, note: $"{_boss.DisplayName} 斩杀最低血");
            if (dealt > 0 && !lowest.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: lowest.RuntimeId,
                           note: $"{lowest.DisplayName} 被斩杀");
        }
    }

    /// <summary>灵力吸取：每回合末我方全队灵力 -amount（溟鲲潮汐）。</summary>
    public sealed class MpDrainHook : BattleHook
    {
        private readonly int _amount;
        public MpDrainHook(int amount) { _amount = amount; }
        public override void OnTurnEnd(BattleState st)
        {
            if (_amount <= 0) return;
            st.TeamMp = CoreMath.Max(0, st.TeamMp - _amount);
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve, note: $"潮汐：我方灵力 -{_amount}");
        }
    }

    /// <summary>死亡 AOE：boss 阵亡时对所有我方造成 frac×boss最大生命 的伤害（溟鲲鲸落）。</summary>
    public sealed class DeathAoeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _frac;
        private bool _used;
        public DeathAoeHook(BattleUnit boss, float frac) { _boss = boss; _frac = frac; }
        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_used || victim != _boss) return;
            _used = true;
            int dmg = CoreMath.RoundDamage(_boss.MaxHp * _frac);
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int dealt = u.TakeDamage(dmg);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: u.RuntimeId,
                           amount: dmg, element: _boss.Element, note: $"{_boss.DisplayName} 死亡 AOE");
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 死于鲸落");
            }
        }
    }

    /// <summary>死亡召唤：boss 首次阵亡时在指定格召唤一只 proto（燋彘熔核 / 归墟可复用）。</summary>
    public sealed class DeathSummonHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly BeastDef _proto;
        private readonly int _cell;
        private bool _used;
        public DeathSummonHook(BattleUnit boss, BeastDef proto, int cell) { _boss = boss; _proto = proto; _cell = cell; }
        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_used || victim != _boss) return;
            _used = true;
            SummonHook.DeploySummon(st, _proto, _cell);
        }
    }

    /// <summary>减速光环：每回合始给我方全体叠 speed -penalty（鏜影寒渊）。modifier turns=1，回合自然衰减后由下回合补。</summary>
    public sealed class SpeedAuraHook : BattleHook
    {
        private readonly float _penalty;
        public SpeedAuraHook(float penalty) { _penalty = penalty; }
        public override void OnTurnStart(BattleState st)
        {
            if (_penalty <= 0f) return;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) list[i].AddModifier(StatKeys.Speed, -_penalty, 1);
        }
    }

    /// <summary>随从回血：回合末若敌方还有存活召唤物，boss 回 frac×最大生命（归墟终焉之卵）。</summary>
    public sealed class AllyPresenceRegenHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _frac;
        public AllyPresenceRegenHook(BattleUnit boss, float frac) { _boss = boss; _frac = frac; }
        public override void OnTurnEnd(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || _frac <= 0f) return;
            var enemies = st.UnitsOf(TeamSide.Enemy);
            bool any = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                var u = enemies[i];
                if (u != null && u.IsAlive && u != _boss && u.Def != null &&
                    u.Def.Id != null && u.Def.Id.StartsWith("summon_")) { any = true; break; }
            }
            if (!any) return;
            int h = _boss.Heal(CoreMath.RoundDamage(_boss.MaxHp * _frac));
            if (h > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve, note: $"{_boss.DisplayName} 随从回血 {h}");
        }
    }
}
