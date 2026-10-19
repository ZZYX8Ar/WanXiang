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
        private const float Size = 72f, Margin = 22f;

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
            bool isSave = panel.GetType().Name == "SavePanel";

            // ① 隐藏旧的 X / 返回（Settings 保留 Btn_Back = 返回主界面）
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (b == null) continue;
                var n = b.gameObject.name;
                if (n.StartsWith("Btn_Close")) { b.gameObject.SetActive(false); continue; }
                if (!isSettings && n.StartsWith("Btn_Back")) b.gameObject.SetActive(false);
            }

            // ①' 存档面板特例：右上角要的是**返回**（回开始界面），它天生有 Btn_Back（返回标题）
            //    ⇒ 只把它挪到右上角、统一外观，不再生成 X。
            if (isSave)
            {
                foreach (var b in root.GetComponentsInChildren<Button>(true))
                {
                    if (b == null || !b.gameObject.name.StartsWith("Btn_Back")) continue;
                    var brt = (RectTransform)b.transform;
                    brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
                    brt.pivot = new Vector2(1f, 1f);
                    brt.sizeDelta = new Vector2(168f, 58f);
                    brt.anchoredPosition = new Vector2(-Margin, -Margin);
                    brt.SetAsLastSibling();
                    var bimg = b.targetGraphic as Image ?? b.GetComponent<Image>();
                    var bsp = Resources.Load<Sprite>("UI/Kit/btn_secondary");
                    if (bimg != null && bsp != null) { bimg.sprite = bsp; bimg.type = Image.Type.Sliced; bimg.color = Color.white; }
                    b.gameObject.SetActive(true);
                    break;
                }
                return;
            }

            // ② 右上角 X：存在则复用，不存在则新建（onClick = CloseSelf）
            Transform x = root.Find("Btn_CloseX_Unified");
            if (x == null)
            {
                var go = new GameObject("Btn_CloseX_Unified", typeof(RectTransform), typeof(Image), typeof(Button));
                x = go.transform;
                x.SetParent(root, false);
                var btn = go.GetComponent<Button>();
                // ★ 走 RequestChromeClose：子类可拦（遗物面板要"暂离不通过节点"）
                btn.onClick.AddListener(() => { if (panel != null) panel.RequestChromeClose(); });
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
            x.SetAsLastSibling();

            // ★ X 要贴着"面板本体"（最大的一块卡片），不能钉在整屏根节点上 ——
            //   看板这类面板本体是内缩的卡片，钉根节点会飘到屏幕角落（用户实测"离太远"）。
            RectTransform cardRt = null;
            float bestArea = 0f;
            var rootRt = root as RectTransform;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.transform == root) continue;
                if (img.transform == x) continue;
                var irt = (RectTransform)img.transform;
                if (irt.rect.width < 200f || irt.rect.height < 120f) continue;
                float area = irt.rect.width * irt.rect.height;
                if (area > bestArea) { bestArea = area; cardRt = irt; }
            }
            if (cardRt != null && rootRt != null && bestArea < rootRt.rect.width * rootRt.rect.height * 0.92f)
            {
                var c4 = new Vector3[4];
                cardRt.GetWorldCorners(c4);   // 2 = 右上
                rt.position = c4[2] + new Vector3(-Margin, -Margin, 0f) * root.lossyScale.x;
            }
            else rt.anchoredPosition = new Vector2(-Margin, -Margin);

            x.gameObject.SetActive(true);
        }
    }
}
