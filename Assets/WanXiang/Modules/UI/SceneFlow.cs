// ============================================================================
//  万相 · 场景流程
//  ---------------------------------------------------------------------------
//  场景只有三个，各司其职：
//    Boot     启动：UIBootstrap + InputBootstrap + GameEntry → 打开开始界面
//    Main     主城：开始界面按「开始游戏」进来；主界面 / 节点地图 / 布阵 / 结算都在这
//    Battle2D 战斗：布阵按「出征」进来，BattleStage2D 播一场，播完回 Main 结算
//
//  为什么用静态字段传参而不是事件总线：
//    跨场景传参的生命周期只有"这一跳"，静态字段的语义最直白，
//    而且不依赖任何服务在场景切换时还活着。用完即取（Consume）避免脏数据。
//
//  ⚠ 用同步 LoadScene：本机编辑器的播放器循环不推进，异步加载的回调永远等不到；
//    同步加载当场生效。本作是单机卡牌，场景切换处不追求加载进度条。
// ============================================================================

using UnityEngine.SceneManagement;

namespace WanXiang.Modules.UI
{
    public static class SceneFlow
    {
        public const string BootScene = "Boot";
        public const string MainScene = "Main";
        public const string BattleScene = "Battle2D";

        /// <summary>进战斗场景前写入；战斗场景启动时取用。</summary>
        public static BattleRequest PendingBattle;

        /// <summary>回主城要弹的结算；主城入口取用后清零。</summary>
        public static ResultRequest PendingResult;

        /// <summary>上一场战斗的【我方】异兽 id（结算时生成编队码用；切场景保持）。</summary>
        /// <summary>上一场是不是好友对战（结算时用来跳过历程记录）。</summary>
        public static bool LastWasPvp;

        /// <summary>PvP 打完回主城：不弹结算、直接重开好友对战面板（用户要求）。</summary>
        public static bool PendingPvpReturn;

        /// <summary>上一场我方上场的九宫格格号（与 LastAllyIds 一一对应；编队码站位用）。</summary>
        public static System.Collections.Generic.List<int> LastAllyCells =
            new System.Collections.Generic.List<int>();

        public static System.Collections.Generic.List<string> LastAllyIds =
            new System.Collections.Generic.List<string>();

        /// <summary>上一场战斗的敌方异兽 id（结算掉魂时用；切场景保持）。</summary>
        public static System.Collections.Generic.List<string> LastFoeIds =
            new System.Collections.Generic.List<string>();

        /// <summary>天阙抉择挂起标记：第四幕守关胜利后置位，主城打开时弹三选一。</summary>
        public static bool PendingFinale;

        /// <summary>
        /// 「终局战」标记（登天阙时置位，跨场景保持）：战斗胜利后据此判定**真通关**
        /// （写入 RunState.BeatFinale，解锁无尽模式与难度）。战斗结算时消费并清零。
        /// </summary>
        public static bool IsFinaleBattle;

        /// <summary>
        /// 「登天阙 ⇒ 进入第 5 幕天阙图」的跨场景标记：TrialPanel 置位，
        /// MainSceneEntry 在主城加载完成后消费并直接打开节点图。
        /// （不能在 TrialPanel 里直接切面板：它是 Overlay 层，关闭时栈重算会把新面板一起判掉。）
        /// </summary>
        public static bool EnterFinaleMap;

        /// <summary>开始游戏：进主城。</summary>
        public static void EnterMain()
        {
            PendingResult = null;
            Load(MainScene);
        }

        /// <summary>出征：进战斗场景。</summary>
        public static void EnterBattle(BattleRequest request)
        {
            LastWasPvp = request != null && request.IsPvpMatch;
            PendingBattle = request;
            Load(BattleScene);
        }

        /// <summary>战斗结束（或被撤退）：回主城并带结算。</summary>
        public static void ExitBattle(ResultRequest result)
        {
            PendingBattle = null;
            PendingResult = result;
            Load(MainScene);
        }

        /// <summary>主城入口消费结算：有则弹结算，没有则正常进主界面。</summary>
        public static bool ConsumeResult(out ResultRequest result)
        {
            result = PendingResult;
            PendingResult = null;
            return result != null;
        }

        private static void Load(string sceneName)
        {
            if (!UnityEngine.Application.CanStreamedLevelBeLoaded(sceneName))
            {
                UnityEngine.Debug.LogError(
                    "[SceneFlow] 场景「" + sceneName + "」不在 Build Settings 里，无法切换。" +
                    "请跑菜单 WanXiang/场景/重建场景结构（Boot / Main / Battle2D）。");
                return;
            }
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            UnityEngine.Debug.Log("[SceneFlow] 已切换场景 → " + sceneName);
        }
    }
}
