// ============================================================================
//  PortraitBoardPrefs —— 看板轮换偏好（展示偏好，存 PlayerPrefs 不占局外存档）
//  ---------------------------------------------------------------------------
//  轮换序列（最多 5 个，按点击顺序）+ 自动轮换开关 + 轮换间隔。
//  HomePanel（进看板时取序列）/ PortraitBoardPanel（管理面板读写）/
//  HomePortraitStage（读开关与间隔）共用这一处，别各自再读散键。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class PortraitBoardPrefs
    {
        public const string KeyIds = "PortraitRotationIds";       // 逗号连接的轮换序列
        public const string KeyAutoRotate = "PortraitAutoRotate"; // 1=开（默认）
        public const string KeySeconds = "PortraitRotateSeconds"; // 默认 180 ≈ 一首歌的时间

        public const int MaxSelected = 5;
        public static readonly int[] IntervalSteps = { 60, 120, 180, 240 };

        /// <summary>读取轮换序列（保持玩家点击顺序）。</summary>
        public static List<string> LoadSelected()
        {
            var result = new List<string>(MaxSelected);
            var raw = PlayerPrefs.GetString(KeyIds, "");
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var id in raw.Split(','))
                if (!string.IsNullOrEmpty(id) && !result.Contains(id)) result.Add(id);
            return result;
        }

        public static void SaveSelected(IReadOnlyList<string> ids)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ids.Count && i < MaxSelected; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            PlayerPrefs.SetString(KeyIds, sb.ToString());
            PlayerPrefs.Save();
        }

        public static bool AutoRotateOn => PlayerPrefs.GetInt(KeyAutoRotate, 1) == 1;

        public static void SetAutoRotate(bool on)
        {
            PlayerPrefs.SetInt(KeyAutoRotate, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static float LoadSeconds()
        {
            var v = PlayerPrefs.GetFloat(KeySeconds, 180f);
            return v < 30f ? 180f : v;
        }

        public static void SetSeconds(float seconds)
        {
            PlayerPrefs.SetFloat(KeySeconds, seconds);
            PlayerPrefs.Save();
        }
    }
}
