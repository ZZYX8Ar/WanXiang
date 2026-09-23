// ============================================================================
//  万相 · 局外存档读写（MetaState 的持久化 + 全局实例）
//  ---------------------------------------------------------------------------
//  `MetaState` / `MetaSaveCode` 一直存在，但**没有任何文件读写与全局实例** ——
//  所以局外养成完全是空转（UI 层零调用，玩家看不到任何跨局积累）。
//
//  本文件补齐三件事：
//    ① `MetaStore.Current`：全局唯一的局外存档实例（跨场景保持）
//    ② `LoadOrCreate()`：首次进入时读档，没有就按内容目录建新档
//    ③ `Save()`：写回 `Application.persistentDataPath/meta.sav`
//
//  ⚠ 放在 UI 程序集（而不是 Meta 程序集）：Meta 程序集不引用 UnityEngine，
//    拿不到 `Application.persistentDataPath`。
// ============================================================================

using System.IO;
using UnityEngine;

namespace WanXiang.Meta
{
    public static class MetaStore
    {
        /// <summary>全局唯一的局外存档（跨场景保持）。未初始化时为 null。</summary>
        public static MetaState Current { get; private set; }

        /// <summary>存档路径（每台机器一个，与槽位存档分开）。</summary>
        private static string FilePath
            => Path.Combine(Application.persistentDataPath, "wanxiang_meta.sav");

        /// <summary>
        /// 读档；没有存档就新建一局。可反复调用（已加载时直接返回）。
        /// </summary>
        /// <param name="content">内容目录的规模信息（决定初始解锁池）。</param>
        public static MetaState LoadOrCreate(MetaContent content, ulong seed = 20260922UL)
        {
            if (Current != null) return Current;

            try
            {
                if (File.Exists(FilePath))
                {
                    var code = File.ReadAllText(FilePath);
                    if (MetaSaveCode.TryDecode(code, out var decoded))
                    {
                        Current = decoded;
                        LoadHistory();
                        Debug.Log("[MetaStore] 已载入局外存档：墨铊 " + Current.Ink +
                                  "｜局数 " + Current.RunsPlayed + "｜最远第 " + Current.BestActReached + " 幕" +
                                  "｜解锁宿主 " + Current.UnlockedHosts.Count + " 灵魂 " + Current.UnlockedSouls.Count);
                        return Current;
                    }
                    Debug.LogWarning("[MetaStore] 存档解码失败（版本不匹配？），将新建");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MetaStore] 读档异常：" + e.Message + "，将新建");
            }

            Current = MetaState.NewGame(seed, content);
            Debug.Log("[MetaStore] 已新建局外存档：解锁宿主 " + Current.UnlockedHosts.Count +
                      " 灵魂 " + Current.UnlockedSouls.Count);
            Save();
            return Current;
        }

        /// <summary>
        /// 历程文件的路径（独立于 MetaSaveCode，避免二进制布局越改越复杂）。
        /// ★ 按【存档槽位】隔离：每个存档一份历程（用户要求：每个存档都是独立的）。
        ///   ActiveSlot 0 = 尚未选档 → 归入 1 号档。
        ///   旧的 wanxiang_history.sav 是全局共用文件（已污染），不再读取 —— 等于自然清空。
        /// </summary>
        private static string HistoryPath
        {
            get
            {
                int slot = WanXiang.Run.RunSave.ActiveSlot > 0 ? WanXiang.Run.RunSave.ActiveSlot : 1;
                return Path.Combine(Application.persistentDataPath, "wanxiang_history_s" + slot + ".sav");
            }
        }

        /// <summary>把 History 写盘（每行一条：act|power|beasts|code|time）。</summary>
        public static void SaveHistory()
        {
            if (Current == null) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Current.HistActs.Count; i++)
                {
                    sb.Append(i < Current.HistRunIds.Count ? Current.HistRunIds[i] : "").Append('|')
                      .Append(Current.HistActs[i]).Append('|')
                      .Append(i < Current.HistPowers.Count ? Current.HistPowers[i] : 0).Append('|')
                      .Append(i < Current.HistBeasts.Count ? Current.HistBeasts[i] : "").Append('|')
                      .Append(i < Current.HistCodes.Count ? Current.HistCodes[i] : "").Append('|')
                      .Append(i < Current.HistTimes.Count ? Current.HistTimes[i] : "")
                      .Append('\n');
                }
                File.WriteAllText(HistoryPath, sb.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[MetaStore] 历程写入失败：" + e.Message); }
        }

