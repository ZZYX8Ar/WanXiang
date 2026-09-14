// ============================================================================
//  万相 · 资源启动引导
//  ---------------------------------------------------------------------------
//  挂在启动场景的空 GameObject 上（建议命名 [ResourceBootstrap]）。
//
//  它做的事：按配置创建资源服务 → 异步初始化 → 把就绪状态广播出去。
//
//  ⚠ 执行顺序 -1010 —— **排在所有 Bootstrap 之前**。
//    因为 UI / 输入 / 配置表都可能在启动时就要资源：
//      InputBootstrap(-990) 要加载 InputActionAsset
//      UIBootstrap(-1000)   要建 Canvas 层级，启动面板可能要加载 Prefab
//    资源没就绪时它们只能各自写兜底逻辑，那是重复且迟早不一致的。
//    所以资源必须最先开始。
//
//  ⚠ 但顺序号只保证「最先**开始**」，不保证「最先**完成**」——
//    初始化是异步的（模拟构建、请求版本、更新清单）。
//    依赖资源的启动步骤必须显式 await：
//        bool ok = await ResourceBootstrap.WaitReadyAsync(ct);
//    别用"我排得比它晚所以它肯定好了"这种假设，它在编辑器下能跑通，
//    到了 Host 模式（要连网）就会变成偶发的空引用。
//
//  ⚠ 为什么不叫 Bootstrap.Resources 而是 Bootstrap.Resource？
//    因为 MonoBehaviour 里有个同名的 UnityEngine.Resources，
//    一旦写成 static 属性就会在本文件内遮蔽掉它。
//    本文件目前不需要 Resources.Load，但将来某个人加一行兜底加载时
//    会撞上一个"看起来莫名其妙"的编译错误。名字换个字就永久避开了。
//
//  ⚠ 失败时的行为：**不抛异常、不退出**，只打一条明确的中文报错。
//    理由：编辑器里没配收集器、Host 模式连不上 CDN，
//    都是很常见的"还没准备好"，不是代码缺陷。
//    让它抛异常会把整个启动流程炸掉，反而看不到真正的原因。
//    但失败后所有加载都会返回 null 并打出"资源系统未初始化"，
//    问题不会静默。
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.ResourceSystem;
using WanXiang.Framework.ResourceSystem.Backend;

namespace WanXiang.Framework.Boot
{
    [DefaultExecutionOrder(-1010)]
    [DisallowMultipleComponent]
    public sealed class ResourceBootstrap : MonoBehaviour
    {
        [Header("运行模式")]
        [Tooltip("EditorSimulate：编辑器下读工程源文件，不构建，迭代最快（仅编辑器可用）。\n" +
                 "Offline：从 StreamingAssets 读随包 Bundle，单机内置。\n" +
                 "Host：内置 Bundle + 远端下载 + 本地缓存，联机热更。")]
        [SerializeField] private ResourcePlayMode _playMode = ResourcePlayMode.EditorSimulate;

        [Tooltip("资源包名。必须与 YooAsset 收集器里的「包裹名」完全一致。")]
        [SerializeField] private string _packageName = ResourceInitOptions.DefaultPackageName;

        [Header("Host 模式（联机热更）")]
        [Tooltip("主资源服务器地址。末尾不要带斜杠。例如 http://cdn.example.com/WanXiang/PC")]
        [SerializeField] private string _hostServer = "";

        [Tooltip("备用服务器地址。留空则与主服务器相同。")]
        [SerializeField] private string _fallbackHostServer = "";

        [Tooltip("同时下载的最大 Bundle 数。移动端别开太高，10~16 是常见甜点。")]
        [SerializeField] private int _downloadingMaxNumber = 10;

        [Tooltip("单个文件下载失败后的重试次数。")]
        [SerializeField] private int _failedTryAgain = 3;

        [Header("资源生命周期")]
        [Tooltip("引用计数归零时自动卸载 Bundle。\n" +
                 "关着更稳（默认），打开更省内存但要求引用计数绝对正确。\n" +
                 "建议先用 false 把功能跑通，等资源日志稳定了再考虑打开。")]
        [SerializeField] private bool _autoUnloadBundleWhenUnused = false;

        [Tooltip("是否跨场景保留。资源服务应当常驻。")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Header("启动行为")]
        [Tooltip("挂上后立刻开始初始化。取消勾选则由启动流程显式调 InitializeAsync。\n" +
                 "（有些项目要先做完版本校验/公告再加载资源，那时需要手动控制时机。）")]
        [SerializeField] private bool _initializeOnAwake = true;

        [Tooltip("初始化后自动给 UIBootstrap 注册「走资源系统」的面板加载器工厂。\n" +
                 "取消勾选则面板继续用原型期的 Resources 加载器（收集器还没配好时的退路）。")]
        [SerializeField] private bool _wireUIPanelLoader = true;

