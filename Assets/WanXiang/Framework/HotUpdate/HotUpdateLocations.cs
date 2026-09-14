// ============================================================================
//  万相 · 热更产物在资源系统里的位置约定（AOT 侧）
//  ---------------------------------------------------------------------------
//  一处定义，三处使用：
//    · 编辑器发布工具 —— 按这里算出「把 DLL 复制到哪里」
//    · 运行期启动流程 —— 按这里算出「从资源系统哪个 location 取」
//    · 体检脚本       —— 按这里报告「找的是哪个 location」
//
//  把位置常量分散写三遍是这类工程最常见的腐烂方式：改一处忘两处，
//  然后表现为"发布成功了但运行期说找不到资源"。
//
//  ---------------------------------------------------------------------------
//  ⚠⚠ 为什么必须是 `.dll.bytes` 而不是 `.dll`
//
//  `Assets/` 下任何 `*.dll` 都会被 Unity 的**托管插件导入器**接管：
//    · 它会把那份 DLL 当成一个**程序集**加进编译
//    · 于是 `WanXiang.HotUpdate.dll` 会被编译两次（源工程一次 + 插件一次）
//    · 表现是 `CS0433: The type 'xxx' exists in both ...` 或者
//      运行期加载到错误的那个副本
//
//  加一层 `.bytes`，Unity 就当普通二进制资产（TextAsset）处理，不再有插件语义，
//  而 YooAsset 的 PackRawFile 规则读的是**磁盘原文**，多一层后缀毫无影响。
//  这是 Unity 工程里的标准做法。
//
//  ---------------------------------------------------------------------------
//  ⚠ location 为什么给多个候选，而不是定死一个
//
//  YooAsset 2.x 的 location 解析有两条并行路径（address 与 asset path），
//  再叠加收集器的 `SupportExtensionless`（不带扩展名也能命中），
//  同一个文件可能存在**多个都能命中的写法**，而且它取决于收集器的具体配置。
//
//  与其在文档里赌一个写法，不如运行期用 `CheckLocationValid` **依次试**，
//  命中哪个就用哪个，并把试过的清单原样报出来。
//  这样收集器配置将来变了，链路不会静默断掉 —— 报告里直接就能看出
//  "原来那个 location 不认了，现在认的是另一个"。
// ============================================================================

using System.Collections.Generic;
using System.Text;
using WanXiang.Framework.ResourceSystem;

namespace WanXiang.Framework.HotUpdate
{
    /// <summary>
    /// 热更产物的资源位置约定。改这里 = 改整个热更交付链的路径。
    /// </summary>
    public static class HotUpdateLocations
    {
        /// <summary>
        /// 热更 DLL 在资源系统里的 location 前缀。
        /// </summary>
        public const string HotUpdatePrefix = "HotUpdate/";

        /// <summary>
        /// AOT 元数据 DLL 在资源系统里的 location 前缀。
        /// </summary>
        public const string AotPrefix = "HotUpdate/AOT/";

        /// <summary>
        /// 这些产物在**工程里**的落地目录（相对工程根）。
        /// </summary>
        /// <remarks>
        /// 编辑器发布工具会把 DLL 复制到这里，再由 YooAsset 收集器收走。
        /// 运行期不读这个常量（它只在编辑器下有路径意义），
        /// 放在这里是为了「资源目录布局」只有一处定义。
        /// </remarks>
        public const string ProjectSubDir = "Assets/WanXiangRes/HotUpdate";

        /// <summary>
        /// 默认的热更入口程序集名。
        /// </summary>
        public const string DefaultHotUpdateAssembly = "WanXiang.HotUpdate";

