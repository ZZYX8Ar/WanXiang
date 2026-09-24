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
                Seed = seed,
            };

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
                                           int termIndex = -1)
        {
            req = null;
            if (catalog == null || run == null) return false;

            var all = ContentLibrary.BuildBeasts(catalog);
            if (all == null || all.Length < 2) return false;

            int act = System.Math.Max(1, System.Math.Min(run.Act, 5));
            int term = termIndex >= 0 ? termIndex : System.Math.Max(0, run.NodeOffset);
            bool elite = kind == WanXiang.Campaign.NodeKind.Elite;

            // 种子：同存档同幕同节点 → 同一套敌人（可背版、可复盘）
            ulong seed = CoreMath.Fnv1a("run:" + run.Slot + ":" + act + ":" + term + ":" + run.Wins);

            // AI 打法（v2.1 P4）：精英必激进；普通遭遇按节点种子轮换 ——
            // 同存档同节点永远同一打法（可背版、可复盘），不同节点之间有差异。
            var roll = (int)(seed % 3);
            var profile = elite
                ? AiProfile.Aggressive
                : (roll == 0 ? AiProfile.Balanced
                 : roll == 1 ? AiProfile.Cautious
                 : AiProfile.Aggressive);

            req = new BattleRequest
            {
                Title = "第" + Cn(act) + "幕 · 第 " + (term + 1) + " 节 · " +
                        (elite ? "精英战" : "遭遇战"),
                WeatherName = weather ?? "",
                Seed = seed,
                AiProfile = profile,
            };

            // L4 祭坛：五行等级和 × 1.6% —— 局外永久数值线，每场都生效（总封顶 +8%）
            int altarSum = 0;
            if (run.MetaAltar != null)
                foreach (var lv in run.MetaAltar) altarSum += lv;

            // 挂起修正（孵穴回复 / 天象异闻）：进本场后清零
            // ★ 触发条件也要看【局外祭坛】(metaA)：否则局内 altarSum=0 时整段被跳过，
            //   局外的永久加成永远不生效。
            var metaGate = WanXiang.Meta.MetaStore.Ensure();
            bool hasMetaAltar = metaGate != null && metaGate.BeastIds.Count > 0;
            // ⚠ 变量名沿用（内容已改为"是否有局外培养数据"）；祭坛已废弃。

            if (run.HealPending != 0 || run.PlayerBuffPct != 0 || run.EnemyBuffPct != 0 ||
                altarSum > 0 || hasMetaAltar)
            {
                // ★ 局外成长改为【异兽培养】等级（替代原"祭坛"）：
                //   队伍里每只兽按**各自**的局外等级给 +1.6%/级（5 级 = +8%），
                //   取全队平均作为本场我方倍率 —— 培养哪只就强在哪只身上。
                float beastBonus = 0f;
                int counted = 0;
                if (metaGate != null && metaGate.BeastIds.Count > 0)
                {
                    int sum = 0;
                    foreach (var id in run.Team)
                    {
                        sum += metaGate.BeastLevelOf(id);
                        counted++;
                    }
                    if (counted > 0) beastBonus = sum / (float)counted * 0.016f;
                }
                req.PlayerMul = 1f + beastBonus + (run.HealPending + run.PlayerBuffPct) / 100f;
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
                // ★ 局外觉醒技注入：队伍里每只兽带上【它装备的觉醒技】（第 4 技能）。
                var metaAw = WanXiang.Meta.MetaStore.Ensure();
                foreach (var id in run.Team)
                {
                    if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var def)) continue;
                    if (req.Player.Count >= 5 || req.Player.Contains(def)) continue;

                    // ⚠ 必须 Clone：byId[id] 是内容目录的共享引用，
                    //   直接改会污染全局（之后每场战斗都带着觉醒技）。
                    var d = def.Clone();
                    if (metaAw != null)
                    {
                        string aid = metaAw.AwakenOf(id);
                        // ★ 诊断日志：觉醒技按钮不显示时，看这里就知道断在哪一环
                        //   （没装备记录 / 装备了但技能 id 在目录里查不到）。
                        UnityEngine.Debug.Log("[BattleRequestFactory][调试] 觉醒技检查：" + d.DisplayName +
                                  " id=" + id + " → 装备记录=" + (string.IsNullOrEmpty(aid) ? "（无）" : aid));                        if (!string.IsNullOrEmpty(aid))
                        {
                            var sk = FindSkillById(all, aid);
                            if (sk != null)
                            {
                                d.Awaken = sk;
                                UnityEngine.Debug.Log("[BattleRequestFactory] 觉醒技注入：" +
                                                      d.DisplayName + " ← " + sk.Name);
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning("[BattleRequestFactory] 觉醒技 " + aid +
                                    " 在内容目录里查不到（FindSkillById 失败）——检查该技能是否在异兽的 AllSkills 里");
                            }
                        }
                    }
                    req.Player.Add(d);
                }
            }

            if (req.Player.Count == 0)
                for (int i = 0; i < 5 && i < all.Length; i++) req.Player.Add(all[i]);

            // ---- 敌方：走正式内容供给（Campaign.SeededEnemyProvider）----
            // 它按 GDD §5.1/§5.5 的规模表定阵容大小、算属性倍率，
            // 并给精英战的 1 只挂「劫象」—— 这些是"兽 + 倍率"那种简版表达不了的。
            // 池子暂用全图鉴（后续按幕/季节过滤，接口已经留好）。
            var provider = new WanXiang.Campaign.SeededEnemyProvider(actIdx => all);
            var entries = provider.EnemiesFor(act, term, kind, seed);

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
