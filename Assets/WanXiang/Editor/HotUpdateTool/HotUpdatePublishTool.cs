// ============================================================================
//  万相 · 热更产物发布工具（仅编辑器）
//  ---------------------------------------------------------------------------
//  把 HybridCLR 生成的东西，搬到「资源系统能下发」的地方。
//
//  为什么必须有这一步：
//    HybridCLR 的产物落在 `HybridCLRData/` —— 那**不在 Assets/ 下**，
//    YooAsset 收集器根本看不见它。而热更 DLL 要能下发，就必须是 Asesets 里的资源。
//    两边中间隔着一个「复制」动作，本工具补的就是它。
//
//  做五件事：
//    ① 编译热更 DLL          HybridCLR 的 CompileDllCommand
//    ② 重算 AOT 泛型引用      AOTReferenceGeneratorCommand
//                             ★ 这一步必须在①之后 —— 名单是从**刚编译出来的**
//                               DLL 里分析出来的。顺序反了拿到的是上一版名单。
//    ③ 复制热更 DLL           → Assets/WanXiangRes/HotUpdate/<程序集>.dll.bytes
//    ④ 复制 AOT 元数据 DLL    → Assets/WanXiangRes/HotUpdate/AOT/<程序集>.dll.bytes
//       并按名单写一份 aot_manifest.txt
//    ⑤ 注册收集器 + 保存配置   复用 YooAssetSetupTool（避免两处各写一份收集器逻辑）
//
//  ---------------------------------------------------------------------------
//  ⚠⚠ `.bytes` 后缀不是可选的美化，是**必需**的
//
//  `Assets/` 下任何 `*.dll` 都会被 Unity 的托管插件导入器接管，
//  于是热更 DLL 会被当成一个**程序集**参与编译 ⇒ 同一个类型存在两份 ⇒
//  `CS0433: The type 'X' exists in both ...`。
//
//  加 `.bytes` 之后 Unity 只当普通二进制资产处理，插件语义消失。
//  YooAsset 的 PackRawFile 读的是磁盘原文，多一层后缀毫无影响。
//  （这条和 HotUpdateLocations 的注释是同一个知识点，那里讲"为什么"，
//    这里讲"谁来做"，两处都写是因为漏掉任何一处都会踩坑。）
//
//  ---------------------------------------------------------------------------
//  ⚠ 为什么 AOT 名单要从 AOTGenericReferences.cs **抄出来**，而不是让运行期直接读
//
//    那个文件在 `Assets/HybridCLRGenerate/` 下、没有 asmdef ⇒ 落在预定义程序集
//    `Assembly-CSharp` 里。而 asmdef 程序集**不能引用预定义程序集**（Unity 硬规则），
//    所以 WanXiang.Runtime 编译期根本拿不到 PatchedAOTAssemblyList。
//    绕开的三种办法与取舍，写在 HotUpdateLocations.AotManifestName 的注释里。
//
//    本工具用**反射**读它（比正则解析 .cs 稳），读不到再退回解析文本。
//    两种都失败就报错并**不写清单** —— 宁可让"没补元数据"这件事显式暴露，
//    也不要写一份空清单让运行期以为"没什么要补的"。
//    （这两件事看起来都是"没补元数据"，但对排查的含义完全不同：
//      前者是工具坏了，后者是确实不需要。）
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;
using WanXiang.Framework.HotUpdate;
using WanXiang.Editor.YooTool;

namespace WanXiang.Editor.HotUpdateTool
{
    /// <summary>
    /// 把 HybridCLR 的产物发布成资源系统可下发的文件。
    /// </summary>
    public static class HotUpdatePublishTool
    {
        /// <summary>热更入口程序集名。</summary>
        public const string HotUpdateAssembly = HotUpdateLocations.DefaultHotUpdateAssembly;

        /// <summary>发布产物在工程里的落地根目录。</summary>
        public const string PublishRoot = HotUpdateLocations.ProjectSubDir;

        /// <summary>AOT 元数据落地目录。</summary>
        public const string PublishAotDir = PublishRoot + "/AOT";

        /// <summary>清单文件名。</summary>
        public const string ManifestName = HotUpdateLocations.AotManifestName + ".txt";

        // ==================================================================
        //  菜单入口
        // ==================================================================

