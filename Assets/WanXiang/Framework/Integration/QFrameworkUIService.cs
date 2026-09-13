// ============================================================================
//  WanXiang · QFramework 接入层
//  ---------------------------------------------------------------------------
//  UI 框架本身不继承 QFramework 的任何基类（原因见 UIDefines.cs 顶部说明），
//  接入方式是"把 UISystem 当作一个 Utility 注册进 Architecture"。
//
//  启用步骤：
//    1. 导入 QFramework 到工程（Package Manager / UPM / 源码复制均可）
//    2. Project Settings → Player → Other Settings → Scripting Define Symbols
//       添加 WANXIANG_QFRAMEWORK
//    3. 在你的 Architecture 里注册（见文件末尾注释）
//
//  为什么要用 #if 包起来：
//    没有导入 QFramework 时，这个文件不参与编译，"框架能不能单独跑起来"这件事
//    就始终是真的。这对排查问题很重要 —— 出 bug 时可以先摘掉 QFramework
//    看是不是集成层的问题，而不用把整条链路翻一遍。
//
//  ⚠ 你的 QFramework 版本若与此处 API 不符（QFramework v1.x 各小版本
//    Architecture 注册方法名有细微差别），只需改下面那一行的调用形式。
// ============================================================================

#if WANXIANG_QFRAMEWORK

using System;
using QFramework;
using UnityEngine;
using WanXiang.Framework.UI;

namespace WanXiang.Framework.Integration
{
    /// <summary>
    /// UI 系统在 QFramework 里的宿主（Utility 角色）。
    ///
    /// 为什么是 Utility 而不是 System：
    ///   QFramework 的 System 语义是"无状态的业务逻辑层"，Model 是"数据层"，
    ///   Utility 是"与框架无关的工具能力"。UI 系统持有 GameObject 与 Canvas，
    ///   属于典型的工具能力，且业务需要"想用就拿"，不该受 System 的生命周期约束。
    /// </summary>
    public sealed class UIService : IUtility
    {
        private readonly IUIPanelLoader _loader;
        private readonly IUILoadingIndicator _indicator;

        private UISystem _system;

        /// <summary>分辨率适配参数。首次访问 <see cref="UI"/> 前设置才生效。</summary>
        public Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        public float MatchWidthOrHeight = 0.5f;
        public int CacheCapacity = 8;

        /// <summary>
        /// 是否跨场景保留 UI 根节点。
        /// 建议 true：绝大多数游戏的 UI 根节点都是常驻的，
        /// 每次切场景重建 8 个 Canvas 既浪费又会造成界面闪一下。
        /// </summary>
        public bool DontDestroyOnLoad = true;

        public UIService(IUIPanelLoader loader, IUILoadingIndicator indicator = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _indicator = indicator;
        }

        /// <summary>
        /// 懒加载获取 UI 系统。
        /// 刻意不用在构造时创建：UIService 可能在 Architecture.Init() 里被 new 出来，
        /// 而那一刻未必在主线程、未必处于播放状态，此时创建 GameObject 会出问题。
        /// 延迟到第一次真正用到 UI 时（必定在业务代码里、必定在主线程）再建。
        /// </summary>
        public IUISystem UI
        {
            get
            {
                if (_system != null) return _system;

                var root = new GameObject("[UIRoot]");
                if (DontDestroyOnLoad) UnityEngine.Object.DontDestroyOnLoad(root);

                _system = new UISystem(_loader, root.transform, _indicator)
                {
                    ReferenceResolution = ReferenceResolution,
                    MatchWidthOrHeight = MatchWidthOrHeight,
                    CacheCapacity = CacheCapacity,
                };

                return _system;
            }
        }

        /// <summary>便捷访问：把面板容器挂到指定父节点下（做 UI 分屏/多相机时用）。</summary>
        public RectTransform GetLayerContent(UILayer layer) => _system?.GetLayerContent(layer);

        public void Dispose()
        {
            _system?.Dispose();
            _system = null;
        }
    }
}

#endif

// ============================================================================
//  在你的 Architecture 里这样注册（把下面这段抄进你的 Architecture 子类）：
//
//    using QFramework;
//    using WanXiang.Framework.Integration;
//    using WanXiang.Framework.UI;
//
//    public class WanXiangArchitecture : Architecture<WanXiangArchitecture>
//    {
//        protected override void Init()
//        {
//            // 原型期用 Resources；接入 YooAsset 后换成 YooAssetPanelLoader
//            RegisterUtility<UIService>(new UIService(new ResourcesPanelLoader()));
//
//            // 其余 Model / System 照常注册……
//        }
//    }
//
//  业务里的用法：
//
//    // 方式一：通过架构取（推荐，便于以后替换实现）
//    var ui = WanXiangArchitecture.Interface.GetUtility<UIService>().UI;
//    await ui.OpenAsync<BackpackPanel>();
//
//    // 方式二：面板内部直接用基类提供的辅助方法
//    protected override async UniTask OnOpenAsync(object payload)
//    {
//        await OpenPanelAsync<BeastDetailPanel>(beastId);
//    }
//
//  注意：如果你的 QFramework 版本 RegisterUtility 的签名不同
//  （例如要求泛型参数为接口），改成对应的重载即可，不影响 UIService 本身。
// ============================================================================
