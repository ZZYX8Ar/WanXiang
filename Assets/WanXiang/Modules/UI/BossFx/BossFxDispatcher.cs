// 万相 · Boss 特效派发器（表现层唯一入口）
// BattleLog.Note 里带 [DICE]/[BOSSFX:xxx] 标记 → 注册表派发。Core 不认识这个类。
using Cysharp.Threading.Tasks;
using WanXiang.Battle.Core.Dice;
using WanXiang.Framework.Boot;

namespace WanXiang.Modules.UI.BossFx
{
    public static class BossFxDispatcher
    {
        private static bool _installed;

        /// <summary>注册所有演出器。加特效 = 这里加一行 Register。</summary>
        public static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            BossFxRegistry.Register("Dice", new CompositeFx(new DiceNoteFx(), new BossShaderFx(BurstStyles.Dice)));
            BossFxRegistry.Register("BossBurst", new BossShaderFx(BurstStyles.Damage));
            BossFxRegistry.Register("BossControl", new BossShaderFx(BurstStyles.Control));
            BossFxRegistry.Register("BossHeal", new BossShaderFx(BurstStyles.Heal));
        }

        /// <summary>战斗事件回调：只读 Note，不写战斗状态。</summary>
        public static void OnBattleEvent(WanXiang.Battle.Core.BattleEvent e)
        {
            if (string.IsNullOrEmpty(e.Note)) return;
            EnsureInstalled();
            string kind, payload;
            if (!BossFxRegistry.TryParse(e.Note, out kind, out payload)) return;
            BossFxRegistry.Dispatch(new FxContext(kind, payload, e.ActorId ?? ""));
        }

        /// <summary>组合多个演出器（新增效果不改已有分支）。</summary>
        private sealed class CompositeFx : IBossFx
        {
            private readonly IBossFx[] _items;
            public CompositeFx(params IBossFx[] items) { _items = items; }
            public void Play(FxContext ctx)
            {
                if (_items == null) return;
                for (int i = 0; i < _items.Length; i++) _items[i].Play(ctx);
            }
        }

        /// <summary>骰子：解析 [DICE] 载荷 → 打开骰子面板播动画。</summary>
        private sealed class DiceNoteFx : IBossFx
        {
            public void Play(FxContext ctx)
            {
                string prefix, tableId, text; int face; int[] dice;
                if (!DiceRule.Decode("[DICE]" + ctx.Note, out prefix, out tableId, out face, out dice, out text))
                    return;
                var ui = UIBootstrap.UI;
                if (ui == null) return;
                ui.OpenAsync<DiceRollFx>(new DiceRollPayload
                {
                    Prefix = prefix, Face = face, Dice = dice, Text = text
                }).Forget();
            }
        }

    }
}
