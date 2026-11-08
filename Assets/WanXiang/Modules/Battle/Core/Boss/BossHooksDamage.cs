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
    public sealed class DamageReflectHook : BattleHook
    {
        private readonly float _ratio;
        public DamageReflectHook(float ratio) { _ratio = ratio; }
        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (src == null || src == dst || !src.IsAlive || dealt <= 0) return;
            // ⛔ 只反弹"**敌人挨打**"这一侧。原实现不看 dst 的阵营 ， 首领用 AOE 打我方时，
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

    public sealed class DpsTimeoutHook : BattleHook
    {
        private readonly int _turnLimit;
        public DpsTimeoutHook(int turnLimit) { _turnLimit = turnLimit; }
        public override void OnTurnEnd(BattleState st)
        {
            if (st.Turn < _turnLimit)
            {
                // ★ 倒计时：硬性 DPS 是"莫名其妙团灭"的重灾区，玩家必须看得见还剩几回合
                BossHint.SetOnBoss(st, "天罚：还剩 " + (_turnLimit - st.Turn) + " 回合");
                return;
            }
            BossHint.SetOnBoss(st, null);
            if (st.AliveCountOf(TeamSide.Enemy) == 0) return;   // 已经打过了
            if (st.AliveCountOf(TeamSide.Player) == 0) return;  // 已经没人才不重复判
            var list = st.UnitsOf(TeamSide.Player);
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsAlive) list[i].TakeTrueDamage(list[i].Hp + 1);
            st.Outcome = BattleOutcome.EnemyWin;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"未在第 {_turnLimit} 回合前击杀首领 ， 灭团");
        }
    }

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
            if (_boss == null || !_boss.IsAlive) return;
            {
                int left = (_interval - (st.Turn % _interval)) % _interval;
                BossHint.Set(st, _boss, "剜心：" + (left == 0 ? "本回合斩最低血" : left + " 回合后"));
            }
            if (st.Turn % _interval != 0) return;
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

    public sealed class HighestHpNukeHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        private readonly float _pctCurrent;
        public HighestHpNukeHook(BattleUnit boss, int interval, float pctCurrent)
        { _boss = boss; _interval = CoreMath.Max(1, interval); _pctCurrent = pctCurrent; }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            {
                int left = (_interval - (st.Turn % _interval)) % _interval;
                BossHint.Set(st, _boss, "吞舟：" + (left == 0 ? "本回合发动" : left + " 回合后"));
            }
            if (st.Turn % _interval != 0) return;
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
            _streak.Clear();                 // 只留最后一个目标 ， 换人就断连击
            if (n < _maxStreak) _streak[id] = n + 1;
            else _streak[id] = _maxStreak;
            if (n > 0) dmg = CoreMath.RoundDamage(dmg * (1f + _perHit * n));
        }
    }

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
            if (_boss != null) BossHint.Set(st, _boss, "引燃：最高灼烧 " + worst + "/" + _threshold);
            if (worst < _threshold) return;

            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: $"{_boss.DisplayName} 引燃！最高灼烧 {worst} 层 ， 引爆");
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
            if (_boss == null || !_boss.IsAlive) return;
            {
                int left = (_interval - (st.Turn % _interval)) % _interval;
                BossHint.Set(st, _boss, _label + "：" + (left == 0 ? "本回合发动" : left + " 回合后"));
            }
            if (st.Turn % _interval != 0) return;
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

    public sealed class ShieldBreakReflectHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly float _ratio;
        private readonly float _maxFracOfBossHp;
        private string _brokenBy;
        private bool _hadShield;
        public ShieldBreakReflectHook(BattleUnit boss, float ratio, float maxFracOfBossHp = 0.10f)
        { _boss = boss; _ratio = ratio; _maxFracOfBossHp = maxFracOfBossHp; }

        public override void OnTurnStart(BattleState st) { _hadShield = _boss != null && _boss.Shield > 0; }

        public override void OnDamageDealt(BattleState st, BattleUnit src, BattleUnit dst, int dmg, int dealt, bool trueDmg)
        {
            if (_boss == null || dst != _boss || src == null) return;
            if (!_hadShield || _boss.Shield > 0) return;
            _hadShield = false;
            _brokenBy = src.RuntimeId;
            int back = CoreMath.RoundDamage(src.Attack * _ratio);
            int cap = CoreMath.RoundDamage(_boss.MaxHp * _maxFracOfBossHp);
            if (cap > 0 && back > cap) back = cap;
            int got = src.TakeDamage(back);
            if (got > 0)
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"{_boss.DisplayName} 齿轮反击：{src.DisplayName} 破盾受 {got} 反伤");
            if (!src.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: src.RuntimeId, note: $"{src.DisplayName} 被齿轮绞碎");
        }
    }

}
