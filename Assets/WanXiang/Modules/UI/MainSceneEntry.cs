// ============================================================================
//  万相 · 主城场景入口
//  ---------------------------------------------------------------------------
//  挂在 Main 场景的一个空物体上。进来时做一件事：
//    · 如果是从战斗场景回来的（SceneFlow 里有结算）→ 弹结算面板
//    · 否则（从开始界面进来 / 直接跑这个场景调试）→ 开主界面
//
//  为什么不是"谁发起谁负责开界面"：发起方（开始界面 / 战斗场景）在切换的那一刻
//  已经随旧场景销毁了，跨场景的界面收尾只能由**新场景的入口**接手。
// ============================================================================

using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.Boot;
using WanXiang.Modules.UI;

namespace WanXiang.Modules.Boot
{
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class MainSceneEntry : MonoBehaviour
    {
        private async UniTaskVoid Start()
        {
            var ui = UIBootstrap.UI;
            if (ui == null)
            {
                Debug.LogError("[MainSceneEntry] UIBootstrap.UI 为空 —— Boot 场景没起来，" +
                               "或本场景被单独运行。请从 Boot 场景开始。");
                return;
            }

            // ★ PvP 打完回主城：不弹结算面板，直接垫主界面 + 叠好友对战面板（用户要求）。
            //   必须放在 ConsumeResult 之前（PvP 回来时 PendingResult 本来就是 null）。
            if (SceneFlow.PendingPvpReturn)
            {
                SceneFlow.PendingPvpReturn = false;
                ui.Close<StartPanel>();
                await ui.OpenAsync<HomePanel>();   // 底下垫主界面，关掉对战面板不是空屏
                await ui.OpenAsync<PvpPanel>();    // 再叠好友对战面板
                Debug.Log("[MainSceneEntry][调试] PvP 结束 → 已直接回到好友对战面板（无结算）");
                return;
            }

            // ★ 重新挑战：战斗里点「重新挑战」→ 回同一节点的编队界面重新布阵重新打。
            //   放在 ConsumeResult 之前：重新挑战不带回结算（result 本来就是 null）。
            if (SceneFlow.PendingRechallengeNode != null)
            {
                var node = SceneFlow.PendingRechallengeNode;
                SceneFlow.PendingRechallengeNode = null;
                SceneFlow.PendingBattleNode = node;   // 重新出征时还能再回到这个节点
                // ★ 重新挑战修复（2026-09-29）：清掉"点节点时写的 PendingCommit"，
                //   否则下面打开 CampaignPanel 会把它当成"已通过"落地 ， 节点被误判通关。
                //   真正的通过要等【重新出征打赢】后回地图才落地（见 FormationPanel.OnDeployClicked 重写 PendingCommit）。
                CampaignPanel.PendingCommit = -1;
                ui.Close<StartPanel>();
                await ui.OpenAsync<CampaignPanel>();       // 底下垫节点地图
                await ui.OpenAsync<FormationPanel>(node);  // 再叠编队界面（带节点上下文）
                Debug.Log("[MainSceneEntry][调试] 重新挑战 ， 已回到编队界面（节点：" +
                          (node != null ? node.Title : "null") + "）");
                return;
            }

            if (SceneFlow.ConsumeResult(out var result))
            {
                // ⚠ 旧的天阙触发条件（Act==4 && NodeOffset==11）已废弃并删除：
                //   11 是"4 层 × 3 格"时代的遗留格号，与现在的 12 层图对不上；
                //   而且幕推进会把 NodeOffset 重置为 -1 ， 条件永远不成立 ， 天阙永不触发。
                //   现在改为：**幕推进到第 5 幕的那一刻**置位 PendingFinale（见 CampaignPanel）。

                await ui.OpenAsync<ResultPanel>(result);
                Debug.Log("[MainSceneEntry] 已弹出战斗结算。");
                return;
            }

            // ⚠ 开始界面必须在这里关掉：StartPanel 与 Home 同在 Main 层，
            //   它一直 Opened 就会一直"遮挡"Home ， Home 永远 Paused，
            //   表现就是「从节点地图返回主城，主界面是灰的/点不动」。
            ui.Close<StartPanel>();

            // ★★ 登天阙：刚从天阙抉择选了"登天阙" ， 直接在**第 5 幕天阙图**开局，
            //    而不是进主界面（用户要求：登天后直接进入第五幕节点地图）。
            //    放在这里是因为：场景已加载完成、UI 栈干净，不会像"Overlay 面板内切面板"那样被栈重算判掉。
            Debug.Log("[MainSceneEntry] 进入主城：EnterFinaleMap=" + SceneFlow.EnterFinaleMap +
                      " Act=" + (WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : -1));
            if (SceneFlow.EnterFinaleMap)
            {
                SceneFlow.EnterFinaleMap = false;
                // ★★ 关键：先关掉"残留的主界面"！
                //    实测（日志）：场景被切了两次 ， MainSceneEntry 跑了两次，
                //    第一次进 HomePanel 分支把主界面打开了；第二次虽然成功打开节点图，
                //    但主界面还在栈里 ， 下一次**栈重算**就把节点图判掉（2 帧后 active=False）。
                // ★★★ 用最硬的办法：**清空整个面板栈**再打开节点图。
                //     实测：只关 HomePanel 不够 —— 场景被切两次 ， MainSceneEntry 跑两次，
                //     第一次遗留的面板会让 UISystem 的"栈重算"把刚打开的节点图判掉
                //     （日志：打开成功但 2 帧后 active=False）。
                ui.CloseAll();
                await UniTask.DelayFrame(2);       // 等 CloseAll 引发的栈重算彻底跑完
                Debug.Log("[MainSceneEntry] 已清空面板栈，准备打开天阙图");
                var cp = await ui.OpenAsync<CampaignPanel>();
                Debug.Log("[MainSceneEntry] 登天阙 ， 已请求节点地图，结果=" +
                          (cp != null ? ("成功 " + cp.name) : "null（打开失败）"));
                // ⚠ 打开后立刻复查它是否还活着（栈重算可能把它关掉）
                await UniTask.DelayFrame(2);
                Debug.Log("[MainSceneEntry] 2 帧后 CampaignPanel=" +
                          (cp != null ? ("active=" + cp.gameObject.activeInHierarchy) : "null"));
                if (cp == null || !cp.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning("[MainSceneEntry] 节点图被关闭了 → 改为进主界面（至少不让画面空着）");
                    await ui.OpenAsync<HomePanel>();
                }
                return;
            }

            // ★ 转场进主界面（2026-10-11 用户："从开始界面进入主界面太生硬，
            //   加一点转场，比如一点或几滴水墨铺开"）。
            //   顺序：墨团从中心铺开盖住屏幕 → 关闭开始界面并打开主界面 → 墨团继续放大淡出。
            // ★ 预热节点地图/幕开场（2026-10-14）：趁这次转场遮盖期把两个大面板先建好，
            //   把"首次进节点地图会卡一下"的实例化成本挪到看不见的时候（详见方法注释）。
            PrewarmFirstMapPanelsAsync();
            {
                var run0 = WanXiang.Run.RunSave.Current;
                int act0 = run0 != null ? Mathf.Clamp(run0.Act, 1, 5) : 1;
                // ⚠ 不传 coverSprite ， 用按幕的纯色纸底（用户 2026-10-12：
                //   "从开始界面到主界面保留之前的黑色就行"）。
                //   背景贯穿只留给「主界面→节点地图」那条（那里两张底图才需要接）。
                await UiTransition.PlayAsync(UiTransition.Kind.Ink, act0, () =>
                {
                    ui.Close<StartPanel>();
                    _ = ui.OpenAsync<HomePanel>();
                }, null, 2.4f, "Start2Home");   // ⚠ 独立标识：与 Home→Map 链路区分
            }
            Debug.Log("[MainSceneEntry] 已进入主界面（带水墨转场，开始界面已关闭）。");
        }

        /// <summary>
        /// ★ 预热「节点地图 + 幕开场」两个大面板（2026-10-14）。
        ///
        /// 用户实测："从主界面到节点地图的这个转场第一次会卡，后面就流畅了"。
        /// 播放剖面定位：首次打开的停顿 ≈ 节点地图面板的打开流程 + 内容首帧渲染/贴图上传
        /// （合计 ~1s，第二次起被缓存/已上传，所以流畅）。
        ///
        /// 这里趁 Start→主界面 水墨转场把屏幕盖满的窗口（约 2 秒全遮盖）：
        ///  · 节点地图面板 → **完整彩排**（真开一遍再关掉，GhostSilent 静默，不播音/不触发开场/不写档）
        ///    —— 把首次打开的全部成本藏进转场；玩家第一次真进图 = 热路径。
        ///  · 幕开场面板 → 轻量预热（实例化 + 隐藏激活）——它自身的冷成本仅 ~0.1s，
        ///    且首次打开已被 CampaignPanel.DeferredIntroAsync 挪进遮盖期。
        ///
        /// ⚠ 只在"从开始界面进主城"这条路上调（转场遮盖期）；战后回城无转场遮盖、不调。
        /// </summary>
        private static async UniTaskVoid PrewarmFirstMapPanelsAsync()
        {
            var ui = UIBootstrap.UI;
            if (ui == null) return;

            await UniTask.Delay(250);   // 等水墨转场遮盖层盖满（0.12s 起生效，留裕量）
            int actNow = WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : 1;
            var strokePaths = new System.Collections.Generic.List<string>();   // 笔顺帧路径（彩排销毁前收集）
            if (ui.Get<CampaignPanel>() == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await ui.GhostOpenCloseAsync<CampaignPanel>();
                Debug.Log("[MainSceneEntry] 彩排 Panel_Campaign 完成 " + sw.ElapsedMilliseconds + "ms");
            }
            if (ui.Get<ActIntroPanel>() == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var intro = await ui.PrewarmAsync<ActIntroPanel>();
                // ★ 大标题字形（150 号 simkai SDF，动态图集）也在这里先烘好 ——
                //   否则首次真打开时会在渲染那一帧现烘、卡 ~0.5s（实测）。
                //   ⚠ 笔顺帧路径也在这里收集：下面的彩排会把本面板销毁（Transient），之后拿不到实例。
                if (intro != null)
                {
                    intro.WarmTitleGlyphs();
                    intro.AppendStrokeSpritePaths(actNow, strokePaths);
                }
                // ★ 彩排开场（与节点地图同理）：它的**首次渲染那一帧**实测要 ~0.4s
                //   （字形材质首编译 / 首帧布局与贴图上传），真开时正落在转场"拨开退场"里。
                //   这里先真开一遍、渲染出来、立刻关掉（静默、不记账、Play 遇关闭干净中止）。
                await ui.GhostOpenCloseAsync<ActIntroPanel>();
                Debug.Log("[MainSceneEntry] 彩排 Panel_ActIntro 完成 " + sw.ElapsedMilliseconds + "ms");
            }

            // ★ 写字笔顺帧的**纹理上传**预热（2026-10-14）：写字演出是逐帧换图，
            //   贴图首次显示要在渲染那一帧上传，首跑前几笔会一顿一顿（实测前 7 笔各卡 10~60ms）。
            //   趁本转场还在遮盖期，把当前幕标题的笔顺帧先上传烤热。
            {
                if (strokePaths.Count == 0)
                {
                    var ip = ui.Get<ActIntroPanel>();
                    if (ip != null) ip.AppendStrokeSpritePaths(actNow, strokePaths);
                }
                var sprites = new System.Collections.Generic.List<Sprite>(strokePaths.Count);
                for (int i = 0; i < strokePaths.Count; i++)
                {
                    var sp = Resources.Load<Sprite>(strokePaths[i]);
                    if (sp != null) sprites.Add(sp);
                }
                await UiTransition.WarmSpriteUploadsAsync(sprites);
                Debug.Log("[MainSceneEntry] 笔顺帧纹理预热 " + sprites.Count + " 张（幕" + actNow + "）");
            }
        }
    }
}
