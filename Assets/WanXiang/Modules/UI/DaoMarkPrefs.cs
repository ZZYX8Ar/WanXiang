// ============================================================================
//  万相 · 道痕仓库（B2-f / 本地资产，不占局外存档）
//  ---------------------------------------------------------------------------
//  道痕是"这局做过什么"的凭证。为**不动 MetaState 的 v7 紧凑二进制存档**
//  （严格长度校验，历史上出过"少算 2 字节写不进档"的事故），
//  这里沿用 LoadoutPrefs / PortraitBoardPrefs 的本地键方案：
//
//    · 键 wx.dao_marks.v1，格式 `id:star,id:star,…`（≤ 表内条数）
//    · **重复获得取最高星**（只升不降，与设计 v2 §7.2 一致）
//    · 非法段读取时跳过；键不存在 = 空仓库
//    · 与 DaoMarkCode 直接对接（导出分享码只差一行 Encode）
//
//  ⚠ 单机资产走 PlayerPrefs 的取舍：清缓存会丢；换来的是"删功能=删 Key"的零风险。
//    若以后要进云存档/多端，再迁到 MetaState v8 段（迁移点只有本文件）。
// ============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class DaoMarkPrefs
    {
        private const string Key = "wx.dao_marks.v1";

        private static Dictionary<string, int> _map;

        private static void EnsureLoaded()
        {
            if (_map != null) return;
            _map = new Dictionary<string, int>();
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return;
            var items = raw.Split(',');
            for (int i = 0; i < items.Length; i++)
            {
                var seg = items[i];
                if (string.IsNullOrEmpty(seg)) continue;
                int colon = seg.LastIndexOf(':');
                if (colon <= 0) continue;
                int star;
                if (!int.TryParse(seg.Substring(colon + 1), out star)) continue;
                if (star < 1 || star > 3) continue;
                string id = seg.Substring(0, colon);
                if (string.IsNullOrEmpty(id)) continue;
                int old;
                _map[id] = _map.TryGetValue(id, out old) && old > star ? old : star;
            }
        }

        /// <summary>仓库全部（id + 星级）；顺序按字典序，稳定可测。</summary>
        public static List<KeyValuePair<string, int>> All()
        {
            EnsureLoaded();
            var list = new List<KeyValuePair<string, int>>(_map.Count);
            foreach (var kv in _map) list.Add(kv);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        public static int StarOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            EnsureLoaded();
            int s;
            return _map.TryGetValue(id, out s) ? s : 0;
        }

        public static int Count { get { EnsureLoaded(); return _map.Count; } }

        /// <summary>获得一条道痕；**只升不降**（★2 不会覆盖已得的 ★3）。</summary>
        public static bool Grant(string id, int star)
        {
            if (string.IsNullOrEmpty(id) || star < 1 || star > 3) return false;
            EnsureLoaded();
            int old;
            if (_map.TryGetValue(id, out old) && old >= star) return false;   // 不变
            _map[id] = star;
            Flush();
            return true;
        }

        /// <summary>局末批量发放（来自 DaoMarkRules.Evaluate）。返回实际提升的条数。</summary>
        public static int GrantAll(IReadOnlyList<KeyValuePair<string, int>> marks)
        {
            if (marks == null) return 0;
            int n = 0;
            for (int i = 0; i < marks.Count; i++)
                if (Grant(marks[i].Key, marks[i].Value)) n++;
            return n;
        }

        /// <summary>导出分享码（空仓库返回空串）。</summary>
        public static string ExportCode()
            => WanXiang.Battle.Core.DaoMarkCode.Encode(All());

        /// <summary>导入分享码；成功返回导入条数，失败 -1。重复项取最高星。</summary>
        public static int ImportCode(string code)
        {
            List<KeyValuePair<string, int>> parsed;
            if (!WanXiang.Battle.Core.DaoMarkCode.TryDecode(code, out parsed)) return -1;
            return GrantAll(parsed);
        }

        // ---- 装备 2 槽（键：wx.dao_marks.equipped，格式 id,id）----
        private const string EquipKey = "wx.dao_marks.equipped";
        private static string[] _equipped;

        private static void EnsureEquipped()
        {
            if (_equipped != null) return;
            _equipped = new[] { "", "" };
            var raw = PlayerPrefs.GetString(EquipKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            var parts = raw.Split(',');
            for (int i = 0; i < parts.Length && i < 2; i++) _equipped[i] = parts[i] ?? "";
        }

        /// <summary>装备位（最多 2）：返回 [(id, 当前星级)]，空位/仓库没有的自动剔除。</summary>
        public static List<KeyValuePair<string, int>> EquippedMarks()
        {
            EnsureEquipped();
            var list = new List<KeyValuePair<string, int>>(2);
            for (int i = 0; i < _equipped.Length; i++)
            {
                string id = _equipped[i];
                if (string.IsNullOrEmpty(id)) continue;
                int star = StarOf(id);
                if (star <= 0) continue;                       // 仓库里没有 ⇒ 不算
                list.Add(new KeyValuePair<string, int>(id, star));
            }
            return list;
        }

        public static string EquippedAt(int slot)
        {
            EnsureEquipped();
            return slot >= 0 && slot < _equipped.Length ? _equipped[slot] : "";
        }

        /// <summary>设置装备位；id 为空 = 卸下。必须在仓库里已有才算数（调用方负责校验）。</summary>
        public static void SetEquipped(int slot, string id)
        {
            EnsureEquipped();
            if (slot < 0 || slot >= _equipped.Length) return;
            _equipped[slot] = id ?? "";
            PlayerPrefs.SetString(EquipKey, _equipped[0] + "," + _equipped[1]);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            _equipped = null;
            PlayerPrefs.DeleteKey(EquipKey);
            _map = new Dictionary<string, int>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        public static void Reload() { _map = null; _equipped = null; }

        private static void Flush()
        {
            var sb = new StringBuilder(96);
            foreach (var kv in _map)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }
    }
}
