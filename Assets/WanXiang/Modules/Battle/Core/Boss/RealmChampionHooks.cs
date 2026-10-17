// ============================================================================
//  镇守专属机制（RealmChampionHooks）—— 2026-10-17
//  ---------------------------------------------------------------------------
//  用户要求：秘境里的特殊敌人要有**常规战斗体现不了的战斗方式**，自会出现在秘境里。
//
//  这里放五个场域各自的镇守机制。它们只在「镇守」（RealmChampion 节点）战斗里挂，
//  由 `Campaign.Core.RealmChampionMechanics.Attach` 调用（见 BattlePlayback）。
//
//  ⛔ 分层说明：本文件在 **Battle.Core**，不认识 Campaign —— 所以 realmId 用**字面量**
//    并在注释里对齐 `WanXiang.Campaign.RealmIds`。改成引用 Campaign 会造成反向依赖。
//
//  ⛔ 全部走既有原语（ApplyStatus / TakeDamage / AddShield / EffectiveSpeed），
//    不碰随机流（st.Random 允许，那是战斗内的确定随机）。
// ============================================================================

using System.Collections.Generic;

namespace WanXiang.Battle.Core
{
    /// <summary>镇守机制挂载表：realmId → 一组钩子。**只在镇守战调用**。</summary>
    public static class RealmChampionMechanics
    {
        // ⚠ 与 WanXiang.Campaign.RealmIds 一一对应（分层原因见文件头）。
        private const string Marsh  = "realm_marsh";   // 苍泽
        private const string Ember  = "realm_ember";   // 烬岭
        private const string Gear   = "realm_gear";    // 机枢
        private const string Silent = "realm_silent";  // 寂域
        private const string Thorn  = "realm_thorn";   // 荆墟

        public static void Attach(BattleState st, string realmId)
        {
            if (st == null || string.IsNullOrEmpty(realmId)) return;
            var boss = StrongestEnemy(st);
            if (boss == null) return;

            switch (realmId)
            {
                case Marsh:
                    st.Hooks.Add(new ChampionDrownHook());          // 沉沦：专拖你最快的
                    break;
                case Ember:
                    st.Hooks.Add(new ChampionBloodFuryHook(boss));  // 薪火：烧自己换攻击
                    break;
                case Gear:
                    st.Hooks.Add(new ChampionOverloadHook(boss));   // 过载校准：周期性全体标记+护盾
                    break;
                case Silent:
                    st.Hooks.Add(new ChampionMuteHook());           // 噤声：队伍灵力恒为 0
                    break;
                case Thorn:
                    st.Hooks.Add(new ChampionGambleHook());         // 开赌：每回合押一边
                    break;
            }
        }

