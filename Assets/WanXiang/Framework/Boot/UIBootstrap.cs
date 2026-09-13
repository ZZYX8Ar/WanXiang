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
        [Tooltip("勾选则使用 Resources 加载器（原型期）。接入 YooAsset 后取消勾选并指定自定义加载器。")]
        [SerializeField] private bool _useResourcesLoader = true;

        /// <summary>全局 UI 系统入口。业务代码通过它开关界面。</summary>
        public static UISystem UI { get; private set; }

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

            if (!_useResourcesLoader)
            {
                Debug.LogError(
                    "[UI] 当前只内置了 Resources 加载器。" +
                    "请实现 IUIPanelLoader（例如基于 YooAsset）后，在这里手动创建 UISystem。");
                return;
            }

            if (_dontDestroyOnLoad)
            {
                // 先让 Bootstrap 自己跨场景，再把 [UIRoot] 挂到它下面。
                // 注意顺序：DontDestroyOnLoad 只对根对象生效，
                // 所以必须"父级先跨场景"，不能对着已经成为子物体的 Root 调用。
                DontDestroyOnLoad(gameObject);
            }

            var loader = new ResourcesPanelLoader();

            var system = new UISystem(loader, transform)
            {
                ReferenceResolution = _referenceResolution,
                MatchWidthOrHeight = _matchWidthOrHeight,
                CacheCapacity = _cacheCapacity,
            };

            UI = system;

            Debug.Log($"[UI] UISystem 已启动。层级数 {System.Enum.GetValues(typeof(UILayer)).Length}，" +
                      $"缓存上限 {_cacheCapacity}。");
        }

        private void OnDestroy()
        {
            if (UI == null) return;
            UI.Dispose();
            UI = null;
        }
    }
}
