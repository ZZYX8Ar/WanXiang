// ============================================================================
//  万相 · 战斗组队工厂（运行期）
//  ---------------------------------------------------------------------------
//  结构验证版：从 ContentCatalogSO 里取前 5 只当我方、第 6~10 只当敌方。
//  正式版应改为：我方 = RunState 的队伍；敌方 = SeededEnemyProvider 按节点抽签。
//  两者都只需要换掉本文件里的两个 for 循环，BattlePanel 一行不用改。
// ============================================================================

using System.Collections.Generic;
using WanXiang.Battle.Core;
using WanXiang.Fusion;

namespace WanXiang.Modules.UI
{
    public static class BattleRequestFactory
    {
        
        /// <summary>最近一次命中的遭遇编队（编队界面用来显示"这队是谁 + 破局点"）。</summary>
        public static WanXiang.Campaign.EncounterDef LastEncounter;
public static bool TryBuild(ContentCatalogSO catalog, string title, string weather,
                                    ulong seed, out BattleRequest req)
        {
            req = null;
            if (catalog == null)
            {
                UnityEngine.Debug.LogError("[BattleRequestFactory] ContentCatalog 为空，无法组队。");
                return false;
            }

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            req = new BattleRequest
            {
                Title = string.IsNullOrEmpty(title) ? "遭遇战" : title,
                WeatherName = weather ?? "",
                // 非战役战斗：本场无天时（同时清掉可能残留的战役天气，避免串场）。
                Weather = null,
                Seed = seed,
            };
            WanXiang.Campaign.LiveWeather.Current = null;

            int half = all.Length / 2;
            for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);

            // 敌方在池子的后半段取，避免敌我和自己镜像对打（结构验证版的可读性考虑）
            var offset = all.Length > 5 ? System.Math.Min(half, all.Length - 1) : 0;
            for (int i = 0; i < 5; i++)
            {
                int idx = offset + i;
                if (idx >= all.Length) idx = i;
                req.Enemy.Add(all[idx]);
            }
            return true;
        }

        /// <summary>当前队伍里第一只的名字，用于主界面立绘展示。</summary>
        public static string FirstBeastId(ContentCatalogSO catalog)
        {
            if (catalog == null) return null;
            var all = ContentLibrary.BuildBeasts(catalog);
            return (all != null && all.Length > 0) ? all[0].Id : null;
        }

        // ==================================================================
        //  正式版组队：我方 = 存档队伍；敌方 = 种子抽签 + 劫数难度缩放
        // ==================================================================

