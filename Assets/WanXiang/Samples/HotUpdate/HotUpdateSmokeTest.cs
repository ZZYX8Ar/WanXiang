// ============================================================================
//  万相 · 热更链路体检
//  ---------------------------------------------------------------------------
//  与 ResourceSmokeTest 是同一套路数（见那个文件头的完整说明），
//  这里只讲**不一样的地方**：
//
//  ⚠ 本体检**不自建 ResourceBootstrap 之外的东西** —— 它自建 ResourceBootstrap，
//    因为热更链路的第一段就是"等资源就绪"，必须真的有一个资源服务在跑。
//    然后它自建 HotUpdateBootstrap，再 await 它跑完。
//
//  ⚠ 关键设计：体检**逐段读的是 AOT 侧暴露出来的状态**，而不是"看日志里有没有
//    某句话"。日志是给人看的，状态是给程序判定的；用日志判定会让体检
//    在日志措辞改动时静默失效。
//    读的状态：HotUpdateBootstrap.Stage / LastError / AotReport /
//              ResolvedDllLocation / HotUpdateLoader.LoadedAssembly
//
//  ⚠ 为什么结果写文件而不是只打日志：
//    同资源体检 —— 失败时日志里全是别的系统的红字，结构化文件才判得清。
//
//  ---------------------------------------------------------------------------
//  ⭐ 一条**只有编辑器才需要**、但很有价值的检查：程序集身份
//
//  编辑器里 `WanXiang.HotUpdate` 本来就已经被编译并加载进域了
//  （Library/ScriptAssemblies 下那一份）。于是"加载成功"这件事本身
//  **不能证明**我们真的把资源目录里那份 DLL 读进来用了 —— 万一是复用了
//  编辑器已加载的副本呢？
//
//  判定办法：`Assembly.Load(byte[])` 载入的程序集是**另一个实例**，
//  于是 `AppDomain.CurrentDomain.GetAssemblies()` 里会出现**两个**同名程序集，
//  且新载入那个的 `Assembly.Location` 是**空串**（它不对应磁盘上的路径）。
//
//  这两个特征一起看，才能说"确实是新字节被加载了"。
//  没有这一步，体检全绿也只能说明"有个 HotUpdateEntry 能用"，证明不了链路。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.Boot;
using WanXiang.Framework.HotUpdate;
using WanXiang.Framework.ResourceSystem;

namespace WanXiang.Framework.Samples.HotUpdate
{
    /// <summary>
    /// 热更链路冒烟测试。由请求文件触发，结果写入 Temp/WanXiangDiag/hot_smoke.txt。
    /// </summary>
    public sealed class HotUpdateSmokeTest : MonoBehaviour
    {
        /// <summary>请求文件（相对工程根）。存在才自我安装。</summary>
        public const string RequestRelativePath = "Temp/WanXiangDiag/hot_smoke.request";

        /// <summary>结果文件（相对工程根）。</summary>
        public const string ResultRelativePath = "Temp/WanXiangDiag/hot_smoke.txt";

        /// <summary>整体超时（秒）。超时也出报告。</summary>
        private const float TimeoutSeconds = 90f;

        private readonly List<string> _lines = new List<string>(64);
        private float _deadline;
        private float _startTime;
        private float _nextHeartbeat;
        private bool _finished;

        private const float HeartbeatInterval = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstallIfRequested()
        {
            try
            {
                if (!File.Exists(ToProjectPath(RequestRelativePath)))
                {
                    return;
                }

                var go = new GameObject("[HotUpdateSmokeTest]");
                DontDestroyOnLoad(go);
                go.AddComponent<HotUpdateSmokeTest>();

                Debug.Log("[热更体检] 检测到请求文件，已启动热更链路体检。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[热更体检] 自我安装失败：{ex}");
            }
        }

