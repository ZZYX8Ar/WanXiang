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
    public sealed class StaticHintHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly string _text;
        private readonly string _partKey;
        /// <param name="partKey">
        /// 非空 ， 走 <c>BossHint.SetPart</c>（**分段**，可与其它机制共存）；
        /// 空 ， 走 <c>BossHint.Set</c>（整条覆盖，会清掉已有分段）。</param>
        public StaticHintHook(BattleUnit boss, string text, string partKey = null)
        { _boss = boss; _text = text; _partKey = partKey; }

        public override void OnTurnStart(BattleState st)
        {
            if (!string.IsNullOrEmpty(_partKey)) BossHint.SetPart(st, _boss, _partKey, _text);
            else BossHint.Set(st, _boss, _text);
        }
    }

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
            _boss.AddShield((int)(_boss.MaxHp * _shieldFrac), st.Turn);
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

    public sealed class IgnoreCounterHook : BattleHook
    {
        private readonly BattleUnit _boss;
        public IgnoreCounterHook(BattleUnit boss) { _boss = boss; }
        public override void OnBattleStart(BattleState st)
        {
            if (_boss != null) _boss.IgnoreElementCounter = true;
        }
    }

    public sealed class RootHook : BattleHook
    {
        private readonly int _interval;
        public RootHook(int interval) { _interval = CoreMath.Max(1, interval); }
        public override void OnTurnStart(BattleState st)
        {
            {
                int left = (_interval - (st.Turn % _interval)) % _interval;
                BossHint.SetOnBoss(st, "根缚：" + (left == 0 ? "本回合缚 1 人" : left + " 回合后"));
            }
            if (st.Turn % _interval != 0) return;
            var list = st.UnitsOf(TeamSide.Player);
            var alive = new List<BattleUnit>();
            for (int i = 0; i < list.Count; i++) if (list[i].IsAlive) alive.Add(list[i]);
            if (alive.Count == 0) return;
            var t = alive[st.Random.NextInt(0, alive.Count)];
            t.ApplyStatus(StatusCatalog.Root, 1, 1);   // PreventsAction 会把 turns 自动 +1 ， 覆盖本回合
            st.Log.Add(st.Turn, BattleEventKind.StatusApplied, targetId: t.RuntimeId,
                       note: $"{t.DisplayName} 被根缚缠住（本回合无法行动）");
        }
    }

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
                           note: $"瘴气弥漫：全体我方 +{_stacks} 层（攻击 -{_stacks * 3}%/层，共 {n} 人）");
        }
    }

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

    public sealed class ScatterMiasmaHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private readonly int _interval;
        public ScatterMiasmaHook(BattleUnit boss, int interval) { _boss = boss; _interval = CoreMath.Max(1, interval); }

        public override void OnTurnStart(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            {
                int left = (_interval - (st.Turn % _interval)) % _interval;
                BossHint.Set(st, _boss, "散瘴：" + (left == 0 ? "本回合瘴气翻倍" : left + " 回合后瘴气翻倍"));
            }
            if (st.Turn % _interval != 0) return;
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
            st.CaptureFrame();   // 提示不进日志之外的帧 ， 见 BossHooks 顶部 SetHint 的说明
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
            // ⚠ 灵力是"我方团队池"（st.TeamMp），不是每单位资源 ， "灵力最高"用**元气最高的单位**近似。
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
                       note: $"{_boss.DisplayName} 冰封：{pick.DisplayName} 被冻结 1 回合，灵力 -{stolen}");
        }
    }

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

    public sealed class BossOpeningMpHook : BattleHook
    {
        private readonly int _mp;
        public BossOpeningMpHook(int mp) { _mp = mp; }
        public override void OnBattleStart(BattleState st)
        {
            if (st.EnemyMp < _mp) st.EnemyMp = _mp;
        }
    }

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
    //    · 瘴气 = StatusCatalog.Miasma（每层攻击 -3%）
    //    · 灼烧 / 冰蚀 = 现成 DoT；冻结 / 沉默 = 现成
    // ============================================================================

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

}
