// ============================================================================
//  万相 · 战斗场景驱动
//  ---------------------------------------------------------------------------
//  挂在 Battle2D 场景的一个空物体上。职责：
//    ① 从 SceneFlow 取本场的 BattleRequest（没有则按内容目录组一场默认的）
//    ② 一次跑完这场战斗（BattlePlayback），把结果交给场景里的 BattleStage2D 摆台
//    ③ 打开 Panel_Battle 作为 HUD（战斗画面由舞台承担，HUD 不再画一遍单位）
//
//  与"主城里直接开 Panel_Battle"的分工：
//    · 主城路径（旧）：Panel_Battle 自己画单位（Root_Units 里的 Image）
//    · 战斗场景路径（本文件）：单位交给 BattleStage2D（世界空间立绘 + 死亡下沉 + 伤害数字），
//      Panel_Battle 只当 HUD。两条路径共用同一个 BattlePlayback 与同一套 HUD 刷新代码。
// ============================================================================

using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Battle.Presentation;
using WanXiang.Framework.Boot;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    /// <summary>传给 Panel_Battle 的"场景舞台"上下文。</summary>
    public sealed class BattleSceneContext
    {
        public BattlePlayback Play;
        public BattleStage2D Stage;
        public BattleRequest Request;
    }

    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class BattleSceneDriver : MonoBehaviour
    {
        [Header("数据引用")]
        [SerializeField] private ContentCatalogSO _contentCatalog;
        [SerializeField] private SpriteCatalog _sprites;

        [Header("场景舞台")]
        [SerializeField] private BattleStage2D _stage;
        [SerializeField] private Sprite _background;

        [Header("兜底")]
        [Tooltip("没有入参且内容目录缺失时，是否直接回主城（避免卡死在战斗场景）。")]
        [SerializeField] private bool _returnToMainOnFailure = true;
        /// <summary>回合制手动模式：开启后我方每个单位行动前等下令（与操作区配套）。</summary>
        [SerializeField] private bool _manualBattle = true;   // 回合制手动（战斗场景由本组件驱动）

        private async UniTaskVoid Start()
        {
            var req = SceneFlow.PendingBattle;

            if (req == null && _contentCatalog != null)
            {
                BattleRequestFactory.TryBuild(_contentCatalog, "遭遇战 · 天阙", "无天时", 20260914UL, out req);
                Debug.Log("[BattleSceneDriver] 没有跨场景入参，已按内容目录组了一场默认战斗。");
            }

            if (req == null || req.Player.Count == 0)
            {
                Debug.LogError("[BattleSceneDriver] 拿不到可用的战斗入参。");
                if (_returnToMainOnFailure) SceneFlow.ExitBattle(new ResultRequest { Summary = "无法开战" });
                return;
            }

            // ★ PvP（好友对战）：强制全自动回放 —— 整场模拟一次跑完、逐事件播放，
            //   不进任何「等令」逻辑（手动模式下异兽会卡在第一个决策点不动，
            //   且 RefreshActionBar 会把隐藏的手动 UI 重新亮出）。等价于「复用战斗场景」，
            //   符合用户诉求：一进来就自动打。
            bool pvpManual = _manualBattle && !req.IsPvpMatch;
            var play = new BattlePlayback(req, pvpManual);
            Debug.Log("[BattleSceneDriver][调试] 建回放 manual=" + pvpManual + " (PvP=" + req.IsPvpMatch + ") → PvP 应为全自动回放");

            if (_stage == null) _stage = FindObjectOfType<BattleStage2D>();
            if (_stage != null)
            {
                // 舞台搭建失败不该把整条流程卡死在战斗场景里：HUD 与结算仍然要走完
                try
                {
                    _stage.Build(play.State, _background, _sprites);
                    Debug.Log("[BattleSceneDriver] 舞台已搭建：" + req.Title);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[BattleSceneDriver] 舞台搭建失败，本场只有 HUD：" + ex);
                    _stage = null;
                }
            }
            else
            {
                Debug.LogWarning("[BattleSceneDriver] 场景里没有 BattleStage2D，战斗只有 HUD、没有立绘。");
            }

            var ui = UIBootstrap.UI;

            // ---- 兜底启动 UI 系统 ----
            // 直接 Play Battle2D 场景（开发时常干的事）时，Boot 场景的 [UIRoot] 不在，
            // UI 系统就是空的 —— 以前这里只打一条错误日志然后 return，结果是
            // "舞台有立绘、但没有战斗 HUD"，很像"战斗界面没做"。现在自己起一套：
            // 有重复保护（Boot 流程进来时 UIBootstrap 还在，不会重复建）。
            if (ui == null)
            {
                // ⚠ 不要在这里 await：AddComponent 会**同步**触发 Awake，
                //   所以建完 UIBootstrap 后 UIBootstrap.UI 立刻可用。
                //   多一个 await 反而会在"帧不推进"的环境（自动化/编辑器预览）里卡住整条流程。
                var boot = new GameObject("[UIRoot]");
                boot.AddComponent<UIBootstrap>();
                var input = new GameObject("[InputBootstrap]");
                input.AddComponent<WanXiang.Framework.Boot.InputBootstrap>();
                ui = UIBootstrap.UI;
                Debug.Log("[BattleSceneDriver] 检测到 UI 系统未启动，已临时自建（单独 Play 战斗场景的情况）。");
            }

            if (ui == null)
            {
                Debug.LogError("[BattleSceneDriver] UI 系统仍不可用，无法打开战斗 HUD。");
                return;
            }

            // ---- 清掉主城侧界面，别让它们叠在战斗 HUD 下面 ----
            // Home 是 Main 层常驻面板，出征时没人关它；它和 BattlePanel 同层，
            // HUD 底图一旦有半透明区域就会透出主城 —— 玩家看到的就是"两个界面叠一起"。
            // 战斗是独占画面的场景，这里整层清空是正确语义。
            ui.CloseAll();

            await ui.OpenAsync<BattlePanel>(new BattleSceneContext
            {
                Play = play,
                Stage = _stage,
                Request = req,
            });
        }
    }
}
