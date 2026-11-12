// ============================================================================
//  万相 · 进度解锁规则（UI 层唯一收口）
//  难度/分队等"通关解锁"判定都走这里，避免散落在各面板里各写一套。
// ============================================================================
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class ProgressionRules
    {
        /// <summary>难度解锁上限：N1 恒开，每通关一次解锁下一档（上限 DifficultyTable.MaxLevel）。</summary>
        public static int MaxUnlockedChallenge()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            int done = meta != null ? meta.RunsCompleted : 0;
            int cap = WanXiang.Battle.Core.DifficultyTable.MaxLevel > 0
                ? WanXiang.Battle.Core.DifficultyTable.MaxLevel : 18;
            return Mathf.Clamp(1 + done, 1, cap);
        }
    }
}
