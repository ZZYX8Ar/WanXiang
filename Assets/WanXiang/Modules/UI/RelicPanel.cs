// ============================================================================
//  Panel_Relic —— 遗物节点（每幕第二层固定节点，参考杀戮尖塔）
//  ---------------------------------------------------------------------------
//  从遗物池随机抽 3 个，玩家挑 1 个入队（局内变强载体）。
//  UI 全部在 prefab 里：三张草稿卡（Draft_0/1/2 + Tmp_DraftName/Desc）+ 确认/跳过。
//   绑定名与 ResultPanel 的奖励区完全一致，便于美术在 prefab 上改。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Campaign;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Relic", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient, FullScreen = false,
             CloseOnMaskClick = false)]
    public sealed class RelicPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;
        [BindArray("Draft_{0}", 3)]
        [SerializeField] private Button[] _draftBtns;
        [BindArray("Tmp_DraftName_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftNames;
        [BindArray("Tmp_DraftDesc_{0}", 3)]
        [SerializeField] private TMP_Text[] _tmpDraftDescs;
        /// <summary>
        /// 每张卡右上角的「本队」角标（与本队异兽相关的遗物才显示）。
        /// 节点由 prefab 提供（`Draft_i/Mark_Team_i`，含底色 Image + 文字 Tmp_Mark）⇒ 可在 prefab 里改配色/换图。
        /// </summary>
        [BindArray("Mark_Team_{0}", 3)]
        [SerializeField] private UnityEngine.GameObject[] _markTeam;
        [SerializeField] private Button _btnConfirm;
        [SerializeField] private Button _btnSkip;

        private int _selected = -1;
        private System.Collections.Generic.List<RelicDef> _offered;

        protected override void OnCreate()
        {
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                var idx = i;
                if (_draftBtns[i] != null) _draftBtns[i].onClick.AddListener(() => OnDraftClicked(idx));
            }
            if (_btnConfirm != null) _btnConfirm.onClick.AddListener(OnConfirmClicked);
            if (_btnSkip != null) _btnSkip.onClick.AddListener(OnSkipClicked);
        }

        /// <summary>
        /// 这件遗物是不是"加成我方/本队"的？—— **敌方弱化类不算**。
        /// 用户实测：`素金克星玉 · 敌方金属性 -8%`（Effect=EnemyElementWeakPct, ScopeId="Metal"）
        /// 因为 ScopeId 命中了本队某只金属性异兽而被标上「本队」，但它实际是**削敌方**，跟"适配本队"无关。
        /// （全局无 ScopeId 的本来就不参与本队角标。）
        /// </summary>
        private static bool IsTeamBenefiting(RelicDef d)
        {
            if (d == null) return false;
            if (d.Effect == RelicEffect.EnemyWeakPct) return false;        // 敌方全属性 -%
            if (d.Effect == RelicEffect.EnemyElementWeakPct) return false; // 敌方指定五行 -%
            if (d.Mechanic == RelicMechanic.EnemyCautious) return false;   // 敌方 AI 变保守
            return true;
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // 抽 3 个遗物（本节点本身是「遗物」节点，视为稀有权重偏高）
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.Act : 1;
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                (ulong)System.DateTime.Now.Ticks ^ (uint)(act * 40503));
            // ★ 与本队异兽相关的遗物**加权**出现（用户 2026-10-07 定案）：
            //   否则给的三选一常是"跟我队伍无关"的东西，玩家没法做"适配队伍"的决策。
            //   作用域口径与遗物结算完全一致（RelicCatalog.AddBeastScopes）：异兽 id / 五行 / 定位。
            System.Collections.Generic.HashSet<string> teamScopes = null;
            var cats = UnityEngine.Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            var cat = (cats != null && cats.Length > 0) ? cats[0] : null;
            if (cat != null && run != null && run.Team != null && cat.Beasts != null)
            {
                teamScopes = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < run.Team.Count; i++)
                {
                    string tid = run.Team[i];
                    if (string.IsNullOrEmpty(tid)) continue;
                    for (int k = 0; k < cat.Beasts.Length; k++)
                    {
                        var cfg = cat.Beasts[k];
                        if (cfg == null || cfg.BeastId != tid) continue;
                        WanXiang.Campaign.RelicCatalog.AddBeastScopes(
                            teamScopes, cfg.BeastId, cfg.Element, cfg.Role);
                        break;
                    }
                }
                Debug.Log("[RelicPanel] 队伍作用域加权 " + teamScopes.Count + " 项（队伍 " +
                          run.Team.Count + " 只）：" + string.Join(",", teamScopes));
            }

            _offered = RelicCatalog.Roll(3, act, true, rng,
                run != null ? run.Relics : null, teamScopes);

            // ★ 「本队」角标：把**与本队异兽相关**的遗物标出来（作用域命中 = 同五行/同定位/该异兽专属）。
            //   加权只是让它们更容易出现；角标是让玩家**一眼看出该挑哪个**。
            for (int i = 0; i < (_markTeam != null ? _markTeam.Length : 0); i++)
            {
                if (_markTeam[i] == null) continue;
                bool related = _offered != null && i < _offered.Count
                            && teamScopes != null && teamScopes.Count > 0
                            && !string.IsNullOrEmpty(_offered[i].ScopeId)
                            && teamScopes.Contains(_offered[i].ScopeId)
                            && IsTeamBenefiting(_offered[i]);   // ★ "减敌方属性"那种不算本队（用户实测）
                _markTeam[i].SetActive(related);
            }
            _selected = -1;

            if (_tmpTitle != null) _tmpTitle.text = "遗物 · 三选一（本局已持有 " +
                (run != null && run.Relics != null ? run.Relics.Count : 0) + " 件）";

            for (int i = 0; i < _tmpDraftNames.Length; i++)
            {
                if (_tmpDraftNames[i] != null)
                    _tmpDraftNames[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Name : "—";
                if (_tmpDraftDescs[i] != null)
                    _tmpDraftDescs[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Desc : "";
            }

            if (_btnConfirm != null) _btnConfirm.interactable = false;
            return UniTask.CompletedTask;
        }

        private void OnDraftClicked(int index)
        {
            _selected = index;
            if (_btnConfirm != null) _btnConfirm.interactable = true;
            for (int i = 0; i < _draftBtns.Length; i++)
            {
                if (_draftBtns[i] == null) continue;
                var img = _draftBtns[i].targetGraphic as Image;
                if (img != null)
                    img.color = (i == index) ? new Color(0.79f, 0.63f, 0.39f, 1f) : Color.white;
            }
        }

        private void OnSkipClicked() => BackToCampaign();

        private void OnConfirmClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && _offered != null && _selected >= 0 && _selected < _offered.Count)
            {
                var relic = _offered[_selected];
                if (run.Relics == null) run.Relics = new System.Collections.Generic.List<string>();
                if (!run.Relics.Contains(relic.Id)) run.Relics.Add(relic.Id);
                WanXiang.Run.RunSave.SaveCurrent();
                Debug.Log("[RelicPanel] 获得遗物：" + relic.Name + "（" + relic.Id + "）");
            }
            BackToCampaign();
        }

        private void BackToCampaign()
        {
            CloseSelf();
            OpenPanelAsync<CampaignPanel>().Forget();
        }
    }
}
