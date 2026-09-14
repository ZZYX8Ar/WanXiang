// ============================================================================
//  万相 · AOT 泛型元数据加载器（AOT 侧）
//  ---------------------------------------------------------------------------
//  职责：把「一份 AOT 程序集名清单」变成「虚拟机已经装上这些元数据」。
//
//  它和 HotUpdateLoader 是一对：
//      HotUpdateLoader   —— 字节 → 可调用的业务入口（加载**热更**程序集）
//      AOTMetadataLoader —— 字节 → 虚拟机可用元数据（补齐**AOT**程序集的泛型实例化）
//
//  两者都**不知道字节从哪来**，都从 IResourceService 取。分层理由同 HotUpdateLoader。
//
//  ---------------------------------------------------------------------------
//  ⚠⚠ 编辑器下这个方法**是空实现**，永远返回 OK —— 不要把编辑器的"成功"当证据
//
//  HybridCLR 的 `RuntimeApi.LoadMetadataForAOTAssembly` 长这样
//  （`Runtime/RuntimeApi.cs`）：
//
//      #if UNITY_EDITOR
//      public static unsafe LoadImageErrorCode LoadMetadataForAOTAssembly(
//          byte[] dllBytes, HomologousImageMode mode)
//      {
//          return LoadImageErrorCode.OK;      // ← 就这么一行
//      }
//      #else
//      [MethodImpl(MethodImplOptions.InternalCall)]
//      public static extern LoadImageErrorCode LoadMetadataForAOTAssembly(...);
//      #endif
//
//  原因很合理：编辑器跑的是 **Mono**，本来就不缺泛型实现（JIT 现编），
//  补元数据这个动作在编辑器里毫无意义。
//
//  但这带来一个**很坏的性质**：编辑器里无论传什么字节进去都返回 OK。
//      · 传空字节 → OK
//      · 传根本不是 DLL 的垃圾 → OK
//      · 传错程序集的 DLL → OK
//  ⇒ **编辑器里的"补元数据成功"完全不能作为链路正确的证据。**
//    真正的验证只能在玩家包（IL2CPP）里做。
//
//  所以本类的报告里专门有一个 `EditorNoOp` 标记，并且摘要文字会**明说**
//  「编辑器下是空跑」。目的就是不让后来的人（包括未来的我）看到
//  "1/1 成功" 就以为这条链路验证过了。
//
//  ---------------------------------------------------------------------------
//  ⚠ 补充元数据只对 **AOT 程序集** 有效
//
//  对热更程序集的 DLL 调这个接口会返回 `HOMOLOGOUS_ONLY_SUPPORT_AOT_ASSEMBLY`。
//  热更程序集本来就是解释执行，不需要补 —— 它的代码是运行期新读进来的，
//  压根没经过 AOT 裁剪这一步。
//
//  ⚠ 同一个程序集**不能补两次**
//
//  第二次会返回 `HOMOLOGOUS_ASSEMBLY_HAS_LOADED`。
//  所以本类对重复项会去重，且报告里把这种情况单独列出来
//  （它在启动流程只跑一次的场景下不该出现；出现了说明有人重复初始化）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using WanXiang.Framework.ResourceSystem;

namespace WanXiang.Framework.HotUpdate
{
    /// <summary>
    /// 一次补元数据的结果。
    /// </summary>
    public sealed class AOTMetadataReport
    {
        /// <summary>补成功的程序集名。</summary>
        public readonly List<string> Loaded = new List<string>();

        /// <summary>资源系统里找不到 DLL 的程序集名。</summary>
        public readonly List<string> Missing = new List<string>();

        /// <summary>找到了但加载失败的程序集名（附原因）。</summary>
        public readonly List<string> Failed = new List<string>();

        /// <summary>被跳过的程序集名（重复项 / 空名）。</summary>
        public readonly List<string> Skipped = new List<string>();

        /// <summary>请求的总程序集数（去重后）。</summary>
        public int Requested { get; internal set; }

        /// <summary>是否编辑器下的空跑（调用是空实现，返回 OK 但什么也没做）。</summary>
        public bool EditorNoOp { get; internal set; }

        /// <summary>失败清单为空即算通过。</summary>
        public bool IsOk => Missing.Count == 0 && Failed.Count == 0;

