// ============================================================================
//  万相 · 资源系统 · YooAsset 后端实现
//  ---------------------------------------------------------------------------
//  IResourceService 的 YooAsset 实现。放在**独立程序集**
//  （WanXiang.ResourceSystem.YooAsset）里，而不是塞进 WanXiang.Runtime。
//
//  为什么单独一个程序集：
//    与「WanXiang.Runtime 不依赖 QFramework」是同一条理由
//    （见 Framework/Inputs/IInputService.cs 头部注释）——
//    资源方案是本工程风险最高的外部依赖（要打包、要 CDN、要版本清单），
//    它一旦编译不过或行为异常，不应该连带把 UI / 输入 / 存档全部打死。
//    现在摘掉 WanXiang.ResourceSystem.YooAsset 这个程序集，
//    框架其余部分照样编译、照样跑（只是没有资源加载）。
//
//  ⚠ 引用计数是本类最要紧的纪律，两条规则：
//
//    ① 每一个成功的 LoadAssetAsync / LoadAssetSync 都对应**恰好一次**
//       ReleaseAsset。UISystem、对象池、异步加载中放弃…… 都是容易漏的点。
//       漏了不会报错，只会表现为"内存只涨不落"，到很后面才发现。
//       所以本类提供 LoadedLocationCount / DumpLoadedLocations 供排查。
//
//    ② 同一个 location 用不同类型加载（比如先当 Texture2D 再当 Sprite）
//       **不会**第二次加载 —— YooAsset 的 Provider 以 location 为单位，
//       第二次拿到的是同一个 Unity 对象。本类会检测到这一点并报错，
//       而不是返回一个 null 让调用方去猜。这是个常见配置错误。
//
//  ⚠ RawFile（文本/字节）不做引用计数：内容已经被读成 string / byte[]，
//    句柄读完立刻释放即可。这样调用方不必为"读一个配置表"这种一次性动作
//    操心生命周期。热更 DLL 走的就是这条路（见 LoadBytesSync）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace WanXiang.Framework.ResourceSystem.Backend
{
    /// <summary>
    /// 基于 YooAsset 的资源服务。
    /// </summary>
    /// <remarks>
    /// 使用方式：由 <c>ResourceBootstrap</c> 在启动场景创建并初始化，
    /// 业务代码只通过 <see cref="IResourceService"/> 使用。
    /// </remarks>
    public sealed class YooAssetResourceService : IResourceService
    {
        /// <summary>一个 location 的缓存条目。</summary>
        private sealed class AssetEntry
        {
            public AssetHandle Handle;
            public int RefCount;

            /// <summary>是否在"已加载但还没被任何业务拿走"的中间态。</summary>
            public bool IsLoading;
        }

        private readonly Dictionary<string, AssetEntry> _assets =
            new Dictionary<string, AssetEntry>(128, StringComparer.Ordinal);

        /// <summary>实例 → 它来自哪个 location。ReleaseInstance 要靠它减引用。</summary>
        private readonly Dictionary<GameObject, string> _instances =
            new Dictionary<GameObject, string>(64);

        private ResourcePackage _package;
        private ResourceInitOptions _options;
        private bool _disposed;

        // ------------------------------------------------------------ 状态

        public bool IsReady { get; private set; }

        public ResourcePlayMode PlayMode => _options?.PlayMode ?? ResourcePlayMode.EditorSimulate;

        public string PackageName => _package?.PackageName ?? (_options?.PackageName ?? ResourceInitOptions.DefaultPackageName);

        public string PackageVersion { get; private set; } = string.Empty;

        public string LastError { get; private set; }

        public int LoadedLocationCount => _assets.Count;

        public int SyncLoadCallCount { get; private set; }

        /// <summary>
        /// 当前正卡在哪一步。初始化或加载"没有报错但也没结果"时，先看这个。
        /// </summary>
        /// <remarks>
        /// ⚠ 之所以要专门为它留一个对外可见的属性：
        ///   资源系统挂起时**既不报错也不打日志**，从外面看就是"什么都没发生"。
        ///   有了它，一条 <c>yoo</c> 诊断就能回答"卡在请求版本，还是卡在更新清单"。
        ///   这是被真实故障逼出来的设计，不是过度设计。
        /// </remarks>
        public string LastStage { get; private set; } = "(未开始)";

        /// <summary>
        /// 打开桥接层追踪。资源加载疑似挂起时打开，能看到每一步的等待/完成。
        /// </summary>
        public static bool TraceAwaiter
        {
            get => YooAssetAwaiter.Trace;
            set => YooAssetAwaiter.Trace = value;
        }

        // -------------------------------------------------------- 初始化

        public async UniTask<bool> InitializeAsync(ResourceInitOptions options, CancellationToken ct = default)
        {
            if (_disposed)
            {
                LastError = "资源服务已释放，不能再次初始化。";
                Debug.LogError("[资源] " + LastError);
                return false;
            }

            // 幂等：启动流程、编辑器工具、测试都可能尝试初始化。
            if (IsReady)
            {
                return true;
            }

            if (options == null)
            {
                LastError = "初始化参数为 null。";
                Debug.LogError("[资源] " + LastError);
                return false;
            }

            string configError = options.Validate();
            if (configError != null)
            {
                LastError = "初始化参数不合法：" + configError;
                Debug.LogError("[资源] " + LastError);
                return false;
            }

            LastError = null;
            _options = options;

            try
            {
                LastStage = "① 初始化 YooAssets / 创建资源包";
                if (!YooAssets.Initialized)
                {
                    YooAssets.Initialize();
                }

                // TryGetPackage 而不是无条件 CreatePackage：
                // 编辑器下反复进入 Play 模式时，静态包列表可能还留着上一次的记录
                // （YooAssets 在编辑器里有 RuntimeInitializeOnLoadMethod 会清，
                //  但域重载被关掉时就不会清）。直接 Create 会抛
                // "Package already existed"。
                _package = YooAssets.TryGetPackage(options.PackageName);
                if (_package == null)
                {
                    _package = YooAssets.CreatePackage(options.PackageName);
                }

                LastStage = "② 装配文件系统";
                InitializeParameters parameters = BuildInitializeParameters(options);
                if (parameters == null)
                {
                    // BuildInitializeParameters 已写好 LastError
                    return false;
                }

                parameters.AutoUnloadBundleWhenUnused = options.AutoUnloadBundleWhenUnused;

                LastStage = "③ 初始化文件系统";
                InitializationOperation operation = _package.InitializeAsync(parameters);

                // 初始化不走进度条，所以传 null 进度。
                await operation.WaitAsync();

                if (operation.Status != EOperationStatus.Succeed)
                {
                    LastError = $"资源包「{options.PackageName}」初始化失败：{operation.Error}";
                    Debug.LogError("[资源] " + LastError);
                    return false;
                }

                // ⚠⚠ 这一步不能省，而且是本类踩过的第一个坑。
                //
                //   ResourcePackage.InitializeAsync **只负责初始化文件系统**，
                //   它做完之后 ActiveManifest 仍然是 null
                //   （已核对 YooAsset 2.3.19 源码 InitializationOperation：
                //    它的全部步骤就是 Prepare → ClearOldFileSystem → InitFileSystem，
                //    从头到尾没有碰过 ActiveManifest）。
                //
                //   清单是在「请求版本 → 更新清单」之后才挂上去的。
                //   少这两步的症状极具迷惑性：**初始化报成功**，
                //   然后第一次访问清单时抛
                //     Exception: Can not found active package manifest !
                //   而这句异常来自 DebugCheckInitialize，堆栈指向
                //   GetPackageVersion / GetAllAssetInfos 之类的**无辜调用点**，
                //   看起来像是"读版本号读错了"，实际根因在初始化阶段少跑了两步。
                //
                //   三步顺序与官方样例 FsmInitializePackage → FsmRequestPackageVersion
                //   → FsmUpdatePackageManifest 完全一致。
                string version = await RequestVersionAndManifestAsync();
                if (version == null)
                {
                    // RequestVersionAndManifestAsync 已写好 LastError
                    return false;
                }

                PackageVersion = version;
                IsReady = true;

                Debug.Log($"[资源] 资源系统就绪。包「{PackageName}」，模式 {options.PlayMode}，" +
                          $"版本 {PackageVersion}，清单内资源 {_package.GetAllAssetInfos().Length} 个。");
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"资源系统初始化异常：{ex.Message}";
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// 请求资源版本 → 更新清单。成功返回版本号，失败返回 null（并写好 <see cref="LastError"/>）。
        /// </summary>
        /// <remarks>
        /// 提取成方法是因为两种场景都要它：
        ///   ① 初始化时（三种模式都必需，否则 ActiveManifest 是 null）；
        ///   ② Host 模式热更时（先更新清单才知道要下载什么）。
        /// </remarks>
        private async UniTask<string> RequestVersionAndManifestAsync()
        {
            LastStage = "④ 请求资源版本";
            RequestPackageVersionOperation versionOperation = _package.RequestPackageVersionAsync();
            await versionOperation.WaitAsync();

            if (versionOperation.Status != EOperationStatus.Succeed)
            {
                LastError = $"请求资源版本失败：{versionOperation.Error}";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            string version = versionOperation.PackageVersion;

            LastStage = $"⑤ 更新资源清单（版本 {version}）";
            UpdatePackageManifestOperation manifestOperation =
                _package.UpdatePackageManifestAsync(version);
            await manifestOperation.WaitAsync();

            if (manifestOperation.Status != EOperationStatus.Succeed)
            {
                LastError = $"更新资源清单失败（版本 {version}）：{manifestOperation.Error}";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            return version;
        }

        /// <summary>
        /// 按运行模式装配文件系统。失败时写好 <see cref="LastError"/> 并返回 null。
        /// </summary>
        private InitializeParameters BuildInitializeParameters(ResourceInitOptions options)
        {
            switch (options.PlayMode)
            {
                case ResourcePlayMode.EditorSimulate:
                {
#if UNITY_EDITOR
                    // 模拟构建会把「收集器能看到的资源」映射成一份虚拟清单，
                    // 落在 <工程>/yoo/Simulate/<包名>/ 下。
                    // ⚠ 这一步依赖收集器配置（Assets/AssetBundleCollectorSetting.asset）
                    //   里存在同名包裹；没配的话 YooAsset 会抛
                    //   "Not found package : xxx"，信息很短，看不出该去点哪个菜单，
                    //   所以这里补一句人话。
                    PackageInvokeBuildResult buildResult;
                    try
                    {
                        buildResult = EditorSimulateModeHelper.SimulateBuild(options.PackageName);
                    }
                    catch (Exception ex)
                    {
                        LastError =
                            $"编辑器模拟构建失败：{DescribeException(ex)}\n" +
                            $"如果是「Not found package : {options.PackageName}」，" +
                            "说明 YooAsset 在**磁盘上**的收集器配置里找不到这个包裹。\n" +
                            "  ⚠ 注意这种坏法：配置可能只写进了内存没落盘 —— " +
                            "收集器窗口里看得见包，但 YooAsset 读文件时是空的。\n" +
                            "  用菜单「万相 / 资源 / 打印收集器状态」看「落盘状态」那一行；\n" +
                            "  要修就跑「万相 / 资源 / 初始化 YooAsset 收集器配置」，然后重启 Play。";
                        Debug.LogError("[资源] " + LastError);
                        return null;
                    }

                    if (buildResult == null || string.IsNullOrEmpty(buildResult.PackageRootDirectory))
                    {
                        LastError = "编辑器模拟构建没有产出包根目录，资源无法加载。";
                        Debug.LogError("[资源] " + LastError);
                        return null;
                    }

                    var editorParameters = new EditorSimulateModeParameters();
                    editorParameters.EditorFileSystemParameters =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(buildResult.PackageRootDirectory);

                    Debug.Log($"[资源] 编辑器模拟包根目录：{buildResult.PackageRootDirectory}");
                    return editorParameters;
#else
                    LastError =
                        "EditorSimulate 模式只能在编辑器下使用。" +
                        "玩家包请用 Offline（单机内置）或 Host（联机热更）。";
                    Debug.LogError("[资源] " + LastError);
                    return null;
#endif
                }

                case ResourcePlayMode.Offline:
                {
                    // 内置文件系统：从 StreamingAssets/yoo/<包名>/ 读。
                    // ⚠ 必须已经用「构建资产包」产出过内容，否则清单为空。
                    var offlineParameters = new OfflinePlayModeParameters();
                    offlineParameters.BuildinFileSystemParameters =
                        FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    return offlineParameters;
                }

                case ResourcePlayMode.Host:
                {
                    string fallback = string.IsNullOrEmpty(options.FallbackHostServer)
                        ? options.HostServer
                        : options.FallbackHostServer;

                    var remoteServices = new HostRemoteServices(options.HostServer, fallback);

                    var hostParameters = new HostPlayModeParameters();
                    hostParameters.BuildinFileSystemParameters =
                        FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                    hostParameters.CacheFileSystemParameters =
                        FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                    return hostParameters;
                }

                default:
                    LastError = $"未支持的资源运行模式：{options.PlayMode}";
                    Debug.LogError("[资源] " + LastError);
                    return null;
            }
        }

        /// <summary>
        /// 把异常摊成能看的文本，重点是**把内层异常挖出来**。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是被真实故障逼出来的一个方法。
        ///   <c>EditorSimulateModeHelper.SimulateBuild</c> 是通过反射调到
        ///   <c>YooAsset.Editor.AssetBundleSimulateBuilder.SimulateBuild</c> 的，
        ///   目标方法一抛，反射就叫 <see cref="System.Reflection.TargetInvocationException"/>，
        ///   而它的 Message 永远只有一句
        ///   "Exception has been thrown by the target of an invocation." —— 零信息量。
        ///   真正的原因（那次是 <c>Not found package : WanXiang</c>）躺在 InnerException 里。
        ///
        ///   只打最外层的 Message，排查体验就是：报错看着有了，但你得再跑到
        ///   控制台里翻 YooAsset 自己打的那一行才知道发生了什么。
        ///   这里把整条 InnerException 链连同最外层的头几帧堆栈一起摊平，
        ///   让一条日志自给自足。
        /// </remarks>
        private static string DescribeException(Exception ex)
        {
            if (ex == null) return "(未知异常)";

            var sb = new System.Text.StringBuilder();

            // 最多挖 5 层，防御性地防止自引用导致的死循环。
            int depth = 0;
            for (Exception cur = ex; cur != null && depth < 5; cur = cur.InnerException, depth++)
            {
                if (depth > 0)
                {
                    sb.Append("\n  ↳ 内层：");
                }
                sb.Append(cur.GetType().Name).Append(": ").Append(cur.Message);

                // 堆栈只取最外层的前 4 帧 —— 全量打出来会把日志淹掉，
                // 而定位问题通常只需要知道"是从哪个入口炸出来的"。
                if (depth == 0 && !string.IsNullOrEmpty(cur.StackTrace))
                {
                    string[] frames = cur.StackTrace.Split('\n');
                    int take = Math.Min(frames.Length, 4);
                    for (int i = 0; i < take; i++)
                    {
                        sb.Append("\n    ").Append(frames[i].Trim());
                    }
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------ 加载

        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            if (!CheckReadyForLoad(location, out T early)) return early;

            // ---- 已缓存：加引用直接返回 ----
            if (_assets.TryGetValue(location, out AssetEntry entry) &&
                entry.Handle != null && entry.Handle.IsValid)
            {
                var cached = entry.Handle.GetAssetObject<T>();
                if (cached != null)
                {
                    entry.RefCount++;
                    return cached;
                }

                // 类型对不上：同一个 location 被用两种类型加载。
                LastError =
                    $"location「{location}」已经以其它类型加载过了。" +
                    "YooAsset 以 location 为单位复用同一个对象，" +
                    "同一个地址不能既当 Texture2D 又当 Sprite 加载 —— " +
                    "请在收集器里给它们不同的地址。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            AssetHandle handle = null;
            try
            {
                handle = _package.LoadAssetAsync<T>(location);
                await handle.WaitAsync();

                if (ct.IsCancellationRequested)
                {
                    handle.Release();
                    return null;
                }

                if (handle.Status != EOperationStatus.Succeed)
                {
                    LastError = $"加载「{location}」失败：{handle.LastError}";
                    Debug.LogError("[资源] " + LastError);
                    handle.Release();
                    return null;
                }

                var asset = handle.GetAssetObject<T>();
                if (asset == null)
                {
                    LastError =
                        $"「{location}」加载成功但类型不是 {typeof(T).Name}。" +
                        "检查收集器里的地址是否指到了别的资源。";
                    Debug.LogError("[资源] " + LastError);
                    handle.Release();
                    return null;
                }

                _assets[location] = new AssetEntry { Handle = handle, RefCount = 1 };
                return asset;
            }
            catch (Exception ex)
            {
                LastError = $"加载「{location}」异常：{ex.Message}";
                Debug.LogException(ex);

                // 句柄已经拿到但流程中途失败 → 必须还回去，否则这个 Bundle
                // 的引用永远挂着。这是最容易漏的一处释放。
                if (handle != null && handle.IsValid)
                {
                    handle.Release();
                }
                return null;
            }
        }

        public T LoadAssetSync<T>(string location) where T : UnityEngine.Object
        {
            SyncLoadCallCount++;

            if (!CheckReadyForLoad(location, out T early)) return early;

            if (_assets.TryGetValue(location, out AssetEntry entry) &&
                entry.Handle != null && entry.Handle.IsValid)
            {
                var cached = entry.Handle.GetAssetObject<T>();
                if (cached != null)
                {
                    entry.RefCount++;
                    return cached;
                }
                LastError = $"location「{location}」已以其它类型加载过，无法再当 {typeof(T).Name} 取。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            AssetHandle handle = null;
            try
            {
                handle = _package.LoadAssetSync<T>(location);
                if (handle.Status != EOperationStatus.Succeed)
                {
                    LastError = $"同步加载「{location}」失败：{handle.LastError}";
                    Debug.LogError("[资源] " + LastError);
                    if (handle.IsValid) handle.Release();
                    return null;
                }

                var asset = handle.GetAssetObject<T>();
                if (asset == null)
                {
                    LastError = $"同步加载「{location}」成功但类型不是 {typeof(T).Name}。";
                    Debug.LogError("[资源] " + LastError);
                    if (handle.IsValid) handle.Release();
                    return null;
                }

                _assets[location] = new AssetEntry { Handle = handle, RefCount = 1 };
                return asset;
            }
            catch (Exception ex)
            {
                LastError = $"同步加载「{location}」异常：{ex.Message}";
                Debug.LogException(ex);
                if (handle != null && handle.IsValid) handle.Release();
                return null;
            }
        }

        public void ReleaseAsset(string location)
        {
            if (string.IsNullOrEmpty(location)) return;
            if (!_assets.TryGetValue(location, out AssetEntry entry)) return;

            entry.RefCount--;

            if (entry.RefCount > 0)
            {
                return;
            }

            // 引用归零。真正卸不卸 Bundle 由 YooAsset 决定
            // （AutoUnloadBundleWhenUnused + 该 Bundle 里其它资源的引用情况）。
            // 我们这里只负责"不再持有这个句柄"。
            if (entry.Handle != null && entry.Handle.IsValid)
            {
                entry.Handle.Release();
            }
            entry.Handle = null;
            _assets.Remove(location);
        }

        // ---------------------------------------------------------- 实例化

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null,
            CancellationToken ct = default)
        {
            if (!IsReady)
            {
                LastError = "资源系统未初始化，不能实例化。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            // 先按资源加载一次（引用 +1）。
            GameObject prefab = await LoadAssetAsync<GameObject>(location, ct);
            if (prefab == null)
            {
                return null;
            }

            InstantiateOperation operation = null;
            try
            {
                AssetHandle handle = _assets[location].Handle;
                operation = handle.InstantiateAsync(parent, true);

                await operation.WaitAsync();

                if (operation.Status != EOperationStatus.Succeed || operation.Result == null)
                {
                    LastError = $"实例化「{location}」失败：{operation.Error}";
                    Debug.LogError("[资源] " + LastError);
                    ReleaseAsset(location);      // 实例化失败，把上面那次引用还回去
                    return null;
                }

                _instances[operation.Result] = location;
                return operation.Result;
            }
            catch (Exception ex)
            {
                LastError = $"实例化「{location}」异常：{ex.Message}";
                Debug.LogException(ex);
                ReleaseAsset(location);
                return null;
            }
        }

        public void ReleaseInstance(GameObject instance)
        {
            if (instance == null) return;

            // 先减资源引用，再销毁对象 —— 顺序反了会短暂出现
            // "对象已销毁但引用还挂着"，内存图上会看到一次无意义的尖峰。
            if (_instances.TryGetValue(instance, out string location))
            {
                _instances.Remove(instance);
                ReleaseAsset(location);
            }
            else
            {
                Debug.LogWarning(
                    $"[资源] ReleaseInstance 收到不是本服务创建的实例「{instance.name}」。\n" +
                    "多半是有人自己 Object.Instantiate 了 Prefab。" +
                    "请一律走 IResourceService.InstantiateAsync —— " +
                    "否则资源系统不知道这个实例的存在，清理时会判断错。");
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        // -------------------------------------------------------- 文本字节

        public async UniTask<string> LoadTextAsync(string location, CancellationToken ct = default)
        {
            if (!IsReady)
            {
                LastError = "资源系统未初始化，不能读取文本。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            RawFileHandle handle = null;
            try
            {
                handle = _package.LoadRawFileAsync(location);
                await handle.WaitAsync();

                if (handle.Status != EOperationStatus.Succeed)
                {
                    LastError = $"读取文本「{location}」失败：{handle.LastError}\n" +
                                "提示：LoadRawFile 要求该文件在收集器里的**打包规则是「打包原生文件」" +
                                "（PackRawFile）**，产出的才是 RawBundle。\n" +
                                "按普通资源收集（PackDirectory / PackSeparately 等）产出的 AssetBundle " +
                                "不能用 LoadRawFile 读。（YooAsset 2.3 的收集器类型里没有 RawFileCollector，" +
                                "别去找它 —— 那是 1.x 的说法。）";
                    Debug.LogError("[资源] " + LastError);
                    if (handle.IsValid) handle.Release();
                    return null;
                }

                string text = handle.GetRawFileText();
                // 内容已经是托管 string 了，句柄读完立即还。
                if (handle.IsValid) handle.Release();
                return text;
            }
            catch (Exception ex)
            {
                LastError = $"读取文本「{location}」异常：{ex.Message}";
                Debug.LogException(ex);
                if (handle != null && handle.IsValid) handle.Release();
                return null;
            }
        }

        public async UniTask<byte[]> LoadBytesAsync(string location, CancellationToken ct = default)
        {
            if (!IsReady)
            {
                LastError = "资源系统未初始化，不能读取字节。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            RawFileHandle handle = null;
            try
            {
                handle = _package.LoadRawFileAsync(location);
                await handle.WaitAsync();

                if (handle.Status != EOperationStatus.Succeed)
                {
                    LastError = $"读取字节「{location}」失败：{handle.LastError}\n" +
                                "提示：LoadRawFile 要求该文件的打包规则是「打包原生文件」（PackRawFile）。";
                    Debug.LogError("[资源] " + LastError);
                    if (handle.IsValid) handle.Release();
                    return null;
                }

                byte[] bytes = handle.GetRawFileData();
                if (handle.IsValid) handle.Release();
                return bytes;
            }
            catch (Exception ex)
            {
                LastError = $"读取字节「{location}」异常：{ex.Message}";
                Debug.LogException(ex);
                if (handle != null && handle.IsValid) handle.Release();
                return null;
            }
        }

        public byte[] LoadBytesSync(string location)
        {
            SyncLoadCallCount++;

            if (!IsReady)
            {
                LastError = "资源系统未初始化，不能同步读取字节。";
                Debug.LogError("[资源] " + LastError);
                return null;
            }

            RawFileHandle handle = null;
            try
            {
                handle = _package.LoadRawFileSync(location);
                if (handle.Status != EOperationStatus.Succeed)
                {
                    LastError = $"同步读取字节「{location}」失败：{handle.LastError}";
                    Debug.LogError("[资源] " + LastError);
                    if (handle.IsValid) handle.Release();
                    return null;
                }

                byte[] bytes = handle.GetRawFileData();
                if (handle.IsValid) handle.Release();
                return bytes;
            }
            catch (Exception ex)
            {
                LastError = $"同步读取字节「{location}」异常：{ex.Message}";
                Debug.LogException(ex);
                if (handle != null && handle.IsValid) handle.Release();
                return null;
            }
        }

        // ------------------------------------------------------------ 更新

        public async UniTask<ResourceUpdateReport> UpdatePackageAsync(IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (!IsReady)
            {
                return ResourceUpdateReport.Fail("资源系统未初始化，不能更新。");
            }

            // 编辑器模拟与离线模式没有远端可连。
            // 直接返回成功，调用方就不用为这两种模式写分支 ——
            // 这正是"模式差异封在实现里"的意义。
            if (_options.PlayMode != ResourcePlayMode.Host)
            {
                return ResourceUpdateReport.Ok(PackageVersion, 0, 0);
            }

            try
            {
                // ---- ① 请求版本 + ② 更新清单（与初始化阶段同一套，抽成方法了） ----
                string version = await RequestVersionAndManifestAsync();
                if (version == null)
                {
                    // RequestVersionAndManifestAsync 已写好 LastError
                    return ResourceUpdateReport.Fail(LastError);
                }

                PackageVersion = version;

                // ---- ③ 下载缺失文件 ----
                ResourceDownloaderOperation downloader =
                    _package.CreateResourceDownloader(_options.DownloadingMaxNumber, _options.FailedTryAgain);

                int totalCount = downloader.TotalDownloadCount;
                long totalBytes = downloader.TotalDownloadBytes;

                if (totalCount <= 0)
                {
                    return ResourceUpdateReport.Ok(version, 0, 0);
                }

                downloader.BeginDownload();
                await downloader.WaitAsync(progress);

                if (downloader.Status != EOperationStatus.Succeed)
                {
                    return ResourceUpdateReport.Fail($"下载资源失败：{downloader.Error}");
                }

                return ResourceUpdateReport.Ok(version, totalCount, totalBytes);
            }
            catch (Exception ex)
            {
                return ResourceUpdateReport.Fail($"资源更新异常：{ex.Message}");
            }
        }

        // ------------------------------------------------------------ 诊断

        public bool CheckLocationValid(string location)
        {
            if (!IsReady || string.IsNullOrEmpty(location)) return false;
            return _package.CheckLocationValid(location);
        }

        public void UnloadUnused()
        {
            if (!IsReady) return;

            // 不等待：调用点通常是切场景的收尾，阻塞在这里没有收益。
            _package.UnloadUnusedAssetsAsync().WaitAsync().Forget();
        }

        public void DumpLoadedLocations()
        {
            if (_assets.Count == 0)
            {
                Debug.Log("[资源] 当前没有任何已加载资源（没有泄漏）。");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[资源] 当前已加载 {_assets.Count} 个 location：" +
                          $"（实例 {_instances.Count} 个）");
            foreach (KeyValuePair<string, AssetEntry> pair in _assets)
            {
                sb.AppendLine($"    x{pair.Value.RefCount,-3} {pair.Key}");
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------ 释放

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsReady = false;

            foreach (AssetEntry entry in _assets.Values)
            {
                if (entry.Handle != null && entry.Handle.IsValid)
                {
                    entry.Handle.Release();
                }
            }
            _assets.Clear();
            _instances.Clear();

            // ⚠ 只销毁自己创建的资源包，不调 YooAssets.Destroy()。
            //   YooAssets 是全局静态的，销毁它会连带干掉别的系统
            //   （多包共存时尤其明显）。编辑器退出 Play 时 Unity 会自己清。
            //
            // ⚠ DestroyAsync() 返回的是 DestroyOperation，**不是 UniTask**，
            //   必须先过一层 WaitAsync() 才能 Forget。少了这层会报
            //   CS1929: 'DestroyOperation' does not contain a definition for 'Forget'。
            if (_package != null)
            {
                _package.DestroyAsync().WaitAsync().Forget();
                _package = null;
            }
        }

        // ------------------------------------------------------------ 内部

        /// <summary>
        /// 加载前的统一校验。返回 false 时 <paramref name="result"/> 是应返回的默认值。
        /// </summary>
        private bool CheckReadyForLoad<T>(string location, out T result) where T : UnityEngine.Object
        {
            result = null;

            if (!IsReady)
            {
                LastError = "资源系统未初始化，不能加载。请确认场景里有 ResourceBootstrap 且它初始化成功。";
                Debug.LogError("[资源] " + LastError);
                return false;
            }

            if (string.IsNullOrEmpty(location))
            {
                LastError = "加载地址为空。";
                Debug.LogError("[资源] " + LastError);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 远端地址查询服务。YooAsset 只要求「给我文件名，还我 URL」。
        /// </summary>
        private sealed class HostRemoteServices : IRemoteServices
        {
            private readonly string _defaultHost;
            private readonly string _fallbackHost;

            public HostRemoteServices(string defaultHost, string fallbackHost)
            {
                _defaultHost = defaultHost;
                _fallbackHost = fallbackHost;
            }

            string IRemoteServices.GetRemoteMainURL(string fileName)
            {
                return $"{_defaultHost}/{fileName}";
            }

            string IRemoteServices.GetRemoteFallbackURL(string fileName)
            {
                return $"{_fallbackHost}/{fileName}";
            }
        }
    }
}
