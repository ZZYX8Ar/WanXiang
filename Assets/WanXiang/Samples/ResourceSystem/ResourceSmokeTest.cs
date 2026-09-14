// ============================================================================
//  万相 · 资源系统冒烟测试
//  ---------------------------------------------------------------------------
//  它不是单元测试（本工程还没有测试框架接入），而是**一次真实链路体检**：
//  在 Play 模式下把 IResourceService 的每条路都走一遍，把结果写成一个文本文件。
//
//  ⚠ 为什么结果写文件，而不是打 Debug.Log 就完事：
//    资源链路的失败往往伴随"日志里一堆红字"，而其中大部分是别的系统的噪音。
//    写成一份结构化的结果文件之后：
//      · 判定成功与否只看文件里的 ✅/❌，不用在几千行日志里挑；
//      · 文件能被任何工具读（本工程用的是 Temp/WanXiangDiag 那条通道）；
//      · 出问题时可以把整份结果贴出来，信息不丢。
//
//  ⚠ 为什么用 [RuntimeInitializeOnLoadMethod] 而不是"把组件挂到场景里"：
//    挂到场景 = 场景文件被改动 = 每次体检都要覆盖一次 SampleScene，
//    而且忘了删就会把测试物体留在正式场景里。
//    改成一个**请求文件**触发：Temp/WanXiangDiag/resource_smoke.request 存在才自我安装。
//    请求文件在 Start 里即刻删除（一次性），所以不体检时它对运行时零影响。
//
//  ⚠ 为什么要自己 new 一个 ResourceBootstrap，而不是要求场景里有：
//    因为体检要能在**任何场景**下跑。自己建一个还能顺带验证
//    "ResourceBootstrap 用默认参数能不能独立跑起来"这件事。
//    代价是它验证不了"场景里的 Bootstrap 有没有接错"——
//    那属于场景配置检查，是另一件事。
//
//  ⚠⚠ 为什么必须有**超时兜底**（Update 里的那个看门狗）：
//    这是被真实故障逼出来的。第一版没有超时，遇到初始化挂起时的表现是
//    "结果文件根本没生成" —— 于是既不知道成功、也不知道失败、更不知道卡在哪，
//    只能靠人再进一次 Play 模式看控制台，而挂起本身又不会打任何日志。
//    一个"挂起时什么都不说"的体检工具是没用的。
//    现在：无论成功、失败、挂起，**一定有**一份报告，且报告里带上
//    IResourceService.LastStage（"卡在④请求资源版本"这种粒度）。
//
//  ⚠ 为什么每项检查都各自 try/catch：
//    第一项失败就把后面全部跳过，会让人误以为"只有第一项有问题"。
//    一次性把所有能测的都测完，信息量最大。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WanXiang.Framework.Boot;
using WanXiang.Framework.ResourceSystem;
using WanXiang.Framework.ResourceSystem.Backend;

namespace WanXiang.Framework.Samples.ResourceSystem
{
    /// <summary>
    /// 资源链路冒烟测试。由请求文件触发，结果写入 Temp/WanXiangDiag/resource_smoke.txt。
    /// </summary>
    public sealed class ResourceSmokeTest : MonoBehaviour
    {
        /// <summary>请求文件（相对工程根）。存在才自我安装。</summary>
        public const string RequestRelativePath = "Temp/WanXiangDiag/resource_smoke.request";

        /// <summary>结果文件（相对工程根）。</summary>
        public const string ResultRelativePath = "Temp/WanXiangDiag/resource_smoke.txt";

        /// <summary>整体超时（秒）。超时也出报告，见文件头说明。</summary>
        private const float TimeoutSeconds = 90f;

        /// <summary>测试用资源地址。与 YooAssetSetupTool 建出来的样本一一对应。</summary>
        private const string TextureLocation = "Assets/WanXiangRes/Art/TestTexture";
        private const string PrefabLocation = "Assets/WanXiangRes/UI/TestPanel";
        private const string ConfigLocation = "Assets/WanXiangRes/Config/test_config";

        /// <summary>配置 JSON 里埋的标记，用来确认"读到的确实是那个文件的内容"。</summary>
        private const string ConfigMarker = "WANXIANG_RESOURCE_OK";

