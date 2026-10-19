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
using DG.Tweening;
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
        /// 节点由 prefab 提供（`Draft_i/Mark_Team_i`，含底色 Image + 文字 Tmp_Mark）， 可在 prefab 里改配色/换图。
        /// </summary>
        [BindArray("Mark_Team_{0}", 3)]
        [SerializeField] private UnityEngine.GameObject[] _markTeam;
        [SerializeField] private Button _btnConfirm;
        [SerializeField] private Button _btnSkip;

private int _selected = -1;
        private System.Collections.Generic.List<RelicDef> _offered;

        /// <summary>
        /// 选完（或跳过）之后回哪儿。默认回 CampaignPanel；秘境里的遗物节点要回 <see cref="RealmPanel"/>。
        /// ⛔ 这是**静态**字段，所以调用方必须在用完立刻调 <see cref="ClearReturnTarget"/> ——
        ///   否则下一次从主线进遗物节点会被上一次秘境留下的目标劫持，回错界面。
        /// </summary>
        private static System.Action _afterPick;

        /// <summary>设置"选完回哪儿"（由调用方在 finally 里用 <see cref="ClearReturnTarget"/> 清掉）。</summary>
        public static void SetReturnTarget(System.Action afterPick) { _afterPick = afterPick; }

        /// <summary>清掉返回目标（防劫持）。</summary>
  public static void ClearReturnTarget() { _afterPick = null; }

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
            // ★ 2026-10-19 用户："遗物面板还是黑背景，换成和招募一样的纸底"
            var kitPanel0 = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Kit/panel_dialog");
            var selfImg0 = GetComponent<Image>();
            if (kitPanel0 != null && selfImg0 != null)
            {
                selfImg0.sprite = kitPanel0;
                selfImg0.type = Image.Type.Sliced;
                selfImg0.color = Color.white;
            }

            // 抽 3 个遗物（本节点本身是「遗物」节点，视为稀有权重偏高）
            var run = WanXiang.Run.RunSave.Current;
            int act = run != null ? run.Act : 1;
            // ★ 2026-10-19 用户："X 只是关闭暂不选择，再次打开还是一样的" ——
            //   种子必须由（RunSeed, 幕, 节点）决定，不能用 DateTime.Now（那会每次重抽）。
            int nodeKeyR = WanXiang.Modules.UI.CampaignPanel.PendingCommit >= 0
                ? WanXiang.Modules.UI.CampaignPanel.PendingCommit
                : (run != null ? run.NodeOffset : -1);
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                WanXiang.Battle.Core.CoreMath.Fnv1a(
                    "relic:" + (run != null ? run.RunSeed : 0) + ":" + act + ":" + nodeKeyR));
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

            // ★ 2026-10-19 用户："底部可以大一点，现在左右都重叠了，上下也可以扩大一点，
            //   顶部的字还被遮挡" —— 面板放大 + 标题/卡片/按钮统一重排（代码版式）。
            {
                var selfRt = (RectTransform)transform;
                selfRt.sizeDelta = new Vector2(1120f, 720f);
                if (_tmpTitle != null)
                {
                    var trt = (RectTransform)_tmpTitle.transform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                    trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.sizeDelta = new Vector2(980f, 56f);
                    trt.anchoredPosition = new Vector2(0f, 302f);
                }
                if (_btnConfirm != null)
                {
                    var brt = (RectTransform)_btnConfirm.transform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.sizeDelta = new Vector2(180f, 56f);
                    brt.anchoredPosition = new Vector2(-104f, -302f);
                }
                if (_btnSkip != null)
                {
                    var srt = (RectTransform)_btnSkip.transform;
                    srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
                    srt.pivot = new Vector2(0.5f, 0.5f);
                    srt.sizeDelta = new Vector2(180f, 56f);
                    srt.anchoredPosition = new Vector2(104f, -302f);
                }
            }

            if (_tmpTitle != null) _tmpTitle.text = "遗物 · 三选一（本局已持有 " +
                (run != null && run.Relics != null ? run.Relics.Count : 0) + " 件）";

            for (int i = 0; i < _tmpDraftNames.Length; i++)
            {
                if (_tmpDraftNames[i] != null)
                    _tmpDraftNames[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Name : "—";
                if (_tmpDraftDescs[i] != null)
                    _tmpDraftDescs[i].text = (_offered != null && i < _offered.Count) ? _offered[i].Desc : "";
            }

            // ★ 2026-10-19 用户："遗物不要上下排序，要肉鸽那种三选一、左右排、弹出来的动画"
            //   卡面 = kit 底（不塞立绘，立绘走独立子节点，避免拉伸/染色互相打架）；
            //   布局与弹入动画全部运行时做，prefab 保持原样。
            ApplyThreeChoiceLayout();

            if (_btnConfirm != null) _btnConfirm.interactable = false;
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 肉鸽式三选一：三张卡**横排**、逐个弹入（缩放 0.86→1 + 淡入，间隔 90ms）。
        /// 卡内版式：图标在上、名字居中、描述在下、「本队」角标贴底。
        /// ⛔ 只动 rect / 子节点位置与动画，不改 prefab 资产。
        /// </summary>
        private void ApplyThreeChoiceLayout()
        {
            var kitCard = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Kit/panel_dialog");
            var relicSprites = RelicSpriteCatalog.Find();
            var draftColors = new[] { -290f, 0f, 290f };

            for (int i = 0; i < _draftBtns.Length; i++)
            {
                var btn = _draftBtns[i];
                if (btn == null) continue;
                var rt = (RectTransform)btn.transform;

                // 卡：横排
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(250f, 450f);
                rt.anchoredPosition = new Vector2(draftColors[i], 30f);

                // 卡面：kit 细边框（点选高亮仍由 OnDraftClicked 染色）
                var bg = btn.targetGraphic as Image;
                if (bg != null && kitCard != null)
                {
                    // 方版底（panel_dialog 是方图）比细条底更适合 210x400 的竖卡，Sliced 不会看着拉伸
                    bg.sprite = kitCard;
                    bg.type = Image.Type.Sliced;
                    bg.color = Color.white;
                }

                // 遗物图标：独立子节点（顶部居中，保持比例）
                var relic = (_offered != null && i < _offered.Count) ? _offered[i] : null;
                var sp = (relic != null && relicSprites != null) ? relicSprites.Get(relic.Id) : null;
                var iconT = rt.Find("Img_Icon") as RectTransform;
                if (iconT == null)
                {
                    var go = new GameObject("Img_Icon", typeof(RectTransform), typeof(Image));
                    iconT = (RectTransform)go.transform;
                    iconT.SetParent(rt, false);
                    iconT.SetAsFirstSibling();
                }
                iconT.anchorMin = iconT.anchorMax = new Vector2(0.5f, 0.5f);
                iconT.pivot = new Vector2(0.5f, 0.5f);
                iconT.sizeDelta = new Vector2(150f, 150f);
                iconT.anchoredPosition = new Vector2(0f, 132f);
                var iconImg = iconT.GetComponent<Image>();
                iconImg.sprite = sp;
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.enabled = sp != null;

                // 名字 / 描述 / 本队角标
                if (i < _tmpDraftNames.Length && _tmpDraftNames[i] != null)
                {
                    var nrt = (RectTransform)_tmpDraftNames[i].transform;
                    nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
                    nrt.pivot = new Vector2(0.5f, 0.5f);
                    nrt.sizeDelta = new Vector2(230f, 44f);
                    nrt.anchoredPosition = new Vector2(0f, 22f);
                    _tmpDraftNames[i].fontSize = 26;
                    _tmpDraftNames[i].alignment = TextAlignmentOptions.Center;
                }
                if (i < _tmpDraftDescs.Length && _tmpDraftDescs[i] != null)
                {
                    var drt = (RectTransform)_tmpDraftDescs[i].transform;
                    drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
                    drt.pivot = new Vector2(0.5f, 1f);
                    drt.sizeDelta = new Vector2(222f, 250f);
                    drt.anchoredPosition = new Vector2(0f, -8f);
                    _tmpDraftDescs[i].fontSize = 18;
                    _tmpDraftDescs[i].alignment = TextAlignmentOptions.TopLeft;
                }
                if (i < _markTeam.Length && _markTeam[i] != null)
                {
                    var mrt = (RectTransform)_markTeam[i].transform;
                    mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
                    mrt.pivot = new Vector2(0.5f, 0.5f);
                    mrt.sizeDelta = new Vector2(72f, 30f);
                    mrt.anchoredPosition = new Vector2(0f, -196f);
                }

                // 悬浮反馈（用户 2026-10-19）：金框亮起 + 整卡上浮 10px
                {
                    var hoverT = rt.Find("Img_Hover") as RectTransform;
                    if (hoverT == null)
                    {
                        var hg = new GameObject("Img_Hover", typeof(RectTransform), typeof(Image));
                        hoverT = (RectTransform)hg.transform;
                        hoverT.SetParent(rt, false);
                        var himg = hg.GetComponent<Image>();
                        himg.sprite = UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Kit/card_hover");
                        himg.type = Image.Type.Sliced;
                        himg.raycastTarget = false;
                        hoverT.anchorMin = Vector2.zero; hoverT.anchorMax = Vector2.one;
                        hoverT.offsetMin = Vector2.zero; hoverT.offsetMax = Vector2.zero;
                    }
                    hoverT.SetAsLastSibling();
                    hoverT.gameObject.SetActive(false);
                    var hoverHome = rt.anchoredPosition;   // 悬浮位移的固定基准（防多次进出累积漂移）

                    var trg = btn.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                    if (trg == null) trg = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    trg.triggers.Clear();
                    var enter = new UnityEngine.EventSystems.EventTrigger.Entry
                    { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
                    enter.callback.AddListener(_ =>
                    {
                        hoverT.gameObject.SetActive(true);
                        DG.Tweening.DOTween.To(() => rt.anchoredPosition, v => rt.anchoredPosition = v,
                            hoverHome + new Vector2(0f, 10f), 0.12f).SetEase(DG.Tweening.Ease.OutCubic);
                    });
                    var exit = new UnityEngine.EventSystems.EventTrigger.Entry
                    { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
                    exit.callback.AddListener(_ =>
                    {
                        hoverT.gameObject.SetActive(false);
                        DG.Tweening.DOTween.To(() => rt.anchoredPosition, v => rt.anchoredPosition = v,
                            hoverHome, 0.12f).SetEase(DG.Tweening.Ease.OutCubic);
                    });
                    trg.triggers.Add(enter);
                    trg.triggers.Add(exit);
                }

                // 落卡动画（用户 2026-10-19：太快看不清，改成从上往下落 + 回弹）
                //   每张卡从上方 +150 处落下、淡入，0.55s，间隔 0.16s；落地带一点压扁回弹。
                var cg = btn.GetComponent<CanvasGroup>();
                if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
                var home = rt.anchoredPosition;
                cg.alpha = 0f;
                rt.anchoredPosition = home + new Vector2(0f, 150f);
                rt.localScale = new Vector3(0.96f, 1.06f, 1f);   // 落下时略拉长，像纸牌下落
                var seq = DG.Tweening.DOTween.Sequence();
                seq.SetDelay(0.10f + i * 0.16f);
                seq.Append(rt.DOAnchorPos(home, 0.55f).SetEase(DG.Tweening.Ease.OutCubic));
                seq.Join(rt.DOScale(Vector3.one, 0.45f).SetEase(DG.Tweening.Ease.OutCubic));
                seq.Join(cg.DOFade(1f, 0.30f));
                seq.Append(rt.DOScale(new Vector3(1.03f, 0.97f, 1f), 0.07f));
                seq.Append(rt.DOScale(Vector3.one, 0.12f).SetEase(DG.Tweening.Ease.OutBack));
            }
        }

        /// <summary>
        /// ★ 2026-10-19 用户："点击 X 怎么就直接通过了，我说了是暂时保留，返回节点地图查看一下，
        ///   还可以再次打开，而且是一样的"。
        /// ⇒ X 关闭前清掉待推进：节点不算通过；种子按（RunSeed, Act, Node）确定 ⇒ 再开同一批。
        /// </summary>
        protected override void OnChromeClose()
        {
            WanXiang.Modules.UI.CampaignPanel.PendingCommit = -1;
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
      // ⚠ 有指定返回目标（秘境遗物节点）就回秘境，否则回主线路线图。
    //   这里**不**清静态字段 —— 清的动作在调用方的 finally 里（它比这里晚执行）。
      var after = _afterPick;
      if (after != null) { after(); return; }
   OpenPanelAsync<CampaignPanel>().Forget();
        }
    }
}
