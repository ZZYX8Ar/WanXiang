// ============================================================================
//  万相 · 资源系统 · 基础定义
//  ---------------------------------------------------------------------------
//  本文件只放「纯数据」：运行模式枚举、初始化参数、更新报告。
//  刻意不出现任何 YooAsset 类型 —— 与 IInputService 同样的理由
//  （见 Framework/Inputs/IInputService.cs 头部注释）：
//    · WanXiang.Runtime 不依赖任何资源包，
//      「框架能不能单独跑起来」这件事始终是真的；
//    · 业务层写 IResourceService，不写 YooAsset，
//      将来从 YooAsset 换到别的资源方案，业务代码一行不用改。
//
//  ⚠ 三个模式的取舍（这是本文件最重要的信息，别跳过）：
//
//    EditorSimulate —— 只在编辑器下有意义。
//        直接读工程里的源文件，**不打包、不构建**，改一张图立刻生效。
//        代价：只能在编辑器里跑；启动时要做一次模拟构建。
//        ⇒ 日常开发用这个，迭代速度最快。
//
//    Offline —— 单机内置。
//        从 StreamingAssets 里读随包发布的 Bundle，不走网络。
//        适用：买断制单机、或者首包就带全部资源不打算更新。
//        ⇒ 真实出包时验证「资源确实被打进包了」用这个。
//
//    Host —— 联机热更。
//        内置 Bundle + 远端下载 + 本地缓存三层。
//        适用：需要发版后更新资源的线上项目。
//        ⇒ 正式版的目标形态，但必须有 CDN 才能跑。
//
//  ⚠ 编辑器下选 Offline / Host 会直接报错而不是静默降级：
//    因为 StreamingAssets 的内容要「构建资产包」才有，
//    不构建就切过去，玩家会拿到一个清单为空的资源系统，
//    表现为「所有资源都加载失败」—— 这种症状极难定位。
//    宁可启动时明确报错。
// ============================================================================

using System;

namespace WanXiang.Framework.ResourceSystem
{
    /// <summary>
    /// 资源运行模式。对应 YooAsset 的 EPlayMode，但不暴露那个类型。
    /// </summary>
    public enum ResourcePlayMode
    {
        /// <summary>编辑器模拟：读工程源文件，不构建。仅编辑器可用，迭代最快。</summary>
        EditorSimulate = 0,

        /// <summary>离线单机：从 StreamingAssets 读随包 Bundle，不走网络。</summary>
        Offline = 1,

        /// <summary>联机热更：内置 Bundle + 远端下载 + 本地缓存。</summary>
        Host = 2,
    }

    /// <summary>
    /// 资源系统初始化参数。
    /// </summary>
    /// <remarks>
    /// 用可写字段而不是构造参数：这类"配置对象"字段会持续增加
    /// （加密服务、解密服务、自定义文件系统……），
    /// 每加一个就改一次构造函数签名，所有调用点都要跟着改。
    /// 字段式可以让调用点只写自己关心的那几个。
    /// </remarks>
    [Serializable]
    public sealed class ResourceInitOptions
    {
        /// <summary>
        /// 资源包名。空则用 <see cref="DefaultPackageName"/>。
        /// </summary>
        /// <remarks>
        /// ⚠ 这个名字必须与 YooAsset 收集器配置里的「包裹名」完全一致。
        /// 不一致时 initializeAsync 能成功（YooAsset 允许运行时创建空包裹），
        /// 但之后所有加载都会失败，且错误信息只说"资源不存在"，
        /// 不会提示"你包名写错了"。
        /// </remarks>
        public string PackageName = DefaultPackageName;

        /// <summary>默认资源包名。</summary>
        public const string DefaultPackageName = "WanXiang";

        /// <summary>运行模式。</summary>
        public ResourcePlayMode PlayMode = ResourcePlayMode.EditorSimulate;

        /// <summary>【Host 专用】主资源服务器地址。末尾不要带斜杠，实现会自己拼。</summary>
        public string HostServer = string.Empty;

