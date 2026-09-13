// ============================================================================
//  万相 · 热更入口实现（热更侧）
//  ---------------------------------------------------------------------------
//  这个文件所在的程序集 WanXiang.HotUpdate 是**会被换掉的那一份**。
//  改这里的代码、只重新生成热更 DLL、不重新出包，运行期就应该看到新行为。
//
//  ★ P4 的验收标准就靠下面那个 BuildStamp：
//      1. 生成热更 DLL
//      2. 启动，日志里看到 标记 = P4-0001
//      3. 把 BuildStamp 改成 P4-0002，只重新生成 DLL
//      4. 再启动（不重新出包），日志变成 P4-0002  →  热更链路成立
//
//  ---------------------------------------------------------------------------
//  热更层写代码时的四条边界规则（违反任一条都会在打包后才炸，编辑器里看不出来）：
//
//  ① 只能引用 AOT 程序集，且 AOT 程序集**不能反过来引用本程序集**。
//     跨层通信必须走 AOT 侧定义的接口（如 IHotUpdateEntry）或委托。
//
//  ② 泛型用「引用类型」实参基本无事；用「值类型」实参（struct、enum、int…）
//     会在 IL2CPP 下需要 AOT 侧存在对应实例化，否则运行时报
//     ExecutionEngineException。解法是 HybridCLR 的 AOT 泛型元数据补全
//     （Generate/AOTGenericReferences + LoadMetadataForAOTAssemblies）。
//     ⚠ 本工程的事件类型是 struct（见 InputEvents.cs），将来热更侧若用
//     QFramework 的 SendEvent<T> 发结构体事件，就会踩到这一条。
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
        public const string BuildStamp = "P4-0001";

        /// <summary>统计 Tick 次数，用来确认帧驱动真的接到了。</summary>
        private int _ticks;

        /// <inheritdoc />
        public string Version => BuildStamp;

        /// <inheritdoc />
        public void Initialize()
        {
            // 这里将来挂玩法的初始化：战斗系统注册、棋盘配置读取、节气表装载…
            // 现在只做一件事：把标记打出来，作为热更生效的证据。
            Debug.Log($"[万相·热更] 热更程序集已加载并初始化 —— 标记 {BuildStamp}，"
                      + $"Unity {Application.unityVersion}，"
                      + $"{(Application.isEditor ? "编辑器" : "真机")}");
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
