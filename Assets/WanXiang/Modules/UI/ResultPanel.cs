// ============================================================================
//  Panel_Result —— 战斗结算（含三选一技能草稿）
//  ---------------------------------------------------------------------------
//  三种出口：确认（拿草稿继续）→ 回节点地图；跳过（+1 灵卵）→ 回节点地图；
//  失败 → **本局直接结束**（清空局内进度、保留胜败统计）；撤退 → 回节点地图。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>结算面板入参。</summary>
    public sealed class ResultRequest
    {
        public bool Win;
        public bool Retreated;
        public int Turns;
        public uint Fingerprint;
        public string Summary = "";
    }

    [UIPanel("Panel_Result", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]   // 奖励三选一：点空白不能关 —— 用户点歪一下整个面板就没了（实测）
    public sealed class ResultPanel : UIPanelBase
    {
        /// <summary>结算必须看完（不该被 Esc 关掉）（基类默认允许，这里显式禁止）。</summary>
        public override bool AllowBackClose => false;

        [SerializeField] private Image _imgBanner;             // Img_Banner    胜负横幅
        [BindArray("Tmp_Line_{0}", 4)]
        [SerializeField] private TMP_Text[] _tmpLines;         // 结算明细 4 行
        [SerializeField] private Image _imgEgg;                // Img_RewardEgg 灵卵大图
        [BindArray("Draft_{0}", 3)]
        [SerializeField] private Button[] _draftBtns;          // 三选一草稿卡
        [BindArray("Tmp_DraftName_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftNames;    // 草稿名
        [BindArray("Tmp_DraftDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftDescs;    // 草稿描述
        [SerializeField] private Button _btnSkip;              // Btn_Skip
        [SerializeField] private Button _btnConfirm;           // Btn_Confirm

        [Header("美术（由生成器自动绑定）")]
        [SerializeField] private Sprite _spriteWin;
        [SerializeField] private Sprite _spriteLose;

        private int _selectedDraft = -1;
        private bool _win;      // 本场胜负（结算奖励用）

        protected override void OnCreate()
        {
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                var idx = i;
                if (_draftBtns[i] != null) _draftBtns[i].onClick.AddListener(() => OnDraftClicked(idx));
            }
            if (_btnSkip != null) _btnSkip.onClick.AddListener(OnSkipClicked);
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var req = payload as ResultRequest;
            bool win = req != null && req.Win;
            _win = win;
            _selectedDraft = -1;
            // ★★ 这里原本有一行 `_win = false;` —— 它把上面刚算好的胜负又覆盖成"失败"，
            //    导致"明明赢了却判失败/奖励不显示/确认可直接点/回主界面/节点不解锁"五个症状。
            //    已删除，胜负只认 ResultRequest.Win。

            if (_imgBanner != null)
            {
                var s = win ? _spriteWin : _spriteLose;
                if (s != null) _imgBanner.sprite = s;
                _imgBanner.color = s != null ? Color.white : (win ? new Color(0.78f, 0.21f, 0.17f, 1f)
                                                                 : new Color(0.45f, 0.41f, 0.35f, 1f));
            }

            // 明细行：回合 / 事件指纹 / 结果
            SetLine(0, win ? "战斗胜利" : (req != null && req.Retreated ? "撤退" : "战斗失败"));
            SetLine(1, "回合数：" + (req != null ? req.Turns : 0));

            // ★ 结算信息改为"本局探索回顾"（用户要求）：过程指纹对玩家无意义，去掉；
            //   失败时也要说明"本局已结束"，而不是显示"灵卵 +1"（失败会清空）。
            var look = WanXiang.Run.RunSave.Current;
            int act = look != null ? look.Act : 1;
            int layer = look != null ? UnityEngine.Mathf.Max(1, look.NodeOffset + 1) : 1;   // 未出发显示第1层，不要第0层
            int wins = look != null ? look.Wins : 0;
            int losses = look != null ? look.Losses : 0;
            SetLine(2, win
                ? "本局进度：第 " + act + " 幕 · 第 " + layer + " 层"
                : "倒在：第 " + act + " 幕 · 第 " + layer + " 层（本局胜 " + wins + " 场）");
            SetLine(3, win
                ? "灵卵 +1"
                : "本局结束 —— 进度已清空，再次出征将从第一幕重新开始");

            // ★ 奖励区整块开关（方案 B）：prefab 里所有奖励控件都在 Root_Rewards 下，
            //   这里一行控制 —— 胜利显示奖励、失败隐藏（不再逐个 SetActive，避免遗漏）。
            var rewardsRoot = transform.Find("Root_Rewards");
            if (rewardsRoot != null) rewardsRoot.gameObject.SetActive(win);

            // 三选一奖励（v2.1）：不再是无内容的占位草稿，走 RunState 已有字段立即生效。
            for (int i = 0; i < _tmpDraftNames.Length; i++)
                if (_tmpDraftNames[i] != null) _tmpDraftNames[i].text = DraftTitle(i);
            for (int i = 0; i < _tmpDraftDescs.Length; i++)
                if (_tmpDraftDescs[i] != null) _tmpDraftDescs[i].text = DraftDesc(i);

            // ★ 按胜负决定确认按钮是否可点（原来无条件设 false，把"失败可直接确认"覆盖掉了）：
            //   胜利 → 必须先选一张奖励才可确认；失败 → 没有奖励，直接可确认。
            if (_btnConfirm != null) _btnConfirm.interactable = !_win;
            return UniTask.CompletedTask;
        }

        private void SetLine(int i, string text)
        {
            if (_tmpLines != null && i < _tmpLines.Length && _tmpLines[i] != null)
                _tmpLines[i].text = text;
        }

        private void OnDraftClicked(int index)
        {
            _selectedDraft = index;
            if (_btnConfirm != null) _btnConfirm.interactable = true;
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                if (_draftBtns[i] == null) continue;
                var img = _draftBtns[i].targetGraphic as Image;
                if (img != null)
                    img.color = (i == index) ? new Color(0.79f, 0.63f, 0.39f, 1f) : Color.white;
            }
        }

        private void OnSkipClicked()
        {
            BackToCampaign();
        }

        private void OnConfirmClicked()
        {
            // 结构验证版：三选一草稿还不是真实内容，没选也允许继续（选了就记一下）
            if (_selectedDraft >= 0)
                ApplyDraft(_selectedDraft);
                Debug.Log("[ResultPanel] 已选择奖励 " + (_selectedDraft + 1) + "：" + DraftTitle(_selectedDraft));
            ApplyOutcome();
            BackToCampaign();
        }

        /// <summary>
        /// 把这一场的胜败写进旅程：赢了给灵卵/墨锭并推进劫数，输了也有少量保底。
        /// 数值是占位节奏（每胜一场进一劫；三劫一境、三境一周目），正式数值等 GDD 定稿后改这里。
        /// </summary>
        private void ApplyOutcome()
        {
            var cur = WanXiang.Run.RunSave.Current;
            if (cur == null)
            {
                Debug.LogWarning("[ResultPanel] 没有进行中的旅程（试炼直进战斗），本场不写入存档。");
                return;
            }

            if (_win)
            {
                cur.Wins++;
                // ★ 战斗胜利固定送 1 枚灵卵。
                //   原来是 `8 + Jie * 2` —— 那是早期"劫/境"体系的遗留公式，
                //   会导致打两场就涨到 26+（用户实测"才两场就 27 了"），且与界面文案
                //   （"灵卵 +1"）完全不符。现已改为与 GDD 一致：胜利固定 +1。
                //   Jie/Realm 的递进不再执行（幕推进已由 Act 承担），字段保留以兼容旧存档。
                cur.Eggs += 1;
                cur.Ink += 1;

                // ★ 胜利掉魂（B 来源）：按幕数给概率，掉"本场敌方某只"的魂。
                //   魂本体不落盘，只记主人 id —— 取用时 SoulForge.Derive 重建。
                // ★ 掉精魄（进化材料，比魂稀有）：15% + 幕数×8%
                {
                    var metaE = WanXiang.Meta.MetaStore.Ensure();
                    if (metaE != null)
                    {
                        int chanceE = 15 + cur.Act * 8;      // 第1幕 23% → 第4幕 47%
                        if (UnityEngine.Random.Range(0, 100) < chanceE)
                        {
                            metaE.Essence += 1;
                            WanXiang.Meta.MetaStore.Save();
                            Debug.Log("[ResultPanel] 掉精魄 +1（现有 " + metaE.Essence + "）—— 可用于异兽进化");
                        }
                    }
                }

                {
                    var foeIds = WanXiang.Modules.UI.SceneFlow.LastFoeIds;
                    if (foeIds != null && foeIds.Count > 0)
                    {
                        int chance = 25 + cur.Act * 10;          // 第1幕 35% → 第4幕 65%
                        if (UnityEngine.Random.Range(0, 100) < chance)
                        {
                            var pick = foeIds[UnityEngine.Random.Range(0, foeIds.Count)];
                            if (cur.Souls == null) cur.Souls = new System.Collections.Generic.List<string>();
                            cur.Souls.Add(pick);
                            Debug.Log("[ResultPanel] 掉魂：" + pick + "（现有 " + cur.Souls.Count + " 个）");
                        }
                    }
                }

                // ★★ 终局战（登天阙）胜利 = **真通关**：写入 BeatFinale。
                //    这是"通关一次后解锁无尽模式 / 难度"的唯一依据。
                if (SceneFlow.IsFinaleBattle)
                {
                    SceneFlow.IsFinaleBattle = false;
                    // ★ 局外结算：通关（cleared=true）
                    try { WanXiang.Meta.MetaStore.SettleRun(cur, true); }
                    catch (System.Exception ex) { Debug.LogWarning("[ResultPanel] 局外结算异常：" + ex.Message); }
                    if (!cur.BeatFinale)
                    {
                        cur.BeatFinale = true;
                        Debug.Log("[ResultPanel] ★ 终局战胜利 ⇒ 通关！解锁无尽模式与更高难度");
                    }
                }

                // ★★ 终局战（登天阙）胜利 = **真通关**：写入 BeatFinale。
                //    这是"通关一次后解锁无尽模式 / 难度"的唯一依据。
                if (SceneFlow.IsFinaleBattle)
                {
                    SceneFlow.IsFinaleBattle = false;
                    if (!cur.BeatFinale)
                    {
                        cur.BeatFinale = true;
                        Debug.Log("[ResultPanel] ★ 终局战胜利 ⇒ 通关！解锁无尽模式与更高难度");
                    }
                }
            }
            else
            {
                // ★ 失败 = **这一局直接结束**（用户规则）。旧实现只给 2 灵卵保底、节点照样推进
                //   ⇒ 失败没有代价（结构验证版占位）。现在清空本局进度，只保留胜败统计。
                cur.Losses++;
                // ★ 局外结算：本局失败（cleared=false，但走过的路也算收益）
                //    必须在下面的"清空进度"**之前**。
                try { WanXiang.Meta.MetaStore.SettleRun(cur, false); }
                catch (System.Exception ex) { Debug.LogWarning("[ResultPanel] 局外结算异常：" + ex.Message); }
                cur.Eggs = 0;
                cur.Ink = 0;
                cur.Act = 1;
                cur.NodeOffset = -1;
                cur.Jie = 1;
                cur.Realm = 1;
                if (cur.Team != null) cur.Team.Clear();
                if (cur.Collection != null) cur.Collection.Clear();
                if (cur.VisitedNodes != null) cur.VisitedNodes.Clear();
                if (cur.Path != null) cur.Path.Clear();
                if (cur.QuestionRevealed != null) cur.QuestionRevealed.Clear();
                cur.RunSeed = UnityEngine.Random.Range(1, int.MaxValue);   // 新一局 = 新路线
                CampaignPanel.PendingCommit = -1;                          // 失败不推进节点
                Debug.Log("[ResultPanel] 战斗失败 ⇒ 本局结束（保留胜败统计，进度已清空）");
            }
            WanXiang.Run.RunSave.Save(cur);
            Debug.Log("[ResultPanel] 旅程已保存：槽位 " + cur.Slot + " " + cur.RealmText +
                      " 灵卵 " + cur.Eggs);
        }

        private void BackToCampaign()
        {
            CloseSelf();
            // ★ 按结果分流（用户规则）：
            //   胜利 → 回节点地图继续探索
            //   失败 → 本局已结束，回**主界面**（让玩家在主界面决定"继续/新局"），
            //          以前无条件回节点图，看起来像"进度被清还留在游戏里"（用户实测）。
            if (_win)
            {
                // ★★ 终局战胜利 = 通关 ⇒ 回【主界面】，不能回节点图：
                //    通关后 Act=5（天阙），那张图没有可走的节点 ⇒ 回节点图会是一片空白
                //    （用户实测：选完通关奖励后画面全空）。
                var r = WanXiang.Run.RunSave.Current;
                if (r != null && r.BeatFinale) OpenPanelAsync<HomePanel>().Forget();
                else OpenPanelAsync<CampaignPanel>().Forget();
            }
            else OpenPanelAsync<HomePanel>().Forget();
        }

        // ================================================================
        //  三选一奖励（用 RunState 已有字段，美术/内容接入后可换成图鉴奖励）
        // ================================================================
        private static string DraftTitle(int i)
        {
            switch (i)
            {
                case 0: return "灵卵 +2";
                case 1: return "全队疗愈";
                default: return "威慑";
            }
        }

        private static string DraftDesc(int i)
        {
            switch (i)
            {
                case 0: return "立刻获得 2 枚灵卵，可在灵市换取异兽。";
                case 1: return "全队回复 15% 生命（下一场开打前生效）。";
                default: return "下一场战斗敌方属性 -5%（士气受挫）。";
            }
        }

        private static void ApplyDraft(int i)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            switch (i)
            {
                case 0:
                    run.Eggs += 2;
                    break;
                case 1:
                    run.HealPending += 15;          // 回到节点图后由孵穴/下一场结算
                    break;
                default:
                    run.EnemyBuffPct -= 5;
                    break;
            }
            WanXiang.Run.RunSave.SaveCurrent();
        }

    }
}
