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

using Cysharp.Threading.Tasks;
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
        /// 「登天阙 ， 进入第 5 幕天阙图」的跨场景标记：TrialPanel 置位，
        /// MainSceneEntry 在主城加载完成后消费并直接打开节点图。
        /// （不能在 TrialPanel 里直接切面板：它是 Overlay 层，关闭时栈重算会把新面板一起判掉。）
        /// </summary>
        public static bool EnterFinaleMap;

        /// <summary>当前战斗对应的节点（出征时在编队界面写入）：重新挑战时据此回到同一节点的编队界面。</summary>
        public static NodeRequest PendingBattleNode;

        /// <summary>
        /// ★ 2026-10-20 用户实测 bug："战斗中直接关游戏，再进来这个节点直接通过了。"
        /// 根因：出征时 FormationPanel.CloseSelf() → 底下的节点地图 **Resume** →
        /// CampaignPanel 把"待推进节点"当场落地 ⇒ 仗还没打，节点已通过。
        /// 修：出征那一刻起、到战斗结束（ExitBattle）为止，禁止地图落地通过。
        /// </summary>
        public static bool BattleDeploying;

        /// <summary>
        /// 抑制"未领奖励补选"。结算流程（MainSceneEntry → ResultPanel）会自己交棒给 RelicPanel，
        /// 开图时若地图也弹一次，就会出现两个三选一叠在一起（2026-10-21 实测）。
        /// </summary>
        public static bool SuppressRewardPrompt;

        /// <summary>「重新挑战」挂起标记：战斗里点「重新挑战」置位，主城入口消费并直接打开编队界面（叠在节点地图之上）。</summary>
        public static NodeRequest PendingRechallengeNode;

        /// <summary>刚打完那场的节点类型（结算面板据此决定遗物掉落权重：精英/Boss 更高稀有度）。</summary>
        public static WanXiang.Campaign.NodeKind LastBattleKind = WanXiang.Campaign.NodeKind.Encounter;

        /// <summary>开始游戏：进主城。</summary>
        public static void EnterMain()
        {
            PendingResult = null;
            Load(MainScene);
        }

        /// <summary>出征：进战斗场景。</summary>
        /// <summary>
        /// 首领试炼：这场战斗要用的**幕**（决定背景/前景/粒子）。0 = 不覆盖（按节点图）。
        /// ⛔ 试炼没有 `PendingBattleNode`，驱动会退化成 act=1（全是春景）——
        ///   用户 2026-10-25 实测："每个 Boss 都是第一幕的战斗场景"。
        /// </summary>
        public static int PendingBattleAct;
        /// <summary>首领试炼：场域 id（非空 ⇒ 用场域专属 BG_/FG_/FX_）。</summary>
        public static string PendingBattleRealmId;

        public static void EnterBattle(BattleRequest request)
        {
            LastWasPvp = request != null && request.IsPvpMatch;
            PendingBattle = request;
            // ★ 2026-10-14 用户："从编队进战斗老是闪一下主界面/节点地图，观感很差" ——
            //   在转场中点（屏幕被当幕纸/墨盖满）时才同步切场景，中间帧用户看不见；
            //   切完由 UiTransition 重建层、纸底淡出露出战场（见 RebuildAfterSceneSwitch）。
            int act = WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : 1;
            // ★ 2026-10-20 用户："战斗转场换成和秘境转场一样（粒子拨开 + 艺术字）" ——
            //   复用 RealmTransition 的粒子/揭示流程，艺术字换成「战斗」，粒子用墨团。
            // ★ 粒子按**当前幕的季节**选（用户："按当前所在的幕的季节使用适合的粒子"）
            // 末尾追加冲击字「开始」：粒子散开 + 「战斗开始」消去之后砸出来
            // ★ 2026-10-23 用户："秘境里战斗转场会出现彩色圆粒子，很难看" ——
            //   秘境战斗必须用**该场域的粒子**（UI/Realm/FX_<realmId>），不能用季节花瓣/萤火。
            var runF = WanXiang.Run.RunSave.Current;
            // ★ 2026-10-25 用户："Boss 连战的转场粒子也要按幕/秘境变化"
            //   试炼没有 RunState 的 InRealm/Act，取面板传的覆盖值。
            int actForFx = PendingBattleAct > 0 ? UnityEngine.Mathf.Clamp(PendingBattleAct, 1, 5) : act;
            string realmForFx = !string.IsNullOrEmpty(PendingBattleRealmId)
                ? PendingBattleRealmId
                : (runF != null && runF.InRealm ? runF.RealmId : null);
            string partPath = !string.IsNullOrEmpty(realmForFx)
                ? ("UI/Realm/FX_" + realmForFx)
                : ParticleForAct(actForFx);
            // ★ 2026-10-23 首战闪主界面：先把转场层建好（Build 要到下一帧才画得出，
            //   第一次播时开场那两帧盖不住旧画面）。同时把主界面收掉 —— 战场只需节点图垫底。
            RealmTransition.Prewarm(partPath);
            // 战斗艺术字：按幕取专属（`UI/Title/battle_act1..5`），缺图退回通用「战斗」
            // 战斗艺术字：先找**场域专属**（UI/Title/battle_<realmId>），再按幕，最后通用
            string battleWord = null;
            if (!string.IsNullOrEmpty(realmForFx) &&
                UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Title/battle_" + realmForFx) != null)
                battleWord = "UI/Title/battle_" + realmForFx;
            if (battleWord == null)
            {
                battleWord = "UI/Title/battle_act" + actForFx;
                if (UnityEngine.Resources.Load<UnityEngine.Sprite>(battleWord) == null)
                    battleWord = "UI/Title/battle";
            }
            RealmTransition.PlayAsync(null, () => Load(BattleScene), null,
                battleWord, partPath, revealDelay: 0.10f,
                impactWordRes: ResolveStartWord(actForFx, realmForFx), instantCover: true).Forget();
        }

        /// <summary>
        /// PvP「重新挑战」：**留在战斗场景**原地重开同一场，不回主城、不播败退转场。
        /// ⛔ 2026-10-25 用户实测："好友对战点重新挑战，败退转场播到一半水墨就出来，
        ///   而且没回对战面板，直接回主界面了" —— 因为旧实现走 ExitBattle(null)：
        ///   必定 Load(MainScene) + 败退转场，而 MainSceneEntry 不知道"这是 PvP 重战"，
        ///   于是开了 HomePanel。
        /// </summary>
        public static void ReplayBattleInPlace()
        {
            var req = PendingBattle;
            PendingResult = null;
            if (req == null)
            {
                ExitBattle(null);
                return;
            }
            // ★ 交给场景驱动重建（清舞台 → 新 playback → CloseAll → 重开面板）。
            //   ⛔ 不要再"用 BattleRequest 直接重开面板"：那会走非场景模式，
            //     在已有舞台上再画一套单位（用户实测：棋盘上两套单位）。
            var drv = UnityEngine.Object.FindObjectOfType<BattleSceneDriver>();
            if (drv != null)
            {
                drv.Restart();
                return;
            }

            // 兜底（纯面板模式：主城内直接打，没有战斗场景驱动）
            UniTask.Void(async () =>
            {
                await UniTask.DelayFrame(2);
                var ui = WanXiang.Framework.Boot.UIBootstrap.UI;
                if (ui != null) ui.OpenAsync<BattlePanel>(req).Forget();
            });
        }

        /// <summary>
        /// PvP「重新挑战」：**播败退转场回主城，并直接打开好友对战面板**。
        /// ⛔ 2026-10-25 用户明确要的就是这条：转场 → 对战面板（不是原地重开战斗）。
        /// 实现：置 `PendingPvpReturn`（MainSceneEntry 据此开 Home + PvpPanel）→ ExitBattle(null)。
        /// </summary>
        /// <summary>首领试炼：本场是否来自试炼面板（打完/重挑都回试炼面板，不进结算、不记历程）。</summary>
        public static bool LastWasBossRush;
        /// <summary>回主城后要打开试炼面板。</summary>
        public static bool PendingBossRushReturn;

        /// <summary>首领试炼：打完/重挑 → 播转场回主城 → 打开试炼面板。</summary>
        public static void ReturnToBossRushPanel()
        {
            PendingBossRushReturn = true;
            PendingResult = null;
            ExitBattle(null);
        }

        public static void ReturnToPvpPanel()
        {
            PendingPvpReturn = true;
            PendingResult = null;
            ExitBattle(null);
        }

        /// <summary>战斗结束（或被撤退）：回主城并带结算。</summary>
        public static void ExitBattle(ResultRequest result)
        {
            PendingBattle = null;
            BattleDeploying = false;          // ★ 战斗结束（胜/败/撤退/重挑）→ 解除"出征中"锁
            PendingResult = result;
            // ★ 2026-10-20 用户："点重新挑战怎么就直接通过这个节点了" ——
            //   撤退/重挑（result == null）绝不能带着"待落地通过"回地图。
            if (result == null) CampaignPanel.PendingCommit = -1;
            // ★ 2026-10-20：回城同样走秘境式转场，胜负各配一张艺术字
            //   （胜利「胜利」/ 失败「败退」/ 平局撤退「败退」/ 通关「通关」）。
            string baseWord = "UI/Title/defeat";
            if (result != null && result.Win) baseWord = result.Draw ? "UI/Title/defeat" : "UI/Title/victory";
            if (WanXiang.Run.RunSave.Current != null && WanXiang.Run.RunSave.Current.Act >= 5 && result != null && result.Win)
                baseWord = "UI/Title/clear";
            int actNow = WanXiang.Run.RunSave.Current != null ? WanXiang.Run.RunSave.Current.Act : 1;
            // revealDelay 0.55s：主城的节点地图是异步打开的，等它就绪再拨开，
            // 否则会先露出主界面背景（用户实测）。
            var runX = WanXiang.Run.RunSave.Current;
            // ★ 2026-10-25 用户："败退的粒子没有变化"
            //   离开战斗同样要用**当前这场**的幕/场域（试炼走 PendingBattleAct/RealmId）。
            int actExit = PendingBattleAct > 0 ? UnityEngine.Mathf.Clamp(PendingBattleAct, 1, 5) : actNow;
            string realmExit = !string.IsNullOrEmpty(PendingBattleRealmId)
                ? PendingBattleRealmId
                : (runX != null && runX.InRealm ? runX.RealmId : null);
            string partExit = !string.IsNullOrEmpty(realmExit)
                ? ("UI/Realm/FX_" + realmExit)
                : ParticleForAct(actExit);
            // 胜负字：按 场域 → 幕 取专属（victory_realm_x / victory_act1..5），缺图退回通用
            string kind = baseWord.Substring("UI/Title/".Length);
            string word = null;
            if (!string.IsNullOrEmpty(realmExit) &&
                UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Title/" + kind + "_" + realmExit) != null)
                word = "UI/Title/" + kind + "_" + realmExit;
            if (word == null &&
                UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Title/" + kind + "_act" + actExit) != null)
                word = "UI/Title/" + kind + "_act" + actExit;
            if (word == null) word = baseWord;

            RealmTransition.PlayAsync(null, () => Load(MainScene), null, word,
                partExit, revealDelay: 0.55f).Forget();
        }

        /// <summary>「开始」冲击字：场域 → 幕 → 通用。</summary>
        private static string ResolveStartWord(int act, string realmId)
        {
            if (!string.IsNullOrEmpty(realmId) &&
                UnityEngine.Resources.Load<UnityEngine.Sprite>("UI/Title/start_" + realmId) != null)
                return "UI/Title/start_" + realmId;
            var byAct = "UI/Title/start_act" + act;
            if (UnityEngine.Resources.Load<UnityEngine.Sprite>(byAct) != null) return byAct;
            return "UI/Title/start";
        }

        /// <summary>按幕取季节粒子（与 HomeScene3D / UiTransition 同一套资源）。</summary>
        private static string ParticleForAct(int act)
        {
            switch (act)
            {
                case 1: return "Battle/FX_particle_act1_petal";    // 春·花瓣
                case 2: return "Battle/FX_particle_act2_firefly";  // 夏·萤火
                case 3: return "Battle/FX_particle_act3_leaf";     // 秋·落叶
                case 4: return "Battle/FX_particle_act4_snow";     // 冬·飞雪
                default: return "Battle/FX_particle_act5_pollen";  // 长夏·金尘
            }
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
