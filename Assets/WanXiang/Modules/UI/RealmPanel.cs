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
        //⛔ FullScreen = true（2026-10-16 截图后改）：这是**路线图级**界面，不是弹窗。
        //   原来 false ⇒ 1080×720 居中，底下露出主城的标题和按钮，像两张界面叠在一起；
        //   而且小地图只有 560 高，却要塞 6 层节点 ⇒ 节点直接溢出到卡片外面。
       FullScreen = true, CloseOnMaskClick = false)]
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
     [SerializeField] private RectTransform _lineLayer; // LineLayer（连线，**必须先于节点层**）
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

        [Header("内容目录（战斗组阵用；留空会走 Resources 兜底）")]
  [SerializeField] private WanXiang.Fusion.ContentCatalogSO _contentCatalog;

        // ---- 节点尺寸（**按地图实际层数/列数自适应**，见 FitNodeMetrics）----
        private float _nodeW = 256f;
        private float _nodeH = 160f;
        private float _gapX = 40f;
        private float _gapY = 64f;
        private int _layers = 1;
        private static readonly Color UIBuildInk = new Color(0.94f, 0.92f, 0.86f, 1f);

        private RealmDef _realm;
        private RealmMap _map;
        private int _rerollUsed;
        private int _curNode = -1;                          // 当前所在节点（-1 = 未进场）
        private readonly HashSet<int> _cleared = new HashSet<int>();
        private readonly List<GameObject> _nodeViews = new List<GameObject>();
        private readonly List<int> _nodeOffsets = new List<int>();   // 与 _nodeViews 一一对应的 offset

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
        //⛔ 连线层也要关：上一局秘境内可能留了虚线，入口态露出来就是"鬼影"。
ClearNodes();
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
        ///
        /// ★★ 第二版（截图后）：节点尺寸改为**按容器与层数自适应**（<see cref="FitNodeMetrics"/>）。
    ///   上一版用固定 NodeW/NodeH，再把 content 撑到 layers*(H+Gap)+40 ——
        ///   6 层 × (72+40) = 672 塞进 560 高的卡片 ⇒ 底部两层直接掉到卡外。
        /// </summary>
        private void BuildMap()
    {
  if (_gridContent != null) _gridContent.gameObject.SetActive(true);
            if (_lineLayer != null) _lineLayer.gameObject.SetActive(true);
    ClearNodes();
   var run = WanXiang.Run.RunSave.Current;
if (_map == null && run != null)
                _map = RealmMapBuilder.Build(_realm, RealmSession.RealmSeedFor(run));
            if (_map == null || _map.Graph == null || _nodeTemplate == null) return;

            _layers = Mathf.Max(1, _map.LayerCount);
       FitNodeMetrics();

            // 节点：**从下往上**（第 0 层在底部 = 入口，首领/出口在顶部），与主线同一读法
    for (int layer = 0; layer < _layers; layer++)
            {
     var row = _map.Graph.Layers[layer];
          float y = NodeY(layer);
    for (int k = 0; k < row.Length; k++)
   {
     int off = row[k];
         var go = Instantiate(_nodeTemplate, _gridContent);
     go.SetActive(true);
      var rt = (RectTransform)go.transform;
 rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(_nodeW, _nodeH);
             rt.anchoredPosition = new Vector2(NodeX(k, row.Length), y);

      ScaleNodeText(rt);
   PaintNode(go, off, _cleared.Contains(off), off == _curNode);
   var btn = go.GetComponent<Button>();
          if (btn == null) btn = go.AddComponent<Button>();
      var idx = off;
btn.onClick.AddListener(() => OnNodeClicked(idx));
            _nodeViews.Add(go);
  _nodeOffsets.Add(off);
  }
     }

        DrawEdges();
        }

        /// <summary>
      /// 按容器可用空间与实际层数/列数，**反算**节点尺寸与间距。
        /// ⛔ 这一步是第二版补的关键：地图层数随波数变（5~7 层）、
        ///   列数随每层节点数变（1~5 列），写死尺寸必然在某些场域溢出。
        /// </summary>
        private void FitNodeMetrics()
    {
        float availW = _gridContent != null ? _gridContent.rect.width : 1200f;
   float availH = _gridContent != null ? _gridContent.rect.height : 700f;
       if (availW < 64f) availW = 1200f;
      if (availH < 64f) availH = 700f;

            int maxCols = 1;
      for (int i = 0; i < _layers; i++)
       {
        int n = _map.Graph.Layers[i] != null ? _map.Graph.Layers[i].Length : 0;
          if (n > maxCols) maxCols = n;
     }

      // 纵向：层数 × (节点高 + 层距) 必须塞进可用高度
            float slotH = availH / _layers;
         _nodeH = Mathf.Clamp(slotH * 0.68f, 56f, 160f);
   _gapY = Mathf.Max(18f, slotH - _nodeH);

   // 横向：最多的一层 × (节点宽 + 列距) 必须塞进可用宽度
         float slotW = availW / maxCols;
         _nodeW = Mathf.Clamp(slotW * 0.74f, 90f, 256f);
         _gapX = Mathf.Max(16f, slotW - _nodeW);
      }

        /// <summary>第 layer 层里第 k 个节点的 x 坐标（层内水平居中铺开）。</summary>
        private float NodeX(int k, int rowLen)
        {
         return rowLen <= 1 ? 0f : (k - (rowLen - 1) * 0.5f) * (_nodeW + _gapX);
        }

   /// <summary>第 layer 层的 y 坐标（层号越大越靠上）。</summary>
  private float NodeY(int layer)
        {
            return -(_layers - 1 - layer) * (_nodeH + _gapY);
        }

        /// <summary>节点中心的本地坐标（连线用；必须与上面的排布公式一致）。</summary>
        private Vector2 NodePos(int offset)
        {
   int layer = _map.LayerOf(offset);
       if (layer < 0) return Vector2.zero;
 var row = _map.Graph.Layers[layer];
            int k = System.Array.IndexOf(row, offset);
          return new Vector2(NodeX(k, row.Length), NodeY(layer));
        }

        /// <summary>
        /// 文字随节点缩放：字号按节点宽高同比缩，避免自适应后大字溢出小卡。
   /// </summary>
