// ============================================================================
//  万相 · 捕获图鉴（P3-b / 本地记录，不占局外存档）
//  ---------------------------------------------------------------------------
//  记录"抓到过哪些异兽"。与 LoadoutPrefs / DaoMarkPrefs 同一套本地键方案：
//    · 键 wx.beast_codex.v1，格式 `id,id,…`
//    · MarkCaught 返回 **是否首次捕获**（图鉴点亮 / 用于奖励判定）
//    · 非法项跳过；键不存在 = 空图鉴
//
//  ⚠ 与"拥有几只"不同：这里只记"见过并抓到过"，具体数量/个体差异留给后续。
//  ⛔ 不校验捕获概率（那是 CaptureRules 的事）。
// ============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class BeastCodexPrefs
    {
        private const string Key = "wx.beast_codex.v1";

        private static HashSet<string> _caught;

        private static void EnsureLoaded()
        {
            if (_caught != null) return;
            _caught = new HashSet<string>();
            var raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return;
            var items = raw.Split(',');
            for (int i = 0; i < items.Length; i++)
            {
                var id = items[i];
                if (!string.IsNullOrEmpty(id)) _caught.Add(id);
            }
        }

        public static bool Caught(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            return _caught.Contains(id);
        }

        public static int Count { get { EnsureLoaded(); return _caught.Count; } }

        /// <summary>记录一次捕获；返回 true = **首次**捕获（图鉴新点亮）。</summary>
        public static bool MarkCaught(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            if (!_caught.Add(id)) return false;
            Flush();
            return true;
        }

        /// <summary>全部已捕获 id（字典序，稳定）。</summary>
        public static List<string> All()
        {
            EnsureLoaded();
            var list = new List<string>(_caught);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        public static void Clear()
        {
            _caught = new HashSet<string>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        public static void Reload() { _caught = null; }

        private static void Flush()
        {
            var sb = new StringBuilder(128);
            foreach (var id in _caught)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }
    }
}
