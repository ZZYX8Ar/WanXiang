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
        public int Realm = 1;                   // 境 1..3
        public int Jie = 1;                     // 劫 1..3（每境三劫）
        public int Eggs = 12;                   // 灵卵
        public int Ink = 3;                     // 墨锭
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

        /// <summary>
        /// L4 祭坛：五行各一条数值增益，每条 0..5 级，每级 +1.6%（总封顶 +8%，GDD 第 8.3 节）。
        /// 局外**永久**生效（区别于 HealPending 这类一次性挂起值）。
        /// </summary>
        public int[] MetaAltar = new int[5];

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
        /// <summary>拥有的异兽图鉴（灵市购买进这里，不占出战名额；出战 5 只在编阵界面选）。</summary>
        public List<string> Collection = new List<string>();

        public List<string> Team = new List<string>();   // 上阵异兽 id（继承用）
        public string LastSaved = "";           // 最后保存时间（展示用）

        /// <summary>进战斗用的强度系数：随劫数缓涨，给敌人与奖励一个共同标尺。</summary>
        public float Difficulty => 1f + (Realm - 1) * 0.35f + (Jie - 1) * 0.12f;

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
