// ============================================================================
//  万相 · 热更加载器（AOT 侧）
//  ---------------------------------------------------------------------------
//  职责：把一份热更程序集的字节流变成可调用的 IHotUpdateEntry。
//
//  它**不知道字节从哪来** —— 这是刻意的分层：
//    · 编辑器 / 原型期：可以从磁盘文件直接读（LoadFromFile）
//    · 正式包：由资源系统（YooAsset）把热更 DLL 当普通资源下发后传入字节
//    加载器只管「字节 → 实例」，资源从哪来是资源层的决定。
//
//  ⚠ HybridCLR 的两条硬性要求，这里都不做（由 HybridCLR 自身机制负责）：
//    1. AOT 泛型补元数据 —— 热更代码里用到「泛型方法 / 泛型类型 + 值类型实参」
//       时，AOT 侧必须存在对应实例化。HybridCLR 用 Generate/AOTGenericReferences
//       生成清单，运行期用 LoadMetadataForAOTAssemblies 补齐。
//       参考类型走共享泛型，通常不需要额外处理。
//    2. 裁剪保护 —— il2cpp 会裁掉没被静态引用的类型与函数。热更代码调用的
//       AOT 类型必须在 link.xml 里保留，否则运行时报
//       `ExecutionEngineException: method body is null`。
//       这就是本工程 link.xml 存在的原因（HybridCLR/Generate/LinkXml）。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace WanXiang.Framework.HotUpdate
{
    /// <summary>
    /// 把热更程序集字节流加载成 <see cref="IHotUpdateEntry"/>。
    /// </summary>
    public static class HotUpdateLoader
    {
        /// <summary>
        /// 热更入口类的完整类型名。与热更工程里的实现类必须一致。
        /// </summary>
        public const string DefaultEntryTypeName = "WanXiang.HotUpdate.HotUpdateEntry";

        /// <summary>
        /// 最近一次成功加载出来的热更程序集。
        /// </summary>
        /// <remarks>
        /// 必须留着这个引用。理由有两条：
        /// <list type="number">
        /// <item>热重载（先卸后装）时需要它来定位旧程序集</item>
        /// <item>调试时能直接看出版本与类型清单，不必再猜加载成功没有</item>
        /// </list>
        /// </remarks>
        public static Assembly LoadedAssembly { get; private set; }

        /// <summary>最近一次加载出来的入口实例，未加载时为 null。</summary>
        public static IHotUpdateEntry Current { get; private set; }

        /// <summary>
        /// 从字节流加载热更程序集并创建入口实例。
        /// </summary>
        /// <exception cref="ArgumentNullException">字节流为空。</exception>
        /// <exception cref="InvalidOperationException">
        /// 找不到入口类型、入口类型没实现 <see cref="IHotUpdateEntry"/>、
        /// 或缺公开无参构造函数。这三种都属于「打包配置错了」，
        /// 直接抛出并带上明确信息，比返回 null 再让调用方空引用好定位。
        /// </exception>
        public static IHotUpdateEntry Load(byte[] assemblyBytes,
            string entryTypeName = DefaultEntryTypeName)
        {
            if (assemblyBytes == null || assemblyBytes.Length == 0)
            {
                throw new ArgumentNullException(nameof(assemblyBytes),
                    "热更程序集字节流为空 —— 多半是资源没下发成功，或分组配错了。");
            }

            var assembly = Assembly.Load(assemblyBytes);
            var entry = CreateEntry(assembly, entryTypeName);

            LoadedAssembly = assembly;
            Current = entry;

            Debug.Log($"[万相·热更] 已加载 {assembly.GetName().Name}"
                      + $"（{assemblyBytes.Length / 1024} KB），入口 {entry.GetType().Name}，"
                      + $"标记 {entry.Version}");
            return entry;
        }

        /// <summary>
        /// 从磁盘文件加载。编辑器与原型期用，正式包走资源系统。
        /// </summary>
        public static IHotUpdateEntry LoadFromFile(string absolutePath,
            string entryTypeName = DefaultEntryTypeName)
        {
            if (string.IsNullOrEmpty(absolutePath))
                throw new ArgumentException("路径为空", nameof(absolutePath));

            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"找不到热更程序集：{absolutePath}\n"
                    + "编辑器下一般是还没跑 HybridCLR/Generate/HotUpdateDll。", absolutePath);
            }

            return Load(File.ReadAllBytes(absolutePath), entryTypeName);
        }

        /// <summary>
        /// 清掉引用。注意：Mono/IL2CPP 下 <c>Assembly.Load(byte[])</c> 载入的程序集
        /// **不能真正卸载**，这里只是断开引用，让后续加载走新的一份。
        /// 真正的热重载要依赖 HybridCLR 的卸载能力，届时本方法需要替换实现。
        /// </summary>
        public static void Clear()
        {
            Current = null;
            LoadedAssembly = null;
        }

        private static IHotUpdateEntry CreateEntry(Assembly assembly, string entryTypeName)
        {
            var type = assembly.GetType(entryTypeName, throwOnError: false);
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"热更程序集 {assembly.GetName().Name} 里找不到类型 {entryTypeName}。\n"
                    + "两种常见原因：① 入口类的命名空间/类名与 DefaultEntryTypeName 不一致；"
                    + "② 入口类没被编译进这份 DLL（asmdef 漏了那个目录）。");
            }

            if (!typeof(IHotUpdateEntry).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"{entryTypeName} 没有实现 IHotUpdateEntry。"
                    + "热更入口必须实现 AOT 侧定义的契约，否则 AOT 代码无法调用它 —— "
                    + "这是 HybridCLR 跨边界的强制要求。");
            }

            // Activator.CreateInstance 在 AOT 侧创建热更类型，HybridCLR 支持。
            var instance = Activator.CreateInstance(type);
            return (IHotUpdateEntry)instance;
        }
    }
}
