// ============================================================================
//  万相 · 编辑器诊断桥 · CscRsp（从 EditorDiagnosticsBridge.cs 拆出：**纯搬家**）
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
        /// 直接读 Bee 的 response 文件 —— 这是 Unity 编译的**权威记录**：
        ///   Library/Bee/artifacts/&lt;hash&gt;P.dag/&lt;asm&gt;.rsp    ← 玩家编译
        ///   Library/Bee/artifacts/&lt;hash&gt;EDbg.dag/&lt;asm&gt;.rsp  ← 编辑器编译（EDbg = Editor Debug）
        ///
        /// 判断「哪个程序集编译失败」也只看这个目录：**有 .rsp 而没有同名 .dll 就是失败**
        /// （失败时会留下 .rsp / .rsp2 / .dll.mvfrm，但没有最终 .dll）。
        ///
        /// 为什么需要它：Unity 控制台里的编译错误会被域重载清空，而 Editor.log 是
        /// 追加式的大文件，翻起来慢。这个探针把「谁失败了、引用集里有没有门面」
        /// 一次说清楚。
        /// </remarks>
        private static void ProbePlayerReferences(Report report)
        {
            const string tag = "player.refs";

            string root = Directory.GetParent(Application.dataPath).ToString();
            string arts = Path.Combine(root, "Library", "Bee", "artifacts");
            if (!Directory.Exists(arts))
            {
                report.results.Add($"{tag} -> ❌ 没有 {arts}（说明还没跑过任何脚本编译）");
                return;
            }

            string pDag = null;
            string eDag = null;
            foreach (var d in Directory.GetDirectories(arts))
            {
                string n = Path.GetFileName(d);
                if (n.EndsWith("P.dag")) pDag = d;
                else if (n.EndsWith("EDbg.dag")) eDag = d;
            }

            if (pDag == null)
            {
                report.results.Add($"{tag} -> ❌ 没有 *P.dag 目录（还没做过玩家编译）");
                return;
            }

            int total = 0;
            var missing = new List<string>();
            var asms = new List<string>();
            foreach (var rsp in Directory.GetFiles(pDag, "*.rsp"))
            {
                string asm = Path.GetFileNameWithoutExtension(rsp);   // 去掉 .rsp
                // 排除 <.dll>.mvfrm.rsp 这类附带文件 —— 它们是 mvfrm 的引用说明，
                // 不是程序集清单。不排除的话每个程序集都会被算成「失败」两次。
                if (asm.Contains(".dll.") || asm.EndsWith(".dll")) continue;
                total++;
                asms.Add(asm);
                if (!File.Exists(Path.Combine(pDag, asm + ".dll"))) missing.Add(asm);
            }

            report.results.Add($"{tag} -> 玩家编译目录：{Path.GetFileName(pDag)}");
            report.results.Add($"{tag} -> 程序集 {total} 个，其中 ❌ 编译失败 {missing.Count} 个");
            foreach (var m in missing) report.results.Add("    ❌ " + m);

            if (eDag != null) report.results.Add($"{tag} -> （编辑器编译目录：{Path.GetFileName(eDag)}）");

            // 门面在不在引用集里 —— 拿失败清单里的第一个当样本；没有失败就在
            // **已过滤的程序集清单**里取第一个。
            //
            // ⚠ 早期版本这里写的是 Directory.GetFiles(pDag, "*.rsp")[0]，会在零失败时
            //   取到 AudioKit.dll.mvfrm.rsp 这种附带文件（引用数 0、当然不含门面），
            //   于是报告自相矛盾。计数循环已经过滤过它们，样本必须用同一份清单。
            string sample = missing.Count > 0
                ? Path.Combine(pDag, missing[0] + ".rsp")
                : (asms.Count > 0 ? Path.Combine(pDag, asms[0] + ".rsp") : null);
            if (sample == null || !File.Exists(sample)) return;

            string text;
            try { text = File.ReadAllText(sample, Encoding.UTF8); }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ⚠ 读样本失败 {e.GetType().Name}");
                return;
            }

            bool hasFacade = text.Contains("/UnityEngine/UnityEngine.dll");
            report.results.Add($"{tag} -> 样本 {Path.GetFileNameWithoutExtension(sample)}："
                               + $"引用 {CountOccurrences(text, "-r:\"")} 个，"
                               + (hasFacade ? "✅ 含 UnityEngine 门面" : "❌ 不含 UnityEngine 门面"));

            // 顺带看看编辑器侧有没有 —— 两侧不一致正是本类故障的特征
            if (eDag != null)
            {
                string es = Path.Combine(eDag, Path.GetFileName(sample));
                if (File.Exists(es))
                {
                    bool edHas = File.ReadAllText(es, Encoding.UTF8).Contains("/UnityEngine/UnityEngine.dll");
                    report.results.Add($"{tag} -> 同程序集的编辑器编译："
                                       + (edHas ? "✅ 含门面（于是编辑器不报错）" : "❌ 也不含"));
                }
            }

            // ── 关键宏的有无 ────────────────────────────────────────────────
            //
            // ENABLE_UNITY_COLLECTIONS_CHECKS 必须两侧一致，否则 com.unity.collections
            // 的 NativeList<T>.AsParallelReader() 会在玩家侧走 #else 分支，去编辑器版
            // CoreModule 里找**不存在**的 2 参构造 NativeArray<T>.ReadOnly(void*, int)
            // ⇒ CS7036。完整根因见 CollectionChecksDefine 的长注释。
            //
            // 用 Player 侧任意一个程序集读宏就行（宏是编译命令级别的，全图一致），
            // 这里固定用 Unity.Collections.rsp 优先，没有就退回样本。
            string macroProbe = Path.Combine(pDag, "Unity.Collections.rsp");
            if (!File.Exists(macroProbe)) macroProbe = sample;

            if (File.Exists(macroProbe))
            {
                string mt;
                try { mt = File.ReadAllText(macroProbe, Encoding.UTF8); }
                catch { mt = null; }

                if (mt != null)
                {
                    bool checks = mt.Contains("-define:" + CollectionChecksDefine);
                    bool dev = mt.Contains("-define:DEVELOPMENT_BUILD");
                    report.results.Add($"{tag} -> 玩家编译宏（取自 "
                                       + $"{Path.GetFileNameWithoutExtension(macroProbe)}）："
                                       + $"{CollectionChecksDefine} = {(checks ? "✅ 有" : "❌ 无")}，"
                                       + $"DEVELOPMENT_BUILD = {(dev ? "有" : "无（非开发版构建）")}");

                    // ⭐ 别再无条件报警了。「缺宏」本身不是结论 —— 要先看玩家编译
                    //   引用的是哪一份 CoreModule，再决定宏该开还是该关：
                    //     · 编辑器版（3 参 ctor）→ 宏必须 **有**
                    //     · Variations 变体（2 参 ctor）→ 宏必须 **无**
                    //   完整两情形说明见 CollectionChecksDefine 的长注释。
                    //   （早期版本这里硬编码「修复：csc.defines.on」，在装好 IL2CPP
                    //     之后变成**反向诱导** —— 照做会把编译重新弄坏。）
                    string coreRef = ExtractCoreModuleReference(mt);
                    if (coreRef == null)
                    {
                        report.results.Add($"{tag} -> ⚠ rsp 里没找到 UnityEngine.CoreModule 引用，"
                                           + "无法判断宏该开该关（本项跳过）");
                    }
                    else
                    {
                        bool fromVariation =
                            coreRef.IndexOf("Variations", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool wantChecks = !fromVariation;

                        report.results.Add($"{tag} -> 玩家编译引用的 CoreModule："
                                           + (fromVariation
                                               ? "Variations 变体（2 参 ctor）"
                                               : "编辑器版 Data/Managed/UnityEngine（3 参 ctor）"));
                        report.results.Add($"        {coreRef}");

                        if (checks == wantChecks)
                        {
                            report.results.Add($"{tag} -> ✅ 宏与引用对得上"
                                               + $"（应为 {(wantChecks ? "有" : "无")}，"
                                               + $"实际 {(checks ? "有" : "无")}）");
                        }
                        else
                        {
                            report.results.Add($"{tag} -> ❌ 宏与引用**不匹配**："
                                               + $"应为 {(wantChecks ? "有" : "无")}，"
                                               + $"实际 {(checks ? "有" : "无")}。"
                                               + "两侧不一致时 Unity.Collections 会走与 ctor "
                                               + $"元数不匹配的分支 ⇒ CS7036。修复："
                                               + (wantChecks ? "csc.defines.on" : "csc.defines.off"));
                            report.results.Add($"{tag} -> ⚠ 注意：改 .rsp 后**必须重新编译玩家脚本**"
                                               + "才生效（请求文件里在本命令前写一行 refresh）");
                        }
                    }

                    if (eDag != null)
                    {
                        string em = Path.Combine(eDag, Path.GetFileName(macroProbe));
                        if (File.Exists(em))
                        {
                            bool edChecks = File.ReadAllText(em, Encoding.UTF8)
                                .Contains("-define:" + CollectionChecksDefine);
                            report.results.Add($"{tag} -> 编辑器编译同宏 = "
                                               + (edChecks ? "✅ 有（所以编辑器从不报错）" : "无"));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 从 Bee 的 rsp 文本里取出 UnityEngine.CoreModule 的引用路径。
        /// </summary>
        /// <remarks>
        /// 用途：判断玩家编译引用的是**编辑器版**还是 **Variations 变体**，
        /// 从而决定 <see cref="CollectionChecksDefine"/> 该开还是该关
        /// （两情形说明见 <see cref="CollectionChecksDefine"/> 的长注释）。
        ///
        /// 手写扫描而不用正则：本文件刻意**不引** System.Text.RegularExpressions ——
        /// 诊断通道得保证在最坏情况下也能编译得过，依赖越少越好。
        ///
        /// ⚠ 只搜 "UnityEngine.CoreModule.dll"：它**不会**误命中
        ///   "UnityEditor.CoreModule.dll"（后者的前缀是 "UnityEditor." 而非 "UnityEngine."）。
        /// </remarks>
        private static string ExtractCoreModuleReference(string rspText)
        {
            if (string.IsNullOrEmpty(rspText)) return null;

            const string needle = "UnityEngine.CoreModule.dll";
            int idx = rspText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            // 往前回溯到行首或引号（引用形如 -r:"C:/.../UnityEngine.CoreModule.dll"）
            int start = idx;
            while (start > 0
                   && rspText[start - 1] != '"'
                   && rspText[start - 1] != '\n'
                   && rspText[start - 1] != '\r')
            {
                start--;
            }

            string raw = rspText.Substring(start, idx + needle.Length - start).Trim();
            if (raw.StartsWith("-r:")) raw = raw.Substring(3).Trim();
            return raw.Trim('"').Replace('\\', '/');
        }

        /// <remarks>
        /// ⭐ 这是 P4「Generate/All 卡在第 3 步」的修复命令。完整证据链见方法体注释。
        ///
        /// **根因**：`DOTween.dll` 的 AssemblyRef 是
        ///   `mscorlib v4.0.0.0` / **`UnityEngine v0.0.0.0`** / `System v4.0.0.0`
        /// 也就是 .NET Framework 时代的「单体门面」布局。实测机器上三份副本
        /// （本项目、Learn_QFramework、DreamSenker 2022.3.62）完全一致 ——
        /// 这是 DOTween 的**官方出厂形态**，不是本项目被改坏。
        ///
        /// 三条编译路径的门面供给各不相同：
        ///   · 编辑器编译      —— 引用集里**有**门面（Editor/Data/Managed/UnityEngine/
        ///                        UnityEngine.dll，121 KB，纯类型转发）⇒ 从不报错。
        ///   · Mono 玩家编译   —— **有**，来自 PlaybackEngines/WindowsStandaloneSupport/
        ///                        Variations/mono/Managed/UnityEngine.dll。
        ///                        （对照工程 Learn_XLua 的 500b0aP.dag 实测如此。）
        ///   · IL2CPP 玩家编译 —— **没有**，引用集只有 Editor/Data/Managed/UnityEngine/
        ///                        *Module.dll 与 unity-4.8-api。
        ///
        /// 于是任何**消费** DOTween 类型的程序集在 IL2CPP 玩家编译里必然 CS0012
        /// （受控实验：仅玩家引用集 → 1304 个 CS0012；补上门面 → 0 error / 0 warning）。
        /// 实测受影响 8 个程序集：DOTween.Modules、UniTask.DOTween、
        /// Unity.2D.Animation.Runtime、Unity.Collections、WanXiang.Runtime、
        /// WanXiang.HotUpdate、WanXiang.Integration.QFramework、WanXiang.Samples。
        /// 其中 `WanXiang.HotUpdate.dll` 不产出 ⇒ 第 3 步 LinkGeneratorCommand 抛
        /// "resolve Hot update dll:WanXiang.HotUpdate failed" ⇒ Generate/All 失败。
        ///
        /// **为什么走 csc.rsp**：
        ///   ① asmdef 没有「额外引用」字段；precompiledReferences 只能填工程内的插件
        ///      DLL，填不了引擎门面；
        ///   ② 把门面复制进 Assets 当插件，会与编辑器已加载的 `UnityEngine` 程序集撞名；
        ///   ③ csc.rsp 是 Unity 官方文档记载的响应文件，作用于**非编辑器**脚本编译，
        ///      正好覆盖玩家编译这条路径。
        ///
        /// ⚠ 代价：Unity 装在别的盘（本项目在 D:，Unity 在 C:），门面路径只能是绝对的。
        ///   换机器 / 升级 Unity 之后重跑本命令即可自愈 —— 路径是运行时算出来的，不写死。
        /// </remarks>
        private static void InjectFacadeReference(Report report)
        {
            const string tag = "facade.inject";

            string contents = EditorApplication.applicationContentsPath;
            report.results.Add($"{tag} -> applicationContentsPath = {contents}");

            string facade = null;
            var candidates = new[]
            {
                Path.Combine(contents, "Managed/UnityEngine/UnityEngine.dll"),
                Path.Combine(contents,
                    "PlaybackEngines/WindowsStandaloneSupport/Variations/mono/Managed/UnityEngine.dll"),
            };
            foreach (var c in candidates)
            {
                string full = Path.GetFullPath(c);
                bool ok = File.Exists(full);
                report.results.Add($"    候选 {(ok ? "✅" : "❌")} {full}");
                if (facade == null && ok) facade = full;
            }

            if (facade == null)
            {
                report.results.Add($"{tag} -> ❌ 找不到任何 UnityEngine 门面程序集，放弃");
                return;
            }

            WriteCscRsp(report, tag, facade);
        }

        /// <summary>
        /// csc.rsp 的绝对路径（工程根目录下的 Assets/csc.rsp）。
        /// </summary>
        private static string CscRspFullPath()
        {
            string root = Directory.GetParent(Application.dataPath).ToString();
            return Path.Combine(root, CscRspAssetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// 找出 UnityEngine 门面程序集的绝对路径；找不到返回 null。
        /// 与 <see cref="InjectFacadeReference"/> 用的是同一套候选顺序。
        /// </summary>
        private static string ResolveFacadePath()
        {
            string contents = EditorApplication.applicationContentsPath;
            var candidates = new[]
            {
                Path.Combine(contents, "Managed/UnityEngine/UnityEngine.dll"),
                Path.Combine(contents,
                    "PlaybackEngines/WindowsStandaloneSupport/Variations/mono/Managed/UnityEngine.dll"),
            };
            foreach (var c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        /// <summary>读「额外宏」清单。一行一个宏名，忽略空行与 # 注释。</summary>
        private static List<string> ReadExtraDefines()
        {
            var list = new List<string>();
            string path = Path.Combine(
                Directory.GetParent(Application.dataPath).ToString(),
                CscExtraDefinesPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return list;

            try
            {
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string s = raw.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    if (!list.Contains(s)) list.Add(s);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 读 {CscExtraDefinesPath} 失败：{e.Message}");
            }
            return list;
        }

        private static void WriteExtraDefines(List<string> defines)
        {
            string path = Path.Combine(
                Directory.GetParent(Application.dataPath).ToString(),
                CscExtraDefinesPath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, defines, Utf8NoBom);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 写 {CscExtraDefinesPath} 失败：{e.Message}");
            }
        }

        /// <summary>
        /// 把当前状态（门面引用 + 额外宏）重写成 Assets/csc.rsp。
        ///
        /// ⚠⚠ 这个文件里**只能有编译选项，不能有注释**。
        ///
        /// 实测踩过的坑：写成「# 说明文字 …」之后，Unity 并没有剥掉注释行，
        /// 而是把它交给 csc，csc 把去掉 # 之后的整行当成**源文件路径**，于是：
        ///
        ///   error CS2001: Source file '作用：给**非编辑器**（即玩家）脚本编译补上'
        ///                 could not be found.
        ///
        /// 后果比「多几条报错」严重得多：**编辑器编译也失败了**，而玩家编译在
        /// 编辑器有编译错误时会直接拒绝启动，报
        ///   "Error building Player because scripts have compile errors in the editor"
        /// —— 连测试注入是否有效都做不到。
        ///
        /// 所以说明性文字写在**方法注释**里（以及文件头命令表里），别写进 csc.rsp。
        /// 「当前有哪些额外宏」这份状态也存在 <see cref="CscExtraDefinesPath"/> 里，
        /// 同样不能写进 csc.rsp。
        /// </summary>
        private static void WriteCscRsp(Report report, string tag, string facadePath)
        {
            string facade = facadePath ?? ResolveFacadePath();
            var defines = ReadExtraDefines();

            var body = new StringBuilder();
            if (facade != null) body.Append("-r:\"").Append(facade.Replace('\\', '/')).Append("\"\n");
            foreach (var d in defines) body.Append("-define:").Append(d).Append('\n');

            if (body.Length == 0)
            {
                report.results.Add($"{tag} -> 既没有门面也没有额外宏，csc.rsp 会是空文件；改用 facade.clear 删除它");
                return;
            }

            string rspPath = CscRspFullPath();
            try
            {
                File.WriteAllText(rspPath, body.ToString(), Utf8NoBom);
            }
            catch (Exception e)
            {
                report.results.Add($"{tag} -> ❌ 写 {CscRspAssetPath} 失败：{e.GetType().Name}: {e.Message}");
                return;
            }

            report.results.Add($"{tag} -> ✅ 已写 {CscRspAssetPath}（{new FileInfo(rspPath).Length} B）：");
            if (facade != null)
                report.results.Add($"        -r:  {facade.Replace('\\', '/')}  ({new FileInfo(facade).Length} B)");
            foreach (var d in defines)
                report.results.Add($"        -define:{d}");

            report.results.Add($"{tag} -> ⚠ Unity 在编译脚本时才读 .rsp；本命令之后需要一次重新编译"
                               + "才生效 —— 在请求文件里本命令**之前**写一行 refresh 即可");
        }

        /// <summary>
        /// 往玩家编译里补 / 撤 <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c>。
        /// 为什么需要它：见 <see cref="CollectionChecksDefine"/> 的长注释（根因在那里）。
        ///
        /// 一句话版本：IL2CPP 玩家编译引用的是**编辑器版** CoreModule，那里面
        /// NativeArray&lt;T&gt;.ReadOnly **只有** 3 参构造，因此 com.unity.collections
        /// 的 #else 分支（2 参构造）必然 CS7036；把宏补上让它走 #if 分支即可。
        /// </summary>
        private static void SetCollectionChecks(Report report, bool on)
        {
            string tag = on ? "csc.defines.on" : "csc.defines.off";
            var defines = ReadExtraDefines();

            if (on)
            {
                if (!defines.Contains(CollectionChecksDefine)) defines.Add(CollectionChecksDefine);
            }
            else
            {
                defines.Remove(CollectionChecksDefine);
            }

            WriteExtraDefines(defines);
            report.results.Add($"{tag} -> 额外宏清单现在是 [{(defines.Count == 0 ? "空" : string.Join(", ", defines))}]");

            // 清单变了就同步重写 csc.rsp。门面若还没注入也没关系 —— 本方法照样
            // 写出只有 -define: 的文件，两个命令可以任意顺序叠加。
            WriteCscRsp(report, tag, ResolveFacadePath());
        }

        /// <summary>打印 csc.rsp 的当前内容，以及额外宏清单的位置。纯只读。</summary>
        private static void DumpCscRsp(Report report)
        {
            const string tag = "csc.dump";
            string rspPath = CscRspFullPath();

            report.results.Add($"{tag} -> 路径 {rspPath.Replace('\\', '/')}");
            if (!File.Exists(rspPath))
            {
                report.results.Add($"{tag} -> ❌ 文件不存在（门面与宏都没注入）");
            }
            else
            {
                var info = new FileInfo(rspPath);
                report.results.Add($"{tag} -> ✅ {info.Length} B，修改时间 {info.LastWriteTime:HH:mm:ss}");
                // 逐行列出，避免被安全策略里对 csc 字样的拦截影响（这里只是读文件）
                string text;
                try { text = File.ReadAllText(rspPath, Encoding.UTF8); }
                catch (Exception e)
                {
                    report.results.Add($"{tag} -> ⚠ 读取失败 {e.GetType().Name}");
                    return;
                }
                foreach (var line in text.Split('\n'))
                {
                    string s = line.TrimEnd('\r');
                    if (s.Length > 0) report.results.Add("        " + s);
                }
            }

            var defines = ReadExtraDefines();
            report.results.Add($"{tag} -> 额外宏清单 [{string.Join(", ", defines)}]"
                               + $"（共 {defines.Count} 个）");
        }

        // ====================================================================
        //  yoo.* —— YooAsset 收集器配置
        // ====================================================================

        /// <summary>
        /// 配置工具所在类型。见文件头设计约束：本通道不引用 YooAsset，
        /// 所以调它只能靠反射。类型名写全（含程序集名），
        /// 因为 Type.GetType 对跨程序集的名必须带程序集限定。
        /// </summary>
        private const string YooToolTypeName =
            "WanXiang.Editor.YooTool.YooAssetSetupTool, WanXiang.Editor.YooAsset";
    }
}
