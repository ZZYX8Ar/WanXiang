// ============================================================================
//  秘境 · 程序集桥接注册（RealmHookRegistrar）—— 2026-10-16
//  ---------------------------------------------------------------------------
//  为什么单独一个文件：程序集分层的**唯一合法接线点**。
//    · WanXiang.Battle.Core （战斗核心，noEngineReferences）—— 不能引用 Campaign
//    · WanXiang.Campaign.Core（秘境，无引擎引用）—— 只能引用 Battle.Core
//    · WanXiang.Modules.UI（界面）—— **能同时看到上面两个**
//  所以"秘境要把场地效果挂到战斗回合始"这件事，只能在这里接线。
//
//  ⚠ 用 [RuntimeInitializeOnLoadMethod] 而不是等某个面板打开才注册：
//    秘境可能由非战斗场景触发，注册晚了会**第一回合没有场地效果**（很难查）。
// ============================================================================

using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static class RealmHookRegistrar
    {
        private static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            EnsureRegistered();
        }

        /// <summary>幂等注册（也供自检与测试显式调用）。</summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            RealmSession.RegisterToCore();
        }
    }
}