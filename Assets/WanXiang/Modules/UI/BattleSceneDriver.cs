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

            var play = new BattlePlayback(req);

            if (_stage == null) _stage = FindObjectOfType<BattleStage2D>();
            if (_stage != null)
            {
                _stage.Build(play.State, _background, _sprites);
                Debug.Log("[BattleSceneDriver] 舞台已搭建：" + req.Title);
            }
            else
            {
                Debug.LogWarning("[BattleSceneDriver] 场景里没有 BattleStage2D，战斗只有 HUD、没有立绘。");
            }

            var ui = UIBootstrap.UI;
            if (ui == null)
            {
                Debug.LogError("[BattleSceneDriver] UIBootstrap.UI 为空，无法打开战斗 HUD。" +
                               "请从 Boot 场景开始游戏。");
                return;
            }

            await ui.OpenAsync<BattlePanel>(new BattleSceneContext
            {
                Play = play,
                Stage = _stage,
                Request = req,
            });
        }
    }
}
