// ============================================================================
//  Panel_Realm —— 秘境外壳（2026-10-16《精英首领与秘境设计 v1.0》）
//  ---------------------------------------------------------------------------
//  两种用法（同一个面板，靠payload 区分）：
//    ① **入口**（payload 为空 &未进场）：问号节点揭晓为"秘境"时弹出来，
//       左侧把场域的**场地效果 / 收益 / 代价**三行摊开给玩家看 ——
//       ⛔ 必须让玩家进之前就看清，不允许"进去了才知道有什么"（设计文档 5.3）。
//       玩家可「进入秘境」/「暂不进入（+1 灵卵）」/「换一个（免费 1 次）」。
//    ② **秘境中**（run.InRealm为真）：右侧画小地图，格子点击推进；
//       底部多一个「撤离秘境」。
//
//  ★ 面板本身**不改任何战斗数据**：进/出秘境一律走
//   <see cref="WanXiang.Modules.UI.RealmSession"/>（快照 / 回滚都在那里）。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Campaign;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_Realm", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
             FullScreen = false, CloseOnMaskClick = false)]
    public sealed class RealmPanel : UIPanelBase
    {
        [Header("标题 / 状态")]
        [SerializeField] private TMP_Text _tmpTitle;         // Tmp_Title
        [SerializeField] private TMP_Text _tmpStatus;        // Tmp_Status

        [Header("场域三行（入口时显示）")]
        [SerializeField] private RectTransform _infoRoot;    // Root_RealmInfo
        [SerializeField] private TMP_Text _tmpRealmName;     // Tmp_RealmName
        [SerializeField] private TMP_Text _tmpHazard;        // Tmp_Hazard
        [SerializeField] private TMP_Text _tmpBoon;          // Tmp_Boon
        [SerializeField] private TMP_Text _tmpCost;          // Tmp_Cost

        [Header("小地图（秘境中显示）")]
        [SerializeField] private RectTransform _mapRoot;    // Root_Map（入口时整块隐藏）
        [SerializeField] private RectTransform _gridContent; // Grid_Nodes
        [SerializeField] private GameObject _nodeTemplate;    // Item_NodeTemplate（prefab 内隐藏）
        [SerializeField] private TMP_Text _tmpNodeIconTemplate;  // Tmp_NodeIcon
        [SerializeField] private TMP_Text _tmpNodeNameTemplate;  // Tmp_NodeName
        [SerializeField] private Image _imgClearedTemplate;      // Img_Cleared

        [Header("按钮")]
        [SerializeField] private Button _btnEnter;          // Btn_Enter
        [SerializeField] private Button _btnLeave;          // Btn_Leave
        [SerializeField] private Button _btnReroll;         // Btn_Reroll
        [SerializeField] private Button _btnQuit;           // Btn_Quit（秘境内才显示）

        // ---- 节点尺寸（与 CampaignPanel 的 NodeW/NodeH/Gap 同量级）----
        private const float NodeW = 96f;
        private const float NodeH = 72f;
        private const float GapX = 26f;
        private const float GapY = 40f;
        private static readonly Color UIBuildInk = new Color(0.94f, 0.92f, 0.86f, 1f);

        private RealmDef _realm;
        private RealmMap _map;
        private int _rerollUsed;
        private int _curNode = -1;                          // 当前所在节点（-1 = 未进场）
        private readonly HashSet<int> _cleared = new HashSet<int>();
        private readonly List<GameObject> _nodeViews = new List<GameObject>();

        // =====================================================================
        //  生命周期
        // =====================================================================
        protected override void OnCreate()
        {
            if (_btnEnter != null) _btnEnter.onClick.AddListener(OnEnterClicked);
            if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
            if (_btnReroll != null) _btnReroll.onClick.AddListener(OnRerollClicked);
            if (_btnQuit != null) _btnQuit.onClick.AddListener(OnQuitClicked);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            RealmHookRegistrar.EnsureRegistered();   // 幂等：秘境钩子挂进战斗核心

            var run = WanXiang.Run.RunSave.Current;
            bool inRealm = run != null && run.InRealm;

            if (inRealm)
            {
                // ---- 秘境内：画小地图 ----
                _realm = RealmCatalog.For(run.RealmId);
                if (_tmpTitle != null) _tmpTitle.text = "秘境 · " + (_realm != null ? _realm.Name : "");
                if (_infoRoot != null) _infoRoot.gameObject.SetActive(true);
                FillRealmInfo(_realm);
                if (_mapRoot != null) _mapRoot.gameObject.SetActive(true);
                if (_btnEnter != null) _btnEnter.gameObject.SetActive(false);
                if (_btnLeave != null) _btnLeave.gameObject.SetActive(false);
                if (_btnReroll != null) _btnReroll.gameObject.SetActive(false);
                if (_btnQuit != null) _btnQuit.gameObject.SetActive(true);
                _curNode = -1;         // 玩家还没在图上走过
                BuildMap();
            }
            else
            {
                // ---- 入口：先抽一个场域 ----
                _rerollUsed = 0;
                _realm = RollRealm();
                if (_tmpTitle != null) _tmpTitle.text = "秘境 · 抉择";
                if (_infoRoot != null) _infoRoot.gameObject.SetActive(true);
                FillRealmInfo(_realm);
                if (_btnEnter != null) _btnEnter.gameObject.SetActive(true);
                if (_btnLeave != null) _btnLeave.gameObject.SetActive(true);
                if (_btnReroll != null) _btnReroll.gameObject.SetActive(true);
                if (_btnQuit != null) _btnQuit.gameObject.SetActive(false);
                if (_mapRoot != null) _mapRoot.gameObject.SetActive(false);
                if (_gridContent != null) _gridContent.gameObject.SetActive(false);
            }
            RefreshStatus();
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            //⛔ 面板关闭**不等于**退出秘境（玩家可能只是打开看看）——
            //   离开面板时保持run.InRealm 原样，撤离必须点「撤离秘境」。
            ClearNodes();
        }

        // =====================================================================
        //  入口流程
        // =====================================================================
        private RealmDef RollRealm()
        {
            var run = WanXiang.Run.RunSave.Current;
            //⛔ 种子必须用共享的那个（RealmSession.RealmSeedFor）——
            //   揭晓处与面板处各写一份公式会出现"揭晓说苍泽、面板抽出烬岭"的错位。
            return RealmSession.RollFor(run, RealmSession.RealmSeedFor(run));
        }

        private void OnRerollClicked()
        {
            // 换算是稀缺资源：免费 1 次（设计文档 7.1），之后要拿灵卵
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (_rerollUsed >= 1)
            {
                if (run.Eggs < 1)
                {
                    SetStatus("灵卵不够换一个了。");
                    return;
                }
                run.Eggs -= 1;
            }
            _rerollUsed++;
            WanXiang.Run.RunSave.SaveCurrent();
            _realm = RollRealm();
            FillRealmInfo(_realm);
            RefreshStatus();
        }

        private void OnEnterClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _realm == null) return;
            if (!RealmSession.Enter(run, _realm))
            {
                SetStatus("进不去秘境（可能本局次数已用完）。");
                return;
            }
            // ⛔ 揭晓问号：让路线图节点显示为已探索，且**不**算"通过"——
            //   秘境的收益走星币/灵魂，不该同时吃节点通过奖励。
            CampaignPanel.PendingCommit = -1;
            WanXiang.Run.RunSave.SaveCurrent();

            _map = RealmMapBuilder.Build(_realm, RealmSession.RealmSeedFor(run));
            _curNode = -1;
            _cleared.Clear();
            OnOpenAsync(null);              // 重画成"秘境内"外观
        }

        private void OnLeaveClicked()
        {
            // 暂不进入：给 1 灵卵当"信息补偿"（不是白嫖 —— 玩家知道了场域情报）
            var run = WanXiang.Run.RunSave.Current;
            if (run != null)
            {
                run.Eggs += 1;
                WanXiang.Run.RunSave.SaveCurrent();
            }
            BackToMap();
        }

        private void OnQuitClicked()
        {
            //★ 撤离 = 硬约束 ②：回滚场域状态 + 还原路线图快照，回到原节点。
            var run = WanXiang.Run.RunSave.Current;
            RealmSession.Exit(run);
            if (run != null) WanXiang.Run.RunSave.SaveCurrent();
            ClearNodes();
            BackToMap();
        }

        private void BackToMap()
        {
            CampaignPanel.PendingCommit = -1;
            CloseSelf();
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) UniTask.Void(async () =>
            {
                await UniTask.DelayFrame(20);
                await ui.OpenAsync<CampaignPanel>();
            });
        }

        // =====================================================================
        //  秘境内小地图
        // =====================================================================
        /// <summary>
        /// 按**层**排布秘境内部图——与 CampaignPanel 的路线图**完全同一套做法与数学**
        /// （y = -(层数-1-layer)*(H+Gap)，层内按列序水平铺开）。
        /// ⛔ 不用GridLayoutGroup：第一版用网格，结果观感和主线完全不像，
        ///   玩家会以为进入了另一个游戏（用户 2026-10-16 明确要求"套用现在的节点地图设计"）。
        /// </summary>
        private void BuildMap()
        {
            if (_gridContent != null) _gridContent.gameObject.SetActive(true);
            ClearNodes();
            var run = WanXiang.Run.RunSave.Current;
            if (_map == null && run != null)
                _map = RealmMapBuilder.Build(_realm, RealmSession.RealmSeedFor(run));
            if (_map == null || _map.Graph == null || _nodeTemplate == null) return;

            // 秘境内部不用自动布局（节点要手摆）
            var glg = _gridContent.GetComponent<GridLayoutGroup>();
            if (glg != null) glg.enabled = false;

            int layers = _map.LayerCount;
            for (int layer = 0; layer < layers; layer++)
            {
                var row = _map.Graph.Layers[layer];
                float y = -(layers - 1 - layer) * (NodeH + GapY);   // 层号越大越靠上
                for (int k = 0; k < row.Length; k++)
                {
                    int off = row[k];
                    var go = Instantiate(_nodeTemplate, _gridContent);
                    go.SetActive(true);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.sizeDelta = new Vector2(NodeW, NodeH);
                    float x = row.Length <= 1 ? 0f : (k - (row.Length - 1) * 0.5f) * (NodeW + GapX);
                    rt.anchoredPosition = new Vector2(x, y);

                    PaintNode(go, off, _cleared.Contains(off), off == _curNode);
                    var btn = go.GetComponent<Button>();
                    if (btn == null) btn = go.AddComponent<Button>();
                    var idx = off;
                    btn.onClick.AddListener(() => OnNodeClicked(idx));
                    _nodeViews.Add(go);
                }
            }
            // content 高度要够，否则底部几层会被挤出可视区
            var crt = _gridContent;
            crt.sizeDelta = new Vector2(crt.sizeDelta.x,
                layers * (NodeH + GapY) + 40f);
        }

        /// <summary>给一格上色/写字（首领与已清高亮，锁定压暗）。</summary>
        private void PaintNode(GameObject go, int offset, bool cleared, bool isHere)
        {
            var kind = _map.KindOf(offset);
            bool boss = _map.IsBossOffset(offset);
            var icon = go.transform.Find("Tmp_NodeIcon");
            var nameTx = go.transform.Find("Tmp_NodeName");
            var mark = go.transform.Find("Img_Cleared");
            if (icon != null) icon.GetComponent<TMP_Text>().text = boss ? "首" : IconOf(kind);
            if (nameTx != null) nameTx.GetComponent<TMP_Text>().text = boss ? "首领" : NameOf(kind);
            if (mark != null) mark.gameObject.SetActive(cleared);

            var card = go.GetComponent<Image>();
            if (card != null)
            {
                if (boss) card.color = new Color(0.86f, 0.72f, 0.44f, 1f);
                else if (isHere) card.color = new Color(0.93f, 0.90f, 0.80f, 1f);
                else if (cleared) card.color = new Color(0.90f, 0.92f, 0.88f, 1f);
                else card.color = UIBuildInk;
            }
        }

        private void OnNodeClicked(int index)
        {
            if (_map == null) return;
            if (_map == null) return;
            // 可达性 = 与当前节点**有边**相连（与主线 HasEdge 同一条规则）
            if (_curNode >= 0 && !_map.CanMove(_curNode, index))
            {
                SetStatus("走不到那里——得从相邻的格子过去。");
                return;
            }
            var kind = _map.KindOf(index);
            _curNode = index;
            if (_map.IsBattleOffset(index))
            {
                // 战斗节点：走一场真战斗（复用同一个BattleRequest 通道，只是倍率不同）
                RealmSession.AddStarCoin(WanXiang.Run.RunSave.Current, 6);
                _cleared.Add(index);
                RefreshNodes();
                RefreshStatus();
                var run = WanXiang.Run.RunSave.Current;
                if (run != null) WanXiang.Run.RunSave.SaveCurrent();
                StartRealmBattle(kind, _map.IsBossOffset(index));
            }
            else
            {
                // 非战斗节点：即时结算（奖励按深度给，见设计文档 8.1）
                GrantCalm(kind);
                _cleared.Add(index);
                RefreshNodes();
                RefreshStatus();
            }
        }

        /// <summary>
        /// 秘境内战斗：走**与主线完全相同**的通道（SceneFlow.PendingBattle → 战斗场景）——
        /// ⛔ 不另开一条路：场域的强度倍率与场地效果已经由
        ///   <see cref="RealmSession"/>（EnemyScaleFor / ExternalHooks）注入到同一条链上，
        ///   另开通道就会出现"秘境难度没生效"这类分叉 bug。
        /// </summary>
        private void StartRealmBattle(WanXiang.Campaign.NodeKind kind, bool isBossCell)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;

            var req = default(BattleRequest);
            //⚠ NodeKind 里**没有** Boss —— 守关是"节点类型 + isBoss 标记"两件事，
            //   别自己造一个枚举值（曾经造过，编译直接报错）。
            //⚠ NodeKind 里**没有** Boss —— 守关是"节点类型 + isBoss 标记"两件事。
            var nodeKind = kind == WanXiang.Campaign.NodeKind.Elite
                ? WanXiang.Campaign.NodeKind.Elite
                : WanXiang.Campaign.NodeKind.Encounter;
            bool ok = BattleRequestFactory.TryBuildFromRun(
                null, run, _realm != null ? _realm.Name + "·场地" : "", out req,
                nodeKind, -1, isBossCell);
            if (!ok || req == null)
            {
                SetStatus("这一场组不起来（内容目录没准备好）。");
                return;
            }
            // 标题带上场域名，玩家一眼知道自己在哪
            req.Title = (_realm != null ? _realm.Name : "秘境") + " · " + (isBossCell ? "首领裂隙" : NodeKinds.Cn(kind));

            // ⛔ 走与主线出征**完全同一条** SceneFlow.EnterBattle（自带 UiTransition 转场）。
            //   秘境强度倍率与场地效果已经挂在同一链上（RealmSession / ExternalHooks），
            //   另开通道就会出现"秘境难度没生效"这类分叉 bug。
            SceneFlow.LastBattleKind = nodeKind;
            SceneFlow.EnterBattle(req);
        }

        /// <summary>非战斗节点的小额收益（按波数深度给，别一口气掏空）。</summary>
        private void GrantCalm(WanXiang.Campaign.NodeKind kind)
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            switch (kind)
            {
                case WanXiang.Campaign.NodeKind.Shop:   // 窖藏：直接拿星币
                    RealmSession.AddStarCoin(run, 4);
                    break;
                case WanXiang.Campaign.NodeKind.Nest:   // 灵泉：回 35% 生命
                    run.HealPending += 35;
                    break;
                case WanXiang.Campaign.NodeKind.Forge:  // 锻炉：给灵卵当"重铸费折现"
                    run.Eggs += 2;
                    break;
            }
            WanXiang.Run.RunSave.SaveCurrent();
        }

        /// <summary>刷新所有格子的外观（已清 / 当前 / 首领高亮）。</summary>
        private void RefreshNodes()
        {
            if (_map == null || _map.Graph == null) return;
            for (int i = 0; i < _nodeViews.Count; i++)
            {
                if (_nodeViews[i] == null) continue;
                // _nodeViews 是按 (层, 列) 顺序建的，与 offset 顺序一致（ActGraph 按层顺序编号）
                int off = i < _map.NodeCount ? i : -1;
                if (off < 0) continue;
                PaintNode(_nodeViews[i], off, _cleared.Contains(off), off == _curNode);
            }
        }

        private void ClearNodes()
        {
            for (int i = 0; i < _nodeViews.Count; i++)
                if (_nodeViews[i] != null) Destroy(_nodeViews[i]);
            _nodeViews.Clear();
        }

        // =====================================================================
        //  文案
        // =====================================================================
        private void FillRealmInfo(RealmDef r)
        {
            if (r == null)
            {
                if (_tmpRealmName != null) _tmpRealmName.text = "（暂无可用秘境）";
                if (_tmpHazard != null) _tmpHazard.text = "";
                if (_tmpBoon != null) _tmpBoon.text = "";
                if (_tmpCost != null) _tmpCost.text = "";
                return;
            }
            if (_tmpRealmName != null) _tmpRealmName.text = r.Name + "　" + r.Concept;
            if (_tmpHazard != null) _tmpHazard.text = "场地：" + HazardText(r);
            if (_tmpBoon != null) _tmpBoon.text = "收益：" + r.Boon;
            if (_tmpCost != null) _tmpCost.text = "代价：" + r.Cost;
        }

        private static string HazardText(RealmDef r)
        {
            switch (r.Hazard)
            {
                case RealmHazardKind.FoeStatusAll:
                    return "每回合敌方全体「" + WanXiang.Battle.Core.StatusCatalog.Get(r.HazardStatusId).Name
                         + "」" + r.HazardStacks + " 层";
                case RealmHazardKind.FoeShieldAll:
                    return "敌方全体获得" + (int)(r.HazardShieldPct * 100f) + "% 最大生命护盾";
                case RealmHazardKind.SelfShieldAll:
                    return "**禁用格挡 QTE**，改为每回合自动蓄盾"
                         + (int)(r.HazardShieldPct * 100f) + "%";
                default:
                    return "无（这里只考验你的牌面）";
            }
        }

        private void RefreshStatus()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            if (_tmpStatus == null) return;
            if (run.InRealm && _realm != null)
                _tmpStatus.text = "第 " + run.RealmWave + " / " + _realm.Waves + " 波　·　星币 " + run.RealmStarCoin;
            else
                _tmpStatus.text = "本局还可进入秘境 " + RealmSession.EntriesLeft(run) + " 次";
        }

        private void SetStatus(string msg)
        {
            if (_tmpStatus != null) _tmpStatus.text = msg;
        }

        private static string IconOf(WanXiang.Campaign.NodeKind k)
        {
            switch (k)
            {
                case WanXiang.Campaign.NodeKind.Encounter: return "战";
                case WanXiang.Campaign.NodeKind.Elite: return "精";
                case WanXiang.Campaign.NodeKind.Forge: return "炉";
                case WanXiang.Campaign.NodeKind.Nest: return "泉";
                case WanXiang.Campaign.NodeKind.Shop: return "窖";
                case WanXiang.Campaign.NodeKind.Tale: return "闻";
                case WanXiang.Campaign.NodeKind.Omen: return "象";
                default: return "？";
            }
        }

        private static string NameOf(WanXiang.Campaign.NodeKind k)
        {
            switch (k)
            {
                case WanXiang.Campaign.NodeKind.Encounter: return "遭遇";
                case WanXiang.Campaign.NodeKind.Elite: return "精英";
                case WanXiang.Campaign.NodeKind.Forge: return "锻炉";
                case WanXiang.Campaign.NodeKind.Nest: return "灵泉";
                case WanXiang.Campaign.NodeKind.Shop: return "窖藏";
                default: return WanXiang.Campaign.NodeKinds.Cn(k);
            }
        }
    }
}