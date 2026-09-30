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
            bool elite = kind == WanXiang.Campaign.NodeKind.Elite;
            // ★ 守关：图里最后一格复用 【精英】占位，靠 isBoss 标志区分（普通精英 vs 首领）。
            bool boss = isBoss && kind == WanXiang.Campaign.NodeKind.Elite;

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
                Title = "第" + Cn(act) + "幕 · 第 " + (term + 1) + " 节 · " +
                        (boss ? "守关（首领）" : elite ? "精英战" : "遭遇战"),
                WeatherName = weather ?? "",
                // ★ 真实天时：从活链路容器取（CampaignPanel 选节点时算好的 WeatherDef）。
                //   为 null = 本场无天时，战斗行为与旧版逐位一致（可复现性红线）。
                Weather = WanXiang.Campaign.LiveWeather.Current,
                Seed = seed,
                AiProfile = profile,
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

            // ★ 2026-09-25：局外养成（等级/进化/觉醒技）已全部改为读**本局快照**，
            //   这里只处理"本场挂起修正"（孵穴回复 / 天象异闻）—— 与局外养成无关，
            //   所以**不再需要**去读 MetaStore（原来那处现读是漏进本局的根源之一）。
            if (run.HealPending != 0 || run.PlayerBuffPct != 0 || run.EnemyBuffPct != 0)
            {
                req.PlayerMul = 1f + (run.HealPending + run.PlayerBuffPct) / 100f;
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

                foreach (var id in run.Team)
                {
                    if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var def)) continue;
                    if (req.Player.Count >= 5 || req.Player.Contains(def)) continue;

                    // ⚠ 必须 Clone：byId[id] 是内容目录的共享引用，
                    //   直接改会污染全局（之后每场战斗都带着觉醒技）。
                    var d = def.Clone();

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
            var provider = new WanXiang.Campaign.SeededEnemyProvider(
                actIdx => all, BossCatalog.BossForActFunc(seed));
            var entries = boss
                ? provider.BossSquadFor(act, seed)
                : provider.EnemiesFor(act, term, kind, seed);
            if (boss)
            {
                string bossId = entries.Length > 0 && entries[0].Def != null ? entries[0].Def.Id : "(无)";
                UnityEngine.Debug.Log("[BattleRequestFactory] 守关首领战：第" + act +
                    "幕 抽出 " + bossId + "（BossFor(seed) 确定性）");
            }

            // ★★ 轮回难度（"续劫"次数）：每轮回敌人属性 +15%
            //    —— 之前的"敌强 +3%"只写在注释里，实际没有任何代码读取（Jie/Realm 已废弃）。
            var runAsc = WanXiang.Run.RunSave.Current;
            float ascMul = 1f + 0.15f * (runAsc != null ? runAsc.Ascension : 0);

            req.EnemyEntries.Clear();
            req.Enemy.Clear();
            req.EnemyMul.Clear();
            foreach (var en in entries)
            {
                var scaled = en.WithMul(en.StatMul * ascMul);
                req.EnemyEntries.Add(scaled);
                req.Enemy.Add(scaled.Def);      // 兼容通道：HUD/预览按 BeastDef 显示名字
                req.EnemyMul.Add(scaled.StatMul);
            }
            if (ascMul > 1f)
                UnityEngine.Debug.Log("[BattleRequestFactory] 轮回 " + (runAsc != null ? runAsc.Ascension : 0) +
                                      " ⇒ 敌人属性 ×" + ascMul.ToString("0.00"));

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
                    // 格号：基础敌人占前几格 ⇒ 额外敌人从它们的后面接着排（0..8 共 9 格）
                    int cell = (req.EnemyEntries.Count + i) % 9;
                    var add = DeployEntry.Enemy(pick, cell).WithMul(0.9f * ascMul);
                    req.EnemyEntries.Add(add);
                    req.Enemy.Add(pick);
                    req.EnemyMul.Add(add.StatMul);
                }
                UnityEngine.Debug.Log("[BattleRequestFactory] 轮回 " + runAsc.Ascension +
                                      " ⇒ 敌人额外 +" + extra + " 只");
            }

            // ★★ 遗物（杀戮尖塔式局内成长）：把本局已持有遗物折叠成战斗增益。
            //   玩家属性% 折进 req.PlayerMul（与孵穴/天象挂起加成叠加）；
            //   敌方弱化% 折进每个 EnemyEntries.StatMul；复活/开局灵力置开关。
            //   纯映射现有管线，不引入战斗核心改动。
            WanXiang.Modules.UI.SceneFlow.LastBattleKind = kind;   // 结算面板据此定遗物权重
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