        /// <summary>一行摘要，直接可进日志 / 报告。</summary>
        public string Summary
        {
            get
            {
                if (Requested == 0)
                {
                    // 这不是"通过"，而是"无从判断"。措辞上必须区分开，
                    // 否则报告里会出现一个看起来全绿的 0/0。
                    return "补元数据：清单为空（0 个）—— 当前热更代码没有「AOT 泛型 + 值类型实参」需求。\n"
                         + "  这本身不是错误；但意味着本链路本次**没有被验证**。\n"
                         + "  若怀疑分析漏了，先确认热更侧确实调了 AOTMetadataProbe.RunAll()，"
                         + "再重跑「HybridCLR/Generate/AOTGenericReference」。";
                }

                var sb = new StringBuilder();
                sb.Append("补元数据：").Append(Loaded.Count).Append('/').Append(Requested).Append(" 成功");

                if (Skipped.Count > 0)
                {
                    sb.Append("，").Append(Skipped.Count).Append(" 跳过");
                }
                if (Missing.Count > 0)
                {
                    sb.Append("，").Append(Missing.Count).Append(" 缺资源");
                }
                if (Failed.Count > 0)
                {
                    sb.Append("，").Append(Failed.Count).Append(" 失败");
                }

                if (EditorNoOp)
                {
                    sb.Append("\n  ⚠ 编辑器下 LoadMetadataForAOTAssembly 是空实现，"
                            + "上面的「成功」**不能证明链路正确** —— 必须到 IL2CPP 玩家包里复验。");
                }

                if (Missing.Count > 0)
                {
                    sb.Append("\n  缺的资源：").Append(string.Join("、", Missing));
                    sb.Append("\n  ⇒ 这些 AOT DLL 没被发布到资源目录。"
                            + "跑「万相/热更/发布热更产物」看看它有没有报「没找到 strip 产物」。");
                }

                if (Failed.Count > 0)
                {
                    sb.Append("\n  失败明细：\n    ").Append(string.Join("\n    ", Failed));
                }

                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// 按 AOT 程序集名清单补齐泛型元数据。
    /// </summary>
    public static class AOTMetadataLoader
    {
        /// <summary>
        /// 依次加载并补元数据。
        /// </summary>
        /// <param name="resource">资源服务。从它取 DLL 字节。</param>
        /// <param name="assemblyNames">
        /// AOT 程序集名清单，通常来自 <c>AOTGenericReferences.PatchedAOTAssemblyList</c>。
        /// </param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="log">逐条日志回调（可为 null）。传了才会打详细日志。</param>
        /// <returns>报告。**不抛异常** —— 单个程序集失败会记进报告继续往下走。</returns>
        /// <remarks>
        /// 单个失败继续往下走是刻意的：补元数据是"能补多少补多少"，
        /// 少一个程序集的元数据只影响用到它的那部分泛型，
        /// 不该让整个启动流程挂掉 —— 那样反而连"哪几个补上了"都看不到。
        /// 真正的错误由报告汇总呈现。
        /// </remarks>
        public static async UniTask<AOTMetadataReport> LoadAsync(
            IResourceService resource,
            IReadOnlyList<string> assemblyNames,
            CancellationToken ct = default,
            Action<string> log = null)
        {
            var report = new AOTMetadataReport
            {
                // 编辑器下这次调用是空跑 —— 先把标记做上，摘要会据此改措辞。
                EditorNoOp = Application.isEditor,
            };

            if (resource == null)
            {
                report.Failed.Add("（资源服务为 null，无法补元数据）");
                return report;
            }

            // 去重 + 去掉空项。同一个程序集补两次会拿到
            // HOMOLOGOUS_ASSEMBLY_HAS_LOADED，不是致命错但说明调用方有问题。
            var unique = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (assemblyNames != null)
            {
                for (int i = 0; i < assemblyNames.Count; i++)
                {
                    string name = assemblyNames[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }
                    if (!seen.Add(name))
                    {
                        report.Skipped.Add(name + "（重复）");
                        continue;
                    }
                    unique.Add(name);
                }
            }

            report.Requested = unique.Count;

            if (unique.Count == 0)
            {
                log?.Invoke("[热更] 补元数据清单为空，跳过。"
                            + "（正常：当前热更代码没有 AOT 泛型 + 值类型实参的需求）");
                return report;
            }

            log?.Invoke($"[热更] 开始补 AOT 泛型元数据，共 {unique.Count} 个程序集："
                        + string.Join("、", unique));

            for (int i = 0; i < unique.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                string name = unique[i];

                // ① 先解析 location —— 用候选列表试，避免把 location 写法赌死在常量里。
                string[] candidates = HotUpdateLocations.CandidatesForAotDll(name);
                bool resolved = HotUpdateLocations.TryResolve(
                    resource, candidates, out string location, out string tried);

                if (!resolved)
                {
                    report.Missing.Add(name);
                    log?.Invoke($"[热更] ❌ 资源系统里找不到 {name} 的 AOT DLL。试过：\n{tried}");
                    continue;
                }

                // ② 取字节。RawFile 路径不做引用计数，读完即释放，无需 ReleaseAsset。
                byte[] bytes;
                try
                {
                    bytes = await resource.LoadBytesAsync(location, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.Failed.Add($"{name}：读取字节异常 {ex.GetType().Name}: {ex.Message}");
                    log?.Invoke($"[热更] ❌ {name} 读取字节异常：{ex.Message}");
                    continue;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    report.Failed.Add($"{name}：字节为空（location={location}）");
                    log?.Invoke($"[热更] ❌ {name} 字节为空");
                    continue;
                }

                // ③ 补元数据。⚠ 编辑器下这是空实现，返回码没有参考价值。
                //
                //    为什么用 SuperSet 而不是 Consistent？
                //    · Consistent：要求 DLL 与 AOT 侧的 image **严格同源**
                //      （同一个 Unity 版本、同一份源码编译出来的）。不满足会报
                //      UNMATCH_FORMAT_VARIANT / UNSUPPORT_FORMAT_VERSION。
                //    · SuperSet：允许 DLL 是 AOT image 的**超集**，
                //      多出来的类型/方法被忽略，只取需要的。
                //    热更 DLL 是"改了源码后重新编译"的产物，本来就是超集关系，
                //    所以 SuperSet 是唯一实用的选择。
                LoadImageErrorCode code;
                try
                {
                    code = RuntimeApi.LoadMetadataForAOTAssembly(
                        bytes, HomologousImageMode.SuperSet);
                }
                catch (Exception ex)
                {
                    report.Failed.Add($"{name}：调用补元数据接口抛异常 "
                                      + $"{ex.GetType().Name}: {ex.Message}");
                    log?.Invoke($"[热更] ❌ {name} 补元数据抛异常：{ex.Message}");
                    continue;
                }

                if (code == LoadImageErrorCode.OK)
                {
                    report.Loaded.Add(name);
                    log?.Invoke($"[热更] ✅ {name} 补元数据 OK"
                                + $"（{bytes.Length / 1024} KB，location={location}）");
                }
                else
                {
                    report.Failed.Add($"{name}：{DescribeErrorCode(code)}");
                    log?.Invoke($"[热更] ❌ {name} 补元数据失败：{DescribeErrorCode(code)}");
                }
            }

            log?.Invoke("[热更] " + report.Summary.Replace("\n", "\n[热更] "));
            return report;
        }

        /// <summary>
        /// 从资源系统读 AOT 元数据清单。
        /// </summary>
        /// <param name="resource">资源服务。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="log">日志回调（可为 null）。</param>
        /// <returns>
        /// 程序集名列表；**清单不存在**时返回 null（区别于"存在但为空"）。
        /// </returns>
        /// <remarks>
        /// 名单由编辑器发布工具从 <c>AOTGenericReferences.cs</c> 抄出来写成数据文件，
        /// 与 AOT DLL 同目录发布。为什么不让运行期直接读那个 .cs ——
        /// 见 <see cref="HotUpdateLocations.AotManifestName"/> 的说明。
        ///
        /// 文件格式刻意做得极简（一行一个名字，<c>#</c> 开头是注释）：
        /// 这份文件是给人看的诊断材料，不该需要任何解析器知识才能读懂。
        /// </remarks>
        public static async UniTask<List<string>> ReadManifestAsync(
            IResourceService resource,
            CancellationToken ct = default,
            Action<string> log = null)
        {
            if (resource == null)
            {
                return null;
            }

            bool found = HotUpdateLocations.TryResolve(
                resource,
                HotUpdateLocations.CandidatesForAotManifest(),
                out string location,
                out string tried);

            if (!found)
            {
                log?.Invoke("[热更] 资源系统里没有 AOT 元数据清单。试过：\n" + tried
                            + "\n  ⇒ 若热更代码确实需要补元数据，说明还没发布过，"
                            + "跑「万相/热更/发布热更产物」。");
                return null;
            }

            string text;
            try
            {
                text = await resource.LoadTextAsync(location, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[热更] ❌ 读 AOT 清单「{location}」异常：{ex.Message}");
                return null;
            }

            var list = ParseManifest(text);
            log?.Invoke($"[热更] AOT 清单「{location}」解析出 {list.Count} 个程序集名。");
            return list;
        }

        /// <summary>
        /// 解析清单文本。空行与 <c>#</c> 开头的行忽略。
        /// </summary>
        /// <param name="text">清单文本，可为 null。</param>
        public static List<string> ParseManifest(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return list;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                // 容错：允许有人手写时把 .dll 后缀带上（HybridCLR 自己写的是纯程序集名）。
                if (line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    line = line.Substring(0, line.Length - 4);
                }

                if (line.Length > 0 && seen.Add(line))
                {
                    list.Add(line);
                }
            }

            return list;
        }

        /// <summary>
        /// 把 <see cref="LoadImageErrorCode"/> 翻成人话 + 排查方向。
        /// </summary>
        /// <param name="code">接口返回码。</param>
        public static string DescribeErrorCode(LoadImageErrorCode code)
        {
            switch (code)
            {
                case LoadImageErrorCode.OK:
                    return "OK";

                case LoadImageErrorCode.BAD_IMAGE:
                    return "BAD_IMAGE —— 字节流不是有效的 PE/DLL。"
                         + "多半是复制产物时被中间层改写过（比如编辑器把 .dll 当文本资产导入过，"
                         + "或资源下载被截断）。";

                case LoadImageErrorCode.NOT_IMPLEMENT:
                    return "NOT_IMPLEMENT —— HybridCLR 没安装或运行期被裁掉了。"
                         + "真机包上跑这个错，说明打包时 HybridCLR 环境没装上。";

                case LoadImageErrorCode.AOT_ASSEMBLY_NOT_FIND:
                    return "AOT_ASSEMBLY_NOT_FIND —— 虚拟机里没有同名 AOT 程序集。"
                         + "通常是程序集名写错了（给了热更程序集名，或少了 .dll 之外的差异），"
                         + "也可能是这份 AOT 程序集压根没进包。";

                case LoadImageErrorCode.HOMOLOGOUS_ONLY_SUPPORT_AOT_ASSEMBLY:
                    return "HOMOLOGOUS_ONLY_SUPPORT_AOT_ASSEMBLY —— 这是**热更程序集**，不是 AOT 程序集。"
                         + "热更程序集不需要也不能补元数据（它本来就是解释执行的）。"
                         + "检查清单是不是误把 WanXiang.HotUpdate 之类的名字写进去了。";

                case LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED:
                    return "HOMOLOGOUS_ASSEMBLY_HAS_LOADED —— 同一个程序集补过两次了。"
                         + "本类已经去重，出现这个说明流程被重复执行过"
                         + "（例如场景里挂了两个热更 Bootstrap）。";

                case LoadImageErrorCode.INVALID_HOMOLOGOUS_MODE:
                    return "INVALID_HOMOLOGOUS_MODE —— 同源模式参数非法。"
                         + "本工程固定用 SuperSet，出现这个基本只可能是 HybridCLR 版本不匹配。";

                case LoadImageErrorCode.PDB_BAD_FILE:
                    return "PDB_BAD_FILE —— 附带的 pdb 无效。"
                         + "发布脚本只该复制 .dll，不该把 .pdb 一起塞进去。";

                case LoadImageErrorCode.UNKNOWN_IMAGE_FORMAT:
                    return "UNKNOWN_IMAGE_FORMAT —— 认不出镜像格式。"
                         + "常见于把 WebGL/Android 的产物混进了 Windows 包。";

                case LoadImageErrorCode.UNSUPPORT_FORMAT_VERSION:
                    return "UNSUPPORT_FORMAT_VERSION —— 产物格式版本对不上。"
                         + "多半是 AOT DLL 来自**另一次出包**（例如本次包用旧 strip 产物）。"
                         + "解法：重新跑一次 HybridCLR/Generate/All。";

                case LoadImageErrorCode.UNMATCH_FORMAT_VARIANT:
                    return "UNMATCH_FORMAT_VARIANT —— 产物与 AOT 镜像的「变体」不一致。"
                         + "典型原因是用了 Consistent 模式；本工程已固定 SuperSet。"
                         + "若仍出现，检查 strip 产物是不是当前平台的。";

                default:
                    return $"未知返回码 {(int)code}（{code}）—— "
                         + "HybridCLR 版本可能比本文件的 switch 新，去 Runtime/LoadImageErrorCode.cs 对一下。";
            }
        }
    }
}