        /// <summary>
        /// 从一段进行中的旅程组一场战斗。
        ///   我方 = 存档里记录的队伍（id 对不上的跳过；一只都对不上退回结构验证版）；
        ///   敌方 = SeededEnemyProvider 按进度种子抽签，数量随劫数 3→5，
        ///          强度用 DeployEntry.StatMul 挂旅程难度系数（速度/暴击不缩放）。
        /// 同一存档同一劫 → 敌人阵容与数值完全一致（可背版、可复盘）。
        /// </summary>
        public static bool TryBuildFromRun(ContentCatalogSO catalog, WanXiang.Run.RunState run,
                                           string weather, out BattleRequest req,
                                           WanXiang.Campaign.NodeKind kind = WanXiang.Campaign.NodeKind.Encounter,
                                           int termIndex = -1, bool isBoss = false)
        {
            req = null;
            if (catalog == null || run == null) return false;

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            int act = System.Math.Max(1, System.Math.Min(run.Act, 5));
            int term = termIndex >= 0 ? termIndex : System.Math.Max(0, run.NodeOffset);
            bool elite = kind == WanXiang.Campaign.NodeKind.Elite
                      || kind == WanXiang.Campaign.NodeKind.RealmChampion;
            // ★ 守关：图里最后一格复用 【精英】占位，靠 isBoss 标志区分（普通精英 vs 首领）。
            bool boss = isBoss && kind == WanXiang.Campaign.NodeKind.Elite;
            // ★★ 秘境「镇守」（2026-10-16 用户："一些特殊的敌人，这些敌人的攻击设计很有意思，
            //   打死了会有特殊奖励"）—— 让它**按首领规格组队**（走 BossSquadFor 那套）：
            //   这是它与普通遭遇最实质的区别，也是"值得为它绕路"的理由。
            //   ⛔ 不要把它标成 IsBoss=true：IsBoss 还牵着"守关奖励/幕推进"等主线逻辑，
            //     那是四季幕末专属的，秘境复用会串味。
            bool champion = kind == WanXiang.Campaign.NodeKind.RealmChampion;

            // 种子：同存档同幕同节点 → 同一套敌人（可背版、可复盘）
            ulong seed = CoreMath.Fnv1a("run:" + run.Slot + ":" + act + ":" + term + ":" + run.Wins);

            // AI 打法（v2.1 P4）：精英/守关必激进；普通遭遇按节点种子轮换 ——
            // 同存档同节点永远同一打法（可背版、可复盘），不同节点之间有差异。
            var roll = (int)(seed % 3);
            var profile = (elite || boss)
                ? AiProfile.Aggressive
                : (roll == 0 ? AiProfile.Balanced
                 : roll == 1 ? AiProfile.Cautious
                 : AiProfile.Aggressive);

            req = new BattleRequest
            {
                // ★ 2026-10-22：秘境里的战斗标题不能写"第X幕第X节"（那是主线）。
                Title = (run != null && run.InRealm)
                    ? ("秘境 · " + (WanXiang.Campaign.RealmCatalog.For(run.RealmId) is var _rl && _rl != null ? _rl.Name : "场域") +
                       " · 第 " + (SceneFlow.PendingBattleNode != null && SceneFlow.PendingBattleNode.RealmLayer > 0
                                     ? SceneFlow.PendingBattleNode.RealmLayer : (term + 1)) + " 层 · " +
                       (champion ? "镇守" : elite ? "精英战" : "遭遇战"))
                    : ("第" + Cn(act) + "幕 · 第 " + (term + 1) + " 节 · " +
                       (champion ? "镇守" : boss ? "守关（首领）" : elite ? "精英战" : "遭遇战")),
                WeatherName = weather ?? "",
                // ★ 真实天时：从活链路容器取（CampaignPanel 选节点时算好的 WeatherDef）。
                //   为 null = 本场无天时，战斗行为与旧版逐位一致（可复现性红线）。
                Weather = WanXiang.Campaign.LiveWeather.Current,
                Seed = seed,
                AiProfile = profile,
                IsBoss = boss,
            };

            // ★ 真机验证用：本场实际带入战斗的天时。id 由 WeatherComposer.BuildId 拼出，
            //   含节点天时 id + 各余气 id（xingyi_* = 星移、linger_* = 幕间），
            //   一眼可辨"这场到底带没带天时、星移/余气有没有真正进战斗"（null = 旧行为）。
            var liveW = WanXiang.Campaign.LiveWeather.Current;
            if (liveW != null)
                UnityEngine.Debug.Log("[Weather] 本场天时注入战斗请求：" + liveW.BuffName +
                    "（属性 " + liveW.Element + "，id=" + liveW.Id + "）");
            else
                UnityEngine.Debug.Log("[Weather] 本场无天时（旧行为）");

            // ★ 镇守战：按**节点种子**从该场域的两名镇守里定一个 ——
            //   信息卡（CampaignPanel）用同一公式，保证"看到的"和"打到的"是同一个。
            //   （泽殍/涡姆、赤髓/灯奴、校核者·零/执行者·壹、无声者/哑钟、枯骨庄家/荷官·蛊）
            if (champion && run != null && run.InRealm)
            {
                var __rl = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
                var __pick = WanXiang.Campaign.RealmChampionPick.Pick(
                    __rl, term, WanXiang.Modules.UI.RealmSession.RealmSeedFor(run));
                req.RealmChampionHookId = __pick != null ? __pick.HookId : null;
            }

            // ★★ 秘境战斗：**不吃天时**（天时是天穹历法的产物，秘境在历法之外）。
            //   顶部横幅那行改成"场地"——把这场战斗里**真实生效**的场域危害讲清楚。
            //   ⛔ 不清 LiveWeather 的话，上一场主线节点合成好的天时会原样漏进秘境战斗
            //     （用户实测：秘境第 1 层的横幅写着"天时 蛰虫始振（木）：我方阵亡留虫卵…"）。
            if (run != null && run.InRealm)
            {
                var __rl = WanXiang.Campaign.RealmCatalog.For(run.RealmId);
                req.Weather = null;
                req.WeatherName = __rl != null
                    ? "场地 · " + WanXiang.Modules.UI.RealmText.Hazard(__rl)
                    : "场地 · （未知场域）";
                WanXiang.Campaign.LiveWeather.Current = null;
            }

            // ★ 2026-09-25：局外养成（等级/进化/觉醒技）已全部改为读**本局快照**，
            //   这里只处理"本场挂起修正"（孵穴回复 / 天象异闻）—— 与局外养成无关，
            //   所以**不再需要**去读 MetaStore（原来那处现读是漏进本局的根源之一）。
            // ★ 2026-11-01「五行俱全」：上阵 5 只**五种不同元素** ⇒ 全队 +10%（反应流的补偿）
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int i = 0; i < req.Player.Count; i++)
                {
                    var bd = req.Player[i];
                    if (bd != null && bd.Element != WanXiang.Battle.Core.Element.None) seen.Add((int)bd.Element);
                }
                if (req.Player.Count >= 5 && seen.Count >= 5)
                {
                    req.PlayerMul *= 1.10f;
                    UnityEngine.Debug.Log("[BattleRequestFactory] 五行俱全：5 种不同元素 → 全队 ×1.10");
                }
            }

            if (run.HealPending != 0 || run.PlayerBuffPct != 0 || run.EnemyBuffPct != 0
                || run.GrowthPct != 0)
            {
                // ★ 2026-10-20：加入本局永久成长（GrowthPct），它**不随本场清零**。
                req.PlayerMul = 1f + (run.HealPending + run.PlayerBuffPct + run.GrowthPct) / 100f;
                if (req.EnemyEntries != null)
                    foreach (var en in req.EnemyEntries)
                        en.WithMul(en.StatMul * (1f + run.EnemyBuffPct / 100f));
                run.HealPending = 0;
                run.PlayerBuffPct = 0;
                run.EnemyBuffPct = 0;
            }

            // ---- 我方：存档队伍按 id 回查 ----
            var byId = new Dictionary<string, BeastDef>();
            foreach (var b in all) byId[b.Id] = b;

            if (run.Team != null)
            {
                // ★ 本局快照：等级 / 进化 / 觉醒技 都在**开局那一刻**锁定（用户 2026-09-25 定案），
                //   局内中途回主城在「异兽培养」里怎么升级/进化/换觉醒技，都不影响这一局。
                run.EnsureMetaSnapshot();

                WanXiang.Modules.UI.SkillOverrideBootstrap.EnsureInstalled();   // ★ 技能表只加载一次
                WanXiang.Modules.UI.DataTables.EnsureInstalled();               // ★ 2026-11-02：状态/首领/天时/预算/地图
                WanXiang.Battle.Presentation.BattleStage2D.AwakenedDefIds.Clear();  // ★ 每场重建，避免上一局残留
                WanXiang.Modules.UI.EncounterBootstrap.EnsureInstalled();      // ★ 遭遇表 + 引用校验

                foreach (var id in run.Team)
                {
                    if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var def)) continue;
                    if (req.Player.Count >= 5 || req.Player.Contains(def)) continue;

                    // ⚠ 必须 Clone：byId[id] 是内容目录的共享引用，
                    //   直接改会污染全局（之后每场战斗都带着觉醒技）。
                    var d = def.Clone();

                    // ★ 2026-11-01 共享主等级：只放大**基础攻击/生命**（+6% / +7% 每级），
                    //   不加暴击/防御/技能 —— 数值成长只走这一条线（设计稿 §6.1）。
                    // ★ 2026-11-01 技能改写（设计稿 §8）：按覆盖表替换名字/说明/倍率
                    ApplySkillOverrides(id, d);

                    // ★ 2026-11-01 专精生效（设计稿 §6.2）：
                    //   Lv1 技能倍率 ×1.12；Lv2 段数 +1；Lv3 再 ×1.20（总 1.34）。无专精不动。
                    ApplyMastery(run, id, d);

                    // ★ 2026-11-01 进化：每只兽整局只能进化一次，进化后**基础攻/血 +10%**
                    //   （等级上限/专精上限/觉醒技的解锁在各自系统里读 run.IsEvolved）。
                    if (run.IsEvolved(id))
                    {
                        WanXiang.Battle.Presentation.BattleStage2D.AwakenedDefIds.Add(d.Id);
                        // ★ 2026-11-02：倍率读 ProgressionTuning（progression.csv 可覆盖）
                        d.BaseAtk = UnityEngine.Mathf.RoundToInt(d.BaseAtk * WanXiang.Run.ProgressionTuning.EvolveStatMul);
                        d.BaseHp = UnityEngine.Mathf.RoundToInt(d.BaseHp * WanXiang.Run.ProgressionTuning.EvolveStatMul);
                    }

                    // ⛔ 别在这里再抄一遍公式：直接读 run 的倍率（唯一来源 = ProgressionTuning）
                    int lvNow = UnityEngine.Mathf.Clamp(run.PlayerLevel, 1, WanXiang.Run.RunState.PlayerLevelMax);
                    if (lvNow > 1)
                    {
                        d.BaseAtk = UnityEngine.Mathf.RoundToInt(d.BaseAtk * run.LevelAtkMul);
                        d.BaseHp = UnityEngine.Mathf.RoundToInt(d.BaseHp * run.LevelHpMul);
                    }

                    // 觉醒技：取**快照**，不再读现档
                    string aid = run.SnapAwakenOf(id);
                    if (!string.IsNullOrEmpty(aid))
                    {
                        var sk = FindSkillById(all, aid);
                        if (sk != null)
                        {
                            d.Awaken = sk;
                            UnityEngine.Debug.Log("[BattleRequestFactory] 觉醒技注入（本局快照）：" +
                                                  d.DisplayName + " ← " + sk.Name);
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning("[BattleRequestFactory] 觉醒技 " + aid +
                                " 在内容目录里查不到（FindSkillById 失败）——检查该技能是否在异兽的 AllSkills 里");
                        }
                    }

                    req.Player.Add(d);
                    // 每只**各自**的战力倍率 = 等级（+1.6%/级）× 进化（+15%），同样取快照。
                    // ⚠ 与面板显示同一套公式（MetaDefaults.CombatBonusMul），别在两处各推一份。
                    req.PlayerMulPer.Add(WanXiang.Meta.MetaDefaults.CombatBonusMul(
                        run.SnapLevelOf(id), run.SnapEvolvedOf(id)));
                }
            }

            if (req.Player.Count == 0)
            {
                for (int i = 0; i < 5 && i < all.Length; i++)
                {
                    req.Player.Add(all[i]);
                    req.PlayerMulPer.Add(1f);
                }
            }

            // ---- 敌方：走正式内容供给（Campaign.SeededEnemyProvider）----
            // 它按 GDD §5.1/§5.5 的规模表定阵容大小、算属性倍率，
            // 并给精英战的 1 只挂「劫象」—— 这些是"兽 + 倍率"那种简版表达不了的。
            // 池子暂用全图鉴（后续按幕/季节过滤，接口已经留好）。
            //
            // ★ 首领战接入（P0 收尾）：把 BossCatalog 的确定性抽首领工厂塞给 provider ——
            //   同局同幕固定抽到同一个首领（可复盘），换局换人。
            //   守关节点走 BossSquadFor（首领 + 随从），普通节点仍走 EnemiesFor。
            //
            // ★ 轮回解锁池（低轮回开 2 个、高轮回全开）：把本局轮回数带进抽取 ——
            //   Ascension < UnlockAscension 时每幕只在池子前 2 个里摇，第 3 个留到高轮回。
            //   （须在构造 provider 之前读出 Ascension，故提到此处；下面的难度倍率复用同一变量。）
            var runAsc = WanXiang.Run.RunSave.Current;
            int ascension = runAsc != null ? runAsc.Ascension : 0;

            var provider = new WanXiang.Campaign.SeededEnemyProvider(
                actIdx => all, BossCatalog.BossForActFunc(seed, ascension));
            // ★ 镇守走**首领规格**（BossSquadFor = 首领 + 随从），而不是普通遭遇：
            //   它是"值得为它绕路"的硬仗，得打得出来不一样的形状。
            var entries = (boss || champion)
                ? provider.BossSquadFor(act, seed)
                : provider.EnemiesFor(act, term, kind, seed);

            // ★★ 2026-11-02 表驱动编队：命中 encounters.csv 就**按表**部署（含站位），否则回退 provider。
            //   表的 nodeKind 只有 Encounter / Elite / Boss 三种；其余节点（事件/商店）不会进战斗。
            {
                EncounterBootstrap.EnsureInstalled();
                string kindName = (boss || champion) ? "Boss"
                                : (kind == WanXiang.Campaign.NodeKind.Elite) ? "Elite" : "Encounter";
                var row = WanXiang.Campaign.EncounterTable.Pick(act, kindName, seed);
                if (row != null && row.Entries != null && row.Entries.Length > 0)
                {
                    var list = new System.Collections.Generic.List<WanXiang.Battle.Core.DeployEntry>();
                    for (int ei = 0; ei < row.Entries.Length; ei++)
                    {
                        var en = row.Entries[ei];
                        // ⛔ id 先查首领表（b_ 前缀），查不到按**异兽图鉴**解析 ——
                        //   普通/精英战斗里的"怪"用的是同一批异兽，不再是 Boss 立绘。
                        var def = WanXiang.Battle.Core.BossCatalog.BuildBoss(en.Id);
                        if (def == null && all != null)
                        {
                            for (int bi = 0; bi < all.Length; bi++)
                                if (all[bi] != null && all[bi].Id == en.Id) { def = all[bi].Clone(); break; }
                        }
                        if (def == null) continue;
                        // 首领战：第一条是本体吃满 scale，其余随从按旧的 BossSidekickMul(0.55) 打折
                        float mul = row.Scale;
                        if (kindName == "Boss" && ei > 0) mul *= WanXiang.Campaign.SeededEnemyProvider.BossSidekickMul;
                        // 精英/首领单独调 HP（不改攻击）：表里 hpMul 列，默认 1
                        if (row.HpMul > 0f && row.HpMul != 1f)
                            def.BaseHp = UnityEngine.Mathf.RoundToInt(def.BaseHp * row.HpMul);
                        StripLegacyOnPlayerBeast(def);        // 敌方同样：CD 归零 + 清旧状态（用户：全部删掉）
                        ApplyEnemySkillOverrides(def);       // 敌方技能（配合向）
                        list.Add(WanXiang.Battle.Core.DeployEntry.Enemy(def, en.Slot).WithMul(mul));
                    }
                    if (list.Count > 0)
                    {
                        entries = list.ToArray();
                        LastEncounter = row;
                        req.Title = row.Name;      // 战斗标题直接用编队名（"藤影伏击"），比"遭遇战"有信息量
                        UnityEngine.Debug.Log("[BattleRequestFactory] 遭遇表命中：" + row.Name +
                                              "（" + kindName + "·第" + act + "幕）" + list.Count + " 只，" +
                                              "强度×" + row.Scale.ToString("0.00") + "｜" + row.Synergy);
                    }
                }
            }
            if (boss)
            {
                string bossId = entries.Length > 0 && entries[0].Def != null ? entries[0].Def.Id : "(无)";
                int actIdx = System.Math.Max(0, System.Math.Min(BossCatalog.Pool.Length - 1, act - 1));
                UnityEngine.Debug.Log("[BattleRequestFactory] 守关首领战：第" + act +
                    "幕 抽出 " + bossId + "（BossFor(seed) 确定性；轮回 " + ascension +
                    " ， 池开 " + BossCatalog.UnlockedCount(act, ascension) + "/" +
                    BossCatalog.Pool[actIdx].Length + "）");
            }

            // ★★ 轮回难度（"续劫"次数）：每轮回敌人属性 +15%
            //    —— 之前的"敌强 +3%"只写在注释里，实际没有任何代码读取（Jie/Realm 已废弃）。
            float ascMul = 1f + 0.15f * ascension;

            // ★ 秘境强度（2026-10-16）：场域的 enemyScale 与幕数加成**相乘但语义不同** ——
            //   幕数加成是"越往后越强"，秘境倍率是"这一局额外加压"。
            //   ⛔ 不在场域里时恒为 1.0 ⇒ 非秘境战斗逐位不变（指纹红线）。
            float realmScale = RealmSession.EnemyScaleFor(run);
            float enemyMul = ascMul * realmScale;

            // ★ 场域代价（我方减速）：只乘我方出手速度，与敌方强度倍率完全独立的两个轴。
            //   ⛔ 不在秘境时恒为 1 ⇒ 非秘境战斗逐位不变。
            req.RealmSpeedMulPlayer = RealmSession.PlayerSpeedMulFor(run);
            if (req.RealmSpeedMulPlayer != 1f)
                UnityEngine.Debug.Log("[BattleRequestFactory] 秘境场域代价：我方速度 ×" +
                                      req.RealmSpeedMulPlayer.ToString("0.00"));

            req.EnemyEntries.Clear();
            req.Enemy.Clear();
            req.EnemyMul.Clear();
            foreach (var en in entries)
            {
                var scaled = en.WithMul(en.StatMul * enemyMul);
                req.EnemyEntries.Add(scaled);
                req.Enemy.Add(scaled.Def);      // 兼容通道：HUD/预览按 BeastDef 显示名字
                req.EnemyMul.Add(scaled.StatMul);
            }
            if (realmScale != 1f)
                UnityEngine.Debug.Log("[BattleRequestFactory] 秘境「"
                    + (WanXiang.Campaign.RealmCatalog.For(run.RealmId) != null
                       ? WanXiang.Campaign.RealmCatalog.For(run.RealmId).Name : run.RealmId)
                    + "」敌人强度 ×" + realmScale.ToString("0.00"));
            if (ascMul > 1f)
                UnityEngine.Debug.Log("[BattleRequestFactory] 轮回 " + (runAsc != null ? runAsc.Ascension : 0) +
                                      " ， 敌人属性 ×" + ascMul.ToString("0.00"));

            // ★★ 轮回难度（"续劫"）：每轮回**敌人数量 +1**，上限 **9 只**（棋盘共 9 格）。
            //    与上面的"属性 +15%/轮回"叠加 —— 数量 + 强度双重递增。
            int extra = System.Math.Min(9 - req.EnemyEntries.Count,
                                        runAsc != null ? runAsc.Ascension : 0);
            if (extra > 0 && all != null && all.Length > 0)
            {
                var rng2 = new System.Random((int)(seed ^ 0x9E3779B9u));
                for (int i = 0; i < extra; i++)
                {
                    var pick = all[rng2.Next(all.Length)];
                    // 格号：基础敌人占前几格 ， 额外敌人从它们的后面接着排（0..8 共 9 格）
                    int cell = (req.EnemyEntries.Count + i) % 9;
                    var add = DeployEntry.Enemy(pick, cell).WithMul(0.9f * ascMul);
                    req.EnemyEntries.Add(add);
                    req.Enemy.Add(pick);
                    req.EnemyMul.Add(add.StatMul);
                }
                UnityEngine.Debug.Log("[BattleRequestFactory] 轮回 " + runAsc.Ascension +
                                      " ， 敌人额外 +" + extra + " 只");
            }

            // ★★ 遗物（杀戮尖塔式局内成长）：把本局已持有遗物折叠成战斗增益。
            //   玩家属性% 折进 req.PlayerMul（与孵穴/天象挂起加成叠加）；
            //   敌方弱化% 折进每个 EnemyEntries.StatMul；复活/开局灵力置开关。
            //   纯映射现有管线，不引入战斗核心改动。
            WanXiang.Modules.UI.SceneFlow.LastBattleKind = kind;   // 结算面板据此定遗物权重
            WanXiang.Modules.UI.SceneFlow.LastBattleWasBoss = boss; // ★ 首领遗物只在首领战掉
            if (run.Relics != null && run.Relics.Count > 0)
            {
                int teamCount = req.Player.Count;
                var mods = WanXiang.Campaign.RelicCatalog.Accumulate(run.Relics, teamCount, run.Wins);

                // ① 全队倍率（基础数值 / 双面收益 / 累计 / 限时 / 众寡术）
                req.PlayerMul *= mods.PlayerMul;

                // ② 逐单位倍率：五行专属 / 定位专属 / 异兽专属遗物（落在 PlayerMulPer[i]）
                for (int i = 0; i < req.Player.Count && i < req.PlayerMulPer.Count; i++)
                {
                    var b = req.Player[i];
                    if (b == null) continue;
                    float per = req.PlayerMulPer[i];
                    per *= mods.MulFor(b.Element);   // 五行灵符
                    per *= mods.RoleFor(b.Role);     // 定位契印
                    per *= mods.BeastFor(b.Id);      // 异兽契印
                    req.PlayerMulPer[i] = per;
                }

                // ③ 敌方：全属性弱化 × 指定五行弱化
                if (req.EnemyEntries != null)
                    for (int i = 0; i < req.EnemyEntries.Count; i++)
                    {
                        var en = req.EnemyEntries[i];
                        float mul = en.StatMul * mods.EnemyMul;
                        if (en.Def != null) mul *= mods.WeakFor(en.Def.Element);
                        req.EnemyEntries[i] = en.WithMul(mul);
                    }

                // ④ 复活 / 开局灵力（弃生契会强制关掉复活）
                if (mods.ReviveOn) { req.PlayerReviveOn = true; req.PlayerReviveHpPercent = mods.ReviveHpPercent; }
                else if (mods.ReviveForbidden) req.PlayerReviveOn = false;
                req.PlayerStartMana += mods.StartMana;

                // ⑤ 机制类遗物（v1.2）：改技能 / 授予劫象 / 改规则 —— 改的是**克隆兽**，不污染内容表
                if (req.PlayerTrait == null) req.PlayerTrait = new System.Collections.Generic.List<string>();
                req.PlayerTrait.Clear();
                int mechCount = 0;
                for (int i = 0; i < req.Player.Count; i++)
                {
                    var b = req.Player[i];
                    string traitId = b != null
                        ? WanXiang.Campaign.RelicCatalog.ApplyBeastMechanics(b, run.Relics) : null;
                    if (!string.IsNullOrEmpty(traitId)) mechCount++;
                    req.PlayerTrait.Add(traitId ?? "");
                }
                int turnsDelta = WanXiang.Campaign.RelicCatalog.RuleTurnsDelta(run.Relics);
                req.MaxTurnsDelta += turnsDelta;

                // ★ 2026-11-01 事件的"限时 N 回合"：换算成相对 48 的差值，打完即清（一场有效）
                if (run.EventTurnLimit > 0)
                {
                    req.MaxTurnsDelta += run.EventTurnLimit - 48;
                    UnityEngine.Debug.Log("[BattleRequestFactory] 事件限时：" + run.EventTurnLimit + " 回合（MaxTurnsDelta=" + req.MaxTurnsDelta + "）");
                    run.EventTurnLimit = 0;
                }
                if (WanXiang.Campaign.RelicCatalog.HasMechanic(run.Relics, WanXiang.Campaign.RelicMechanic.UltimateNoRage))
                    req.UltimateNoRage = true;
                if (WanXiang.Campaign.RelicCatalog.HasMechanic(run.Relics, WanXiang.Campaign.RelicMechanic.NoHitChance))
                    req.NoHitChance = true;
                if (WanXiang.Campaign.RelicCatalog.HasMechanic(run.Relics, WanXiang.Campaign.RelicMechanic.EnemyCautious))
                    req.AiProfile = AiProfile.Cautious;

                UnityEngine.Debug.Log("[BattleRequestFactory] 遗物生效：" + run.Relics.Count +
                    " 件（全队x" + mods.PlayerMul.ToString("0.000") + " 敌x" + mods.EnemyMul.ToString("0.000") +
                    " 复活" + req.PlayerReviveOn + " 灵力+" + mods.StartMana +
                    " 队" + teamCount + " 胜" + run.Wins + "）");
            }

            return req.Player.Count > 0 && req.EnemyEntries.Count > 0;
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
    
        /// <summary>按技能 id 在全部异兽的技能里找（觉醒技池来源：90 个技能）。</summary>
        /// <summary>
        /// 我方异兽净化：①所有技能 CD = 0（用户：带 CD 的机制全部删掉）
        /// ②移除效果里所有 ApplyStatus（旧状态：湿/冰蚀/裂甲/斩标/霜…，先清空，之后按新反应重加）
        /// ③清掉 Trait（劫象是**敌方**词条，不该长在我方兽身上）
        /// </summary>
        private static void StripLegacyOnPlayerBeast(WanXiang.Battle.Core.BeastDef d)
        {
            if (d == null) return;
            d.Trait = default;
            var slots = new[] { d.Basic, d.Active, d.Ultimate, d.Awaken };
            for (int i = 0; i < slots.Length; i++)
            {
                var sk = slots[i];
                if (sk == null) continue;
                sk.Cd = 0;
                if (sk.Effects == null) continue;
                var keep = new System.Collections.Generic.List<WanXiang.Battle.Core.EffectAtom>(sk.Effects.Length);
                for (int e = 0; e < sk.Effects.Length; e++)
                {
                    var a = sk.Effects[e];
                    if (a.Kind == WanXiang.Battle.Core.EffectAtomKind.ApplyStatus) continue;
                    keep.Add(a);
                }
                // ⛔ 全被清空 ⇒ 技能变成"空技能"，AI/玩家都没东西可放 ⇒ **回合卡死**（用户实测）。
                //   兜底：补一个 1.0× 单体伤害，保证每条技能永远有动作。
                if (keep.Count == 0)
                    keep.Add(WanXiang.Battle.Core.EffectAtom.Damage(
                        WanXiang.Battle.Core.TargetSelector.SingleFrontMost, 1.0f));
                sk.Effects = keep.ToArray();
            }
        }

        /// <summary>敌方专用技能覆盖（如"嘲讽挡路""全队回春"），让敌人之间有分工。</summary>
        private static void ApplyEnemySkillOverrides(WanXiang.Battle.Core.BeastDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) return;
            var slots = new[] { d.Basic, d.Active, d.Ultimate };
            for (int slot = 0; slot < slots.Length; slot++)
            {
                var ov = WanXiang.Campaign.SkillOverrideCatalog.GetEnemy(d.Id, slot);
                var sk = slots[slot];
                if (ov == null || sk == null) continue;
                if (!string.IsNullOrEmpty(ov.Name)) sk.Name = ov.Name;
                if (!string.IsNullOrEmpty(ov.Target))
                {
                    WanXiang.Battle.Core.TargetSelector ts;
                    switch (ov.Target.ToLowerInvariant())
                    {
                        case "front":   ts = WanXiang.Battle.Core.TargetSelector.SingleFrontMost; break;
                        case "back":    ts = WanXiang.Battle.Core.TargetSelector.SingleBackMost; break;
                        case "mid":     ts = WanXiang.Battle.Core.TargetSelector.SingleMidRow; break;
                        case "lowest":  ts = WanXiang.Battle.Core.TargetSelector.SingleLowestHp; break;
                        case "highest": ts = WanXiang.Battle.Core.TargetSelector.SingleHighestHp; break;
                        default:        ts = sk.PrimaryTarget; break;
                    }
                    sk.PrimaryTarget = ts;
                    if (sk.Effects != null)
                        for (int e2 = 0; e2 < sk.Effects.Length; e2++)
                        {
                            var at2 = sk.Effects[e2];
                            if (at2.Target != WanXiang.Battle.Core.TargetSelector.Self) at2.Target = ts;
                            sk.Effects[e2] = at2;
                        }
                }
                if (!string.IsNullOrEmpty(ov.Desc)) sk.Description = ov.Desc;
                if (sk.Effects != null && ov.PowerMul != 1f)
                    for (int e = 0; e < sk.Effects.Length; e++)
                    {
                        var a = sk.Effects[e];
                        if (a.Power > 0f) a.Power *= ov.PowerMul;
                        sk.Effects[e] = a;
                    }
            }
        }

        /// <summary>按覆盖表改写该兽的技能（名字/说明/倍率/段数），只作用于克隆体。</summary>
        private static void ApplySkillOverrides(string beastId, WanXiang.Battle.Core.BeastDef d)
        {
            if (d == null) return;
            var slots = new[] { d.Basic, d.Active, d.Ultimate };
            for (int slot = 0; slot < slots.Length; slot++)
            {
                var ov = WanXiang.Campaign.SkillOverrideCatalog.Get(beastId, slot);
                var sk = slots[slot];
                if (ov == null || sk == null) continue;

                if (!string.IsNullOrEmpty(ov.Name)) sk.Name = ov.Name;
                if (!string.IsNullOrEmpty(ov.Desc)) sk.Description = ov.Desc;
                if (sk.Effects != null)
                {
                    for (int e = 0; e < sk.Effects.Length; e++)
                    {
                        var a = sk.Effects[e];
                        if (a.Power > 0f) a.Power *= ov.PowerMul;
                        if (ov.HitsDelta != 0) a.Hits = System.Math.Max(1, a.Hits) + ov.HitsDelta;
                        sk.Effects[e] = a;
                    }
                }
            }
        }

        /// <summary>把该兽 3 个技能槽的专精等级作用到克隆体上（不改内容目录）。</summary>
        private static void ApplyMastery(WanXiang.Run.RunState run, string beastId, WanXiang.Battle.Core.BeastDef d)
        {
            if (run == null || d == null) return;
            var slots = new[] { d.Basic, d.Active, d.Ultimate };
            for (int slot = 0; slot < slots.Length; slot++)
            {
                var sk = slots[slot];
                if (sk == null || sk.Effects == null) continue;
                int m = run.MasteryOf(beastId, slot);
                if (m <= 0) continue;
                float mul = 1f;
                if (m >= 1) mul *= WanXiang.Run.ProgressionTuning.MasteryLv1Mul;
                if (m >= 3) mul *= WanXiang.Run.ProgressionTuning.MasteryLv3Mul;
                for (int e = 0; e < sk.Effects.Length; e++)
                {
                    var atom = sk.Effects[e];
                    if (atom.Power > 0f) { atom.Power *= mul; }
                    if (m >= 2) atom.Hits = System.Math.Max(1, atom.Hits) + 1;   // Lv2：段数 +1
                    sk.Effects[e] = atom;
                }
            }
        }

        private static WanXiang.Battle.Core.SkillDef FindSkillById(BeastDef[] all, string skillId)
        {
            if (all == null || string.IsNullOrEmpty(skillId)) return null;
            for (int i = 0; i < all.Length; i++)
            {
                var arr = all[i].AllSkills;
                for (int k = 0; k < arr.Length; k++)
                    if (arr[k] != null && arr[k].Id == skillId) return arr[k];
            }
            return null;
        }
}
}
