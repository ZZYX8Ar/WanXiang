// ============================================================================
//  万相 · 编辑器诊断桥 · HotUpdate（从 EditorDiagnosticsBridge.cs 拆出：**纯搬家**）
//  ---------------------------------------------------------------------------
//  partial class —— 与主文件共享作用域；字段/常量仍声明在主文件里。
//  ⚠ 只挪位置，没改任何一行逻辑。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace WanXiang.EditorTools.Diagnostics
{
    public static partial class EditorDiagnosticsBridge
    {

        /// <remarks>
        /// 反射导出 HybridCLR 设置对象的所有公开字段。不硬编码字段名，
        /// 因为「热更程序集列表」这个字段在 HybridCLR 历史上改过名。
        /// 找到对象之后，用 JsonUtility 不行（它要编译期类型），所以手写字段遍历。
        /// </remarks>
        private static void DumpHybridClrSettings(Report report)
        {
            var settingsType = FindType("HybridCLR.Editor.Settings.HybridCLRSettings");
            if (settingsType == null)
            {
                report.hybridClrSettings.Add("❌ 找不到 HybridCLR.Editor.Settings.HybridCLRSettings");
                return;
            }

            object instance = GetHybridClrSettings(out string via);
            if (instance == null)
            {
                report.hybridClrSettings.Add("⚠ 四种取法都拿不到设置对象。类型上的公开静态字段如下：");
                DumpFields(report.hybridClrSettings, null, settingsType);
                return;
            }

            report.hybridClrSettings.Add($"类型: {settingsType.FullName}   （取得方式: {via}）");
            DumpFields(report.hybridClrSettings, instance, settingsType);
        }

        /// <remarks>
        /// 拿 HybridCLR 设置对象有四条可能的路子，按可能性从高到低逐个试。
        /// 之所以要试这么多：它的 Settings 是「静态 Save + 静态 LoadOrCreate」
        /// 这种 ScriptableSingleton 变体，实测反射出来的公开方法就是
        /// <c>static HybridCLRSettings LoadOrCreate()</c> 与 <c>static Void Save()</c>。
        /// 第一版只找 Instance 属性，结果什么都没拿到 —— 所以现在多路兜底。
        /// </remarks>
        private static object GetHybridClrSettings(out string via)
        {
            via = null;
            var settingsType = FindType("HybridCLR.Editor.Settings.HybridCLRSettings");
            if (settingsType == null) return null;

            var instance = TryGetStaticMember(settingsType, "Instance");
            if (instance != null) { via = "static Instance"; return instance; }

            instance = TryInvokeStatic(settingsType, "LoadOrCreate");
            if (instance != null) { via = "static LoadOrCreate()"; return instance; }

            instance = TryInvokeStatic(settingsType, "GetOrCreateInstance");
            if (instance != null) { via = "static GetOrCreateInstance()"; return instance; }

            // 兜底：按 Unity 的 ScriptableSingleton 惯例路径直接加载资产
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "ProjectSettings/HybridCLRSettings.asset");
            if (asset != null) { via = "ProjectSettings/HybridCLRSettings.asset"; return asset; }

            return null;
        }

        private static object TryGetStaticMember(Type type, string name)
        {
            try
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (prop != null) return prop.GetValue(null);

                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (field != null) return field.GetValue(null);
            }
            catch { /* 取不到就交给下一个策略 */ }
            return null;
        }

        /// <remarks>
        /// 只在**无参重载**存在时才调用 —— 参数对不上就不猜，宁可不做事也不能调错。
        /// </remarks>
        private static object TryInvokeStatic(Type type, string name)
        {
            try
            {
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name) continue;
                    if (m.GetParameters().Length != 0) continue;
                    return m.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 调用 {type.Name}.{name}() 失败：{e.GetType().Name}");
            }
            return null;
        }

        private static void DumpFields(List<string> sink, object instance, Type type)
        {
            // 拿到对象就导实例字段，拿不到就退一步导静态字段。
            // 两种情况用不同的 BindingFlags —— 混用会对着 null 实例反射实例字段而抛异常。
            var flags = instance != null
                ? BindingFlags.Public | BindingFlags.Instance
                : BindingFlags.Public | BindingFlags.Static;

            var fields = type.GetFields(flags);
            if (fields.Length == 0)
            {
                sink.Add(instance != null ? "  (没有公开实例字段)" : "  (没有公开静态字段)");
                return;
            }

            foreach (var f in fields.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                object value = null;
                try { if (instance != null) value = f.GetValue(instance); }
                catch (Exception e) { sink.Add($"  {f.Name} = <读取失败 {e.GetType().Name}>"); continue; }

                // 数组与列表展开，不然只会看到一个类型名，毫无信息量
                if (value is System.Collections.IEnumerable en && !(value is string))
                {
                    var items = new List<string>();
                    foreach (var item in en) items.Add(item?.ToString() ?? "null");
                    sink.Add($"  {f.Name} ({f.FieldType.Name}, {items.Count} 项) = "
                             + (items.Count > 0 ? string.Join(", ", items) : "(空)"));
                }
                else
                {
                    sink.Add($"  {f.Name} ({f.FieldType.Name}) = {value ?? "null"}");
                }
            }
        }

        // ====================================================================
        //  assemblies —— 已加载的程序集（判断「没加载」还是「名字写错」）
        // ====================================================================

        /// <remarks>
        /// 反射探针报告「未找到」时，第一步就该看这个：到底是目标程序集压根没被加载，
        /// 还是单纯类型名写错了。没有这条信息就只能瞎猜。
        /// </remarks>
        private static void ProbeAssemblies(Report report)
        {
            int total = 0;
            var interesting = new List<string>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                total++;
                string name;
                try { name = asm.GetName().Name; }
                catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;

                if (name.StartsWith("WanXiang", StringComparison.Ordinal)
                    || name.StartsWith("HybridCLR", StringComparison.Ordinal)
                    || name.StartsWith("YooAsset", StringComparison.Ordinal)
                    || name.StartsWith("QFramework", StringComparison.Ordinal)
                    || name.StartsWith("UniTask", StringComparison.Ordinal))
                {
                    interesting.Add(name);
                }
            }

            interesting.Sort(StringComparer.OrdinalIgnoreCase);
            report.assemblies.Add($"已加载程序集总数 {total}，其中本工程/相关共 {interesting.Count} 个：");
            foreach (var n in interesting) report.assemblies.Add("  " + n);
        }

        // ====================================================================
        //  console —— 读 Unity 控制台（包解析失败、编译错误只在这里出现）
        // ====================================================================

        /// <remarks>
        /// Unity **没有公开 API** 读历史控制台记录，只能反射内部类
        /// `UnityEditor.LogEntries` / `UnityEditor.LogEntry`。
        /// 这两个类在版本间改过，所以全程 try 包裹、拿不到就明说，不假装成功。
        ///
        /// 这条命令的价值：MCP 挂了之后，控制台是唯一能告诉我「包里为什么没装上」
        /// 「哪个文件编译不过」的地方。
        /// </remarks>
        private static void ProbeConsole(Report report)
        {
            var logEntriesType = FindType("UnityEditor.LogEntries");
            var logEntryType = FindType("UnityEditor.LogEntry");
            if (logEntriesType == null || logEntryType == null)
            {
                report.console.Add("⚠ 找不到 UnityEditor.LogEntries / LogEntry（内部 API 改名了）");
                return;
            }

            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var getCount = logEntriesType.GetMethod("GetCount", any);
            var getEntry = logEntriesType.GetMethod("GetEntryInternal", any);
            if (getCount == null || getEntry == null)
            {
                report.console.Add("⚠ LogEntries 上找不到 GetCount / GetEntryInternal");
                return;
            }

            var messageField = logEntryType.GetField("message",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var modeField = logEntryType.GetField("mode",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (messageField == null) { report.console.Add("⚠ LogEntry 上没有 message 字段"); return; }

            int count;
            try { count = Convert.ToInt32(getCount.Invoke(null, null)); }
            catch (Exception e) { report.console.Add($"⚠ GetCount 调用失败 {e.GetType().Name}"); return; }

            object entry;
            try { entry = Activator.CreateInstance(logEntryType); }
            catch (Exception e) { report.console.Add($"⚠ 无法构造 LogEntry {e.GetType().Name}"); return; }

            report.console.Add($"控制台共 {count} 条，下面是最近 30 条（mode 低位即 LogType："
                               + "0=Error 1=Assert 2=Warning 3=Log 4=Exception）：");

            int emitted = 0;
            for (int i = count - 1; i >= 0 && emitted < 30; i--, emitted++)
            {
                try { getEntry.Invoke(null, new object[] { i, entry }); }
                catch { break; }

                string msg = messageField.GetValue(entry) as string ?? string.Empty;
                int mode = 0;
                if (modeField != null)
                {
                    try { mode = Convert.ToInt32(modeField.GetValue(entry)); } catch { }
                }

                // 多行报错挤在一行里没法读，压成一行
                msg = msg.Replace("\r", " ").Replace("\n", " ");
                if (msg.Length > 300) msg = msg.Substring(0, 300) + " …(截断)";

                report.console.Add($"  [{i}] mode={mode} {msg}");
            }
        }

        // ====================================================================
        //  hybridclr —— 状态查询与安装
        // ====================================================================

        private const string InstallerTypeName = "HybridCLR.Editor.Installer.InstallerController";

        /// <summary>
        /// 反射 new 一个 InstallerController。找不到/无法构造都返回 null 并记原因。
        /// </summary>
        private static object CreateInstaller(Report report, string tag)
        {
            var type = FindType(InstallerTypeName);
            if (type == null)
            {
                report.results.Add($"{tag} -> ❌ 找不到 {InstallerTypeName}（包没装上？）");
                return null;
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ❌ 无法实例化：{e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static void ProbeHybridClrStatus(Report report)
        {
            object controller = CreateInstaller(report, "hybridclr.status");
            if (controller == null) return;

            var type = controller.GetType();
            report.results.Add("hybridclr.status -> InstallerController 上的无参查询方法：");

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.GetParameters().Length != 0) continue;
                if (m.ReturnType != typeof(bool) && m.ReturnType != typeof(string)) continue;
                if (m.Name.StartsWith("get_", StringComparison.Ordinal)) continue;

                string value;
                try { value = Convert.ToString(m.Invoke(controller, null)); }
                catch (Exception e) { value = $"<抛 {e.GetType().Name}>"; }

                report.results.Add($"    {Signature(m)} = {value}");
            }
        }

        /// <summary>
        /// 安装 HybridCLR 运行环境（把 Unity 自带 il2cpp 复制到工程内并打补丁）。
        /// </summary>
        /// <remarks>
        /// 刻意**先打印所有候选签名、只在存在无参重载时才调用**。
        /// 因为「参数个数靠猜」= 反射调用必炸；而且这个方法可能要跑几分钟，
        /// 调错了很浪费时间。宁可多一个来回，也不盲调。
        /// </remarks>
        private static void InstallHybridClr(Report report)
        {
            object controller = CreateInstaller(report, "hybridclr.install");
            if (controller == null) return;

            var type = controller.GetType();

            // 已装就跳过，避免把已经打好的 il2cpp 又覆盖一遍
            var already = FindMethod(type, "HasInstalledHybridCLR");
            if (already != null)
            {
                bool installed;
                try { installed = Convert.ToBoolean(already.Invoke(controller, null)); }
                catch (Exception e)
                {
                    report.results.Add($"hybridclr.install -> ❌ HasInstalledHybridCLR() 抛 {e.GetType().Name}");
                    return;
                }

                report.results.Add($"hybridclr.install -> HasInstalledHybridCLR() = {installed}");
                if (installed)
                {
                    report.results.Add("hybridclr.install -> 环境已就绪，跳过安装");
                    return;
                }
            }

            var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "InstallDefaultHybridCLR")
                .ToList();

            if (candidates.Count == 0)
            {
                report.results.Add("hybridclr.install -> ❌ 找不到 InstallDefaultHybridCLR");
                return;
            }

            foreach (var m in candidates)
                report.results.Add("hybridclr.install -> 候选签名: " + Signature(m));

            var target = candidates.FirstOrDefault(m => m.GetParameters().Length == 0);
            if (target == null)
            {
                report.results.Add("hybridclr.install -> ⚠ 没有无参重载，不擅自调用（等看清单后按签名调）");
                return;
            }

            report.results.Add("hybridclr.install -> 开始安装，可能要几分钟，请勿关闭编辑器…");
            try
            {
                target.Invoke(controller, null);
                report.results.Add("hybridclr.install -> ✅ InstallDefaultHybridCLR() 正常返回");
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                report.results.Add($"hybridclr.install -> ❌ 安装失败 {inner.GetType().Name}: {inner.Message}");
            }
        }

        /// <summary>
        /// 从**本地**已合并好的 libil2cpp 目录安装 HybridCLR 环境。
        /// </summary>
        /// <remarks>
        /// 为什么走这条而不是 <see cref="InstallHybridCLR"/> 调的
        /// InstallDefaultHybridCLR()：
        ///   后者内部会 `git clone`（hybridclr + il2cpp_plus 两个仓库）。
        ///   而 Unity 是**用户手动启动**的进程 —— 它既继承不到我们这边的
        ///   HTTP_PROXY，又会优先使用 git config 里那个已失效的 http.proxy，
        ///   所以必失败。而且那条路是同步阻塞的，失败后也不好排查。
        ///
        ///   官方文档明确支持「自己把两个仓库合并成 libil2cpp，再让 Installer
        ///   从本地复制」。我们只是把 git 那一步挪到编辑器外面做掉了。
        ///
        /// 这条路的好处（都对 Unity 2022 生效）：
        ///   • 无 shell 依赖 —— BashUtil 是纯 .NET 实现（Directory/FileUtil），
        ///     只有 RunCommand 才调 git，而这不走那条
        ///   • 无网络依赖
        ///   • 2019 才需要替换 Unity.IL2CPP.dll，2022 直接跳过，
        ///     所以连包里的 Data~ 都不需要读
        /// </remarks>
        private static void InstallFromLocalDir(Report report)
        {
            string dataDir = ResolveHybridClrDataDir();
            string libil2cpp = Path.Combine(dataDir, "il2cpp_plus_repo", "libil2cpp");
            report.results.Add($"hybridclr.install-local -> 目标: {libil2cpp}");

            if (!Directory.Exists(libil2cpp))
            {
                report.results.Add("❌ libil2cpp 目录不存在。先 clone 两个仓库并合并："
                                   + "把 hybridclr_repo/hybridclr 移到 il2cpp_plus_repo/libil2cpp/hybridclr");
                return;
            }
            if (!Directory.Exists(Path.Combine(libil2cpp, "hybridclr")))
            {
                report.results.Add("❌ libil2cpp 下没有 hybridclr 子目录 —— 两个仓库还没合并，安装出来的是普通 il2cpp");
                return;
            }

            object controller = CreateInstaller(report, "hybridclr.install-local");
            if (controller == null) return;

            var type = controller.GetType();

            var compatible = FindMethod(type, "GetCompatibleType");
            if (compatible != null)
            {
                try { report.results.Add($"hybridclr.install-local -> GetCompatibleType() = {compatible.Invoke(controller, null)}"); }
                catch { /* 不重要，失败就算了 */ }
            }

            var install = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "InstallFromLocal"
                                     && m.GetParameters().Length == 1
                                     && m.GetParameters()[0].ParameterType == typeof(string));
            if (install == null)
            {
                report.results.Add("❌ 找不到 InstallFromLocal(string)");
                return;
            }

            report.results.Add("hybridclr.install-local -> 开始安装。"
                               + "会复制 Editor 自带的 il2cpp（约 110MB）与 MonoBleedingEdge，"
                               + "并清空 Library/Il2cppBuildCache，请耐心等待…");
            try
            {
                install.Invoke(controller, new object[] { libil2cpp });
                report.results.Add("✅ InstallFromLocal 正常返回");
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                report.results.Add($"❌ 安装失败 {inner.GetType().Name}: {inner.Message}");
                return;
            }

            // 复检：目录里真的出现 libil2cpp/hybridclr 才算成功
            var has = FindMethod(type, "HasInstalledHybridCLR");
            if (has != null)
            {
                try { report.results.Add($"hybridclr.install-local -> 复检 HasInstalledHybridCLR() = {has.Invoke(controller, null)}"); }
                catch (Exception e) { report.results.Add($"复检失败 {e.GetType().Name}"); }
            }
        }

        /// <remarks>
        /// 先问 <c>SettingsUtil.HybridCLRDataDir</c>（最权威），拿不到再按官方目录约定
        /// 自己拼 <c>{工程根}/HybridCLRData</c>。两条都走不通就没法继续。
        /// </remarks>
        private static string ResolveHybridClrDataDir()
        {
            var settingsUtil = FindType("HybridCLR.Editor.SettingsUtil");
            if (settingsUtil != null)
            {
                var prop = settingsUtil.GetProperty("HybridCLRDataDir",
                    BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var value = prop.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }

            string projectRoot = Directory.GetParent(Application.dataPath).ToString();
            return Path.Combine(projectRoot, "HybridCLRData");
        }

        // ====================================================================
        //  hybridclr.set-hotupdate —— 把热更程序集登记进 HybridCLR 设置
        // ====================================================================

        /// <summary>热更程序集的 asmdef 路径（相对工程根，正斜杠）。</summary>
        private const string HotUpdateAsmdefPath =
            "Assets/WanXiang/HotUpdate/WanXiang.HotUpdate.asmdef";

        /// <summary>该 asmdef 声明的程序集名。要同步进 hotUpdateAssemblies 那份名字清单。</summary>
        private const string HotUpdateAssemblyName = "WanXiang.HotUpdate";

        /// <remarks>
        /// HybridCLR 判断「哪些程序集要热更」其实读**两个**字段并合并：
        ///   hotUpdateAssemblyDefinitions : AssemblyDefinitionAsset[]   ← 设置窗口写这个
        ///   hotUpdateAssemblies          : string[] / List&lt;string&gt;  ← 老路径，也认
        /// 所以两个都写。附带好处：万一 asmdef 资产加载失败，光凭名字那份它也能生效。
        ///
        /// ⚠ 三处不许猜：
        ///   1. 数组元素类型**从字段自己身上取**（FieldType.GetElementType()），
        ///      不按名字去猜。因为 AssemblyDefinitionAsset 在 UnityEditorInternal
        ///      命名空间，编译期引用不到 —— 上一版写 ProbeAsmdefs 时已经在这儿
        ///      吃过一次 CS0246，教训就是「凭印象写的 Unity API 就会写错」。
        ///   2. 拿资产用 AssetDatabase.LoadAssetAtPath(path, Type) 的**非泛型**重载。
        ///      asmdef 是导入器生成的 TextAsset 子类资产，new 不出来，必须走 AssetDatabase。
        ///   3. 合并而非覆盖：设置里可能还有别人配的 asmdef，不能一把推平。
        /// </remarks>
        private static void SetHotUpdateAssemblies(Report report)
        {
            const string tag = "hybridclr.set-hotupdate";

            object settings = GetHybridClrSettings(out string via);
            if (settings == null)
            {
                report.results.Add($"{tag} -> ❌ 四条路都拿不到 HybridCLRSettings。先跑 hybridclr.dump。");
                return;
            }
            Type settingsType = settings.GetType();
            report.results.Add($"{tag} -> 设置对象取得方式: {via}");

            string projectRoot = Directory.GetParent(Application.dataPath).ToString();
            string asmdefOnDisk = Path.Combine(projectRoot,
                HotUpdateAsmdefPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(asmdefOnDisk))
            {
                report.results.Add($"{tag} -> ❌ 磁盘上找不到 {HotUpdateAsmdefPath}，先确认热更程序集的 asmdef 建好了");
                return;
            }

            // ---- 1. 元素类型从字段自身取，字段缺失才退化为按简名找 ----
            FieldInfo defField = FindField(settingsType, "hotUpdateAssemblyDefinitions");
            Type elementType = defField?.FieldType.GetElementType();
            if (elementType == null)
            {
                elementType = FindTypeBySimpleName("AssemblyDefinitionAsset");
                report.results.Add(elementType == null
                    ? $"{tag} -> ❌ 既没有 hotUpdateAssemblyDefinitions 字段，也找不到 AssemblyDefinitionAsset 类型"
                    : $"{tag} -> ⚠ 没有 hotUpdateAssemblyDefinitions 字段，退化用简名找到 {elementType.FullName}");
            }
            if (elementType == null) return;

            // ---- 2. 加载 asmdef 资产 ----
            object asmdefAsset = null;
            try
            {
                asmdefAsset = AssetDatabase.LoadAssetAtPath(HotUpdateAsmdefPath, elementType);
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ⚠ LoadAssetAtPath({elementType.Name}) 抛异常: {e.GetType().Name}");
            }

            if (asmdefAsset == null)
            {
                report.results.Add($"{tag} -> ⚠ LoadAssetAtPath 拿不到资产，改为只写 hotUpdateAssemblies 名字"
                                   + "（HybridCLR 合并读两份，仍然生效）");
            }
            else
            {
                report.results.Add($"{tag} -> 已加载资产 {asmdefAsset}  @ {AssetPathOf(asmdefAsset)}");
            }

            // ---- 3. 写 hotUpdateAssemblyDefinitions（追加，保留既有项）----
            if (defField == null)
            {
                // 上面已经报过「字段不存在」，这里不再重复报警
            }
            else if (asmdefAsset == null)
            {
                report.results.Add($"{tag} -> 资产为空，hotUpdateAssemblyDefinitions 保持原样");
            }
            else
            {
                var old = defField.GetValue(settings) as Array;
                var kept = new List<object>();
                if (old != null) foreach (object o in old) kept.Add(o);

                int slot = kept.FindIndex(o => AssetPathOf(o) == HotUpdateAsmdefPath);
                if (slot >= 0)
                {
                    kept[slot] = asmdefAsset;
                    report.results.Add($"{tag} -> 已存在，替换为最新引用（第 {slot} 项）");
                }
                else
                {
                    kept.Add(asmdefAsset);
                    report.results.Add($"{tag} -> 追加为新项（{old?.Length ?? 0} → {kept.Count} 项）");
                }

                Array fresh = Array.CreateInstance(elementType, kept.Count);
                for (int i = 0; i < kept.Count; i++) fresh.SetValue(kept[i], i);
                defField.SetValue(settings, fresh);
            }

            // ---- 4. 把 hotUpdateAssemblies 规范成非 null ----
            // 只有 asmdef 那条路没走通时才往里写名字（否则会与 asmdef 的 name 重复）。
            FieldInfo namesField = FindField(settingsType, "hotUpdateAssemblies");
            if (namesField == null)
            {
                report.results.Add($"{tag} -> ⚠ 没有 hotUpdateAssemblies 字段，跳过");
            }
            else
            {
                WriteAssemblyNameList(report, tag, settings, namesField, addName: asmdefAsset == null);
            }

            // ---- 5. 保存 ----
            SaveSettings(report, tag, settings, settingsType);

            // ---- 6. 复读验证，顺便把最新设置快照打进报告的 HybridCLR 段 ----
            var now = defField?.GetValue(settings) as Array;
            report.results.Add($"{tag} -> 复读 hotUpdateAssemblyDefinitions: {now?.Length ?? 0} 项"
                               + (now != null && now.Length > 0
                                   ? " → " + string.Join(", ", now.Cast<object>().Select(AssetPathOf))
                                   : ""));
            report.hybridClrSettings.Add($"── 登记热更程序集后的设置快照（{DateTime.Now:HH:mm:ss}）──");
            DumpFields(report.hybridClrSettings, settings, settingsType);
        }

        /// <summary>把对象还原成资产路径；不是资产或没有路径时给出可辨认的替代文本。</summary>
        private static string AssetPathOf(object asset)
        {
            if (asset == null) return "null";
            var unityObj = asset as UnityEngine.Object;
            if (unityObj == null) return asset.ToString();
            string path = AssetDatabase.GetAssetPath(unityObj);
            return string.IsNullOrEmpty(path) ? "<无路径，可能是内存对象>" : path;
        }

        /// <remarks>
        /// 名字清单在 HybridCLR 里是 <c>public string[] hotUpdateAssemblies</c>，
        /// 实测**初值是 null**（不是空数组）。这里有三个坑叠在一起，逐条说清：
        ///
        /// ⚠ 坑一：null 不能放过。
        ///   HybridCLR 的 CheckSettings.cs 里写着
        ///     <c>((gs.hotUpdateAssemblies?.Length + gs.hotUpdateAssemblyDefinitions?.Length) ?? 0) == 0</c>
        ///   这是它自己的 bug：<c>int?</c> 相加时只要一边为 null，整体就是 null，
        ///   再过 <c>?? 0</c> 就变成 0 —— 于是**明明配了 asmdef 也会误报**
        ///   "No hot update modules configured"。所以必须把它写成非 null 的空数组。
        ///
        /// ⚠ 坑二：不能在 asmdef 已经登记的情况下再往这里塞名字。
        ///   SettingsUtil.HotUpdateAssemblyNamesExcludePreserved 是**两份合并**：
        ///   先取 hotUpdateAssemblyDefinitions 里每个 asmdef 的 name，再把本数组
        ///   AddRange 进去。两处都写 → 热更程序集清单里出现**重名**。
        ///   所以只有 asmdef 加载失败时才用名字兜底。
        ///
        /// ⚠ 坑三：形态不止一种。版本之间出现过 string[] 与 List&lt;string&gt;。
        ///   数组定长、必须重建再 SetValue；List 可以直接改。判定 Array 要放前面，
        ///   因为数组也实现 IList。
        /// </remarks>
        private static void WriteAssemblyNameList(Report report, string tag, object settings,
            FieldInfo field, bool addName)
        {
            object value = field.GetValue(settings);
            Type declared = field.FieldType;

            if (value == null)
            {
                if (!addName)
                {
                    // asmdef 那条路已经成了，这里只需把它从 null 规范成空数组，躲开坑一
                    object empty = CreateEmptyCollection(declared);
                    if (empty != null)
                    {
                        field.SetValue(settings, empty);
                        report.results.Add($"{tag} -> hotUpdateAssemblies 原本是 null，"
                                           + $"已规范成空 {declared.Name}（躲开 HybridCLR 的 int? 相加误判）");
                    }
                    else
                    {
                        report.results.Add($"{tag} -> ⚠ hotUpdateAssemblies 是 null 且形态 {declared.Name} 造不出来，未改");
                    }
                    return;
                }
            }

            if (value is Array arr)
            {
                var names = new List<string>();
                foreach (object o in arr) names.Add(o as string);
                if (addName && !names.Contains(HotUpdateAssemblyName)) names.Add(HotUpdateAssemblyName);

                Type elem = arr.GetType().GetElementType() ?? typeof(string);
                Array fresh = Array.CreateInstance(elem, names.Count);
                for (int i = 0; i < names.Count; i++) fresh.SetValue(names[i], i);
                field.SetValue(settings, fresh);
                report.results.Add($"{tag} -> hotUpdateAssemblies 现为 {names.Count} 项"
                                   + (names.Count > 0 ? ": " + string.Join(", ", names) : "（空，仅用于躲开 null 误判）"));
                return;
            }

            if (value is System.Collections.IList list)
            {
                if (addName && !list.Cast<object>().Any(o => (o as string) == HotUpdateAssemblyName))
                {
                    list.Add(HotUpdateAssemblyName);
                }
                field.SetValue(settings, list);
                report.results.Add($"{tag} -> hotUpdateAssemblies 现为 {list.Count} 项");
                return;
            }

            report.results.Add($"{tag} -> ⚠ hotUpdateAssemblies 是 {declared.Name}，形态不认识，未改");
        }

        /// <summary>按声明类型造一个空的 string[] 或 List&lt;string&gt;；造不出来返回 null。</summary>
        private static object CreateEmptyCollection(Type type)
        {
            try
            {
                if (type.IsArray)
                {
                    Type elem = type.GetElementType();
                    if (elem != typeof(string)) return null;   // 只处理字符串集合，别的形态不猜
                    return Array.CreateInstance(elem, 0);
                }

                if (type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(List<>)
                    && type.GetGenericArguments()[0] == typeof(string))
                {
                    return Activator.CreateInstance(type);
                }
            }
            catch
            {
                // 造不出来就返回 null，由调用方报告
            }
            return null;
        }

        /// <remarks>
        /// HybridCLR 的设置是 ScriptableSingleton 变体，落盘靠它自己的 <c>static Save()</c>。
        /// 但同时调一次 SetDirty + AssetDatabase.SaveAssets() 更保险 —— 三个都做，
        /// 成本可忽略。
        /// </remarks>
        private static void SaveSettings(Report report, string tag, object settings, Type settingsType)
        {
            if (settings is UnityEngine.Object unityObj) EditorUtility.SetDirty(unityObj);

            var save = settingsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 0);

            if (save != null)
            {
                try
                {
                    save.Invoke(null, null);
                    report.results.Add($"{tag} -> 已调用 static Save()");
                }
                catch (Exception e)
                {
                    report.results.Add($"{tag} -> ⚠ Save() 抛异常 {e.GetType().Name}: {e.Message}");
                }
            }
            else
            {
                report.results.Add($"{tag} -> ⚠ 没有无参静态 Save()，靠 AssetDatabase.SaveAssets() 落盘");
            }

            AssetDatabase.SaveAssets();
        }

        private static FieldInfo FindField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }

        // ====================================================================
        //  hybridclr.generate / hybridclr.outputs —— 代码生成与产物核查
        // ====================================================================

        /// <remarks>
        /// 直接触发菜单项 <c>HybridCLR/Generate/All</c>。
        ///
        /// 为什么不走反射调内部 API：生成逻辑散在 BuildProcessor 与 AOT 相关类里，
        /// 版本间签名不稳；而菜单项是**官方承诺稳定**的入口 —— HybridCLR 的文档
        /// 与构建流程都围绕这几个菜单项组织。
        ///
        /// ⚠ 它会往 HybridCLRGenerate/ 写 AOTGenericReferences.cs 与 link.xml，
        ///   这两份都是会被导入的资产，会触发重新编译 → 域重载 → 打断本方法。
        ///   所以报告必须先落盘（Run 已保证），执行结果只能靠 hybridclr.outputs
        ///   在下一轮核对。
        /// </remarks>
        private static void GenerateAll(Report report)
        {
            const string tag = "hybridclr.generate";
            const string menuPath = "HybridCLR/Generate/All";

            report.results.Add($"{tag} -> 即将执行菜单项 {menuPath}（耗时较长，且可能触发重编译）");
            try
            {
                bool ok = EditorApplication.ExecuteMenuItem(menuPath);
                report.results.Add($"{tag} -> ExecuteMenuItem 返回 {ok}"
                                   + (ok ? "" : "（false = 找不到该菜单项，确认 HybridCLR 包装好了）"));
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ❌ {e.GetType().Name}: {e.Message}");
            }

            // ⚠ 不能只看上面那句。实测：Generate/All 内部抛
            //   "Exception: GenerateStripedAOTDlls failed"（内部又包了
            //   "Error building Player: Currently selected scripting backend (IL2CPP) is not installed."）时，
            //   Unity 的菜单包装层会把它 Debug.LogException 掉并让 ExecuteMenuItem **照常返回 true** ——
            //   于是报告里只有「返回 True」，看起来像成功。
            //
            // 所以判据必须是**产物计数**：AssembliesPostIl2CppStrip 是 Generate/All 最后一步
            // （IL2CPP buildScriptsOnly 出包 + 裁剪）的产物，正常应有上百个 dll；为 0 就是失败。
            CheckGenerateProducts(report, tag);
        }

        /// <summary>
        /// Generate/All 之后的产物自检。判据只看磁盘，不看日志。
        /// </summary>
        private static void CheckGenerateProducts(Report report, string tag)
        {
            string root = Directory.GetParent(Application.dataPath).ToString();
            string target = EditorUserBuildSettings.activeBuildTarget.ToString();

            int hot = CountFiles(Path.Combine(root, "HybridCLRData/HotUpdateDlls", target), "*.dll");
            int strip = CountFiles(Path.Combine(root, "HybridCLRData/AssembliesPostIl2CppStrip", target), "*.dll");

            report.results.Add($"{tag} -> 热更 DLL：{hot} 个"
                               + (hot > 0 ? " ✅" : " ❌（第 1 步 CompileDll 没产出）"));
            report.results.Add($"{tag} -> 裁剪后的 AOT DLL：{strip} 个"
                               + (strip > 0
                                   ? " ✅"
                                   : " ❌ ← Generate/All **没跑完**。"
                                     + "这一步要靠一次 buildScriptsOnly 的 IL2CPP 出包，"
                                     + "常见原因是 IL2CPP 后端没装，"
                                     + "日志原文是 “Error building Player: Currently selected "
                                     + "scripting backend (IL2CPP) is not installed.”。"
                                     + "装模块的命令见 player.il2cpp"));
        }

        private static int CountFiles(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return 0;
            try { return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly).Length; }
            catch { return 0; }
        }

        /// <remarks>
        /// 检查「Windows 平台的 IL2CPP 后端到底装了没有」。
        ///
        /// 为什么单开一条命令：Unity 在 IL2CPP 缺失时**不会立刻说实话**。
        /// 实测暴露顺序是：
        ///   ① 玩家脚本编译先报一个看起来像「包不兼容」的错误
        ///      （com.unity.collections 的 NativeList.cs(839,24) CS7036 —— 真因见
        ///        <see cref="CollectionChecksDefine"/>：IL2CPP 缺失会让玩家编译
        ///        退回编辑器版 CoreModule 且不带 ENABLE_UNITY_COLLECTIONS_CHECKS）；
        ///   ② 直到真正出包时才吐真话：
        ///      “Error building Player: Currently selected scripting backend (IL2CPP) is not installed.”
        ///
        /// 这一点在 Unity Issue Tracker（ADDR-3250）里有官方记录：
        /// 同一个 CS7036，标注「仅在没有安装 IL2CPP 时复现；装上即解决」。
        ///
        /// 判据（全部只读）：
        ///   · PlayerSettings 里 Standalone 的后端是不是 IL2CPP
        ///   · BuildPipeline.IsBuildTargetSupported —— 只能说明平台支持在不在，**不能**说明 IL2CPP 在不在
        ///   · PlaybackEngines/&lt;平台&gt;/Variations/ 下有没有 il2cpp 变体目录 ← 这是最直接的文件级判据
        ///   · HybridCLR 自带的 LocalIl2CppData-WindowsEditor 在不在
        ///
        /// ⭐ 结论读法：只要 Variations 下**清一色是 mono**，就是没装
        ///   “Windows Build Support (IL2CPP)” 模块 —— 去 Unity Hub →
        ///   安装页 → 对应版本右侧「添加模块」→ 勾 Windows Build Support (IL2CPP)。
        ///   注意：**已装 Mono 版构建支持不等于装了 IL2CPP 版**，两者是独立模块。
        /// </remarks>
        private static void ProbeIl2CppSupport(Report report)
        {
            const string tag = "player.il2cpp";

            var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            report.results.Add($"{tag} -> Standalone 脚本后端 = {backend}"
                               + (backend == ScriptingImplementation.IL2CPP ? "（IL2CPP）" : "（非 IL2CPP）"));

            report.results.Add($"{tag} -> BuildPipeline.IsBuildTargetSupported(Standalone, {EditorUserBuildSettings.activeBuildTarget})"
                               + $" = {BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, EditorUserBuildSettings.activeBuildTarget)}"
                               + "（注意：这只代表平台支持在，不代表 IL2CPP 在）");

            string contents = EditorApplication.applicationContentsPath;
            string pe = Path.Combine(contents, "PlaybackEngines");
            report.results.Add($"{tag} -> PlaybackEngines = {pe.Replace('\\', '/')}");

            bool anyIl2Cpp = false;
            int variationCount = 0;
            try
            {
                foreach (var platform in Directory.GetDirectories(pe))
                {
                    string variations = Path.Combine(platform, "Variations");
                    if (!Directory.Exists(variations)) continue;
                    foreach (var v in Directory.GetDirectories(variations))
                    {
                        variationCount++;
                        string name = Path.GetFileName(v);
                        bool isIl2Cpp = name.IndexOf("il2cpp", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isIl2Cpp) anyIl2Cpp = true;
                        report.results.Add($"        {Path.GetFileName(platform)}/Variations/{name}"
                                           + (isIl2Cpp ? "   ← IL2CPP" : ""));
                    }
                }
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ⚠ 列 Variations 失败 {e.GetType().Name}");
            }

            report.results.Add($"{tag} -> Variations 共 {variationCount} 个"
                               + $"，其中 IL2CPP 变体 {(anyIl2Cpp ? "有 ✅" : "❌ 一个都没有")}");

            string local = Path.Combine(
                Directory.GetParent(Application.dataPath).ToString(),
                "HybridCLRData/LocalIl2CppData-WindowsEditor/il2cpp");
            report.results.Add($"{tag} -> HybridCLR 的本地 il2cpp 副本："
                               + (Directory.Exists(local) ? $"✅ {local.Replace('\\', '/')}" : "❌ 不存在"));

            report.results.Add($"{tag} -> ⭐ 判定："
                               + (anyIl2Cpp
                                   ? "找到了 IL2CPP 变体，后端应该可用"
                                   : "Variations 下清一色 mono ⇒ **没装 “Windows Build Support (IL2CPP)” 模块**。"
                                     + "去 Unity Hub → 安装 → 2022.3.62f3c1 右侧「添加模块」→ "
                                     + "勾选 Windows Build Support (IL2CPP) → 安装后重启编辑器。"
                                     + "（已装 Mono 版构建支持**不等于**装了 IL2CPP 版）"));
        }

        /// <remarks>
        /// 核查 HybridCLR 的产物是否真的落盘。
        /// 为什么必须有这条：安装器与生成器都用 Debug.Log 报「成功」，但日志会滚、
        /// 读控制台还要反射，而**磁盘状态是唯一不会骗人的证据**。
        /// </remarks>
        private static void ProbeHybridClrOutputs(Report report)
        {
            string root = Directory.GetParent(Application.dataPath).ToString();

            AddDir(report, root, "HybridCLRData/HotUpdateDlls", "热更 DLL 输出（Generate/All 生成）");
            // ⚠ 路径别写错：link.xml / AOTGenericReferences.cs 落在 **Assets/HybridCLRGenerate**，
            //   不是工程根的 HybridCLRGenerate。实测写成后者会误报「不存在」。
            AddDir(report, root, "Assets/HybridCLRGenerate", "生成的 AOT 引用与 link.xml");
            AddDir(report, root, "HybridCLRData/AssembliesPostIl2CppStrip", "AOT 裁剪后的 dll");
        }

        /// <remarks>
        /// 只跑 Generate/All 的第 1 步（编译热更 DLL），用来**快速验证「玩家编译能不能过」**。
        /// 全量 Generate/All 里第 4 步会做一次 buildScriptsOnly 的 IL2CPP 出包（分钟级），
        /// 排查阶段没必要每次都陪着跑。
        /// </remarks>
        private static void CompileHotUpdateDlls(Report report)
        {
            const string tag = "hybridclr.compile-dll";
            const string menuPath = "HybridCLR/CompileDll/ActiveBuildTarget";

            report.results.Add($"{tag} -> 当前活动构建目标 = {EditorUserBuildSettings.activeBuildTarget}，"
                               + $"development = {EditorUserBuildSettings.development}");
            report.results.Add($"{tag} -> 即将执行菜单项 {menuPath}（同步执行，可能要十几秒到一分钟）");
            try
            {
                bool ok = EditorApplication.ExecuteMenuItem(menuPath);
                report.results.Add($"{tag} -> ExecuteMenuItem 返回 {ok}"
                                   + (ok ? "" : "（false = 找不到该菜单项）"));
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ❌ {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
