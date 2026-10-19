// ============================================================================
//  万相 · 面板通用"关闭 X"统一（2026-10-19 第二版）
//  ---------------------------------------------------------------------------
//  用户两轮口径合并后的最终规则：
//    · 内容面板右上角统一放一个 **X**（点击 = 关闭本面板，不代表"通过节点"）；
//    · 各面板原来的"返回"按钮隐藏（避免 X 和返回并存）；
//    · ⚠ SettingsPanel 例外：底部中央的「返回主界面」必须保留（那是回标题画面，
//      和"关闭设置"不是一回事），所以 SettingsPanel **不动 Btn_Back**。
//
//  为什么新建 X 而不是改造旧按钮：旧按钮的 onClick 可能被接到"回标题/回地图"等
//  特殊流程上（用户实测：设置面板的返回直接跳到了开始界面）。新建按钮绑
//  `UIPanelBase.CloseSelf` 才能保证语义 = 只是关面板。
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Framework.UI
{
    public static class UIPanelChrome
    {
        private const float Size = 56f, Margin = 34f;

        /// <summary>按类型名限定作用范围（用户点名的内容面板）。</summary>
        private static readonly System.Collections.Generic.HashSet<string> Targets =
            new System.Collections.Generic.HashSet<string>
            {
                "CodexPanel", "MetaPanel", "ArchivePanel", "HistoryPanel", "SettingsPanel",
                "PortraitBoardPanel", "RelicPanel", "RecruitPanel", "MarketPanel",
                "ForgePanel", "OmenPanel", "SavePanel",
            };

        public static void UnifyBackButton(Transform root)
        {
            if (root == null) return;
            var panel = root.GetComponent<UIPanelBase>();
            if (panel == null) return;
            if (!Targets.Contains(panel.GetType().Name)) return;

            bool isSettings = panel.GetType().Name == "SettingsPanel";

            // ① 隐藏旧的 X / 返回（Settings 保留 Btn_Back = 返回主界面）
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (b == null) continue;
                var n = b.gameObject.name;
                if (n.StartsWith("Btn_Close")) { b.gameObject.SetActive(false); continue; }
                if (!isSettings && n.StartsWith("Btn_Back")) b.gameObject.SetActive(false);
            }

            // ② 右上角 X：存在则复用，不存在则新建（onClick = CloseSelf）
            Transform x = root.Find("Btn_CloseX_Unified");
            if (x == null)
            {
                var go = new GameObject("Btn_CloseX_Unified", typeof(RectTransform), typeof(Image), typeof(Button));
                x = go.transform;
                x.SetParent(root, false);
                var btn = go.GetComponent<Button>();
                btn.onClick.AddListener(() => { if (panel != null) panel.CloseSelf(); });
                var img = go.GetComponent<Image>();
                var sp = Resources.Load<Sprite>("UI/Kit/btn_close");
                if (sp != null) { img.sprite = sp; img.type = Image.Type.Sliced; }
                else img.color = new Color(0.93f, 0.91f, 0.86f, 1f);
                img.raycastTarget = true;
            }
            var rt = (RectTransform)x;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(Size, Size);
            rt.anchoredPosition = new Vector2(-Margin, -Margin);
            rt.SetAsLastSibling();
            x.gameObject.SetActive(true);
        }
    }
}
