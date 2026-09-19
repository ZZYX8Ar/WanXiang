// ============================================================================
//  战斗事件流追踪（编辑器工具）
//  ---------------------------------------------------------------------------
//  为什么有这个工具：
//    「某段演出没生效 / 事件被跳过 / 顺序不对」这类问题，靠读代码推理极不可靠
//    （实战教训：我曾连续两轮猜错原因）。把"谁在何时被消费"逐条打出来，
//    一眼就能看出跳过或错序 —— 定位一次成功。
//
//  用法：Unity 菜单 → WanXiang / 战斗 / 事件流追踪（手动模式）
//        输出在 Console；也可复制给协作者排查。
// ============================================================================

#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using WanXiang.Battle.Core;
using WanXiang.Fusion;
using WanXiang.Modules.UI;

namespace WanXiang.EditorTools
{
    public static class BattleFlowTrace
    {
        private const string CatalogPath = "Assets/WanXiang/Config/ContentCatalog.asset";

        [MenuItem("WanXiang/战斗/事件流追踪（手动模式）")]
        public static void Trace()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogSO>(CatalogPath);
            if (catalog == null) { Debug.LogError("[BattleFlowTrace] 找不到内容目录：" + CatalogPath); return; }

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 3) { Debug.LogError("[BattleFlowTrace] 图鉴数据不足（需要 ≥3 只）"); return; }

            var req = new BattleRequest { Title = "事件流追踪" };
            req.Player.Add(all[0]);
            req.Player.Add(all[1]);
            req.Enemy.Add(all[2]);

            // manual = true：复现"玩家手动下令"的推进节奏（正是出问题的那条路径）
            var play = new BattlePlayback(req, true);

            var sb = new StringBuilder();
            sb.Append("[BattleFlowTrace] 构造后：事件数=").Append(play.State.Log.Count)
              .Append(" Awaiting=").Append(play.AwaitingCommand).Append('\n');

            int guard = 0, commands = 0, steps = 0;
            while (!play.Finished && guard++ < 400)
            {
                // 与 BattlePanel.PlayLoop 同一顺序铁律：先播完已产生的事件，再等令
                if (play.State != null && steps < play.State.Log.Count - 1)
                {
                    if (!play.Step()) break;
                    steps++;
                    if (steps <= 20)
                        sb.Append("  step").Append(steps)
                          .Append(" [").Append(play.Current.Kind).Append("] by ")
                          .Append(string.IsNullOrEmpty(play.Current.ActorId) ? "?" : play.Current.ActorId)
                          .Append("（总事件=").Append(play.State.Log.Count).Append("）\n");
                    continue;
                }

                if (play.AwaitingCommand)
                {
                    commands++;
                    sb.Append("▶ 等令#").Append(commands).Append("：")
                      .Append(play.PendingUnit != null ? play.PendingUnit.DisplayName : "?")
                      .Append("（已播=").Append(steps).Append("/").Append(play.State.Log.Count).Append("）\n");
                    if (commands > 4) break;      // 只看前几轮，足够定位"第一次攻击"这类问题
                    play.SubmitCommand((int)SkillType.Active, -1);
                    continue;
                }

                if (!play.Step()) break;
            }

            Debug.Log(sb.ToString());
            Debug.Log("[BattleFlowTrace] 完成：决策点=" + commands + " 步数=" + steps + " 结果=" + play.Result.Outcome);
        }
    }
}
#endif
