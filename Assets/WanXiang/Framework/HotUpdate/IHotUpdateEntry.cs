// ============================================================================
//  万相 · 热更入口契约（AOT 侧）
//  ---------------------------------------------------------------------------
//  这个接口为什么放在 AOT（WanXiang.Runtime）里，而不是放在热更程序集里：
//
//    热更程序集是「会被换掉」的。如果 AOT 代码直接引用热更程序集里的类型，
//    编译期就形成了一个**指向会被替换掉的东西**的强依赖 —— 换一版热更 DLL，
//    AOT 侧的元数据就对不上了。所以正确的做法是：
//
//      AOT 定义接口  →  热更实现接口  →  AOT 只持有接口引用
//
//    AOT 侧用反射把热更程序集里的实现类 new 出来，转成这个接口，之后就像调用
//    普通对象一样调用它。HybridCLR 会自动生成桥接函数，让 AOT 与解释器
//    双向互调（interpreter → AOT 走原生调用，AOT → interpreter 走桥接）。
//
//  ⚠ 与 IInputService 一致的原则：本接口刻意**不继承** QFramework 的 IUtility。
//    理由同样是保住 WanXiang.Runtime 对 QFramework 的零依赖 —— 框架层不该
//    被某个具体的事件框架绑住，绑定发生在集成层。
// ============================================================================

namespace WanXiang.Framework.HotUpdate
{
    /// <summary>
    /// 热更模块的统一入口。由热更程序集里的实现类实现，由
    /// <see cref="HotUpdateLoader"/> 在运行期反射创建。
    /// </summary>
    /// <remarks>
    /// 刻意设计成「普通 C# 类 + 显式 Tick」，而不是 MonoBehaviour：
    /// <list type="bullet">
    /// <item>由 AOT 侧的驱动器决定何时 Tick，热更层不依赖 Unity 的生命周期回调，
    ///       换实现时不必考虑 GameObject 的挂载时机</item>
    /// <item>纯 C# 类的反射创建路径最短，不涉及 HybridCLR 的
    ///       「热更 MonoBehaviour 挂到资产上」那套特殊打包流程</item>
    /// </list>
    /// </remarks>
    public interface IHotUpdateEntry
    {
        /// <summary>
        /// 本份热更内容的版本标记。用于在运行期确认「跑的是哪一版」——
        /// 改一行重编、不出包就能变，这是热更是否真的生效的直接证据。
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 热更程序集加载完成后调用一次。玩法系统的注册、配置读取都应放这里。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 每帧调用。没有帧驱动需求时留空即可。
        /// </summary>
        void Tick(float deltaTime);

        /// <summary>
        /// 卸载前调用。用于反注册事件、释放句柄。
        /// </summary>
        /// <remarks>
        /// HybridCLR 支持 100% 卸载程序集（热重载），但前提是外部没有残留对
        /// 热更类型的引用。所以这个方法是给「热重载」留的钩子，实现时务必
        /// 把静态事件订阅、缓存都清干净，否则卸载会失败。
        /// </remarks>
        void Shutdown();
    }
}
