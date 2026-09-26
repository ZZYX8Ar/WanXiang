// ============================================================================
//  万相 · 编辑器操作通道（仅编辑器）
//  ---------------------------------------------------------------------------
//  为什么需要这个文件：
//    MCP 桥接链路（WorkBuddy → 沙箱代理 → Python 服务 → Unity 编辑器）是
//    **有状态**的，而那个 session 是绑在 Unity 侧那条连接上的。Unity 一重启，
//    旧连接断掉，Python 服务就把这个 session 名下的东西全部作废，而客户端
//    **不会自动重新握手**。此后所有 MCP 工具统一返回 "MCP session not found"。
//    （实测：此时 /health 依然 200、initialize 依然能拿到新 session，
//     所以是客户端的问题，服务本身没病。）
//
//    而 P4 的每一步 —— 切脚本后端、装 HybridCLR 环境、改热更程序集设置 ——
//    都只能通过编辑器 API 完成，没有 MCP 就寸步难行。
//
//    本文件提供一条**完全绕开 MCP** 的通道：
//
//        写请求文件  →  Unity 自动执行  →  写报告文件  →  任意工具读
//
//  请求文件： Temp/WanXiangDiag/request.txt    （命令名，一行一个，# 开头是注释）
//  报告文件： Temp/WanXiangDiag/report.json    （JSON；控制台也会打一份可读版）
//  附带文件： Temp/WanXiangDiag/pending.txt    （refresh 之后排队的命令）
//             Temp/WanXiangDiag/domain.stamp   （域指纹：每次域重载换一个新 GUID）
//             Temp/WanXiangDiag/pending.stamp  （排队时的域指纹 + 时间戳，升格判据）
//
//  ⭐ 最重要的一条命令是 refresh：
//    它会调 AssetDatabase.Refresh()。有了它，改完 .cs 文件后**不需要把 Unity
//    窗口切到前台**也能让它重新编译 —— 写 refresh 进请求文件，等几秒即可。
//    这是本通道存在的主要理由（编辑器在后台时会推迟资产刷新，否则每次改代码
//    都要人工去点一下窗口）。
//
//  命令一览：
//    refresh            触发资产刷新与重新编译（改完代码后用这个）
//    input              输入后端是否真的生效
//    types              关键类型落在哪个程序集（按简名搜，asmdef 分层体检）
//    scene              当前场景的 EventSystem 与模块
//    asmdefs            列出工程里所有 .asmdef 及其引用
//    packages           指定包是否已装、版本、来源
//    assemblies         已加载的程序集清单（分辨「没加载」与「名字写错」）
//    console            读 Unity 控制台最近 30 条（反射 LogEntries）
//    backend            脚本后端 / API 兼容级别 / unsafe 现状
//    backend.il2cpp     把平台脚本后端切成 IL2CPP
//    backend.netframework  把 Api Compatibility Level 切成 .NET Framework（★ 会改设置）
//    hybridclr.api      反射枚举 HybridCLR 编辑器 API 的完整签名
//    hybridclr.dump     反射导出 HybridCLR 设置的全部字段
//    hybridclr.status   问 InstallerController：环境装了没
//    hybridclr.install  从 git 远程安装（内部会 git clone，网络不好会失败）
//    hybridclr.install-local  从本地已合并的 libil2cpp 安装（推荐，无 git/网络依赖）
//    hybridclr.set-hotupdate  把 WanXiang.HotUpdate 登记为热更程序集（★ 会改设置，非只读）
//    hybridclr.generate 触发菜单项 HybridCLR/Generate/All（★ 会生成代码，非只读）
//    hybridclr.compile-dll  只跑 Generate/All 的第 1 步（编译热更 DLL），验证玩家编译用
//    hybridclr.outputs  核查 HybridCLR 的产物到底落盘了没有
//    player.refs        读 Bee 的 response 文件，报告玩家编译引用集与失败清单
//    player.il2cpp      查 Windows 平台的 IL2CPP 后端到底装了没有（只读）
//    player.dev.on      把玩家构建切成 development（★ 会改 EditorUserBuildSettings）
//    player.dev.off     把玩家构建切回非 development（★ 会改 EditorUserBuildSettings）
//    facade.inject      写 Assets/csc.rsp 注入 UnityEngine 门面（★ 会新增文件）
//    facade.clear       删掉 Assets/csc.rsp（★ 会删文件）
//    csc.defines.on     在 csc.rsp 里补 -define:ENABLE_UNITY_COLLECTIONS_CHECKS（★ 会改文件）
//    csc.defines.off    移除上面那个宏（★ 会改文件）
//    csc.dump           打印 Assets/csc.rsp 的当前内容
//    yoo.setup          建资源目录/样本资源 + 配好 YooAsset 收集器并保存（★ 会改配置，非只读）
//    yoo.status         打印当前收集器配置（只读）
//    yoo.testassets     只补建测试样本资源，不动收集器（★ 会新增文件）
//    hot.publish        发布热更产物：编译热更 DLL + 重算 AOT 泛型引用 + 复制进资源目录
//                       + 写 AOT 清单 + 配收集器（★ 会改文件与配置，非只读）
//    hot.status         打印热更产物现状：HybridCLR 侧 / 资源侧 / 收集器（只读）
//    hot.smoke          触发热更链路体检：写请求文件并进入 Play 模式（★ 会切 Play）
//    fusion.selftest    融合管线自检（GDD STEP 2 验收①②③机器判据；Edit 模式，不切 Play）
//    share.selftest     分享码自检（GDD STEP 3 验收③：≤90 字符 + 对局 1:1 复现；Edit 模式）
//    weather.selftest   天时系统自检（GDD STEP 3；含基准局指纹回归保护；Edit 模式）
//    resource.smoke     触发资源链路体检：写请求文件并进入 Play 模式（★ 会切 Play）
//    play.enter         只进 Play 模式、不跑体检（对照实验：验证播放器循环在不在跑）
//    play.exit          退出 Play 模式（体检卡住时捞一把；会先解除暂停）
//    play.state         报告 isPlaying / isPaused / isFocused / frameCount（只读）
//    all                跑一遍全部只读诊断
//
//  ℹ play.state 是排查"Play 开着但游戏侧什么都不动"的第一手段：
//    **暂停态（EditorApplication.isPaused）下，Play 看起来是开着的，
//    但所有 Update / 异步操作都不推进** —— 而 EditorApplication.update 照跑，
//    所以通道还活着。光看 isPlaying 分辨不出来，必须看 isPaused；
//    隔几秒连读两次 frameCount 没变化，就是铁证。
//
//  ℹ play.enter 存在的意义：把"环境能不能跑播放器"与"我们的资源代码对不对"
//    这两件事分开。空场景都不推进帧，就不是资源代码的问题。
//
//  ℹ resource.smoke 与 refresh **不要写在同一个请求里**：refresh 可能触发域重载，
//    会让后续动作被掐掉。分两次发。
//  ℹ resource.smoke 要求场景**没有未保存改动**，否则进 Play 时 Unity 会弹模态框
//    把自动化流程卡死。有改动时命令会直接拒绝并说明原因。
//
//  ℹ refresh 是一道**分水岭**，不是普通命令：
//      写在 refresh 之前的命令，由当前已编译的版本执行；
//      写在 refresh 之后的命令，会先暂存到 pending.txt，等**域重载确实发生**之后
//      由 Poll 自动升格执行。
//    所以「改完 .cs → 让 Unity 重编译 → 立刻用新命令」可以写在同一个请求文件里：
//
//        backend.il2cpp
//        refresh
//        types
//        hybridclr.install-local
//
//  ⚠ 注意「等域重载确实发生」这几个字。早期版本是「下一轮 Poll 就升格」，
//    实测**必错**：AssetDatabase.Refresh() 只是把编译交给 Bee 异步做
//    （实测 1.49s 构建 + 10.58s 装配件加载），这十几秒里 EditorApplication.update
//    照常 tick，于是 pending 被提前升格、命令跑在**旧程序集**上，症状是刚加的
//    命令报「⚠ 未知命令」。现在用域指纹把关：OnLoad 每次域重载都往
//    domain.stamp 写一个新 GUID，排队时把当时的 GUID 记进 pending.stamp，
//    两者不同才放行；另配 90 秒超时，防「没引起重编译时永久卡死」。
//
//  触发方式（两条并存，互不冲突）：
//    1. 自动：[InitializeOnLoadMethod] 挂 EditorApplication.update 轮询，每秒
//       看一次请求文件。**写好请求后不需要重编译**，等一秒就有结果。
//    2. 手动：菜单「万相 / 诊断 / 执行诊断请求」。
//
//  ⚠ 唯一前提：Unity 必须已经**编译过本脚本**。编辑器在后台时会推迟资产刷新，
//    所以首次使用请把 Unity 窗口切到前台点一下 —— 之后就可以全程用 refresh
//    命令自己驱动了。
//
//  ⚠ Temp/ 在 .gitignore 里，报告文件不会进版本库。
//
//  ⚠ 设计约束：本文件**不直接引用** HybridCLR / YooAsset 的类型，一律走反射。
//    原因：如果 MCP 或某个包解析失败导致引用不成立，本文件自己就编译不过，
//    那这条通道会在最需要它的时候失效。反射换来的是「永远能编译」。
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
    /// <summary>
    /// 基于文件的编辑器操作通道。用途与命令表见文件头。
    /// </summary>
    public static partial class EditorDiagnosticsBridge
    {        private const string DiagDir = "Temp/WanXiangDiag";
        private const string RequestPath = DiagDir + "/request.txt";
        private const string PendingPath = DiagDir + "/pending.txt";
        private const string ReportPath = DiagDir + "/report.json";

        /// <summary>域指纹文件。每次 OnLoad（＝ 每次域重载）都会覆写成新的 GUID。</summary>
        private const string DomainStampPath = DiagDir + "/domain.stamp";

        /// <summary>排队 pending 时留下的「当时的域指纹 + 时间戳」。</summary>
        private const string PendingStampPath = DiagDir + "/pending.stamp";

        /// <summary>
        /// 等域重载的最长时间。超时兜底放行 —— 否则当这次 refresh 没引起任何脚本变化
        /// （域重载根本不会发生）时，排队的命令会被永久卡住。
        /// </summary>
        private static readonly TimeSpan PendingWaitTimeout = TimeSpan.FromSeconds(90);

        /// <summary>refresh 看门狗补发的间隔与次数上限。见 <see cref="TryWatchdogRefresh"/>。</summary>
        private const double RefreshRetryIntervalSeconds = 20.0;
        private const int MaxRefreshAttempts = 4;

        /// <summary>轮询间隔（秒）。File.Exists 开销可忽略，1 秒足够跟手。</summary>
        private const double PollIntervalSeconds = 1.0;

        /// <summary>
        /// 全局编译器响应文件。Unity 官方文档记载：它作用于**非编辑器**脚本编译，
        /// 即正好覆盖玩家编译这条路径。用途见 <see cref="InjectFacadeReference"/>。
        /// </summary>
        private const string CscRspAssetPath = "Assets/csc.rsp";

        /// <summary>
        /// 通过 Assets/csc.rsp 往玩家编译里补的宏。用途见 <see cref="SetCollectionChecks"/>。
        ///
        /// ⭐ 这不是"随手加个宏"，而是**让分支与引用程序集对上**的必需手段：
        ///   ⭐⭐ 判定规则：**宏必须与「玩家编译实际引用的那份 CoreModule」对上**,
        ///   而不是「有宏就对、没宏就错」。方向会随环境翻转，两种情形正好相反：
        ///
        ///     ┌ 玩家编译引用 **编辑器版**（3 参 ctor）→ 宏必须 **开**
        ///     │   这是「平台 IL2CPP 模块没装」时的退路：安装里没有 il2cpp 变体目录，
        ///     │   Unity 只能退回 Data/Managed/UnityEngine/。此时若不补宏，
        ///     │   集合包走 #else 去要 2 参构造，编辑器版里没有 ⇒ CS7036。
        ///     └ 玩家编译引用 **Variations/&lt;后端&gt;**（2 参 ctor）→ 宏必须 **关**
        ///         装上 Windows Build Support (IL2CPP) 后走这条（实测已确认引用路径
        ///         变成 Variations/il2cpp/Managed/，玩家 rsp 里有 67 处 Variations）。
        ///         此时若还留着宏，集合包走 #if 去要 3 参构造，变体版里没有
        ///         ⇒ CS7036 原样复发。
        ///
        ///   实测（metaprobe 只读元数据）三份 UnityEngine.CoreModule.dll，
        ///   里 NativeArray&lt;T&gt;.ReadOnly 的形状不同：
        ///
        ///     · 编辑器版     1,538,560 B：只有 ctor(void* buffer, int length, ref AtomicSafetyHandle safety)
        ///                                 字段 m_Buffer / m_Length / **m_Safety**
        ///     · mono 变体    1,395,024 B：只有 ctor(void* buffer, int length)
        ///                                 字段 m_Buffer / m_Length
        ///     · il2cpp 变体  1,395,536 B：只有 ctor(void* buffer, int length)
        ///                                 字段 m_Buffer / m_Length
        ///
        ///   ⇒ **两个平台变体都只有 2 参构造**，3 参构造只存在于编辑器版。
        ///     所以「缺宏」本身不是错误信号 —— 要看引用的是哪一份。
        ///     判定实现见 <see cref="ProbePlayerReferences"/> 里的 CoreModule 引用探测。
        ///
        ///   于是 com.unity.collections@1.2.4 的 NativeList&lt;T&gt;.AsParallelReader()：
        ///     · 宏有 → 走 #if  分支，3 参构造 → 编辑器版里有、变体版里没有
        ///     · 宏无 → 走 #else 分支，2 参构造 → 变体版里有、编辑器版里没有
        ///   报错原文（Editor.log，player 图 638 evaluated，即上面第一种情形）：
        ///     NativeList.cs(839,24): error CS7036: There is no argument given that
        ///     corresponds to the required formal parameter 'safety' of
        ///     'NativeArray&lt;T&gt;.ReadOnly.ReadOnly(void*, int, ref AtomicSafetyHandle)'
        ///
        ///   连带失败：Unity.Collections.ref.dll 没产出 → 引用它的
        ///   Unity.2D.Animation.Runtime 也失败（**它不是独立问题**）。
        /// </summary>
        private const string CollectionChecksDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS";

        /// <summary>
        /// 额外宏的持久化副档。csc.rsp 本身不允许有注释（见 <see cref="InjectFacadeReference"/>），
        /// 也放不下"当前有哪些额外宏"这份状态，所以另存一份纯文本清单，
        /// 让 facade.inject 与 csc.defines.* 可以叠加而互不清除。
        /// </summary>
        private const string CscExtraDefinesPath = DiagDir + "/csc.defines.txt";

        /// <summary>请求与报告用的编码：不带 BOM，免得某些工具读出一串怪字符。</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static double _nextPollTime;
        private static bool _subscribed;

        /// <summary>本次域生命周期内的唯一 id。用途见 <see cref="DomainReloadedSincePending"/>。</summary>
        private static string _domainId;

        /// <summary>本轮请求里已经发过几次 refresh。域重载会把它清成 0（正是我们要的）。</summary>
        private static int _refreshAttempts;

        /// <summary>上一次发 refresh 的时刻（EditorApplication.timeSinceStartup）。</summary>
        private static double _lastRefreshTime;

        /// <summary>pending 在「Unity 空闲」状态下累计等待的秒数。用途见 <see cref="DomainReloadedSincePending"/>。</summary>
        private static double _pendingIdleSeconds;

        /// <summary>上次累计空闲时间的时刻，用来算增量。</summary>
        private static double _lastIdleTickTime;

        // ====================================================================
        //  生命周期
        // ====================================================================

        /// <remarks>
        /// 每次域重载都会跑一遍，所以必须防重复订阅 —— 否则 update 回调会随
        /// 重载次数线性累积，重载十次就变成每秒十个回调。
        ///
        /// 另外在这里刷新「域指纹」。它是 pending 能否升格的判据：
        /// 光凭「等几秒」是不够的（见 <see cref="DomainReloadedSincePending"/>）。
        /// </remarks>
        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            if (_subscribed) return;
            _subscribed = true;
            _nextPollTime = 0d;

            _domainId = Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(DiagDir);
                File.WriteAllText(DomainStampPath, _domainId, Utf8NoBom);
            }
            catch (Exception e)
            {
                // 指纹写不出去不该让整条通道失效，只是 pending 得不到保护
                Debug.LogWarning($"[万相通道] 写域指纹失败：{e.Message}");
            }

            EditorApplication.update += Poll;

            // ⚠ 播放期间必须接管"推进"，否则自动化会得到"安静地什么都没有"的结果。
            //   详见 KeepPlayModeAlive 里的说明。
            EditorApplication.update += KeepPlayModeAlive;
        }

        /// <summary>YooAssets 的类型全名（走反射，见文件头的设计约束）。</summary>
        private const string YooAssetsTypeName = "YooAsset.YooAssets, YooAsset";

        private static MethodInfo _yooAssetsUpdate;
        private static bool _yooAssetsUpdateResolved;

        /// <summary>
        /// 播放期间维持"推进"：催编辑器循环 + 代推 YooAsset 的异步操作系统。
        /// </summary>
        /// <remarks>
        /// ⚠⚠ 这是本会话里最难判读的一个环境坑，值得单独记一笔。
        ///
        ///   现象：`EditorApplication.isPlaying = true` 之后，Play 模式**看着是开着的**
        ///     · isPlaying=True
        ///     · isPaused=False              ← 不是暂停
        ///     · Application.isFocused=True   ← 也不是失焦
        ///   但游戏侧**什么都不动**：
        ///     · 脚本的 Update() 不执行（心跳日志一条都没有）
        ///     · YooAsset 的 OperationSystem.Update() 不推进（异步操作永远 Pending）
        ///     · 超时看门狗也不响
        ///   实测：进 Play 二十秒后 Time.frameCount 仍是 2，一百秒后是 3。
        ///   而 EditorApplication.update **照常在 tick** —— 所以诊断通道还活着，
        ///   这一点最容易把人带偏（会误以为主线程没卡，于是往业务代码里找问题）。
        ///
        ///   对照实验（关键）：**空场景进 Play、不跑任何我们的代码，帧数同样不涨**。
        ///   所以这不是资源代码的问题，是这台机器上编辑器不维持播放器循环。
        ///
        ///   做法：编辑器侧的 update 是活的，那就让它替停摆的循环干活。
        ///     ① QueuePlayerLoopUpdate()：Unity 给"编辑器没在刷新"的官方入口，
        ///        对播放器循环只能推动零星帧，但聊胜于无。
        ///     ② 代推 YooAsset：它的 OperationSystem 是纯 C#、靠 YooAssets.Update()
        ///        每帧泵一次。这本来由 [YooAssets] 那个驱动 GameObject 的 Update 负责，
        ///        播放器循环停摆时它就不跑了。既然在这里反射也能调，就替它调。
        ///
        ///     ⭐ 为什么代推就够：我们的 await 桥接是**事件驱动 + 同步唤醒**的
        ///       （Settle → TrySetResult → UniTask 的 continuation 被同步调用），
        ///       不依赖 UniTask 自己的 PlayerLoop runner。
        ///       所以只要有人推 YooAsset，整条异步链就能一路跑到底。
        ///       这一点在堆栈里被反复证实过，不是推测。
        /// </remarks>
        private static void KeepPlayModeAlive()
        {
            if (EditorApplication.isPlaying == false) return;
            if (EditorApplication.isPaused) return;

            EditorApplication.QueuePlayerLoopUpdate();

            // ---- 代推 YooAsset ----
            if (_yooAssetsUpdateResolved == false)
            {
                _yooAssetsUpdateResolved = true;

                Type type = Type.GetType(YooAssetsTypeName);
                if (type != null)
                {
                    _yooAssetsUpdate = type.GetMethod(
                        "Update", BindingFlags.NonPublic | BindingFlags.Static);
                }

                if (_yooAssetsUpdate == null)
                {
                    Debug.LogWarning("[万相通道] 取不到 YooAsset.YooAssets.Update，"
                                     + "播放器循环停摆时代推将不可用。");
                }
            }

            if (_yooAssetsUpdate == null) return;

            try
            {
                _yooAssetsUpdate.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                // 目标方法自己抛的：说清楚，别让它伪装成通道的问题。
                Debug.LogError($"[万相通道] 代推 YooAssets.Update 时目标抛异常："
                               + $"{ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[万相通道] 代推 YooAssets.Update 失败：{ex.Message}");
            }
        }

        private static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPollTime) return;
            _nextPollTime = now + PollIntervalSeconds;

            // refresh 会触发域重载、把 Run() 拦腰打断，所以「refresh 之后要跑的
            // 命令」在执行前会被暂存到 pending.txt；域重载完成后由这里升格为
            // 正式请求。这样「先重编译、再用新命令」在一个请求文件里就能表达，
            // 不需要人工发第二轮。
            AccumulatePendingIdleTime();
            TryPromotePending();

            // 闸门还关着、且 Unity 闲着呢 —— 说明上次 refresh 没引起重编译，补发一次。
            // 放在升格之后：万一这一轮刚好升格开了闸，看门狗自己就会停下。
            TryWatchdogRefresh();

            if (!File.Exists(RequestPath)) return;

            // 先删请求文件再执行：万一执行中抛异常，也不会陷入
            // 「每次轮询都重跑同一个坏请求」的死循环。
            string request;
            try
            {
                request = File.ReadAllText(RequestPath, Encoding.UTF8);
                File.Delete(RequestPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 读取请求文件失败：{e.Message}");
                return;
            }

            Run(request);
        }

        [MenuItem("万相/诊断/执行诊断请求", priority = 200)]
        public static void RunFromMenu()
        {
            if (!File.Exists(RequestPath))
            {
                Debug.Log($"[万相通道] 没有请求文件。请先创建 {RequestPath}，"
                          + "内容为 refresh / input / types / scene / all 等命令。");
                return;
            }
            Run(File.ReadAllText(RequestPath, Encoding.UTF8));
        }

        [MenuItem("万相/诊断/在控制台跑一遍只读诊断", priority = 201)]
        public static void RunAllToConsole()
        {
            Debug.Log("[万相通道]\n" + Render(BuildReport("all")));
        }

        // ====================================================================
        //  主流程
        // ====================================================================

        private static void Run(string request)
        {
            Report report;
            try
            {
                report = BuildReport(request ?? string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogError($"[万相通道] 构建报告时抛异常：{e}");
                return;
            }

            // 先落盘再谈副作用。
            // 这一点很要紧：refresh 会触发重新编译 → 域重载 → 本方法被拦腰打断，
            // 要是把写文件放在 refresh 后面，报告就永远写不出来。
            try
            {
                Directory.CreateDirectory(DiagDir);
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true), Utf8NoBom);
            }
            catch (Exception e)
            {
                Debug.LogError($"[万相通道] 写报告失败：{e.Message}");
            }

            // refresh 之后要跑的命令先落盘。必须赶在 refresh 真正触发之前写完，
            // 否则域重载会把这条语句连同 pending.txt 的写入一起打断。
            if (report.pending.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(DiagDir);

                    // ⚠ 指纹必须**先于** pending.txt 落盘。
                    // 升格判据读的是指纹：没有指纹时会退化成立刻放行（宽容模式），
                    // 那就正好回到「提前升格、跑在旧程序集上」的坑里。
                    //
                    // 这两个文件都得赶在 refresh 真跑起来前写完。refresh 被放在
                    // delayCall 里，所以此处还在同一帧内，来得及。
                    File.WriteAllText(PendingStampPath,
                        (_domainId ?? "unknown") + "|" + DateTime.UtcNow.Ticks, Utf8NoBom);

                    File.WriteAllText(PendingPath,
                        string.Join("\n", report.pending) + "\n", Utf8NoBom);

                    Debug.Log($"[万相通道] 已暂存 {report.pending.Count} 条命令到 pending.txt，"
                              + $"等域重载完成后自动继续（当前域指纹 {ShortId(_domainId)}）。");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[万相通道] 写 pending.txt 失败：{e.Message}");
                }
            }

            try
            {
                Debug.Log($"[万相通道] 已写出报告：{Path.GetFullPath(ReportPath)}\n" + Render(report));
            }
            catch (Exception e)
            {
                Debug.LogError($"[万相通道] 渲染控制台输出失败：{e.Message}");
            }

            ApplyDeferredEffects(report);
        }

        /// <remarks>
        /// 需要「晚一点再做」的动作放这里（比如触发重编译）。
        ///
        /// ⭐ 早期版本把 AssetDatabase.Refresh() 塞进 EditorApplication.delayCall，
        ///   理由是「先让报告与控制台输出完整跑完」。实测**这个理由是多余的，而代价很大**：
        ///     · 报告与 pending.txt 在调用到这里之前**已经同步落盘**了（见 Run），
        ///       所以没有任何东西需要等；
        ///     · 而 delayCall 会丢 —— 20:40:17 那次请求里，refresh 的 delayCall
        ///       从未被调用（Editor.log 里既没有它的输出，也没有随后的
        ///       "Asset Pipeline Refresh" 记录），于是源码改了却没重编译，
        ///       白白等满 90 秒超时才放行 pending。
        ///   现在直接调用，少一层不确定性。
        /// </remarks>
        private static void ApplyDeferredEffects(Report report)
        {
            // ⚠ 顺序：refresh 放在最后，因为它可能触发域重载 ——
            //   域重载会把本方法剩下的代码直接掐掉。
            //   所以「refresh + resource.smoke」**不要写在同一个请求文件里**，
            //   会表现成"进了 Play 但没跑体检"或者干脆没反应。
            //   正确用法：先发一个只含 refresh 的请求把代码编过，
            //             再发一个只含 resource.smoke 的请求。
            bool wantEnter = report.commands.Any(c => c == "resource.smoke"
                                                   || c == "hot.smoke"
                                                   || c == "play.enter");
            bool wantExit = report.commands.Any(c => c == "play.exit");

            if (wantExit)
            {
                if (EditorApplication.isPlaying)
                {
                    // ⚠ 必须先解暂停：暂停状态下 isPlaying = false 可能不生效
                    //   （游戏循环停摆，退出流程也走不动）。
                    if (EditorApplication.isPaused)
                    {
                        Debug.Log("[万相通道] 退出前先解除编辑器暂停。");
                        EditorApplication.isPaused = false;
                    }

                    Debug.Log("[万相通道] 退出 Play 模式。");
                    EditorApplication.isPlaying = false;
                }
                // 退出 Play 同样会触发域重载，后面的动作不用再考虑了。
                return;
            }

            if (wantEnter)
            {
                // ⚠ 进 Play 之前一定先解暂停。残留的暂停态会让游戏循环整段停摆：
                //   进了 Play、控制台却一条日志都没有，看门狗也不响 —— 非常难判读。
                if (EditorApplication.isPaused)
                {
                    Debug.Log("[万相通道] 进入 Play 前先解除编辑器暂停。");
                    EditorApplication.isPaused = false;
                }

                Debug.Log("[万相通道] 进入 Play 模式，开始资源链路体检。");
                EditorApplication.isPlaying = true;
                return;
            }

            if (!report.commands.Any(c => c == "refresh")) return;
            _refreshAttempts = 0;
            DoRefresh(report, "请求内的 refresh");
        }

        /// <remarks>
        /// 带上 <c>ForceSynchronousImport</c>：让导入与随后的重新编译**同步跑完**，
        /// 而不是交给 Bee 异步去做。日志证据：异步路径下 Asset Pipeline Refresh
        /// 可能只花 0.016 秒（认为无事可做），而同步路径实测 20~22 秒且会一路走到
        /// "Reloading assemblies"。
        /// </remarks>
        private static void DoRefresh(Report report, string why)
        {
            _refreshAttempts++;
            _lastRefreshTime = EditorApplication.timeSinceStartup;

            string msg = $"refresh -> 第 {_refreshAttempts} 次调用 AssetDatabase.Refresh"
                         + $"(ForceUpdate|ForceSynchronousImport)，触发者：{why}";

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate
                                      | ImportAssetOptions.ForceSynchronousImport);
                report?.results.Add(msg);
                Debug.Log($"[万相通道] {msg}");
            }
            catch (Exception e)
            {
                report?.results.Add(msg + $" -> ❌ {e.GetType().Name}: {e.Message}");
                Debug.LogError($"[万相通道] {msg} -> ❌ {e}");
            }
        }

        /// <remarks>
        /// refresh 看门狗。
        ///
        /// 为什么需要：<c>AssetDatabase.Refresh()</c> **不保证**真的去重新编译 ——
        /// 它可能认为没有变化（实测有一次只花 0.016 秒就返回，源码改动被无视）。
        /// 一旦这样，pending 就只能干等 90 秒超时，迭代速度直接被拖成分钟级。
        ///
        /// 判据刻意做得保守：只在「有 pending 在等（说明确实期望一次重编译）」
        /// 且「Unity 既没在导入也没在编译」时，每 20 秒补发一次，最多 4 次。
        /// 一旦待编译状态出现就自然停下；闸门一开（pending 被升格）就彻底停止。
        /// </remarks>
        private static void TryWatchdogRefresh()
        {
            if (_refreshAttempts <= 0 || _refreshAttempts >= MaxRefreshAttempts) return;
            if (!File.Exists(PendingPath)) return;          // 闸门已开，不需要重编译了
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime < RefreshRetryIntervalSeconds) return;

            DoRefresh(null, "看门狗补发（上次 refresh 似乎没引起重编译）");
        }

        private static Report BuildReport(string request)
        {
            var report = new Report
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath,
                request = request.Trim(),
            };

            // 逐行取命令，去掉空行与注释。
            //
            // refresh 是一道分水岭：它会让脚本重新编译，也就是说 refresh 前后
            // 执行的其实是**不同版本**的桥接。所以 refresh 之后的命令不能现在跑，
            // 要存进 pending.txt，等域重载完成后再执行。
            // 这让「先重编译、再用刚写好的新命令」可以在一个请求文件里表达完。
            bool afterRefresh = false;

            foreach (var rawLine in report.request.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#")) continue;

                string lowered = line.ToLowerInvariant();

                if (lowered == "refresh")
                {
                    report.commands.Add(lowered);   // 必须有，ApplyDeferredEffects 靠它触发
                    afterRefresh = true;
                    continue;
                }

                if (afterRefresh)
                {
                    // 保留原始大小写 —— 这里可能是路径，不能小写化
                    report.pending.Add(line);
                }
                else
                {
                    report.commands.Add(lowered);
                }
            }

            foreach (string cmd in report.commands)
            {
                if (cmd == "refresh") continue;   // 它只是排队标记，不在这里执行

                try
                {
                    Dispatch(report, cmd);
                }
                catch (Exception e)
                {
                    report.results.Add($"{cmd} -> ❌ 抛异常 {e.GetType().Name}: {e.Message}");
                    Debug.LogError($"[万相通道] 命令 {cmd} 失败：{e}");
                }
            }

            return report;
        }

        /// <remarks>
        /// 把上一次 refresh 之后暂存的命令升格成正式请求。
        /// 只在没有正式请求时才动手 —— 正式请求优先级更高，不能被插队。
        ///
        /// ⭐ 为什么要加「域重载闸门」：
        ///   最初只写了「下一轮 Poll 就升格」，实测**必错**。
        ///   AssetDatabase.Refresh() 只是把编译交给 Bee 异步去做，实测日志是
        ///   「Tundra build success (1.49 seconds)」+「Loaded All Assemblies, in 10.581
        ///   seconds」；而这十几秒里 EditorApplication.update **照常在 tick**。
        ///   于是 pending 被提前升格，命令跑在**旧程序集**上 ——
        ///   症状是明明刚加了新命令，却报「⚠ 未知命令」，看起来像代码没生效。
        ///
        ///   所以升格必须等到「域确实重载过」这个事实出现。判据用域指纹：
        ///   每次 OnLoad 生成新 GUID 写进 domain.stamp；排队时把当时的 GUID
        ///   记进 pending.stamp。两者不同 = 域换过了 = 新代码已就位。
        ///
        ///   再加 90 秒超时兜底：若这次 refresh 没引起任何脚本变化（域重载压根不会
        ///   发生），指纹永远不变，排队的命令就永远卡死 —— 超时后放行。
        /// </remarks>
        private static void TryPromotePending()
        {
            if (!File.Exists(PendingPath)) return;
            if (File.Exists(RequestPath)) return;

            if (!DomainReloadedSincePending(out string why))
            {
                return;   // 静默等待，别刷日志
            }

            try
            {
                File.Move(PendingPath, RequestPath);
                try { File.Delete(PendingStampPath); } catch { /* 清不掉不影响正确性 */ }
                _pendingIdleSeconds = 0d;
                Debug.Log($"[万相通道] pending.txt 已升格为 request.txt（{why}），"
                          + "即将执行上次 refresh 之后排队的命令。");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[万相通道] 升格 pending.txt 失败：{e.Message}");
            }
        }

        /// <summary>
        /// 只在 Unity「真的闲着」时给 pending 累计等待时间。
        /// </summary>
        /// <remarks>
        /// ⭐ 为什么不能直接用挂钟时间（这是早期版本的实测缺陷）：
        /// 一次全量重编译（比如刚切过 Api Compatibility Level）可能要 1.5 分钟以上，
        /// 而域重载**恰恰在重编译结束时才发生**。若超时按挂钟算，90 秒一到就提前
        /// 放行，pending 就会跑在**旧程序集**上 —— 绕了一圈又回到当初那个坑。
        /// 实测：21:06:32 报告就走了超时路径放行，而那次编译直到 21:07:06 才结束。
        /// 改成只计空闲时间后，编译/导入期间不累加，超时就只对「Unity 闲着却迟迟
        /// 没域重载」这种真异常生效。
        /// </remarks>
        private static void AccumulatePendingIdleTime()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = _lastIdleTickTime <= 0d ? 0d : now - _lastIdleTickTime;
            _lastIdleTickTime = now;

            if (!File.Exists(PendingPath))
            {
                _pendingIdleSeconds = 0d;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            // 上限保护：断点/卡顿会让 delta 变成几十秒，不该算成「等待」
            if (delta > 0d && delta < 10d) _pendingIdleSeconds += delta;
        }

        /// <summary>
        /// 判断「排队之后，域真的重载过」。返回 false 表示还要继续等。
        /// </summary>
        /// <remarks>
        /// 宽容原则：指纹文件读不到/格式不对时**放行**。理由是这种情况说明通道被
        /// 手工干预过（比如人工删了 Temp 目录），此时卡住比放行更糟 —— 放行至少
        /// 还能干活，卡住就彻底死了。
        /// </remarks>
        private static bool DomainReloadedSincePending(out string why)
        {
            string stamp = null;
            try { stamp = File.ReadAllText(PendingStampPath, Encoding.UTF8).Trim(); }
            catch { /* 读不到 → 走宽容分支 */ }

            if (string.IsNullOrEmpty(stamp))
            {
                why = "没有指纹（宽容放行）";
                return true;
            }

            int bar = stamp.IndexOf('|');
            string queuedDomain = bar < 0 ? stamp : stamp.Substring(0, bar);
            long queuedTicks = 0;
            if (bar >= 0) long.TryParse(stamp.Substring(bar + 1), out queuedTicks);

            if (!string.IsNullOrEmpty(_domainId) && queuedDomain != _domainId)
            {
                why = $"域指纹 {ShortId(queuedDomain)} → {ShortId(_domainId)}，重载已发生";
                return true;
            }

            if (queuedTicks > 0
                && _pendingIdleSeconds > PendingWaitTimeout.TotalSeconds)
            {
                why = $"累计空闲等待已超过 {PendingWaitTimeout.TotalSeconds:0} 秒仍无域重载，超时兜底放行";
                return true;
            }

            why = null;
            return false;
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        private static void Dispatch(Report report, string cmd)
        {
            switch (cmd)
            {
                case "all":
                    ProbeInput(report);
                    ProbeTypes(report);
                    ProbeScene(report);
                    ProbeAsmdefs(report);
                    ProbePackages(report);
                    ProbeAssemblies(report);
                    ProbeBackend(report);
                    break;

                case "input": ProbeInput(report); break;
                case "types": ProbeTypes(report); break;
                case "scene": ProbeScene(report); break;
                case "asmdefs": ProbeAsmdefs(report); break;
                case "packages": ProbePackages(report); break;
                case "assemblies": ProbeAssemblies(report); break;
                case "console": ProbeConsole(report); break;
                case "backend": ProbeBackend(report); break;
                case "backend.il2cpp": SetIl2Cpp(report); break;
                case "backend.netframework": SetNetFramework(report); break;
                case "hybridclr.api": ProbeHybridClrApi(report); break;
                case "hybridclr.dump": DumpHybridClrSettings(report); break;
                case "hybridclr.status": ProbeHybridClrStatus(report); break;
                case "hybridclr.install": InstallHybridClr(report); break;
                case "hybridclr.install-local": InstallFromLocalDir(report); break;
                case "hybridclr.set-hotupdate": SetHotUpdateAssemblies(report); break;
                case "hybridclr.generate": GenerateAll(report); break;
                case "hybridclr.compile-dll": CompileHotUpdateDlls(report); break;
                case "hybridclr.outputs": ProbeHybridClrOutputs(report); break;
                case "player.refs": ProbePlayerReferences(report); break;
                case "player.il2cpp": ProbeIl2CppSupport(report); break;
                case "player.dev.on": SetPlayerDevelopment(report, true); break;
                case "player.dev.off": SetPlayerDevelopment(report, false); break;
                case "facade.inject": InjectFacadeReference(report); break;
                case "facade.clear": ClearFacadeReference(report); break;
                case "csc.defines.on": SetCollectionChecks(report, true); break;
                case "csc.defines.off": SetCollectionChecks(report, false); break;
                case "csc.dump": DumpCscRsp(report); break;
                case "yoo.setup": RunYooTool(report, "yoo.setup"); break;
                case "yoo.status": RunYooTool(report, "yoo.status"); break;
                case "yoo.testassets": RunYooTool(report, "yoo.testassets"); break;
                case "hot.publish": RunHotTool(report, "hot.publish"); break;
                case "hot.status": RunHotTool(report, "hot.status"); break;
                case "hot.smoke": RequestHotUpdateSmoke(report); break;
                case "battle.selftest": RunBattleSelfTest(report); break;
                case "battle.graybox": RunBattleTool(report, "battle.graybox"); break;
                case "fusion.selftest": RunFusionSelfTest(report); break;
                case "share.selftest": RunShareSelfTest(report); break;
                case "weather.selftest": RunWeatherSelfTest(report); break;
                case "campaign.selftest": RunCampaignSelfTest(report); break;
                case "meta.selftest": RunMetaSelfTest(report); break;
                case "pvp.selftest": RunPvpSelfTest(report); break;
                case "trials.selftest": RunTrialsSelfTest(report); break;
                case "resource.smoke": RequestResourceSmoke(report); break;
                case "play.enter": RequestPlayEnter(report); break;
                case "play.exit": RequestPlayExit(report); break;
                case "play.state": ReportPlayState(report); break;

                case "refresh":
                    // 说明写在这里而不是 DoRefresh 里：报告文件在本方法返回后、
                    // ApplyDeferredEffects 之前就落盘了，所以 DoRefresh 里补记的
                    // 结果进不了 report.json，只能在控制台看到。
                    report.results.Add("refresh -> 已排队；报告与 pending.txt 落盘后会直接调用 "
                                       + "AssetDatabase.Refresh(ForceUpdate|ForceSynchronousImport)，"
                                       + "若 20 秒内没引起重编译，看门狗会补发（最多 4 次）");
                    break;

                default:
                    report.results.Add($"{cmd} -> ⚠ 未知命令");
                    break;
            }
        }

        // ====================================================================
        //  input —— 输入后端是否真的生效
        // ====================================================================

        /// <remarks>
        /// 这里是「编译期正常、运行期炸」的经典现场。activeInputHandler 选 New 时，
        /// 依赖旧 Input API 的代码照样编译通过，但运行期抛
        /// InvalidOperationException。所以两件事必须分开验：
        ///   ① 工程设置里的值是多少（静态）
        ///   ② 设备实例真的存在吗（动态，只有这个能证伪）
        /// 硬指标是 Keyboard.current != null。**不要**只看设备数 —— 编辑器非播放
        /// 模式下设备数本来就可能为 0，会把正常情况误判成故障。
        /// </remarks>
        private static void ProbeInput(Report report)
        {
            report.inputHandlerRaw = ReadInputHandlerRaw();
            report.inputHandlerName = DescribeInputHandler(report.inputHandlerRaw);

            report.keyboardCurrent = Keyboard.current != null;
            report.mouseCurrent = Mouse.current != null;
            report.gamepadCurrent = Gamepad.current != null;

            try
            {
                report.inputSystemUpdateMode = InputSystem.settings != null
                    ? InputSystem.settings.updateMode.ToString()
                    : "(InputSystem.settings 为 null)";
            }
            catch (Exception e)
            {
                report.inputSystemUpdateMode = "(读取失败: " + e.GetType().Name + ")";
            }

            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            report.assetFound = asset != null;
            report.assetPath = InputAssetPath;
            if (asset == null)
            {
                report.notes.Add($"未能在 {InputAssetPath} 找到 InputActionAsset。");
                return;
            }

            report.mapCount = asset.actionMaps.Count;
            foreach (var map in asset.actionMaps)
            {
                var names = new List<string>();
                foreach (var action in map.actions) names.Add(action.name);
                report.maps.Add($"{map.name} ({map.actions.Count}) : {string.Join(", ", names)}");
            }

            // 只报前 8 条绑定显示名，够验收就行，不必刷满整个报告
            int emitted = 0;
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count && emitted < 8; i++, emitted++)
                    {
                        var b = action.bindings[i];
                        string display;
                        try { display = action.GetBindingDisplayString(i); }
                        catch (Exception e) { display = "(解析失败 " + e.GetType().Name + ")"; }

                        string flag = b.isComposite ? " [composite]"
                                    : b.isPartOfComposite ? " [part]" : string.Empty;
                        report.bindings.Add($"{map.name}/{action.name}[{i}] = {display}{flag}");
                    }
                    if (emitted >= 8) break;
                }
                if (emitted >= 8) break;
            }
        }

        // ====================================================================
        //  报告 DTO
        // ====================================================================

        /// <remarks>
        /// 用 public 字段而不是属性 —— JsonUtility 只序列化字段。
        /// </remarks>
        [Serializable]
        private class Report
        {
            public string timestamp;
            public string unityVersion;
            public string projectPath;
            public string request;
            public List<string> commands = new List<string>();
            public List<string> pending = new List<string>();
            public List<string> results = new List<string>();

            // 输入后端
            public int inputHandlerRaw = -1;
            public string inputHandlerName;
            public bool keyboardCurrent;
            public bool mouseCurrent;
            public bool gamepadCurrent;
            public string inputSystemUpdateMode;
            public bool assetFound;
            public string assetPath;
            public int mapCount;
            public List<string> maps = new List<string>();
            public List<string> bindings = new List<string>();

            // 类型
            public List<string> types = new List<string>();

            // 程序集 / 包 / 后端
            public List<string> asmdefs = new List<string>();
            public List<string> packages = new List<string>();
            public List<string> assemblies = new List<string>();
            public List<string> console = new List<string>();
            public List<string> backend = new List<string>();

            // HybridCLR
            public List<string> hybridClrApi = new List<string>();
            public List<string> hybridClrSettings = new List<string>();

            // 场景
            public string sceneName;
            public bool sceneIsDirty;
            public bool eventSystemFound;
            public string eventSystemObject;
            public List<string> eventSystemModules = new List<string>();

            public List<string> notes = new List<string>();
        }
    }
}
