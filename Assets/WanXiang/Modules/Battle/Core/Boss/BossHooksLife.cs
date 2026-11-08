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
    public sealed class FirstHitReduceHook : BattleHook
    {
        private readonly float _reduce;
        private readonly HashSet<string> _hitThisTurn = new HashSet<string>();
        public FirstHitReduceHook(float reduce) { _reduce = reduce; }
        public override void OnTurnStart(BattleState st)
        {
            _hitThisTurn.Clear();
            // ★ 首击减伤**不再**写战斗头顶提示（用户 2026-10-12 定案：属于静态机制，
            //   在编队面板的"首领机制"里一次讲清楚即可，战斗里常驻提示反而干扰；
            //   战斗头顶只留"倒计时类"提示，如双子 N 回合后复活）。见 BossCatalog.MechanicText。
        }
        public override void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst, ref int dmg, Element el)
        {
            if (dmg <= 0) return;
            if (_hitThisTurn.Add(dst.RuntimeId))
                dmg = CoreMath.RoundDamage(dmg * (1f - _reduce));
        }
    }

    public sealed class PhaseCountdownHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float[] _thresholds;   // 必须**降序**（0.75, 0.50, 0.25）
        private readonly string _partKey;
        private readonly string _label;
        public PhaseCountdownHook(BattleUnit boss, float[] thresholds, string partKey, string label)
        {
            _boss = boss; _thresholds = thresholds ?? new float[0];
            _partKey = partKey; _label = label;
        }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            float next = -1f;
            for (int i = 0; i < _thresholds.Length; i++)
                if (_thresholds[i] > _boss.HpPercent + 0.0001f) { next = _thresholds[i]; break; }
            if (next < 0f) { BossHint.SetPart(st, _boss, _partKey, null); return; }
            int need = CoreMath.RoundDamage((next - _boss.HpPercent) * _boss.MaxHp);
            BossHint.SetPart(st, _boss, _partKey, _label + "：再掉 " + need + " 血分裂");
        }
    }

    public sealed class PhaseHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _threshold;
        private readonly Action<BattleState, BattleUnit> _onCross;
        private readonly bool _once;
        private readonly string _label;
        private bool _fired;
        public PhaseHook(BattleUnit boss, float threshold, Action<BattleState, BattleUnit> onCross,
                         bool once = true, string label = null)
        {
            _boss = boss; _threshold = threshold; _onCross = onCross; _once = once; _label = label;
        }
        public override void OnTurnStart(BattleState st)
        {
            if (_fired && _once) return;
            if (_boss == null || !_boss.IsAlive) return;
            if (_boss.HpPercent > _threshold) return;
            _fired = true;
            // ★ 2026-11-02：阶段切换必须让玩家看见（原来只有数值变化，玩家不知道"它变招了"）
            BossHint.SetOnBoss(st, string.IsNullOrEmpty(_label)
                ? "首领进入下一阶段（" + (int)System.Math.Round(_threshold * 100f) + "%）"
                : _label);
            _onCross(st, _boss);
        }
    }

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

        /// <summary>复活倒计时提示的分段 key（走 BossHint 分段，和"首击减伤"等共存）。</summary>
        private const string HintKey = "kl";

        /// <summary>窗口期内仍活着的那一只 —— 倒计时提示写在它头顶（死掉那只头顶是灰的，看不见）。</summary>
        private BattleUnit Survivor => _pendingDead == null ? null : (_pendingDead == _a ? _b : _a);

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
                RefreshHint(st);   // ★ 立刻在存活那只头顶显示"N 回合后复活"（用户要能看见）
            }
            else
            {
                // 窗口内另一只也倒下 ， 双杀成立，两只都真死
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"双子同命：双杀成立 —— {victim.DisplayName} 与 {_pendingDead.DisplayName} 同命陨落");
                var s = Survivor;
                if (s != null) BossHint.SetPart(st, s, HintKey, null);   // 双杀 ， 不会复活，撤掉倒计时
                _pendingDead = null;
                _deadTurn = -1;
            }
        }

        /// <summary>回合始刷新倒计时（窗口期内每回合递减），玩家能一眼看到"还剩几回合复活"。</summary>
        public override void OnTurnStart(BattleState st) { if (_pendingDead != null) RefreshHint(st); }

        private void RefreshHint(BattleState st)
        {
            var s = Survivor;
            if (s == null || !s.IsAlive) return;
            int left = _windowTurns - (st.Turn - _deadTurn);
            BossHint.SetPart(st, s, HintKey,
                left > 0 ? $"{_pendingDead.DisplayName} 倒下：{left} 回合后复活"
                         : $"{_pendingDead.DisplayName} 本回合复活");
        }

        public override void OnTurnEnd(BattleState st)
        {
            if (_pendingDead == null) return;
            if (st.Turn - _deadTurn < _windowTurns) return;   // 窗口尚未结束，继续等玩家双杀

            var other = Survivor;
            if (other != null)
            {
                if (other.IsAlive && !_pendingDead.IsAlive)
                {
                    _pendingDead.ReviveAtHp(CoreMath.Max(1, (int)(_pendingDead.MaxHp * _revivePct)));
                    // ★ 显式广播 Revive 事件（与 Action.cs 的灵卵复活同口径）：
                    //   之前只写 RoundResolve note ， 舞台没有"复活"事件可处理、DeadBlend 不复位，
                    //   复活的双子可能仍以"灰尸"状态出现，玩家以为没复活（用户 2026-10-12 报障）。
                    st.Log.Add(st.Turn, BattleEventKind.Revive,
                               actorId: _pendingDead.RuntimeId, targetId: _pendingDead.RuntimeId,
                               note: $"双子同命：{_pendingDead.DisplayName} 以 {(_revivePct * 100f):F0}% 血复活");
                }
                BossHint.SetPart(st, other, HintKey, null);   // 窗口结束 ， 撤掉倒计时分段
            }
            _pendingDead = null;
            _deadTurn = -1;
        }
    }

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
            _boss.StateHint = null;   // 冰核期结束 ， 清掉 HUD 提示
            if (_accum >= _boss.MaxHp * _dmgFrac)
            {
                _used = true;
                _boss.SetHp(0);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 冰核被击碎 ， 真死");
            }
            else
            {
                _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * _revivePct)));
                st.Log.Add(st.Turn, BattleEventKind.Revive, actorId: _boss.RuntimeId, targetId: _boss.RuntimeId,
                           note: $"{_boss.DisplayName} 未被破核 ， 以 {(_revivePct * 100f):F0}% 血复活");
            }
        }

        /// <summary>破核所需累计伤害（= MaxHp × dmgFrac），提示与判定共用同一口径。</summary>
        private int NeedDmg() => CoreMath.Max(1, (int)(_boss.MaxHp * _dmgFrac));
    }

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
            if (!anySummon) return;   // 火种全灭 ， 真死
            _used = true;
            _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * _revivePct)));
            st.Log.Add(st.Turn, BattleEventKind.Revive, actorId: _boss.RuntimeId, targetId: _boss.RuntimeId,
                       note: $"{_boss.DisplayName} 火种同源：以 {(_revivePct * 100f):F0}% 血复活");
        }
    }

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
            // ★ 实时提示：组装是**每回合递增**的，玩家要能看出"它现在多痛了"
            BossHint.Set(st, _boss,
                "组装：攻击 +" + (_boss.PermanentAttackBonus * 100f).ToString("F0")
                + "%（破盾可止）");
        }
    }

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
        /// ⛔ 必须**顺手抓一帧**：提示不进日志，而帧只在 `Log.Add` 时抓 ，
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

            // ① 冰晶全灭 ， 真死（Boss 保持阵亡）
            if (AllCrystalsDead())
            {
                _active = false;
                SetHint(st, null);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"冰晶尽碎 ， {_boss.DisplayName} 真正陨落");
                return;
            }

            // ② 窗口未满 ， 继续等玩家清冰晶
            int elapsed = st.Turn - _deadTurn;
            if (elapsed < _windowTurns)
            {
                SetHint(st, $"冰晶期 · 还有 {_windowTurns - elapsed} 回合复活");
                return;
            }

            // ③ 超时 ， 按序列复活（<=0 则直接陨落）
            float pct = (_revivePcts != null && _revivePcts.Length > 0)
                ? _revivePcts[System.Math.Min(_cycle, _revivePcts.Length - 1)] : 0f;
            _cycle++;
            _active = false;
            SetHint(st, null);

            if (pct <= 0f)
            {
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"冰晶第 {_cycle} 次未被破 ， {_boss.DisplayName} 力竭陨落（不再复活）");
                return;   // Boss 保持阵亡
            }

            _boss.ReviveAtHp(CoreMath.Max(1, (int)(_boss.MaxHp * pct)));
            EnsureCrystals(st, fullHeal: true);   // 冰晶不消失 + 全部满血
            st.Log.Add(st.Turn, BattleEventKind.Revive, actorId: _boss.RuntimeId, targetId: _boss.RuntimeId,
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
    /// B3-i：首领阶段二 —— 本体生命首次跌破阈值时攻击永久提升 + 头顶提示「阶段二」（一次性）。
    /// 由 BattlePlayback 在难度里程碑 boss_phase2 命中时挂到敌方首领身上。
    /// </summary>
    public sealed class BossPhase2Hook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _pct;       // 触发血线（0.5 = 50%）
        private readonly float _atkBonus;  // 攻击提升（0.15 = +15%）
        private bool _fired;

        public BossPhase2Hook(BattleUnit boss, float pct, float atkBonus)
        {
            _boss = boss; _pct = pct; _atkBonus = atkBonus;
        }

        public override void OnTurnStart(BattleState st)
        {
            if (_fired || _boss == null || !_boss.IsAlive) return;
            if ((float)_boss.Hp / System.Math.Max(1, _boss.MaxHp) > _pct) return;
            _fired = true;
            _boss.PermanentAttackBonus += _atkBonus;
            BossHint.Set(st, _boss, "阶段二");   // 头顶提示（含 CaptureFrame），Core 层不打 Unity 日志
        }
    }
}
