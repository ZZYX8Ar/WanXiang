// ============================================================================
//  万相 · 热更启动引导（AOT 侧）
//  ---------------------------------------------------------------------------
//  挂在启动场景的空 GameObject 上（建议命名 [HotUpdateBootstrap]）。
//
//  它把「热更」这件事从头到尾串成一条链，五段：
//
//    ① 等资源系统就绪      ResourceHub.WaitReadyAsync
//    ② 补 AOT 泛型元数据    AOTMetadataLoader.LoadAsync
//    ③ 取热更 DLL 字节      IResourceService.LoadBytesAsync
//    ④ 加载热更程序集       HotUpdateLoader.Load
//    ⑤ 初始化热更入口       entry.Initialize()
//
//  之后每帧 Update 驱动 entry.Tick(deltaTime)。
//
//  ---------------------------------------------------------------------------
//  ⚠ 执行顺序 -1005 —— 夹在 ResourceBootstrap(-1010) 与 UIBootstrap(-1000) 之间。
//
//    · 必须晚于 ResourceBootstrap：第③步要用资源服务。
//      （顺序号只保证"开始得晚"，实际依赖靠 await —— 见下面的说明。）
//
//    · 必须早于 UIBootstrap / InputBootstrap：
//      热更层往往要接管 UI 注册、输入上下文、玩法初始化。
//      如果 UI 先把自己初始化完了，热更层再想插手就得做一遍"卸载重装"，
//      那是纯粹的浪费。让热更层先就位，UI 起来时热更逻辑已经在了。
//
//  ⚠ 但顺序号**只保证"最先开始"，不保证"最先完成"** ——
//    ①~⑤ 全是异步的（尤其 Host 模式要连网下载）。
//    所以任何依赖热更已就绪的逻辑，都必须显式：
//        var entry = await HotUpdateBootstrap.WaitReadyAsync(ct);
//    别假设"我排得比它晚所以它肯定好了"。那条假设在编辑器下能跑通，
//    到 Host 模式就变成偶发的空引用 —— 和 ResourceBootstrap 那条注释一个道理。
//
//  ---------------------------------------------------------------------------
//  ⚠ 失败时的行为：**不抛异常、不退出**，只打一条明确的中文报错。
//
//    与 ResourceBootstrap 保持一致的取舍：
//      · 编辑器里还没发布过热更产物
//      · Host 模式连不上 CDN
//      · 热更 DLL 里入口类名改了但 AOT 侧常量没跟着改
//    这些都是"还没准备好"或"配置写错了"，不是代码缺陷。
//    让它们抛异常会把整个启动流程炸掉，反而看不到真正的原因。
//
//    但失败后 Stage 会停在出错那一段、LastError 里写着中文原因，
//    HotUpdateLoader.Current 保持 null —— 问题不会静默。
//
//  ---------------------------------------------------------------------------
//  ⚠ 为什么还要一个 MonoBehaviour，而不是让 ResourceBootstrap 顺手做完
//
//    因为"资源就绪"和"热更就绪"是**两个独立的里程碑**，依赖它们的东西不同：
//      · UI / 输入 只依赖资源就绪
//      · 玩法初始化 依赖热更就绪
//    混在一个组件里，UI 就得等热更（Host 模式下要多等一次网络往返），
//    而热更失败会连带 UI 也起不来。分开之后失败面是隔离的。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.HotUpdate;
using WanXiang.Framework.ResourceSystem;

namespace WanXiang.Framework.Boot
{
    [DefaultExecutionOrder(-1005)]
    [DisallowMultipleComponent]
    public sealed class HotUpdateBootstrap : MonoBehaviour
    {
        [Header("热更入口")]
        [Tooltip("热更入口类的完整类型名。必须与热更工程里的实现类一致，否则第④步会明确报错。")]
        [SerializeField] private string _entryTypeName = HotUpdateLoader.DefaultEntryTypeName;

        [Tooltip("热更入口所在程序集名。决定从资源系统找哪个 DLL。")]
        [SerializeField] private string _hotUpdateAssembly = HotUpdateLocations.DefaultHotUpdateAssembly;

        [Header("AOT 泛型元数据")]
        [Tooltip("是否补 AOT 泛型元数据。\n" +
                 "热更代码用到「泛型 + 值类型实参」时必须开，否则真机上会\n" +
                 "ExecutionEngineException: method body is null。\n" +
                 "（编辑器下这个动作是空实现，开不开都一样，见 AOTMetadataLoader 文件头。）")]
        [SerializeField] private bool _loadAotMetadata = true;