        [MenuItem("万相/热更/发布热更产物", false, 200)]
        public static void MenuPublish()
        {
            foreach (string line in Publish())
            {
                Debug.Log("[热更工具] " + line);
            }
        }

        [MenuItem("万相/热更/打印热更产物状态", false, 201)]
        public static void MenuStatus()
        {
            foreach (string line in Status())
            {
                Debug.Log("[热更工具] " + line);
            }
        }

        // ==================================================================
        //  诊断通道入口
        // ==================================================================

        /// <summary>
        /// 诊断通道的统一入口。
        /// </summary>
        /// <param name="command">hot.publish / hot.status</param>
        /// <returns>逐行报告。由诊断通道原样写进 report.json。</returns>
        public static string[] Run(string command)
        {
            switch (command)
            {
                case "hot.publish":
                    return Publish();
                case "hot.status":
                    return Status();
                default:
                    return new[] { $"❌ HotUpdatePublishTool 不认识命令：{command}" };
            }
        }

        // ==================================================================
        //  发布
        // ==================================================================

        /// <summary>
        /// 编译 → 复制 → 写清单 → 配收集器。
        /// </summary>
        public static string[] Publish()
        {
            var report = new List<string>();
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool development = EditorUserBuildSettings.development;
            report.Add($"发布目标：{target}，development={development}");

            // 先体检 AOT strip 产物是否比源码旧。理由见 CheckAotStripFreshness ——
            // 这是本流程最容易踩、且**表现最误导人**的坑。
            List<string> freshness = CheckAotStripFreshness(target);
            report.AddRange(freshness);
            bool stripStale = freshness.Any(l => l.StartsWith("⚠⚠", StringComparison.Ordinal));

            // ---------- ① 编译热更 DLL ----------
            string dllDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            try
            {
                CompileDllCommand.CompileDll(target, development);
                report.Add($"① 编译热更 DLL 完成 → {dllDir}");
            }
            catch (Exception ex)
            {
                report.Add($"❌ ① 编译热更 DLL 失败：{ex.GetType().Name}: {ex.Message}");
                report.Add("   （脚本编译不过时这里就会失败。先修编译错误，别往下走。）");
                return report.ToArray();
            }

            // ---------- ② 重算 AOT 泛型引用 ----------
            // ★ 顺序关键：必须在①之后。名单是从刚编译的 DLL 里分析出来的。
            //
            // ⚠⚠ strip 产物过期时**必须跳过**这一步，不能"照跑一遍"。
            //    因为分析器解析不到新 AOT 类型时会**静默**产出空名单，
            //    于是这一步会把上一份（可能本来是对的）名单**覆盖成空的**。
            //    跳过之后沿用旧名单，至少不会造成损失 —— 而且报告里已经喊了。
            if (stripStale)
            {
                report.Add("② ⏭ 跳过 AOT 泛型引用重算 —— strip 产物过期，"
                           + "重算只会把名单覆盖成空（见上面的 ⚠⚠）。");
                report.Add("   本次沿用上一份 AOTGenericReferences.cs。");
            }
            else
            {
                try
                {
                    AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
                    report.Add("② AOT 泛型引用已重算（写了 AOTGenericReferences.cs）");
                }
                catch (Exception ex)
                {
                    report.Add($"⚠ ② AOT 泛型引用计算失败：{ex.GetType().Name}: {ex.Message}");
                    report.Add("   继续往下走，但名单可能过期 —— 报告末尾会标出来。");
                }
            }

            // ---------- ③ 复制热更 DLL ----------
            string srcDll = Path.Combine(dllDir, HotUpdateAssembly + ".dll");
            if (!File.Exists(srcDll))
            {
                report.Add($"❌ ③ 找不到热更 DLL：{srcDll}");
                report.Add("   常见原因：asmdef 的名字与 HotUpdateAssembly 常量不一致，"
                           + "或 WanXiang.HotUpdate 没被登记进 HybridCLR 的热更程序集列表。");
                return report.ToArray();
            }

            // ⚠ 用 YooAssetSetupTool.EnsureFolder 而不是 Directory.CreateDirectory：
            //   Assets 下的目录必须通过 AssetDatabase 建，否则 Unity 的资产库里没有这个名字，
            //   随后 Setup() 再建同名目录时 Unity 会把它改名成 "HotUpdate 1"，
            //   在版本库里留下一个空目录 + 孤立 meta（实测踩过）。
            //   建目录只留一个实现，就放在 YooAssetSetupTool 里。
            YooAssetSetupTool.EnsureFolder(PublishRoot, report);
            string dstDll = PublishRoot + "/" + HotUpdateAssembly + ".dll.bytes";
            CopyFile(srcDll, dstDll, report, "③ 热更 DLL");

            // ---------- ④ 复制 AOT 元数据 DLL + 写清单 ----------
            var aotNames = ReadPatchedAotAssemblyList(report);
            if (aotNames == null)
            {
                // 读不到名单 ⇒ **不写清单**。理由见文件头。
                report.Add("⚠ ④ 拿不到 AOT 泛型名单，本次**不写清单、不复制 AOT DLL**。");
                report.Add("   运行期读不到清单会明确报「没找到 AOT 元数据清单」，"
                           + "不会假装补过元数据。");
            }
            else if (aotNames.Count == 0)
            {
                // 空名单是**合法**的：当前热更代码确实没有 AOT 泛型需求。
                // 但和"读不到"必须区分开，所以照样写一份空清单。
                YooAssetSetupTool.EnsureFolder(PublishAotDir, report);
                WriteManifest(new List<string>(), report);
                report.Add("○ ④ AOT 名单为空 —— 当前热更代码没有「AOT 泛型 + 值类型实参」需求。");
                report.Add("   已写入空清单。运行期会报「清单为空（0 个）」并说明**本次链路未被验证**，"
                           + "而不是假装通过。");
                CleanStale(PublishAotDir, new HashSet<string>(StringComparer.Ordinal), report);
            }
            else
            {
                YooAssetSetupTool.EnsureFolder(PublishAotDir, report);
                report.Add($"④ AOT 名单共 {aotNames.Count} 项：{string.Join("、", aotNames)}");

                string stripDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
                var copied = new HashSet<string>(StringComparer.Ordinal);

                foreach (string name in aotNames)
                {
                    // 名单里是**程序集名**（不带 .dll），strip 产物是 <name>.dll
                    string src = Path.Combine(stripDir, name + ".dll");
                    if (!File.Exists(src))
                    {
                        report.Add($"   ❌ 缺 strip 产物：{src}");
                        report.Add("      ⇒ `AssembliesPostIl2CppStrip` 是整个 AOT 集成的裁剪结果。"
                                   + "跑一次完整的「HybridCLR/Generate/All」再发布。");
                        continue;
                    }

                    string dst = PublishAotDir + "/" + name + ".dll.bytes";
                    CopyFile(src, dst, report, $"   AOT 元数据 {name}");
                    copied.Add(name + ".dll.bytes");
                }

                CleanStale(PublishAotDir, copied, report);
                WriteManifest(aotNames, report);
            }

            // ---------- ⑤ 注册收集器 ----------
            try
            {
                string[] setup = YooAssetSetupTool.Setup();
                report.AddRange(setup.Select(l => "⑤ " + l));
            }
            catch (Exception ex)
            {
                report.Add($"❌ ⑤ 配置收集器失败：{ex.GetType().Name}: {ex.Message}");
                report.Add("   产物已经复制好了，只是收集器没配上。"
                           + "手动跑「万相/资源/初始化 YooAsset 收集器配置」也行。");
            }

            AssetDatabase.Refresh();

            report.Add("完成。下一步：跑「万相/热更/体检」进 Play 走一遍完整链路。");
            report.Add("⚠ 编辑器跑的是 Mono，补元数据那一步是空跑 —— "
                       + "真机结论必须出一次包才能拿到。");
            return report.ToArray();
        }

