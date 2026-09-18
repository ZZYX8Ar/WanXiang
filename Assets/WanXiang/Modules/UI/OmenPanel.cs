// ============================================================================
//  Panel_Omen —— 天象抉择弹窗（收益鎏金 / 代价朱红，两栏等宽）
//  v1.2 填充：天象池（4 个）按局种子随机抽 1；接受 → 写入挂起增益（下一场生效）。
//  效果通道：PlayerBuffPct（我方）/ EnemyNerfPct（敌方减益）—— 与孵穴的 HealPending
//  一样都是"下一场战斗一次性"的挂起值，TryBuildFromRun 消费后清零。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Omen", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class OmenPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;           // Tmp_Title
        [SerializeField] private TMP_Text _tmpGain;            // Tmp_GainText  收益（鎏金栏）
        [SerializeField] private TMP_Text _tmpCost;            // Tmp_CostText  代价（朱红栏）
        [SerializeField] private Button _btnAccept;            // Btn_Accept
        [SerializeField] private Button _btnDecline;           // Btn_Decline

        /// <summary>一条天象：收益 + 明确的负面（GDD：每个增益都配一条代价）。</summary>
        private struct Omen
        {
            public string Name, Gain, Cost;
            public int PlayerBuffPct;    // 下一场我方 %（可负）
            public int EnemyBuffPct;     // 下一场敌方 %（正=变强）
            public int EggDelta;         // 灵卵增减
        }

        private static readonly Omen[] Pool =
        {
            new Omen { Name = "亢阳", Gain = "下一场战斗全队能力 +30%", Cost = "失去 3 枚灵卵",
                       PlayerBuffPct = 30, EggDelta = -3 },
            new Omen { Name = "霈泽", Gain = "获得 4 枚灵卵", Cost = "下一场战斗敌方 +20%",
                       EggDelta = 4, EnemyBuffPct = 20 },
            new Omen { Name = "风蚀", Gain = "获得 6 枚灵卵", Cost = "下一场战斗全队能力 -10%",
                       EggDelta = 6, PlayerBuffPct = -10 },
            new Omen { Name = "晦明", Gain = "下一场战斗敌方 -15%", Cost = "失去 2 枚灵卵",
                       EnemyBuffPct = -15, EggDelta = -2 },
        };

        private Omen _current;

        public override bool AllowBackClose => false;

        protected override void OnCreate()
        {
            if (_btnAccept != null) _btnAccept.onClick.AddListener(OnAcceptClicked);
            if (_btnDecline != null) _btnDecline.onClick.AddListener(OnDeclineClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            var run = WanXiang.Run.RunSave.Current;
            ulong seed = WanXiang.Battle.Core.CoreMath.Fnv1a(
                "omen:" + (run != null ? run.RunSeed : 0) + ":" + (run != null ? run.NodeOffset : 0));
            _current = Pool[(int)(seed % (ulong)Pool.Length)];

            if (_tmpTitle != null) _tmpTitle.text = "天象 · " + _current.Name;
            if (_tmpGain != null) _tmpGain.text = "收益　" + _current.Gain;
            if (_tmpCost != null) _tmpCost.text = "代价　" + _current.Cost;
            return UniTask.CompletedTask;
        }

        private void OnAcceptClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && _current.Name != null)
            {
                run.PlayerBuffPct += _current.PlayerBuffPct;
                run.EnemyBuffPct += _current.EnemyBuffPct;
                run.Eggs = System.Math.Max(0, run.Eggs + _current.EggDelta);
                WanXiang.Run.RunSave.SaveCurrent();
            }
            CloseSelf();
        }

        private void OnDeclineClicked()
        {
            CloseSelf();      // 天象放弃无补偿（与异闻的"拒绝换灵卵"区分开）
        }
    }
}
