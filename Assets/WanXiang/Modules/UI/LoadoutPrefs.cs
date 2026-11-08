// ============================================================================
//  万相 · 技能槽配置（B2-d / 本地偏好，不占局外存档）
//  ---------------------------------------------------------------------------
//  技能槽"装了什么"属于**配置偏好**（删掉只会回到默认技能，不是资产），
//  因此与 PortraitBoardPrefs 同一套路：存 PlayerPrefs，不进 meta.sav。
//
//  · 格式：`beastId:slot:skillId|beastId:slot:skillId|…`（id 不含 : |）
//  · 非法项读取时跳过，不抛异常；键不存在 = 全默认
//  · 写空 skillId = 卸下（等同于删除该条）
//  ⛔ 不校验"能不能装"（那是 SkillSlotRules 的事），本类只负责存取。
// ============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class LoadoutPrefs
    {
        private const string Key = "wx.skill_loadout.v1";

        private static Dictionary<string, string> _map;   // "beastId:slot" → skillId

        private static string MapKey(string beastId, int slot) => beastId + ":" + slot;

        private static void EnsureLoaded()
        {
            if (_map != null) return;
            _map = new Dictionary<string, string>();
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return;
            var items = raw.Split('|');
            for (int i = 0; i < items.Length; i++)
            {
                var seg = items[i];
                if (string.IsNullOrEmpty(seg)) continue;
                var parts = seg.Split(':');
                if (parts.Length != 3) continue;              // 非法段跳过
                if (string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[2])) continue;
                _map[parts[0] + ":" + parts[1]] = parts[2];
            }
        }

        /// <summary>某只兽某槽的技能 id；空串 = 用默认（没配过）。</summary>
        public static string SkillOf(string beastId, int slot)
        {
            if (string.IsNullOrEmpty(beastId)) return "";
            EnsureLoaded();
            string v;
            return _map.TryGetValue(MapKey(beastId, slot), out v) ? v : "";
        }

        /// <summary>设置某槽；skillId 传空 = 卸下。</summary>
        public static void SetSkill(string beastId, int slot, string skillId)
        {
            if (string.IsNullOrEmpty(beastId)) return;
            EnsureLoaded();
            var k = MapKey(beastId, slot);
            if (string.IsNullOrEmpty(skillId)) _map.Remove(k);
            else _map[k] = skillId;
            Flush();
        }

        public static int Count { get { EnsureLoaded(); return _map.Count; } }

        /// <summary>清空全部配置（回到默认技能）。</summary>
        public static void Clear()
        {
            _map = new Dictionary<string, string>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>测试/外部改动后强制重读。</summary>
        public static void Reload() { _map = null; }

        private static void Flush()
        {
            var sb = new StringBuilder(128);
            foreach (var kv in _map)
            {
                if (sb.Length > 0) sb.Append('|');
                // kv.Key 已是 "beastId:slot"
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }
    }
}
