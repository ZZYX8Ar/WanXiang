// ============================================================================
//  万相 · 编辑器诊断桥 · Probes（从 EditorDiagnosticsBridge.cs 拆出：**纯搬家**）
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

        private const string InputAssetPath = "Assets/ArtRes/Input/WanXiang.inputactions";

        /// <remarks>
        /// activeInputHandler 没有公开 C# API，只能从 ProjectSettings.asset 上
        /// 用 SerializedObject 掏。用 DefaultAssets 路径而不是绝对路径，
        /// 这样换机器也能跑。
        /// </remarks>
        private static int ReadInputHandlerRaw()
        {
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (assets == null || assets.Length == 0) return -1;

                var so = new SerializedObject(assets[0]);
                var prop = so.FindProperty("activeInputHandler");
                return prop != null ? prop.intValue : -1;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 读取 activeInputHandler 失败：{e.Message}");
                return -1;
            }
        }

        private static string DescribeInputHandler(int raw)
        {
            switch (raw)
            {
                case 0: return "0 = Old（旧 Input Manager）。新 Input System 完全没启用。";
                case 1: return "1 = New（仅新系统）。⚠ 会打死依赖旧 Input API 的存量代码，"
                             + "且是**运行期**抛 InvalidOperationException，编译期看不出来。";
                case 2: return "2 = Both（两者都启用）。本工程要求的取值。";
                default: return $"{raw} = 读取失败（属性名变了？）";
            }
        }

        // ====================================================================
        //  types —— 关键类型落在哪个程序集
        // ====================================================================

        /// <remarks>
        /// 用反射而不是直接引用：本文件在 WanXiang.Editor 里，而它没有引用
        /// Integration 层（也不该引用 —— 那是运行时代码）。反射既避免加引用，
        /// 又能在类型缺失时给出「没找到」而不是编译错误。
        /// </remarks>
        /// <remarks>
        /// 关注这些「类型简名」。刻意**只写简名、不写命名空间** ——
        /// 之前写全名踩过坑：工程约定是 `&lt;程序集名&gt;.&lt;目录路径&gt;`，
        /// 所以 `Framework/Inputs/` 下的东西是 `WanXiang.Framework.Inputs` 而不是
        /// `WanXiang.Inputs`，八个类型里七个「未找到」，看起来像程序集没加载，
        /// 实则只是我猜错了前缀。
        ///
        /// 按简名搜还有一个好处：能发现重名 —— 同一个简名在多个程序集里出现，
        /// 往往意味着代码被复制粘贴了，是分层被破坏的信号。
        /// </remarks>
        private static readonly string[] WatchedTypeSimpleNames =
        {
            // 输入层
            "IInputService", "InputService", "InputContext", "InputMapUtil",
            "InputBootstrap", "InputServiceUtility", "InputContextChangedEvent",
            // 框架其他层
            "UISystem", "UIPanelBase", "UIBootstrap", "SaveSystem",
    // 编辑器工具
    "InputAssetGenerator", "EditorDiagnosticsBridge",
    // 热更边界：这三个必须分处两侧 —— IHotUpdateEntry/HotUpdateLoader 在 AOT 的
    // WanXiang.Runtime，HotUpdateEntry 在热更的 WanXiang.HotUpdate。
    // 全都按简名搜，正好能暴露「同一个类型名出现在两个程序集」这种分层事故。
    "IHotUpdateEntry", "HotUpdateLoader", "HotUpdateEntry",
};

        private static void ProbeTypes(Report report)
        {
            // 一次遍历建索引。逐个简名去遍历所有程序集是 O(n×m)，类型多时会明显卡。
            var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName;
                try { asmName = asm.GetName().Name; }
                catch { continue; }

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (Array.IndexOf(WatchedTypeSimpleNames, t.Name) < 0) continue;

                    if (!index.TryGetValue(t.Name, out var list))
                    {
                        list = new List<string>();
                        index[t.Name] = list;
                    }
                    list.Add($"{t.FullName} @ {asmName}");
                }
            }

            foreach (var simple in WatchedTypeSimpleNames)
            {
                if (!index.TryGetValue(simple, out var hits) || hits.Count == 0)
                {
                    report.types.Add($"{simple} -> ⚠ 未找到");
                }
                else if (hits.Count == 1)
                {
                    report.types.Add($"{simple} -> {hits[0]}");
                }
                else
                {
                    // 重名是要警觉的：多半是同一份代码被复制进了两个程序集
                    report.types.Add($"{simple} -> ⚠ 命中 {hits.Count} 处: {string.Join(" | ", hits)}");
                }
            }
        }

        // ====================================================================
        //  scene —— EventSystem 与模块
        // ====================================================================

        /// <remarks>
        /// 换到新输入后端后，uGUI 默认的 StandaloneInputModule 会失效，表现为
        /// 「UI 画得好好的，就是点不动」—— 没有任何报错，极难定位。
        /// 刻意不用 FindObjectOfType：它在较新版本已标记过时，而遍历场景根节点
        /// 是各版本都稳的写法。
        /// </remarks>
        private static void ProbeScene(Report report)
        {
            var scene = EditorSceneManager.GetActiveScene();
            report.sceneName = scene.name;
            report.sceneIsDirty = scene.isDirty;

            EventSystem found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var es = root.GetComponentInChildren<EventSystem>(true);
                if (es != null) { found = es; break; }
            }

            if (found == null)
            {
                report.eventSystemFound = false;
                report.notes.Add("当前场景（编辑模式）里没有 EventSystem。"
                                 + "如果由 InputBootstrap 在运行期创建，这属于预期；"
                                 + "但做 UI 时编辑模式下点不动任何控件 —— "
                                 + "建议在场景里常驻一个，并挂 InputSystemUIInputModule。");
                return;
            }

            report.eventSystemFound = true;
            report.eventSystemObject = found.gameObject.name;

            foreach (var module in found.GetComponents<BaseInputModule>())
            {
                string kind = module is InputSystemUIInputModule ? "✅ 新后端"
                            : module is StandaloneInputModule ? "❌ 旧后端（新后端下静默失效）"
                            : "❓ 其他";

                string extra = string.Empty;
                if (module is InputSystemUIInputModule newModule)
                {
                    extra = newModule.actionsAsset != null
                        ? " actionsAsset=" + newModule.actionsAsset.name
                        : " ⚠ actionsAsset 为空 —— UI 会点不动且不报错";
                }

                report.eventSystemModules.Add(
                    $"{module.GetType().Name}{extra}  enabled={module.enabled}  {kind}");
            }
        }

        // ====================================================================
        //  asmdefs —— 程序集清单（设计热更边界时要用）
        // ====================================================================

        /// <remarks>
        /// 刻意不走 AssetDatabase.FindAssets("t:AssemblyDefinitionAsset")。
        /// 原因：`AssemblyDefinitionAsset` 这个类型不在 UnityEditor 命名空间下
        /// （在 UnityEditorInternal 里），属于「凭记忆写必然写错」的那类 API；
        /// 而 asmdef 就是磁盘上一个纯 JSON 文本文件，直接读文件反而更稳、
        /// 更少依赖，也顺带把「哪些文件真的存在」这件事一并验证了。
        ///
        /// 这个坑是离线类型检查抓出来的：写完代码不去 Unity 里编译，
        /// 根本看不出错。
        /// </remarks>
        private static void ProbeAsmdefs(Report report)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(Application.dataPath, "*.asmdef",
                    SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                report.results.Add($"asmdefs -> 扫描失败 {e.GetType().Name}: {e.Message}");
                return;
            }

            var lines = new List<string>();
            foreach (var file in files)
            {
                // Windows 上通配符有时会连 .asmdef.meta 一起匹配回来，显式挡一道。
                if (!file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)) continue;

                string json;
                try { json = File.ReadAllText(file, Encoding.UTF8); }
                catch { continue; }

                string name = ParseJsonString(json, "\"name\"");
                var refs = ParseJsonStringArray(json, "\"references\"");
                var platforms = ParseJsonStringArray(json, "\"includePlatforms\"");
                var constraints = ParseJsonStringArray(json, "\"defineConstraints\"");

                string plat = platforms.Count > 0 ? string.Join("/", platforms) : "全平台";
                lines.Add($"{name}  [{plat}]  引用 {refs.Count} 个");
                lines.Add($"    引用: {(refs.Count > 0 ? string.Join(", ", refs) : "(无)")}");
                if (constraints.Count > 0)
                    lines.Add($"    条件编译: {string.Join(", ", constraints)}");

                // 相对路径比绝对路径好读，也方便直接粘到 shell 里
                string relative = "Assets"
                                  + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                lines.Add($"    位置: {relative}");
            }

            report.asmdefs.AddRange(lines);
            report.results.Add($"asmdefs -> 扫到 {files.Length} 个 .asmdef 文件");
        }

        /// <summary>
        /// 从 JSON 文本里抠出一个字符串值（<c>"key": "value"</c> 这种）。
        /// 找不到返回空串。
        /// </summary>
        private static string ParseJsonString(string json, string key)
        {
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return string.Empty;

            int colon = json.IndexOf(':', keyIndex);
            if (colon < 0) return string.Empty;

            int firstQuote = json.IndexOf('"', colon + 1);
            if (firstQuote < 0) return string.Empty;

            int secondQuote = json.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return string.Empty;

            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }

        /// <summary>
        /// 从 JSON 文本里抠出某个 key 对应的字符串数组。
        /// 刻意不引 JSON 库：asmdef 结构固定且简单，字符串扫描足够；
        /// 关键这是编辑器工具，能少一个依赖就少一个。
        /// </summary>
        private static List<string> ParseJsonStringArray(string json, string key)
        {
            var result = new List<string>();
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0) return result;

            int start = json.IndexOf('[', keyIndex);
            if (start < 0) return result;
            int end = json.IndexOf(']', start);
            if (end < 0) return result;

            string body = json.Substring(start + 1, end - start - 1);
            foreach (var piece in body.Split(','))
            {
                string s = piece.Trim().Trim('"');
                if (s.Length > 0) result.Add(s);
            }
            return result;
        }

        // ====================================================================
        //  packages —— 指定包是否装上了
        // ====================================================================

        /// <remarks>
        /// 直接问 Unity 的包管理器，而不是去看 Packages/manifest.json ——
        /// manifest 里写了不等于解析成功（scopes 没配对、版本不存在、
        /// 或者被其他包降级，都会让 manifest 与真实状态不一致）。
        /// </remarks>
        private static void ProbePackages(Report report)
        {
            string[] wanted =
            {
                "com.tuyoogame.yooasset",
                "com.code-philosophy.hybridclr",
                "com.cysharp.unitask",
                "com.unity.inputsystem",
            };

            for (int i = 0; i < wanted.Length; i++)
            {
                string id = wanted[i];
                var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(id);
                if (info == null)
                {
                    report.packages.Add($"{id} -> ❌ 未解析");
                }
                else
                {
                    var src = info.source.ToString();
                    report.packages.Add($"{id} -> ✅ {info.version}  来源 {src}  于 {info.resolvedPath}");
                }
            }

            // 包解析失败时 Unity 会把错误记在控制台，顺手报出来
            report.results.Add("packages -> 另外检查控制台里有没有 'Package Manager' 相关报错");
        }

        // ====================================================================
        //  backend —— 脚本后端
        // ====================================================================

        private static void ProbeBackend(Report report)
        {
            foreach (BuildTargetGroup group in new[]
                     {
                         BuildTargetGroup.Standalone,
                         BuildTargetGroup.Android,
                         BuildTargetGroup.iOS,
                         BuildTargetGroup.WebGL,
                     })
            {
                try
                {
                    var impl = PlayerSettings.GetScriptingBackend(group);
                    report.backend.Add($"{group} -> {DescribeBackend(impl)}");
                }
                catch (Exception e)
                {
                    report.backend.Add($"{group} -> (读取失败 {e.GetType().Name})");
                }
            }

            report.backend.Add($"ApiCompatibilityLevel(Standalone) -> "
                               + PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone));
            report.backend.Add($"allowUnsafeCode -> {PlayerSettings.allowUnsafeCode}");
            report.backend.Add($"当前构建目标 -> {EditorUserBuildSettings.activeBuildTarget}");

            // ⭐ 平台模块装没装 —— 这是**权威判据**，不要靠翻 PlaybackEngines 目录猜。
            // 为什么这里特别重要：把脚本后端设成 IL2CPP 是**可以不装模块就设成功的**
            // （只是一个序列化字段），但真出包会失败。而 HybridCLR 的
            // HybridCLR/Generate/All 里包含 StripAOTDllCommand，它内部会做一次
            // buildScriptsOnly 的 IL2CPP 出包去生产裁剪后的 AOT dll ——
            // 模块没装，Generate/All 就会在半途炸掉。
            report.backend.Add("── 平台模块安装情况（IsBuildTargetSupported）──");
            foreach (var pair in new[]
                     {
                         Tuple.Create(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                         Tuple.Create(BuildTargetGroup.Android, BuildTarget.Android),
                         Tuple.Create(BuildTargetGroup.iOS, BuildTarget.iOS),
                         Tuple.Create(BuildTargetGroup.WebGL, BuildTarget.WebGL),
                     })
            {
                try
                {
                    bool ok = BuildPipeline.IsBuildTargetSupported(pair.Item1, pair.Item2);
                    report.backend.Add($"  {pair.Item2} -> {(ok ? "✅ 已安装" : "❌ 未安装（出包会失败）")}");
                }
                catch (Exception e)
                {
                    report.backend.Add($"  {pair.Item2} -> (查询失败 {e.GetType().Name})");
                }
            }
        }

        private static string DescribeBackend(ScriptingImplementation impl)
        {
            switch (impl)
            {
                case ScriptingImplementation.Mono2x: return "Mono2x ❌（HybridCLR 用不了，必须 IL2CPP）";
                case ScriptingImplementation.IL2CPP: return "IL2CPP ✅";
                default: return impl.ToString();
            }
        }

        /// <remarks>
        /// 注意 GetScriptingBackend 需要该平台的构建支持模块已安装，否则会抛。
        /// 所以上面每个平台都单独 try —— 一个平台缺模块不该让整份报告失败。
        /// </remarks>
        private static void SetIl2Cpp(Report report)
        {
            BuildTargetGroup[] groups =
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.WebGL,
            };

            foreach (var group in groups)
            {
                try
                {
                    var before = PlayerSettings.GetScriptingBackend(group);
                    if (before == ScriptingImplementation.IL2CPP)
                    {
                        report.results.Add($"{group} 已是 IL2CPP，跳过");
                        continue;
                    }

                    PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
                    var after = PlayerSettings.GetScriptingBackend(group);
                    report.results.Add($"{group}: {before} -> {after}");
                }
                catch (Exception e)
                {
                    report.results.Add($"{group}: ⚠ 设置失败 {e.GetType().Name}（多半是没装该平台的构建模块，可忽略）");
                }
            }

            AssetDatabase.SaveAssets();
            report.results.Add("已调 AssetDatabase.SaveAssets() 落盘到 ProjectSettings.asset");
        }

        /// <remarks>
        /// 把 Api Compatibility Level 切成 **.NET Framework 4.x**。
        ///
        /// 为什么非切不可（这条是实测证据链，不是推测）：
        ///   1. 反射读 Assets/Plugins/Demigiant/DOTween/DOTween.dll 的 AssemblyRef：
        ///        mscorlib    v4.0.0.0
        ///        UnityEngine v0.0.0.0    ← 门面程序集，不是 UnityEngine.CoreModule
        ///        System      v4.0.0.0
        ///      即它是按「.NET Framework + 老式 UnityEngine.dll 布局」编译出来的。
        ///   2. 本工程 apiCompatibilityLevel = .NET Standard。对比 Unity 自己落在
        ///      Library/Bee/artifacts/*/DOTween.Modules.rsp 的两份引用集：
        ///        编辑器编译 227 个引用，**含 UnityEngine.dll**
        ///        玩家编译 193 个引用，**不含 UnityEngine.dll**
        ///      于是「消费 DOTween 类型」的源码在编辑器里没事，一出包就炸。
        ///   3. 离线复现（把玩家 rsp 还原成 csproj 交给 dotnet）：
        ///        仅玩家引用集           → 1304 个 CS0012
        ///        玩家引用集 + 门面 dll  → 0 error / 0 warning
        ///
        ///   顺带这也是 HybridCLR 官方要求的设置（文档要 .NET 4.x / .NET Framework）。
        ///
        /// ⚠ 枚举成员名在 Unity 各版本里换过（NET_4_6 → NET_Unity_4_8），所以**按名字
        ///   模糊找**而不是直接写死一个常量 —— 写死一旦不存在就是编译错误，反而更糟。
        /// </remarks>
        private static void SetNetFramework(Report report)
        {
            const string tag = "backend.netframework";

            var enumType = typeof(ApiCompatibilityLevel);
            report.results.Add($"{tag} -> 可选值: {string.Join(", ", Enum.GetNames(enumType))}");

            ApiCompatibilityLevel? target = null;
            // 按优先级找，而不是按 Enum.GetNames 的声明顺序 —— 否则可能先撞上已过时的
            // NET_4_6（那是个老别名，Unity 会报 obsolete）。
            foreach (var hint in new[] { "4_8", "4_7", "Framework", "4_6" })
            {
                string found = null;
                foreach (var name in Enum.GetNames(enumType))
                {
                    if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) { found = name; break; }
                }
                if (found != null)
                {
                    target = (ApiCompatibilityLevel)Enum.Parse(enumType, found);
                    report.results.Add($"{tag} -> 选中 {found}（按 \"{hint}\" 匹配）");
                    break;
                }
            }

            if (target == null)
            {
                report.results.Add($"{tag} -> ❌ 枚举里找不到 .NET Framework 那一档，未改");
                return;
            }

            BuildTargetGroup[] groups =
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.WebGL,
            };

            foreach (var group in groups)
            {
                try
                {
                    var before = PlayerSettings.GetApiCompatibilityLevel(group);
                    if (before == target.Value)
                    {
                        report.results.Add($"{group} 已是 {before}，跳过");
                        continue;
                    }

                    PlayerSettings.SetApiCompatibilityLevel(group, target.Value);
                    report.results.Add($"{group}: {before} -> {PlayerSettings.GetApiCompatibilityLevel(group)}");
                }
                catch (Exception e)
                {
                    report.results.Add($"{group}: ⚠ 设置失败 {e.GetType().Name}（多半没装该平台构建模块，可忽略）");
                }
            }

            AssetDatabase.SaveAssets();
            report.results.Add($"{tag} -> 已落盘。⚠ 兼容级别变化会触发一次全量重编译 + 重导入，请耐心等");
        }

        // ====================================================================
        //  hybridclr —— 反射探 API 与设置
        // ====================================================================

        /// <remarks>
        /// 刻意不硬编码调用 HybridCLR 的方法名。它的编辑器 API 在版本间改过，
        /// 我凭记忆写很可能写错；而写错就是编译错误 —— 在「还没有别的验证手段」
        /// 的时候，编译错误代价极高。先枚举出真实存在的东西，再照着写。
        /// </remarks>
        private static void ProbeHybridClrApi(Report report)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name != null
                            && a.GetName().Name.StartsWith("HybridCLR", StringComparison.Ordinal))
                .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
                .ToList();

            if (assemblies.Count == 0)
            {
                report.hybridClrApi.Add("❌ 没有任何 HybridCLR.* 程序集被加载 —— 包没装上，"
                                        + "或者装了但没编译成功。先看 packages 命令的结果。");
                return;
            }

            report.hybridClrApi.Add("已加载的 HybridCLR 程序集：");
            foreach (var a in assemblies) report.hybridClrApi.Add("  " + a.GetName().Name);

            // 只关心名字里带这几个词的类型：装环境的、读设置的
            string[] interesting = { "Installer", "Settings", "BuildProcessor", "AOT" };

            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || !type.IsPublic && !type.IsNestedPublic) continue;
                    if (!interesting.Any(k => type.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    report.hybridClrApi.Add($"[{asm.GetName().Name}] {type.FullName}");

                    // 只列静态公开方法：装环境这类操作用静态方法居多
                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static
                                                      | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsSpecialName) continue;
                        report.hybridClrApi.Add("    static " + Signature(m));
                    }

                    // 构造函数也要看 —— 想用反射 new 一个出来就必须知道参数（或确认无参）
                    foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var cps = ctor.GetParameters()
                            .Select(p => p.ParameterType.Name + " " + p.Name);
                        report.hybridClrApi.Add($"    ctor({string.Join(", ", cps)})");
                    }

                    // 实例方法带上完整签名。只列名字是没用的 —— 反射调用前必须知道
                    // 参数个数与类型，靠猜就会调错。
                    var instSigs = type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                   | BindingFlags.DeclaredOnly)
                        .Where(m => !m.IsSpecialName)
                        .Select(Signature)
                        .Distinct()
                        .ToList();

                    const int instLimit = 30;
                    for (int i = 0; i < instSigs.Count && i < instLimit; i++)
                        report.hybridClrApi.Add("    " + instSigs[i]);
                    if (instSigs.Count > instLimit)
                        report.hybridClrApi.Add($"    ...（另有 {instSigs.Count - instLimit} 个实例方法未列出）");
                }
            }
        }
    }
}