        /// <summary>读历程（读档时调用一次）。</summary>
        public static void LoadHistory()
        {
            if (Current == null) return;
            if (!File.Exists(HistoryPath)) return;
            try
            {
                foreach (var line in File.ReadAllLines(HistoryPath))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length < 6) continue;      // v2: runId|act|power|beasts|code|time
                    int act, power;
                    if (!int.TryParse(parts[1], out act)) continue;
                    if (!int.TryParse(parts[2], out power)) power = 0;
                    Current.UpsertHistory(parts[0], act, power, parts[3], parts[4], parts[5]);
                }
                Debug.Log("[MetaStore] 已载入历程 " + Current.HistActs.Count + " 条");
            }
            catch (System.Exception e) { Debug.LogWarning("[MetaStore] 历程读入失败：" + e.Message); }
        }

        /// <summary>写回磁盘。任何改动局外存档后都应调用一次。</summary>
        public static void Save()
        {
            if (Current == null) return;
            try
            {
                File.WriteAllText(FilePath, MetaSaveCode.Encode(Current));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MetaStore] 存档写入失败：" + e.Message);
            }
        }

        /// <summary>
        /// 从内容目录构建 MetaContent（局外存档需要知道"池子里有几只、各自什么稀有度"）。
        /// </summary>
        public static MetaContent BuildContentFromCatalog()
        {
            var cats = Resources.FindObjectsOfTypeAll<WanXiang.Fusion.ContentCatalogSO>();
            if (cats == null || cats.Length == 0)
            {
                Debug.LogWarning("[MetaStore] 找不到 ContentCatalogSO，用空内容兜底");
                return new MetaContent { HostCount = 5, SoulCount = 5 };
            }
            var all = WanXiang.Fusion.ContentLibrary.BuildBeasts(cats[0]);
            var content = new MetaContent
            {
                HostCount = all.Length,
                SoulCount = all.Length,
                HostRarities = new WanXiang.Battle.Core.Rarity[all.Length],
                SoulRarities = new WanXiang.Battle.Core.Rarity[all.Length],
            };
            for (int i = 0; i < all.Length; i++)
            {
                content.HostRarities[i] = all[i].Rarity;
                content.SoulRarities[i] = all[i].Rarity;
            }
            return content;
        }

        /// <summary>确保已初始化（任何入口都可安全调用）。</summary>
        public static MetaState Ensure()
            => Current != null ? Current : LoadOrCreate(BuildContentFromCatalog());

        /// <summary>
        /// 结算一局（归元 / 通关 / 失败都走这里）：把本局成果换算成局外灵卵。
        /// </summary>
        /// <param name="run">本局存档（用它的路线统计节点数）。</param>
        /// <param name="cleared">是否通关（登天阙打赢）。</param>
        public static MetaRewards.RunIncome SettleRun(WanXiang.Run.RunState run, bool cleared)
        {
            var meta = Ensure();
            if (run == null || meta == null) return default;

            int nodes = run.VisitedNodes != null ? run.VisitedNodes.Count : 0;
            int bosses = 0;
            // 精英/守关节点数：按类型无法回溯（VisitedNodes 只有下标），
            // 用"每幕 1 场精英 + 守关 1 场"估算，保证换算有意义。
            bosses = System.Math.Max(1, System.Math.Min(4, run.Act));
            int actReached = System.Math.Max(1, run.Act);

            var income = MetaRewards.Settle(meta, nodes, bosses, cleared, actReached);
            Save();

            Debug.Log("[MetaStore] 局外结算：节点 " + nodes + " · 击破 " + bosses +
                      " · 最远第 " + actReached + " 幕 · 通关=" + cleared +
                      " ⇒ 墨铊 +" + income.Total + "（局外墨铊共 " + meta.Ink +
                      "，累计 " + meta.RunsPlayed + " 局）");
            return income;
        }

        /// <summary>清档（调试用：删掉文件并重建）。</summary>
        public static void Reset(MetaContent content, ulong seed = 20260922UL)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (System.Exception e) { Debug.LogWarning("[MetaStore] 删档失败：" + e.Message); }
            try { if (File.Exists(HistoryPath)) File.Delete(HistoryPath); }   // 历程跟着本槽位档一起清
            catch (System.Exception e) { Debug.LogWarning("[MetaStore] 删历程失败：" + e.Message); }
            Current = null;
            LoadOrCreate(content, seed);
        }
    }
}
