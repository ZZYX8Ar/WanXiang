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
    public static class EditorDiagnosticsBridge
    {
        private const string DiagDir = "Temp/WanXiangDiag";
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
