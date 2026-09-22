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
                case 0:  // 登天阙：终局战
                    {
                        var all = _contentCatalog != null ? ContentLibrary.BuildBeasts(_contentCatalog) : null;
                        if (all == null || all.Length == 0) { CloseSelf(); return; }

                        // 主将 = 图鉴最强（后土的 BeastDef 待内容补齐，先以最强者顶位并注明）
                        BeastDef boss = all[0];
                        foreach (var b in all)
                            if ((int)b.Rarity > (int)boss.Rarity) boss = b;

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
                        {
                            var byId = new System.Collections.Generic.Dictionary<string, BeastDef>();
                            foreach (var b in all) byId[b.Id] = b;
                            if (byId.TryGetValue(run.Team[i], out var mirror))
                                entries.Add(DeployEntry.Enemy(mirror, BattleRequest.Cells[i]).WithMul(1.2f));
                        }
                        req.EnemyEntries.AddRange(entries);

                        // ★ 标记为终局战：胜利即"真通关"（ResultPanel 据此写 BeatFinale）
                        SceneFlow.IsFinaleBattle = true;

                        CloseSelf();
                        SceneFlow.EnterBattle(req);
                        break;
                    }
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
