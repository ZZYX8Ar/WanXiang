// ============================================================================
//  万相 · 资源系统 · YooAsset ⇄ UniTask 桥接
//  ---------------------------------------------------------------------------
//  YooAsset 2.3.19 **没有任何 UniTask 支持** —— 官方 Runtime 里搜不到一个
//  "UniTask" 字面量。它只提供两种等待方式：
//    · HandleBase / AsyncOperationBase 实现了 IEnumerator（给协程用）
//    · 两者都有 Completed 事件（给事件驱动用）
//  所以「await 一个加载句柄」这件事必须自己搭。本文件就是那座桥。
//
//  ⚠⚠ 为什么不用 YooAsset 自带的官方样例（Samples~/UniTask Sample）？
//    它放在 Samples~ 下，要先用 Package Manager 导入到 Assets/ 才会编译。
//    而且它依赖两样我们不该引入的东西：
//      ① `using static Cysharp.Threading.Tasks.Internal.Error;`
//         —— Error 是 **internal** 的。为了拿到它，样例额外拖了一个
//            `UniTaskRef.asmref` + `_InternalVisibleTo.cs`，去给 UniTask 程序集
//            塞 InternalsVisibleTo。等于为了让我们的程序集看见 UniTask 内部，
//            去改动 UniTask 自己的程序集边界。
//      ② 它把自己的 asmdef 命名为 UniTask.YooAsset，将来真去导入那个样例时
//         会与它**程序集重名冲突**。
//    本文件的实现与官方逐行对齐，但**不碰任何 internal 类型**。
//    （已核实 UniTask 2.5.11：IPlayerLoopItem / PlayerLoopHelper / TaskPool<T> /
//      ITaskPoolNode<T> / TaskTracker / UniTaskCompletionSourceCore<T> 全是 public，
//      只有 Internal.Error 是 internal —— 本文件没有用它。）
//
//  ⚠⚠ 为什么订阅事件要用"逐个具名类型 switch"而不是拿一个 Action<HandleBase>？
//    C# 里 Action<in T> 是逆变的，Action<HandleBase> 能隐式转成 Action<AssetHandle>，
//    Mono 下没问题。但 **IL2CPP 下这个委托转换会炸**：
//      ArgumentException: Incompatible Delegate Types.
//        First is System.Action`1[[YooAsset.AssetHandle, YooAsset, ...]]
//        second is System.Action`1[[YooAsset.HandleBase, YooAsset, ...]]
//    这正是 YooAsset 官方样例里那段 UNITY_2020_BUG 注释描述的同一个问题
//    （官方在 Unity 2020.3.36 上复现的）。而它的条件写的是
//      `#if UNITY_2020_1_OR_NEWER && ! UNITY_2021`
//    —— Unity 根本不定义 `UNITY_2021` 这个符号（只有 `UNITY_2021_1_OR_NEWER`），
//    所以 `! UNITY_2021` 恒为真，2020.1 以上**永远走 switch 分支**。
//    结论：本工程（2022.3 + IL2CPP）必须照 switch 写，不要"优化"掉。
//
//  ⚠⚠ 生命周期上三个必须同时满足的约束（任何一条破了都是难查的 bug）：
//
//    ① **先订阅、再查 IsDone**。
//       反过来的话，句柄恰好在"查到未完成"与"订阅"之间完成，
//       Completed 已经发过，我们永远等不到 —— 表现为随机卡死。
//       订阅在前最坏只是收到一次多余回调，而回调里有幂等保护。
//
//    ② **不要从事件回调里归还对象池**。
//       UniTaskCompletionSourceCore.Reset() 会把 version +1，
//       而 await 侧的 GetResult(token) 会校验 version，
//       一旦在 GetResult 之前 Reset，就会抛
//       "Token version is not matched, can not await twice..."。
//       （已核实 UniTask 2.5.11 源码：Reset() 里 `version += 1`。
//        这也是官方样例把 TryReturn 放在下一帧 MoveNext 里的原因。）
//
//    ③ **每帧的 MoveNext 必须始终挂上**，不能只在"需要进度"时才挂。
//       归还对象池的动作就发生在 MoveNext 里；如果没挂，
//       加载完成后对象永远回不了池 —— 官方样例就有这个毛病
//       （它只在 progress != null 时才 AddAction），
//       结果是每次加载都新建一个 source 且 TaskTracker 里的记录永不释放。
//       本文件改为**无条件挂 MoveNext**，代价只是每帧一次空判断。
// ============================================================================

