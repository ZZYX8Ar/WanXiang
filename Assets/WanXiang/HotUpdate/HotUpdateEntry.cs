// ============================================================================
//  万相 · 热更入口实现（热更侧）
//  ---------------------------------------------------------------------------
//  这个文件所在的程序集 WanXiang.HotUpdate 是**会被换掉的那一份**。
//  改这里的代码、只重新生成热更 DLL、不重新出包，运行期就应该看到新行为。
//
//  ★ 热更链路的验收标准 —— 现在有工具支撑了，不用再手工做：
//
//      1) 改下一行的标记（HOT-0001 → HOT-0002），保存
//      2) 跑「万相/热更/发布热更产物」  —— 编译热更 DLL + 复制进资源目录
//      3) 跑「万相/热更/体检」          —— 进 Play 走完整条链
//      4) 报告里的「运行标记」从 HOT-0001 变成 HOT-0002  ⇒  热更链路成立
//
//  ⚠ 这里换掉的是**运行期真正加载的那个 DLL**，不是重新编译整个工程。
//    编辑器的脚本编译（Library/ScriptAssemblies）不算数 —— 那份不会进包。
//    只有「发布热更产物」复制进 Assets/WanXiangRes/HotUpdate/ 的那一份才会。
//
//  ---------------------------------------------------------------------------
//  热更层写代码时的四条边界规则（违反任一条都会在打包后才炸，编辑器里看不出来）：
//
//  ① 只能引用 AOT 程序集，且 AOT 程序集**不能反过来引用本程序集**。
//     跨层通信必须走 AOT 侧定义的接口（如 IHotUpdateEntry）或委托。
//
//  ② 泛型用「引用类型」实参基本无事；用「值类型」实参（struct、enum、int…）
//     会在 IL2CPP 下需要 AOT 侧存在对应实例化，否则运行时报
//     ExecutionEngineException。解法是 HybridCLR 的 AOT 泛型元数据补全。
//     ⚠ 本工程的事件类型是 struct（见 InputEvents.cs），将来热更侧若用
//     QFramework 的 SendEvent<T> 发结构体事件，就会踩到这一条。
//     ★ 为了让这条链路**不是死代码**，本工程在 AOT 侧放了 AOTMetadataProbe，
//       并由下面的 Initialize() 主动调用它 —— 理由见 AOTMetadataProbe.cs 文件头。
//
//  ③ 用到 AOT 类型时，该类型必须在 link.xml 里保留，否则被 il2cpp 裁掉，
//     运行时报 `method body is null`。HybridCLR 的 Generate/LinkXml 负责生成。
//
//  ④ 不要在热更代码里写静态字段来跨「热重载」保状态 —— 卸载程序集时静态字段
//     会一起没掉。需要跨重载保留的数据要放到 AOT 侧或序列化出去。
// ============================================================================

using UnityEngine;
using WanXiang.Framework.HotUpdate;

namespace WanXiang.HotUpdate
{
    /// <summary>
    /// 热更模块入口。AOT 侧的 <see cref="HotUpdateLoader"/> 会反射创建本类，
    /// 并当作 <see cref="IHotUpdateEntry"/> 使用。
    /// </summary>
    /// <remarks>
    /// 三个硬性要求（缺一个就会在加载时报出明确异常，不会静默失败）：
    /// <list type="bullet">
    /// <item>类名与 <see cref="HotUpdateLoader.DefaultEntryTypeName"/> 完全一致</item>
    /// <item>实现 <see cref="IHotUpdateEntry"/></item>
    /// <item>有公开无参构造函数（默认就有，别写成带参构造）</item>
    /// </list>
    /// </remarks>
    public sealed class HotUpdateEntry : IHotUpdateEntry
    {
        /// <summary>
        /// 热更验证标记。★ 改这一行就能验证热更链路 —— 见文件头说明。
        /// </summary>
        /// <remarks>
        /// 命名说明：原名 <c>P4-0001</c> 是 P4 阶段（接 HybridCLR）的编号。
        /// 现在热更链路自成一块（任务 #12），改用 <c>HOT-</c> 前缀，
        /// 免得后来的人以为这串数字跟里程碑强绑定。
        /// </remarks>
        public const string BuildStamp = "HOT-0001";

        /// <summary>统计 Tick 次数，用来确认帧驱动真的接到了。</summary>
        private int _ticks;

        /// <summary>最近一次探针结果，供体检报告引用。</summary>
        private string _probeResult;

        /// <summary>
        /// 暴露探针结果，让体检脚本能核对「热更代码确实执行了」。
        /// </summary>
        public string ProbeResult => _probeResult;

        /// <inheritdoc />
        public string Version => BuildStamp;

        /// <inheritdoc />
        public void Initialize()
        {
            // 这里将来挂玩法的初始化：战斗系统注册、棋盘配置读取、节气表装载…
            // 现在做两件事：
            //   ① 跑一遍 AOT 泛型探针 —— 让「补元数据」这条链路被真正走到
            //   ② 把标记打出来 —— 作为热更生效的证据

            // ⚠ 探针方法被标了 [Obsolete]，是**故意的**：
            //   它只允许热更侧直接调用。一旦 AOT 侧也调了，或热更侧把它包进
            //   AOT 侧的辅助方法里，泛型实例化就落不到热更程序集的元数据表上，
            //   HybridCLR 的泛型分析会扫不到，名单当场变空 —— 而且是**静默**变空。
            //   所以这里的警告必须靠 pragma 压住，而不是靠删掉 [Obsolete]。
            //   详见 AOTMetadataProbe.cs 文件头（那里有实测经过）。
            //
            // ⚠⚠ 下面三行必须**留在这里**、**直接**调用，不要提取成 AOT 侧的工具方法。
            //     删掉任意一行，对应那类泛型实例化就不进名单了。
#pragma warning disable CS0618
            string probeSingle = AOTMetadataProbe.Describe(42);              // 泛型方法 + int
            string probePair = AOTMetadataProbe.DescribePair("hp", 100L);    // 双泛型参数 + long
            AOTMetadataProbe.ProbeBox<int> probeBox = AOTMetadataProbe.Box(7); // 泛型类型 + int
#pragma warning restore CS0618

            // 读回来，防止被优化掉；顺便作为"热更代码确实执行了"的证据。
            int probeReadBack = probeBox.Value;
            _probeResult = string.Join("；", new[]
            {
                "泛型方法=" + probeSingle,
                "双参泛型=" + probePair,
                "泛型类型=Int32(值 " + probeReadBack + ")",
            });

            Debug.Log($"[万相·热更] 热更程序集已加载并初始化 —— 标记 {BuildStamp}，"
                      + $"Unity {Application.unityVersion}，"
                      + $"{(Application.isEditor ? "编辑器" : "真机")}");
            Debug.Log($"[万相·热更] AOT 泛型探针：{_probeResult}"
                      + "（这行的存在本身就是证据：它用的是 AOT 侧的泛型方法/泛型类型，"
                      + "真机上必须靠补元数据才能跑起来）");
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
            _ticks++;
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            // 反注册事件、清静态缓存 —— 不清干净会导致热重载卸载失败。
            // 目前没有持有任何外部引用，所以只报一句。
            Debug.Log($"[万相·热更] 已关闭，本次运行共 Tick {_ticks} 次。");
            _ticks = 0;
        }
    }
}