        [Tooltip("手工指定要补元数据的 AOT 程序集名（留空则读资源目录里的 aot_manifest）。\n" +
                 "只在排查问题、或临时强制补某个程序集时用。填了就以这里为准。")]
        [SerializeField] private string[] _aotAssemblyOverride = Array.Empty<string>();

        [Header("生命周期")]
        [Tooltip("挂上后立刻开始。取消勾选则由启动流程显式调 StartHotUpdate。")]
        [SerializeField] private bool _runOnAwake = true;

        [Tooltip("是否跨场景保留。热更入口应当常驻。")]
        [SerializeField] private bool _dontDestroyOnLoad = true;

        [Tooltip("打出逐段详细日志。排查热更链路时打开。")]
        [SerializeField] private bool _verbose = true;

        // --------------------------------------------------------------------
        //  对外状态（业务与体检都读这些）
        // --------------------------------------------------------------------

        /// <summary>当前实例。场景里没有 HotUpdateBootstrap 时为 null。</summary>
        public static HotUpdateBootstrap Instance { get; private set; }

        /// <summary>流程是否已经结束（无论成败）。</summary>
        public static bool IsFinished { get; private set; }

        /// <summary>热更入口是否已经可用。</summary>
        public static bool IsReady => HotUpdateLoader.Current != null;

        /// <summary>
        /// 当前阶段的可读名字。卡住时看这个直接知道停在哪一段。
        /// </summary>
        public static string Stage { get; private set; } = "未开始";

        /// <summary>最近一次失败原因。成功时为 null。</summary>
        public static string LastError { get; private set; }

        /// <summary>最近一次 AOT 补元数据的结果（未跑时为 null）。</summary>
        public static AOTMetadataReport AotReport { get; private set; }

        /// <summary>实际命中的热更 DLL location（解析成功后才有值）。</summary>
        public static string ResolvedDllLocation { get; private set; }

        /// <summary>已 Tick 的帧数。用来确认帧驱动真的接上了。</summary>
        public static int TickCount { get; private set; }

        /// <summary>热更入口实例（等价于 <c>HotUpdateLoader.Current</c>）。</summary>
        public static IHotUpdateEntry Entry => HotUpdateLoader.Current;

        /// <summary>热更入口自报的版本 / 标记。</summary>
        public static string EntryVersion =>
            HotUpdateLoader.Current != null ? HotUpdateLoader.Current.Version : "(未加载)";

        private static UniTaskCompletionSource<bool> _readySource;

        /// <summary>
        /// 复位静态状态。
        /// </summary>
        /// <remarks>
        /// ⚠ 关掉「域重载」的编辑器下静态字段会跨 Play 会话残留，
        ///   第二次进 Play 会拿到上一次的 Entry。详见 UIBootstrap.ResetStatics 的说明。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            IsFinished = false;
            Stage = "未开始";
            LastError = null;
            AotReport = null;
            ResolvedDllLocation = null;
            TickCount = 0;
            _readySource = null;
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning(
                    "[热更] 检测到第二个 HotUpdateBootstrap，已销毁重复实例。请确认场景里只放了一个。");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            _readySource = new UniTaskCompletionSource<bool>();
            IsFinished = false;
            LastError = null;

            if (_runOnAwake)
            {
                RunAsync().Forget();
            }
        }

        /// <summary>
        /// 显式启动。仅当 <see cref="_runOnAwake"/> 关掉时才需要调。
        /// </summary>
        public void StartHotUpdate()
        {
            if (IsFinished)
            {
                Debug.LogWarning("[热更] StartHotUpdate 被调了两次，第二次忽略。");
                return;
            }

            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            var ct = this.GetCancellationTokenOnDestroy();
            bool ok = false;

            try
            {
                ok = await RunPipelineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // 退出时正常路径：对象销毁导致令牌取消。不算失败。
                SetStage("已取消（对象销毁）");
                return;
            }
            catch (Exception ex)
            {
                LastError = $"{Stage} 段抛出未捕获异常：{ex.GetType().Name}: {ex.Message}";
                Debug.LogException(ex);
                ok = false;
            }

            Finish(ok);
        }

