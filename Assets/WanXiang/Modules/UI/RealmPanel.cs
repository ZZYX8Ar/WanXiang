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
            // ★ 先落文字色，再填内容（顺序无所谓，但别漏 —— 漏了就是截图里那种浅粉压米色）
            ApplyReadableInk();
            FillRealmInfo(_realm);
       RefreshStatus();
      return UniTask.CompletedTask;
        }

  // =====================================================================
        //  入口流程
        // =====================================================================

        /// <summary>
        /// 调试旁路：忽略"第 1 幕不出秘境"和"本局已进过 2 次"两条限制。
        /// <para>由节点地图的「秘境测试」按钮置位，面板消费一次后自动复位。</para>
        /// <para>⛔ 只放宽**判定**，不修改 <c>RunState</c> 的任何字段 ——
        ///   早先那版调试入口是"临时抬高 run.Act / 清空 RealmEntries 再还原"，
        ///   一旦某条撤离路径漏了还原，玩家的幕数会被永久改掉。</para>
        /// </summary>
        public static bool DebugBypass;

        private RealmDef RollRealm()
        {
            var run = WanXiang.Run.RunSave.Current;
            // ⛔ 种子必须用共享的那个（RealmSession.RealmSeedFor）——
            //   揭晓处与面板处各写一份公式会出现"揭晓说苍泽、面板抽出烬岭"的错位。
            // ★ 2026-10-17：必须把 `_rerollUsed` 带进种子，并**排除当前这个场域** ——
            //   否则"换一个"是空操作（种子只由 RunSeed 决定，Roll 是纯函数）。
            //   两个保险都要：换种子让结果变，排除当前保证一定不同（换种子可能撞回同一个）。
            string cur = (_rerollUsed > 0 && _realm != null) ? _realm.Id : null;
            return RealmSession.RollFor(run,
                RealmSession.RealmSeedFor(run, _rerollUsed), DebugBypass, cur);
        }

        /// <summary>
        /// 把本面板的文字色**按 sRGB 重新落一遍**（2026-10-17）。
        /// <para>为什么必须显式设：prefab 里存的是旧数值（深褐 0.165 / 朱砂 0.784），
        /// 在 **Linear** 色彩空间下会被提亮成"中褐"和"浅粉" —— 实机截图里
        /// 「场地/收益/代价」三行几乎读不清（浅粉字压在米色纸上）。</para>
        /// <para>⛔ 不要改 prefab 里的原始数值去凑：那是在和 gamma 曲线猜数字。
        ///   统一走 <see cref="UiInk.Srgb"/>，把"屏幕上想要什么颜色"写清楚。</para>
        /// </summary>
        private void ApplyReadableInk()
        {
            var inkMain = UiInk.Srgb(0.16f, 0.13f, 0.10f);   // 墨（正文）—— 压在**米色卡片**上
            var inkSoft = UiInk.Srgb(0.42f, 0.38f, 0.34f);   // 淡墨（次要说明）
            var cinnabar = UiInk.Srgb(0.66f, 0.17f, 0.14f);  // 朱砂（场地/警示）
            // ⚠ 标题与状态行压在**深色外框**上，必须用**亮**字 ——
            //   这是"同一块面板里两种底色"的坑：只按"面板是浅色"整体调深，
            //   标题就会变成深墨压深底（实测直接消失）。
            var paperLight = UiInk.Srgb(0.93f, 0.90f, 0.82f);
            var paperDim = UiInk.Srgb(0.72f, 0.68f, 0.60f);

            void Paint(TMPro.TMP_Text t, Color c) { if (t != null) t.color = c; }

            Paint(_tmpTitle, paperLight);        // 深框上的标题
            Paint(_tmpStatus, paperDim);         // 深框上的"还可进入 N 次"
            Paint(_tmpRealmName, inkMain);       // 以下都在米色卡片上
            Paint(_tmpBoon, inkMain);
            Paint(_tmpHazard, cinnabar);
            Paint(_tmpCost, inkSoft);
            if (_btnEnter != null) Paint(_btnEnter.GetComponentInChildren<TMPro.TMP_Text>(true), inkMain);
            if (_btnLeave != null) Paint(_btnLeave.GetComponentInChildren<TMPro.TMP_Text>(true), inkMain);
            if (_btnReroll != null) Paint(_btnReroll.GetComponentInChildren<TMPro.TMP_Text>(true), inkMain);
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
            }

            // ★★ `_rerollUsed` 必须**每次**都 ++（2026-10-17 修）：
            //   旧版只在"收费"那一支里 ++，于是**第一次免费换时它还等于 0** ——
            //   而种子与"排除当前场域"都以它为据 ⇒ 免费那一次抽出来的还是同一个场域，
            //   玩家点了"换一个"界面纹丝不动（实测）。计费门槛语义不变（第 2 次起收费）。
            _rerollUsed++;

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
            // ⛔ 只在**成功**时关：失败还关掉，玩家会看到"面板消失但什么都没发生"。
            var realm = _realm;
            bool bypass = DebugBypass;
            DebugBypass = false;              // ★ 消费一次就复位，别让下一次正常进入也走旁路
            if (RealmFlow.Enter(realm, onAfterEnter: null, bypassLimits: bypass)) CloseSelf();
            else SetStatus("进不去秘境（本局进入次数已用完）。");
        }


    // =====================================================================
        //  撤离
        // =====================================================================

        // ⛔ 这里**不再**注册/劫持 CampaignPanel.OnRealmQuitRequested（2026-10-17 删）。
        //   旧版的 `EnsureQuitHook()` 把它指向一个**不走转场**的 DoQuit，而
        //   `RealmFlow.Enter` 又把它指向走转场的 QuitNow —— 两处赋值靠"谁后执行"，
        //   是本项目已经踩过的坑（见记忆：多入口同一件事不要靠替换静态回调）。
        //   现在撤离只有唯一入口 `RealmFlow.QuitNow`，由 RealmFlow.Enter 自己登记。

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