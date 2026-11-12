// ============================================================================
//  万相 · 结局解锁进度（单机资产，PlayerPrefs；删功能 = 删 Key，零风险）
//  终局判定写入 → 选分队面板据此解锁"达成特定结局"的分队与难度上限。
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class EndingUnlocks
    {
        private const string Key = "wx.endings.unlocked";

        public static bool Has(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var raw = PlayerPrefs.GetString(Key, "");
            foreach (var s in raw.Split(',')) if (s == id) return true;
            return false;
        }

        public static void Mark(string id)
        {
            if (string.IsNullOrEmpty(id) || Has(id)) return;
            var raw = PlayerPrefs.GetString(Key, "");
            PlayerPrefs.SetString(Key, string.IsNullOrEmpty(raw) ? id : raw + "," + id);
            PlayerPrefs.Save();
            Debug.Log("[EndingUnlocks] 结局解锁：" + id);
        }

        /// <summary>测试档用：全部结局解锁。</summary>
        public static void GrantAll(IEnumerable<string> ids)
        {
            foreach (var id in ids) Mark(id);
        }
    }
}