//  ⚠⚠ 命名空间为什么叫 ...ResourceSystem.Backend？
//    这条是被编译错误打出来才记住的，两个方向都会翻车：
//
//    【方向一】命名空间末尾别跟一个「别人家的命名空间名」
//      本文件早先叫 `WanXiang.Framework.Resources.Backend`，
//      结果限定名 `YooAsset.AssetHandle` 里的最左标识符 `YooAsset` 向外层查找时，
//      先在 `WanXiang.Framework.Resources` 里找到了一个名叫 `YooAsset` 的成员
//      —— 那是我自己的命名空间 —— 于是整个限定名解析成
//      "我命名空间里的 AssetHandle"，报 CS0234。
//
//    【方向二 · 这个更狠】命名空间末尾别跟一个 UnityEngine 的类型名
//      第一版整个命名空间叫 `WanXiang.Framework.Resources`。
//      于是框架里**已有的**代码全部编译不过：
//          InputBootstrap.cs(121,20): error CS0234:
//            The type or namespace name 'Load' does not exist
//            in the namespace 'WanXiang.Framework.Resources'
//          ResourcesPanelLoader.cs(78,31): error CS0234: ... 'LoadAsync' ...
//      原因是 `UnityEngine.Resources.Load(...)` 里的 `Resources`
//      先在**外层命名空间** `WanXiang.Framework` 里找到了同名子命名空间，
//      于是 `using UnityEngine;` 根本没机会生效。
//      注意：这跟"本文件用不用 Resources 无关"——
//      它把**同一个命名空间树下的所有文件**都污染了。
//
//    结论：命名空间取名的唯一硬约束是「末段不要等于任何会被无限定引用的类型名」。
//    叫 `ResourceSystem` 而不是 `Resources`，就同时躲开了上面两个方向。
//
//  ⚠ 另外：目录也**不能**叫 `Resources`。
//    Unity 里任何名为 Resources 的目录都是特殊目录 ——
//    里面的资产会被无条件打进包体、且可以用 Resources.Load 按相对路径取到。
//    本工程的资源目录因此叫 `Framework/ResourceSystem/`。

