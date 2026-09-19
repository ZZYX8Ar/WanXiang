// ============================================================================
//  Panel_Tale —— 异闻事件弹窗（选择不允许误触跳过）
//  v1.2 填充：轶事池（3 则）按局种子随机抽 1；三选一（各有代价）+ 拒绝换 1 灵卵。
//  效果通道同天象：PlayerBuffPct / EnemyBuffPct（下一场一次性挂起值）。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Tale", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class TalePanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpQuote;           // Tmp_ClassicQuote 典籍引文
        [SerializeField] private TMP_Text _tmpStory;           // Tmp_StoryText    白话叙述

        [SerializeField] private Button[] _optBtns;            // 三个选项
        [SerializeField] private TMP_Text[] _tmpOpts;          // 选项文本
        [SerializeField] private TMP_Text[] _tmpCosts;         // 选项代价
        [SerializeField] private Button _btnDecline;           // Btn_Decline 拒绝

        /// <summary>一则异闻：引文 + 叙述 + 三个选项（文本/代价/效果参数）。</summary>
        private struct Tale
        {
            public string Title, Quote, Story;
            public string[] OptText, OptCost;
            public int[] PlayerBuff, EnemyBuff, EggCost, EggGain;   // 每选项一组
        }

        private static readonly Tale[] Pool =
        {
            new Tale
            {
                Title = "精卫填海",
                Quote = "「常衔西山之木石，以堙于东海。」——《山海经·北山经》",
                Story = "路口的石鸟对你说：它填了几千年海，不差你这几块石头。但它愿意借你一点填海的执念。",
                OptText  = new [] { "借执念（强化全队）", "拾几枚石子（换灵卵）", "帮它搬一块最大的石头（强化敌军？）" },
                OptCost  = new [] { "失去 3 灵卵", "无", "下一场敌方 +20%" },
                PlayerBuff = new [] { 30, 0, 0 },
                EnemyBuff  = new [] { 0, 0, 20 },
                EggCost    = new [] { 3, 0, 0 },
                EggGain    = new [] { 0, 4, 0 },
            },
            new Tale
            {
                Title = "夸父逐日",
                Quote = "「弃其杖，化为邓林。」——《山海经·海外北经》",
                Story = "渴死巨人遗下的手杖长成了桃林。路过的人摘了桃子都说够甜，只是没人问过桃树同不同意。",
                OptText  = new [] { "摘桃充饥（换灵卵）", "折杖为兵（强化全队）", "把桃核种回去（下一场敌方 -15%）" },
                OptCost  = new [] { "下一场敌方 +15%", "失去 2 灵卵", "失去 2 灵卵" },
                PlayerBuff = new [] { 0, 20, 0 },
                EnemyBuff  = new [] { 15, 0, -15 },
                EggCost    = new [] { 0, 2, 2 },
                EggGain    = new [] { 3, 0, 0 },
            },
            new Tale
            {
                Title = "烛龙衔烛",
                Quote = "「视为昼，瞑为夜，吹为冬，呼为夏。」——《山海经·大荒北经》",
                Story = "衔着火烛的巨神睁眼便是白昼。它说可以为你把黑夜点亮一阵——但借来的光总是要还的。",
                OptText  = new [] { "借光（大幅强化全队）", "只借一小截（换灵卵）", "谢绝好意" },
                OptCost  = new [] { "失去 4 灵卵", "下一场敌方 +10%", "无" },
                PlayerBuff = new [] { 50, 0, 0 },
                EnemyBuff  = new [] { 0, 10, 0 },
                EggCost    = new [] { 4, 0, 0 },
                EggGain    = new [] { 0, 2, 0 },
            },
        };

        private Tale _current;
        private int _picked = -1;

        protected override void OnCreate()
        {
            for (int i = 0; i < _optBtns.Length; i++)
            {
                var idx = i;
                if (_optBtns[i] != null) _optBtns[i].onClick.AddListener(() => OnOptionClicked(idx));
            }
            if (_btnDecline != null) _btnDecline.onClick.AddListener(OnDeclineClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            ulong seed = WanXiang.Battle.Core.CoreMath.Fnv1a(
                "tale:" + (run != null ? run.RunSeed : 0) + ":" + (run != null ? run.NodeOffset : 0));
            _current = Pool[(int)(seed % (ulong)Pool.Length)];
            _picked = -1;

            if (_tmpTitle != null) _tmpTitle.text = "异闻 · " + _current.Title;
            if (_tmpQuote != null) _tmpQuote.text = _current.Quote;
            if (_tmpStory != null) _tmpStory.text = _current.Story;

            for (int i = 0; i < _tmpOpts.Length && i < _current.OptText.Length; i++)
                if (_tmpOpts[i] != null) _tmpOpts[i].text = _current.OptText[i];
            for (int i = 0; i < _tmpCosts.Length && i < _current.OptCost.Length; i++)
                if (_tmpCosts[i] != null) _tmpCosts[i].text = "代价：" + _current.OptCost[i];
            return UniTask.CompletedTask;
        }

        private void Apply(int index)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            run.PlayerBuffPct += _current.PlayerBuff[index];
            run.EnemyBuffPct += _current.EnemyBuff[index];
            run.Eggs = System.Math.Max(0, run.Eggs - _current.EggCost[index] + _current.EggGain[index]);
            WanXiang.Run.RunSave.SaveCurrent();
        }

        private void OnOptionClicked(int index)
        {
            if (_picked >= 0) return;      // 只能选一次
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && run.Eggs < _current.EggCost[index])
            {
                // 灵卵不足：本项选不了（不扣款）
                if (_tmpStory != null) _tmpStory.text = "你的灵卵不够付这个代价。";
                return;
            }
            _picked = index;
            Apply(index);
            BackToMap();
        }

        private void OnDeclineClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null) { run.Eggs += 1; WanXiang.Run.RunSave.SaveCurrent(); }   // GDD：拒绝换 1 灵卵
            BackToMap();
        }

        /// <summary>异闻是节点图的子面板：返回 = 回节点地图继续探索，不是回主城。</summary>
        private void BackToMap()
        {
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            CloseSelf();
            if (ui != null) _ = ui.OpenAsync<CampaignPanel>();
        }


    }
}
