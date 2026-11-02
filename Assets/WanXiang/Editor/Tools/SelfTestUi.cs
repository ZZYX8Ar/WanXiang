// ============================================================================
//  WanXiang · 自检收尾输出（编辑器）
//  ---------------------------------------------------------------------------
//  ⛔ 为什么要有它（2026-11-02 踩过的坑）：
//    `EditorUtility.DisplayDialog` 是**阻塞式**的 —— 自检失败时弹一个模态框，
//    编辑器主线程就停在对话框里，MCP / 诊断桥的后续命令全部超时，
//    表现成"Unity 卡死但进程 Responding=True、CPU 不动"。
//    当时连挂两次，最后靠用户手动点「好」才恢复。
//
//  ⇒ 规矩：自检**默认只写 Console + 报告文件**，不弹窗；
//    需要人看的时候用菜单「万相/诊断/自检失败时弹窗」临时打开。
// ============================================================================

using UnityEditor;
using UnityEngine;

namespace WanXiang.EditorTools
{
    public static class SelfTestUi
    {
        /// <summary>默认静默。自动化（MCP/诊断桥/批处理）跑自检时必须是 true。</summary>
        public static bool SuppressDialogs = true;

        [MenuItem("万相/诊断/自检失败时弹窗（开关）", priority = 220)]
        private static void Toggle()
        {
            SuppressDialogs = !SuppressDialogs;
            Debug.Log("[SelfTestUi] 自检失败弹窗 = " + (SuppressDialogs ? "关（只写 Console）" : "开"));
        }

        /// <summary>自检失败的统一收尾：先 LogError，需要时才弹窗。</summary>
        public static void Failed(string title, string message)
        {
            Debug.LogError("[" + title + "] " + message);
            if (SuppressDialogs || Application.isBatchMode) return;
            EditorUtility.DisplayDialog(title, message, "好");
        }
    }
}