        /// <summary>【Host 专用】备用资源服务器地址。主站不可达时回退到它。</summary>
        public string FallbackHostServer = string.Empty;

        /// <summary>【Host 专用】同时下载的最大 Bundle 数。</summary>
        /// <remarks>
        /// 别贪大。移动端并发开太高会打满带宽导致每个请求都变慢，
        /// 总时长反而更长；而且内存峰值会上去。10~16 是常见甜点。
        /// </remarks>
        public int DownloadingMaxNumber = 10;

        /// <summary>【Host 专用】单个文件下载失败后的重试次数。</summary>
        public int FailedTryAgain = 3;

        /// <summary>
        /// 引用计数归零时自动卸载 Bundle。
        /// </summary>
        /// <remarks>
        /// ⚠ 默认 false（关）。
        /// 打开能省内存，但代价是「关了再开」会重新走 IO，
        /// 而且如果引用计数算错了（UI 框架、对象池、异步加载中放弃……
        /// 都是容易算错的地方），会表现成随机丢资源、贴图变白。
        /// 先用 false 把功能跑通，等资源加载日志稳定了再考虑打开。
        /// </remarks>
        public bool AutoUnloadBundleWhenUnused = false;

        /// <summary>创建一份默认配置。</summary>
        public static ResourceInitOptions CreateDefault()
        {
            return new ResourceInitOptions();
        }

        /// <summary>当前配置是否自洽。不自洽时返回原因，自洽返回 null。</summary>
        public string Validate()
        {
            if (string.IsNullOrEmpty(PackageName))
            {
                return "PackageName 为空。";
            }

            if (PlayMode == ResourcePlayMode.Host)
            {
                if (string.IsNullOrEmpty(HostServer))
                {
                    return "Host 模式必须设置 HostServer。";
                }

                // 末尾斜杠会拼出 "http://host//file"，部分 CDN 会把它当不同路径
                // 而返回 404。这里直接判错，不让它溜到运行时。
                if (HostServer.EndsWith("/", StringComparison.Ordinal))
                {
                    return "HostServer 末尾不要带斜杠。";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 一次资源更新（版本 → 清单 → 下载）的结果。
    /// </summary>
    /// <remarks>
    /// 刻意做成结构体 + 只读字段：更新流程是"要么全成、要么拿到一个明确失败原因"，
    /// 不需要中间态。用异常表达失败会让「网络断了」和「代码写错了」
    /// 混在同一个 catch 里，反而更难处理。
    /// </remarks>
    public readonly struct ResourceUpdateReport
    {
        /// <summary>整个过程是否成功。</summary>
        public readonly bool Success;

        /// <summary>最终生效的资源版本号。失败时为空。</summary>
        public readonly string PackageVersion;

        /// <summary>需要下载的文件总数（0 表示已是最新，无需下载）。</summary>
        public readonly int TotalDownloadCount;

        /// <summary>需要下载的总字节数。</summary>
        public readonly long TotalDownloadBytes;

        /// <summary>失败原因。成功时为 null。</summary>
        public readonly string Error;

        public ResourceUpdateReport(bool success, string packageVersion,
            int totalDownloadCount, long totalDownloadBytes, string error)
        {
            Success = success;
            PackageVersion = packageVersion;
            TotalDownloadCount = totalDownloadCount;
            TotalDownloadBytes = totalDownloadBytes;
            Error = error;
        }

        /// <summary>构造一个成功结果。</summary>
        public static ResourceUpdateReport Ok(string version, int count, long bytes)
        {
            return new ResourceUpdateReport(true, version, count, bytes, null);
        }

        /// <summary>构造一个失败结果。</summary>
        public static ResourceUpdateReport Fail(string error)
        {
            return new ResourceUpdateReport(false, null, 0, 0, error);
        }

        public override string ToString()
        {
            return Success
                ? $"✅ 版本 {PackageVersion}，待下载 {TotalDownloadCount} 个 / {TotalDownloadBytes} 字节"
                : $"❌ {Error}";
        }
    }
}
