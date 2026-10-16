// ============================================================================
//  Panel_Realm —— 秘境**入口抉择**（2026-10-16《精英首领与秘境设计 v1.0》）
//  ---------------------------------------------------------------------------
//  ★★★ 这个面板现在**只做一件事**：把场域的「场地效果 / 收益 / 代价」三行摊开，
//     让玩家进之前就看清（设计文档 5.3：「不允许进去了才知道有什么」），
//     然后让玩家选「进入 / 暂不进入 / 换一个」。
//
//  ★★ 秘境**内部**不再有自己的地图 —— 用户 2026-10-16：
//     "这个秘境就是和节点地图一模一样啊，直接复用就完事了…你看一下第五幕，
//      不也是只有5个线性的节点吗…你直接把这些秘境想象成特殊的幕不就行了吗"
//
//     照这个思路落地：秘境内部图 = 一张 <see cref="WanXiang.Campaign.ActGraph"/>
//     （RealmMapBuilder 产出，**与天阙图同类型**），直接
//     <c>CampaignPanel.OpenAsRealm(graph)</c> 打开 ——
//     排布 / 连线 / 三态配色 / 落印 / 选中 / 出征 全部是主线同一套代码，一个字没改。
//
//     与主线节点地图的行为差异只有三处（都在 CampaignPanel 里、只有三行）：
//     ① 图从参数来，不走 SolarTermGraph.BuildRoute
//     ② 通过节点**不推进主线 Act**（秘境 offset 与 act*1000+offset 不是一套命名空间）
//     ③ 「回主界面」那个按钮变成「撤离秘境」（回滚场域状态 + 回原节点）
//
//  ★ 本面板**不改任何战斗数据**：进/出秘境一律走 <see cref="RealmSession"/>。
// ============================================================================

using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Campaign;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    /// <summary>
    /// 秘境入口抉择。<b>不画地图</b> —— 内部地图复用 <see cref="CampaignPanel"/>（注入外部图）。
 /// </summary>
    [UIPanel("Panel_Realm", Layer = UILayer.Popup, CachePolicy = UICachePolicy.Transient,
        // FullScreen=false：这是"抉择框"，不是路线图；真正的路线图是 CampaignPanel。
        FullScreen = false, CloseOnMaskClick = false)]
 public sealed class RealmPanel : UIPanelBase
    {
   [Header("标题 / 状态")]
        [SerializeField] private TMP_Text _tmpTitle;    // Tmp_Title
     [SerializeField] private TMP_Text _tmpStatus;        // Tmp_Status

        [Header("场域三行（效果 / 收益 / 代价）")]
      [SerializeField] private RectTransform _infoRoot;   // Root_RealmInfo
        [SerializeField] private TMP_Text _tmpRealmName;     // Tmp_RealmName
        [SerializeField] private TMP_Text _tmpHazard;        // Tmp_Hazard
        [SerializeField] private TMP_Text _tmpBoon;       // Tmp_Boon
        [SerializeField] private TMP_Text _tmpCost;          // Tmp_Cost

        [Header("按钮")]
        [SerializeField] private Button _btnEnter;       // Btn_Enter  进入秘境
        [SerializeField] private Button _btnLeave;          // Btn_Leave  暂不进入（+1 灵卵）
        [SerializeField] private Button _btnReroll;         // Btn_Reroll 换一个（免费 1 次）

        private RealmDef _realm;
        private int _rerollUsed;

  // =====================================================================
        //  生命周期
        // =====================================================================

    protected override void OnCreate()
        {
       if (_btnEnter != null) _btnEnter.onClick.AddListener(OnEnterClicked);
         if (_btnLeave != null) _btnLeave.onClick.AddListener(OnLeaveClicked);
            if (_btnReroll != null) _btnReroll.onClick.AddListener(OnRerollClicked);
 }

      protected override UniTask OnOpenAsync(object payload)
        {
    RealmHookRegistrar.EnsureRegistered();   // 幂等：秘境钩子挂进战斗核心
        EnsureQuitHook();         // ★ 让 CampaignPanel 的「回主界面」按钮在秘境模式下变成「撤离」

            // ⚠ 面板可能被"已经在秘境里"时打开（战后回图 / 调试菜单）。
            //   那不该再走入口抉择 —— 直接打开秘境路线图。
            var run = WanXiang.Run.RunSave.Current;
            if (run != null && run.InRealm)
            {
                var r2 = RealmCatalog.For(run.RealmId);
                var m2 = RealmMapBuilder.Build(r2, RealmSession.RealmSeedFor(run));
                if (m2 != null && m2.Graph != null)
                {
                    CampaignPanel.OnRealmQuitRequested = () => RealmFlow.QuitNow(r2 != null ? r2.Id : null);
                    CampaignPanel.OpenAsRealm(m2.Graph);
                    CloseSelf();
                }
                return UniTask.CompletedTask;
            }

   _rerollUsed = 0;
       _realm = RollRealm();
       if (_tmpTitle != null) _tmpTitle.text = "秘境 · 抉择";
            FillRealmInfo(_realm);
       RefreshStatus();
      return UniTask.CompletedTask;
        }

  // =====================================================================
        //  入口流程
        // =====================================================================

        private RealmDef RollRealm()
  {
            var run = WanXiang.Run.RunSave.Current;
            // ⛔ 种子必须用共享的那个（RealmSession.RealmSeedFor）——
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
         SetStatus("换一个需要 1 枚灵卵（免费次数已用完）。");
             return;
        }
        run.Eggs -= 1;
         _rerollUsed++;
            }
    _realm = RollRealm();
     FillRealmInfo(_realm);
     RefreshStatus();
   WanXiang.Run.RunSave.SaveCurrent();
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
    CloseSelf();
     OpenPanelAsync<CampaignPanel>(null).Forget();
        }

        /// <summary>
        /// 「进入秘境」：进场 → 打开秘境路线图（CampaignPanel 注入秘境图）。
        /// 「进入秘境」：进场 → **转场** → 打开秘境路线图（CampaignPanel 注入秘境图）。
        /// ⛔ 转场必须在"盖住屏幕"时完成界面切换，否则玩家会看到面板"啪"地变形。
