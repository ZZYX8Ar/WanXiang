// ============================================================================
//  万相 · 设置存储（2026-11-14）
//  ---------------------------------------------------------------------------
//  唯一收口：所有设置项在这里读写 PlayerPrefs，消费点只读属性。
//  ⛔ 只允许放"有真实消费点"的设置项（用户 11-14 永久约束：不许假开关）。
//  新增一项 = 加一个 Key + 属性 + 在设置页加一行 + 消费点读一次。
// ============================================================================
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class SettingsStore
    {
        private const string KAutoBattle = "wx_set_autobattle";
        private const string KSkillCutIn = "wx_set_cutin";
        private const string KDamageNumbers = "wx_set_damagenum";
        private const string KScreenShake = "wx_set_shake";
        private const string KTooltips = "wx_set_tooltip";
        private const string KSpeed = "wx_set_battlespeed";     // 存 100/150/200
        private const string KTargetFps = "wx_set_targetfps";   // 30/60/120/0(不限)
        private const string KAutoSave = "wx_set_autosave";

        private static bool B(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) == 1;
        private static void SetB(string key, bool v) { PlayerPrefs.SetInt(key, v ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>每局开始自动战斗（不需要再点「自动」）。默认关。</summary>
        public static bool AutoBattle { get => B(KAutoBattle, false); set => SetB(KAutoBattle, value); }

        /// <summary>技能 cut-in 特写演出。默认开（关掉后只播冲锋与命中特效）。</summary>
        public static bool SkillCutIn { get => B(KSkillCutIn, true); set => SetB(KSkillCutIn, value); }

        /// <summary>伤害/状态飘字。默认开。</summary>
        public static bool DamageNumbers { get => B(KDamageNumbers, true); set => SetB(KDamageNumbers, value); }

        /// <summary>屏幕震动。默认开。</summary>
        public static bool ScreenShake { get => B(KScreenShake, true); set => SetB(KScreenShake, value); }

        /// <summary>战斗内提示气泡（技能/捕获说明）。默认开。</summary>
        public static bool Tooltips { get => B(KTooltips, true); set => SetB(KTooltips, value); }

        /// <summary>战斗速度倍率（1 / 1.5 / 2），默认 1。</summary>
        public static float BattleSpeed
        {
            get
            {
                int v = PlayerPrefs.GetInt(KSpeed, 100);
                return Mathf.Clamp(v / 100f, 1f, 4f);   // 与战斗界面一致：1/2/4
            }
            set
            {
                int v = Mathf.RoundToInt(Mathf.Clamp(value, 1f, 4f) * 100f);
                PlayerPrefs.SetInt(KSpeed, v);
                PlayerPrefs.Save();
            }
        }

        /// <summary>帧率上限（0 = 不限）。</summary>
        public static int TargetFps
        {
            get => PlayerPrefs.GetInt(KTargetFps, 60);
            set
            {
                PlayerPrefs.SetInt(KTargetFps, value);
                PlayerPrefs.Save();
                ApplyTargetFps();
            }
        }

        /// <summary>自动存档（节点通过后落盘）。默认开。</summary>
        public static bool AutoSave { get => B(KAutoSave, true); set => SetB(KAutoSave, value); }

        private static void ApplyTargetFps()
        {
            int fps = TargetFps;
            Application.targetFrameRate = fps <= 0 ? -1 : fps;
        }

        /// <summary>启动/进游戏时应用一次（帧率这类需要立刻生效的）。</summary>
        public static void ApplyAll() => ApplyTargetFps();
    }
}
