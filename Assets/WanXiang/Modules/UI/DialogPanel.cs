// ============================================================================
//  万相 · 通用弹窗（DialogPanel）
//  ---------------------------------------------------------------------------
//  三种形态，全项目共用：
//    · Confirm  确认：标题 + 正文 + 「确定 / 取消」→ 返回 bool
//    · Tip      提示：标题 + 正文 + 「知道了」→ 无返回值
//    · Choose   二选一：标题 + 正文 + 两个自定义选项 → 返回选项下标（孵穴等）
//
//  用法（await 拿结果，这是它存在的意义）：
//      bool ok = await Dialog.Confirm("重开一局", "当前进度会清空，确定？");
//      if (ok) { ... }
//      int pick = await Dialog.Choose("孵穴", "二选一", "回复全队 40% 生命", "取 2 枚灵卵");
//
//  ⚠ UI 是**代码自建**的（不依赖 prefab / 美术稿）：
//    弹窗是所有功能的公共依赖，先保证"任何地方都能立刻弹一个能用的框"，
//    等美术给了对话框稿子，再把这里的 BuildUi() 换成加载 prefab 即可 —— 调用方一行不用改。
//
//  ⚠ 弹窗放在 Overlay 层（会遮住下面的界面并自带暗遮罩），
//    与 Normal 层的主界面面板天然隔离，不会被 `CloseAll` 之外的逻辑误关。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.Boot;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    public enum DialogKind { Tip = 0, Confirm = 1, Choose = 2 }

    [UIPanel("Panel_Dialog", Layer = UILayer.Overlay, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class DialogPanel : UIPanelBase
    {
        private TMP_Text _title;
        private TMP_Text _body;
        private Button _left;       // 取消 / 选项一
        private Button _right;      // 确定 / 选项二
        private TMP_Text _leftLabel;
        private TMP_Text _rightLabel;

        private UniTaskCompletionSource<bool> _confirmTcs;
        private UniTaskCompletionSource<int> _chooseTcs;
        private bool _built;

        // ================================================================
        //  静态入口（业务只用这三个）
        // ================================================================

        /// <summary>提示：只有一个「知道了」。</summary>
        public static async UniTask Tip(string title, string body)
        {
            var dlg = await Open(title, body, DialogKind.Tip, null, null);
            await dlg.WaitTip();
        }

        /// <summary>确认：返回是否点了「确定」。</summary>
        public static async UniTask<bool> Confirm(string title, string body,
                                                  string okText = "确定", string cancelText = "取消")
        {
            var dlg = await Open(title, body, DialogKind.Confirm, okText, cancelText);
            return await dlg.WaitConfirm();
        }

        /// <summary>二选一：返回 0（左）/ 1（右）。</summary>
        public static async UniTask<int> Choose(string title, string body,
                                                string leftText, string rightText)
        {
            var dlg = await Open(title, body, DialogKind.Choose, leftText, rightText);
            return await dlg.WaitChoose();
        }

        private static async UniTask<DialogPanel> Open(string title, string body, DialogKind kind,
                                                       string leftText, string rightText)
        {
            var ui = UIBootstrap.UI;
            if (ui == null)
            {
                Debug.LogError("[Dialog] UI 系统未启动，弹窗被丢弃：" + title + " / " + body);
                return null;
            }

            var dlg = await ui.OpenAsync<DialogPanel>();
            if (dlg == null) return null;

            dlg.Setup(title, body, kind, leftText, rightText);
            return dlg;
        }

        // ================================================================
        //  生命周期
        // ================================================================

        protected override void OnCreate()
        {
            BuildUi();
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            BuildUi();               // 从缓存里复用时也要保证结构在
            return UniTask.CompletedTask;
        }

        protected override void OnClose()
        {
            // 面板被外部关掉（例如整层清空）时不能让 await 永远挂着
            _confirmTcs?.TrySetResult(false);
            _chooseTcs?.TrySetResult(-1);
            _confirmTcs = null;
            _chooseTcs = null;
        }

        // ================================================================
        //  对外行为
        // ================================================================

        private void Setup(string title, string body, DialogKind kind, string leftText, string rightText)
        {
            BuildUi();

            if (_title != null) _title.text = title ?? "";
            if (_body != null) _body.text = body ?? "";

            switch (kind)
            {
                case DialogKind.Tip:
                    _right.gameObject.SetActive(true);
                    _left.gameObject.SetActive(false);
                    if (_rightLabel != null) _rightLabel.text = string.IsNullOrEmpty(leftText) ? "知道了" : leftText;
                    _right.onClick.RemoveAllListeners();
                    _right.onClick.AddListener(() => { Finish(); });
                    break;

                case DialogKind.Confirm:
                    _left.gameObject.SetActive(true);
                    _right.gameObject.SetActive(true);
                    if (_leftLabel != null) _leftLabel.text = string.IsNullOrEmpty(rightText) ? "取消" : rightText;
                    if (_rightLabel != null) _rightLabel.text = string.IsNullOrEmpty(leftText) ? "确定" : leftText;
                    _left.onClick.RemoveAllListeners();
                    _left.onClick.AddListener(() => { _confirmTcs?.TrySetResult(false); Finish(); });
                    _right.onClick.RemoveAllListeners();
                    _right.onClick.AddListener(() => { _confirmTcs?.TrySetResult(true); Finish(); });
                    break;

                case DialogKind.Choose:
                    _left.gameObject.SetActive(true);
                    _right.gameObject.SetActive(true);
                    if (_leftLabel != null) _leftLabel.text = leftText ?? "选项一";
                    if (_rightLabel != null) _rightLabel.text = rightText ?? "选项二";
                    _left.onClick.RemoveAllListeners();
                    _left.onClick.AddListener(() => { _chooseTcs?.TrySetResult(0); Finish(); });
                    _right.onClick.RemoveAllListeners();
                    _right.onClick.AddListener(() => { _chooseTcs?.TrySetResult(1); Finish(); });
                    break;
            }
        }

        private async UniTask WaitTip()
        {
            _confirmTcs = new UniTaskCompletionSource<bool>();
            await _confirmTcs.Task;
        }

        private async UniTask<bool> WaitConfirm()
        {
            _confirmTcs = new UniTaskCompletionSource<bool>();
            return await _confirmTcs.Task;
        }

        private async UniTask<int> WaitChoose()
        {
            _chooseTcs = new UniTaskCompletionSource<int>();
            return await _chooseTcs.Task;
        }

        private void Finish()
        {
            _confirmTcs?.TrySetResult(false);
            _chooseTcs?.TrySetResult(-1);
            CloseSelf();
        }

        // ================================================================
        //  自建 UI（美术稿到位后整体替换这里即可，调用方不受影响）
        // ================================================================

        private void BuildUi()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            // 全屏暗遮罩（挡住下层点击）
            var dim = NewRect("Dim", rt, new Vector2(0f, 0f), new Vector2(1f, 1f),
                              new Vector2(0f, 0f), new Vector2(0f, 0f));
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0.12f, 0.10f, 0.08f, 0.62f);
            dimImg.raycastTarget = true;

            // 卡片
            var card = NewRect("Card", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               new Vector2(0f, 0f), new Vector2(720f, 420f));
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = new Color(0.98f, 0.97f, 0.94f, 0.98f);

            // 标题
            _title = NewText("Tmp_Title", card, new Vector2(0f, 1f), new Vector2(1f, 1f),
                             new Vector2(0f, -40f), new Vector2(-48f, 64f), 36, TextAlignmentOptions.Center);
            // 正文
            _body = NewText("Tmp_Body", card, new Vector2(0f, 1f), new Vector2(1f, 1f),
                            new Vector2(0f, -120f), new Vector2(-64f, 160f), 26, TextAlignmentOptions.TopLeft);

            // 两个按钮（左侧是"取消/选项一"，右侧是"确定/选项二"）
            _left = NewButton("Btn_Left", card, new Vector2(0f, 0f), new Vector2(0f, 0f),
                              new Vector2(64f, 40f), new Vector2(280f, 92f),
                              new Color(0.94f, 0.92f, 0.88f, 1f), out _leftLabel);
            _right = NewButton("Btn_Right", card, new Vector2(1f, 0f), new Vector2(1f, 0f),
                               new Vector2(-64f, 40f), new Vector2(280f, 92f),
                               new Color(0.79f, 0.63f, 0.39f, 1f), out _rightLabel);
        }

        private static RectTransform NewRect(string name, RectTransform parent,
                                             Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = new Vector2((aMin.x + aMax.x) * 0.5f, (aMin.y + aMax.y) * 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static TMP_Text NewText(string name, RectTransform parent,
                                        Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size,
                                        float fontSize, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent, aMin, aMax, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            return t;
        }

        private static Button NewButton(string name, RectTransform parent,
                                        Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size,
                                        Color bg, out TMP_Text label)
        {
            var rt = NewRect(name, parent, aMin, aMax, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var lrt = NewRect("Tmp_Label", rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            label = lrt.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 28;
            label.color = new Color(0.16f, 0.13f, 0.09f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return btn;
        }
    }

    /// <summary>
    /// 业务侧的短写法：Dialog.Confirm(...) 之类。
    /// 单独放一个静态门面，是为了让业务代码不必写命名空间路径，也方便以后加"排队弹窗"策略。
    /// </summary>
    public static class Dialog
    {
        public static UniTask Tip(string title, string body) => DialogPanel.Tip(title, body);

        public static UniTask<bool> Confirm(string title, string body,
                                            string okText = "确定", string cancelText = "取消")
            => DialogPanel.Confirm(title, body, okText, cancelText);

        public static UniTask<int> Choose(string title, string body, string leftText, string rightText)
            => DialogPanel.Choose(title, body, leftText, rightText);
    }
}
