// ============================================================================
//  万相 · 资源服务接口
//  ---------------------------------------------------------------------------
//  核心原则：业务代码不直接引用 YooAsset（或任何具体资源系统），
//  只通过本接口拿资源。
//
//  这样做的收益：
//    · 换资源方案（YooAsset → 别的）时，业务层零改动；
//    · 单元测试可以塞一个"从 Resources 读"的假实现，不用起 AssetBundle；
//    · 编辑器模拟 / 单机 / 联机三种模式对业务层完全透明。
//
//  ⚠ 与 IInputService 一样，本接口**不继承 QFramework 的 IUtility**。
//    理由见 Framework/Inputs/IInputService.cs 头部注释。
//
//  ⚠ 接口里刻意不出现 YooAsset 的 Handle / Operation 类型。
//    一旦暴露，业务层就会开始 `handle.Release()`、自己 await operation，
//    引用计数与生命周期纪律会当场瓦解。
//    需要释放？用 ReleaseAsset(location) / ReleaseInstance(instance)，
//    引用计数由实现统一维护。
//
//  ⚠ 关于「同步加载」：
//    LoadAssetSync / LoadBytesSync 只应该在**启动阶段**用
//    （典型场景：HybridCLR 加载热更 DLL —— 那时还没有任何异步上下文）。
//    运行期用同步加载会在主线程阻塞 IO，表现为明显卡顿。
//    诊断通道会统计调用次数，超过阈值时告警。
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Framework.ResourceSystem
{
    /// <summary>
    /// 资源服务。负责资源系统的初始化、加载、实例化、释放与热更更新。
    /// </summary>
    public interface IResourceService : IDisposable
    {
        /// <summary>是否已初始化完成（清单已就绪、可以加载资源）。</summary>
        bool IsReady { get; }

        /// <summary>当前运行模式。</summary>
        ResourcePlayMode PlayMode { get; }

        /// <summary>当前资源包名。</summary>
        string PackageName { get; }

        /// <summary>
        /// 当前生效的资源版本号。编辑器模拟模式与离线模式通常返回 "Simulate" / "1.0"。
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// 初始化资源系统：创建包裹、装配文件系统、载入清单。
        /// </summary>
        /// <param name="options">初始化参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否成功。失败原因通过 <see cref="LastError"/> 取。</returns>
        /// <remarks>
        /// ⚠ 重复调用是**安全的空操作**（直接返回上次结果），
        /// 因为启动流程、编辑器工具、测试都可能尝试初始化。
        /// 但**并发调用不安全** —— 实现内部没有加锁，
        /// 请在启动流程里只调一次（典型位置：ResourceBootstrap.Awake）。
        /// </remarks>
        UniTask<bool> InitializeAsync(ResourceInitOptions options, CancellationToken ct = default);

        /// <summary>最近一次失败的原因。没有失败过则为 null。</summary>
        string LastError { get; }

        /// <summary>
        /// 当前正卡在哪一步。只用于诊断「没有报错但也没结果」的挂起。
        /// </summary>
        /// <remarks>
        /// ⚠ 资源系统挂起时既不报错也不打日志，从外面看就是"什么都没发生"。
        ///   加载流程卡住时，第一件该看的就是本属性。
        /// </remarks>
        string LastStage { get; }

        // ---------------------------------------------------------------- 加载

        /// <summary>
        /// 异步加载资源。同一 location 重复调用会命中缓存并累加引用计数。
        /// </summary>
        /// <param name="location">资源地址（YooAsset 的 location，通常是含扩展名的工程路径或可寻址名）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>资源对象；失败或 key 无效时返回 null（并写 LastError）。</returns>
        /// <remarks>
        /// ⚠ 拿到对象后**必须**在不再使用时调 <see cref="ReleaseAsset"/>，
        /// 否则引用计数永远归不了零，资源永远卸不掉。
        /// 这是资源系统最常见的泄漏来源，代码审查时要盯住。
        /// </remarks>
        UniTask<T> LoadAssetAsync<T>(string location, CancellationToken ct = default)
            where T : UnityEngine.Object;

        /// <summary>
        /// 同步加载资源。**仅限启动阶段**，见接口头部说明。
        /// </summary>
        T LoadAssetSync<T>(string location) where T : UnityEngine.Object;

        /// <summary>
        /// 释放一次资源引用（引用计数 -1，归零后按配置决定是否卸载 Bundle）。
        /// 未加载过的 location 调用本方法是安全空操作。
        /// </summary>
        void ReleaseAsset(string location);

        // -------------------------------------------------------------- 实例化

        /// <summary>
        /// 加载并实例化一个 Prefab。
        /// </summary>
        /// <param name="location">Prefab 的资源地址。</param>
        /// <param name="parent">父节点。null 表示放在场景根。</param>
        /// <param name="ct">取消令牌。</param>
        /// <remarks>
        /// 走本方法而不是「LoadAssetAsync 之后自己 Instantiate」，
        /// 是因为资源系统需要知道这个实例是从哪个 Bundle 里出来的，
        /// 才能在清理时正确判断"还有没有活着的实例"。
        /// 自己 Instantiate 出来的实例，资源系统看不见。
        /// </remarks>
        UniTask<GameObject> InstantiateAsync(string location, Transform parent = null,
            CancellationToken ct = default);

        /// <summary>销毁实例并释放对应的一次资源引用。</summary>
        void ReleaseInstance(GameObject instance);

        // -------------------------------------------------------- 文本 / 字节

        /// <summary>
        /// 读取文本文件资源（配置文件、Lua/JSON 等）。
        /// </summary>
        UniTask<string> LoadTextAsync(string location, CancellationToken ct = default);

        /// <summary>
        /// 异步读取二进制资源。热更 DLL 走这条路。
        /// </summary>
        UniTask<byte[]> LoadBytesAsync(string location, CancellationToken ct = default);

        /// <summary>
        /// 同步读取二进制资源。**仅限启动阶段**（HybridCLR 加载热更 DLL）。
        /// </summary>
        byte[] LoadBytesSync(string location);

        // ---------------------------------------------------------------- 更新

        /// <summary>
        /// 更新资源包：请求版本 → 更新清单 → 下载缺失文件。
        /// </summary>
        /// <param name="progress">下载进度回调（0~1）。可空。</param>
        /// <param name="ct">取消令牌。</param>
        /// <remarks>
        /// ⚠ 编辑器模拟模式与离线模式下本方法**必然成功且不下载任何东西**
        /// （没有任何远端可以连）。调用方不需要为这两种模式写分支。
        /// </remarks>
        UniTask<ResourceUpdateReport> UpdatePackageAsync(IProgress<float> progress = null,
            CancellationToken ct = default);

        // ---------------------------------------------------------------- 诊断

        /// <summary>某个 location 是否在当前清单里。用于启动时校验配置表地址写没写错。</summary>
        bool CheckLocationValid(string location);

        /// <summary>当前有多少个 location 处于"已加载"状态（引用计数 &gt; 0）。泄漏排查用。</summary>
        int LoadedLocationCount { get; }

        /// <summary>累计同步加载调用次数。启动后应当停止增长。</summary>
        int SyncLoadCallCount { get; }

        /// <summary>触发一次"卸载无引用资源"。切场景/回主菜单时调。</summary>
        void UnloadUnused();

        /// <summary>
        /// 打印当前所有已加载 location 及其引用计数到控制台。泄漏排查用。
        /// </summary>
        void DumpLoadedLocations();
    }
}