        // ==================================================================
        //  AOT strip 产物新鲜度
        // ==================================================================

        /// <summary>
        /// 检查 AOT strip 产物是不是比源码旧。
        /// </summary>
        /// <remarks>
        /// ⚠⚠ 这个方法是被一次真实故障逼出来的，**务必保留**。
        ///
        ///   实测经过：
        ///     给 AOT 侧新增了一个泛型探针类型（<c>AOTMetadataProbe.ProbeBox&lt;T&gt;</c>），
        ///     并让热更代码调用它，然后只跑发布（发布里会跑 CompileDll +
        ///     GenerateAOTGenericReference），期望 AOT 名单里出现 `WanXiang.Runtime`。
        ///
        ///     结果名单**依然是 0 项** —— 和"确实不需要补元数据"长得一模一样。
        ///
        ///   控制台里的真话（HybridCLR 自己打的警告，很容易被淹没）：
        ///     <code>
        ///     type:...AOTMetadataProbe/ProbeBox`1&lt;System.Int32&gt; ResolveTypeDef() == null
        ///     PostPrepare genericTypes:0 genericMethods:0 newMethods:0
        ///     </code>
        ///
        ///   原因：泛型分析器要解析"被引用的 AOT 类型"，而它**从
        ///   <c>HybridCLRData/AssembliesPostIl2CppStrip/</c> 里读 AOT 程序集**。
        ///   那份 strip 产物是**上一次出包时**生成的，里面根本没有新加的探针类型
        ///   ⇒ 解析失败 ⇒ 整个泛型实例化被**静默**丢弃 ⇒ 名单为空。
        ///
        ///   ⭐ 关键教训：**strip 产物会过期；过期时不报错，只会让名单变空。**
        ///      而"空名单"有两种含义 ——
        ///        ① 热更代码真的不需要补元数据（正常）
        ///        ② 工具链产物过期，分析根本没跑成（坏）
        ///      两者外观完全一样，所以必须靠**时间戳**把它们区分开。
        ///
        ///   注：发布流程本身**修不了**这个问题（strip 要出一次包才能生成），
        ///      所以本方法只负责**喊出来**，让人去跑完整的 HybridCLR/Generate/All。
        /// </remarks>
        private static List<string> CheckAotStripFreshness(BuildTarget target)
        {
            var lines = new List<string>();
            string stripDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);