private void ScaleNodeText(RectTransform rt)
        {
            float k = _nodeW / 256f;
     var icon = rt.Find("Tmp_NodeIcon");
            var nameTx = rt.Find("Tmp_NodeName");
            var mark = rt.Find("Img_Cleared");
if (icon != null)
     {
       var rtIcon = icon as RectTransform;
    if (rtIcon != null) rtIcon.sizeDelta = new Vector2(240f * k, 74f * k);
    var t = icon.GetComponent<TMPro.TMP_Text>();
                if (t != null) t.fontSize = Mathf.Max(12, Mathf.RoundToInt(52f * k));
   }
 if (nameTx != null)
            {
                var rtName = nameTx as RectTransform;
                if (rtName != null) rtName.sizeDelta = new Vector2(240f * k, 40f * k);
         var t = nameTx.GetComponent<TMPro.TMP_Text>();
        if (t != null) t.fontSize = Mathf.Max(10, Mathf.RoundToInt(28f * k));
}
            if (mark != null)
            {
     var rtMark = mark as RectTransform;
    if (rtMark != null) rtMark.sizeDelta = new Vector2(_nodeW, _nodeH);
        }
        }

   /// <summary>
    /// 层间连线 —— 与 CampaignPanel.DrawEdges **同一套视觉语言**：
    /// 未走通 = 虚线（Tiled 平铺），走通 = 实线描金。
        /// ⛔ 秘境第一版**没有连线**，表现就是"一堆孤立方块"，跟主线路线图完全不像
        ///   （用户要求"直接套用现在的节点地图设计"）。连线是"像不像"的关键一环。
        /// </summary>
     private void DrawEdges()
      {
        if (_lineLayer == null || _map == null || _map.Graph == null) return;
       for (int i = _lineLayer.childCount - 1; i >= 0; i--)
        if (_lineLayer.GetChild(i) != null) Destroy(_lineLayer.GetChild(i).gameObject);

    if (_map.Graph.Edges == null) return;
      for (int a = 0; a < _map.Graph.Edges.Count; a++)
            {
        if (a >= _map.Graph.NodeCount) continue;
       var tos = _map.Graph.Edges[a];
    if (tos == null) continue;
              foreach (var b in tos)
    {
           if (b < 0 || b >= _map.Graph.NodeCount) continue;
         Vector2 pa = NodePos(a);
           Vector2 pb = NodePos(b);
       bool walked = _cleared.Contains(a) && _cleared.Contains(b);

            var go = new GameObject("Edge_" + a + "_" + b, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
      rt.SetParent(_lineLayer, false);
          rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
       rt.pivot = new Vector2(0f, 0.5f);
              // ⚠ 高度必须与 line_dash 贴图一致（与 CampaignPanel 同款），否则虚线会被裁掉
   rt.sizeDelta = new Vector2(Mathf.Max(1f, Vector2.Distance(pa, pb)), 14f);
            rt.anchoredPosition = pa;
      rt.localRotation = Quaternion.Euler(0f, 0f,
      Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);

     var img = go.AddComponent<UnityEngine.UI.Image>();
              img.sprite = walked ? EdgeSolidSprite() : EdgeDashSprite();
   img.type = walked ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Tiled;
   img.color = walked ? new Color(0.79f, 0.63f, 0.39f, 1f) : new Color(0.72f, 0.68f, 0.60f, 0.55f);
   img.raycastTarget = false;
     }
     }
        }

    private static Sprite _edgeSolid, _edgeDash;
   private static bool _edgeTried;
      /// <summary>连线美术：与主线共用 Resources/UI/Campaign 下的两张贴图（缺图回退白块）。</summary>
        private static Sprite EdgeSolidSprite()
 {
        EnsureEdgeSprites();
    return _edgeSolid;
        }
        private static Sprite EdgeDashSprite()
        {
            EnsureEdgeSprites();
            return _edgeDash;
        }
      private static void EnsureEdgeSprites()
        {
      if (_edgeTried) return;
  _edgeTried = true;
       _edgeSolid = Resources.Load<Sprite>("UI/Campaign/map_edge_ink");
         _edgeDash = Resources.Load<Sprite>("UI/Campaign/line_dash");
            if (_edgeSolid == null || _edgeDash == null)
       {
     var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
       var px = new Color[16];
      for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
       var white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
   if (_edgeSolid == null) _edgeSolid = white;
      if (_edgeDash == null) _edgeDash = white;
    }
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
            // ⚠ NodeKind 里**没有** Boss —— 守关是"节点类型 + isBoss 标记"两件事，
            //   别自己造一个枚举值（曾经造过，编译直接报错）。
      // ⛔⛔ 内容目录**绝不能传 null**：TryBuildFromRun 第 79 行 `if (catalog == null) return false`
         //   ⇒ 之前这里写死 null，秘境里点任何战斗格都只会弹"这一场组不起来（内容目录没准备好）"。
        var catalog = ResolveCatalog();
      if (catalog == null)
       {
        SetStatus("找不到内容目录（ContentCatalogSO），战斗组不起来。");
    return;
      }
   var nodeKind = kind == WanXiang.Campaign.NodeKind.Elite
           ? WanXiang.Campaign.NodeKind.Elite
        : WanXiang.Campaign.NodeKind.Encounter;
      bool ok = BattleRequestFactory.TryBuildFromRun(
   catalog, run, _realm != null ? _realm.Name + "·场地" : "", out req,
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

        /// <summary>
        /// 内容目录：优先用 prefab 上配的，没配就扫Resources（与 CampaignPanel 同一口径）。
        /// ⛔ 找不到就是 null —— 调用方必须处理，别把 null 喂给 TryBuildFromRun（它会直接 false）。
        /// </summary>
        private WanXiang.Fusion.ContentCatalogSO ResolveCatalog()
        {
   if (_contentCatalog != null) return _contentCatalog;
    var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats != null && cats.Length > 0) return cats[0];
   return null;
        }

/// <summary>
        /// 锻炉 = 遗物三选一（设计文档 §8.1「第3 波 遗物三选一 —— 秘境的主要构筑来源」）。
        /// ⛔ **复用主线那张遗物面板**，不要另造一个三选一 UI：
        ///   造第二份就意味着三选一的规则（队伍作用域加权 / 「本队」角标 / 敌方弱化类不算本队）
        ///   要维护两遍，迟早会不一致。
        /// </summary>
        private void OpenRelicPick()
   {
         var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui == null) { SetStatus("界面系统没起来，遗物发不了。"); return; }
   RelicPanel.SetReturnTarget(() =>
     {
          // 回秘境面板：关掉遗物弹层 → 重开秘境面板（面板会重画小地图与已清标记）
      var self = this;
       if (self == null) return;
       var u2 = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (u2 != null) u2.OpenAsync<RealmPanel>(null).Forget();
      });
    ui.OpenAsync<RelicPanel>(null).Forget();
        }

/// <summary>非战斗节点的结算（设计文档 §8.1 奖励梯度）。</summary>
   private void GrantCalm(WanXiang.Campaign.NodeKind kind)
   {
    var run = WanXiang.Run.RunSave.Current;
     if (run == null) return;
            bool gotRelic = false;
switch (kind)
      {
        case WanXiang.Campaign.NodeKind.Shop:   // 窖藏：直接拿星币
                {
       int coin = RealmSession.AddStarCoin(run, 4);
          SetStatus("窖藏：星币 +" + coin);
        }
             break;
            case WanXiang.Campaign.NodeKind.Nest:   // 灵泉：回 35% 生命
        run.HealPending += 35;
            SetStatus("灵泉：下一场战斗我方回复加成 +35%");
             break;
  case WanXiang.Campaign.NodeKind.Forge:  // 锻炉：⭐ 遗物三选一（§8.1 秘境的主要构筑来源）
        // 上一版这里给的是 "+2 灵卵当重铸费折现" —— 等于把秘境最有价值的一格
                // 换成了零钱，玩家进来一趟构筑上一点变化都没有，违背文档 §8.1 的定位。
  gotRelic = true;
     break;
            }
    WanXiang.Run.RunSave.SaveCurrent();
    if (gotRelic) OpenRelicPick();
        }

/// <summary>刷新所有格子的外观（已清 / 当前 / 首领高亮）。</summary>
 private void RefreshNodes()
{
            if (_map == null || _map.Graph == null) return;
   for (int i = 0; i < _nodeViews.Count; i++)
        {
            if (_nodeViews[i] == null) continue;
    //⛔ 用**建时记下的 offset**，不要拿下标当 offset（注释里那个"ActGraph 按层顺序编号"
    //   的假设一旦不成立，刷新就会把 A 格画成 B 格 —— 高亮全乱，且没有任何报错）。
        if (i >= _nodeOffsets.Count) continue;
        int off = _nodeOffsets[i];
   PaintNode(_nodeViews[i], off, _cleared.Contains(off), off == _curNode);
      }
            // 连线也要跟着重画：走过的边从虚线变实线描金
     DrawEdges();
        }

private void ClearNodes()
  {
            for (int i = 0; i < _nodeViews.Count; i++)
if (_nodeViews[i] != null) Destroy(_nodeViews[i]);
            _nodeViews.Clear();
  _nodeOffsets.Clear();
       ClearEdges();
        }

  /// <summary>清掉连线层（节点与连线必须一起清，否则重画会叠出两层虚线）。</summary>
        private void ClearEdges()
        {
   if (_lineLayer == null) return;
    for (int i = _lineLayer.childCount - 1; i >= 0; i--)
      {
        var c = _lineLayer.GetChild(i);
       if (c != null) Destroy(c.gameObject);
        }
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