// ============================================================================
//  Panel_Campaign —— 节点地图（五幕节气）
//  接的是 Modules/Campaign/Core 的既有战役 Core：
//    · SolarTermGraph.BuildDefault() 给出五幕图（每幕 6 个节气节点、4 层拓扑）
//    · 节点类型七种（遭遇/精英/灵市/孵穴/异闻/铸魂台/天象），非战斗节点直接
//      路由到已有面板（灵市→Panel_Market、异闻→Panel_Tale、铸魂台→Panel_Forge、
//      天象→Panel_Omen），战斗节点→Panel_Formation
//    · 可达性用 ActGraph.CanMove（只能走相邻下一层）
//  进度存 RunSave（Act / NodeOffset），与存档系统共用一条线。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Battle.Core;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.UI;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    /// <summary>从一个节点进入布阵时携带的上下文。</summary>
    public sealed class NodeRequest
    {
        public string Title = "小暑 · 温风至";
        public string Weather = "本场精英位：每回合全场 3% 灼烧";
        public ulong Seed = 20260914UL;
        public int NodeIndex = 7;

        /// <summary>所属幕（1..5）与节点下标（幕内 0..5）。</summary>
        public int Act = 1;
        public int Offset = -1;
        public string GoalId = "";   // ★ 战斗目标（2026-11-19）：按 seed/act/offset 确定性分配

        /// <summary>秘境层号（1 起）。&gt;0 表示这是秘境节点 —— 战斗标题用它，别再拿幕内下标当层号。</summary>
        public int RealmLayer = -1;

        /// <summary>节点类型（决定要不要打仗、带不带劫象）。</summary>
        public WanXiang.Campaign.NodeKind Kind = WanXiang.Campaign.NodeKind.Encounter;

        /// <summary>
        /// 是否**守关**节点（幕末最后一格）。图里这一格复用 <see cref="WanXiang.Campaign.NodeKind.Elite"/>
        /// 作类型占位，光看 Kind 分不出「普通精英」与「守关」—— 所以由 CampaignPanel 显式置位。
        /// 战斗工厂据此调 SeededEnemyProvider.BossSquadFor（Boss + 随从），而不是普通精英抽签。
        /// </summary>
        public bool IsBoss;

        /// <summary>
        /// 是否**终局战**节点（第 5 幕 / 天阙的最后一格）。
        /// ★ 用户 2026-10-03 实测：点这一格出征，打出来的是**普通精英战**（诸怀/旱魃/毕方），
        ///   不是归墟之主/鸿蒙。根因是 `IsBoss` 只覆盖幕 1~4，第 5 幕终局格 IsBoss=false
        ///   ， FormationPanel 按普通遭遇建敌。终局战的构建方式（Boss + 我方镜像×3）与守关
        ///   完全不同，所以**单独立一个标志**，由 FormationPanel.OnDeployClicked 分派到
        ///   TrialPanel.BuildFinaleBattle，而不是靠 IsBoss 兼管。
        /// </summary>
        public bool IsFinale;
    }

    [UIPanel("Panel_Campaign", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    // ↑ 全屏面板不该"点空白就关"：它铺满屏幕，没有"面板外"可言，
    //   否则玩家点任何空白处都会把界面关掉（踩过）。
    public sealed partial class CampaignPanel : UIPanelBase
    {
        /// <summary>节点地图不允许返回键关闭 —— 关掉后 Home 不会自动重开，屏幕就是空白（用户实测）。
        ///  回主城走面板上的返回按钮。</summary>
        public override bool AllowBackClose => false;

        /// <summary>返回主城：节点图是全屏面板，只 Close 不 Open Home 会留白屏（用户实测）。</summary>
private void OnBackToHome()
      {
         // ★ 秘境模式下，这个按钮是「撤离秘境」而不是「回主界面」。
        //   ⚠⚠ **绝不能靠"关掉再打开"来回切图**：UISystem 遇到"正在 Closing 的实例"会
    //     **直接忽略**这次 OpenAsync（实测：画面一片空白，日志里没报错）。
        //   正解：全程用**同一个实例**——撤离后原地重建成主线图。
            if (_realmMode)
            {
  var quit = OnRealmQuitRequested;
          if (quit != null) quit();       // 负责回滚数据（RealmSession.Exit）
         // 回滚后原地重建主线图：清秘境标记 + 重新 BuildRoute
       _realmMode = false;
         _realmGraphPayload = null;
  _injectedGraph = null;
   _currentOffset = -1;
            _visited.Clear();
       _passed.Clear();
BuildNodeMap();
        return;
 }
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            // ★ 2026-10-20 用户："从节点地图回到主界面也一样要有个转场"（双帘拨开、不写字）
            int actOut = WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : 1;
            UiTransition.Play(UiTransition.Kind.Split, actOut, () =>
            {
                CloseSelf();
                if (ui != null) _ = ui.OpenAsync<HomePanel>();
            }, null, 2.0f, "Map2Home");
        }

        [SerializeField] private TMP_Text _tmpActTitle;        // Tmp_ActTitle  幕名
        [SerializeField] private TMP_Text _tmpJie;             // Tmp_JieCount  劫数

        /// <summary>待推进节点（跨面板共享）：出征时保留、返回地图时撤销。</summary>
        /// <summary>
        /// 换存档 / 开新局时置 true：本面板是 Cached 复用实例，`_currentOffset/_visited/_passed/_graph`
        /// 会带着上一局的残留（用户 2026-10-24："换档重新开始，有些节点已通过、异兽遗物还在"）。
        /// ⛔ 只重置存档数据（RunState.ResetForNewRun）不够，视图必须一起清。
        /// </summary>
        public static bool NeedsViewReset;

        public static int PendingCommit = -1;      // 事件类节点：完成后才推进（未完成就关游戏 ， 节点不通过、奖励不丢）
        [SerializeField] private ScrollRect _scrollNodes;      // Scroll_Nodes  节点长卷
        [SerializeField] private RectTransform _nodeItemTemplate;  // Item_Node（模板，默认隐藏）
        [SerializeField] private GameObject _rootNodeInfo;     // Root_NodeInfo 右侧信息卡
        [SerializeField] private TMP_Text _tmpNodeName;        // Tmp_NodeName
        [SerializeField] private TMP_Text _tmpNodeType;        // Tmp_NodeType
        [SerializeField] private TMP_Text _tmpWeather;         // Tmp_Weather
        [SerializeField] private Image _imgNodeIcon;           // Img_NodeIcon
        [SerializeField] private GameObject _targetIconTemplate;  // Item_Node/Img_TargetIcon（目标图标模板，2026-11-19）
        [SerializeField] private TMP_Text _tmpNodeGoal;           // Root_NodeInfo/Tmp_NodeGoal（节点目标说明）
        [BindArray("Img_Avatar_{0}", 5)]
        [SerializeField] private Image[] _imgAvatars;          // 队伍预览 5 个头像
        [SerializeField] private Button _btnNext;              // Btn_Next
        [SerializeField] private Button _btnBack;              // Btn_Back
        [SerializeField] private Button _btnXingyi;            // Btn_Xingyi  星移（注入余气）

        // ---- 按幕背景 + 飘落粒子（2026-10-11 用户要求，见 CampaignAmbientFx）----
        [SerializeField] private Image _imgMapBg;            // Img_MapBg     按幕背景（铺满）
        [SerializeField] private RectTransform _rootMapParticles;   // Root_MapParticles 粒子层（铺满）
        private CampaignAmbientFx _ambientFx;               // 运行时挂在面板上（自己驱动 Update）

        // 遗物 HUD（左上角一排小牌 + 悬浮说明；见 RelicHud / Editor/UITool/BuildRelicHudPatch.cs）
        [SerializeField] private RectTransform _rootRelics;    // Root_Relics
        [SerializeField] private GameObject _relicTemplate;    // Item_Relic
        [SerializeField] private GameObject _relicTipRoot;     // Root_RelicTip
        [SerializeField] private TMP_Text _relicTipText;       // Tmp_RelicTip

        /// <summary>星移余气持续节点数（Batch 3 星移）。每次星移把当前天时减半带入后续 N 节。</summary>
        private const int XINGYI_NODES = 3;
        /// <summary>星移消耗的灵卵数（2026-09-27 定案：不再免费，防无限制白嫖；一处调参）。</summary>
        private const int XINGYI_COST = 3;

        [Header("数据引用（由生成器自动绑定）")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        private readonly NodeRequest _current = new NodeRequest();
        /// <summary>R5-b：实例级节点路由表（简单节点注册在这里，可随时增删）。</summary>
        private readonly NodeRouter _nodeRouter = new NodeRouter();

        protected override void OnCreate()
        {
            EnsureInkColors();   // ★ 节点文字色按当前色彩空间校正（Linear 下必须）
            if (_rootNodeInfo != null) _rootNodeInfo.SetActive(true);
            if (_btnNext != null) _btnNext.onClick.AddListener(OnNextClicked);
            if (_btnBack != null) _btnBack.onClick.AddListener(OnBackToHome);
            // 星移：把当前节点天时减半、注入为余气带入后续节点（仅在有时天时可见）
            if (_btnXingyi != null)
            {
                _btnXingyi.onClick.AddListener(OnXingyiClicked);
                _btnXingyi.gameObject.SetActive(false);   // 默认隐藏，选中有天时的节点才显示
            }

            // ★ 秘境「离开」按钮（prefab 里 Btn_RealmQuit）：
            //   与「返回主界面」**分开**——撤离要回滚场域 + 还原原节点，两者语义不同。
            //   只在内秘境内显示（OnRefresh/BuildNodeMap 里按 _realmMode 开关）。
            var quitBtn = transform.Find("Btn_RealmQuit");
            if (quitBtn != null)
            {
                var bq = quitBtn.GetComponent<UnityEngine.UI.Button>();
                if (bq != null) { bq.onClick.RemoveAllListeners(); bq.onClick.AddListener(OnRealmQuitClicked); }
                quitBtn.gameObject.SetActive(false);   // 默认隐藏，进秘境才亮
            }
            else Debug.LogWarning("[Campaign] 缺 Btn_RealmQuit（秘境离开按钮）");

            // ★ 瘴雾调试按钮（prefab 里 Btn_DebugFog）：点一下触发 3 节瘴雾，方便实机看效果。
            //   等正式触发口（天象/事件/守关失败）接上后可以删掉这个按钮。
            var dbg = transform.Find("Btn_DebugFog");
            if (dbg != null)
            {
                var b = dbg.GetComponent<UnityEngine.UI.Button>();
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(ArmFogOnNextLayer); }
            }
                     else Debug.LogWarning("[Campaign] 缺 Btn_DebugFog（瘴雾调试按钮）");

            // ★ 秘境测试按钮（prefab 里 Btn_DebugRealm）：一键进秘境，方便实机看效果。
      //   正常游玩靠路线图上的【秘境】节点进入（第 2 幕起随机出现）；
            //   这个按钮是**调试入口**：不用刷到秘境节点就能直接进去看。
      //   ⛔ 撤离时 RealmSession.Exit 会把 NodeOffset/Act 相关字段按进入前的快照还原
    //   ⇒ 无论在哪个幕哪个格点进去，撤离都回原处（硬约束 ②）。
      var dbgRealm = transform.Find("Btn_DebugRealm");
    if (dbgRealm != null)
         {
       var b = dbgRealm.GetComponent<UnityEngine.UI.Button>();
      if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(DebugEnterRealm); }
         }
            else Debug.LogWarning("[Campaign] 缺 Btn_DebugRealm（秘境测试按钮）");
        }

        /// <summary>
      /// 调试：直接进秘境（等价于点路线图上的秘境节点，但**不消耗也不受"幕数/次数"限制**）。
        /// ⛔ 不改 Act / 不改 NodeOffset —— 位置由 <see cref="RealmSession.Enter"/> 存快照、
        ///   撤离时还原，所以点一百次也不会把主线带偏。
        /// </summary>
        private void DebugEnterRealm()
        {
            if (_realmMode)
            {
                Debug.LogWarning("[Campaign] 已在秘境中，忽略秘境测试按钮");
                return;
            }

            // ★★ 2026-10-17 用户反馈："之前我怎么看你说是有一个面板可以刷新秘境的，
            //   现在点击测试按钮直接就进去了" —— 说得对，调试入口不该绕过**抉择面板**。
            //   正式入口是「秘境节点 → RealmPanel（看场域/刷新/进入）→ 转场 → 秘境图」，
            //   调试入口必须走同一条路，否则玩家看不到"这次抽到哪个场域、能不能换"。
            //   ⛔ 只置旁路标记（放宽"第1幕/次数"两条判定），**一个 run 字段都不改** ——
            //     早先那版是临时抬高 run.Act 再还原，任何一条撤离路径漏了还原就会永久改坏幕数。
            WanXiang.Modules.UI.RealmPanel.DebugBypass = true;
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui == null) { Debug.LogError("[Campaign] UI 未就绪，无法打开秘境抉择"); return; }
            ui.OpenAsync<WanXiang.Modules.UI.RealmPanel>(null).Forget();
        }

        /// <summary>由 <see cref="HomePanel"/> 在“出征”开图前置位 ， 让开场动画每次都播（用户要求）。</summary>
        public static bool RequestIntroFromMenu;

        /// <summary>
        /// 本进程内是否**真开过**（彩排不算；2026-10-14 首开优化用）。
        /// HomePanel 出征时据此判断要不要在转场遮盖期先"环境级"真开一遍（见 GhostOpenCloseAsync 注释）。
        /// </summary>
        public static bool SessionOpened;

        /// <summary>
        /// **PendingCommit 落地**（事件节点"通过"落账）：地图常开流程下，回图走
        /// OnRefresh / OnResume（不重放 OnOpenAsync）⇒ 抽成方法供三条路径共用。
        /// </summary>
        /// <returns>true = 吃掉了本帧后续（进终局战/天阙），调用方应立即停止。</returns>
        /// <summary>
        /// 把存档里"战斗胜利待落地"的节点号补回静态 PendingCommit（2026-10-20）。
        /// 场景：胜利后没点结果面板就退游戏 → 下次开图在此恢复，走与正常完全相同的落地流程。
        /// </summary>
        // 补选奖励只在"这次启动"提醒一次（玩家可以 X 掉，不服气下次还会提醒就太烦）
        private static string _rewardPromptedKey;

        /// <summary>胜利奖励未领 → 拉起 RelicPanel 让玩家补选（同一批遗物，种子已落盘）。</summary>

        private static void RestorePendingBattlePass()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || run.PendingBattlePass < 0) return;
            if (PendingCommit < 0) PendingCommit = run.PendingBattlePass;
            run.PendingBattlePass = -1;
            WanXiang.Run.RunSave.SaveCurrent();
            Debug.Log("[Campaign] 恢复战场待落地通过：节点 " + PendingCommit);
        }


        /// <summary>奖励待确认闸门：ExitBattle 置位，ResultPanel.OnClose 清除。不依赖面板实例，避免栈重算时序问题。</summary>
        internal static bool RewardAwaitingConfirm;

        /// <summary>本次待落地节点是不是本幕最后一格（守关）。用真图判断，不硬编码格数。</summary>

