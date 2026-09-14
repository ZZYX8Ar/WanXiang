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
        public static IResourceService Resource { get; private set; }

        /// <summary>初始化是否已经结束（无论成败）。</summary>
        public static bool IsFinished { get; private set; }

        /// <summary>初始化是否成功。</summary>
        public static bool IsReady => Resource != null && Resource.IsReady;

        private static UniTaskCompletionSource<bool> _readySource;

        /// <summary>
        /// 复位静态状态。
        /// </summary>
        /// <remarks>
        /// ⚠ 关掉「域重载」的编辑器下静态字段会跨 Play 会话残留，
        ///   残留的 Resource 会让第二次进 Play 时拿到一个已 Dispose 的服务。
        ///   详见 UIBootstrap.ResetStatics 的说明。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Resource = null;
            IsFinished = false;
            _readySource = null;
        }

        private void Awake()
        {
            if (Resource != null)
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

            // 先建好实例，让 WaitReadyAsync 的调用方即使还没开始初始化
            // 也能拿到一个非空的服务对象（否则它们要处理 null 分支，
            // 而"还没初始化"和"初始化失败"在调用点几乎无法区分）。
            Resource = new YooAssetResourceService();
            _readySource = new UniTaskCompletionSource<bool>();
            IsFinished = false;

            // ⚠ 注册工厂要放在**初始化之前**，而且必须在 UIBootstrap(-1000) 的
            //   Awake 之前完成 —— 本组件执行顺序 -1010，天然满足。
            //   工厂里包的是 Resource 这个静态属性（不是实例），
            //   所以即使此刻资源还没就绪，UIBootstrap 也能先拿到加载器；
            //   真正加载资源时加载器会检查 IsReady 并给出明确报错。
            if (_wireUIPanelLoader)
            {
                UIBootstrap.RegisterPanelLoaderFactory(() => new YooAssetPanelLoader(Resource));
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
            IsFinished = true;
            _readySource?.TrySetResult(ok);
        }

        /// <summary>
        /// 等资源系统就绪。依赖资源的启动步骤**必须** await 它。
        /// </summary>
        /// <returns>是否就绪。返回 false 时不要继续往下走。</returns>
        public static async UniTask<bool> WaitReadyAsync(CancellationToken ct = default)
        {
            if (IsFinished)
            {
                return IsReady;
            }

            if (_readySource == null)
            {
                // 场景里没有 ResourceBootstrap。
                Debug.LogError(
                    "[资源] 调用了 ResourceBootstrap.WaitReadyAsync，但场景里没有 ResourceBootstrap。\n" +
                    "请在启动场景加一个空物体并挂上本组件。");
                return false;
            }

            return await _readySource.Task.AttachExternalCancellation(ct);
        }

        private void OnDestroy()
        {
            if (Resource == null) return;

            Resource.Dispose();
            Resource = null;
            IsFinished = false;
            _readySource = null;
        }
    }
}
