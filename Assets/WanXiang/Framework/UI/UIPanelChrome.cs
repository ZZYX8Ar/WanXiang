// ============================================================================
//  万相 · 面板通用"返回"按钮统一（2026-10-19）
//  ---------------------------------------------------------------------------
//  用户定案："把这些按钮打开的面板的返回按钮统一一下位置，有一些有 X 关闭按钮又有
//  返回按钮，统一为返回按钮。"
//
//  规则（运行时套用，不改 prefab —— 用户手改的面板不会被覆盖）：
//    ① 所有名字以 Btn_Close 开头的按钮 → 隐藏（X 不再出现）；
//    ② 有 Btn_Back → 统一到**右上角**（锚 1,1 / 尺寸 168×58 / kit 按钮底 / 文案"返回"）；
//    ③ 只有 X 没有 Btn_Back → 把那个 X **改造成返回按钮**（沿用它的 onClick，本来就是关闭）；
//    ④ 两者都没有 → 不处理（例如战斗 HUD、弹窗）。
//
//  为什么右上角：原来的 X 就在右上角，改成"返回"后位置不变，不会压住左上角的标题条。
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace WanXiang.Framework.UI
{
    public static class UIPanelChrome
    {
        private const float W = 168f, H = 58f, MarginX = 40f, MarginY = 40f;

        /// <summary>对所有子按钮套用统一返回规则（幂等，可每次 OnOpened 调）。</summary>
        public static void UnifyBackButton(Transform root)
        {
            if (root == null) return;

            // ⛔ 弹窗类（确认框/二选一）的 × 是**模态关闭**，不能改成"返回"；
            //   战斗 HUD 也不属于"内容面板"。这些面板一律不动。
            var panel = root.GetComponent<UIPanelBase>();
            string tname = panel != null ? panel.GetType().Name : "";
            if (tname == "DialogPanel" || tname == "BattlePanel" || tname == "ActIntroPanel") return;

            Button back = null, closeX = null;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (b == null) continue;
                var n = b.gameObject.name;
                if (back == null && n.StartsWith("Btn_Back")) back = b;
                else if (closeX == null && n.StartsWith("Btn_Close")) closeX = b;
            }

            // ① 隐藏 X
            if (closeX != null && closeX != back) closeX.gameObject.SetActive(false);

            // ③ 没有返回 → 把 X 改造为返回
            if (back == null) back = closeX;
            if (back == null) return;

            var rt = (RectTransform)back.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = new Vector2(-MarginX, -MarginY);
            rt.SetAsLastSibling();                       // 压在标题条之上，保证可点

            var img = back.targetGraphic as Image ?? back.GetComponent<Image>();
            var sprite = Resources.Load<Sprite>("UI/Kit/btn_secondary");
            if (img != null && sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = true;
            }

            // 文案：把子节点的 TMP 文字改成"返回"。
            // ⛔ 本程序集（WanXiang.Runtime）没有引用 TextMeshPro ⇒ 不能直接用 TMP 类型，
            //    这里按类型名反射设置（text/fontSize/color 都是公共属性）。
            Component label = null;
            foreach (var c in back.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (tn == "TextMeshProUGUI" || tn == "TextMeshPro") { label = c; break; }
            }
            if (label != null)
            {
                var lt = label.GetType();
                lt.GetProperty("text")?.SetValue(label, "返回");
                lt.GetProperty("fontSize")?.SetValue(label, 26f);
                lt.GetProperty("color")?.SetValue(label, new Color(0.16f, 0.13f, 0.09f, 1f));
            }

            back.gameObject.SetActive(true);
        }
    }
}
