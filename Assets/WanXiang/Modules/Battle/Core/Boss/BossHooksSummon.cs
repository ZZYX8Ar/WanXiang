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
                // ⛔ 这里**不能**再判占位 —— 那样 DeploySummon 内的"找空格"永远走不到，
                //    两只召唤物活着时后续回合照样全跳过（用户 2026-10-30 再次反馈）。
                DeploySummon(st, proto, cell);
            }
        }
        /// <summary>把一只召唤物放上敌方棋盘（克隆 + 占位面板 + 上阵）。</summary>
        public static void DeploySummon(BattleState st, BeastDef proto, int cell)
        {
            if (proto == null) return;
            // ★ 2026-10-26 用户："每 2 回合召唤，怎么只召唤了一次" ——
            //   原来目标格被占就**直接放弃**，两只召唤物都活着时后续回合全跳过。
            //   改成：目标格被占就往后找空格（最多 3 只同时在场的上限仍由调用方的格表决定）。
            if (cell < 0 || cell >= BoardLayout.CellCount) return;
            // 上限 4 只（别把 9 格全塞满，玩家会看不清谁是谁）
            int alive = 0;
            var enemies = st.UnitsOf(TeamSide.Enemy);
            for (int i = 0; i < enemies.Count; i++)
            {
                var eu = enemies[i];
                if (eu != null && eu.IsAlive && eu.Def != null && !string.IsNullOrEmpty(eu.Def.Id) &&
                    eu.Def.Id.StartsWith("summon_")) alive++;
            }
            if (alive >= 4) return;
            // ⛔ 尸体不腾格 ⇒ 打几波后 9 格全被"死掉的召唤物"占着，再也召不出来（用户 2026-10-30 报障）。
            st.ClearSlotIfDead(TeamSide.Enemy, cell);
            if (st.SlotAt(TeamSide.Enemy, cell) != null)
            {
                int free = -1;
                for (int c = 0; c < BoardLayout.CellCount; c++)
                {
                    int idx = (cell + 1 + c) % BoardLayout.CellCount;
                    st.ClearSlotIfDead(TeamSide.Enemy, idx);
                    if (st.SlotAt(TeamSide.Enemy, idx) == null) { free = idx; break; }
                }
                if (free < 0) return;          // 真满了（9 格全占）
                cell = free;
            }
            var def = proto.Clone();
            st.Config.ApplyPlaceholderStats(def);          // summon_ 前缀会被跳过，保留弱面板
            var e = DeployEntry.Enemy(def, cell);
            int before = st.UnitsOf(TeamSide.Enemy).Count;
            BattleFactoryDeploy(st, e);
            if (st.UnitsOf(TeamSide.Enemy).Count > before)
            {
                var spawned = st.SlotAt(TeamSide.Enemy, cell);
                st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                           note: $"召唤 {def.DisplayName}（格 {cell}）");
                // ★ 2026-10-26：单独发一条 Summon（表现层据此播"落地"爆发）
                st.Log.Add(st.Turn, BattleEventKind.Summon,
                           targetId: spawned != null ? spawned.RuntimeId : null,
                           note: def.DisplayName);
            }
        }
        private static void BattleFactoryDeploy(BattleState st, DeployEntry e)
        {
            // ⛔ 必须用 DeploySpawn（唯一 id）：用 "E" 前缀会让召唤物拿到 "E0" 与开场第一个敌人撞号，
            //   表现层按 RuntimeId 建 view ， 召唤物不可见、伤害数字/血条错位到 Boss 身上。
            BattleFactory.DeploySpawn(st, new[] { e });
        }
    }

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

}
