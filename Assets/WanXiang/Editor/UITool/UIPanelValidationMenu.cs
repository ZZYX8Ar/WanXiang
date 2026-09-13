// ============================================================================
//  WanXiang · UI 框架 · 编辑器校验菜单
//  ---------------------------------------------------------------------------
//  菜单：WanXiang / UI / 校验面板配置
//
//  这个菜单要做的事很小，但价值不小：把面板配置错误从"运行时白屏/看不到界面"
//  提前到"打包前一条红字"。UI 的配置错误几乎都是这种形态 ——
//  不会崩，只是那个界面打不开，如果没人去点，它能一路活到上线。
//
//  建议接进 CI：打包流水线的第一步跑一次校验，有问题直接让构建失败。
// ============================================================================

using UnityEditor;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.EditorTools
{
    public static class UIPanelValidationMenu
    {
        [MenuItem("WanXiang/UI/校验面板配置", priority = 100)]
        public static void ValidatePanelConfig()
        {
            // 清缓存：Unity 重新编译后 Attribute 可能已变（改了层级、换了 key），
            // 不清会拿上一次的结果，校验就成了摆设。
            UIPanelRegistry.Clear();

            var problems = UIPanelRegistry.Validate();

            if (problems == null || problems.Count == 0)
            {
                Debug.Log("[UI] 面板配置校验通过，没有发现问题。");
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError($"[UI] {problems[i]}");
            }

            Debug.LogError($"[UI] 面板配置共发现 {problems.Count} 个问题，详见上方红色日志。");
        }
    }
}
