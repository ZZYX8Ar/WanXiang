// ============================================================================
//  WanXiang · UI 启动引导
//  ---------------------------------------------------------------------------
//  挂在启动场景的一个空 GameObject 上即可（建议命名 [UIBootstrap]）。
//
//  为什么要有这么个组件：UI 系统需要在"游戏真正开始前"就把 Canvas 层级建好。
//  若等到第一个界面要打开时才创建，会出现两个问题：
//    1. 首帧卡顿 —— 建 8 个 Canvas 是有开销的，不该发生在玩家已经看到画面的时刻
//    2. 顺序依赖 —— 谁先打开界面谁负责初始化，这种隐式依赖迟早出事
//  [DefaultExecutionOrder(-1000)] 保证它比业务脚本先执行。
//
//  本文件不依赖 QFramework，单独可用。接入 QFramework 见 Integration 目录。
// ============================================================================

using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.Framework.Boot
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class UIBootstrap : MonoBehaviour
    {
        [Header("分辨率适配")]
        [Tooltip("设计分辨率。PC / Steam 建议 1920×1080。")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("0 = 以宽度为准，1 = 以高度为准，0.5 = 折中。PC 多分辨率差异大，0.5 最稳。")]
        [Range(0f, 1f)]
        [SerializeField] private float _matchWidthOrHeight = 0.5f;

        [Header("缓存")]
        [Tooltip("Cached 策略面板的缓存上限。0 表示不缓存（面板一关就销毁）。")]
        [SerializeField] private int _cacheCapacity = 8;

        [Header("生命周期")]
        [Tooltip("是否跨场景保留。单场景原型可关掉，正式项目的 UI 根节点通常跨场景常驻。")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Header("加载器")]
        [Tooltip("勾选则使用 Resources 加载器（原型期）。\n" +
                 "若已有资源系统注册了面板加载器工厂（ResourceBootstrap 会做），本项自动失效。")]
        [SerializeField] private bool _useResourcesLoader = true;

        /// <summary>全局 UI 系统入口。业务代码通过它开关界面。</summary>
        public static UISystem UI { get; private set; }

        /// <summary>
        /// 面板加载器工厂。由资源系统在启动时注册。
        /// </summary>
        /// <remarks>
        /// ⚠ 为什么用「工厂注册」而不是「运行时替换已建好的 UISystem 的加载器」：
        ///    替换意味着 UISystem 要支持中途换加载器，而"换的那一刻"
        ///    有没有面板已经加载过、引用计数算在哪个加载器头上，
        ///    都是难以穷尽的中间态。工厂注册则让 UISystem 从诞生起
        ///    就只有一个加载器，没有中间态。
        ///
        /// ⚠ 时序靠 DefaultExecutionOrder 保证：
        ///    ResourceBootstrap(-1010) 的 Awake 先跑，注册工厂；
        ///    UIBootstrap(-1000) 的 Awake 后跑，取用工厂。
        ///    两者必须在同一个场景里，且都不能被运行时动态添加。
        ///    （如果顺序不成立，下面会打一条明确的警告，不会静默退化成
        ///      Resources 加载 —— 那种退化在出包时才会暴露。）
        /// </remarks>
        private static System.Func<IUIPanelLoader> _panelLoaderFactory;

        /// <summary>
        /// 注册面板加载器工厂。由资源系统在自身 Awake 里调用。
        /// </summary>
        public static void RegisterPanelLoaderFactory(System.Func<IUIPanelLoader> factory)
        {
            _panelLoaderFactory = factory;
        }

        /// <summary>
        /// 复位静态状态。
        /// </summary>
        /// <remarks>
        /// ⚠ 关掉「域重载」的编辑器（Enter Play Mode Options）下，
        ///   静态字段会跨 Play 会话残留。残留的工厂闭包会抓着**上一次**
        ///   的资源服务实例不放，于是第二次进 Play 时 UI 拿着一个已 Dispose
        ///   的服务去加载 —— 症状是"第一次能跑，第二次面板全打不开"。
        ///   SubsystemRegistration 这个时机早于场景加载，是复位静态字段的标准位置。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _panelLoaderFactory = null;
        }

        private void Awake()
        {
            if (UI != null)
            {
                Debug.LogWarning(
                    "[UI] 检测到第二个 UIBootstrap，已销毁重复实例。" +
                    "请确认场景里只放了一个。");
                Destroy(gameObject);
                return;
            }

            // ---- 决定用哪个加载器 ----
            IUIPanelLoader loader = null;
            if (_panelLoaderFactory != null)
            {
                loader = _panelLoaderFactory();
                if (loader == null)
                {
                    Debug.LogError("[UI] 资源系统注册的加载器工厂返回了 null。");
                    return;
                }
            }
            else if (_useResourcesLoader)
            {
                loader = new ResourcesPanelLoader();
            }

            if (loader == null)
            {
                Debug.LogError(
                    "[UI] 没有可用的面板加载器。\n" +
                    "两种可能：\n" +
                    "  ① 用了资源系统但 ResourceBootstrap 不在同一个场景里 —— 它必须和 UIBootstrap 同场景，\n" +
                    "     因为它靠执行顺序（-1010 早于 -1000）在 UIBootstrap 之前注册加载器工厂；\n" +
                    "  ② 取消了 _useResourcesLoader 又没有任何资源系统接入。\n" +
                    "UI 系统未启动。");
                return;
            }

            if (_dontDestroyOnLoad)
            {
                // 先让 Bootstrap 自己跨场景，再把 [UIRoot] 挂到它下面。
                // 注意顺序：DontDestroyOnLoad 只对根对象生效，
                // 所以必须"父级先跨场景"，不能对着已经成为子物体的 Root 调用。
                DontDestroyOnLoad(gameObject);
            }

            var system = new UISystem(loader, transform)
            {
                ReferenceResolution = _referenceResolution,
                MatchWidthOrHeight = _matchWidthOrHeight,
                CacheCapacity = _cacheCapacity,
            };

            UI = system;

            Debug.Log($"[UI] UISystem 已启动。层级数 {System.Enum.GetValues(typeof(UILayer)).Length}，" +
                      $"缓存上限 {_cacheCapacity}，" +
                      $"面板加载器 {loader.GetType().Name}。");
        }

        private void OnDestroy()
        {
            if (UI == null) return;
            UI.Dispose();
            UI = null;
        }
    }
}