        /// <summary>
        /// AOT 元数据清单的文件名（不带扩展名）。
        /// </summary>
        /// <remarks>
        /// ⚠ 为什么要单独做一个清单文件，而不是让运行期直接读
        ///   HybridCLR 生成的 <c>AOTGenericReferences.cs</c>？
        ///
        ///   因为那个文件在 `Assets/HybridCLRGenerate/` 下、**没有 asmdef**，
        ///   于是它落在 Unity 的预定义程序集 `Assembly-CSharp` 里。
        ///   而 asmdef 程序集**不能引用预定义程序集** —— 这是 Unity 的硬规则。
        ///   所以 `WanXiang.Runtime` 想读 `PatchedAOTAssemblyList`，编译期根本拿不到。
        ///
        ///   绕开的办法有三条，选了第三条：
        ///     ① 把生成目录挪进一个带 asmdef 的文件夹，让 `WanXiang.Runtime` 引用它
        ///        ✗ 副作用很坏：以后谁清一下 `HybridCLRGenerate/`，
        ///          asmdef 的引用就悬空 ⇒ **整个工程编译不过**。
        ///          一个纯生成物不该有能力打死整个工程。
        ///     ② 运行期用反射去 `Type.GetType("AOTGenericReferences, Assembly-CSharp")`
        ///        ✗ 靠字符串名字耦合，改名/换程序集就静默失效，且没有任何编译期保护。
        ///     ③ 编辑器发布时把它**读出来、写成一份数据文件**放进资源目录
        ///        ✓ 运行期只认数据，不认任何 C# 类型 ⇒ 零编译期耦合
        ///        ✓ 名单和它描述的 DLL **同一次发布、同一个目录** ⇒ 天然不会错配
        ///        ✓ 出问题时可以直接打开文件看，不用调试
        ///
        ///   这也是本工程一贯的分层口味：**生成物的产物是数据，不是代码。**
        /// </remarks>
        public const string AotManifestName = "aot_manifest";

        /// <summary>
        /// AOT 元数据清单的候选 location（按优先级排列）。
        /// </summary>
        public static string[] CandidatesForAotManifest()
        {
            return new[]
            {
                AotPrefix + AotManifestName,
                AotPrefix + AotManifestName + ".txt",
                ProjectSubDir + "/AOT/" + AotManifestName,
            };
        }

        /// <summary>
        /// 热更 DLL 的候选 location（按优先级排列）。
        /// </summary>
        /// <param name="assemblyName">程序集名，例如 <c>WanXiang.HotUpdate</c>。</param>
        public static string[] CandidatesForHotUpdateDll(string assemblyName)
        {
            return new[]
            {
                // ① 收集器配 AddressByFileName 时，address 就是「文件名（含扩展名）」
                HotUpdatePrefix + assemblyName + ".dll.bytes",
                // ② 不支持后缀上的后缀时，退一层
                HotUpdatePrefix + assemblyName + ".dll",
                // ③ 走 asset path 风格（本工程收集器实测可用的是"全路径去扩展名"）
                ProjectSubDir + "/" + assemblyName + ".dll",
                // ④ asset path 风格 + 保留 .dll
                ProjectSubDir + "/" + assemblyName + ".dll.bytes",
            };
        }

        /// <summary>
        /// AOT 元数据 DLL 的候选 location（按优先级排列）。
        /// </summary>
        /// <param name="assemblyName">程序集名，例如 <c>WanXiang.Runtime</c>。</param>
        public static string[] CandidatesForAotDll(string assemblyName)
        {
            return new[]
            {
                AotPrefix + assemblyName + ".dll.bytes",
                AotPrefix + assemblyName + ".dll",
                ProjectSubDir + "/AOT/" + assemblyName + ".dll",
                ProjectSubDir + "/AOT/" + assemblyName + ".dll.bytes",
            };
        }

        /// <summary>
        /// 用 <see cref="IResourceService.CheckLocationValid"/> 依次试候选，命中即返回。
        /// </summary>
        /// <param name="resource">资源服务。</param>
        /// <param name="candidates">候选 location 列表。</param>
        /// <param name="resolved">命中的 location；全部未命中时为 null。</param>
        /// <param name="triedReport">试过的清单（可读多行文本），用于报告。</param>
        /// <returns>是否命中。</returns>
        public static bool TryResolve(IResourceService resource, string[] candidates,
            out string resolved, out string triedReport)
        {
            resolved = null;
            var sb = new StringBuilder();

            if (resource == null)
            {
                triedReport = "（资源服务为 null，没能试任何 location）";
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                bool valid;
                try
                {
                    valid = resource.CheckLocationValid(candidate);
                }
                catch
                {
                    // 清单里没有这个 key 时，不同版本 YooAsset 的表现不一样
                    // （有的返回 false，有的抛异常）。这里统一当成"不认"。
                    valid = false;
                }

                sb.Append(valid ? "  ✅ " : "  ❌ ").Append(candidate).Append('\n');
                if (valid)
                {
                    resolved = candidate;
                    triedReport = sb.ToString().TrimEnd('\n');
                    return true;
                }
            }

            triedReport = sb.ToString().TrimEnd('\n');
            return false;
        }
    }
}
