// ============================================================================
//  万相 · 编辑器诊断桥 · Suites（从 EditorDiagnosticsBridge.cs 拆出：**纯搬家**）
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

        /// <summary>
        /// 热更发布工具的类型名。
        /// </summary>
        private const string HotToolTypeName =
            "WanXiang.Editor.HotUpdateTool.HotUpdatePublishTool, WanXiang.Editor.HotUpdate";

        /// <remarks>
        /// 反射调用的代价是"类型名写错了只会在运行时报错"，而且报的是
        /// "找不到类型"这种没头没尾的信息。所以这里对三种失败分别给出
        /// 能直接行动的提示，而不是统一一句 ❌。
        /// </remarks>
        private static void RunYooTool(Report report, string command)
        {
            RunEditorTool(report, command, YooToolTypeName,
                "① WanXiang.Editor.YooAsset 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里 YooAsset / YooAsset.Editor 引用不成立。");
        }

        private static void RunHotTool(Report report, string command)
        {
            RunEditorTool(report, command, HotToolTypeName,
                "① WanXiang.Editor.HotUpdate 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里 HybridCLR.Editor / WanXiang.Editor.YooAsset 引用不成立。");
        }

        /// <summary>战斗核心自检工具的类型名（与诊断通道同程序集）。</summary>
        private const string BattleToolTypeName =
            "WanXiang.Editor.BattleTool.BattleSelfTest, WanXiang.Editor";

        /// <remarks>
        /// 这是 Edit 模式同步命令，**不会切 Play**。理由有两条：
        /// ① 这台机器的编辑器不维持 Play 模式的播放器循环（见项目记忆），靠 Play 跑验收不可靠；
        /// ② 战斗核心是零引擎依赖的纯逻辑（WanXiang.Battle.Core 的 noEngineReferences=true），
        ///    本来就不需要 Play —— 需要的话反而说明有人把引擎依赖漏进了核心层。
        /// </remarks>
        private static void RunBattleSelfTest(Report report)
        {
            RunEditorTool(report, "battle.selftest", BattleToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Battle.Core 引用。");
        }

        private const string GrayBoxTypeName =
            "WanXiang.Editor.BattleTool.BattleGrayBoxWindow, WanXiang.Editor";

        /// <summary>融合管线自检工具的类型名（与诊断通道同程序集）。</summary>
        private const string FusionToolTypeName =
            "WanXiang.Editor.FusionTool.FusionSelfTest, WanXiang.Editor";

        /// <summary>
        /// 融合管线自检（GDD STEP 2 验收①②③的机器判据）。Edit 模式同步跑，不进 Play ——
        /// 理由同战斗自检：融合规则与战斗核心一样是纯计算，靠 Play 反而不可靠。
        /// </summary>
        private static void RunFusionSelfTest(Report report)
        {
            RunEditorTool(report, "fusion.selftest", FusionToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Modules.Fusion(.Core) 引用。");
        }

        private const string ShareToolTypeName =
            "WanXiang.Editor.FusionTool.ShareSelfTest, WanXiang.Editor";

        /// <summary>分享码自检（GDD STEP 3 验收③：≤90 字符 + 1:1 复现对局）。Edit 模式同步跑。</summary>
        private static void RunShareSelfTest(Report report)
        {
            RunEditorTool(report, "share.selftest", ShareToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Modules.Fusion(.Core) 引用。");
        }

        private const string WeatherToolTypeName =
            "WanXiang.Editor.WeatherTool.WeatherSelfTest, WanXiang.Editor";

        /// <summary>天时系统自检（GDD STEP 3；含基准局指纹回归保护）。Edit 模式同步跑。</summary>
        private static void RunWeatherSelfTest(Report report)
        {
            RunEditorTool(report, "weather.selftest", WeatherToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Battle.Core 引用。");
        }

        private const string TrialsToolTypeName =
            "WanXiang.Editor.TrialsTool.TrialsSelfTest, WanXiang.Editor";

        /// <summary>劫·难度循环自检（GDD v1.1 §7）。Edit 模式同步跑。</summary>
        private static void RunTrialsSelfTest(Report report)
        {
            RunEditorTool(report, "trials.selftest", TrialsToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Trials.Core 引用。");
        }

        private const string PvpToolTypeName =
            "WanXiang.Editor.PvpTool.PvpSelfTest, WanXiang.Editor";

        /// <summary>异步对战自检（GDD STEP 3：分享码 → 敌方阵容 → 可对照的战报）。Edit 模式同步跑。</summary>
        private static void RunPvpSelfTest(Report report)
        {
            RunEditorTool(report, "pvp.selftest", PvpToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Pvp.Core 引用。");
        }

        private const string MetaToolTypeName =
            "WanXiang.Editor.MetaTool.MetaSelfTest, WanXiang.Editor";

        /// <summary>局外孵蛋自检（GDD STEP 3：灵卵经济 / 确定性孵蛋 / 存档码 / 端到端）。Edit 模式同步跑。</summary>
        private static void RunMetaSelfTest(Report report)
        {
            RunEditorTool(report, "meta.selftest", MetaToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Meta.Core 引用。");
        }

        private const string CampaignToolTypeName =
            "WanXiang.Editor.CampaignTool.CampaignSelfTest, WanXiang.Editor";

        /// <summary>节气节点图自检（GDD STEP 3：分叉路径 / 一局 21 战 / 跨幕余气）。Edit 模式同步跑。</summary>
        private static void RunCampaignSelfTest(Report report)
        {
            RunEditorTool(report, "campaign.selftest", CampaignToolTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Campaign.Core 引用。");
        }

        /// <summary>打开灰盒预览窗口（并把预跑数据与自检结果回报一行）。</summary>
        private static void RunBattleTool(Report report, string command)
        {
            RunEditorTool(report, command, GrayBoxTypeName,
                "① WanXiang.Editor 还没编译过（改完代码先跑 refresh）；"
                + "② 它的 asmdef 里缺 WanXiang.Battle.Core 引用。");
        }

        /// <summary>
        /// 反射调用「有 <c>public static string[] Run(string)</c>」的编辑器工具。
        /// </summary>
        /// <remarks>
        /// 走反射而不是直接引用：诊断通道所在程序集（WanXiang.Editor）刻意
        /// **不依赖**任何具体工具程序集。这样某个工具编不过时，
        /// 通道本身还能活着把错误报出来 —— 否则会变成
        /// "工具坏了 ⇒ 通道也编不过 ⇒ 连报告都看不到"，查不了。
        /// </remarks>
        private static void RunEditorTool(Report report, string command, string typeName, string missingHint)
        {
            Type type = Type.GetType(typeName);
            if (type == null)
            {
                report.results.Add($"{command} -> ❌ 找不到类型 {typeName}\n        常见原因：{missingHint}");
                return;
            }

            MethodInfo method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                report.results.Add($"{command} -> ❌ {type.FullName} 上没有 public static Run(string)。");
                return;
            }

            try
            {
                var lines = method.Invoke(null, new object[] { command }) as string[];
                if (lines == null || lines.Length == 0)
                {
                    report.results.Add($"{command} -> （工具没有输出）");
                    return;
                }

                foreach (string line in lines)
                {
                    // 缩进一下，报告里能看出这些行属于同一个命令。
                    report.results.Add($"{command} -> {line}");
                }
            }
            catch (TargetInvocationException tie)
            {
                // 反射调用抛异常时会包一层 TargetInvocationException，
                // 真正有用的信息在 InnerException 里。不拆开的话只能看到
                // "Exception has been thrown by the target of an invocation"。
                Exception inner = tie.InnerException ?? tie;
                report.results.Add($"{command} -> ❌ {inner.GetType().Name}: {inner.Message}");
                if (!string.IsNullOrEmpty(inner.StackTrace))
                {
                    report.results.Add($"{command} ->     {inner.StackTrace}");
                }
            }
            catch (Exception e)
            {
                report.results.Add($"{command} -> ❌ {e.GetType().Name}: {e.Message}");
            }
        }

        // ====================================================================
        //  resource.smoke —— 资源链路体检（需要进 Play 模式）
        // ====================================================================

        private const string SmokeRequestPath = DiagDir + "/resource_smoke.request";
        private const string SmokeResultPath = DiagDir + "/resource_smoke.txt";

        /// <summary>热更体检的请求 / 结果文件。与 HotUpdateSmokeTest 里的常量必须一致。</summary>
        private const string HotSmokeRequestPath = DiagDir + "/hot_smoke.request";
        private const string HotSmokeResultPath = DiagDir + "/hot_smoke.txt";

        /// <remarks>
        /// 为什么必须进 Play 模式，而不是在编辑器里直接测：
        ///   本工程的资源服务是 **UniTask 驱动**的，而 UniTask 的
        ///   PlayerLoop 只在 Play 模式下才起来。编辑器非播放态下
        ///   await 一个 UniTask 永远不会恢复，测出来的会是"卡死"，
        ///   而不是真的结果 —— 那种"假失败"比不测更误导人。
        ///
        ///   （YooAsset 自己倒是提供了 WaitForAsyncComplete 这种同步驱动，
        ///     但那只覆盖得到 YooAsset，覆盖不到我们自己写的桥接层和
        ///     引用计数逻辑，而那才是真正需要体检的部分。）
        ///
        /// 机制：写请求文件 → 运行时的 ResourceSmokeTest 靠它自我安装 →
        ///       跑完把结果写到 SmokeResultPath 并自动退出 Play。
        ///       所以本命令只是"点火"，结果要另外读文件。
        /// </remarks>
        private static void RequestResourceSmoke(Report report)
        {
            RequestSmoke(report, "resource.smoke", SmokeRequestPath, SmokeResultPath, null);
        }

        /// <summary>
        /// 热更链路体检的点火。
        /// </summary>
        /// <remarks>
        /// 与 <c>resource.smoke</c> 唯一的实质差别是**请求文件不同**，
        /// 于是运行时装上的是 HotUpdateSmokeTest 而不是 ResourceSmokeTest。
        ///
        /// ⚠ 顺序建议：**先跑过 resource.smoke（或至少确认资源链路通），再跑本命令**。
        ///   热更的第一段就是"等资源就绪"（热更 DLL 与 AOT DLL 都是当资源下发的）。
        ///   资源不通时热更必然不通，报告会停在 ① —— 那是**正确结论**，
        ///   但如果你没先单独验过资源，就很容易误判成"热更坏了"。
        /// </remarks>
        private static void RequestHotUpdateSmoke(Report report)
        {
            RequestSmoke(report, "hot.smoke", HotSmokeRequestPath, HotSmokeResultPath,
                "热更体检会自建 ResourceBootstrap + HotUpdateBootstrap，走完五段流程后自动退 Play。");
        }

        /// <summary>
        /// 体检点火（资源 / 热更共用）。
        /// </summary>
        /// <param name="command">命令名，只用于报告前缀与请求文件内容。</param>
        /// <param name="requestPath">请求文件路径（相对工程根）。</param>
        /// <param name="resultPath">结果文件路径，用于先删旧的。</param>
        /// <param name="note">额外提示，可为 null。</param>
        private static void RequestSmoke(Report report, string command,
            string requestPath, string resultPath, string note)
        {
            if (EditorApplication.isPlaying)
            {
                report.results.Add($"{command} -> ⚠ 当前已经在 Play 模式里，先跑 play.exit");
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.results.Add($"{command} -> ⚠ 正在切 Play 模式，稍后再试");
                return;
            }

            // ⚠ 场景有未保存改动时进入 Play 模式，Unity 会弹"是否保存场景"的模态框。
            //   那条框在自动化环境里没人点，于是整个流程就卡在那里，
            //   表现为"命令发出去了但什么都没发生"。所以这里直接拦住并说清原因。
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                report.results.Add(
                    $"{command} -> ❌ 当前场景「{scene.name}」有未保存改动，"
                    + "进 Play 模式会弹模态框，自动化流程会卡住。\n"
                    + "        解决：在 Unity 里 Ctrl+S 保存场景，或先关掉不想保存的改动，再重跑本命令。");
                return;
            }

            try
            {
                Directory.CreateDirectory(DiagDir);
                File.WriteAllText(requestPath,
                    command + " requested at "
                    + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n",
                    Utf8NoBom);
            }
            catch (Exception e)
            {
                report.results.Add($"{command} -> ❌ 写请求文件失败：{e.Message}");
                return;
            }

            // 上一次的结果先删掉：否则"跑完了"和"还没跑"分不出来，
            // 会拿着上一轮的成功结果当成这一轮的。
            try
            {
                if (File.Exists(resultPath)) File.Delete(resultPath);
            }
            catch (Exception e)
            {
                report.results.Add($"{command} -> ⚠ 删旧结果失败（不致命）：{e.Message}");
            }

            if (!string.IsNullOrEmpty(note))
            {
                report.results.Add($"{command} -> ℹ {note}");
            }

            report.results.Add($"{command} -> 已写请求文件 {requestPath}；"
                               + "报告落盘后会进入 Play 模式");
            report.results.Add($"{command} -> 结果写到 {resultPath}，跑完自动退出 Play");

            // ⚠ 残留的暂停态必须清掉，否则这一轮体检会"安安静静什么都不做"。
            //   暂停时游戏侧的 Update 与异步推进全部停摆，看门狗也不会响，
            //   表现成"进了 Play 但一个日志都没有" —— 极难判读。
            if (EditorApplication.isPaused)
            {
                report.results.Add($"{command} -> ⚠ 检测到编辑器处于暂停态，已解除"
                                   + "（残留的暂停会让游戏循环整段停摆）");
            }
        }

        /// <summary>
        /// 只进 Play 模式，不写体检请求文件。
        /// </summary>
        /// <remarks>
        /// ⚠ 这是给「Play 模式到底有没有在跑」做的对照实验。
        ///   resource.smoke 会顺带装上体检脚本，于是"循环是不是真的在跑"
        ///   和"体检代码有没有问题"两件事混在一起，读不出来。
        ///   play.enter 什么都不装，进 Play 后用 play.state 隔几秒连读两次：
        ///   连空场景都不推进帧，那就是环境问题（编辑器没在跑播放器循环），
        ///   跟我们自己的代码无关 —— 这一刀必须切干净，否则会一直往错的方向修。
        /// </remarks>
        private static void RequestPlayEnter(Report report)
        {
            if (EditorApplication.isPlaying)
            {
                report.results.Add("play.enter -> ⚠ 已经在 Play 模式里了");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
            {
                report.results.Add(
                    $"play.enter -> ❌ 场景「{scene.name}」有未保存改动，"
                    + "进 Play 会弹模态框把流程卡住。先保存场景再试。");
                return;
            }

            report.results.Add("play.enter -> 报告落盘后会进入 Play 模式（不跑体检）");
        }

        /// <remarks>
        /// 体检理论上会自己退出 Play，本命令是"卡住了捞一把"用的。
        /// </remarks>
        private static void RequestPlayExit(Report report)        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.results.Add("play.exit -> 当前不在 Play 模式，无需退出");
                return;
            }

            // ⚠ 先解暂停，再退 Play。
            //   被暂停时 EditorApplication.isPlaying = false 不一定立刻生效 ——
            //   表现成"发了 play.exit 但还停在 Play 里"。顺带一提，暂停期间
            //   游戏侧的 Update 是不跑的（见 play.state 的说明），所以退出前
            //   一定要把暂停也清掉，否则会陷在"退不出、又没人推进"的状态。
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
                report.results.Add("play.exit -> 检测到编辑器处于暂停，已先解除暂停");
            }

            report.results.Add("play.exit -> 报告落盘后会退出 Play 模式");
        }

        /// <summary>
        /// 只读：报告编辑器/播放器的运行状态。
        /// </summary>
        /// <remarks>
        /// ⚠ 这条命令是被一个"看起来自相矛盾"的故障逼出来的：
        ///   资源体检跑一半没了下文，可编辑器本身还活着、还能响应命令。
        ///   当时的观测是——
        ///     · 游戏侧的 Update（看门狗）一次都没跑
        ///     · YooAsset 的 OperationSystem.Update() 也没推进
        ///     · 但 channel 的 console 命令照常返回
        ///   这三条同时成立，唯一说得通的解释就是**编辑器被暂停了**
        ///   （EditorApplication.isPaused，Console 的 Error Pause 开关会触发）：
        ///   暂停时游戏循环整段停摆，而 EditorApplication.update 照跑。
        ///
        ///   光看"Play 模式开着"是分辨不出这个状态的，所以这里把
        ///   isPaused / frameCount 一起报出来 —— frameCount 隔几秒连读两次
        ///   就能直接判定游戏循环有没有在走，比任何推断都硬。
        /// </remarks>
        private static void ReportPlayState(Report report)
        {
            report.results.Add($"play.state -> isPlaying={EditorApplication.isPlaying}，" +
                               $"isPlayingOrWillChangePlaymode={EditorApplication.isPlayingOrWillChangePlaymode}，" +
                               $"isPaused={EditorApplication.isPaused}，" +
                               $"isCompiling={EditorApplication.isCompiling}，" +
                               $"isUpdating={EditorApplication.isUpdating}");

            // 这三个只在游戏侧才有意义；不在 Play 时读到的是编辑器自己的时间。
            report.results.Add($"play.state -> Time.frameCount={Time.frameCount}，" +
                               $"realtimeSinceStartup={Time.realtimeSinceStartup:0.000}，" +
                               $"timeScale={Time.timeScale}，" +
                               $"Application.isPlaying={Application.isPlaying}");

            // ⚠ 焦点与后台运行：这两个是"帧不推进"最常见的元凶。
            //   编辑器窗口失焦 + runInBackground 关掉时，播放器循环可能整段停摆 ——
            //   而 isPlaying 仍然是 true、isPaused 仍然是 false，
            //   只看前两个字段完全分辨不出来。
            report.results.Add($"play.state -> Application.isFocused={Application.isFocused}，" +
                               $"runInBackground={Application.runInBackground}，" +
                               $"targetFrameRate={Application.targetFrameRate}，" +
                               $"qualityVsync={QualitySettings.vSyncCount}");

            if (EditorApplication.isPaused)
            {
                report.results.Add("play.state -> ⚠ 编辑器处于暂停：游戏侧 Update 不会执行，" +
                                   "操作系统的异步任务也不会推进。" +
                                   "用 play.exit 退出，或关掉 Console 的 Error Pause 开关。");
            }
        }

        /// <summary>
        /// 切换玩家构建的 development 开关。
        ///
        /// 为什么要单独一条命令：<c>HybridCLR/CompileDll/ActiveBuildTarget</c> 编出来的
        /// 热更 DLL 是**跟着当前 development 状态走**的 —— 出包时用什么状态，热更 DLL
        /// 就必须用同一状态编，否则运行期会因宏不同而对不上。
        ///
        /// 另外它也是排查 CS7036 的一个对照实验：确认 development 是否会让 Unity
        /// 补上 ENABLE_UNITY_COLLECTIONS_CHECKS。
        /// </summary>
        private static void SetPlayerDevelopment(Report report, bool on)
        {
            string tag = on ? "player.dev.on" : "player.dev.off";
            bool before = EditorUserBuildSettings.development;
            EditorUserBuildSettings.development = on;
            report.results.Add($"{tag} -> development {before} → {EditorUserBuildSettings.development}"
                               + $"（构建目标 {EditorUserBuildSettings.activeBuildTarget}）");
            report.results.Add($"{tag} -> 该开关只影响**之后**的编译/出包；"
                               + "要重编热更 DLL 请接着跑 hybridclr.compile-dll");
        }

        private static void ClearFacadeReference(Report report)
        {
            const string tag = "facade.clear";
            string rspPath = CscRspFullPath();

            foreach (var p in new[] { rspPath, rspPath + ".meta" })
            {
                if (!File.Exists(p)) { report.results.Add($"{tag} -> 不存在 {Path.GetFileName(p)}，跳过"); continue; }
                try
                {
                    File.Delete(p);
                    report.results.Add($"{tag} -> 已删除 {Path.GetFileName(p)}");
                }
                catch (Exception e)
                {
                    report.results.Add($"{tag} -> ❌ 删除 {Path.GetFileName(p)} 失败：{e.Message}");
                }
            }

            // ⚠ 只删文件不删清单：额外宏是独立状态，用户可能只想撤门面。
            var defines = ReadExtraDefines();
            if (defines.Count > 0)
            {
                report.results.Add($"{tag} -> ⚠ 额外宏清单仍有 [{string.Join(", ", defines)}]，"
                                   + "它们现在不会生效（csc.rsp 已被删除）；要撤掉请跑 csc.defines.off");
            }
            report.results.Add($"{tag} -> 同样需要一次重新编译才会反映到编译命令里");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += needle.Length;
            }
            return n;
        }

        private static void AddDir(Report report, string root, string rel, string what)
        {
            string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                report.results.Add($"{what}: ❌ 不存在 {rel}");
                return;
            }

            var files = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                {
                    files.Add($"{f.Substring(full.Length).TrimStart('\\', '/')}  {new FileInfo(f).Length} B");
                }
            }
            catch (Exception e)
            {
                report.results.Add($"{what}: ⚠ 列目录失败 {e.GetType().Name}");
                return;
            }

            report.results.Add($"{what}: ✅ {rel} 共 {files.Count} 个文件");
            foreach (var f in files.Take(25)) report.results.Add("    " + f);
            if (files.Count > 25) report.results.Add($"    ...另有 {files.Count - 25} 个");
        }

        // ====================================================================
        //  通用反射工具
        // ====================================================================

        /// <remarks>
        /// 按**简名**找类型。用途：某些 Unity 内部类型（如 AssemblyDefinitionAsset）
        /// 在 <c>UnityEditorInternal</c> 命名空间里，写全名等于靠猜；而简名唯一且稳定。
        /// </remarks>
        private static Type FindTypeBySimpleName(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t != null && t.Name == simpleName) return t;
                    }
                }
                catch
                {
                    // 某些程序集的 GetTypes() 会抛（动态程序集 / 缺依赖）—— 跳过
                }
            }
            return null;
        }

        /// <remarks>
        /// 只在**无参**时才返回 —— 反射调用参数对不上必炸，所以宁可返回 null。
        /// </remarks>
        private static MethodInfo FindMethod(Type type, string name)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0);
        }

        // ====================================================================
        //  通用反射工具
        // ====================================================================

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch
                {
                    // 某些动态程序集在 GetType 时会抛 —— 跳过就好，不该中断体检。
                }
            }
            return null;
        }

        /// <summary>把方法签名渲染成一行可读文本，用于「先看清再调用」。</summary>
        private static string Signature(MethodInfo m)
        {
            var ps = m.GetParameters()
                .Select(p => p.ParameterType.Name + " " + p.Name);
            return $"{m.ReturnType.Name} {m.Name}({string.Join(", ", ps)})";
        }

        // ====================================================================
        //  控制台渲染（报告文件之外，顺手给人眼看的版本）
        // ====================================================================

        private static string Render(Report r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"时间     : {r.timestamp}");
            sb.AppendLine($"Unity    : {r.unityVersion}");
            sb.AppendLine($"命令     : {string.Join(" | ", r.commands)}");
            sb.AppendLine();

            Section(sb, "执行结果", r.results);

            if (r.inputHandlerRaw != -1 || r.maps.Count > 0)
            {
                sb.AppendLine("── 输入后端 ──");
                sb.AppendLine($"activeInputHandler : {r.inputHandlerName}");
                sb.AppendLine($"Keyboard.current   : {(r.keyboardCurrent ? "✅ 非 null（后端已生效）" : "❌ null（后端未生效）")}");
                sb.AppendLine($"Mouse.current      : {(r.mouseCurrent ? "✅" : "❌")}");
                sb.AppendLine($"Gamepad.current    : {(r.gamepadCurrent ? "✅" : "（无手柄，正常）")}");
                sb.AppendLine($"UpdateMode         : {r.inputSystemUpdateMode}");
                sb.AppendLine($"资产               : {(r.assetFound ? r.assetPath : "⚠ 未找到 " + r.assetPath)}");
                sb.AppendLine($"Map 数             : {r.mapCount}");
                foreach (var m in r.maps) sb.AppendLine("  " + m);
                foreach (var b in r.bindings) sb.AppendLine("  " + b);
                sb.AppendLine();
            }

            Section(sb, "类型落位", r.types);
            Section(sb, "程序集定义", r.asmdefs);
            Section(sb, "包状态", r.packages);
            Section(sb, "已加载程序集", r.assemblies);
            Section(sb, "控制台", r.console);
            Section(sb, "脚本后端", r.backend);
            Section(sb, "HybridCLR API", r.hybridClrApi);
            Section(sb, "HybridCLR 设置", r.hybridClrSettings);

            if (r.sceneName != null)
            {
                sb.AppendLine("── 场景 ──");
                sb.AppendLine($"场景       : {r.sceneName}{(r.sceneIsDirty ? "（有未保存修改）" : string.Empty)}");
                sb.AppendLine($"EventSystem: {(r.eventSystemFound ? r.eventSystemObject : "❌ 不存在")}");
                foreach (var m in r.eventSystemModules) sb.AppendLine("  " + m);
                sb.AppendLine();
            }

            Section(sb, "提示", r.notes);
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, List<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            sb.AppendLine($"── {title} ──");
            foreach (var l in lines) sb.AppendLine("  " + l);
            sb.AppendLine();
        }
    }
}
