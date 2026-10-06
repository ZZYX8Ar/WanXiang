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
            // ⛔ 只反弹"**敌人挨打**"这一侧。原实现不看 dst 的阵营 ⇒ 首领用 AOE 打我方时，
            //    会把自己打出去的伤害按比例"反弹"给自己（等于自伤），与设计"受到的伤害反弹给攻击者"相反。
            if (dst == null || dst.Side != TeamSide.Enemy) return;
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
            // ⛔ 必须用 DeploySpawn（唯一 id）：用 "E" 前缀会让召唤物拿到 "E0" 与开场第一个敌人撞号，
            //   表现层按 RuntimeId 建 view ⇒ 召唤物不可见、伤害数字/血条错位到 Boss 身上。
            BattleFactory.DeploySpawn(st, new[] { e });
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

    /// <summary>
    /// 组装：回合末若仍持有护盾 ⇒ 下回合攻击 +atkPerTurn（千机傀儡铩）。**受 maxBonus 封顶**。
    /// ⛔ 文档写"可叠、无上限"，但实测那是**滚雪球致死**：护盾每回合刷新 25% 最大生命，
    ///   玩家破不掉就每回合 +15% 攻击、永不回头 ⇒ 铩对满编神品队胜率恒为 0%，
    ///   且把玩家成长倍率从 1.6 拉到 2.2 **仍然是 0%**（机制不可解，不是数值问题）。
    ///   ⇒ 加上限；只在上限那一回合记一条日志，避免刷屏。
    /// </summary>
    public sealed class AssembleHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _atkPerTurn;
        private readonly float _maxBonus;
        public AssembleHook(BattleUnit boss, float atkPerTurn, float maxBonus = 0.90f)
        { _boss = boss; _atkPerTurn = atkPerTurn; _maxBonus = maxBonus; }

        public override void OnTurnEnd(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || _boss.Shield <= 0) return;
            if (_boss.PermanentAttackBonus >= _maxBonus)
            {
                if (_boss.PermanentAttackBonus - _atkPerTurn < _maxBonus)
                    st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                               note: $"{_boss.DisplayName} 组装已达上限 +{_maxBonus * 100f:F0}%（护盾仍未被破）");
                return;
            }
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

    /// <summary>
    /// 冰晶重生（玄溟专属；用户 2026-10-03 定案，**替代旧的"碎冰重生"**）。
    ///
    /// 流程：
    ///   ① 玄溟被打死 ⇒ **本体退场**（保持阵亡，不在棋盘上、不再是攻击目标），
    ///      同时在**十字格**（中宫格 4 的上下左右 = 1 / 7 / 3 / 5）生成 4 枚**冰晶**；
    ///   ② 冰晶是**可被攻击的独立单位**，每枚 HP = Boss 满血 × <c>crystalHpFrac</c>（默认 25%）
    ///      ⇒ 4 枚合计 ≈ 1 个 Boss 的血量，必须拿出 AOE 才清得完；
    ///   ③ 玩家须在 <c>windowTurns</c> 回合内打光全部冰晶 ⇒ Boss **真死**；
    ///   ④ 超时未打光 ⇒ Boss 复活回归中宫，血量依次 <c>revivePcts</c>
    ///      = **50% → 25% → 0%（第 3 次直接陨落，不再复活）**；
    ///      冰晶**不消失**（继续留在场上），且 Boss 每次复活时**冰晶满血**。
    ///
    /// ⚠ 与旧 <see cref="PhantomDeathHook"/> 的关键区别：
    ///   · 旧版"1 回合无敌 + 本回合打出 25% 伤害破核"，玩家**看不到目标**（用户报"打不死"）；
    ///   · 旧版 `_used` 只在破核成功时置位 ⇒ **理论上可无限假死**。
    ///     新版用 <c>revivePcts</c> 的长度封顶 ⇒ 第 3 次必定真死，**结构上不可能无限循环**。
    /// </summary>
    public sealed class IceCrystalRebirthHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly BeastDef _crystalProto;
        private readonly int[] _cells;          // 冰晶格位（十字格）
        private readonly int _windowTurns;      // 打光冰晶的回合窗口
        private readonly float[] _revivePcts;   // 依次复活血量（<=0 表示直接陨落）
        private readonly float _crystalHpFrac;  // 每枚冰晶 HP / Boss MaxHp
        private readonly List<BattleUnit> _crystals = new List<BattleUnit>();

        private int _cycle;        // 已超时复活次数（= 下次取 revivePcts 的下标）
        private int _deadTurn = -1;
        private bool _active;      // 是否处于「冰晶期」

        public IceCrystalRebirthHook(BattleUnit boss, BeastDef crystalProto, int[] cells,
                                    int windowTurns, float[] revivePcts, float crystalHpFrac = 0.25f)
        {
            _boss = boss; _crystalProto = crystalProto; _cells = cells;
            _windowTurns = windowTurns < 1 ? 1 : windowTurns;
            _revivePcts = revivePcts;
            _crystalHpFrac = crystalHpFrac;
        }

        /// <summary>
        /// 写 Boss 头顶的机制提示（表现层读 <c>BattleUnit.StateHint</c>，随帧流走）。
        /// ⛔ 必须**顺手抓一帧**：提示不进日志，而帧只在 `Log.Add` 时抓 ⇒
        ///   若只赋值不抓帧，这条提示要等到**下一个事件**才进帧流（约 0.3~1 秒），
        ///   如果死亡恰好是该回合最后一步、或玩家同回合把冰晶清完，观感就是"提示根本没出现"。
        ///   （`BoardRules` 也有直接调 `st.CaptureFrame()` 的先例，这里是同一手法。）
        /// </summary>
        private void SetHint(BattleState st, string s)
        {
            if (_boss == null) return;
            if (_boss.StateHint == s) return;     // 值没变就不抓帧（CaptureFrame 自身也会短路）
            _boss.StateHint = s;
            if (st != null) st.CaptureFrame();
        }

        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_active || victim != _boss) return;
            // 本体退场（保持阵亡）+ 生成满血冰晶，开始冰晶期
            _active = true;
            _deadTurn = st.Turn;
            EnsureCrystals(st, fullHeal: true);
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 碎成 {_crystals.Count} 枚冰晶（{_windowTurns} 回合内打光可破）");
            SetHint(st, $"冰晶期 · 还有 {_windowTurns} 回合复活");
        }

        public override void OnTurnEnd(BattleState st)
        {
            if (!_active) return;

            // ① 冰晶全灭 ⇒ 真死（Boss 保持阵亡）
            if (AllCrystalsDead())
            {
                _active = false;
                SetHint(st, null);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"冰晶尽碎 ⇒ {_boss.DisplayName} 真正陨落");
                return;
            }

            // ② 窗口未满 ⇒ 继续等玩家清冰晶
            int elapsed = st.Turn - _deadTurn;
            if (elapsed < _windowTurns)
            {
                SetHint(st, $"冰晶期 · 还有 {_windowTurns - elapsed} 回合复活");
                return;
            }

            // ③ 超时 ⇒ 按序列复活（<=0 则直接陨落）
            float pct = (_revivePcts != null && _revivePcts.Length > 0)
                ? _revivePcts[System.Math.Min(_cycle, _revivePcts.Length - 1)] : 0f;
            _cycle++;
            _active = false;
            SetHint(st, null);

            if (pct <= 0f)
            {
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"冰晶第 {_cycle} 次未被破 ⇒ {_boss.DisplayName} 力竭陨落（不再复活）");
                return;   // Boss 保持阵亡
            }

            _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * pct)));
            EnsureCrystals(st, fullHeal: true);   // 冰晶不消失 + 全部满血
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 以 {(pct * 100f):F0}% 血复活（冰晶已复原）");
        }

        /// <summary>冰晶是否全部阵亡（且确实生成过）。</summary>
        private bool AllCrystalsDead()
        {
            if (_crystals.Count == 0) return false;
            for (int i = 0; i < _crystals.Count; i++)
                if (_crystals[i] != null && _crystals[i].IsAlive) return false;
            return true;
        }

        /// <summary>
        /// 首次生成冰晶，或在 Boss 复活时把它们复原到满血。
        /// ⚠ 冰晶血量用 <c>GrowMaxHp</c> 对齐到「Boss 满血 × crystalHpFrac」——
        ///   proto 的 BaseHp 只是个占位（不然会随守关倍率走，比例就错了）。
        /// </summary>
        private void EnsureCrystals(BattleState st, bool fullHeal)
        {
            if (_crystals.Count == 0)
            {
                if (_cells == null || _crystalProto == null) return;
                int targetHp = CoreMath.Max(1, (int)(_boss.MaxHp * _crystalHpFrac));
                for (int i = 0; i < _cells.Length; i++)
                {
                    SummonHook.DeploySummon(st, _crystalProto, _cells[i]);
                    var u = st.SlotAt(TeamSide.Enemy, _cells[i]);
                    if (u == null) continue;
                    u.GrowMaxHp(targetHp - u.MaxHp);
                    u.SetHp(u.MaxHp);
                    _crystals.Add(u);
                }
                return;
            }

            if (!fullHeal) return;
            for (int i = 0; i < _crystals.Count; i++)
            {
                var u = _crystals[i];
                if (u == null) continue;
                if (u.IsAlive) u.SetHp(u.MaxHp);
                else u.ReviveAtHp(u.MaxHp);
            }
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

    // ============================================================================
    //  2026-10-06 补齐：《首领战设计文档 v1.0》里**设计了却没实现**的机制
    //  ---------------------------------------------------------------------------
    //  背景（用户报障"一、二幕首领像小兵、只会普攻"）：原先只实现了"好做"的积木
    //  （召唤 / 反弹 / 护盾 / 阶段），凡是要「叠状态 + 判阈值 + 周期 AOE」的一律没做。
    //  这里全部用**现成的状态系统**实现，不新增系统：
    //    · 根缚 = StatusCatalog.Root（PreventsAction，无法行动）
    //    · 瘴气 = StatusCatalog.Miasma（每层攻击 −3%）
    //    · 灼烧 / 冰蚀 = 现成 DoT；冻结 / 沉默 = 现成
    // ============================================================================

    /// <summary>
    /// 首领**造成**的伤害乘区（生机：每只召唤物 +6%；燎原：每层灼烧 +3%）。
    /// 走 <see cref="BattleHook.ModifyIncoming"/>，只在"自己是攻击方"时改写 dmg —— 不碰别人的结算。
    /// </summary>
    public sealed class OutgoingDamageBonusHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly Func<BattleState, BattleUnit, float> _bonus;
        public OutgoingDamageBonusHook(BattleUnit boss, Func<BattleState, BattleUnit, float> bonus)
        { _boss = boss; _bonus = bonus; }

        public override void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (src != _boss || dmg <= 0 || _bonus == null) return;
            float b = _bonus(st, dst);
            if (b <= 0f) return;
            dmg = CoreMath.RoundDamage(dmg * (1f + b));
        }
    }

    /// <summary>
    /// 首领**开场给灵力**（否则前两回合只能普攻：EnemyMp 从 0 起、每回合 +2、战技要 3）。
    /// ⚠ 只对首领生效（在 AttachHooks 里挂）⇒ 普通遭遇战的敌人行为完全不变。
    /// </summary>
    public sealed class BossOpeningMpHook : BattleHook
    {
        private readonly int _mp;
        public BossOpeningMpHook(int mp) { _mp = mp; }
        public override void OnBattleStart(BattleState st)
        {
            if (st.EnemyMp < _mp) st.EnemyMp = _mp;
        }
    }

    /// <summary>根缚：每 interval 回合随机缚住 1 名我方单位 1 回合（无法行动）。蝮魇 / 蔓娘。</summary>
    public sealed class RootHook : BattleHook
    {
        private readonly int _interval;
        public RootHook(int interval) { _interval = CoreMath.Max(1, interval); }
        public override void OnTurnStart(BattleState st)
        {
            if (st.Turn % _interval != 0) return;
            var list = st.UnitsOf(TeamSide.Player);
            var alive = new List<BattleUnit>();
            for (int i = 0; i < list.Count; i++) if (list[i].IsAlive) alive.Add(list[i]);
            if (alive.Count == 0) return;
            var t = alive[st.Random.NextInt(0, alive.Count)];
            t.ApplyStatus(StatusCatalog.Root, 1, 1);   // PreventsAction 会把 turns 自动 +1 ⇒ 覆盖本回合
            st.Log.Add(st.Turn, BattleEventKind.StatusApplied, targetId: t.RuntimeId,
                       note: $"{t.DisplayName} 被根缚缠住（本回合无法行动）");
        }
    }

    /// <summary>瘴气：每回合结束给全体我方叠 stacks 层「瘴气」（每层攻击 −3%，可叠到 10）。魍魉。</summary>
    public sealed class MiasmaHook : BattleHook
    {
        private readonly int _stacks;
        public MiasmaHook(int stacks) { _stacks = CoreMath.Max(1, stacks); }
        public override void OnTurnEnd(BattleState st)
        {
            var list = st.UnitsOf(TeamSide.Player);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) { list[i].ApplyStatus(StatusCatalog.Miasma, _stacks, 99); n++; }
            if (n > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"瘴气弥漫：全体我方 +{_stacks} 层（攻击 −{_stacks * 2}%/层，共 {n} 人）");
        }
    }

    /// <summary>瘴爆：首领阵亡时，按我方各自当前瘴气层数 × pct 最大生命 造成伤害。魍魉。</summary>
    public sealed class MiasmaBurstHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _pctPerStack;
        private bool _used;
        public MiasmaBurstHook(BattleUnit boss, float pctPerStack) { _boss = boss; _pctPerStack = pctPerStack; }

        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (_used || victim != _boss) return;
            _used = true;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int layers = u.GetStacks(StatusCatalog.Miasma);
                if (layers <= 0) continue;
                int dmg = CoreMath.RoundDamage(u.MaxHp * _pctPerStack * layers);
                int dealt = u.TakeDamage(dmg);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: u.RuntimeId,
                           amount: dmg, element: _boss.Element, note: $"瘴爆（{layers} 层瘴气）");
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 死于瘴爆");
            }
        }
    }

    /// <summary>散瘴：每 interval 回合清除首领自身全部负面，并把全体我方的瘴气层数**翻倍**。魍魉。</summary>
    public sealed class ScatterMiasmaHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        public ScatterMiasmaHook(BattleUnit boss, int interval) { _boss = boss; _interval = CoreMath.Max(1, interval); }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || st.Turn % _interval != 0) return;
            int cleared = 0;
            for (int i = _boss.Statuses.Count - 1; i >= 0; i--)
                if (_boss.Statuses[i].Def.IsDebuff) { _boss.Statuses.RemoveAt(i); cleared++; }

            var list = st.UnitsOf(TeamSide.Player);
            int doubled = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int cur = u.GetStacks(StatusCatalog.Miasma);
                if (cur <= 0) continue;
                u.ApplyStatus(StatusCatalog.Miasma, cur, 99);   // 再加 cur 层 = 翻倍（MaxStacks 封顶）
                doubled++;
            }
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 散瘴：清除自身 {cleared} 个负面，{doubled} 人的瘴气翻倍");
        }
    }

    /// <summary>
    /// 连环斩：对**同一目标**连续攻击时，每次伤害递增 +perHit（换目标立刻清零）。白魍 / 霜锋。
    /// 实现：只保留"最后一个被打的目标"计数 —— 这就是"连续"的语义。
    /// ⛔ **连击数封顶 maxStreak**：原实现无上限，而首领的普攻/战技都是 AOE，
    ///   一轮下来"最后一个被打的目标"固定 ⇒ 连击数**每回合 +1、永不回头**，
    ///   十几回合后就是 +100% 起步。实测霜锋 12 回合就打穿满编神品队。
    /// </summary>
    public sealed class StreakDamageHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _perHit;
        private readonly int _maxStreak;
        private readonly Dictionary<string, int> _streak = new Dictionary<string, int>();
        public StreakDamageHook(BattleUnit boss, float perHit, int maxStreak = 3)
        { _boss = boss; _perHit = perHit; _maxStreak = CoreMath.Max(1, maxStreak); }

        public override void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (src != _boss || dmg <= 0 || dst == null) return;
            string id = dst.RuntimeId;
            int n;
            _streak.TryGetValue(id, out n);
            _streak.Clear();                 // 只留最后一个目标 ⇒ 换人就断连击
            if (n < _maxStreak) _streak[id] = n + 1;
            else _streak[id] = _maxStreak;
            if (n > 0) dmg = CoreMath.RoundDamage(dmg * (1f + _perHit * n));
        }
    }

    /// <summary>
    /// 缠丝：被**同一单位**连续攻击时，第 2 次起对该单位的反击额外 +bonus。蔓娘。
    /// （与 <see cref="DamageReflectHook"/> 同一套反弹口径，只是倍率随风向递增。）
    /// </summary>
    public sealed class ThornStreakHook : BattleHook
    {
        private readonly float _ratio;
        private readonly float _bonus;
        private string _lastSrc;
        private int _streak;
        public ThornStreakHook(float ratio, float bonus) { _ratio = ratio; _bonus = bonus; }

        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (src == null || src == dst || !src.IsAlive || dealt <= 0) return;
            if (dst == null || dst.Side != TeamSide.Enemy) return;   // 只反弹"敌人挨打"
            if (_lastSrc == src.RuntimeId) _streak++;
            else { _lastSrc = src.RuntimeId; _streak = 0; }
            float r = _ratio + _bonus * _streak;
            int reflect = CoreMath.RoundDamage(dealt * r);
            if (reflect <= 0) return;
            int got = src.TakeDamage(reflect);
            if (got > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"缠丝反击 ×{1 + _streak}：{dst.DisplayName} 反伤 {got} 给 {src.DisplayName}");
            if (!src.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: src.RuntimeId, note: $"{src.DisplayName} 被反伤打死");
        }
    }

    /// <summary>焚身爆裂：召唤物 / 分身阵亡时，对全体我方造成其各自 pct 最大生命的伤害并叠 1 层灼烧。赤魃。</summary>
    public sealed class SummonDeathBurstHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _pct;
        public SummonDeathBurstHook(BattleUnit boss, float pct) { _boss = boss; _pct = pct; }

        public override void OnUnitKilled(BattleState st, BattleUnit killer, BattleUnit victim)
        {
            if (victim == null || victim == _boss) return;
            if (victim.Def == null || victim.Def.Id == null || !victim.Def.Id.StartsWith("summon_")) return;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int dmg = CoreMath.RoundDamage(u.MaxHp * _pct);
                int dealt = u.TakeDamage(dmg);
                u.ApplyStatus(StatusCatalog.Burn, 1, 2, dmg * 0.25f);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: u.RuntimeId,
                           amount: dmg, element: Element.Fire, note: "焚身爆裂（分身崩解）");
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 死于焚身爆裂");
            }
        }
    }

    /// <summary>
    /// 灼烧叠层（含「焚身」阶段）：命中给目标叠 stacks 层灼烧；
    /// 首领血量 ≤ lowHpPct 后叠层速度**翻倍**。烬蛟（取代旧 <see cref="BurnOnHitHook"/>）。
    /// </summary>
    public sealed class BurnOnHitPhaseHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _lowHpPct;
        private readonly float _perStackPower;
        public BurnOnHitPhaseHook(BattleUnit boss, float lowHpPct, float perStackPower)
        { _boss = boss; _lowHpPct = lowHpPct; _perStackPower = perStackPower; }

        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || src != _boss || dst == null || !dst.IsAlive) return;
            if (dst.Side == _boss.Side) return;                    // 不烧自己人
            int stacks = _boss.HpPercent <= _lowHpPct ? 2 : 1;     // 焚身：≤40% 叠层翻倍
            dst.ApplyStatus(StatusCatalog.Burn, stacks, 2, _boss.Attack * _perStackPower);
        }
    }

    /// <summary>
    /// 引燃：**我方任一单位的灼烧层数** ≥ threshold 时引爆 —— 该单位每层受 pct 最大生命伤害并清空其灼烧。烬蛟。
    ///
    /// ⛔ 判据用"**单人最高层数**"而不是文档写的"全场总层数"：烬蛟的附烧是 AOE 命中，
    ///   一次出手就给 5 人各叠 1~2 层 ⇒ 按总层数判（≥8）等于**第一回合就引爆、之后每回合都炸**，
    ///   实测把第 2 幕胜率直接压到 0%（且加大玩家成长倍率也救不回来 = 机制不可解）。
    ///   按单人口径后：玩家只需盯住"谁的灼烧最高"去治/净化 —— 与文档"别让单人叠太高"的应对一致。
    /// 检查点放在回合末：玩家有整个回合决定"清层还是硬吃"。
    /// </summary>
    public sealed class BurnDetonateHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _threshold;
        private readonly float _pctPerStack;
        public BurnDetonateHook(BattleUnit boss, int threshold, float pctPerStack)
        { _boss = boss; _threshold = threshold; _pctPerStack = pctPerStack; }

        public override void OnTurnEnd(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            var list = st.UnitsOf(TeamSide.Player);
            int worst = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive)
                {
                    int n = list[i].GetStacks(StatusCatalog.Burn);
                    if (n > worst) worst = n;
                }
            if (worst < _threshold) return;

            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 引燃！最高灼烧 {worst} 层 ⇒ 引爆");
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int layers = u.GetStacks(StatusCatalog.Burn);
                if (layers <= 0) continue;
                int dmg = CoreMath.RoundDamage(u.MaxHp * _pctPerStack * layers);
                int dealt = u.TakeDamage(dmg);
                u.RemoveStatus(StatusCatalog.Burn);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: u.RuntimeId,
                           amount: dmg, element: Element.Fire, note: $"引燃（{layers} 层）");
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 被引燃焚尽");
            }
        }
    }

    /// <summary>周期 AOE：每 interval 回合对全体我方造成各自 pct 最大生命的伤害（岩浆喷发 / 通用）。</summary>
    public sealed class PeriodicNukeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        private readonly float _pct;
        private readonly string _label;
        public PeriodicNukeHook(BattleUnit boss, int interval, float pct, string label)
        { _boss = boss; _interval = CoreMath.Max(1, interval); _pct = pct; _label = label; }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || st.Turn % _interval != 0) return;
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                int dmg = CoreMath.RoundDamage(u.MaxHp * _pct);
                int dealt = u.TakeDamage(dmg);
                st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: u.RuntimeId,
                           amount: dmg, element: _boss.Element, note: _label);
                if (dealt > 0 && !u.IsAlive)
                    st.Log.Add(st.Turn, BattleEventKind.Death, targetId: u.RuntimeId, note: $"{u.DisplayName} 死于{_label}");
            }
        }
    }

    /// <summary>齿轮反击：首领护盾被打破时，对**破盾者**造成其自身攻击 × ratio 的反伤。铩。</summary>
    public sealed class ShieldBreakReflectHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _ratio;
        private string _brokenBy;
        private bool _hadShield;
        public ShieldBreakReflectHook(BattleUnit boss, float ratio) { _boss = boss; _ratio = ratio; }

        public override void OnTurnStart(BattleState st) { _hadShield = _boss != null && _boss.Shield > 0; }

        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || dst != _boss || src == null) return;
            if (!_hadShield || _boss.Shield > 0) return;
            _hadShield = false;
            _brokenBy = src.RuntimeId;
            int back = CoreMath.RoundDamage(src.Attack * _ratio);
            int got = src.TakeDamage(back);
            if (got > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 齿轮反击：{src.DisplayName} 破盾受 {got} 反伤");
            if (!src.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: src.RuntimeId, note: $"{src.DisplayName} 被齿轮绞碎");
        }
    }

    /// <summary>
    /// 吞舟：每 interval 回合对**当前生命最高**的我方造成其**当前生命** × pct 的伤害。溟鲲。
    /// （旧实现误用了 LowestHpNukeHook ⇒ 打的是血最少的，与设计相反。）
    /// </summary>
    public sealed class HighestHpNukeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        private readonly float _pctCurrent;
        public HighestHpNukeHook(BattleUnit boss, int interval, float pctCurrent)
        { _boss = boss; _interval = CoreMath.Max(1, interval); _pctCurrent = pctCurrent; }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || st.Turn % _interval != 0) return;
            var list = st.UnitsOf(TeamSide.Player);
            BattleUnit top = null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                if (top == null || u.Hp > top.Hp) top = u;
            }
            if (top == null) return;
            int dmg = CoreMath.RoundDamage(top.Hp * _pctCurrent);
            int dealt = top.TakeDamage(dmg);
            st.Log.Add(st.Turn, BattleEventKind.Damage, actorId: _boss.RuntimeId, targetId: top.RuntimeId,
                       amount: dmg, element: _boss.Element, note: $"{_boss.DisplayName} 吞舟（{_pctCurrent * 100f:F0}% 当前生命）");
            if (dealt > 0 && !top.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: top.RuntimeId, note: $"{top.DisplayName} 被吞舟咬碎");
        }
    }

    /// <summary>
    /// 深潜：首领血量 ≤ pct 后，**每回合额外多一次行动**。溟鲲。
    /// 实现：本钩子在回合始置 <see cref="BattleUnit.ExtraActionPending"/>，
    /// 由 <c>BattleSimulator.Steps</c> 的行动循环在它常规行动后**再执行一次**（同一出手逻辑，不另写一套）。
    /// </summary>
    public sealed class LastStandHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _pct;
        public LastStandHook(BattleUnit boss, float pct) { _boss = boss; _pct = pct; }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            if (_boss.HpPercent > _pct) return;
            _boss.ExtraActionPending = true;
            if (st.Turn % 1 == 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 深潜：濒死反扑，本回合额外行动一次");
        }
    }

    /// <summary>
    /// 虚实：每 interval 回合随机让 1 个存活单位**本回合免疫伤害**（可能落在真身）。霜影。
    /// ⚠ 这是**输出税**：每回合 1 个人免疫 ≈ 抹掉 20% 队伍输出 ⇒ 太密会让战斗变成"打不死"（实测霜影平局 100%）。
    /// </summary>
    public sealed class RandomImmunityHook : BattleHook
    {
        private readonly int _interval;
        public RandomImmunityHook(int interval = 1) { _interval = CoreMath.Max(1, interval); }

        public override void OnTurnStart(BattleState st)
        {
            if (st.Turn % _interval != 0) return;
            var all = new List<BattleUnit>();
            for (int s = 0; s < 2; s++)
            {
                var list = st.UnitsOf((TeamSide)s);
                for (int i = 0; i < list.Count; i++) if (list[i].IsAlive) all.Add(list[i]);
            }
            if (all.Count == 0) return;
            var pick = all[st.Random.NextInt(0, all.Count)];
            pick.Invulnerable = true;
            pick.StateHint = "虚实：本回合免疫伤害";
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"虚实：{pick.DisplayName} 本回合免疫伤害");
            st.CaptureFrame();   // 提示不进日志之外的帧 ⇒ 见 BossHooks 顶部 SetHint 的说明
        }
        public override void OnTurnEnd(BattleState st)
        {
            for (int s = 0; s < 2; s++)
            {
                var list = st.UnitsOf((TeamSide)s);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].StateHint != null && list[i].StateHint.StartsWith("虚实"))
                    { list[i].Invulnerable = false; list[i].StateHint = null; }
            }
            st.CaptureFrame();
        }
    }

    /// <summary>镜碎：真身受伤时，全体分身承受该伤害的 ratio。霜影。</summary>
    public sealed class CloneDamageShareHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _ratio;
        public CloneDamageShareHook(BattleUnit boss, float ratio) { _boss = boss; _ratio = ratio; }

        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || dst != _boss || dealt <= 0) return;
            int share = CoreMath.RoundDamage(dealt * _ratio);
            if (share <= 0) return;
            var list = st.UnitsOf(_boss.Side);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c == _boss || !c.IsAlive) continue;
                if (c.Def == null || c.Def.Id == null || !c.Def.Id.StartsWith("summon_")) continue;
                c.TakeDamage(share);
            }
        }
    }

    /// <summary>冰封：每 interval 回合冻结**灵力最高**的我方单位 1 回合，并偷走 steal 点灵力。玄溟。</summary>
    public sealed class FreezeStealMpHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        private readonly int _steal;
        public FreezeStealMpHook(BattleUnit boss, int interval, int steal)
        { _boss = boss; _interval = CoreMath.Max(1, interval); _steal = steal; }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive || st.Turn % _interval != 0) return;
            // ⚠ 灵力是"我方团队池"（st.TeamMp），不是每单位资源 ⇒ "灵力最高"用**元气最高的单位**近似。
            var list = st.UnitsOf(TeamSide.Player);
            BattleUnit pick = null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (!u.IsAlive) continue;
                if (pick == null || u.Rage > pick.Rage) pick = u;
            }
            if (pick == null) return;
            pick.ApplyStatus(StatusCatalog.Freeze, 1, 1);
            int stolen = CoreMath.Min(_steal, st.TeamMp);
            st.TeamMp -= stolen;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 冰封：{pick.DisplayName} 被冻结 1 回合，灵力 −{stolen}");
        }
    }
}
