// ============================================================================
//  万相 · 秘境调试入口（2026-10-16，用户："你先帮我改存档或者加一个测试按钮，让我进入秘境看看"）
//  ---------------------------------------------------------------------------
//  两条路，都放在菜单里，**不需要玩家在主线里刷到第 2 幕**：
//
//   A. 改存档（Edit 模式就能跑，不需要 Play）
//      「① 改存档：跳到第 2 幕 + 清空秘境次数」
//        直接改磁盘上的存档 JSON。改完正常开游戏就能在节点图上遇到秘境。
//      「② 改存档：五个场域逐个看（循环切种子）」
//        每按一次把当前档的 RunSeed 换掉⇒ 抽到的场域跟着换。
//
//   B. 直接进（需要 Play 模式）
//      「③ 直接打开秘境面板（入口态）」
//      「④ 直接跳进秘境（跳过入口抉择）」——最常用的一条，一条龙进内部小地图。
//
//  ⛔ 这些菜单**只改秘境相关字段 + Act/RunSeed**，不动队伍、不动异兽养成，
//     也不写任何"玩家已经通关"之类的假记录。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;   // .Forget() 是 UniTask 的扩展方法（Editor asmdef 已加 UniTask 引用）
using UnityEditor;
using UnityEngine;
using WanXiang.Campaign;
using WanXiang.Modules.UI;

namespace WanXiang.Editor.BattleTool
{
    public static class RealmDebugTool
    {
        // =====================================================================
        //  A. 改存档（Edit 模式即可）
        // =====================================================================

        [MenuItem("万相/调试/秘境/① 改存档：跳到第 2 幕 + 清空秘境次数", priority = 200)]
        public static void PatchSave()
        {
            int n = 0;
            for (int slot = 1; slot <= WanXiang.Run.RunSave.SlotCount; slot++)
            {
                if (!WanXiang.Run.RunSave.Exists(slot))
                {
                    Debug.Log("[秘境调试] 槽位 " + slot + "：空档，跳过");
                    continue;
                }
                var st = WanXiang.Run.RunSave.Load(slot);
                if (st == null) continue;
                int beforeAct = st.Act;
                int beforeSeed = st.RunSeed;
                int beforeEntries = st.RealmEntries != null ? st.RealmEntries.Count : 0;

                st.Act = 2;                                  // 秘境第 2 幕起才出现
                if (st.RunSeed == 0) st.RunSeed = Random.Range(1, 999999);
                if (st.RealmEntries == null) st.RealmEntries = new List<string>();
                st.RealmEntries.Clear();                    // 把次数用满 ⇒ 随时能进
                st.InRealm = false;           // 别把"半个秘境"写进存档
                st.RealmId = null;
                st.RealmSnapshot = null;

                WanXiang.Run.RunSave.Save(st);
                n++;
                Debug.Log("[秘境调试] 槽位 " + slot + "：幕 " + beforeAct + "→2，秘境次数 " +
                          beforeEntries + "→0，种子 " + beforeSeed + "→" + st.RunSeed +
                          "（抽到的场域：" + NameOf(RollName(st)) + "）");
            }
            EditorUtility.DisplayDialog("秘境调试",
                n > 0 ? "已改写 " + n + " 个存档：幕=2，秘境次数清零。\n现在正常开游戏即可在节点图遇到秘境。"
                      : "三个槽位都是空档 —— 先正常开一局新旅程，再回来执行本菜单。",
       "好");
        }

        [MenuItem("万相/调试/秘境/② 改存档：换一个种子（换场域）", priority = 201)]
        public static void CycleSeed()
        {
            var cur = WanXiang.Run.RunSave.Current;
            for (int slot = 1; slot <= WanXiang.Run.RunSave.SlotCount; slot++)
            {
                if (!WanXiang.Run.RunSave.Exists(slot)) continue;
                var st = cur != null && cur.Slot == slot ? cur : WanXiang.Run.RunSave.Load(slot);
                if (st == null) continue;
                int before = st.RunSeed;
                st.RunSeed = Random.Range(1, 999999);
                st.RealmEntries.Clear();
                WanXiang.Run.RunSave.Save(st);
                Debug.Log("[秘境调试] 槽位 " + slot + "：种子 " + before + "→" + st.RunSeed +
             "（场域：" + NameOf(RollName(st)) + "）");
            }
        }

        // =====================================================================
        //  B. 直接进（需要 Play 模式）
        // =====================================================================

        [MenuItem("万相/调试/秘境/③ 直接打开秘境面板（入口态）", priority = 202)]
        public static void OpenPanel()
        {
            if (!EnsurePlay("③ 直接打开秘境面板")) return;
            if (EnsureRun() == null) return;
            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui == null) { Fail("UIBootstrap.UI 为空 —— 场景里没有 UIBootstrap？"); return; }
            ui.OpenAsync<RealmPanel>(null).Forget();
        }

