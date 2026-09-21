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
        [SerializeField] private Button _btnBack;              // Btn_Back（兜底创建）

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
            EnsureBackButton();
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
            var __ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            Cysharp.Threading.Tasks.UniTask.Void(async () =>
            {
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                await __ui.OpenAsync<CampaignPanel>();
            });
        }

        private void OnDeclineClicked()
        {
            CloseSelf();      // 天象放弃无补偿（与异闻的"拒绝换灵卵"区分开）
            
            
        }

        /// <summary>返回地图按钮兜底（prefab 没有时代码创建，保证能返回继续探索）。</summary>
        private void EnsureBackButton()
        {
            if (_btnBack != null) return;

            var go = new GameObject("Btn_Back", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.94f, 0.92f, 0.88f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(96f, -52f);
            r.sizeDelta = new Vector2(152f, 64f);

            var tgo = new GameObject("Tmp_Label", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = "返回地图";
            tmp.fontSize = 26;
            tmp.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;

            _btnBack = btn;
            btn.onClick.AddListener(() =>
            {
                CampaignPanel.PendingCommit = -1;      // 返回 = 未完成，节点不通过
                CloseSelf();
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(30);
                    await ui.OpenAsync<CampaignPanel>();
                });
            });
        }

    }
}