        private void Start()
        {
            DeleteRequestFile();

            _lines.Add("=== 万相 · 热更链路体检 ===");
            _lines.Add($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _lines.Add($"Unity：{Application.unityVersion}");
            _lines.Add($"平台：{Application.platform}，Play 模式：{Application.isPlaying}");
            _lines.Add("");

            Application.runInBackground = true;

            _deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            _startTime = Time.realtimeSinceStartup;
            _nextHeartbeat = _startTime + HeartbeatInterval;

            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            try
            {
                await RunChecksAsync();
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 体检本体抛异常：{ex.GetType().Name}: {ex.Message}");
                Debug.LogException(ex);
            }

            FinishAndWrite();
        }

        private void Update()
        {
            if (_finished) return;

            float now = Time.realtimeSinceStartup;
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + HeartbeatInterval;
                Debug.Log($"[热更体检] 心跳：已过 {now - _startTime:0.0} 秒，"
                          + $"第 {Time.frameCount} 帧，Play={Application.isPlaying}。"
                          + "（这行停了就说明游戏循环停了，不是热更没唤醒）");
            }

            if (now < _deadline) return;

            _lines.Add("");
            _lines.Add($"❌ 超时：{TimeoutSeconds} 秒内没跑完。");
            _lines.Add($"   当前阶段：{HotUpdateBootstrap.Stage}");
            _lines.Add($"   LastError：{HotUpdateBootstrap.LastError ?? "(无)"}");
            _lines.Add("   排查提示：");
            _lines.Add("     · 若上面**连一条心跳都没有** ⇒ 游戏循环停了，不是热更的问题；");
            _lines.Add("       本机编辑器不主动推进播放器循环，靠诊断通道的 KeepPlayModeAlive 代推。");
            _lines.Add("     · 若心跳正常但卡住 ⇒ 看 HotUpdateBootstrap.Stage 停在第几段。");

            FinishAndWrite();
        }

        // ==================================================================
        //  检查主体
        // ==================================================================