        /// <summary>
        /// 五段流程。返回是否全部成功。
        /// </summary>
        private async UniTask<bool> RunPipelineAsync(CancellationToken ct)
        {
            // ---------------- ① 等资源系统就绪 ----------------
            // ⚠ 这里用 ResourceHub 而不是 ResourceBootstrap：
            //   ResourceBootstrap 在**后端程序集**（WanXiang.ResourceSystem.YooAsset）里，
            //   而本文件在 WanXiang.Runtime —— 框架层刻意不引用后端，
            //   直接写 ResourceBootstrap 会编译不过（CS0103）。
            //   ResourceHub 是抽象层的注册点，后端往那里注册，方向是单向的。
            SetStage("① 等待资源系统就绪");
            bool resourceReady = await ResourceHub.WaitReadyAsync(ct);
            if (!resourceReady)
            {
                LastError = "① 资源系统未就绪，热更链路无法继续。\n"
                          + "  先看资源那边的报告：ResourceBootstrap 的初始化是不是失败了"
                          + "（控制台搜「[资源]」）。热更 DLL 是当资源下发的，资源不通就取不到字节。";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            var resource = ResourceHub.Current;
            Log($"① 资源就绪（包「{resource.PackageName}」，模式 {resource.PlayMode}，"
                + $"版本 {resource.PackageVersion}）。");

            // ---------------- ② 补 AOT 泛型元数据 ----------------
            if (_loadAotMetadata)
            {
                SetStage("② 补 AOT 泛型元数据");

                // 名单来源优先级：Inspector 手工覆盖 > 资源目录里的清单文件。
                List<string> names;
                if (_aotAssemblyOverride != null && _aotAssemblyOverride.Length > 0)
                {
                    names = new List<string>(_aotAssemblyOverride);
                    Log($"② 用 Inspector 手工清单，{names.Count} 个程序集。");
                }
                else
                {
                    names = await AOTMetadataLoader.ReadManifestAsync(resource, ct, Log);
                }

                if (names == null)
                {
                    // 清单文件不存在。这**不是**致命错误：
                    // 要么还没发布过热更产物，要么当前热更代码确实不需要补元数据。
                    // 但要明说"没验证过"，别让它看起来像通过了。
                    AotReport = null;
                    Log("② 没找到 AOT 元数据清单，跳过补元数据。"
                        + "（若热更代码用到「泛型 + 值类型实参」，真机上会崩；"
                        + "先跑「万相/热更/发布热更产物」生成清单与 DLL。）");
                }
                else
                {
                    AotReport = await AOTMetadataLoader.LoadAsync(resource, names, ct, Log);

                    // ⚠ 这里刻意**不**因为补元数据失败就中断整个流程。
                    //   理由：补元数据是"能补多少补多少"，缺一个程序集只影响
                    //   用到它的那部分泛型；而直接中断会让热更层完全起不来，
                    //   连"哪几个补上了"都看不到。
                    //   错误已经完整记在 AotReport 里，报告一看就知道。
                    if (!AotReport.IsOk)
                    {
                        Log("② 补元数据有失败项（已记录，不中断流程）。详见报告。");
                    }
                }
            }
            else
            {
                Log("② 已关闭补元数据（Inspector 关掉了 _loadAotMetadata）。");
            }

            // ---------------- ③ 取热更 DLL 字节 ----------------
            SetStage("③ 取热更 DLL 字节");

            string[] dllCandidates = HotUpdateLocations.CandidatesForHotUpdateDll(_hotUpdateAssembly);
            if (!HotUpdateLocations.TryResolve(resource, dllCandidates,
                    out string dllLocation, out string dllTried))
            {
                LastError = $"③ 资源系统里找不到热更 DLL「{_hotUpdateAssembly}」。试过的 location：\n"
                          + dllTried
                          + "\n  排查顺序：\n"
                          + "    1) 跑「万相/热更/发布热更产物」—— 它会把 DLL 复制到资源目录；\n"
                          + "    2) 跑「万相/资源/初始化 YooAsset 收集器配置」—— 让收集器把 HotUpdate 目录收进去；\n"
                          + "    3) 重跑一次模拟构建（编辑器模式每次进 Play 都会重建，通常自动就好）。";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            ResolvedDllLocation = dllLocation;
            Log($"③ 命中热更 DLL location：{dllLocation}");

            byte[] dllBytes;
            try
            {
                dllBytes = await resource.LoadBytesAsync(dllLocation, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"③ 读取热更 DLL 字节异常：{ex.GetType().Name}: {ex.Message}";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            if (dllBytes == null || dllBytes.Length == 0)
            {
                LastError = $"③ 热更 DLL「{dllLocation}」字节为空。"
                          + "多半是发布时复制失败，或被中间层改写过。重新发布一次。";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            Log($"③ 取到 {dllBytes.Length / 1024} KB 字节。");

            // ---------------- ④ 加载热更程序集 ----------------
            SetStage("④ 加载热更程序集");
            try
            {
                HotUpdateLoader.Load(dllBytes, _entryTypeName);
            }
            catch (Exception ex)
            {
                LastError = $"④ 加载热更程序集失败：{ex.Message}\n"
                          + "  这一类错误通常自带明确原因（找不到入口类型 / 没实现接口 /"
                          + " 没有公开无参构造），照它说的改就行。";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            Log($"④ 热更程序集已加载，版本标记 {EntryVersion}。");

            // ---------------- ⑤ 初始化热更入口 ----------------
            SetStage("⑤ 初始化热更入口");
            try
            {
                Entry.Initialize();
            }
            catch (Exception ex)
            {
                LastError = $"⑤ 热更入口 Initialize() 抛异常：{ex.GetType().Name}: {ex.Message}\n"
                          + "  ⚠ 此时程序集已经加载成功，说明热更链路本身是通的，"
                          + "问题在热更代码自己的初始化逻辑里。";
                Debug.LogError("[热更] " + LastError);
                return false;
            }

            SetStage("完成");
            Log($"✅ 热更链路全程通过。运行标记 = {EntryVersion}，"
                + $"打包方式 {(Application.isEditor ? "编辑器（Mono）" : "玩家包（IL2CPP）")}。");
            if (Application.isEditor)
            {
                Log("ℹ 注意：编辑器跑的是 Mono，上面「补元数据」一项是空跑，"
                    + "**不能**作为真机结论。真机复验要出一次包。");
            }

            return true;
        }

        private void Update()
        {
            var entry = HotUpdateLoader.Current;
            if (entry == null)
            {
                return;
            }

            TickCount++;
            try
            {
                entry.Tick(Time.deltaTime);
            }
            catch (Exception ex)
            {
                // Tick 每帧都跑，异常若每帧打会把控制台刷爆。
                // 只报第一次，之后静默 —— 但状态要留着，别让问题看起来消失了。
                if (LastError == null)
                {
                    LastError = $"热更入口 Tick() 抛异常（后续同类异常不再重复打印）："
                              + $"{ex.GetType().Name}: {ex.Message}";
                    Debug.LogError("[热更] " + LastError);
                }
            }
        }

        private void Finish(bool ok)
        {
            IsFinished = true;
            if (ok)
            {
                LastError = null;
            }

            _readySource?.TrySetResult(ok);
        }

        /// <summary>
        /// 等热更链路跑完。依赖热更层的逻辑**必须** await 它。
        /// </summary>
        /// <returns>
        /// 热更入口是否可用。返回 false 时不要继续往下走 ——
        /// 但要区分「失败」与「连 Bootstrap 都没有」（见日志）。
        /// </returns>
        public static async UniTask<bool> WaitReadyAsync(CancellationToken ct = default)
        {
            if (IsFinished)
            {
                return IsReady;
            }

            if (_readySource == null)
            {
                Debug.LogError(
                    "[热更] 调用了 HotUpdateBootstrap.WaitReadyAsync，但场景里没有 HotUpdateBootstrap。\n"
                    + "请在启动场景加一个空物体并挂上本组件。"
                    + "（若这个工程暂时不需要热更，就别调这个接口，"
                    + "而不是让调用方去处理 null。）");
                return false;
            }

            return await _readySource.Task.AttachExternalCancellation(ct);
        }

        /// <summary>
        /// 一行式状态摘要。给体检脚本与诊断通道共用，避免两处各写一份。
        /// </summary>
        public static string DescribeState()
        {
            return $"IsFinished={IsFinished}，IsReady={IsReady}，阶段={Stage}，"
                 + $"标记={EntryVersion}，Tick={TickCount}，"
                 + $"DLL location={(ResolvedDllLocation ?? "(未解析)")}，"
                 + $"LastError={(LastError == null ? "(无)" : LastError.Replace("\n", " "))}";
        }

        private void SetStage(string stage)
        {
            Stage = stage;
            Log(stage);
        }

        private void Log(string message)
        {
            if (!_verbose)
            {
                return;
            }

            Debug.Log("[热更] " + message);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;

            // 断开热更入口的引用。注意 Assembly.Load(byte[]) 载入的程序集
            // 在 Mono/IL2CPP 下**不能真正卸载**，这里只是让后续流程不再拿到它。
            // 真正的热重载要依赖 HybridCLR 的卸载能力（见 HotUpdateLoader.Clear 的说明）。
            var entry = HotUpdateLoader.Current;
            if (entry != null)
            {
                try
                {
                    entry.Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[热更] 入口 Shutdown() 抛异常（退出路径，忽略）：{ex.Message}");
                }

                HotUpdateLoader.Clear();
            }

            IsFinished = false;
            _readySource = null;
        }
    }
}
