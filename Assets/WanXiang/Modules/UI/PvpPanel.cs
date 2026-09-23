// ============================================================================
//  Panel_Pvp —— 好友对战（配对码）
//  ---------------------------------------------------------------------------
//  语义（用户定案）：把「我这一局的最终编队」生成配对码发给朋友；
//  朋友粘贴后，用【他的编队】vs【我的编队】跑一场 **AI 自动对战**（无手动），
//  双方各跑一遍必然得到同一份 `PvpReport`（含指纹可对账）。
//
//  底层已具备：ShareCode（编解码）+ PvpMatch.PlayByCode（对战）。
//  本面板只做：取码 / 解码 / 填 PvpContent / 展示战报。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Pvp", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class PvpPanel : UIPanelBase
    {
        [SerializeField] private TMP_InputField _inputOpp;    // Inp_OppCode（粘贴对手码）
        [SerializeField] private TMP_Text _tmpMyCode;         // Tmp_MyCode（我的码，可复制）
        [SerializeField] private Button _btnCopyMine;         // Btn_CopyMine
        [SerializeField] private Button _btnFight;            // Btn_Fight
        [SerializeField] private TMP_Text _tmpResult;         // Tmp_Result（战报）
        [SerializeField] private Button _btnBack;             // Btn_Back

        protected override void OnCreate()
        {
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);
            if (_btnCopyMine != null) _btnCopyMine.onClick.AddListener(OnCopyMine);
            if (_btnFight != null) _btnFight.onClick.AddListener(OnFight);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            RenderMine();
            if (_tmpResult != null) _tmpResult.text = "粘贴朋友的配对码，然后点「开始对战」。";
            return UniTask.CompletedTask;
        }

        // ---------------------------------------------------------------- 我的码

        private void RenderMine()
        {
            if (_tmpMyCode == null) return;
            var mine = MyLatestCode();
            _tmpMyCode.text = string.IsNullOrEmpty(mine)
                ? "（还没有可分享的编队 —— 先打一局）"
                : ("我的配对码：\n" + mine);
        }

        /// <summary>取我最近一局的编队码（历程里最新的那条）。</summary>
        private static string MyLatestCode()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            if (meta == null || meta.HistCodes.Count == 0) return "";
            for (int i = meta.HistCodes.Count - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(meta.HistCodes[i])) return meta.HistCodes[i];
            return "";
        }

        private void OnCopyMine()
        {
            var mine = MyLatestCode();
            if (string.IsNullOrEmpty(mine)) { Debug.LogWarning("[PvpPanel] 没有可复制的码"); return; }
            GUIUtility.systemCopyBuffer = mine;
            Debug.Log("[PvpPanel] 已复制我的配对码：" + mine);
            if (_tmpResult != null) _tmpResult.text = "已复制我的配对码（发给朋友）。\n" + mine;
        }

        // ---------------------------------------------------------------- 对战

        private void OnFight()
        {
            var mine = MyLatestCode();
            if (string.IsNullOrEmpty(mine)) { Show("我还没有配对码 —— 先打一局。"); return; }

            string opp = _inputOpp != null ? (_inputOpp.text ?? "").Trim() : "";
            if (string.IsNullOrEmpty(opp)) { Show("请先粘贴对方的配对码。"); return; }

            // 内容：异兽表 + 灵魂表 + 技能解析器（与融合管线同一份）
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0) { Show("找不到内容目录，无法对战。"); return; }
            var beasts = WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
            if (beasts == null || beasts.Length == 0) { Show("内容目录为空。"); return; }

            var souls = new System.Collections.Generic.List<WanXiang.Fusion.SoulDef>(beasts.Length);
            for (int i = 0; i < beasts.Length; i++) souls.Add(WanXiang.Fusion.SoulForge.Derive(beasts[i], i));

            WanXiang.Fusion.SkillResolver resolver = (skillId) =>
            {
                for (int i = 0; i < beasts.Length; i++)
                {
                    var arr = beasts[i].AllSkills;
                    for (int k = 0; k < arr.Length; k++)
                        if (arr[k] != null && arr[k].Id == skillId) return arr[k];
                }
                return null;
            };

            var content = new WanXiang.Pvp.PvpContent
            {
                Beasts = beasts,
                Souls = souls.ToArray(),
                Resolver = resolver,
            };

            WanXiang.Pvp.PvpReport report;
            try
            {
            // ★★ 进入战斗场景（AI 自动对战回放）
            var myEntries = WanXiang.Pvp.PvpMatch.BuildSquadOf(mine, content,
                WanXiang.Battle.Core.TeamSide.Player, out string errMine);
            if (myEntries == null) { Show("我方：" + errMine); return; }
            var oppEntries = WanXiang.Pvp.PvpMatch.BuildSquadOf(opp, content,
                WanXiang.Battle.Core.TeamSide.Enemy, out string errOpp);
            if (oppEntries == null) { Show("对方：" + errOpp); return; }

            ulong seed = WanXiang.Pvp.PvpMatch.SeedOf(mine, opp);

            var req = new WanXiang.Modules.UI.BattleRequest
            {
                Title = "好友对战",
                WeatherName = "AI 自动对战（双方各自动放技能）",
                Seed = seed,
            };
            req.EnemyEntries.AddRange(oppEntries);
            foreach (var en in oppEntries) { req.Enemy.Add(en.Def); req.EnemyMul.Add(en.StatMul); }
            req.Player.Clear();
            if (req.PlayerCells == null) req.PlayerCells = new System.Collections.Generic.List<int>();
            req.PlayerCells.Clear();
            foreach (var e in myEntries) { req.Player.Add(e.Def); req.PlayerCells.Add(e.BoardSlot); req.PlayerMul.Add(e.StatMul); }

            CloseSelf();
            SceneFlow.EnterBattle(req);
        }

        private void Show(string text)
        {
            if (_tmpResult != null) _tmpResult.text = text;
        }
    }
}