        /// <summary>镇守 = 敌方里血最厚的那只（BossSquadFor 的首领排在首位，这里再兜一层）。</summary>
        private static BattleUnit StrongestEnemy(BattleState st)
        {
            var list = st.UnitsOf(TeamSide.Enemy);
            BattleUnit best = null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                if (best == null || u.MaxHp > best.MaxHp) best = u;
            }
            return best;
        }
    }

    // ========================================================================
    //  苍泽 · 泽殍 ——「沉沦」
    //  与常规 RootHook（随机缚 1 人）的区别：**永远拖走你速度最高的那个**。
    //  ⇒ 你的核心输出/最灵活的单位每两回合就废一回合，逼你重排出手顺序。
    // ========================================================================
    public sealed class ChampionDrownHook : BattleHook
    {
        public override void OnTurnEnd(BattleState st)
        {
            if (st.Turn % 2 != 0) return;                    // 每 2 回合一次
            var list = st.UnitsOf(TeamSide.Player);
            BattleUnit fastest = null;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                if (fastest == null || st.EffectiveSpeed(u) > st.EffectiveSpeed(fastest)) fastest = u;
            }
            if (fastest == null) return;
            fastest.ApplyStatus(StatusCatalog.Root, 1, 1);
            fastest.ApplyStatus(StatusCatalog.Frost, 2, 2);  // 顺带减速，"从水里出来很沉"
            st.Log.Add(st.Turn, BattleEventKind.StatusApplied, targetId: fastest.RuntimeId,
                       note: "泽殍·沉沦：" + fastest.DisplayName +
                             "（全场最快）被拖入深水，下回合无法行动且减速");
        }
    }

    // ========================================================================
    //  烬岭 · 赤髓 ——「薪火」
    //  每回合**烧掉自己 5% 最大生命**，换永久攻击 +8%（可无限叠）。
    //  ⇒ 拖得越久它越痛，但它也在自毁 —— 逼你算"它还能烧几回合"，纯数值对轰没有出路。
    // ========================================================================
    public sealed class ChampionBloodFuryHook : BattleHook
    {
        private readonly BattleUnit _boss;
        private int _stacks;
        public ChampionBloodFuryHook(BattleUnit boss) { _boss = boss; }

        public override void OnTurnEnd(BattleState st)
        {
            if (_boss == null || !_boss.IsAlive) return;
            int cost = CoreMath.RoundDamage(_boss.MaxHp * 0.05f);
            int dealt = _boss.TakeDamage(cost);
            _stacks++;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _boss.RuntimeId,
                       note: "赤髓·薪火：自焚 " + cost + " 生命，攻击永久 +" +
                             (_stacks * 8) + "%（第 " + _stacks + " 层）");
            if (dealt > 0 && !_boss.IsAlive)
                st.Log.Add(st.Turn, BattleEventKind.Death, targetId: _boss.RuntimeId,
                           note: "赤髓 烧尽了自己");
        }

        public override void ModifyIncoming(BattleState st, BattleUnit src, BattleUnit dst,
                                            ref int dmg, Element el)
        {
            if (src != _boss || _stacks <= 0 || dmg <= 0) return;
            dmg = CoreMath.RoundDamage(dmg * (1f + 0.08f * _stacks));
        }
    }

    // ========================================================================
    //  机枢 · 校核者·零 ——「过载校准」
    //  每 3 回合：**全体我方被标记**（易伤）+ **全体敌方获得 15% 生命护盾**。
    //  ⇒ "校准"意味着你的输出窗口是有周期的：标记回合别硬打，护盾回合攒资源。
    // ========================================================================
    public sealed class ChampionOverloadHook : BattleHook
    {
        private readonly BattleUnit _boss;
        public ChampionOverloadHook(BattleUnit boss) { _boss = boss; }

        public override void OnTurnEnd(BattleState st)
        {
            if (st.Turn % 3 != 0) return;

            var players = st.UnitsOf(TeamSide.Player);
            int marked = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var u = players[i];
                if (u == null || !u.IsAlive) continue;
                u.ApplyStatus(StatusCatalog.Marked, 2, 2);
                marked++;
            }

            var enemies = st.UnitsOf(TeamSide.Enemy);
            int shielded = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var u = enemies[i];
                if (u == null || !u.IsAlive) continue;
                int amt = CoreMath.RoundDamage(u.MaxHp * 0.15f);
                if (amt <= 0) continue;
                u.AddShield(amt, 2);
                shielded++;
            }

            st.Log.Add(st.Turn, BattleEventKind.RoundResolve, actorId: _boss != null ? _boss.RuntimeId : null,
                       note: "校核者·零：过载校准 —— 我方 " + marked + " 人被标记（受击 +25%），" +
                             "敌方 " + shielded + " 人获得 15% 护盾");
        }
    }

    // ========================================================================
    //  寂域 · 无声者 ——「噤声」
    //  **我方队伍灵力每回合归零** ⇒ 战记 / 连携整个战斗放不出来。
    //  ⇒ 这才是"无声之地"：不是数值加压，是**直接抽掉一个系统**。
    //  （寂域同时禁 QTE —— 玩家只剩普攻、站位与状态反应。）
    // ========================================================================
    public sealed class ChampionMuteHook : BattleHook
    {
        public override void OnTurnStart(BattleState st)
        {
            if (st.TeamMp <= 0) return;
            st.TeamMp = 0;
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: "无声者·噤声：队伍灵力被抹平（战记/连携无法积蓄）");
        }
    }

    // ========================================================================
    //  荆墟 · 枯骨庄家 ——「开赌」
    //  每回合开局掷骰：50% 全体我方被标记（易伤），50% 全体敌方被标记。
    //  ⇒ 每回合的开局都不一样，配合荆墟"结算 2.2 倍"的赌坊定位。
    // ========================================================================
    public sealed class ChampionGambleHook : BattleHook
    {
        public override void OnTurnStart(BattleState st)
        {
            bool playerWins = (st.Random.NextInt(0, 2) == 0);
            var side = playerWins ? TeamSide.Enemy : TeamSide.Player;
            var list = st.UnitsOf(side);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null || !u.IsAlive) continue;
                u.ApplyStatus(StatusCatalog.Marked, 1, 1);
                n++;
            }
            st.Log.Add(st.Turn, BattleEventKind.RoundResolve,
                       note: playerWins
                           ? "枯骨庄家·开赌：这一局押你赢 —— 敌方 " + n + " 人被标记（受击 +25%）"
                           : "枯骨庄家·开赌：这一局押你输 —— 我方 " + n + " 人被标记（受击 +25%）");
        }
    }
}
