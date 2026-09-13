// ============================================================================
//  WanXiang · UI 框架冒烟测试
//  ---------------------------------------------------------------------------
//  把本脚本挂在任意场景的空 GameObject 上，Play 就能看到整套 UI 框架跑起来。
//  不需要任何 Prefab、任何美术资源、任何手工配置。
//
//  它会用代码构建面板（而不是从磁盘加载），这样可以验证：
//    · 分层 Canvas 与排序（8 个独立 Canvas）
//    · 面板生命周期（OnCreate / OnOpen / OnOpened / OnPause / OnResume / OnClose）
//    · 遮罩与射线拦截（全屏面板透明遮罩拦住下层；半屏弹窗深色遮罩点空白关闭）
//    · 暂停传播（打开背包后，主界面的计时器应该停下来）
//    · 返回键分层处理（HandleBack 由内向外逐层关闭）
//    · 缓存策略（背包 Cached 关掉后重开不重建；弹窗 Transient 关掉即销毁）
//
//  为什么值得写这么个东西：
//    UI 框架的问题基本都是"多层级叠加时才暴露"的 —— 遮罩没拦住、
//    关掉弹窗后下层点不动、暂停没恢复。这些在单面板测试里一个都测不出来，
//    等到业务界面铺开才发现，改动成本会翻好几倍。
//
//  面板脚本与文件名不一致在 Unity 里不能被 Inspector 挂载，
//  但这里的面板是代码 AddComponent 上去的，所以放在同一个文件里没有问题。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Samples.UI
{
    // ======================================================================
    //  启动器
    // ======================================================================

    [DisallowMultipleComponent]
    public sealed class UIFrameworkSmokeTest : MonoBehaviour
    {
        private UISystem _ui;

        private void Start()
        {
            var loader = new SmokePanelLoader();
            _ui = new UISystem(loader);

            Debug.Log(
                "[冒烟] UI 框架已启动。\n" +
                "  操作：主界面点「打开背包」→ 背包点「打开确认弹窗」\n" +
                "  注意观察：打开背包后主界面的计时器是否停止；关掉弹窗后是否恢复。");

            _ui.OpenAsync<SmokeHomePanel>().Forget();
        }

        private void OnDestroy()
        {
            _ui?.Dispose();
            _ui = null;
        }
    }

    // ======================================================================
    //  面板 1：主界面（Main 层，常驻）
    // ======================================================================

    [UIPanel("Panel_SmokeHome",
        Layer = UILayer.Main,
        CachePolicy = UICachePolicy.Resident,
        FullScreen = true)]    // Main 层本身无遮罩、不暂停下层（由 UILayerUtil.NeedsMask 决定）
    public sealed class SmokeHomePanel : UIPanelBase
    {
        private Text _tickText;
        private float _tick;

        protected override void OnCreate()
        {
            UIBuilder.PanelBackground(gameObject, new Color(0.10f, 0.13f, 0.18f, 1f));

            UIBuilder.Label(transform, "《万相》UI 框架冒烟测试",
                new Vector2(0f, 260f), new Vector2(1200f, 60f), 36, TextAnchor.MiddleCenter);

            _tickText = UIBuilder.Label(transform, "计时器：0.0 s",
                new Vector2(0f, 190f), new Vector2(800f, 40f), 24, TextAnchor.MiddleCenter);

            UIBuilder.Note(transform,
                "本面板属于 Main 层（常驻，无遮罩）。\n" +
                "打开背包后本面板会收到 OnPause —— 计时器应当停住。",
                new Vector2(0f, 120f));

            UIBuilder.Button(transform, "打开背包", new Vector2(-180f, -40f), new Vector2(280f, 72f),
                () => OpenPanelAsync<SmokeBackpackPanel>().Forget());

            UIBuilder.Button(transform, "模拟返回键 (ESC)", new Vector2(180f, -40f), new Vector2(280f, 72f),
                () =>
                {
                    bool consumed = UI.HandleBack();
                    Debug.Log(consumed
                        ? "[冒烟] 返回键被界面消费（关闭了最上层界面）。"
                        : "[冒烟] 已在最外层，未被消费 —— 此时应弹「再按一次退出游戏」。");
                });

            UIBuilder.Button(transform, "打印栈状态", new Vector2(0f, -140f), new Vector2(280f, 56f),
                PrintStackState);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Debug.Log("[冒烟] Home.OnOpenAsync（每次打开都会调用，事件订阅写在这里）");
            return UniTask.CompletedTask;
        }

        protected override void OnPause()
        {
            Debug.Log("[冒烟] Home.OnPause —— 计时器已停止");
        }

        protected override void OnResume()
        {
            Debug.Log("[冒烟] Home.OnResume —— 计时器恢复");
        }

        private void Update()
        {
            // 只在 Opened 状态跑。这行 if 就是"控件为什么要关心 Panel 状态"的答案，
            // 少了它会出现"背包打开了但主界面计时器还在涨"。
            if (State != UIPanelState.Opened) return;

            _tick += Time.deltaTime;
            if (_tickText != null)
            {
                _tickText.text = $"计时器：{_tick:F1} s";
            }
        }

        private void PrintStackState()
        {
            Debug.Log($"[冒烟] 栈深度 = {UI.StackDepth}，" +
                      $"背包已打开 = {UI.IsOpen<SmokeBackpackPanel>()}，" +
                      $"弹窗已打开 = {UI.IsOpen<SmokeConfirmPanel>()}");
        }
    }

    // ======================================================================
    //  面板 2：背包（Normal 层，全屏，LRU 缓存）
    // ======================================================================

    [UIPanel("Panel_SmokeBackpack",
        Layer = UILayer.Normal,
        CachePolicy = UICachePolicy.Cached,
        FullScreen = true,          // 全屏 → 暂停下层、遮罩完全透明但拦截射线
        CloseOnMaskClick = false)]  // 全屏面板点空白不关闭，避免误触
    public sealed class SmokeBackpackPanel : UIPanelBase
    {
        private static int _openCount;
        private Text _infoText;

        protected override void OnCreate()
        {
            // OnCreate 只在首次创建时调用。用它来数"面板被重建了几次"，
            // 就能验证缓存策略是否生效：Cached 面板关掉再开，这个数不该增加。
            _openCount++;
            Debug.Log($"[冒烟] Backpack.OnCreate（第 {_openCount} 次创建。Cached 策略下应该只创建一次）");

            UIBuilder.PanelBackground(gameObject, new Color(0.14f, 0.16f, 0.22f, 1f));

            UIBuilder.Label(transform, "背包（全屏 · Normal 层）",
                new Vector2(0f, 260f), new Vector2(1200f, 60f), 34, TextAnchor.MiddleCenter);

            _infoText = UIBuilder.Label(transform, string.Empty,
                new Vector2(0f, 180f), new Vector2(1000f, 40f), 22, TextAnchor.MiddleCenter);

            UIBuilder.Note(transform,
                "全屏面板的遮罩是「完全透明但仍然拦截射线」的：\n" +
                "试着点击背后主界面的按钮 —— 应该点不到（这是防误触的关键）。",
                new Vector2(0f, 100f));

            UIBuilder.Button(transform, "打开确认弹窗", new Vector2(-180f, -40f), new Vector2(300f, 72f),
                () => OpenPanelAsync<SmokeConfirmPanel>("确定要融合这两只异兽吗？").Forget());

            UIBuilder.Button(transform, "关闭背包", new Vector2(180f, -40f), new Vector2(300f, 72f),
                () => CloseSelf<SmokeBackpackPanel>());
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Debug.Log("[冒烟] Backpack.OnOpenAsync —— 这里是订阅数据变化事件的地方");
            return UniTask.CompletedTask;
        }

        protected override void OnOpened()
        {
            if (_infoText != null)
            {
                _infoText.text = $"本面板累计创建 {_openCount} 次。关闭再打开，这个数字不应变化。";
            }
        }

        protected override void OnPause() => Debug.Log("[冒烟] Backpack.OnPause（被弹窗遮挡）");
        protected override void OnResume() => Debug.Log("[冒烟] Backpack.OnResume（弹窗已关闭）");
        protected override void OnClose() => Debug.Log("[冒烟] Backpack.OnClose —— 框架随后自动解绑所有事件订阅");

        protected override void OnDestroyed() => Debug.Log("[冒烟] Backpack.OnDestroyed（被 LRU 淘汰）");
    }

    // ======================================================================
    //  面板 3：确认弹窗（Popup 层，单次，点遮罩关闭）
    // ======================================================================

    [UIPanel("Panel_SmokeConfirm",
        Layer = UILayer.Popup,
        CachePolicy = UICachePolicy.Transient,  // 弹窗用完即弃，不值得缓存
        FullScreen = false,                     // 非全屏 → 遮罩 62% 黑，点空白处关闭
        CloseOnMaskClick = true)]
    public sealed class SmokeConfirmPanel : UIPanelBase
    {
        private Text _messageText;

        protected override void OnCreate()
        {
            Debug.Log("[冒烟] Confirm.OnCreate（Transient 策略：每次打开都会重建，这是预期行为）");

            UIBuilder.PanelBackground(gameObject, new Color(0.20f, 0.22f, 0.28f, 1f));

            // 弹窗不是全屏，但框架不会强制拉伸它 —— 尺寸由 Prefab（这里是代码）自己决定。
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(720f, 380f);
            rt.anchoredPosition = Vector2.zero;

            UIBuilder.Label(transform, "确认", new Vector2(0f, 120f), new Vector2(600f, 50f), 30, TextAnchor.MiddleCenter);

            _messageText = UIBuilder.Label(transform, string.Empty,
                new Vector2(0f, 40f), new Vector2(620f, 80f), 24, TextAnchor.MiddleCenter);

            UIBuilder.Button(transform, "取消", new Vector2(-140f, -110f), new Vector2(220f, 64f),
                () => CloseSelf<SmokeConfirmPanel>());

            UIBuilder.Button(transform, "确定", new Vector2(140f, -110f), new Vector2(220f, 64f),
                () =>
                {
                    Debug.Log("[冒烟] 点了确定 —— 这里应该触发业务 Command");
                    CloseSelf<SmokeConfirmPanel>();
                });
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            // payload 类型检查是必须的：调用方传错参数时应该立刻报错，
            // 而不是等到取字段时抛 NullReference 让人去猜哪一层传错了。
            var message = payload as string;
            if (message == null && payload != null)
            {
                Debug.LogError($"[冒烟] Confirm 期望 string 类型的 payload，收到 {payload.GetType().Name}");
            }

            if (_messageText != null)
            {
                _messageText.text = message ?? "(未传入消息)";
            }

            return UniTask.CompletedTask;
        }

        protected override void OnClose() => Debug.Log("[冒烟] Confirm.OnClose（点遮罩或按钮都会走到这里）");
        protected override void OnDestroyed() => Debug.Log("[冒烟] Confirm.OnDestroyed（Transient 关闭即销毁）");
    }

    // ======================================================================
    //  代码构建用的加载器（替代 Resources / YooAsset，方便无资源配置验证）
    // ======================================================================

    internal sealed class SmokePanelLoader : IUIPanelLoader
    {
        private readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        public async UniTask<GameObject> LoadPanelPrefabAsync(string key, CancellationToken ct)
        {
            // 模拟一帧真实加载耗时，好让"加载去重/防连点"的逻辑真的被走到。
            // 若这里同步返回，并发点击根本不会叠加，测不出问题。
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            if (_prefabs.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            GameObject prefab = BuildPrefab(key);
            if (prefab == null)
            {
                Debug.LogError($"[冒烟] 不认识的面板 key：{key}");
                return null;
            }

            prefab.SetActive(false);   // 模板自身保持在未激活状态
            _prefabs[key] = prefab;
            return prefab;
        }

        public void ReleasePanelPrefab(string key)
        {
            // 冒烟测试不真正释放，保持简单
        }

        private static GameObject BuildPrefab(string key)
        {
            var go = new GameObject(key, typeof(RectTransform));

            switch (key)
            {
                case "Panel_SmokeHome": go.AddComponent<SmokeHomePanel>(); break;
                case "Panel_SmokeBackpack": go.AddComponent<SmokeBackpackPanel>(); break;
                case "Panel_SmokeConfirm": go.AddComponent<SmokeConfirmPanel>(); break;
                default:
                    UnityEngine.Object.Destroy(go);
                    return null;
            }

            return go;
        }
    }

    // ======================================================================
    //  极简 UI 构建工具（仅示例用，业务项目应该用 Prefab + 美术资源）
    // ======================================================================

    internal static class UIBuilder
    {
        private static Font _font;

        private static Font DefaultFont
        {
            get
            {
                if (_font != null) return _font;

                // Unity 2022.2 起内置 Arial 被 LegacyRuntime 取代。
                // 两代都试一遍，保证 2021 与 2022 都能跑。
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch { /* 2021 及更早没有这个资源 */ }

                if (_font == null)
                {
                    try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                    catch { /* 2022 及以后已移除 */ }
                }

                if (_font == null)
                {
                    Debug.LogWarning("[冒烟] 找不到内置字体，UI 文字将不显示。");
                }

                return _font;
            }
        }

        public static void PanelBackground(GameObject panel, Color color)
        {
            var image = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            image.color = color;
        }

        public static Text Label(
            Transform parent, string content, Vector2 position, Vector2 size,
            int fontSize, TextAnchor anchor)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var text = go.GetComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;   // 文字不该吃射线
            return text;
        }

        public static void Note(Transform parent, string content, Vector2 position)
        {
            var text = Label(parent, content, position, new Vector2(1100f, 120f), 20, TextAnchor.MiddleCenter);
            text.color = new Color(0.72f, 0.78f, 0.86f, 1f);
        }

        public static Button Button(
            Transform parent, string label, Vector2 position, Vector2 size, Action onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.22f, 0.46f, 0.82f, 1f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = Label(go.transform, label, Vector2.zero, size, 24, TextAnchor.MiddleCenter);
            text.color = Color.white;

            return button;
        }
    }
}
