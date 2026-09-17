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

        /// <summary>幕内当前节点下标（-1 = 还没出发）；换幕时归 -1。</summary>
        public int NodeOffset = -1;
        public int Losses;                      // 本程败场
        public List<string> Team = new List<string>();   // 上阵异兽 id（继承用）
        public string LastSaved = "";           // 最后保存时间（展示用）

        /// <summary>进战斗用的强度系数：随劫数缓涨，给敌人与奖励一个共同标尺。</summary>
        public float Difficulty => 1f + (Realm - 1) * 0.35f + (Jie - 1) * 0.12f;

        public string RealmText
        {
            get { return "第" + Cn(Realm) + "境 · 第" + Cn(Jie) + "劫"; }
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
            if (state == null) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                state.LastSaved = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                File.WriteAllText(PathOf(state.Slot), JsonUtility.ToJson(state, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[RunSave] 写入存档失败 slot=" + state.Slot + "\n" + ex);
            }
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
