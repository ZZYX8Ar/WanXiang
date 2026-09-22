// ============================================================================
//  Panel_Trial —— 天阙抉择（通关后，全作最重要的一次选择）
//  三卡等宽等高，禁止默认高亮引导；AllowBackClose = false。
//  ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Fusion;
using WanXiang.Framework.UI;

using Cysharp.Threading.Tasks;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Trial", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Transient,
             CloseOnMaskClick = false)]
    public sealed class TrialPanel : UIPanelBase
    {
        [BindArray("Choice_{0}", 3)]
        [SerializeField] private Button[] _choiceBtns;         // 登天阙 / 续劫 / 归元
        [BindArray("Tmp_ChoiceTitle_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpChoiceTitles;  // 卡题
        [BindArray("Tmp_ChoiceDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpChoiceDescs;   // 后果说明
        [SerializeField] private TMP_Text _tmpStatus;          // Tmp_Status 当前境劫与灵卵数
        [SerializeField] private WanXiang.Fusion.ContentCatalogSO _contentCatalog;  // 由生成器注入

        public override bool AllowBackClose => false;

        /// <summary>
        /// ★★ 填充三个选项的文案 —— 这些字段以前**从来没被赋值过**，
        ///    所以天阙抉择弹出来时显示的是 prefab 里的占位符（Choice 0 / desc），
        ///    玩家完全看不出三个选项是什么意思（用户实测）。
        /// </summary>
        protected override Cysharp.Threading.Tasks.UniTask OnOpenAsync(object payload)
        {
            string[] titles = { "登天阙", "续劫", "归元" };
            string[] descs =
            {
                "终局战：后土 + 我方队伍镜像（×1.5）。\n赢了就是真通关 —— 解锁无尽模式与更高难度。",
                "季节回春：队伍与资源全部继承，\n敌人强度 +3%、道劫律 +1（无尽模式）。",
                "归元：主动结束本局，\n按当前进度正常结算后回主城。",
            };

            for (int i = 0; i < titles.Length; i++)
            {
                if (_tmpChoiceTitles != null && i < _tmpChoiceTitles.Length && _tmpChoiceTitles[i] != null)
                    _tmpChoiceTitles[i].text = titles[i];
                if (_tmpChoiceDescs != null && i < _tmpChoiceDescs.Length && _tmpChoiceDescs[i] != null)
                    _tmpChoiceDescs[i].text = descs[i];
            }

            var run = WanXiang.Run.RunSave.Current;
            if (_tmpStatus != null && run != null)
                _tmpStatus.text = run.RealmText + " · 灵卵 " + run.Eggs;

            return Cysharp.Threading.Tasks.UniTask.CompletedTask;
        }

        /// <summary>
        /// 关闭本面板后，等两帧再打开节点图。
        /// 为什么需要延迟：UISystem 是栈式重算，Overlay 层的本面板关闭会触发一次重算，
        /// 同一帧打开的节点图会被"判为不在栈里"而立刻关闭（实测 8 个面板全关、画面全空）。
        /// </summary>
        private async void OpenCampaignNextFrame()
        {
            await UniTask.DelayFrame(2);
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null)
            {
                ui.Close<HomePanel>();                 // 主界面若还开着，先关掉（避免压住节点图）
                await ui.OpenAsync<CampaignPanel>();
            }
        }

        /// <summary>
        /// 构造【终局战】：后土（图鉴最强顶位，×1.5）+ 我方队伍镜像（×1.2）+ 玩家自己的队伍。
        /// 由 <c>CampaignPanel</c> 在"走到天阙最后一格"时调用（登天阙本身只负责进入天阙图）。
        /// ⚠ 玩家队伍必须填！手写 BattleRequest 漏填 Player 会让驱动判定"拿不到可用的战斗入参"。
        /// </summary>
        public static BattleRequest BuildFinaleBattle(WanXiang.Run.RunState run, BeastDef[] all)
        {
            BeastDef boss = all[0];
            foreach (var b in all)
                if ((int)b.Rarity > (int)boss.Rarity) boss = b;

            var byId = new System.Collections.Generic.Dictionary<string, BeastDef>();
            foreach (var b in all) byId[b.Id] = b;

            var req = new BattleRequest
            {
                Title = "天阙 · " + boss.DisplayName,
                WeatherName = "终局：后土 + 我方镜像 ×3，倍率 ×1.5",
                Seed = (ulong)run.RunSeed + 9999,
            };

            var entries = new System.Collections.Generic.List<DeployEntry>();
            entries.Add(DeployEntry.Enemy(boss, BattleRequest.Cells[4]).WithMul(1.5f));
            int mirrors = System.Math.Min(3, run.Team != null ? run.Team.Count : 0);
            for (int i = 0; i < mirrors; i++)
                if (byId.TryGetValue(run.Team[i], out var mirror))
                    entries.Add(DeployEntry.Enemy(mirror, BattleRequest.Cells[i]).WithMul(1.2f));
            req.EnemyEntries.AddRange(entries);

            req.Player.Clear();
            if (req.PlayerCells == null) req.PlayerCells = new System.Collections.Generic.List<int>();
            req.PlayerCells.Clear();
            int[] myCells = { 2, 5, 8, 1, 4, 7, 0, 3, 6 };
            int k = 0;
            if (run.Team != null)
            {
                foreach (var tid in run.Team)
                {
                    if (!byId.TryGetValue(tid, out var pb)) continue;
                    req.Player.Add(pb);
                    req.PlayerCells.Add(myCells[k % myCells.Length]);
                    k++;
                }
            }
            UnityEngine.Debug.Log("[TrialPanel] 终局战：我方 " + req.Player.Count + " 只，敌方 " + entries.Count + " 只");
            return req;
        }

        protected override void OnCreate()
        {
            for (int i = 0; i < _choiceBtns.Length; i++)
            {
                var idx = i;
                if (_choiceBtns[i] != null) _choiceBtns[i].onClick.AddListener(() => OnChoiceClicked(idx));
            }
        }

        /// <summary>
        /// 天阙抉择（GDD 第 7 章，每轮结束三选一）：
        ///   0 登天阙 → 终局战（后土位：图鉴最强 + 我方队伍镜像 ×1.5），赢了就是真通关；
        ///   1 续劫   → 季节回春，队伍/资源/路线种子继承，敌强 +3%、+1 道劫律；
        ///   2 归元   → 主动结束本局，按当前进度正常结算回主城。
        /// </summary>
        private void OnChoiceClicked(int index)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            switch (index)
            {
                case 0:  // 登天阙：**进入天阙图**（休整→商店→熔炼→看护关→后土），不是直接开战
                    {
                        // ★★ 用户设计：登天阙后要走一段"登天"流程（天阙图），
                        //    走到最后一格（后土）才打终局战。原来这里直接 EnterBattle，
                        //    等于跳过整段流程（用户实测："选登天阙没有进入第五幕，而是直接开战"）。
                        SceneFlow.IsFinaleBattle = false;   // 终局战推迟到天阙最后一格
                        var run0 = WanXiang.Run.RunSave.Current;
                        if (run0 != null)
                        {
                            run0.Act = 5;
                            run0.NodeOffset = -1;
                            if (run0.VisitedNodes != null) run0.VisitedNodes.Clear();
                            WanXiang.Run.RunSave.SaveCurrent();
                        }
                        // ★★ 进入天阙图（第 5 幕节点地图）。
                        //    难点：本面板在 **Overlay 层**，CloseSelf() 会触发 UISystem 的"栈重算"，
                        //    若同一帧就打开节点图，它会被这次重算一起判掉 ⇒ 8 个面板全关、画面全空（MCP 实测）。
                        //    ⇒ 必须**先关自己，等两帧、栈重算完成后再打开节点图**。
                        // ★★ 不在本面板里切面板（Overlay 关闭会触发栈重算，把新面板一起判掉）。
                        //    改为：置跨场景标记 + 回主城；由 MainSceneEntry 在**场景加载完成后**
                        //    直接打开节点图（那时栈是干净的，绝不可能失败）。
                        WanXiang.Modules.UI.SceneFlow.EnterFinaleMap = true;
                        CloseSelf();
                        WanXiang.Modules.UI.SceneFlow.EnterMain();
                    }
                    break;
                case 1:  // 续劫：回春 + 劫数 +1 + 敌强 +3%
                    {
                        run.Jie = run.Jie >= 3 ? 1 : run.Jie + 1;
                        if (run.Jie == 1) run.Realm = System.Math.Min(3, run.Realm + 1);
                        run.Act = 1;
                        run.NodeOffset = -1;
                        run.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);   // 新劫新图
                        if (run.QuestionRevealed != null) run.QuestionRevealed.Clear();
                        WanXiang.Run.RunSave.SaveCurrent();

                        CloseSelf();
                        var tt = OpenPanelAsync<CampaignPanel>();
                        Cysharp.Threading.Tasks.UniTaskExtensions.Forget<CampaignPanel>(tt);
                        break;
                    }
                default:  // 归元：结束本局，回主城
                    SceneFlow.IsFinaleBattle = false;
                    CloseSelf();
                    break;
            }
        }
    }
}