            if (!Directory.Exists(stripDir))
            {
                lines.Add($"⚠⚠ AOT strip 目录不存在：{stripDir}");
                lines.Add("    任何「AOT 泛型 + 值类型实参」的分析结果都不可信（必然为空）。");
                lines.Add("    ⇒ 先跑一次完整的「HybridCLR/Generate/All」，再发布。");
                return lines;
            }

            DateTime stripTime;
            try
            {
                string[] dlls = Directory.GetFiles(stripDir, "*.dll");
                if (dlls.Length == 0)
                {
                    lines.Add($"⚠⚠ AOT strip 目录是空的：{stripDir}");
                    lines.Add("    ⇒ 先跑一次完整的「HybridCLR/Generate/All」，再发布。");
                    return lines;
                }

                stripTime = dlls.Select(File.GetLastWriteTime).Max();
            }
            catch (Exception ex)
            {
                lines.Add($"⚠ 读 strip 产物时间失败，无法判断新鲜度：{ex.Message}");
                return lines;
            }

            DateTime newestSource = default;
            try
            {
                const string codeRoot = "Assets/WanXiang";
                if (Directory.Exists(codeRoot))
                {
                    IEnumerable<string> files = Directory
                        .GetFiles(codeRoot, "*.cs", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(codeRoot, "*.asmdef", SearchOption.AllDirectories));

                    foreach (string f in files)
                    {
                        if (!AffectsAotStrip(f))
                        {
                            continue;
                        }

                        DateTime t = File.GetLastWriteTime(f);
                        if (t > newestSource)
                        {
                            newestSource = t;
                        }
                    }
                }
            }
            catch
            {
                // 扫不到就不判（newestSource 保持 default，下面会跳过比较）。
                // 这只是个启发式体检，不能因为它自己出错就阻断发布。
            }

            lines.Add($"   AOT strip 产物时间：{stripTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add(newestSource == default
                ? "   AOT 侧最新源码时间：(没扫到，跳过新鲜度判断)"
                : $"   AOT 侧最新源码时间：{newestSource:yyyy-MM-dd HH:mm:ss}");

            if (newestSource != default && stripTime < newestSource)
            {
                lines.Add("⚠⚠ AOT strip 产物**比 AOT 侧源码旧**。");
                lines.Add("    后果很隐蔽：泛型分析解析不到新加的 AOT 类型，"
                          + "名单会**静默变空**");
                lines.Add("    （HybridCLR 只会打一条 ResolveTypeDef() == null 的警告，"
                          + "很容易被淹没）。");
                lines.Add("    ⇒ 先跑一次完整的「HybridCLR/Generate/All」"
                          + "（它要出一次包才能生成 strip 产物），再跑本命令。");
            }
            else if (newestSource != default)
            {
                lines.Add("✅ AOT strip 产物不旧于 AOT 侧源码，泛型分析结果可信。");
            }