protected override UniTask OnOpenAsync(object payload)
     {
            // ★ 2026-11-02：地图之前先装表（map.csv / node_weights.csv 决定层数与节点权重）。
            //   EnsureInstalled 是幂等的，BattleRequestFactory 那边也会调一次。
            DataTables.EnsureInstalled();

            // ★★ 换档/新局：先清上一局的视图残留，再走正常构建
            if (NeedsViewReset)
            {
                NeedsViewReset = false;
                _currentOffset = -1;
                _visited.Clear();
                _passed.Clear();
                _fogWeather.Clear();
                _graph = null;
                _injectedGraph = null;
                _realmGraphPayload = null;
                _realmMode = false;
                _justPassedNode = -1;
                PendingCommit = -1;
                Debug.Log("[Campaign] 换档/新局：视图状态已重置");
            }

            // ★★ 秘境 = 特殊的一幕：payload 是 ActGraph 就走秘境模式。
            //   本面板**只有这一个分叉点**，其余（排布/连线/选中/出征/落印）全部是主线同一套代码。
            _realmGraphPayload = payload as WanXiang.Campaign.ActGraph;
            if (_realmGraphPayload == null && _injectedGraph != null) _realmGraphPayload = _injectedGraph;

            // ⚠ 提前把 _realmMode 定下来：下面的 RefreshAmbient 要按它选"场域背景"还是"当幕背景"。
            //   （BuildNodeMap 里还会再判一次并真正接管图 —— 两处判据都指向同一个载荷，不会分叉。）
            if (_realmGraphPayload != null && !_realmGraphPayload.IsEmpty) _realmMode = true;

            // 跨存档恢复星移余气：把持久化的 XingyiLingers 重建为会话级 LiveWeather.Lingers，
    //   保证「用了星移→关游戏→重进」星移天气仍在（之前天气随会话丢失，等于白花灵卵）。
         RehydrateXingyi();

            // ★ 真开过标记（彩排不算）：HomePanel 首进图前据此决定要不要先"环境级"彩排（2026-10-14）
            if (!UIPanelBase.GhostSilent) SessionOpened = true;

            _justPassedNode = -1;   // ★ 每次开面重置：只有下面 PendingCommit 真落地了才会重新置位

            // ---- 按幕背景 + 飘落粒子（2026-10-11）----
            //   放在 ActIntro 之前：换幕时 OnOpenAsync 会重跑，这里跟着重建，背景/粒子随幕变。
            RefreshAmbient();

            // ★ 2026-11-14：当前段落规则横幅（明日方舟肉鸽"层修饰"的轻量版）
            if (!_realmMode && _graph != null)
            {
                var frRun = WanXiang.Run.RunSave.Current;
                int frLayer = _currentOffset >= 0 ? _graph.LayerOf(_currentOffset) : 0;
                var fr = WanXiang.Campaign.FloorRules.RuleFor(frRun != null ? frRun.Act : 1, frLayer);
                if (fr != null && frRun != null)
                {
                    frRun.ActiveFloorRule = fr.RuleId;
                    if (!string.IsNullOrEmpty(fr.RuleText))
                    {
                        var fsp = WanXiang.Campaign.FloorRules.SpecialFor(frRun.Act, frLayer);
                        string prefix = (fsp != null && !string.IsNullOrEmpty(fsp.Label))
                            ? fsp.Label + " · " : "";
                        ShowFogBanner(prefix + fr.RuleTitle + "：" + fr.RuleText);
                    }
                }
            }

            // ★ 每一幕的开场/转场动画（宣纸底 + 大字逐字浮现 + 当季落叶）。
            //   触发口子**只放这一处**：进图与换幕都会经过 CampaignPanel.OnOpenAsync，
            //   免得两处各写一遍、行为还不一致。是否已播记在存档 RunState.IntroShownAct。
            // ⚠ 彩排（GhostSilent，见 UIPanelBase.GhostSilent）时整块跳过：
            //   开场是"真进图"的标志性演出，记账/触发/播音都不该在彩排里发生。
            if (!UIPanelBase.GhostSilent && !_realmMode)   // ⛔ 秘境里不播主线的"第N幕"开场 —— 那是另一套世界
            {
                var introRun = WanXiang.Run.RunSave.Current;
                int introAct = introRun != null ? introRun.Act : 1;
                bool fromMenu = RequestIntroFromMenu;      // 由 HomePanel 出征时置位
                RequestIntroFromMenu = false;              // 一次性：消费掉，避免战后回图也重播
                if (ActIntroPanel.ShouldPlay(introAct, fromMenu))
                {
                    // ★ 若转场还在播，**等它结束再开场**（用户 2026-10-12：
                    //   "我不是说了要前面这个转场完才开始写字吗"）。
                    //   原来中点就打开 ActIntro ， 写字与转场退场重叠，观感很乱。
                    if (UiTransition.IsPlaying) DeferredIntroAsync().Forget();
                    else DeferredIntroAsync().Forget();   // ★ 统一走 DeferredIntroAsync（内含等奖励界面确认），不再绕过
                }
            }


            // 遗物 HUD（左上角一排小牌，悬浮看说明）
            var hudRun = WanXiang.Run.RunSave.Current;
            RelicHud.Populate(_rootRelics, _relicTemplate, hudRun != null ? hudRun.Relics : null,
                def => RelicHud.ShowTip(_relicTipRoot, _relicTipText, def),
                () => RelicHud.HideTip(_relicTipRoot));

            // ★ 待推进落地：从事件面板（灵市/孵穴/铸魂台/异闻/天象）返回节点图时，
            //   把之前未完成的节点记为通过 —— 玩家没处理完就关游戏的话，_pendingCommit 是内存变量、
            //   不会持久化，节点也不会推进 ， 下次进来还能重做（奖励不丢）。
            // ⚠ 彩排（GhostSilent）时跳过：不让彩排消费 PendingCommit / 写档（真开会再处理）。
            if (ResultPanelIsOpen()) { ScheduleDeferredCommit(); return UniTask.CompletedTask; }
            if (LandPendingCommit()) return UniTask.CompletedTask;

            BuildNodeMap();
            // ⚠ 图完整性告警：BuildRoute 在 64 次重试都失败时会回退到"缺省图"
            //   （旧 4 层结构、只有 6 个节点、布局重叠）—— 必须让它在 Console 里可见，
            //   否则就是"地图坏了但没有任何报错"（今天已经踩过这种静默降级）。
            if (_graph != null && _graph.Act < 5 && _graph.NodeCount < Layers)
                Debug.LogWarning("[Campaign] ⚠ 节点图不完整：NodeCount=" + _graph.NodeCount +
                                 " < 期望 " + Layers + " 层 —— BuildRoute 回退到了缺省图，" +
                                 "请检查 MeetsV12Constraints 或更换 RunSeed。");

Debug.Log("[Campaign] 图诊断：幕=" + (_graph != null ? _graph.Act.ToString() : "?") +
                      " NodeCount=" + (_graph != null ? _graph.NodeCount.ToString() : "?") +
                      " 实际画出=" + _nodeItems.Count +
                      "｜Act=" + (WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : -1) +
                      " RunSeed=" + (WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.RunSeed : 0));

            var req = payload as NodeRequest;
            // ★ 2026-11-15 用户实测：存档在第 3 幕倒数第二格，详情面板却显示"第 1 节 · 立秋"
            //   → 原来传 _current.Offset（默认 -1），会选中第一个可达节点。改为优先当前所在格。
            int openSel = req != null ? req.Offset : (_currentOffset >= 0 ? _currentOffset : _current.Offset);
            FillTeamPreview();
            ApplyRealmChrome();
            Debug.Log("[Campaign] 打开地图 openSel=" + openSel + " _currentOffset=" + _currentOffset
                + " _graph=" + (_graph != null ? _graph.NodeCount.ToString() : "NULL"));
            SelectNode(openSel, silent: true);
            // ★ 按钮组成要按模式落一次：冷启动直接开进秘境（RealmPanel 发现已在秘境 ⇒
            //   OpenAsRealm）走的是本方法，不是 OnRefresh，漏了这里返回键/星移会露着。
            // ★ 有未领的胜利奖励 → 下一帧补一次三选一（不能在"本面板还在开"的同一帧叠面板）
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 图变了以后要跟着变的那几样：背景 + 粒子、标题、离开按钮、当前选中格。
        /// <para>⛔ 抽出来是因为它被**两条路**用到：正常打开（OnOpenAsync）与
        /// 「同一个实例换图」（OnRefresh，即进/出秘境）。只写在 OnOpenAsync 里的话，
        /// 走 OnRefresh 那条路时背景会**留在主线那一幕**、信息卡会停在主线节点上
        /// （实测：进秘境后背景仍是第三幕秋天、右侧写着"第10节·秋分 / 天时 蛰虫始振"）。</para>
        /// </summary>
        /// <summary>
        /// 顶栏文案的**唯一拼装处**（幕 / 季节 / 守关 / Lv / 素材 / 灵卵 / 下场 buff）。
        /// ⛔ 别再在别处另拼一份 —— 曾因"简版"覆盖完整版，导致缺 Lv 与素材。
        /// </summary>
        private string BuildActTitle()
        {
            if (_graph == null) return "";
            var run = WanXiang.Run.RunSave.Current;
            int lv = run != null ? run.PlayerLevel : 1;
            if (_realmMode)
                return "秘境 · " + _graph.SeasonCn + "　｜　灵卵 " + (run != null ? run.Eggs : 0)
                     + " 枚　｜　Lv " + lv;
            var sb = new System.Text.StringBuilder();
            sb.Append("第").Append(CnNum(_graph.Act)).Append("幕 · ").Append(_graph.SeasonCn)
              .Append(" · 守关 ").Append(_graph.BossName)
              .Append("　｜　Lv ").Append(lv)
              .Append("　｜　素材 ").Append(run != null ? run.Materials : 0)
              .Append("　｜　灵卵 ").Append(run != null ? run.Eggs : 0).Append(" 枚");
            if (run != null)
            {
                int nextBuff = run.PlayerBuffPct + run.HealPending;
                if (nextBuff != 0)
                    sb.Append("　｜　下场我方 ").Append(nextBuff > 0 ? "+" : "").Append(nextBuff).Append("%");
                if (run.EnemyBuffPct != 0)
                    sb.Append("　｜　下场敌方 ").Append(run.EnemyBuffPct > 0 ? "+" : "").Append(run.EnemyBuffPct).Append("%");
            }
            return sb.ToString();
        }

        private void ApplyViewAfterGraphChange(bool silentSelect)
        {
            RefreshAmbient();
            ApplyRealmChrome();

            var run = WanXiang.Run.RunSave.Current;
            if (_tmpActTitle != null && _graph != null)
            {
                // ★ 2026-11-15：顶栏文案只有这一处来源（原来这里有个"简版"会覆盖完整版，
                //   导致缺 Lv / 素材 / 下场 buff）。
                _tmpActTitle.text = BuildActTitle();
            }

            // 选中格：秘境从 RealmCursor 恢复；主线回到"当前所在格"。
            int sel = -1;
            if (_realmMode) sel = run != null ? run.RealmCursor : -1;
            else sel = _currentOffset;
            SelectNode(sel, silent: silentSelect);
        }

        /// <summary>
        /// 按当前模式切换**按钮区的组成**（秘境 vs 主线各有一套出入口）。
        /// <para>⛔ 单独抽出来是因为它有**两个调用点**（首次打开 / 同实例换图）：
        ///   只写在 ApplyViewAfterGraphChange 里的话，"冷启动直接开进秘境"那条路
        ///   （RealmPanel 发现已在秘境 ⇒ OpenAsRealm）会漏掉，返回键和星移照样露着。</para>
        /// <para>秘境：只留「离开秘境」（左下）；隐藏「返回」（右上）—— 出口必须唯一，
        ///   否则玩家分不清哪条会回滚场域状态。</para>
        /// </summary>
        private void ApplyRealmChrome()
        {
            var qb = transform.Find("Btn_RealmQuit");
            if (qb != null && qb.gameObject.activeSelf != _realmMode)
                qb.gameObject.SetActive(_realmMode);

            if (_btnBack != null) _btnBack.gameObject.SetActive(!_realmMode);

            // 星移在秘境里一律不显示：秘境没有天时（不在天穹历法里），星移无事可做。
            // ⚠ 这里只兜"开图那一刻"；每次选中节点还会经 UpdateXingyiButton 再挡一次
            //   （否则点几下节点它又会被那边的逻辑重新激活）。
            if (_realmMode && _btnXingyi != null) _btnXingyi.gameObject.SetActive(false);
        }

        // ================================================================
        //  节点图（按 SolarTermGraph 的层拓扑生成）
        // ================================================================

        // ★ 2026-11-15：项目是 Linear 色彩空间，裸 new Color 会被当线性值 → 显示偏亮偏淡。
        //   ⛔ 不能在静态字段初始化器里调 UiInk.Srgb（会读 QualitySettings.activeColorSpace，
        //      该 API 禁止在 MonoBehaviour 构造/字段初始化阶段调用 → TypeInitializationException）。
        //      ⇒ 这里存 sRGB 原值，OnCreate 里用 EnsureInkColors() 一次性转 linear。
        private static Color NodeVisited = new Color(0.79f, 0.63f, 0.39f);   // 金
        private static Color NodeReachable = new Color(0.47f, 0.57f, 0.38f); // 木绿
        private static Color NodeLocked = new Color(0.30f, 0.27f, 0.24f);    // 灰（加深：彩叶/雪景底上也看得清）

        // 路线图尺寸常量（连线的节点坐标必须与排布公式一致，所以提出来共用）
        // ★ 2026-11-02：层数改为读 MapRules（Resources/Data/map.csv 可覆盖），
        //   不能再写 const —— 否则表里改了层数，这里的坐标/上限还是 18。
        private static int Layers => WanXiang.Campaign.MapRules.Layers;
        private const float NodeW = 420f, NodeH = 110f, GapX = 24f, GapY = 90f;
        private const float NodeAreaW = 1376f;   // Scroll_Nodes 内容区可用宽

        /// ⛔ 面板是 Transient + Cached 复用，所以传完**立刻清空静态引用**，
        ///   否则下一次正常打开节点地图会继续用这张旧图（而且不报错）。
        /// </summary>
        public static void OpenAsRealm(WanXiang.Campaign.ActGraph graph)
        {
   _injectedGraph = graph;
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
  if (ui == null) { _injectedGraph = null; return; }
  ui.OpenAsync<CampaignPanel>(graph).Forget();
        }

        /// <summary>
        /// 已打开实例的"刷新"回调（UISystem 在实例已 Opened 时走 InternalRefresh，**不重放 OnOpenAsync**）。
        /// ⛔ 所以切秘境图必须在这里也处理一遍，否则会出现
        ///   "画面是主线图、状态却是秘境图"的错位（实测点按钮必现）。
        /// </summary>
        protected override void OnRefresh(object payload)
        {
            RestorePendingBattlePass();
            var g = payload as WanXiang.Campaign.ActGraph;
            // ★ 地图常开：子面板关闭后 OpenAsync 只走刷新路径（payload 为空）⇒
            //   无条件落地 + 全量重建（同样不能拿 PendingCommit 当判据）。
            if (g == null)
            {
                if (ResultPanelIsOpen()) ScheduleDeferredCommit();
                else { LandPendingCommit(); BuildNodeMap(); ApplyViewAfterGraphChange(silentSelect: true); }
                return;
            }

            // ★★ 自愈：**在秘境模式、但存档说已经不在秘境里** ⇒ 说明刚撤离了，
            //   应该原地切回主线图。
            //   为什么做成"自愈"而不是显式调用：撤离路径有好几条（按钮 / 调试 / 战败），
            //   靠调用方记得"顺手把面板切回来"迟早会漏；而"状态与存档不一致就修"是无条件成立的。
            if (g == null && _realmMode)
            {
                var r = WanXiang.Run.RunSave.Current;
                if (r == null || !r.InRealm)
                {
                    _realmMode = false;
                    _realmGraphPayload = null;
                    _injectedGraph = null;
                    _currentOffset = -1;
                    _visited.Clear();
                    _passed.Clear();
                    BuildNodeMap();
                    ApplyViewAfterGraphChange(silentSelect: true);
                    return;
                }
            }

            if (g != null && !g.IsEmpty)
            {
                _realmGraphPayload = g;
                _injectedGraph = null;
                _realmMode = true;
                _currentOffset = -1;
                _visited.Clear();
                _passed.Clear();
                BuildNodeMap();
                // ⛔ 必须走这一个统一入口：背景/标题/离开按钮/选中格都要跟着换，
                //   只 BuildNodeMap 的话背景会留在主线那一幕（实测踩过）。
                ApplyViewAfterGraphChange(silentSelect: true);
            }
        }

        /// <summary>秘境模式下的撤离入口（由 <see cref="RealmSession"/> 提供回滚语义）。</summary>
        public static System.Action OnRealmQuitRequested;

        /// <summary>是否处于秘境模式（外部可读：决定「出征」按钮文案等）。</summary>
        public bool InRealmMode { get { return _realmMode; } }
        private int _currentOffset = -1;      // 当前所在节点（-1 = 还没出发）
        private int _selected = -1;
        /// <summary>本次**真正通过**的节点 —— 只在 OnOpenAsync 把 <c>NodeOffset = PendingCommit</c>
        ///  落地那一刻才置位。-1 = 本次没有"通过"。
        ///  ⛔ 绝不能用"进过节点(VisitedNodes)"当判据：点进战斗/事件节点**只是进入**（进入即写 VisitedNodes），
        ///     玩家点开编队又返回、或事件没做完就返回，都不算通过 —— 用 visited 会误播动画（用户实锤）。</summary>
        private int _justPassedNode = -1;
        /// <summary>本幕"真·通过"的节点集合（= PassedNodes ∩ VisitedNodes）。落印✕ / 连线实线 只看它，
        ///  ⛔ 不看 _visited（那是"进入"）。</summary>
        private readonly HashSet<int> _passed = new HashSet<int>();
        /// <summary>挂了「瘴雾」天时的节点（点"前往"走到它 ， 起雾）。</summary>
        private readonly HashSet<int> _fogWeather = new HashSet<int>();
        /// <summary>本幕被强制成"整层都是 ？"的层（走进去必定起瘴雾）。</summary>
        private readonly HashSet<int> _forcedQLayers = new HashSet<int>();
        /// <summary>待播动画的节点（等真正回到地图看到画面时才播，见 PlayPendingAnim）。</summary>
        private readonly HashSet<int> _pendingNodes = new HashSet<int>();
        private bool _pendingAnim;
        /// <summary>本次画出的边（供回到地图后播画线动画）。</summary>
        private readonly List<RectTransform> _edgeItems = new List<RectTransform>();
        private readonly List<int> _edgeA = new List<int>();
        private readonly List<int> _edgeB = new List<int>();
        private readonly List<RectTransform> _nodeItems = new List<RectTransform>(8);

        /// <summary>
        /// 等 UI 转场进入**退场/拨开**阶段再打开写字开场。
        /// ⚠ 不是等转场完全结束：用户 2026-10-12："粒子拨开转场就立马到写字了，没有衔接起来，
        ///    要连贯"。， 在退场那一刻就让写字淡入，与拨开重叠，视觉上是连续的。
        /// </summary>
        /// <summary>第 5 幕：先等奖励关 + 播幕间转场，转场结束后再弹天阙抉择（原来三者抢屏）。</summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid DeferredIntroThenTrialAsync()
        {
            await WaitResultClosedAsync();          // ★ 奖励确认后才播
            await OpenPanelAsync<ActIntroPanel>();
            int guard = 0;
            while (guard < 1800)
            {
                var ip = UnityEngine.Object.FindObjectOfType<WanXiang.Modules.UI.ActIntroPanel>(true);
                if (ip == null || !ip.gameObject.activeInHierarchy) break;
                guard++;
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            Debug.Log("[Campaign] 天阙转场播完（等 " + guard + " 帧），打开天阙抉择");
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui != null) ui.OpenAsync<WanXiang.Modules.UI.TrialPanel>().Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid DeferredIntroAsync()
        {
            // ★ 2026-10-14 首播优化（用户："从主界面到节点地图的转场第一次会卡，后面就流畅"）：
            //   开场面板**本进程首次**打开时，写字笔顺帧 / 字形图集 / 首帧布局都是冷的，
            //   打开那一帧实测卡 ~0.5s，且正好落在转场"拨开退场"里、肉眼可见。
            //   首次改在**建图转场的遮盖期内**就打开（不等退场时刻）—— 卡顿落在屏幕
            //   被盖住时、不可见；第二次起资源已热，照旧等退场时刻，保持"写字与拨开
            //   重叠"的原设计。冷热判定用 ActIntroPanel.SessionPlayed。
            // ★ 2026-11-14 用户：守关 Boss 的奖励界面挡住幕间转场，甚至让人以为跳幕
            //   → 先等 ResultPanel 关掉（玩家确认奖励），再播写字转场。
            await WaitResultClosedAsync();

            bool cold = !ActIntroPanel.SessionPlayed;
            if (cold)
            {
                // 让当前这帧的建图开面收尾再开开场（避免嵌在建图开面的调用栈里连锁重活）
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
            }
            else
            {
                int guard = 0;
                // 等退场开始；若转场异常没在播（ guard 耗尽）也继续，别卡住不进图
                while (UiTransition.IsPlaying && !UiTransition.ExitStarted && guard < 900)
                {
                    guard++;
                    await Cysharp.Threading.Tasks.UniTask.DelayFrame(1);
                }
            }
            Debug.Log(cold
                ? "[Campaign] 开场首播（冷）→ 遮盖期内提前打开（首帧成本藏进转场）"
                : "[Campaign] 转场退场中 → 开始写字开场（与拨开重叠）");
            OpenPanelAsync<ActIntroPanel>().Forget();
        }

/// <param name="payloadGraph">
        /// 可选的**外部图**（秘境内部图）。非空则用它替代 BuildRoute —— 这就是"秘境 = 特殊的一幕"。
        ///   走参数而不是 static 字段优先；static 只是给 <see cref="OpenAsRealm"/> 兜底。
        /// </param>
        /// <summary>
        /// 把本幕中段的某一格改写成「越境」（2026-10-18）。**只对续劫局生效**。
        /// <para>位置：第 0.65×层数 层的第一格；跳过结构位（0=招募 / 1=遗物 / 末=守关）。</para>
        /// <para>⛔ 不做成"随机多插一格"—— 图的**拓扑不能变**（层/边/节气序号全不动），
        ///   只换某一格的**类型**，这样所有按下标存的存档字段仍然有效。</para>
        /// </summary>
        private void ApplyActSkipNode(WanXiang.Run.RunState run)
        {
            if (_realmMode || run == null || _graph == null || _graph.Kinds == null) return;
            if (run.Ascension <= 0) return;                       // 第一周目没有
            if (_graph.Act < 1 || _graph.Act >= 5) return;        // 天阙（第 5 幕）不插
            if (_graph.Layers == null || _graph.Layers.Length < 6) return;

            int layer = Mathf.Clamp(Mathf.RoundToInt((_graph.Layers.Length - 1) * 0.65f),
                                    2, _graph.Layers.Length - 2);   // 避开首两层与幕末
            var cells = _graph.Layers[layer];
            if (cells == null || cells.Length == 0) return;
            int off = cells[0];
            if (off < 0 || off >= _graph.Kinds.Length) return;
            if (off == _graph.NodeCount - 1) return;

            _graph.Kinds[off] = WanXiang.Campaign.NodeKind.ActSkip;
            Debug.Log("[Campaign] 续劫局：第 " + _graph.Act + " 幕第 " + (off + 1) + " 格为「越境」");
        }

        /// <summary>雾遮到的"起始层"：layer >= 它就被雾盖住。当前层 + 下一层可见；没雾 = int.MaxValue。</summary>

        /// <summary>选中一个节点：刷新信息卡与"出征/前往"按钮文案。</summary>
        /// <summary>
        /// 重置显示状态（归元/开新局时用）—— 光关面板不够，`_currentOffset`/`_visited`
        /// 这些字段还留着上一局的值，重开时会沿用旧位置（用户实测：归元后重进
        /// 落点在第 27 节，而不是起点）。
        /// </summary>
        public void ResetViewForNewRun()
        {
            _currentOffset = -1;
            _visited.Clear();
            // ★ 跨局清空星移余气（会话级、非存档），避免上一局的余气污染新局天气
            WanXiang.Campaign.LiveWeather.Reset();
            var _run = WanXiang.Run.RunSave.Current;
            if (_run != null && _run.XingyiLingers != null) _run.XingyiLingers.Clear();   // 持久化镜像一并清，防跨局残留
            Debug.Log("[Campaign] 视图状态已重置（_currentOffset=-1, visited 清空）");
        }

        /// <summary>
        /// 按【当前存档】重建图并重画 —— 供天阙抉择里"续劫"复用本面板时调用
        /// （节点图就在栈里，弹窗关掉后会露出来；只需让它按新的 Act 重排）。
        /// </summary>
        public void ReloadForCurrentAct()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) return;
            RebuildGraphFor(run.Act, run.RunSeed);
            _currentOffset = run.NodeOffset;
            _visited.Clear();
            if (run.VisitedNodes != null)
                foreach (var v in run.VisitedNodes) _visited.Add(v);
            BuildNodeMap();
            Debug.Log("[Campaign] 已按当前存档重排节点图：Act=" + run.Act +
                      " Layers=" + (_graph != null && _graph.Layers != null ? _graph.Layers.Length : -1) +
                      " NodeCount=" + (_graph != null ? _graph.NodeCount : -1));
        }

        /// <summary>按幕号重建路线图（换幕时用）。</summary>
        private void RebuildGraphFor(int act, int runSeed)
        {
            int a = Mathf.Clamp(act, 1, 5);
            ulong sd = WanXiang.Campaign.SolarTermGraph.RouteSeed(runSeed, a);
            _graph = WanXiang.Campaign.SolarTermGraph.BuildRoute(a, sd, Layers);
        }

        /// <summary>
        /// 节点选中反馈（2026-10-22 用户："选中的时候怎么没有提示，边框/图标亮起来"）：
        /// 选中的卡放大 1.06 + 染暖金色；其它恢复常态。
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, Color> _dotColorCache
            = new System.Collections.Generic.Dictionary<int, Color>();

        /// <summary>
        /// 节点选中反馈（**主线与秘境共用这一份**）。
        /// 2026-10-22 用户："金黄色太丑了，直接把左边这个图标亮起来就行" ⇒ 圆点放大 1.35 + 提亮；
        /// 2026-11-04 用户："秘境里面节点选中的时候没有变大，还有选中的反馈" ⇒ 补上整卡 ×1.06。
        /// <para>⛔ 以前这里只做圆点、而 SelectNode 末尾又有一份"整卡 1.06 + 改圆点颜色"的旧循环，
        ///   两份互相覆盖；且秘境分支在中途 return，根本走不到末尾那份 ⇒ 秘境完全没反馈。
        ///   现在只保留这一个实现，SelectNode 里那份已删。</para>
        /// </summary>
        private void ApplySelectionVisual(int offset)
        {
            for (int i = 0; i < _nodeItems.Count; i++)
            {
                var it = _nodeItems[i];
                if (it == null) continue;
                int off = OffsetOfName(it.name);
                bool sel = off == offset;

                // 整卡轻微放大：不染底色，避免又变成"金黄色卡"
                it.localScale = sel ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;

                var dotT = it.Find("Img_Dot") as RectTransform;
                if (dotT == null) continue;
                var dot = dotT.GetComponent<Image>();
                if (dot == null) continue;

                if (!_dotColorCache.ContainsKey(off)) _dotColorCache[off] = dot.color;
                dotT.localScale = sel ? Vector3.one * 1.35f : Vector3.one;
                // ★ 2026-11-16 用户："奇遇/未知的图标变成纯绿圆，图标不符合"
                //   根因：这里把**可前往**节点强行染成木绿 NodeReachable，
                //   覆盖了上面按 NodeKind 设好的图标自带配色（书/问号/骰子全被染绿）。
                //   修：任何状态都保留 dot 原色（图标语义优先）；"可前往"由卡片描边 +
                //   文字「← 可前往」+ 选中放大表达，不再靠染图标。
                dot.color = _dotColorCache.TryGetValue(off, out var c0) ? c0 : Color.white;
            }
        }

        private void SelectNode(int offset, bool silent)
        {
            Debug.Log("[Campaign] SelectNode 入口 offset=" + offset + " _graph="
                + (_graph != null ? _graph.NodeCount.ToString() : "NULL")
                + " _realmMode=" + _realmMode + " 名称控件=" + (_tmpNodeName != null));
            if (_graph == null)
            {
                if (!silent) Debug.LogWarning("[Campaign] 节点图还没建好。");
                return;
            }
            if (offset < 0 || offset >= _graph.NodeCount)
            {
                // ★★ 越界必须先归零！否则下面循环条件（offset < 0）不成立、循环不执行，
                //    带着越界值走到 Terms[offset] 就 IndexOutOfRange（用户实测：奖励后回节点图报错）。
                //    越界来源：payload 带的 Offset 属于旧图/上一幕，格号超出当前图范围。
                if (offset >= _graph.NodeCount) offset = Mathf.Max(0, _graph.NodeCount - 2);

                // 没有 payload 时默认指向第一个可前往的节点，避免右侧信息卡空着
                for (int l = 0; l < _graph.Layers.Length && offset < 0; l++)
                    foreach (var o in _graph.Layers[l])
                        if (IsReachable(o)) { offset = o; break; }
                // ★ 兜底：站在最后一个节点上时没有"可达后继"，但详情面板仍应显示当前格信息，
                //   否则一片 Node Name/Type/Weather 占位符（用户实测"什么都点不了"）。
                if (offset < 0 && _currentOffset >= 0 && _currentOffset < _graph.NodeCount)
                    offset = _currentOffset;
                if (offset < 0) return;
            }

            _selected = offset;
            // 玩家点选节点。silent=true 是程序化恢复视图（开面板/切幕），不该出声。
            if (!silent) SfxThrottled("sfx_map_node_select");
            var kind = _graph.KindOf(offset);
            RefreshNextButton(kind);   // ⛔ 必须在秘境 early-return **之前**：否则秘境里这颗永远停在旧文案（实测"前往·异闻"）

            // ★★ 秘境模式的信息卡（2026-10-16 用户："这个节点的天时是不是可以改一下，
            //   全部去掉或者在其他地方说明这个秘境的特性等等"）。
            //   ⇒ 秘境没有节气、也没有主线天时（天时是"天穹历法"的产物，秘境在地图之外），
            //     继续显示"第1节·立春 / 天时 蛰虫始振"是**假的**，玩家会以为天时在场域里生效。
            //     改成：标题写层号 + 场域名，正文写**场域特性**（这才是玩家真正要读的信息）。
            if (_realmMode)
            {
                ApplySelectionVisual(offset);
                UpdateRealmInfoCard(offset, kind, silent);
                return;
            }

            ApplySelectionVisual(offset);
            string term = TermName(_graph.Terms[offset]);

            // ★ 2026-11-15 用户：这里原来用 (offset+1) 格号，会显示"第 51 节"（本幕只有 18 层）
            if (_tmpNodeName != null) _tmpNodeName.text = "第 " + (_graph.LayerOf(offset) + 1) + " 层 · " + term;
            // ★ 天阙最后一格 = 终局战（后土）：类型显示为"终局·后土"，而不是占位用的【精英】
            bool isFinaleBoss = _graph != null && _graph.Act >= 5 && offset == _graph.NodeCount - 1;
            if (isFinaleBoss && _tmpNodeName != null)
                _tmpNodeName.text = "终局 · " + (_graph.BossName ?? "后土");

            // ★★ 守关（幕 1~4 的最后一格）也必须改类型标签：它在图里复用【精英】作占位，
            //   光看 kind 会显示成"【精英】"——和"普通精英"完全看不出区别，玩家不知道下一格是首领。
            //   isFinaleBoss（第 5 幕）走上面那行；这里只管四季幕的守关。
            bool isSeasonBoss = IsSeasonBossNode(offset);
            if (isSeasonBoss && _tmpNodeType != null)
                _tmpNodeType.text = "守关 · " + (_graph.BossName ?? "首领") + "　（战斗）";
            else if (_tmpNodeType != null && !isFinaleBoss)
                _tmpNodeType.text = WanXiang.Campaign.NodeKinds.Cn(kind) +
                                    (kind == WanXiang.Campaign.NodeKind.ActSkip
                                        ? "　（续劫专属 · 直入下一幕）"
                                        : (WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "　（战斗）" : "　（休整）"));
            if (_tmpWeather != null) _tmpWeather.text = WeatherHint(kind, isSeasonBoss, isFinaleBoss);
            // ★ 2026-11-19：节点信息面板显示附加目标说明（就地重算，避免依赖 _current 赋值顺序）
            if (_tmpNodeGoal != null)
            {
                var runForGoal = WanXiang.Run.RunSave.Current;
                string gid = (runForGoal != null && WanXiang.Campaign.NodeKinds.IsBattle(kind))
                    ? (WanXiang.Battle.Core.ObjectiveRules.AssignFor(
                        (ulong)runForGoal.RunSeed, _graph != null ? _graph.Act : 1, offset)?.Id ?? "")
                    : "";
                bool hasGoal = !string.IsNullOrEmpty(gid);
                _tmpNodeGoal.gameObject.SetActive(hasGoal);
                if (hasGoal)
                {
                    var goalDef = WanXiang.Battle.Core.ObjectiveRules.Get(gid);
                    if (goalDef != null)
                    {
                        // ★ 2026-11-20：两行分层，避免长文案挤成孤字换行
                        string desc = goalDef.Desc.Contains("N")
                            ? goalDef.Desc.Replace("N", goalDef.Param.ToString()) : goalDef.Desc;
                        _tmpNodeGoal.text = "<size=21><color=#8A6A2F>额外目标 · " + goalDef.Name + "</color></size>"
                            + "\n<size=17>" + desc + "　<color=#3B6D11>"
                            + WanXiang.Battle.Core.ObjectiveRules.RewardText(goalDef) + "</color></size>";
                    }
                }
            }
            Debug.Log("[Campaign] SelectNode 完成 offset=" + offset + " 名称=\"" + (_tmpNodeName != null ? _tmpNodeName.text : "NULL") + "\"");

            // _current 是常驻实例（表单里被 Btn_Next 复用），逐字段赋值而不是换新对象
            _current.Title = term;
            _current.Weather = WeatherHint(kind, isSeasonBoss, isFinaleBoss);
            _current.Seed = (ulong)(_graph.Act * 1000 + offset + 7);
            _current.NodeIndex = offset;
            _current.Act = _graph.Act;
            _current.Offset = offset;
            _current.Kind = kind;
            _current.RealmLayer = -1;
            // ★ 守关 = 幕末最后一格（图里它是【精英】占位，靠这个标志与"普通精英"区分）。
            //   只对**四季幕（1~4）**置位：这些幕的守关抽 BossCatalog 首领（BossSquadFor）。
            //   天阙（第 5 幕）最后一格是终局战（后土），由 TrialPanel.BuildFinaleBattle 处理，
            //   不走这里，故排除，避免"双重首领"。
            _current.IsBoss = _graph.Act >= 1 && _graph.Act <= 4 &&
                              _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;
            // ★ 2026-11-19：附加目标按 (RunSeed, act, offset) 确定性分配；只有战斗节点才有
            {
                var runForGoal = WanXiang.Run.RunSave.Current;
                bool isBattle = WanXiang.Campaign.NodeKinds.IsBattle(kind);
                _current.GoalId = (runForGoal != null && isBattle)
                    ? (WanXiang.Battle.Core.ObjectiveRules.AssignFor(
                        (ulong)runForGoal.RunSeed, _graph.Act, offset)?.Id ?? "")
                    : "";
            }

            // ★ 终局战格（第 5 幕末格）：走 TrialPanel.BuildFinaleBattle（Boss + 我方镜像×3），
            //   与守关（BossSquadFor：Boss + 2 随从）是**两条不同的建敌路径**，必须分开标。
            //   ⚠ 少了这个标志，第 5 幕最后一格会退回普通遭遇 —— 用户实测"第五幕 boss 关
            //     还是小异兽（诸怀/旱魃/毕方），标题只写天时、没有 Boss 关字样"。
            _current.IsFinale = _graph.Act >= 5 &&
                                _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;

            // ★ 活链路天气：选节点时算好「节点天时 + 重放 run.Path 还原的余气」合成一份
            //   WeatherDef 存入 LiveWeather.Current，供 BattleRequestFactory / FormationPanel 取用
            //   （取代原来只给占位的 WeatherHint）。可复现性红线：算不出 = null（旧行为逐位一致）。
            // ★ 星移区统一刷新：合成天时 → 信息卡（标题 + 去重后的效果行 + 星移摘要）→ 按钮状态。
            //   每节点限一次（写存档：换节点/进出编队/重启都不重置）；未出发可撤销；灵卵不足置灰。
            RefreshXingyiUI();

            // 选中反馈已统一到 ApplySelectionVisual（主线/秘境都走那一份）。
        }

        /// <summary>右下角「出征 / 前往 · X」按钮的文案。主线与秘境**共用**这一份。
        /// <para>⛔ 之前写在 SelectNode 的末尾，而秘境分支在中途就 return 了 ⇒
        ///   秘境里这颗按钮永远显示上一次主线选中的节点（用户实测停在"前往·异闻"）。</para>
        /// </summary>
        private void RefreshNextButton(WanXiang.Campaign.NodeKind kind)
        {
            if (_btnNext == null) return;
            var label = _btnNext.transform.Find("Tmp_Label");
            var t = label != null ? label.GetComponent<TMP_Text>() : null;
            if (t != null)
                t.text = WanXiang.Campaign.NodeKinds.IsBattle(kind) ? "出征" : "前往 · " +
                         WanXiang.Campaign.NodeKinds.Cn(kind);
        }

        /// <summary>
        /// 该格是不是**四季幕（1~4）的守关**（幕末最后一格）。
        /// ⚠ 唯一口径：类型标签、说明文案、<c>NodeRequest.IsBoss</c> 三处都读它 ——
        ///   守卫格在图里复用 NodeKind.Elite 作占位，判定公式散在一处就会漂移
        ///   （曾漏掉类型标签，导致守关显示成"【精英】"）。改这里等于改全局。
        /// 第 5 幕（天阙）**不算**：它最后一格是终局战（后土），走 TrialPanel.BuildFinaleBattle。
        /// </summary>
        private bool IsSeasonBossNode(int offset)
        {
            return _graph != null && _graph.Act >= 1 && _graph.Act <= 4 &&
                   _graph.NodeCount > 0 && offset == _graph.NodeCount - 1;
        }

        /// <summary>
        /// 节点的说明文案。天气/场地事件由 WeatherComposer / FieldEvents 决定，先给占位。
        /// <paramref name="isSeasonBoss"/>：幕 1~4 的守关格（幕末最后一格）—— 说明要写首领，
        /// 不能沿用精英那句"规模 +1，旗舰携劫象"，否则玩家进战前以为只是精英战。
        /// <paramref name="isFinaleBoss"/>：第 5 幕终局战（后土）。
        /// </summary>
        private static string WeatherHint(WanXiang.Campaign.NodeKind kind, bool isSeasonBoss = false,
                                          bool isFinaleBoss = false)
        {
            // 守关优先：它与精英在图里同占位，但战斗内容完全不同（首领 + 随从，带 BossUnitMul）。
            if (isFinaleBoss) return "终局战：后土镇守中宫，敌方还有你队伍前几只的镜像";
            if (isSeasonBoss) return "守关：本幕首领亲自镇守（属性 ×1.35，另有随从）——硬仗";
            // ★ 2026-11-08 R5-a：节点说明改为**表驱动**（labels.csv 的 node_hint 组）。
            //   改文案 = 改 CSV；删节点 = 删 CSV 行（自动回退 default，不留 case）；
            //   加节点 = 加一行。`Omen` 只保留兼容文案（新图已不产出该节点）。
            return WanXiang.Battle.Core.LabelTable.Of("node_hint", kind.ToString(),
                "遭遇：常规战斗，敌方按幕数规模成队");
        }

        /// <summary>
        /// R5-b：把"一行调用"型节点注册到路由表。只注册一次（Count 为 0 时初始化）。
        /// ⚠ 委托捕获 this（实例级 router），面板销毁后自然释放。
        /// </summary>
        private void EnsureNodeRoutes()
        {
            if (_nodeRouter.Count > 0) return;
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Realm, _ => OpenRealmPanel());
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.RealmVault, _ => OpenRealmVault());
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.ActSkip, _ => ConfirmActSkip());
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Shop, _ => OpenPanelAsync<MarketPanel>().Forget());
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Omen, _ => OpenPanelAsync<OmenPanel>().Forget());
            // 招募节点已删除（2026-11-12）：不再注册路由；老存档遇到该枚举按普通节点处理
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Relic, _ => OpenPanelAsync<RelicPanel>(_current).Forget());
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Nest, r => NestChoose(r));
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Respite, r => NestChoose(r));   // 歇脚：复用扎营二选一
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Crossroad,
                r => WanXiang.Modules.UI.TalePanel.OpenEvent(BuildCrossroadEvent(), null));
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Gamble,
                r => WanXiang.Modules.UI.TalePanel.OpenEvent(BuildGambleEvent(), null));
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Tale, r => RunTaleNode(r));
            _nodeRouter.Register(WanXiang.Campaign.NodeKind.Forge, r => RunForgeNode(r));
        }

    }
}