using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace WanXiang.Framework.ResourceSystem.Backend
{
    /// <summary>
    /// 把 YooAsset 的加载句柄 / 异步操作接成可 await 的 UniTask。
    /// </summary>
    internal static class YooAssetAwaiter
    {
        /// <summary>
        /// 诊断开关：打开后把「开始等待 / 完成 / 每 60 帧仍在等」打到控制台。
        /// </summary>
        /// <remarks>
        /// ⚠ 这个开关的存在本身就是一条教训：资源加载卡住时，
        ///   「到底是我这边的 await 没被唤醒，还是 YooAsset 那边操作没跑完」
        ///   从外面完全看不出来 —— 表现都是"没有任何日志，也没报错"。
        ///   有了它，一次 Play 就能把这件事问清楚。
        ///   默认关闭；要开就把 <see cref="Backend.YooAssetResourceService.TraceAwaiter"/> 设 true。
        /// </remarks>
        public static bool Trace;

        /// <summary>等一个加载句柄（AssetHandle / RawFileHandle / SceneHandle / …）。</summary>
        /// <remarks>
        /// 句柄已失效或已完成时立即返回，不抛异常。
        /// 失败（Status == Failed）时**抛异常** —— 由上层
        /// <c>YooAssetResourceService</c> 捕获并转成"返回 null + 记 LastError"，
        /// 这样底层信息不丢，上层调用点又不用到处写 try。
        /// </remarks>
        public static UniTask WaitAsync(this HandleBase handle, IProgress<float> progress = null)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (!handle.IsValid) return UniTask.CompletedTask;
            if (handle.IsDone) return UniTask.CompletedTask;

            return new UniTask(HandleAwaiterSource.Create(handle, progress, out var token), token);
        }

        /// <summary>等一个异步操作（初始化 / 请求版本 / 更新清单 / 下载 …）。</summary>
        public static UniTask WaitAsync(this AsyncOperationBase operation, IProgress<float> progress = null)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.IsDone) return UniTask.CompletedTask;

            return new UniTask(OperationAwaiterSource.Create(operation, progress, out var token), token);
        }

        // ====================================================================
        //  公共基类：结果拍板 + 进度上报 + 池归还
        // ====================================================================

        internal abstract class AwaiterSourceBase : IUniTaskSource, IPlayerLoopItem
        {
            protected UniTaskCompletionSourceCore<AsyncUnit> Core;

            /// <summary>结果是否已拍板（成功或失败）。幂等保护，防止重复回调重复完成。</summary>
            protected bool Settled;

            protected IProgress<float> ProgressReporter;

            /// <summary>追踪用：每 60 帧打一次"还在等"。</summary>
            private int _frameCounter;

            /// <summary>被等待对象的类型名，只用于追踪输出。</summary>
            protected abstract string TargetName { get; }

            /// <summary>底层对象是否还有效。</summary>
            protected abstract bool NativeValid { get; }

            /// <summary>底层对象是否已完成。</summary>
            protected abstract bool NativeDone { get; }

            /// <summary>底层对象的状态。</summary>
            protected abstract EOperationStatus NativeStatus { get; }

            /// <summary>底层对象的错误信息。</summary>
            protected abstract string NativeError { get; }

            /// <summary>底层对象的进度。</summary>
            protected abstract float NativeProgress { get; }

            /// <summary>撤掉事件订阅并把字段清干净。归还池之前调用。</summary>
            protected abstract void Detach();

            /// <summary>借出时复位。</summary>
            protected void ResetForReuse(IProgress<float> progress)
            {
                Core.Reset();
                Settled = false;
                ProgressReporter = progress;
                _frameCounter = 0;

                TaskTracker.TrackActiveTask(this, 3);

                // 始终挂上（见文件头约束 ③）。不带进度时这里什么都不做，
                // 只负责在完成后把对象收回池。
                PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, this);

                if (Trace)
                {
                    Debug.Log($"[资源桥接] 开始等待 {TargetName}");
                }
            }

            /// <summary>事件回调入口：拍板结果。</summary>
            protected void Settle()
            {
                if (Settled) return;
                Settled = true;

                if (Trace)
                {
                    Debug.Log($"[资源桥接] 完成 {TargetName}，状态={NativeStatus}，有效={NativeValid}");
                }

                if (!NativeValid)
                {
                    // 句柄在完成前被释放（典型场景：加载中切场景）。
                    // 这一条会被上层转成"加载失败"，但它是正常生命周期的一部分，
                    // 不该当成代码 bug 去报 error 级日志。
                    Core.TrySetException(new InvalidOperationException(
                        "资源对象在完成前已失效（可能发生了包裹销毁或提前 Release）。"));
                    return;
                }

                if (NativeStatus == EOperationStatus.Failed)
                {
                    Core.TrySetException(new Exception($"YooAsset 操作失败：{NativeError}"));
                }
                else
                {
                    Core.TrySetResult(AsyncUnit.Default);
                }
            }

            /// <summary>
            /// 每帧回调。**归还对象池只在这里做**（见文件头约束 ②）。
            /// </summary>
            bool IPlayerLoopItem.MoveNext()
            {
                _frameCounter++;

                if (Settled)
                {
                    // 走到这里说明 await 侧的 GetResult 已经执行完了
                    // （结果是在上一帧/本帧的事件回调里拍板的，continuation 已同步调用），
                    // 现在 Reset 才安全。
                    Detach();
                    ProgressReporter = null;
                    TaskTracker.RemoveTracking(this);
                    ReturnToPool();
                    return false;
                }

                // 兜底：万一 Completed 事件没送到（订阅前就完成了、
                // 或驱动对象被销毁），靠每帧查 IsDone 也能让 await 醒过来。
                // 主路径是事件，这里只是保险。
                if (!NativeValid || NativeDone)
                {
                    Settle();
                    Detach();
                    ProgressReporter = null;
                    TaskTracker.RemoveTracking(this);
                    ReturnToPool();
                    return false;
                }

                if (ProgressReporter != null)
                {
                    ProgressReporter.Report(NativeProgress);
                }

                if (Trace && _frameCounter % 60 == 0)
                {
                    Debug.Log($"[资源桥接] 仍在等 {TargetName}：" +
                              $"IsDone={NativeDone}，状态={NativeStatus}，进度={NativeProgress:0.00}");
                }

                return true;
            }

            /// <summary>把 this 推回各自的静态池。子类实现。</summary>
            protected abstract void ReturnToPool();

            // ---- IUniTaskSource ----
            UniTaskStatus IUniTaskSource.GetStatus(short token) => Core.GetStatus(token);
            UniTaskStatus IUniTaskSource.UnsafeGetStatus() => Core.UnsafeGetStatus();
            void IUniTaskSource.GetResult(short token) => Core.GetResult(token);
            void IUniTaskSource.OnCompleted(Action<object> continuation, object state, short token)
                => Core.OnCompleted(continuation, state, token);
        }

        // ====================================================================
        //  HandleBase（加载句柄）
        // ====================================================================

        internal sealed class HandleAwaiterSource : AwaiterSourceBase, ITaskPoolNode<HandleAwaiterSource>
        {
            private static TaskPool<HandleAwaiterSource> _pool;

            private HandleAwaiterSource _nextNode;
            private HandleBase _handle;

            /// <summary>记住具体类型的句柄，退订时要按同一个类型减回去。</summary>
            private object _typedHandle;

            // ⚠ 委托缓存成字段：订阅与退订必须用**同一个委托实例**，
            //   否则 -= 匹配不上，会在已归还的池对象上留下僵尸订阅
            //   （那会让下次借出这个对象时收到别人的回调）。
            private readonly Action<AssetHandle> _onAsset;
            private readonly Action<RawFileHandle> _onRawFile;
            private readonly Action<SceneHandle> _onScene;
            private readonly Action<SubAssetsHandle> _onSubAssets;
            private readonly Action<AllAssetsHandle> _onAllAssets;

            public ref HandleAwaiterSource NextNode => ref _nextNode;

            static HandleAwaiterSource()
            {
                TaskPool.RegisterSizeGetter(typeof(HandleAwaiterSource), () => _pool.Size);
            }

            private HandleAwaiterSource()
            {
                _onAsset = OnAssetCompleted;
                _onRawFile = OnRawFileCompleted;
                _onScene = OnSceneCompleted;
                _onSubAssets = OnSubAssetsCompleted;
                _onAllAssets = OnAllAssetsCompleted;
            }

            public static IUniTaskSource Create(HandleBase handle, IProgress<float> progress, out short token)
            {
                if (!_pool.TryPop(out var source))
                {
                    source = new HandleAwaiterSource();
                }

                source._handle = handle;
                source.ResetForReuse(progress);

                // 见文件头约束 ①：先订阅，再查 IsDone。
                source.Subscribe(handle);

                if (handle.IsDone || !handle.IsValid)
                {
                    source.Settle();
                }

                token = source.Core.Version;
                return source;
            }

            private void Subscribe(HandleBase handle)
            {
                // 见文件头「为什么必须逐个具名类型 switch」。
                // ⚠ IL2CPP 下不要"优化"成 Action<HandleBase>。
                // ⚠ 各 case 必须用**不同的**变量名：同一个 switch 里
                //   重复的模式变量名会报 CS0128。
                //
                // 整段包 try：事件的 add 访问器在句柄失效时同样会抛异常
                // （见 Detach 里的说明）。订阅失败不能让异常飞出 WaitAsync ——
                // 那会变成"加载一句简单调用突然抛异常"，而调用方毫无准备。
                try
                {
                    switch (handle)
                    {
                        case AssetHandle hAsset:
                            _typedHandle = hAsset; hAsset.Completed += _onAsset; break;
                        case RawFileHandle hRaw:
                            _typedHandle = hRaw; hRaw.Completed += _onRawFile; break;
                        case SceneHandle hScene:
                            _typedHandle = hScene; hScene.Completed += _onScene; break;
                        case SubAssetsHandle hSub:
                            _typedHandle = hSub; hSub.Completed += _onSubAssets; break;
                        case AllAssetsHandle hAll:
                            _typedHandle = hAll; hAll.Completed += _onAllAssets; break;
                        default:
                            // YooAsset 将来新增句柄类型时会落到这里。
                            // 不静默忽略 —— 静默的后果是 await 永远不返回，
                            // 排查起来毫无线索。直接失败并点名。
                            Settled = true;
                            Core.TrySetException(new NotSupportedException(
                                $"未支持的 YooAsset 句柄类型：{handle.GetType().FullName}。" +
                                "请在 YooAssetAwaiter.HandleAwaiterSource.Subscribe 里补一个 case。"));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Settled = true;
                    Core.TrySetException(ex);
                }
            }

            protected override void Detach()
            {
                // ⚠⚠ YooAsset 的 Completed 事件访问器在句柄失效时会**抛异常**：
                //      remove { if (IsValidWithWarning == false)
                //                 throw new Exception($"AssetHandle is invalid"); ... }
                //    而"句柄失效"恰恰是加载中切场景的常态。
                //    所以退订必须包 try —— 不包的话异常会从 PlayerLoop 里抛出去，
                //    表现为一串看不懂的堆栈，而且 Loader 还少收回一次对象池。
                //    退订失败本身无害：句柄都失效了，它的回调自然不会再触发。
                try
                {
                    switch (_typedHandle)
                    {
                        case AssetHandle hAsset: hAsset.Completed -= _onAsset; break;
                        case RawFileHandle hRaw: hRaw.Completed -= _onRawFile; break;
                        case SceneHandle hScene: hScene.Completed -= _onScene; break;
                        case SubAssetsHandle hSub: hSub.Completed -= _onSubAssets; break;
                        case AllAssetsHandle hAll: hAll.Completed -= _onAllAssets; break;
                    }
                }
                catch (Exception)
                {
                    // 忽略：句柄已失效，退订已无意义。
                }

                _typedHandle = null;
                _handle = null;
            }

            private void OnAssetCompleted(AssetHandle h) { Settle(); }
            private void OnRawFileCompleted(RawFileHandle h) { Settle(); }
            private void OnSceneCompleted(SceneHandle h) { Settle(); }
            private void OnSubAssetsCompleted(SubAssetsHandle h) { Settle(); }
            private void OnAllAssetsCompleted(AllAssetsHandle h) { Settle(); }

            protected override void ReturnToPool()
            {
                _pool.TryPush(this);
            }

            protected override string TargetName =>
                _handle == null ? "(句柄已释放)" : _handle.GetType().Name;

            protected override bool NativeValid => _handle != null && _handle.IsValid;
            protected override bool NativeDone => _handle == null || _handle.IsDone;
            protected override EOperationStatus NativeStatus => _handle?.Status ?? EOperationStatus.Failed;
            protected override string NativeError => _handle?.LastError ?? "句柄已失效";
            protected override float NativeProgress => _handle?.Progress ?? 0f;
        }

        // ====================================================================
        //  AsyncOperationBase（初始化 / 版本 / 清单 / 下载）
        // ====================================================================

        internal sealed class OperationAwaiterSource : AwaiterSourceBase, ITaskPoolNode<OperationAwaiterSource>
        {
            private static TaskPool<OperationAwaiterSource> _pool;

            private OperationAwaiterSource _nextNode;
            private AsyncOperationBase _operation;

            // 这个事件的签名统一是 Action<AsyncOperationBase>，
            // 不存在上面那种委托逆变问题，所以不需要 switch。
            private readonly Action<AsyncOperationBase> _onCompleted;

            public ref OperationAwaiterSource NextNode => ref _nextNode;

            static OperationAwaiterSource()
            {
                TaskPool.RegisterSizeGetter(typeof(OperationAwaiterSource), () => _pool.Size);
            }

            private OperationAwaiterSource()
            {
                _onCompleted = OnOperationCompleted;
            }

            public static IUniTaskSource Create(AsyncOperationBase operation, IProgress<float> progress, out short token)
            {
                if (!_pool.TryPop(out var source))
                {
                    source = new OperationAwaiterSource();
                }

                source._operation = operation;
                source.ResetForReuse(progress);

                // 同样是先订阅、再查 IsDone（见文件头约束 ①）。
                operation.Completed += source._onCompleted;

                if (operation.IsDone)
                {
                    source.Settle();
                }

                token = source.Core.Version;
                return source;
            }

            private void OnOperationCompleted(AsyncOperationBase op) { Settle(); }

            protected override void Detach()
            {
                if (_operation != null)
                {
                    _operation.Completed -= _onCompleted;
                }
                _operation = null;
            }

            protected override void ReturnToPool()
            {
                _pool.TryPush(this);
            }

            protected override string TargetName =>
                _operation == null ? "(操作已释放)" : _operation.GetType().Name;

            protected override bool NativeValid => _operation != null;
            protected override bool NativeDone => _operation == null || _operation.IsDone;
            protected override EOperationStatus NativeStatus => _operation?.Status ?? EOperationStatus.Failed;
            protected override string NativeError => _operation?.Error ?? "操作已失效";
            protected override float NativeProgress => _operation?.Progress ?? 0f;
        }
    }
}