        private readonly List<string> _lines = new List<string>(64);
        private float _deadline;
        private bool _finished;

        /// <summary>开始时刻，用于心跳里报"已经过了多久"。</summary>
        private float _startTime;

        /// <summary>下一次打心跳的时刻。</summary>
        private float _nextHeartbeat;

        /// <summary>心跳间隔（秒）。</summary>
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

                var go = new GameObject("[ResourceSmokeTest]");
                DontDestroyOnLoad(go);
                go.AddComponent<ResourceSmokeTest>();

                Debug.Log("[资源体检] 检测到请求文件，已启动资源链路体检。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[资源体检] 自我安装失败：{ex}");
            }
        }

        private void Start()
        {
            // ⚠ 请求文件是**一次性**的，用完立刻删。
            //   不删的后果：以后任何一次手动进 Play 模式都会莫名其妙跑一遍体检、
            //   然后自动退出 Play —— 那会让人完全摸不着头脑。
            DeleteRequestFile();

            _lines.Add("=== 万相 · 资源链路体检 ===");
            _lines.Add($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _lines.Add($"Unity：{Application.unityVersion}");
            _lines.Add($"平台：{Application.platform}，Play 模式：{Application.isPlaying}");
            _lines.Add(string.Empty);

            // 打开桥接层追踪。资源挂起时这份日志是唯一能区分
            // "等不到唤醒"和"操作本身没跑完"的证据。
            YooAssetResourceService.TraceAwaiter = true;
            _lines.Add("（桥接层追踪已打开，控制台会有 [资源桥接] 开头的日志）");
            _lines.Add(string.Empty);

            // ⚠ 自动化体检必须自己把"后台运行"打开。
            //   编辑器窗口不在前台时，播放器循环可能整段停摆：异步操作不推进、
            //   Update 不执行、看门狗也不响 —— 体检会"安静地"没有任何输出。
            //   这不是我们的资源代码的问题，但会把结果读成资源代码的问题，
            //   所以这里直接打开，让体检不依赖窗口焦点。
            Application.runInBackground = true;

            _deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            _startTime = Time.realtimeSinceStartup;
            _nextHeartbeat = _startTime + HeartbeatInterval;
            RunAsync().Forget();
        }

        /// <summary>
        /// 超时看门狗 + 心跳。
        /// </summary>
        /// <remarks>
        /// ⚠ 心跳不是"顺手加的日志"，而是排查"静默卡住"的关键证据。
        ///
        ///   之前遇到过一次：体检既没报错也没结果，控制台的桥接日志停在
        ///   "开始等待 xxx"。这时有**两种完全相反**的可能，从外面看不出区别：
        ///     A. 游戏循环还在跑，只是那个异步操作不推进；
        ///     B. 游戏循环整个停了（编辑器被暂停 / Play 没真正跑起来）。
        ///   两者都表现为"Update 不打日志"。
        ///
        ///   有心跳就能一刀切开：心跳持续 ⇒ 是 A；心跳停了 ⇒ 是 B。
        ///   注意 A 情况下本方法会一直跑，所以心跳是可靠的"游戏循环存活"证明。
        /// </remarks>
        private void Update()
        {
            if (_finished) return;

            float now = Time.realtimeSinceStartup;

            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + HeartbeatInterval;
                Debug.Log($"[资源体检] 心跳：已过 {now - _startTime:0.0} 秒，" +
                          $"第 {Time.frameCount} 帧，Play={Application.isPlaying}。" +
                          "（这行停了就说明游戏循环停了，不是异步没唤醒）");
            }

            if (now < _deadline) return;

            _lines.Add(string.Empty);
            _lines.Add($"⏱ 超时（{TimeoutSeconds:0} 秒）：体检没有跑完，卡在上面的某一步。");
            _lines.Add("   看下面的「服务状态」和「最后阶段」，再配合控制台的 [资源桥接] 日志判断。");
            _lines.Add("   ⚠ 若控制台里连 [资源体检] 心跳都没有了，说明是**游戏循环停了**" +
                       "（多半是编辑器处于暂停态），而不是异步等待挂掉。");
            FinishAndWrite();
        }

        private async UniTaskVoid RunAsync()
        {
            try
            {
                await RunChecksAsync();
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 体检过程整体异常：{ex.GetType().Name}: {ex.Message}");
                _lines.Add(ex.StackTrace ?? string.Empty);
            }

            // 中途提前返回（比如初始化就没过）时也要有尾巴，
            // 否则"报告没有最后一行"会被误读成"文件写坏了"。
            _lines.Add(string.Empty);
            _lines.Add("=== 体检结束 ===");
            FinishAndWrite();
        }

        /// <summary>无论成败只落一次盘，然后退出 Play。</summary>
        private void FinishAndWrite()
        {
            if (_finished) return;
            _finished = true;

            IResourceService res = ResourceBootstrap.Resource;
            _lines.Add(string.Empty);
            _lines.Add("--- 服务状态 ---");
            if (res == null)
            {
                _lines.Add("ResourceBootstrap.Resource 为 null（Bootstrap 没建起来？）");
            }
            else
            {
                _lines.Add($"IsReady={res.IsReady}，模式={res.PlayMode}，" +
                           $"包={res.PackageName}，版本={res.PackageVersion}");
                _lines.Add($"最后阶段：{res.LastStage}");
                _lines.Add($"LastError：{res.LastError ?? "(无)"}");
            }

            WriteResult();
            ExitPlayMode();
        }

        private async UniTask RunChecksAsync()
        {
            // ---- 启动资源系统 ----
            // AddComponent 会同步触发 Awake，Awake 里开始异步初始化。
            var bootGo = new GameObject("[ResourceBootstrap]");
            bootGo.AddComponent<ResourceBootstrap>();

            _lines.Add("--- ① 初始化 ---");
            bool ready = await ResourceBootstrap.WaitReadyAsync(this.GetCancellationTokenOnDestroy());
            _lines.Add(ready
                ? "✅ WaitReadyAsync 返回 true"
                : "❌ WaitReadyAsync 返回 false（看控制台 [资源] 开头的报错）");

            IResourceService res = ResourceBootstrap.Resource;
            if (res == null)
            {
                _lines.Add("❌ ResourceBootstrap.Resource 为 null，后续检查无法进行。");
                return;
            }

            if (!res.IsReady)
            {
                _lines.Add($"   包={res.PackageName}，模式={res.PlayMode}");
                _lines.Add("❌ 资源系统未就绪，跳过加载检查。");
                return;
            }

            // ---- 清单里到底有哪些地址（不看这个就是在猜） ----
            _lines.Add(string.Empty);
            _lines.Add("--- ② 地址有效性（CheckLocationValid） ---");
            CheckLocation(res, "贴图", TextureLocation);
            CheckLocation(res, "Prefab", PrefabLocation);
            CheckLocation(res, "配置JSON", ConfigLocation);

            // ---- 异步加载 ----
            _lines.Add(string.Empty);
            _lines.Add("--- ③ LoadAssetAsync<Texture2D>（异步 + UniTask 桥接） ---");
            try
            {
                Texture2D texture = await res.LoadAssetAsync<Texture2D>(TextureLocation);
                if (texture != null)
                {
                    _lines.Add($"✅ 拿到贴图「{texture.name}」{texture.width}×{texture.height}");
                    res.ReleaseAsset(TextureLocation);
                    _lines.Add($"   已 ReleaseAsset，剩余已加载 location={res.LoadedLocationCount}");
                }
                else
                {
                    _lines.Add($"❌ 返回 null。LastError={res.LastError}");
                }
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 抛异常 {ex.GetType().Name}: {ex.Message}");
            }

            // ---- 同步加载 ----
            _lines.Add(string.Empty);
            _lines.Add("--- ④ LoadAssetSync<Texture2D>（同步路径） ---");
            try
            {
                Texture2D texture = res.LoadAssetSync<Texture2D>(TextureLocation);
                if (texture != null)
                {
                    _lines.Add($"✅ 拿到贴图「{texture.name}」{texture.width}×{texture.height}");
                    res.ReleaseAsset(TextureLocation);
                }
                else
                {
                    _lines.Add($"❌ 返回 null。LastError={res.LastError}");
                }
                _lines.Add($"   累计同步加载调用次数={res.SyncLoadCallCount}");
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 抛异常 {ex.GetType().Name}: {ex.Message}");
            }

            // ---- 实例化 ----
            _lines.Add(string.Empty);
            _lines.Add("--- ⑤ InstantiateAsync（面板走的就是这条） ---");
            GameObject instance = null;
            try
            {
                instance = await res.InstantiateAsync(PrefabLocation);
                _lines.Add(instance != null
                    ? $"✅ 实例化出「{instance.name}」"
                    : $"❌ 返回 null。LastError={res.LastError}");
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 抛异常 {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (instance != null)
                {
                    res.ReleaseInstance(instance);
                    _lines.Add("   已 ReleaseInstance");
                }
            }

            // ---- 文本（原生文件路径） ----
            _lines.Add(string.Empty);
            _lines.Add("--- ⑥ LoadTextAsync（原生文件 / 配置表路径） ---");
            try
            {
                string text = await res.LoadTextAsync(ConfigLocation);
                if (text == null)
                {
                    _lines.Add($"❌ 返回 null。LastError={res.LastError}");
                }
                else if (text.Contains(ConfigMarker))
                {
                    _lines.Add($"✅ 读到 {text.Length} 字符，且含标记「{ConfigMarker}」");
                }
                else
                {
                    _lines.Add($"⚠ 读到 {text.Length} 字符，但**不含**标记「{ConfigMarker}」。");
                    _lines.Add("   实际内容：" + text.Replace("\n", "\\n").Replace("\r", string.Empty));
                }
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 抛异常 {ex.GetType().Name}: {ex.Message}");
            }

            // ---- 更新（编辑器模拟模式应当直接成功且不下载） ----
            _lines.Add(string.Empty);
            _lines.Add("--- ⑦ UpdatePackageAsync（编辑器模拟模式应为空操作成功） ---");
            try
            {
                ResourceUpdateReport report = await res.UpdatePackageAsync();
                _lines.Add(report.Success ? $"✅ {report}" : $"❌ {report}");
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ 抛异常 {ex.GetType().Name}: {ex.Message}");
            }

            // ---- 泄漏自查 ----
            _lines.Add(string.Empty);
            _lines.Add("--- ⑧ 引用计数收尾检查 ---");
            _lines.Add(res.LoadedLocationCount == 0
                ? "✅ 已加载 location 数为 0（所有引用都还干净了）"
                : $"❌ 还剩 {res.LoadedLocationCount} 个 location 没释放（有泄漏）：");
            res.DumpLoadedLocations();
        }

        private void CheckLocation(IResourceService res, string label, string location)
        {
            bool ok;
            try
            {
                ok = res.CheckLocationValid(location);
            }
            catch (Exception ex)
            {
                _lines.Add($"❌ {label} CheckLocationValid 抛异常：{ex.Message}");
                return;
            }

            _lines.Add(ok
                ? $"{label}：✅ 在清单里  {location}"
                : $"{label}：❌ 不在清单里  {location}");
        }

        // ==================================================================
        //  落盘
        // ==================================================================

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
                Debug.LogWarning($"[资源体检] 删除请求文件失败（不影响本次体检）：{ex.Message}");
            }
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
                Debug.Log("[资源体检] 结果已写入：" + path + "\n" + sb);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[资源体检] 写结果文件失败：{ex}");
            }
        }

        private static void ExitPlayMode()
        {
#if UNITY_EDITOR
            // 体检是一次性的：跑完自动退出 Play，省掉"还得手动停"这一步，
            // 也让"跑一次体检"变成一个可以完全自动化的动作。
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>把相对工程根的路径转成绝对路径。</summary>
        /// <remarks>
        /// ⚠ <c>Application.dataPath</c> 指向 &lt;工程&gt;/Assets，
        ///   所以工程根是它的**父目录**。直接用 dataPath 拼会得到
        ///   ".../Assets/Temp/..." —— 那是错的，而且错的目录可能根本不存在，
        ///   File.WriteAllText 会直接抛 DirectoryNotFoundException。
        /// </remarks>
        private static string ToProjectPath(string relative)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            return Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
