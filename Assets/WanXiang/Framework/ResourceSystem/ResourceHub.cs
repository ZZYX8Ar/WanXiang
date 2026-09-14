// ============================================================================
//  万相 · 资源服务注册点（AOT / 抽象层侧）
//  ---------------------------------------------------------------------------
//  作用：让「需要资源的代码」与「具体是哪个资源后端」解耦。
//
//  ⚠ 为什么必须有这一层：
//
//    `WanXiang.Runtime`（抽象层，里面是 UI / 输入 / 热更这些框架代码）
//    **刻意不引用** `WanXiang.ResourceSystem.YooAsset`（后端）。
//    这是本工程的硬边界：业务与框架只认 `IResourceService`，
//    换掉 YooAsset 时不该动框架一行代码。
//
//    代价是：框架代码想"等资源就绪"，却没处去拿那个服务实例 ——
//    `ResourceBootstrap` 这个类型在**后端程序集**里，
//    框架程序集看不见它（看不见就是编译错误 CS0103，不是运行时才炸）。
//
//    这就是本类存在的理由：**后端往抽象层注册，抽象层对外提供查询**。
//    方向是单向的（后端 → 抽象层），抽象层不认识任何后端类型。
//
//    ★ 这个模式在本工程已经有先例：`UIBootstrap.RegisterPanelLoaderFactory`。
//      那里也是"后端把实现塞给抽象层"，本类只是把它用在资源服务上。
//      这不是新发明，是把已有的做法用在该用的地方。
//
//  ---------------------------------------------------------------------------
//  ⚠ 谁负责调什么（顺序不能错）
//
//      ResourceBootstrap.Awake    → ResourceHub.Register(service)
//                                    ★ 必须在开始初始化**之前**
//                                    （否则"刚注册但还没开始初始化"这段空窗期
//                                      里来的 WaitReadyAsync 会拿不到 source）
//      ResourceBootstrap.Finish   → ResourceHub.ReportFinished(ok)
//                                    ★ 无论成功失败都要调，否则等的人永远等下去
//      ResourceBootstrap.OnDestroy→ ResourceHub.Clear()
//
//  ---------------------------------------------------------------------------
//  ⚠ 关于「等就绪」的一个常见误解
//
//    `IsReady` 与 `IsFinished` 是两件事：
//      IsFinished  —— 初始化**结束了**（可能失败）
//      IsReady     —— 初始化**成功了**，服务可用
//    只判 IsFinished 就往下走，会在初始化失败时拿到一个"存在但不可用"的服务，
//    然后在很远的地方报一个看不懂的空引用。
//    WaitReadyAsync 返回的就是 IsReady 的语义，直接用它的返回值。
// ============================================================================

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Framework.ResourceSystem
{
    /// <summary>
    /// 资源服务的全局注册点。后端注册，框架与业务查询。
    /// </summary>
    public static class ResourceHub
    {
        /// <summary>
        /// 当前资源服务。未注册时为 null。
        /// </summary>
        /// <remarks>
        /// ⚠ 拿到的服务**可能还没初始化完**（Awake 里就注册了，但初始化是异步的）。
        ///   要"确保可用"就 await <see cref="WaitReadyAsync"/>，别只看非空。
        /// </remarks>
        public static IResourceService Current { get; private set; }

        /// <summary>初始化是否已结束（无论成败）。</summary>
        public static bool IsFinished { get; private set; }

        /// <summary>服务是否存在且已就绪。</summary>
        public static bool IsReady => Current != null && Current.IsReady;

        private static UniTaskCompletionSource<bool> _readySource;

        /// <summary>
        /// 注册服务。由后端在开始初始化前调用。
        /// </summary>
        public static void Register(IResourceService service)
        {
            Current = service;
            IsFinished = false;

            // ⚠ 每次注册都换一个新的 source。
            //   复用旧的会出问题：旧 source 已经完成过，
            //   第二次注册后 WaitReadyAsync 会立刻返回上一次的结果。
            _readySource = new UniTaskCompletionSource<bool>();
        }

        /// <summary>
        /// 报告初始化结束。**成功失败都要调。**
        /// </summary>
        /// <param name="ok">初始化是否成功。</param>
        public static void ReportFinished(bool ok)
        {
            IsFinished = true;

            if (_readySource == null)
            {
                // 没注册就报告结束 —— 说明后端的调用顺序错了。
                // 不静默吞掉：这种顺序错误会让等的人拿到"永远等不到"的结果，
                // 而那是最难查的一类问题。
                Debug.LogError(
                    "[资源] ResourceHub.ReportFinished 被调用，但还没有 Register 过。\n"
                    + "  后端的正确顺序：先 Register(service)，初始化结束后再 ReportFinished(ok)。");
                return;
            }

            _readySource.TrySetResult(ok);
        }

        /// <summary>
        /// 清空注册。由后端在销毁时调用。
        /// </summary>
        public static void Clear()
        {
            Current = null;
            IsFinished = false;
            _readySource = null;
        }

        /// <summary>
        /// 等资源服务就绪。
        /// </summary>
        /// <returns>是否就绪。返回 false 时不要继续往下走。</returns>
        public static async UniTask<bool> WaitReadyAsync(CancellationToken ct = default)
        {
            if (IsFinished)
            {
                // 已经结束过：直接给结论，不再等。
                return IsReady;
            }

            if (_readySource == null)
            {
                // 没有任何后端注册过。区分两种可能，因为解法完全不同。
                Debug.LogError(
                    "[资源] 调用了 ResourceHub.WaitReadyAsync，但没有任何资源后端注册过。\n"
                    + "  ① 场景里没有 ResourceBootstrap（它是 YooAsset 后端的注册者）"
                    + "—— 在启动场景加一个空物体挂上它；\n"
                    + "  ② 或者调用的时机早于任何 Awake（例如在别的脚本的 Awake 里就 await）"
                    + "—— 挪到 Start 之后，或先 await 一帧。");
                return false;
            }

            return await _readySource.Task.AttachExternalCancellation(ct);
        }

        /// <summary>
        /// 复位静态状态。
        /// </summary>
        /// <remarks>
        /// ⚠ 关掉「域重载」的编辑器下，静态字段会跨 Play 会话残留 ——
        ///   残留的 Current 指向一个**已 Dispose** 的服务，
        ///   第二次进 Play 时拿到它会在很后面报一个看不懂的错。
        ///   细节见 UIBootstrap.ResetStatics 的说明。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            IsFinished = false;
            _readySource = null;
        }
    }
}
