// ============================================================================
//  万相 · 旅程状态与存档
//  ---------------------------------------------------------------------------
//  一局"旅程"的全部可持久化数据：境·劫、灵卵/墨锭、胜败、上阵队伍。
//  存档 = 这份数据的 JSON，落在 Application.persistentDataPath/Saves/ 下，
//  三个槽位互不干扰。
//
//  为什么自己写而不是 PlayerPrefs：
//    ① 一个存档就是一份可读的 JSON，出问题玩家能把文件拿给我看；
//    ② 存档要能"复制备份/迁移"，散在注册表里做不到；
//    ③ 以后接云存档，换的只是 PathOf 与读写两端。
//
//  使用约定（谁改谁存）：
//    · 改 Current 的任何字段后，由改动方调用 SaveCurrent() 落盘 ——
//      框架不做自动保存，避免"半局状态"被写进档里；
//    · 现在的存档时机：选档进场、战斗结算确认、设置面板手动保存。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WanXiang.Run
{
    /// <summary>一局旅程的可持久化状态。字段全是 public，JsonUtility 直接读写。</summary>
    [Serializable]
    public sealed class RunState
    {
        public int Slot = 1;                    // 1..SlotCount
        /// <summary>
        /// 【旧体系】境 / 劫。进度主轴已改为 **Act（幕）**（见 RealmText / Difficulty）。
        /// 仅为兼容旧存档与试炼玩法保留 —— **不要再依据它们做进度/数值判断**，
        /// 否则会出现"已经第二幕了但数值还按第一境算"这类不一致（已踩过）。
        /// </summary>
        public int Realm = 1;                   // 境 1..3（旧）
        public int Jie = 1;                     // 劫 1..3（旧）
        public int Eggs = 0;                    // 灵卵（每局从 0 开始；局末结转 MetaState.Eggs）

        /// <summary>当前所在层的规则 id（floor_rules.csv）；进层时写入，老档空串=无修饰。</summary>
        public string ActiveFloorRule = "";
        /// <summary>B3：本局分队 id（空 = 默认「行旅」）。JsonUtility 序列化，老档自动为空。</summary>
        public string SquadId = "";
        /// <summary>B3：本局挑战难度（玩家选的 N1~N18）。老档自动为 1。
        /// ⚠ 与上方只读属性 <see cref="Difficulty"/>（幕数系数 1+(Act-1)×0.35）是两个概念：
        ///   前者是"玩家选的关卡难度"，后者是"幕数自带的强度系数"，战斗请求两者都要乘。</summary>
        public int ChallengeLevel = 1;

        [SerializeField] private float eggRemainder;   // B3-d：灵卵小数余额（老档默认 0）

        // ---- B2 道痕骨架：本局统计 —— 直接复用规则层 struct（DaoMarkRules.RunStats），
        //      字段与评估口径单一来源；战斗结束由 UI 回传累加，幕推进处补幕数。----
        public WanXiang.Battle.Core.RunStats Stats = new WanXiang.Battle.Core.RunStats();

        /// <summary>B2：一场战斗的局内统计回传（BattleState.Stat* → 规则层 struct）。</summary>
        public void AddBattleStats(int reactions, int maxHit, int bossKills, int deaths, int fastWin)
        {
            Stats.Reactions += reactions;
            if (maxHit > Stats.MaxHit) Stats.MaxHit = maxHit;
            Stats.BossKills += bossKills;
            Stats.Deaths += deaths;
            Stats.FastWins += fastWin;
        }

        /// <summary>
        /// B3-d：**奖励性**灵卵统一入口 —— 乘「挑战难度」egg_mul（0.99~0.85）。
        /// 小数余额累计到下一次凑整（N18 ×0.85 → 2 卵收 1.7，不被四舍五入吞掉）。
        /// ⚠ 只用于奖励（节点/休整/事件/灵泉/修行）；花费与返还不走这里。
        /// </summary>
        public void AddRewardEggs(int baseEggs)
        {
            float mul = 1f;
            if (ChallengeLevel > 1)
            {
                var drow = WanXiang.Battle.Core.DifficultyTable.Row(ChallengeLevel);
                if (drow != null && drow.EggMul > 0f) mul = drow.EggMul;
            }
            float v = baseEggs * mul + eggRemainder;
            int whole = (int)v;
            eggRemainder = v - whole;
            if (ActiveFloorRule == "press_foe") whole += 1;   // 瘴气渐起：胜利灵卵 +1
            Eggs += whole;
        }

        // ---- 2026-11-01 局内养成：共享主等级（设计稿 v3 §6.1）----
        /// <summary>主等级：战斗获得经验，<b>上阵 5 人共享</b>；只加基础攻击/生命。</summary>
        public int PlayerLevel = 1;
        /// <summary>当前等级内的经验（升级后清零）。</summary>
        public int PlayerExp;
        /// <summary>进化记录：beastId → 路线（0=未进化，1=路线A 消耗化，2=路线B 引爆化）。
        /// 每只**整局只能进化一次**（设计稿 §6.3）。</summary>
        public System.Collections.Generic.Dictionary<string, int> Evolved
            = new System.Collections.Generic.Dictionary<string, int>();

        public int EvolutionOf(string id)
        {
            if (string.IsNullOrEmpty(id) || Evolved == null) return 0;
            return Evolved.TryGetValue(id, out int v) ? v : 0;
        }

        public bool IsEvolved(string id) => EvolutionOf(id) > 0;

        /// <summary>进化一次。返回 false = 已经进化过（或 id 非法）。</summary>
        public bool Evolve(string id, int route)
        {
            if (string.IsNullOrEmpty(id) || route <= 0) return false;
            if (EvolutionOf(id) > 0) return false;          // ⛔ 每只只能进化一次
            Evolved[id] = route;
            return true;
        }

        /// <summary>专精：key = "兽id:技能槽(0普攻/1战技/2绝技)"，值 0~3（设计稿 §6.2）。
        /// 未进化上限 2，已进化上限 3。</summary>
        public System.Collections.Generic.Dictionary<string, int> Mastery
            = new System.Collections.Generic.Dictionary<string, int>();

        public int MasteryOf(string beastId, int slot)
        {
            if (string.IsNullOrEmpty(beastId) || Mastery == null) return 0;
            return Mastery.TryGetValue(beastId + ":" + slot, out int v) ? v : 0;
        }

        /// <summary>专精上限：未进化 2，已进化 3。</summary>
        public int MasteryCap(string beastId)
            => IsEvolved(beastId) ? ProgressionTuning.MasteryCapEvolved : ProgressionTuning.MasteryCapNormal;

        /// <summary>提升一级专精（自动按上限截断）。返回升完后的等级。</summary>
        public int AddMastery(string beastId, int slot, int add = 1)
        {
            if (string.IsNullOrEmpty(beastId)) return 0;
            int cur = MasteryOf(beastId, slot);
            int next = System.Math.Min(MasteryCap(beastId), cur + System.Math.Max(0, add));
            Mastery[beastId + ":" + slot] = next;
            return next;
        }

        /// <summary>进化素材（本局资源）：进化一只消耗 1 个。</summary>
        public int Materials;

        /// <summary>本局已出现过的事件 id（避免同一局反复遇到同一个不期而遇）。</summary>
        public System.Collections.Generic.List<string> SeenEvents
            = new System.Collections.Generic.List<string>();

        /// <summary>事件给的"限时"：下一场必须在该回合数内通过（0 = 不限制）；打完即清。</summary>
        public int EventTurnLimit;
        /// <summary>每只兽的"战后血量百分比"（0~100）：阵亡记 50，编队界面据此显示重伤。</summary>
        public System.Collections.Generic.Dictionary<string, int> TeamHpPct
            = new System.Collections.Generic.Dictionary<string, int>();

        // ★ 2026-11-02：曲线数值统一读 ProgressionTuning（progression.csv 可覆盖）。
        //   原来这里与 BattleRequestFactory 各写一份 +6%/+7%，改一处漏一处。
        public static int PlayerLevelMax => ProgressionTuning.LevelMax;
        /// <summary>升到下一级所需经验：Lv1→2 需 300，之后每级 +180。</summary>
        public static int ExpToNext(int level)
            => ProgressionTuning.ExpBase + ProgressionTuning.ExpStep * (System.Math.Max(1, level) - 1);
        /// <summary>等级带来的攻击倍率（+6%/级）。</summary>
        public float LevelAtkMul
            => 1f + ProgressionTuning.LevelAtk * (System.Math.Clamp(PlayerLevel, 1, PlayerLevelMax) - 1);
        /// <summary>等级带来的生命倍率（+7%/级）。</summary>
        public float LevelHpMul
            => 1f + ProgressionTuning.LevelHp * (System.Math.Clamp(PlayerLevel, 1, PlayerLevelMax) - 1);

        /// <summary>加经验并处理升级（可能连升）。返回升了几级。</summary>
        public int AddExp(int amount)
        {
            if (amount <= 0) return 0;
            PlayerExp += amount;
            int ups = 0;
            while (PlayerLevel < PlayerLevelMax && PlayerExp >= ExpToNext(PlayerLevel))
            {
                PlayerExp -= ExpToNext(PlayerLevel);
                PlayerLevel++;
                ups++;
            }
            if (PlayerLevel >= PlayerLevelMax) PlayerExp = 0;
            return ups;
        }
        // ⚠ 2026-09-25 删掉了 `Ink`（局内墨锭）字段：全项目**无人消费**（HomePanel.RunInk 声明未用、
        //   灵市花的是灵卵），每场胜利给它 +1 只是死数据。局外墨铊是 MetaState.Ink，与此无关。
        public int Wins;                        // 本程胜场

        /// <summary>当前幕（1..5，对应 SolarTermGraph 的五幕）——节点地图用。</summary>
        public int Act = 1;

        /// <summary>
        /// 本局路线图种子。**新开一局时随机生成并持久化**：
        /// 局内重进 → 同一张图（可背版）；重开一局 / 新档 → 新种子 → 全新路线图。
        /// 0 = 未生成（打开节点地图时 lazy 补种）。
        /// </summary>
        public int RunSeed = 0;

        /// <summary>孵穴「回复 40%」的挂起值（下一场战斗我方 ×(1+值/100)，用后清零）。</summary>
        public int HealPending = 0;

        // ⚠ 2026-09-25 删掉了 `MetaAltar`（L4 五行祭坛）：该系统已废弃，字段全项目**无人读写**
        //   （原用法在 BattleRequestFactory 里已随"局外养成改走本局快照"一并移除）。
        //   局外数值线现在是「异兽培养」的等级/进化，见下方快照区与 MetaDefaults。

        // ====================================================================
        //  本局快照：局外养成（异兽等级 / 进化 / 已装备觉醒技）
        //  --------------------------------------------------------------------
        //  用户 2026-09-25 定案：**开局那一刻锁定**。局内中途回主城在「异兽培养」里
        //  升级 / 进化 / 换觉醒技，**都不影响进行中的这一局**，要下一局才吃到。
        //  ⚠ 之前是"每次出征都现读 MetaStore" ， 局内换觉醒技、升级都会立刻漏进本局。
        // ====================================================================

        /// <summary>快照对应的 RunSeed。换局（重开/失败重来/新档）时 RunSeed 会变 ， 自动重拍。</summary>
        public int SnapshotSeed = 0;
        public List<string> SnapBeastIds = new List<string>();
        public List<int>    SnapLevels   = new List<int>();
        public List<int>    SnapStars    = new List<int>();   // B2：星级快照
        public List<bool>   SnapEvolved  = new List<bool>();
        public List<string> SnapAwaken   = new List<string>();   // 已装备的觉醒技 id（"" = 未装备）

        /// <summary>
        /// 已星移的节点 key（act*1000+offset）。**每节点限星移一次，写存档**：
        /// 换节点 / 进出编队界面 / 重启游戏都不会重置（防反复白嫖）；「重开一局」时清零。
        /// </summary>
        public List<int> XingyiUsed = new List<int>();

        /// <summary>
        /// 星移注入余气的【持久化镜像】。WeatherDef / LingerEntry 不可 Json 序列化，故存档只存
        /// 「来源幕 + 节点 offset + 剩余生效节点数 + 来源节气序号」，重进游戏时由 CampaignPanel
        /// 重新拉取 WeatherCatalog 重建会话级 LiveWeather.Lingers —— 让星移跨存档保留
        /// （之前只存了 XingyiUsed 标记、天气本身随会话丢失，等于白花灵卵）。
        /// </summary>
        public List<XingyiLingerSave> XingyiLingers = new List<XingyiLingerSave>();

        /// <summary>星移余气的单条存档（见 <see cref="XingyiLingers"/>）。</summary>
        [Serializable]
        public sealed class XingyiLingerSave
        {
            public int Act;          // 来源幕
            public int Offset;       // 来源节点 offset（act*1000+offset = OwnerKey）
            public int NodesLeft;    // 剩余生效节点数
            public int Term;         // 来源节气序号（重取天气用 WeatherCatalog.GetSolarTerm）
        }

        /// <summary>
        /// 若本局还没拍过快照（或已经换了一局）， 按当前局外存档拍一份；**整局不再更新**。
        /// 以 RunSeed 为界：任何"新一局"都会换 RunSeed，所以不必在每个重开点手动调用。
        /// </summary>
        public void EnsureMetaSnapshot()
        {
            if (SnapshotSeed == RunSeed && SnapBeastIds.Count > 0) return;

            var m = WanXiang.Meta.MetaStore.Ensure();
            SnapshotSeed = RunSeed;
            SnapBeastIds.Clear(); SnapLevels.Clear(); SnapStars.Clear(); SnapEvolved.Clear(); SnapAwaken.Clear();
            if (m == null) return;

            int n = System.Math.Min(m.BeastIds.Count, m.BeastLevels.Count);
            for (int i = 0; i < n; i++)
            {
                string id = m.BeastIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                SnapBeastIds.Add(id);
                SnapLevels.Add(i < m.BeastLevels.Count ? m.BeastLevels[i] : 0);
                SnapStars.Add(i < m.BeastStars.Count ? m.BeastStars[i] : 0);
                SnapEvolved.Add(i < m.BeastEvolved.Count && m.BeastEvolved[i]);
                SnapAwaken.Add(m.AwakenOf(id));
            }

            RunSave.Save(this);   // ★ 快照必须落盘：否则退出再进会重拍（等于没锁）
            UnityEngine.Debug.Log("[RunState] 已锁定本局快照：" + SnapBeastIds.Count + " 只异兽的等级/进化/觉醒技" +
                                  "（局内再升级/进化/换觉醒技，下一局才生效）");
        }

        public int SnapLevelOf(string id)
        {
            int i = SnapBeastIds.IndexOf(id);
            return i >= 0 && i < SnapLevels.Count ? SnapLevels[i] : 0;
        }

        public bool SnapEvolvedOf(string id)
        {
            int i = SnapBeastIds.IndexOf(id);
            return i >= 0 && i < SnapEvolved.Count && SnapEvolved[i];
        }

        /// <summary>B2：星级快照（未锁/不在名单 → 0）。</summary>
        public int SnapStarOf(string id)
        {
            int i = SnapBeastIds.IndexOf(id);
            return i >= 0 && i < SnapStars.Count ? SnapStars[i] : 0;
        }

        public string SnapAwakenOf(string id)
        {
            int i = SnapBeastIds.IndexOf(id);
            return i >= 0 && i < SnapAwaken.Count ? (SnapAwaken[i] ?? "") : "";
        }

        /// <summary>天象/异闻挂起：下一场战斗我方修正 %（可负，用后清零）。</summary>
        public int PlayerBuffPct = 0;

        /// <summary>
        /// ★ 2026-10-20 新增节点（灵泉/修行）带来的**本局永久成长**：我方全属性 +N%。
        /// 与 PlayerBuffPct（只作用于下一场、用后清零）不同，这个会一直累计到本局结束。
        /// </summary>
        public int GrowthPct = 0;

        /// <summary>
        /// ★ 2026-10-21 孵穴重做（用户："孵穴不像孵穴，应该是拿到蛋、过几个节点才孵化"）：
        /// EggHatchIn &gt; 0 = 有蛋在孵，值是**还要经过几个节点**；0 = 本节点孵化；-1 = 没蛋。
        /// EggBeastId = 蛋里那只异兽的 id（拾蛋时就抽好，孵化时才入队）。
        /// </summary>
        public int EggHatchIn = -1;
        public string EggBeastId;

        /// <summary>天象/异闻挂起：下一场战斗敌方修正 %（正=变强，用后清零）。</summary>
        public int EnemyBuffPct = 0;

        /// <summary>是否已打赢天阙终局战（真通关；防止抉择重复触发）。</summary>
        public bool BeatFinale = false;

        /// <summary>幕内当前节点下标（-1 = 还没出发）；换幕时归 -1。</summary>
        public int NodeOffset = -1;

        /// <summary>本幕已走过的节点序列（把路线描金 + 复盘用）。</summary>
        public System.Collections.Generic.List<int> Path = new System.Collections.Generic.List<int>();

        /// <summary>整局走过的全部节点（跨幕累计）。</summary>
        public System.Collections.Generic.List<int> VisitedNodes = new System.Collections.Generic.List<int>();

        /// <summary>
        /// ★ 同层互斥（2026-10-18 用户定案："每一层如果点击去过这个节点，
        /// 同一层的其他节点就不能再点击了"）—— 本局**进过**的节点下标。
        /// 进入节点（OnNextClicked 通过校验）那一刻记账；同层再有其他节点被点击即拒绝。
        /// </summary>
        public System.Collections.Generic.List<int> ChosenNodes = new System.Collections.Generic.List<int>();

        /// <summary>
        /// ★ 2026-10-20 用户实测 bug："战斗胜利、死了几只、结果面板还没点就退游戏，
        /// 再进来发现死亡的异兽没了，但这个节点没有通过。"
        /// 根因：阵亡在 HandOff 就地写档，而"节点通过"要等回地图才落地（PendingCommit 是静态字段，
        /// 退出即丢）。修法：胜利那一刻把待落地的节点号**存进存档**，
        /// 下次开地图（或本次回图）由 CampaignPanel 走正常落地流程（含换幕），逻辑不分叉。
        /// </summary>
        public int PendingBattlePass = -1;

        /// <summary>
        /// ★ 2026-10-20 用户："这样是不是就丢失了一次选择奖励的机会" ——
        /// 胜利的三选一奖励在**胜利那一刻**就抽好并落盘（3 个遗物 id），
        /// 玩家没点结果面板就退出也不会丢：下次进地图会再把这次选择补给他
        /// （ResultPanel 优先消费它；错过则由 CampaignPanel 拉起 RelicPanel 补选）。
        /// 消费掉（确认/跳过）即清空。
        /// </summary>
        public System.Collections.Generic.List<string> PendingRelicDraft
            = new System.Collections.Generic.List<string>();

        /// <summary>
        /// **真正通过**的节点 —— 与 <see cref="VisitedNodes"/> 的区别：
        /// `VisitedNodes` = **进入**（点节点那一刻就写）；本表 = **通过**（只有 `CampaignPanel.OnOpenAsync`
        /// 把 `NodeOffset = PendingCommit` 落地时才记一笔）。
        /// ⛔ 落印「✕」、连线描金实线 等"通过之后才有"的表现一律看本表，不看 VisitedNodes
        ///   （用户实锤：点进战斗编队又返回，算"进入"但没通过）。换幕/重开时随 VisitedNodes 一起作废
        ///   （读取端用 `PassedNodes ∩ VisitedNodes` 过滤即可，不必每处都清）。
        /// </summary>
        public System.Collections.Generic.List<int> PassedNodes = new System.Collections.Generic.List<int>();

        /// <summary>
        /// 问号节点的揭晓结果，形如 "7:nest"（节点下标:类型）。
        /// ⚠ 不用 Dictionary —— JsonUtility 不支持，存成 List<string> 最省事。
        /// </summary>
        public System.Collections.Generic.List<string> QuestionRevealed =
            new System.Collections.Generic.List<string>();

        // ==================== 瘴雾（2026-10-10 新玩法） ====================
        /// <summary>瘴雾剩余节点数（0 = 无雾）。>0 时节点图上"当前格 + 下一层"之外的层被雾遮住，
        ///  每**真正通过**一个节点 -1，扣到 0 雾散。</summary>
        public int FogLeft = 0;
        /// <summary>本批瘴雾是否已经重掷过节点类型（防止反复开关面板时节点变来变去）。</summary>
        public bool FogRerolled = false;
        /// <summary>被瘴雾重掷过的节点类型，形如 "7:3"（节点下标:NodeKind）。
        ///  ⛔ 节点图每次都由种子重建 ， 重掷结果**必须落盘并在 BuildRoute 之后覆盖回去**（照 QuestionRevealed 的做法）。</summary>
        public System.Collections.Generic.List<string> RerolledKinds =
            new System.Collections.Generic.List<string>();

        /// <summary>挂了「瘴雾」天时的节点：**走到这些节点（点"前往"那一刻）就起雾**。本幕有效，换幕重生成。</summary>
        public System.Collections.Generic.List<int> FogWeatherNodes =
            new System.Collections.Generic.List<int>();

        /// <summary>已经为哪一幕生成过瘴雾节点（0 = 还没生成）。换幕置 0 ， 下一幕重新生成。</summary>
        public int FogGenAct = 0;

        /// <summary>"起雾的那一格"（走到它才起的雾）：**雾期间它自己也显示成「？？迷雾」**。-1 = 无。</summary>
        public int FogTriggerNode = -1;
        public int Losses;                      // 本程败场

        // ---- 秘境（Realm，2026-10-16 设计文档第 7 章）----
        //  ⚠ 这三个字段**不进存档**（RunSave 的序列化白名单里没有它们）：
        //    玩家在秘境里退出游戏，读档后回到主线的原节点 —— 不会卡在"半个秘境"里。

        /// <summary>是否正在秘境中。</summary>
        public bool InRealm = false;

        /// <summary>当前场域 id（见 <c>WanXiang.Campaign.RealmIds</c>）。</summary>
        public string RealmId = null;

        /// <summary>本局已清的波数（进秘境的第 N 场）。</summary>
        public int RealmWave = 0;

        /// <summary>
        /// 本次秘境里**已通过**的节点 offset（落印✕ / 连线实线只看它）。
        /// ⛔⛔ 秘境进度**只存在这里**，绝不写进 NodeOffset / VisitedNodes / PassedNodes ——
   ///   那三个是"主线走到第几格"的存档，语义属于主线。
        ///   借用的代价（实测能踩到的）：中途存档/退出再进来，主线位置就停在秘境格上。
    /// </summary>
        public System.Collections.Generic.List<int> RealmCleared =
            new System.Collections.Generic.List<int>();

        /// <summary>秘境里当前所在节点（-1 = 还没出发）。同样只属于秘境，不碰主线 NodeOffset。</summary>
      public int RealmCursor = -1;

        /// <summary>
        /// 本次秘境里**已发过镇守奖励**的节点 offset。
        /// ⛔ 必须落盘：重进面板会重跑"通过节点"那段，没有这个幂等表就会无限刷奖励。
        /// </summary>
        public System.Collections.Generic.List<int> RealmChampionRewarded =
            new System.Collections.Generic.List<int>();

        /// <summary>本局进过的场域 id（**同局去重**：一种场域一局只进一次）。</summary>
        public System.Collections.Generic.List<string> RealmEntries =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// 「换一个」的免费次数记在**哪一次秘境遭遇**上（= Act*10000 + NodeOffset）。
        /// <para>⛔⛔ 为什么必须落盘（2026-10-17 用户实测）：`_rerollUsed` 原本只是面板的
        ///   **实例字段**，而每次打开面板都会 `_rerollUsed = 0` ——
        ///   于是"关掉面板再打开"就能无限白嫖免费换，玩家一分灵卵没花换了好几次。</para>
        /// <para>换成"遭遇 key"而不是单纯计数：换到下一个秘境节点时自动归零，
        ///   不需要谁记得去重置（少一个可能漏掉的调用点）。</para>
        /// </summary>
        public int RealmRerollKey = -1;

        /// <summary>该次遭遇已用掉几次「换一个」（0 = 免费那次还在）。</summary>
        public int RealmRerollUsed = 0;

        /// <summary>
        /// 进入秘境前的**路线图快照**（硬约束 ②「打完必回原节点」的实现基础）。
        /// ⛔ 只存**会被场域改动**的字段 —— 存全量反而容易漏改。
        /// </summary>
        public RealmRouteSnapshot RealmSnapshot = null;

        /// <summary>路线图快照（List 在复制时必须深拷贝，见RealmRuntime.Enter）。</summary>
        [System.Serializable]
        public sealed class RealmRouteSnapshot
        {
            public int NodeOffset = -1;
            public int HealPending = 0;
            public int PlayerBuffPct = 0;
            public int EnemyBuffPct = 0;
            public int FogLeft = 0;
            /// <summary>进场时的已探索节点数（用于面板显示，不参与还原）。</summary>
            public int NodeCount = 0;
            public System.Collections.Generic.List<int> Path;
            public System.Collections.Generic.List<int> VisitedNodes;
            public System.Collections.Generic.List<int> PassedNodes;
        }
        /// <summary>
        /// 轮回数（"续劫"次数）：每轮回一次，敌人属性 +15%、天气更恶劣。
        /// 与已废弃的 Jie/Realm 无关 —— 那是旧体系；这个是**无尽模式的实际难度轴**。
        /// </summary>
        public int Ascension = 0;

        /// <summary>
        /// 已获得的【灵魂】（存"魂的主人"的异兽 id）。
        /// 魂本体不落盘：由 <c>SoulForge.Derive(beast, ordinal)</c> 按 id 确定性重建（同 id 必同魂）。
        /// 来源：灵市购买 / 战斗与事件掉落 / 铸魂台"炼魂"（消耗一只异兽）。
        /// </summary>
        public System.Collections.Generic.List<string> Souls =
            new System.Collections.Generic.List<string>();

        /// <summary>拥有的异兽图鉴（灵市购买进这里，不占出战名额；出战 5 只在编阵界面选）。</summary>
        public List<string> Collection = new List<string>();

        /// <summary>
        /// 已经播过开场动画的幕号（0 = 一幕都没播）。见 <c>ActIntroPanel</c>。
        /// ⛔ 必须落盘：否则每次读档回节点地图都会重播（用户要的是"每到新的一幕才播"）。
        /// 老存档没这个字段 ， 反序列化后为 0 ， 补播一次当前幕（可接受的自愈）。
        /// </summary>
        public int IntroShownAct;

        public List<string> Team = new List<string>();   // 上阵异兽 id（继承用）
        public string LastSaved = "";           // 最后保存时间（展示用）

        /// <summary>
        /// 本局已获得的【遗物】（杀戮尖塔式局内成长）。存遗物 id 列表；
        /// 出战前由 <c>BattleRequestFactory</c> 读它折叠成战斗增益（玩家侧属性/复活/开局灵力等）。
        /// 重开一局时清空（与 Team/Collection 同口径）。
        /// </summary>
        public System.Collections.Generic.List<string> Relics = new System.Collections.Generic.List<string>();

        // ---- B4：结局骨架（印记授予来源 B4-3 接；终局判定 = EndingsRules.Evaluate）----
        /// <summary>已获印记/关键道具（item_tu_1..3 = 后土三印；事件 grant_item / 节点改造授予）。</summary>
        public System.Collections.Generic.List<string> GrantedItems = new System.Collections.Generic.List<string>();
        /// <summary>已通过「后土试炼」（真结局前置）。</summary>
        public bool ClearedHoutu;
        /// <summary>终局判定结果（endings.csv 的 id；守关胜利后写）。</summary>
        public string EndingId = "";
        /// <summary>本场守关是「后土试炼」（3 印且幕 5 的变形守关；胜利 → ClearedHoutu）。</summary>
        public bool PendingHoutuTrial;

        /// <summary>B4-3：授予印记/关键道具（去重 + 日志）。</summary>
        public bool GrantItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || GrantedItems.IndexOf(itemId) >= 0) return false;
            GrantedItems.Add(itemId);
            UnityEngine.Debug.Log("[RunState] 获得印记：" + itemId + "（累计 " + GrantedItems.Count + "）");
            return true;
        }

        /// <summary>B4-3：已持后土印数（item_tu_1..3）。</summary>
        public int TuMarkCount()
        {
            int n = 0;
            for (int i = 0; i < GrantedItems.Count; i++)
                if (GrantedItems[i].StartsWith("item_tu_")) n++;
            return n;
        }

        /// <summary>
        /// 进战斗用的强度系数：随【幕】缓涨，给敌人与奖励一个共同标尺。
        /// ⚠ 原实现用 Realm/Jie（旧"境/劫"体系）—— 那套在幕推进后已不再递增，
        ///   会导致难度永远停在 1.0（数值标尺失效）。现改为以 Act 为唯一主轴。
        /// </summary>
        /// <summary>
        /// ★ 2026-10-20 用户实测："同一个存档点重开一局，保留了上一局的遗物；
        /// 而且节点又推不动了。"根因：重开只清了 Act/NodeOffset/Visited 等少数字段，
        /// **Relics / Team / PassedNodes / ChosenNodes(同层互斥) / 待领奖励 / 秘境状态** 全留着：
        /// - 旧遗物带进新局（本不该有）；
        /// - 旧 ChosenNodes 把"同层已进过"的锁带到新局 ⇒ 整层点不动（节点推不动的真因）。
        /// 统一在这里重置**所有局内进度**；收集/图鉴类跨局资产不动。
        /// </summary>
        public void ResetForNewRun(int newSeed)
        {
            Act = 1;
            NodeOffset = -1;
            Wins = 0;
            Losses = 0;
            Eggs = 0;
            RunSeed = newSeed;
            HealPending = 0;
            PlayerBuffPct = 0;
            EnemyBuffPct = 0;
            GrowthPct = 0;
            ActiveFloorRule = "";   // 新局不继承上一段的层修饰
            EggHatchIn = -1;
            EggBeastId = null;
            BeatFinale = false;
            IntroShownAct = 0;
            // 局外成长快照：清空后下次进场会按 MetaState 重新快照
            SnapshotSeed = 0;
            if (SnapBeastIds != null) SnapBeastIds.Clear();
            if (SnapAwaken != null) SnapAwaken.Clear();

            // 进度/路线
            if (Path != null) Path.Clear();
            if (VisitedNodes != null) VisitedNodes.Clear();
            if (PassedNodes != null) PassedNodes.Clear();
            if (ChosenNodes != null) ChosenNodes.Clear();          // ★ 同层互斥锁：跨局必须清
            PendingBattlePass = -1;
            if (PendingRelicDraft != null) PendingRelicDraft.Clear();

            // 雾 / 重掷 / 星移
            if (QuestionRevealed != null) QuestionRevealed.Clear();
            if (RerolledKinds != null) RerolledKinds.Clear();
            if (FogWeatherNodes != null) FogWeatherNodes.Clear();
            FogLeft = 0; FogRerolled = false; FogGenAct = 0; FogTriggerNode = -1;
            if (XingyiUsed != null) XingyiUsed.Clear();
            if (XingyiLingers != null) XingyiLingers.Clear();

            // 遗物 / 拥有 / 编成（重开 = 全新旅程：上局招募的异兽也不该跟着）
            //   ⛔ 2026-10-20 用户实测："只招募了 3 个，拥有里却一大堆" —— 重开没清 Collection。
            if (Relics != null) Relics.Clear();
            if (Collection != null) Collection.Clear();
            if (Team != null) Team.Clear();
            Stats = new WanXiang.Battle.Core.RunStats();   // B2：本局统计随局重置

            // 秘境状态（半个秘境残留会让新局所有进入被拒）
            InRealm = false;
            RealmId = null;
            RealmWave = 0;
            if (RealmCleared != null) RealmCleared.Clear();
            if (RealmChampionRewarded != null) RealmChampionRewarded.Clear();
            if (RealmEntries != null) RealmEntries.Clear();
            RealmRerollKey = -1;
            RealmRerollUsed = 0;
            RealmSnapshot = null;

            WanXiang.Run.RunSave.SaveCurrent();
            Debug.Log("[RunSave] 重开一局：局内进度已全部重置（遗物/队伍/通过/互斥锁/秘境）");
        }

        /// <summary>
        /// 只重置**地图进度**（续劫 / 登天阙用）：Act/位置/通过/互斥锁/雾/重掷/星移/待办，
        /// 保留遗物、队伍、收集、灵卵。
        /// ⛔ 存在的理由：这些"章节切换"路径原来只手写清 Visited ——
        ///   PassedNodes / ChosenNodes（同层互斥）是**按本幕下标**存的，跨幕不清必然串味，
        ///   表现就是"下一幕整层点不动"（用户 2026-10-20 报的解锁 bug 的同类）。
        /// </summary>
        public void ResetMapProgress()
        {
            NodeOffset = -1;
            if (Path != null) Path.Clear();
            if (VisitedNodes != null) VisitedNodes.Clear();
            if (PassedNodes != null) PassedNodes.Clear();
            if (ChosenNodes != null) ChosenNodes.Clear();
            PendingBattlePass = -1;
            if (PendingRelicDraft != null) PendingRelicDraft.Clear();
            if (QuestionRevealed != null) QuestionRevealed.Clear();
            if (RerolledKinds != null) RerolledKinds.Clear();
            if (FogWeatherNodes != null) FogWeatherNodes.Clear();
            FogLeft = 0; FogRerolled = false; FogGenAct = 0; FogTriggerNode = -1;
            if (XingyiUsed != null) XingyiUsed.Clear();
            if (XingyiLingers != null) XingyiLingers.Clear();
        }

        public float Difficulty => 1f + (Act - 1) * 0.35f;

        public string RealmText
        {
            // ★ 进度主轴是 **Act（幕）** —— Realm/Jie 是旧体系，幕推进时不更新，
            //   会出现"已经第二幕了存档还写第一境"（用户实测）。这里直接以幕为准。
            get { return "第" + Cn(Act) + "幕"; }
        }

        private static string Cn(int n)
        {
            switch (n)
            {
                case 1: return "一";
                case 2: return "二";
                case 3: return "三";
                default: return n.ToString();
            }
        }
    }

    /// <summary>
    /// 局外图鉴解锁表（**跨局永久**，独立于任何槽位）。
    /// 设计：异兽都是"局内养成"的，失败清空 Collection —— 但**只要曾经获得过**，
    /// 外面的图鉴就永久解锁（用户明确要求）。
    /// </summary>
    public static class CodexUnlock
    {
        private static System.Collections.Generic.HashSet<string> _ids;
        private static string FilePath
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, "codex_unlocked.json"); }
        }

        [System.Serializable]
        private class Wrap { public string[] Ids; }

        private static void Ensure()
        {
            if (_ids != null) return;
            _ids = new System.Collections.Generic.HashSet<string>();
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    var w = JsonUtility.FromJson<Wrap>(System.IO.File.ReadAllText(FilePath));
                    if (w != null && w.Ids != null)
                        foreach (var id in w.Ids)
                            if (!string.IsNullOrEmpty(id)) _ids.Add(id);
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[CodexUnlock] 读取失败：" + ex.Message); }
        }

        private static void Flush()
        {
            try
            {
                var list = new System.Collections.Generic.List<string>(_ids);
                System.IO.File.WriteAllText(FilePath, JsonUtility.ToJson(new Wrap { Ids = list.ToArray() }, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[CodexUnlock] 写入失败：" + ex.Message); }
        }

        /// <summary>是否曾经获得过（用于图鉴显示解锁状态）。</summary>
        public static bool IsUnlocked(string id)
        {
            Ensure();
            return !string.IsNullOrEmpty(id) && _ids.Contains(id);
        }

        /// <summary>登记"曾获得"。返回 true = 这次是新解锁。</summary>
        public static bool Unlock(string id)
        {
            Ensure();
            if (string.IsNullOrEmpty(id) || !_ids.Add(id)) return false;
            Flush();
            return true;
        }

        public static int Count { get { Ensure(); return _ids.Count; } }
    }

    /// <summary>存档读写 + 当前旅程的单例入口。</summary>
    public static class RunSave
    {
        public const int SlotCount = 3;

        /// <summary>当前进行中的旅程（选档后一直有值；未选档时为 null）。</summary>
        public static RunState Current;

        /// <summary>当前槽位（0 = 还没选档）。</summary>
        public static int ActiveSlot
        {
            get { return Current != null ? Current.Slot : 0; }
        }

        private static string Dir
        {
            get { return Path.Combine(Application.persistentDataPath, "Saves"); }
        }

        private static string PathOf(int slot)
        {
            return Path.Combine(Dir, "wanxiang_save_" + slot + ".json");
        }

        /// <summary>槽位上有没有档。</summary>
        public static bool Exists(int slot)
        {
            return File.Exists(PathOf(slot));
        }

        /// <summary>读槽位。空档/损坏返回 null（不抛异常，让调用方走"空档"分支）。</summary>
        /// <summary>
        /// 只读取槽位数据、**不改变 Current**（存档列表用它显示；进入存档请走 ContinueWith）。
        /// </summary>
        public static RunState Load(int slot)
        {
            try
            {
                var path = PathOf(slot);
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var state = JsonUtility.FromJson<RunState>(json);
                if (state != null) state.Slot = slot;

                // ★★ 掉线保护（2026-10-17 补上调用）：
                //   `RealmSession.SanitizeOnLoad` 早就写好了，但**从来没人调用** ——
                //   结果就是"进场存了档、转场没走完就退出"会在存档里留下 InRealm=true，
                //   之后再点进秘境一律被 `Enter` 拒绝（判定：已在秘境中），**表现是点了没反应**。
                //   放在 Load 里而不是 ContinueWith：所有读档路径（继续 / 读槽 / 自动）都过这里。
                if (state != null) WanXiang.Modules.UI.RealmSession.SanitizeOnLoad(state);
                return state;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RunSave] 读取存档失败 slot=" + slot + "\n" + ex);
                return null;
            }
        }

        /// <summary>写槽位（自动盖时间戳）。</summary>
        public static void Save(RunState state)
        {
            // ★ 防跨档污染：只允许把 **当前旅程** 写回它自己的槽位。
            //   出现过"在 A 存档里操作却覆盖了 B 存档"（某处拿到了不属于 Current 的旧对象）。
            //   这里直接拦掉并留日志，比事后找凶手容易得多。
            if (state != null && Current != null && !ReferenceEquals(state, Current))
            {
                Debug.LogWarning("[RunSave] 拒绝写入非当前旅程（write slot=" + state.Slot +
                                 " / current slot=" + Current.Slot + "），已阻止跨档污染");
                return;
            }

            if (state == null) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                state.LastSaved = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // ★ 自动登记图鉴解锁：凡是出现在本局"图鉴/队伍"里的异兽，都算"曾经获得过"
            //   （存在 Collection 或 Team 即可，不必在各获得点分别调用，避免漏登记）
            if (state.Collection != null)
                foreach (var id in state.Collection) CodexUnlock.Unlock(id);
            if (state.Team != null)
                foreach (var id in state.Team) CodexUnlock.Unlock(id);
                File.WriteAllText(PathOf(state.Slot), JsonUtility.ToJson(state, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[RunSave] 写入存档失败 slot=" + state.Slot + "\n" + ex);
            }
        }

        /// <summary>清空单个槽位（删文件；若是当前档则清 Current）。</summary>
        public static void ClearSlot(int slot)
        {
            try { if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot)); }
            catch (Exception ex) { Debug.LogWarning("[RunSave] 删除失败 slot=" + slot + "：" + ex.Message); }
            if (Current != null && Current.Slot == slot) Current = null;
            Debug.Log("[RunSave] 已清空槽位 " + slot);
        }

        /// <summary>清空全部存档（删文件 + 清当前）。给"从头开始"用。</summary>
        public static void ClearAll()
        {
            for (int slot = 1; slot <= SlotCount; slot++)
            {
                try { if (File.Exists(PathOf(slot))) File.Delete(PathOf(slot)); }
                catch (Exception ex) { Debug.LogWarning("[RunSave] 删除失败 slot=" + slot + "：" + ex.Message); }
            }
            Current = null;
            Debug.Log("[RunSave] 已清空全部存档");
        }

        /// <summary>在指定槽位开一段全新旅程（覆盖该槽位）。</summary>
        public static RunState StartNew(int slot)
        {
            Current = new RunState { Slot = Mathf.Clamp(slot, 1, SlotCount), Eggs = 0 };
            Save(Current);
            Debug.Log("[RunSave] 新的旅程 → 槽位 " + Current.Slot);
            return Current;
        }

        /// <summary>
        /// B3：把 SquadSelectPanel 的选择写进本局（分队 id + 难度），然后读后清。
        /// ⚠ 分队 id 不在表里 / 为空 → 回退默认分队（SquadTable.Default）。
        /// </summary>
        public static void ApplyPendingChoice(RunState run)
        {
            if (run == null) return;
            // B3 修正：无选择时**不动**现有字段（选择在出征时才发生，
            // StartNew 选槽时玩家还没选 —— 早应用会回退默认、覆盖玩家后续选择）。
            bool hasPending = !string.IsNullOrEmpty(WanXiang.Modules.UI.SquadSelectPanel.PendingSquadId)
                || WanXiang.Modules.UI.SquadSelectPanel.PendingDifficulty != 1;
            if (!hasPending) return;
            string id = WanXiang.Modules.UI.SquadSelectPanel.PendingSquadId;
            var def = string.IsNullOrEmpty(id) ? null : WanXiang.Battle.Core.SquadTable.For(id);
            run.SquadId = def != null ? def.Id
                        : (WanXiang.Battle.Core.SquadTable.Default() != null ? WanXiang.Battle.Core.SquadTable.Default().Id : "");
            run.ChallengeLevel = Mathf.Clamp(WanXiang.Modules.UI.SquadSelectPanel.PendingDifficulty,
                                         1, WanXiang.Battle.Core.DifficultyTable.MaxLevel > 0 ? WanXiang.Battle.Core.DifficultyTable.MaxLevel : 18);
            WanXiang.Modules.UI.SquadSelectPanel.PendingSquadId = "";    // 读后清
            WanXiang.Modules.UI.SquadSelectPanel.PendingDifficulty = 1;
            Debug.Log("[RunSave] 开局配置：分队=" + run.SquadId + " 难度=N" + run.ChallengeLevel);
            ApplySquadLoadout(run);                 // B3-c：起手异兽 / 灵卵 / 遗物
        }

        /// <summary>
        /// B3-c：按分队发牌（起手异兽 + 初始灵卵 + 初始遗物）。
        /// 只应在 ResetForNewRun / StartNew 之后调用一次（重复调用会重复发灵卵）。
        /// 随机源 = 种子哈希 ⇒ 同一档位重开是同一批起手（与遗物抽取同口径）。
        /// </summary>
        public static void ApplySquadLoadout(RunState run)
        {
            if (run == null) return;
            var squad = string.IsNullOrEmpty(run.SquadId)
                ? WanXiang.Battle.Core.SquadTable.Default()
                : WanXiang.Battle.Core.SquadTable.For(run.SquadId);
            if (squad == null)
            {
                Debug.LogWarning("[RunSave] 分队表缺失/找不到 " + run.SquadId + "，跳过发牌");
                return;
            }

            // 灵卵 / 初始遗物
            run.Eggs = Mathf.Max(0, squad.Eggs);
            if (!string.IsNullOrEmpty(squad.Relic) && run.Relics != null && !run.Relics.Contains(squad.Relic))
                run.Relics.Add(squad.Relic);

            // 起手异兽
            var rng = new WanXiang.Battle.Core.DeterministicRandom(
                WanXiang.Battle.Core.CoreMath.Fnv1a("squadLoadout:" + run.RunSeed));
            var starters = ResolveStarters(squad, rng);
            if (run.Collection == null) run.Collection = new List<string>();
            if (run.Team == null) run.Team = new List<string>();
            int added = 0;
            foreach (var id in starters)
            {
                if (!run.Collection.Contains(id)) run.Collection.Add(id);
                if (run.Team.Count < 5 && !run.Team.Contains(id)) run.Team.Add(id);
                added++;
            }
            WanXiang.Run.RunSave.Save(run);
            Debug.Log("[RunSave] 分队发牌：" + squad.Name + " → 起手 " + added + " 只 [" +
                      string.Join(",", starters) + "] 灵卵 " + run.Eggs + " 遗物 " +
                      (squad.Relic ?? "无"));
        }

        /// <summary>解析起手表达式：`id|id|id` / `random:N` / `random_same_element:N`。</summary>
        /// <summary>固定 5 人起手（2026-11-12）：starter 只认 `id|id|...`，随机发牌已删除。</summary>
        public static List<string> ResolveStarters(WanXiang.Battle.Core.SquadDef squad,
                                                   WanXiang.Battle.Core.DeterministicRandom rng)
        {
            var result = new List<string>();
            string expr = squad.StarterExpr ?? "";
            if (string.IsNullOrEmpty(expr)) return result;
            var ids = expr.Split('|');
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            return result;
        }


        public static RunState ContinueWith(RunState state)
        {
            Current = state;
            if (Current != null) Debug.Log("[RunSave] 继承旅程 → 槽位 " + Current.Slot +
                                           "（" + Current.RealmText + "，灵卵 " + Current.Eggs + "）");
            return Current;
        }

        /// <summary>把当前旅程落盘（改动方自行决定何时调用）。</summary>
        public static void SaveCurrent()
        {
            if (Current != null) Save(Current);
        }
    }
}