        private async UniTask RunChecksAsync()
        {
            // ---- 起资源服务 ----
            var resGo = new GameObject("[ResourceBootstrap]");
            resGo.AddComponent<ResourceBootstrap>();

            bool resReady = await ResourceBootstrap.WaitReadyAsync(this.GetCancellationTokenOnDestroy());
            IResourceService res = ResourceBootstrap.Resource;

            _lines.Add("--- ① 资源系统就绪（热更链路的前置条件） ---");
            _lines.Add(resReady
                ? $"✅ 资源就绪：包={res.PackageName}，模式={res.PlayMode}，版本={res.PackageVersion}"
                : $"❌ 资源未就绪：{res?.LastStage} / {res?.LastError}");

            if (!resReady || res == null)
            {
                _lines.Add("❌ 资源不通，热更必不通（热更 DLL 是当资源下发的）。跳过后面的检查。");
                return;
            }

            // ---- 起热更引导，等它跑完 ----
            var hotGo = new GameObject("[HotUpdateBootstrap]");
            hotGo.AddComponent<HotUpdateBootstrap>();

            bool hotReady = await HotUpdateBootstrap.WaitReadyAsync(
                this.GetCancellationTokenOnDestroy());

            _lines.Add("");
            _lines.Add("--- ② 热更五段流程结果 ---");
            _lines.Add($"阶段：{HotUpdateBootstrap.Stage}");
            _lines.Add(hotReady
                ? "✅ 五段流程全部走通"
                : "❌ 流程未走通");
            _lines.Add($"IsFinished={HotUpdateBootstrap.IsFinished}，IsReady={HotUpdateBootstrap.IsReady}");
            _lines.Add($"LastError：{HotUpdateBootstrap.LastError ?? "(无)"}");

            // ---- AOT 清单到底认了哪个 location ----
            _lines.Add("");
            _lines.Add("--- ③ AOT 元数据清单 ---");
            _lines.Add("候选 location（按优先级试，命中即停）：");
            _lines.Add("  " + string.Join("\n  ", HotUpdateLocations.CandidatesForAotManifest()));
            if (HotUpdateLocations.TryResolve(res, HotUpdateLocations.CandidatesForAotManifest(),
                    out string manifestHit, out _))
            {
                _lines.Add($"✅ 命中：{manifestHit}");
            }
            else
            {
                _lines.Add("⚠ 清单不存在 —— 要么还没发布过热更产物，要么当前确实不需要补元数据。");
            }

            // ---- 补元数据结果 ----
            _lines.Add("");
            _lines.Add("--- ④ AOT 泛型元数据补充 ---");
            AOTMetadataReport aot = HotUpdateBootstrap.AotReport;
            if (aot == null)
            {
                _lines.Add("○ 没跑（清单缺失，或 Inspector 关掉了 _loadAotMetadata）。");
                _lines.Add("   ⚠ 这条链路本次**未被验证**。");
            }
            else
            {
                _lines.Add(aot.IsOk ? "✅ 全部成功" : "❌ 有失败项");
                _lines.Add($"   请求 {aot.Requested} 个：成功 {aot.Loaded.Count}，"
                           + $"缺资源 {aot.Missing.Count}，失败 {aot.Failed.Count}，"
                           + $"跳过 {aot.Skipped.Count}");
                if (aot.Loaded.Count > 0)
                {
                    _lines.Add($"   成功名单：{string.Join("、", aot.Loaded)}");
                }
                if (aot.Missing.Count > 0)
                {
                    _lines.Add($"   ⚠ 缺资源：{string.Join("、", aot.Missing)}");
                }
                if (aot.Failed.Count > 0)
                {
                    _lines.Add($"   ⚠ 失败明细：\n     {string.Join("\n     ", aot.Failed)}");
                }
                _lines.Add($"   编辑器空跑标记 EditorNoOp={aot.EditorNoOp}");
                if (aot.EditorNoOp)
                {
                    _lines.Add("   ⚠⚠ 编辑器跑的是 Mono，LoadMetadataForAOTAssembly 是**空实现**"
                               + "（直接 return OK）。");
                    _lines.Add("        所以上面的「成功」**不能**作为链路正确的证据 —— "
                               + "真机结论必须出一次 IL2CPP 包才能拿到。");
                }
            }

            // ---- 热更 DLL 的 location ----
            _lines.Add("");
            _lines.Add("--- ⑤ 热更 DLL 取用 ---");
            _lines.Add("候选 location（按优先级试，命中即停）：");
            _lines.Add("  " + string.Join("\n  ", HotUpdateLocations.CandidatesForHotUpdateDll(
                HotUpdateLocations.DefaultHotUpdateAssembly)));
            _lines.Add($"命中：{HotUpdateBootstrap.ResolvedDllLocation ?? "(未解析)"}");

            // ---- 程序集身份：证明"确实是新字节被加载了" ----
            _lines.Add("");
            _lines.Add("--- ⑥ 程序集身份（本条只在编辑器下有意义） ---");
            Assembly loaded = HotUpdateLoader.LoadedAssembly;
            if (loaded == null)
            {
                _lines.Add("❌ HotUpdateLoader.LoadedAssembly 为 null —— 程序集没加载成功。");
            }
            else
            {
                string name = loaded.GetName().Name;
                _lines.Add($"✅ 已加载程序集：{name} v{loaded.GetName().Version}");

                // 字节流载入的程序集不对应磁盘路径，Location 是空串。
                _lines.Add(string.IsNullOrEmpty(loaded.Location)
                    ? "✅ Location 为空串 ⇒ 它是从**字节流**载入的（不是从磁盘路径加载）"
                    : $"⚠ Location 非空：{loaded.Location} ⇒ 可能是从磁盘路径加载的，不是字节流");

                int sameName = AppDomain.CurrentDomain.GetAssemblies()
                    .Count(a => a.GetName().Name == name);
                _lines.Add($"   当前域里同名程序集数量：{sameName}");
                if (sameName >= 2)
                {
                    _lines.Add("   ✅ ≥2 份 ⇒ 编辑器自己编译的那份 + 本次从字节流加载的那份，"
                               + "两者是**不同实例**。");
                    _lines.Add("      这就是「新读进来的字节真的生效了」的证据。");
                }
                else
                {
                    _lines.Add("   ⚠ 只有 1 份。若你是从没编译过热更工程的干净环境跑的，"
                               + "这是正常的；");
                    _lines.Add("      否则要怀疑是不是复用了编辑器已编译的副本。");
                }
            }

            // ---- 运行标记 ----
            _lines.Add("");
            _lines.Add("--- ⑦ 热更运行标记（验收的核心） ---");
            _lines.Add($"标记：{HotUpdateBootstrap.EntryVersion}");
            _lines.Add($"当前源码里的标记：{ExpectedStampFromSource()}（读取热更源码文件得到）");
            _lines.Add("★ 判据：把热更源码里的标记改一行 → 只跑「发布热更产物」→ 再跑本体检，");
            _lines.Add("   上面的「标记」跟着变，就说明热更链路成立（没有重新编译整个工程）。");

            // ---- 探针结果 ----
            _lines.Add("");
            _lines.Add("--- ⑧ AOT 泛型探针（证明热更代码真的执行了） ---");
            string probe = ReadProbeResultByReflection();
            _lines.Add(probe ?? "⚠ 反射读不到 ProbeResult —— 接口上没这个方法，属预期；"
                              + "探针结果看控制台 [万相·热更] 那两行。");

            // ---- Tick 驱动 ----
            _lines.Add("");
            _lines.Add("--- ⑨ Tick 帧驱动 ---");
            int tickBefore = HotUpdateBootstrap.TickCount;
            int frameBefore = Time.frameCount;
            await UniTask.Delay(300);
            _lines.Add($"300ms 内：帧数 {frameBefore} → {Time.frameCount}，"
                       + $"Tick {tickBefore} → {HotUpdateBootstrap.TickCount}");
            if (Time.frameCount > frameBefore)
            {
                _lines.Add(HotUpdateBootstrap.TickCount > tickBefore
                    ? "✅ 帧在走且 Tick 被调用"
                    : "❌ 帧在走但 Tick 没被调用（Bootstrap 的 Update 没接到？）");
            }
            else
            {
                _lines.Add("⚠ 帧数没动 ⇒ 本机播放器循环停摆（环境问题，不是热更的问题）。");
                _lines.Add("   这种环境下 Tick 看不了，但前面①~⑧全部走的是事件驱动的异步链，"
                           + "不受影响。");
            }
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>
        /// 从热更源码文件里读出当前的标记常量。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是**唯一**一处让体检去读源码的地方，而且读的是"期望值"，
        ///   不是"实际值"。用它的目的很具体：把"源码改了但产物没重新发布"
        ///   这种很常见的失误当场指出来 —— 否则看到标记没变会误判成"热更坏了"，
        ///   而实际只是忘了跑发布。
        ///
        ///   读不到就返回"(读不到)"，不影响前面的判定。
        /// </remarks>
        private static string ExpectedStampFromSource()
        {
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
                string path = Path.Combine(root,
                    "Assets/WanXiang/HotUpdate/HotUpdateEntry.cs".Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    return "(读不到源码文件)";
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    int idx = line.IndexOf("BuildStamp", StringComparison.Ordinal);
                    if (idx < 0) continue;

                    int first = line.IndexOf('"', idx);
                    int last = line.LastIndexOf('"');
                    if (first >= 0 && last > first)
                    {
                        return line.Substring(first + 1, last - first - 1);
                    }
                }

                return "(源码里没找到 BuildStamp)";
            }
            catch (Exception ex)
            {
                return $"(读源码失败：{ex.Message})";
            }
        }

