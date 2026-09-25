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
        public int Eggs = 12;                   // 灵卵
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
        //  ⚠ 之前是"每次出征都现读 MetaStore" ⇒ 局内换觉醒技、升级都会立刻漏进本局。
        // ====================================================================

        /// <summary>快照对应的 RunSeed。换局（重开/失败重来/新档）时 RunSeed 会变 ⇒ 自动重拍。</summary>
        public int SnapshotSeed = 0;
        public List<string> SnapBeastIds = new List<string>();
        public List<int>    SnapLevels   = new List<int>();
        public List<bool>   SnapEvolved  = new List<bool>();
        public List<string> SnapAwaken   = new List<string>();   // 已装备的觉醒技 id（"" = 未装备）

        /// <summary>
        /// 若本局还没拍过快照（或已经换了一局）⇒ 按当前局外存档拍一份；**整局不再更新**。
        /// 以 RunSeed 为界：任何"新一局"都会换 RunSeed，所以不必在每个重开点手动调用。
        /// </summary>
        public void EnsureMetaSnapshot()
        {
            if (SnapshotSeed == RunSeed && SnapBeastIds.Count > 0) return;

            var m = WanXiang.Meta.MetaStore.Ensure();
            SnapshotSeed = RunSeed;
            SnapBeastIds.Clear(); SnapLevels.Clear(); SnapEvolved.Clear(); SnapAwaken.Clear();
            if (m == null) return;

            int n = System.Math.Min(m.BeastIds.Count, m.BeastLevels.Count);
            for (int i = 0; i < n; i++)
            {
                string id = m.BeastIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                SnapBeastIds.Add(id);
                SnapLevels.Add(i < m.BeastLevels.Count ? m.BeastLevels[i] : 0);
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

        public string SnapAwakenOf(string id)
        {
            int i = SnapBeastIds.IndexOf(id);
            return i >= 0 && i < SnapAwaken.Count ? (SnapAwaken[i] ?? "") : "";
        }

        /// <summary>天象/异闻挂起：下一场战斗我方修正 %（可负，用后清零）。</summary>
        public int PlayerBuffPct = 0;

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
        /// 问号节点的揭晓结果，形如 "7:nest"（节点下标:类型）。
        /// ⚠ 不用 Dictionary —— JsonUtility 不支持，存成 List<string> 最省事。
        /// </summary>
        public System.Collections.Generic.List<string> QuestionRevealed =
            new System.Collections.Generic.List<string>();
        public int Losses;                      // 本程败场
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

        public List<string> Team = new List<string>();   // 上阵异兽 id（继承用）
        public string LastSaved = "";           // 最后保存时间（展示用）

        /// <summary>
        /// 进战斗用的强度系数：随【幕】缓涨，给敌人与奖励一个共同标尺。
        /// ⚠ 原实现用 Realm/Jie（旧"境/劫"体系）—— 那套在幕推进后已不再递增，
        ///   会导致难度永远停在 1.0（数值标尺失效）。现改为以 Act 为唯一主轴。
        /// </summary>
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
            Current = new RunState { Slot = Mathf.Clamp(slot, 1, SlotCount) };
            Save(Current);
            Debug.Log("[RunSave] 新的旅程 → 槽位 " + Current.Slot);
            return Current;
        }

        /// <summary>继承一段已有旅程。</summary>
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