            return lines;
        }

        /// <summary>
        /// 这个源文件的变化，会不会让 AOT strip 产物过期？
        /// </summary>
        /// <remarks>
        /// ⚠ 判据必须**收窄到"进玩家包的 AOT 程序集"**，不能用"整个 Assets/WanXiang
        ///   下最新改动的文件"。第一版就是这么写的，结果是：
        ///   我刚改了一下编辑器工具（本文件），下次发布就报"strip 产物比源码旧"——
        ///   而编辑器代码**根本不在玩家包里**，不可能让 strip 产物过期。
        ///   一个总是误报的检查等于没有检查（而且比没有更糟：会让人忽略它）。
        ///
        ///   排除两类：
        ///     · 路径里含 <c>/Editor/</c> 的 —— 编辑器程序集不进玩家包
        ///       （它们在自己的 asmdef 里写了 includePlatforms: ["Editor"]）
        ///     · <c>Assets/WanXiang/HotUpdate/</c> —— 那是**热更程序集**，
        ///       它压根不在 AOT 主包里，改动它只会让"泛型引用分析"需要重跑
        ///       （步骤②每次都会跑），不会让 strip 产物过期。
        /// </remarks>
        private static bool AffectsAotStrip(string assetPath)
        {
            string p = assetPath.Replace('\\', '/');
            if (p.Contains("/Editor/"))
            {
                return false;
            }

            if (p.StartsWith("Assets/WanXiang/HotUpdate/", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        // ==================================================================
        //  状态报告
        // ==================================================================

        /// <summary>
        /// 只读体检：各段产物在不在、名单是什么。
        /// </summary>
        public static string[] Status()
        {
            var report = new List<string>();
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

            report.Add($"目标平台：{target}（development={EditorUserBuildSettings.development}）");

            // ---- HybridCLR 侧产物 ----
            string dllDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotDll = Path.Combine(dllDir, HotUpdateAssembly + ".dll");
            report.Add(File.Exists(hotDll)
                ? $"✅ 热更 DLL 已生成：{hotDll}（{Size(hotDll)}）"
                : $"❌ 热更 DLL 不存在：{hotDll} —— 跑「万相/热更/发布热更产物」");

            string stripDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            int stripCount = Directory.Exists(stripDir)
                ? Directory.GetFiles(stripDir, "*.dll").Length
                : 0;
            report.Add(stripCount > 0
                ? $"✅ AOT strip 产物 {stripCount} 个：{stripDir}"
                : $"❌ AOT strip 产物目录为空：{stripDir} —— 跑一次完整的 HybridCLR/Generate/All");

            // ---- 资源侧产物 ----
            string publishedDll = PublishRoot + "/" + HotUpdateAssembly + ".dll.bytes";
            report.Add(File.Exists(publishedDll)
                ? $"✅ 热更 DLL 已发布：{publishedDll}（{Size(publishedDll)}）"
                : $"❌ 热更 DLL 未发布：{publishedDll}");

            string manifestPath = PublishAotDir + "/" + ManifestName;
            if (File.Exists(manifestPath))
            {
                string text = File.ReadAllText(manifestPath);
                var names = AOTMetadataLoader.ParseManifest(text);
                report.Add($"✅ AOT 清单已发布：{manifestPath}（{names.Count} 项）");
                report.Add(names.Count > 0
                    ? $"   名单：{string.Join("、", names)}"
                    : "   名单为空 ⇒ 当前热更代码没有 AOT 泛型需求，补元数据链路本次未被验证。");
            }
            else
            {
                report.Add($"❌ AOT 清单未发布：{manifestPath}");
            }

            int aotBytes = Directory.Exists(PublishAotDir)
                ? Directory.GetFiles(PublishAotDir, "*.bytes").Length
                : 0;
            report.Add($"   AOT 元数据 DLL：{aotBytes} 个");

            // ---- 收集器 ----
            var collectorLines = YooAssetSetupTool.Status();
            foreach (string line in collectorLines)
            {
                if (line.Contains("HotUpdate") || line.Contains("组") || line.Contains("落盘"))
                {
                    report.Add("   " + line);
                }
            }

            return report.ToArray();
        }

        // ==================================================================
        //  AOT 名单
        // ==================================================================

        /// <summary>
        /// 读出 <c>AOTGenericReferences.PatchedAOTAssemblyList</c>。
        /// </summary>
        /// <returns>
        /// 程序集名列表；读不到返回 null（**与"读出空列表"是两回事**）。
        /// </returns>
        /// <remarks>
        /// 两种读法，优先反射：
        ///   反射 —— 从 `Assembly-CSharp` 里找类型，稳、不依赖文件格式
        ///   解析 —— 读 .cs 原文，正则抽引号里的名字，作为兜底
        /// 反射失败一般是"生成文件还没被编译进 Assembly-CSharp"
        /// （刚 Generate 完、还没 Refresh 过），这时解析文本正好补上。
        /// </remarks>
        public static List<string> ReadPatchedAotAssemblyList(List<string> report)
        {
            // ---- 反射 ----
            try
            {
                Type type = Type.GetType("AOTGenericReferences, Assembly-CSharp", false);
                if (type != null)
                {
                    FieldInfo field = type.GetField("PatchedAOTAssemblyList",
                        BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        if (field.GetValue(null) is IEnumerable<string> values)
                        {
                            // ⚠ 必须归一化：HybridCLR 的 GenericReferenceWriter 写进去的是
                            //   `module.Name`，**带 `.dll` 后缀**（例如 "WanXiang.Runtime.dll"）。
                            //   不剥掉的话，后面拼 "名字 + .dll" 会得到
                            //   "WanXiang.Runtime.dll.dll"，然后报"缺 strip 产物"。
                            //   （这个 bug 就是被本工具自己的报错抓出来的 —— 所以那条报错
                            //     比"静默跳过"值钱得多，别把它改温和了。）
                            var list = new List<string>();
                            var seen = new HashSet<string>(StringComparer.Ordinal);
                            foreach (string v in values)
                            {
                                string name = NormalizeAssemblyName(v);
                                if (name.Length > 0 && seen.Add(name))
                                {
                                    list.Add(name);
                                }
                            }

                            report.Add($"   （名单来源：反射 Assembly-CSharp/AOTGenericReferences，"
                                       + $"{list.Count} 项）");
                            return list;
                        }
                    }
                    else
                    {
                        report.Add("   ⚠ AOTGenericReferences 上没有 PatchedAOTAssemblyList 字段"
                                   + " —— HybridCLR 版本可能变了，改读文件。");
                    }
                }
                else
                {
                    report.Add("   ⚠ 找不到 AOTGenericReferences 类型（还没被编译进 "
                               + "Assembly-CSharp？），改读文件。");
                }
            }
            catch (Exception ex)
            {
                report.Add($"   ⚠ 反射读名单异常：{ex.GetType().Name}: {ex.Message}，改读文件。");
            }

            // ---- 解析文本 ----
            try
            {
                string rel = SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile;
                string full = Path.Combine(Application.dataPath, rel);
                if (!File.Exists(full))
                {
                    report.Add($"   ❌ AOTGenericReferences.cs 不存在：{full}");
                    report.Add("      ⇒ 跑一次「HybridCLR/Generate/AOTGenericReference」。");
                    return null;
                }

                var list = ParseAotGenericReferenceFile(File.ReadAllText(full));
                report.Add($"   （名单来源：解析文件 {rel}，{list.Count} 项）");
                return list;
            }
            catch (Exception ex)
            {
                report.Add($"   ❌ 解析 AOTGenericReferences.cs 异常："
                           + $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 AOTGenericReferences.cs 原文里抽出程序集名。
        /// </summary>
        /// <remarks>
        /// ⚠ 只认 <c>// {{ AOT assemblies</c> 到 <c>// }}</c> 之间的内容。
        ///   为什么不用「全文找所有引号字符串」这种省事的写法：
        ///   文件下面还有 <c>RefMethods()</c> 与两段注释块，
        ///   一旦 HybridCLR 哪天把泛型名从注释改成真实代码，
        ///   全文扫描就会把类型名当成程序集名抄进名单 —— 那种错很难看出来。
        ///   锁定区段之后，格式变化最多导致"名单为空"，是**响亮**的失败。
        /// </remarks>
        public static List<string> ParseAotGenericReferenceFile(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return list;
            }

            const string begin = "// {{ AOT assemblies";
            const string end = "// }}";

            int start = text.IndexOf(begin, StringComparison.Ordinal);
            if (start < 0)
            {
                return list;
            }

            int stop = text.IndexOf(end, start + begin.Length, StringComparison.Ordinal);
            if (stop < 0)
            {
                stop = text.Length;
            }

            string section = text.Substring(start, stop - start);

            // 形如：  "mscorlib.dll",
            MatchCollection matches = Regex.Matches(section, "\"([^\"]+)\"");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in matches)
            {
                string name = m.Groups[1].Value.Trim();
                name = NormalizeAssemblyName(name);
                if (name.Length == 0) continue;
                if (seen.Add(name))
                {
                    list.Add(name);
                }
            }

            return list;
        }

        /// <summary>
        /// 把程序集名归一化成**不带 <c>.dll</c>** 的形式。
        /// </summary>
        /// <remarks>
        /// ⚠ 两种来源的后缀情况**不一样**，很容易搞错：
        ///   · 反射读 <c>PatchedAOTAssemblyList</c> —— 值来自 <c>module.Name</c>，
        ///     **带** `.dll`（"WanXiang.Runtime.dll"）
        ///   · 解析 <c>AOTGenericReferences.cs</c> 原文 —— 引号里也是带 `.dll` 的字面量
        /// 所以两条路都要过一遍本方法。归一化之后，清单文件与磁盘文件名就一致了。
        /// </remarks>
        private static string NormalizeAssemblyName(string raw)
        {
            string name = (raw ?? string.Empty).Trim();
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            return name;
        }

        // ==================================================================
        //  文件操作
        // ==================================================================

        private static void WriteManifest(List<string> names, List<string> report)
        {
            var sb = new StringBuilder();
            sb.Append("# 万相 · AOT 泛型元数据清单\n");
            sb.Append("# 由「万相/热更/发布热更产物」自动生成，**不要手工改**。\n");
            sb.Append("# 来源：HybridCLR 的 Generate/AOTGenericReference 分析热更代码后写出的\n");
            sb.Append("#       AOTGenericReferences.cs 里的 PatchedAOTAssemblyList。\n");
            sb.Append("# 格式：一行一个程序集名（不带 .dll），# 开头是注释。\n");
            sb.Append($"# 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            sb.Append($"# 程序集数：{names.Count}\n");
            foreach (string n in names)
            {
                sb.Append(n).Append('\n');
            }

            string full = PublishAotDir + "/" + ManifestName;
            File.WriteAllText(full, sb.ToString(), new UTF8Encoding(false));
            report.Add($"   ✎ 已写清单 {full}（{names.Count} 项）");
        }

        private static void CopyFile(string source, string target, List<string> report, string label)
        {
            try
            {
                string dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(source, target, true);
                report.Add($"   ✓ {label} → {target}（{Size(target)}）");
            }
            catch (Exception ex)
            {
                report.Add($"   ❌ {label} 复制失败：{source} → {target}，"
                           + $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 删掉上次发布、这次已经不在名单里的 <c>.bytes</c>。
        /// </summary>
        /// <remarks>
        /// ⚠ 必须做这一步。否则名单缩短时（某个泛型需求被删掉），
        ///   旧 DLL 会**一直留在资源目录里**继续被打进包 —— 包体白涨，
        ///   而且它会以"幽灵资源"的形式出现在清单里，让人以为还在用。
        /// </remarks>
        private static void CleanStale(string dir, HashSet<string> keep, List<string> report)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            int removed = 0;
            foreach (string file in Directory.GetFiles(dir, "*.bytes"))
            {
                string name = Path.GetFileName(file);
                if (keep.Contains(name))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    // 同名的 .meta 也要删，否则 Unity 会留一个指向空文件的 meta。
                    string meta = file + ".meta";
                    if (File.Exists(meta))
                    {
                        File.Delete(meta);
                    }
                    removed++;
                }
                catch (Exception ex)
                {
                    report.Add($"   ⚠ 清理旧产物失败：{name}，{ex.Message}");
                }
            }

            if (removed > 0)
            {
                report.Add($"   ✂ 清理了 {removed} 个已不在名单里的旧产物");
            }
        }

        private static string Size(string path)
        {
            try
            {
                return new FileInfo(path).Length / 1024 + " KB";
            }
            catch
            {
                return "大小未知";
            }
        }
    }
}