        /// <summary>
        /// 反射读热更入口的 <c>ProbeResult</c>。
        /// </summary>
        /// <remarks>
        /// ⚠ 为什么必须用反射：<c>WanXiang.Samples</c> **刻意不引用**
        ///   <c>WanXiang.HotUpdate</c>。
        ///   一旦引用，热更程序集就会被拉进玩家构建，热更的整个前提就没了
        ///   （能被换掉的那一份必须**不在**主包里）。
        ///   所以体检只能通过 AOT 侧接口 + 反射去碰它。
        ///   这不是绕路，这正是热更边界该有的样子。
        /// </remarks>
        private static string ReadProbeResultByReflection()
        {
            try
            {
                IHotUpdateEntry entry = HotUpdateLoader.Current;
                if (entry == null)
                {
                    return "❌ 热更入口为 null，读不到探针结果。";
                }

                PropertyInfo prop = entry.GetType().GetProperty("ProbeResult",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                {
                    return null;
                }

                object value = prop.GetValue(entry);
                return value == null
                    ? "⚠ ProbeResult 为 null（探针没跑？）"
                    : "✅ 探针结果：" + value;
            }
            catch (Exception ex)
            {
                return $"⚠ 反射读探针结果失败：{ex.GetType().Name}: {ex.Message}";
            }
        }

        private void FinishAndWrite()
        {
            if (_finished) return;
            _finished = true;

            _lines.Add("");
            _lines.Add("--- 引导状态汇总 ---");
            _lines.Add(HotUpdateBootstrap.DescribeState());

            WriteResult();
            ExitPlayMode();
        }

        private void WriteResult()
        {
            try
            {
                string path = ToProjectPath(ResultRelativePath);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();
                foreach (string line in _lines)
                {
                    sb.AppendLine(line);
                }
                sb.AppendLine();
                sb.AppendLine("--- 控制台也有一份同样的内容 ---");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("[热更体检] 结果已写入：" + path + "\n" + sb);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[热更体检] 写结果文件失败：{ex}");
            }
        }

        private static void ExitPlayMode()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private static void DeleteRequestFile()
        {
            try
            {
                string path = ToProjectPath(RequestRelativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[热更体检] 删请求文件失败（不致命）：{ex.Message}");
            }
        }

        /// <summary>把相对工程根的路径转成绝对路径。</summary>
        /// <remarks>
        /// ⚠ <c>Application.dataPath</c> 指向 &lt;工程&gt;/Assets，工程根是它的父目录。
        ///   直接用 dataPath 拼会得到 ".../Assets/Temp/..."，那个目录可能不存在，
        ///   File.WriteAllText 会抛 DirectoryNotFoundException。
        ///   同 ResourceSmokeTest.ToProjectPath —— 这段被抄第二遍，说明它该进工具类了，
        ///   但两个体检脚本各自独立也让它俩能单独删掉，暂时保持重复。
        /// </remarks>
        private static string ToProjectPath(string relative)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            return Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