        /// <summary>
        /// 全局资源服务入口。业务代码通过它加载资源。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是个**转发属性**，真正的状态在 <see cref="ResourceHub"/>。
        ///
        ///   为什么改成转发：本类在**后端程序集**（WanXiang.ResourceSystem.YooAsset）里，
        ///   而框架层（WanXiang.Runtime）刻意不引用后端 ——
        ///   于是框架代码（例如 HotUpdateBootstrap）根本看不见本类型，
        ///   想"等资源就绪"就会撞 CS0103。
        ///   状态搬到抽象层的 ResourceHub 之后，两边都读同一份，
        ///   不会出现"后端的 IsFinished 是 true、框架看到的是 false"这种鬼故事。
        ///
        ///   保留这个属性是为了不破坏既有调用点（体检脚本、面板加载器注释里都提到它）。
        /// </remarks>
        public static IResourceService Resource => ResourceHub.Current;

        /// <summary>初始化是否已经结束（无论成败）。转发自 <see cref="ResourceHub"/>。</summary>
        public static bool IsFinished => ResourceHub.IsFinished;

        /// <summary>初始化是否成功。转发自 <see cref="ResourceHub"/>。</summary>
        public static bool IsReady => ResourceHub.IsReady;

        private void Awake()
        {
            if (ResourceHub.Current != null)
            {
                Debug.LogWarning(
                    "[资源] 检测到第二个 ResourceBootstrap，已销毁重复实例。请确认场景里只放了一个。");
                Destroy(gameObject);
                return;
            }

            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            // 注册到抽象层。此后框架侧用 ResourceHub.WaitReadyAsync 等它。
            // ⚠ 顺序：Register 必须在 InitializeAsync **之前** ——
            //   否则"已注册但还没开始初始化"这段空窗会被跳过，
            //   期间来的等待者会看到 _readySource 为 null 而误报"没有后端注册"。
            ResourceHub.Register(new YooAssetResourceService());

            // ⚠ 注册工厂要放在**初始化之前**，而且必须在 UIBootstrap(-1000) 的
            //   Awake 之前完成 —— 本组件执行顺序 -1010，天然满足。
            //   工厂里包的是 ResourceHub.Current（不是实例），
            //   所以即使此刻资源还没就绪，UIBootstrap 也能先拿到加载器；
            //   真正加载资源时加载器会检查 IsReady 并给出明确报错。
            if (_wireUIPanelLoader)
            {
                UIBootstrap.RegisterPanelLoaderFactory(() => new YooAssetPanelLoader(ResourceHub.Current));
            }

            if (_initializeOnAwake)
            {
                RunInitializeAsync().Forget();
            }
        }

        /// <summary>
        /// 显式初始化。仅当 <see cref="_initializeOnAwake"/> 关掉时才需要调。
        /// </summary>
        public void InitializeAsync()
        {
            RunInitializeAsync().Forget();
        }

        private async UniTaskVoid RunInitializeAsync()
        {
            var options = new ResourceInitOptions
            {
                PackageName = _packageName,
                PlayMode = _playMode,
                HostServer = _hostServer,
                FallbackHostServer = _fallbackHostServer,
                DownloadingMaxNumber = _downloadingMaxNumber,
                FailedTryAgain = _failedTryAgain,
                AutoUnloadBundleWhenUnused = _autoUnloadBundleWhenUnused,
            };

            bool ok = false;
            try
            {
                ok = await Resource.InitializeAsync(options, this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                // 退出时正常路径：对象销毁导致令牌取消。
                Finish(false);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ok = false;
            }

            if (ok && _wireUIPanelLoader)
            {
                // 工厂已经在 Awake 里注册过了，这里只需要确认它确实被 UIBootstrap 取用。
                // 如果顺序没生效（比如两者不在同一场景），UIBootstrap 会自己报错，
                // 不必在这里重复判断。
                Debug.Log($"[资源] 已就绪。面板加载器：YooAssetPanelLoader（包「{Resource.PackageName}」，模式 {Resource.PlayMode}）。");
            }

            Finish(ok);
        }

        private void Finish(bool ok)
        {
            ResourceHub.ReportFinished(ok);
        }

        /// <summary>
        /// 等资源系统就绪。依赖资源的启动步骤**必须** await 它。
        /// </summary>
        /// <returns>是否就绪。返回 false 时不要继续往下走。</returns>
        /// <remarks>
        /// 转发到 <see cref="ResourceHub.WaitReadyAsync"/>。
        /// 保留本方法是为了不破坏既有调用点，且"从 ResourceBootstrap 等资源"
        /// 读起来比"从 Hub 等"更直白。两者是同一个门，不会出现两套状态。
        /// </remarks>
        public static UniTask<bool> WaitReadyAsync(CancellationToken ct = default)
        {
            return ResourceHub.WaitReadyAsync(ct);
        }

        private void OnDestroy()
        {
            IResourceService service = ResourceHub.Current;
            if (service == null) return;

            service.Dispose();
            ResourceHub.Clear();
        }
    }
}