/// </summary>
        /// <summary>
        /// 点「进入秘境」。★ 走**与调试按钮完全相同**的 <see cref="RealmFlow.Enter"/> ——
        ///   转场（粒子合拢 → 场域名 + 艺术字 → 拨开）、进场、注册撤离回调全在那一处。
        ///   两个入口各写一遍，就一定会出现"测试的能进、正式的没转场"这类分叉。
        /// </summary>
        private void OnEnterClicked()
        {
            var run = WanXiang.Run.RunSave.Current;
            if (run == null || _realm == null) return;

            // 入口面板自己要关掉：转场的 onSwitch 会打开 CampaignPanel，
            // 而入口面板是 Popup 层，不关会盖在新地图上。
            var realm = _realm;
            RealmFlow.Enter(realm, onAfterEnter: null);
            CloseSelf();
        }


  // =====================================================================
  //  撤离（由 CampaignPanel 在秘境模式下点「回主界面」那个按钮时回调）
        // =====================================================================

        /// <summary>注册撤离回调（幂等）。回滚语义全在 <see cref="RealmSession.Exit"/>。</summary>
        public static void EnsureQuitHook()
        {
   CampaignPanel.OnRealmQuitRequested = DoQuit;
   }

private static void DoQuit()
        {
            var run = WanXiang.Run.RunSave.Current;
RealmSession.Exit(run);              // ★ 回滚场域状态 + 还原原节点
     if (run != null) WanXiang.Run.RunSave.SaveCurrent();
   // ⚠ 不要 OpenAsync 重新打开 CampaignPanel —— 撤离入口（OnBackToHome）会
            //   用**同一个实例**原地重建主线图。"关掉再打开"会被 UISystem 静默忽略
            //   （Closing 中的实例再次请求打开 ⇒ 直接 return），表现是**画面一片空白且无报错**。
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
if (_tmpRealmName != null) _tmpRealmName.text = r.Name;
            if (_tmpHazard != null) _tmpHazard.text = "场地：" + RealmText.Hazard(r);
            if (_tmpBoon != null) _tmpBoon.text = "收益：" + r.Boon;
            if (_tmpCost != null) _tmpCost.text = "代价：" + r.Cost;
  }

        private void RefreshStatus()
        {
            if (_tmpStatus == null) return;
            var run = WanXiang.Run.RunSave.Current;
            int left = run != null ? RealmSession.EntriesLeft(run) : 0;
            _tmpStatus.text = "本局还可进入 " + left + " 次" +
              (_rerollUsed > 0 ? "　·　已用掉 1 次免费更换" : "");
        }

        private void SetStatus(string msg)
        {
            if (_tmpStatus != null) _tmpStatus.text = msg;
   }
    }
}