        [MenuItem("万相/调试/秘境/④ 直接跳进秘境（跳过入口抉择）", priority = 203)]
        public static void JumpIn()
        {
            if (!EnsurePlay("④ 直接跳进秘境")) return;
            var run = EnsureRun();
            if (run == null) return;

            var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
            if (ui == null) { Fail("UIBootstrap.UI 为空 —— 场景里没有 UIBootstrap？"); return; }

// 已在秘境里：直接打开秘境路线图（CampaignPanel 注入秘境图）
            if (run.InRealm)
  {
       Debug.Log("[秘境调试] 已在秘境中，直接打开秘境路线图");
      var r2 = RealmCatalog.For(run.RealmId);
    var m2 = RealmMapBuilder.Build(r2, RealmSession.RealmSeedFor(run));
     if (m2 == null || m2.Graph == null) { Fail("建不出秘境图。"); return; }
              CampaignPanel.OnRealmQuitRequested = () =>
                {
        var r = WanXiang.Run.RunSave.Current;
     RealmSession.Exit(r);
          if (r != null) WanXiang.Run.RunSave.SaveCurrent();
     ui.OpenAsync<CampaignPanel>(null).Forget();
            };
    CampaignPanel.OpenAsRealm(m2.Graph);
    return;
            }

      var realm = RealmSession.RollFor(run, RealmSession.RealmSeedFor(run));
            if (realm == null)
          {
       Fail("抽不到场域（幕数不足 / 本局已进满 / 全被去重）—— 菜单 ① 改完存档再来。");
     return;
      }
if (!RealmSession.Enter(run, realm))
    {
 Fail("RealmSession.Enter 失败（本局次数已用完？）");
 return;
      }
            CampaignPanel.PendingCommit = -1;
            WanXiang.Run.RunSave.SaveCurrent();
      Debug.Log("[秘境调试] 已进入秘境：" + realm.Name + "（难度 ×" + realm.EnemyScale +
          "，波数 " + realm.Waves + "，" + (realm.NoQte ? "禁 QTE" : "可格挡") + "）");

  // ★ 内部地图 = CampaignPanel 注入秘境图（复用现成节点地图，零额外地图代码）
        var map = RealmMapBuilder.Build(realm, RealmSession.RealmSeedFor(run));
            if (map == null || map.Graph == null) { Fail("建不出秘境图。"); return; }
            // 注册撤离回调：CampaignPanel 的「回主界面」按钮在秘境模式下变成「撤离」
 CampaignPanel.OnRealmQuitRequested = () =>
 {
        var r = WanXiang.Run.RunSave.Current;
        RealmSession.Exit(r);
          if (r != null) WanXiang.Run.RunSave.SaveCurrent();
        ui.OpenAsync<CampaignPanel>(null).Forget();
            };
  CampaignPanel.OpenAsRealm(map.Graph);
   }

        /// <summary>退出秘境（调试用；正常游玩点面板里的「撤离秘境」）。</summary>
        [MenuItem("万相/调试/秘境/⑤ 强制退出秘境（回滚）", priority = 204)]
        public static void ForceExit()
        {
            if (!EnsurePlay("⑤ 强制退出秘境")) return;
            var run = WanXiang.Run.RunSave.Current;
            if (run == null) { Fail("当前没有旅程。"); return; }
            if (!RealmSession.Exit(run)) { Debug.Log("[秘境调试] 当前不在秘境里，无需回滚。"); return; }
      WanXiang.Run.RunSave.SaveCurrent();
Debug.Log("[秘境调试] 已回滚到原路线节点。");
        }

        // =====================================================================
        //  工具
        // =====================================================================

        private static bool EnsurePlay(string who)
        {
            if (EditorApplication.isPlaying) return true;
            Fail(who + " 需要在 **Play 模式**下用（面板是运行期对象）。");
        return false;
        }

        private static WanXiang.Run.RunState EnsureRun()
     {
            var run = WanXiang.Run.RunSave.Current;
            if (run != null)
  {
          PrepareRun(run);
  return run;
        }
        // 没选档：优先继承槽位 1，其次新开一局（都能立刻进秘境看）
            var loaded = WanXiang.Run.RunSave.Load(1);
 run = loaded != null ? WanXiang.Run.RunSave.ContinueWith(loaded)
    : WanXiang.Run.RunSave.StartNew(1);
        PrepareRun(run);
            Debug.Log("[秘境调试] 没有当前旅程，已自动" + (loaded != null ? "继承槽位 1" : "新开槽位 1"));
            return run;
        }

        /// <summary>把当前旅程调到"能进秘境"的状态（幕数 / 种子 / 次数）。</summary>
        private static void PrepareRun(WanXiang.Run.RunState run)
  {
            if (run.Act < 2) run.Act = 2;
         if (run.RunSeed == 0) run.RunSeed = Random.Range(1, 999999);
   if (run.RealmEntries == null) run.RealmEntries = new List<string>();
            run.EnsureMetaSnapshot();      // 组阵要读本局快照，别让第一次进战斗时再去拍
   }

        private static RealmDef RollName(WanXiang.Run.RunState run)
        {
      return run == null ? null : RealmSession.RollFor(run, RealmSession.RealmSeedFor(run));
      }

        private static string NameOf(RealmDef r)
        {
            return r != null ? r.Name : "(无)";
}

private static void Fail(string msg)
  {
      Debug.LogError("[秘境调试] " + msg);
            // ⛔⛔ **Play 模式下绝不能弹模态框**（2026-10-16 踩过）：
            //   EditorUtility.DisplayDialog 是**阻塞式**的，它会卡住编辑器主线程 ——
            //   表现是"Unity 整个卡死、MCP 全部超时、Editor.log 停在那一行"。
            //   而这里的失败原因（UIBootstrap.UI 为空）恰恰只在 Play 模式出现 ⇒ 必然踩中。
            //   Play 模式一律只打日志；Edit 模式（改存档那两个菜单）才弹框。
            if (!Application.isPlaying) EditorUtility.DisplayDialog("秘境调试", msg, "好");
        }
    }
}