// ============================================================================
//  WanXiang · UI 框架 · 基础定义
//  ---------------------------------------------------------------------------
//  设计前提：本框架不继承 QFramework 的任何基类，只依赖纯 C# 与 UniTask。
//    理由有三：
//      1. 可独立编译测试 —— 不导入 QFramework 也能跑起来看效果
//      2. 接入 QFramework 只需写一个薄适配器（见文件末尾注释），不侵入框架内部
//      3. UI 框架属于"框架层"，不该知道自己被哪个架构骨架托管
//
//  依赖：UniTask（Cysharp）
//    UPM 安装：https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WanXiang.Framework.UI
{
    /// <summary>
    /// UI 层级。
    ///
    /// 每一层对应一个独立的 Canvas —— 这不是洁癖，是移动端与低端 PC 的性能刚需。
    /// Unity 的 UI 合批以 Canvas 为单位：任何一个元素变动，会导致该 Canvas 下
    /// 整个网格重建（rebuild）。若所有界面共用一个 Canvas，战斗 HUD 上血条每帧跳动，
    /// 都会连带把背包里那 200 个格子一起重建。
    /// </summary>
    public enum UILayer
    {
        /// <summary>背景、场景视差。常驻。</summary>
        Background = 0,

        /// <summary>主界面、常驻 HUD（血条 / 技能 CD 环）。常驻。</summary>
        Main = 1000,

        /// <summary>常规面板：背包、图鉴、异兽详情。LRU 缓存。</summary>
        Normal = 2000,

        /// <summary>弹窗、确认框、二级选择。单次。</summary>
        Popup = 3000,

        /// <summary>高优先弹窗：获得神品、通关结算。单次。</summary>
        Overlay = 4000,

        /// <summary>飘字、轻提示、断线重连提示。常驻。</summary>
        Toast = 5000,

        /// <summary>加载遮罩、场景切换。常驻。</summary>
        Loading = 6000,

        /// <summary>调试面板，仅开发版编译。</summary>
        Debug = 9000,
    }

    /// <summary>
    /// 面板缓存策略。决定面板关闭后是被保留还是销毁。
    ///
    /// 选错策略的代价是双向的：该缓存的没缓存 → 每次打开都卡一下；
    /// 不该缓存的缓存了 → 内存被几十个用不到的面板占住。
    /// </summary>
    public enum UICachePolicy
    {
        /// <summary>常驻。加载后永不释放（主界面、HUD、Loading）。</summary>
        Resident,

        /// <summary>LRU 缓存。关闭后保留，超出容量时淘汰最久未使用的（背包、图鉴）。</summary>
        Cached,

        /// <summary>单次。关闭即释放（结算、活动弹窗）。</summary>
        Transient,
    }

    /// <summary>面板生命周期状态。由框架维护，业务只读。</summary>
    public enum UIPanelState
    {
        None = 0,
        Loading,     // 资源加载中
        Created,     // 已实例化，尚未打开
        Opening,     // OnOpen 执行中
        Opened,      // 已打开，可交互
        Paused,      // 被上层遮挡，不可交互
        Closing,     // OnClose 执行中
        Closed,      // 已关闭
    }

    /// <summary>
    /// 面板加载接口。
    ///
    /// 刻意做成接口，是为了让 UI 框架与具体资源系统解耦：
    ///   - 原型期：用 <c>ResourcesBasedPanelLoader</c> 即可跑通
    ///   - 正式期：换成 YooAsset 或 Addressables 实现
    /// UI 框架本身一行都不用改。
    /// </summary>
    public interface IUIPanelLoader
    {
        /// <summary>异步加载面板 Prefab。key 由 <see cref="UIPanelAttribute.Key"/> 指定。</summary>
        UniTask<GameObject> LoadPanelPrefabAsync(string key, CancellationToken ct);

        /// <summary>释放面板 Prefab 的引用。是否真正卸载由资源系统决定。</summary>
        void ReleasePanelPrefab(string key);
    }

    /// <summary>
    /// 面板元数据。标注在 <see cref="UIPanelBase"/> 的派生类上。
    ///
    /// 用特性而不是 Prefab 上的 SerializeField，是为了让面板配置跟着代码走：
    ///   代码 diff 里能看见"这个面板从 Normal 层挪到了 Popup 层"，
    ///   而不是埋在某个 .prefab 的二进制里没人发现。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class UIPanelAttribute : Attribute
    {
        /// <summary>资源 key。留空则自动推导为 <c>Panel_{类名去掉 Panel 后缀}</c>。</summary>
        public string Key;

        public UILayer Layer = UILayer.Normal;

        public UICachePolicy CachePolicy = UICachePolicy.Cached;

        /// <summary>点击遮罩是否关闭。仅 Popup / Overlay 层有效。</summary>
        public bool CloseOnMaskClick = true;

        /// <summary>
        /// 是否全屏铺满。
        ///
        /// ⚠ 这个字段**只管视觉**，不决定是否暂停下层 —— 暂停下层由所属层级决定
        /// （见 <see cref="UILayerUtil.NeedsMask"/>）。两者的分工是：
        ///   true  → 面板 RectTransform 被强制拉伸铺满屏幕；遮罩完全透明（靠它自己挡视线）
        ///   false → 保留 Prefab 自己设计的尺寸与位置（居中/靠边的弹窗）；遮罩用半透明黑
        ///
        /// 之所以拆开：一个居中的半屏确认框虽然"不满屏"，但它同样拦住了下层输入，
        /// 下层同样应该停。把两件事绑在一起就会出现"确认框开着，背包冷却还在跑"。
        /// </summary>
        public bool FullScreen = true;

        public UIPanelAttribute() { }
        public UIPanelAttribute(string key) { Key = key; }
    }

    /// <summary>UI 系统接口。业务通过它开关面板，不直接操作 GameObject。</summary>
    public interface IUISystem
    {
        /// <summary>打开面板（已打开则返回现有实例）。</summary>
        UniTask<T> OpenAsync<T>(object payload = null) where T : UIPanelBase;

        /// <summary>关闭指定面板。</summary>
        void Close<T>() where T : UIPanelBase;

        /// <summary>
        /// 关闭指定面板实例（非泛型版本）。
        /// 存在意义：<see cref="UIPanelBase.CloseSelf"/> 需要一个"我就是我"的调用路径，
        /// 而泛型 Close&lt;T&gt; 只能在编译期确定类型，拿不到运行时 this 的类型。
        /// </summary>
        void Close(UIPanelBase panel);

        /// <summary>关闭栈顶面板（Android 返回键 / 手柄 B 键的默认行为）。</summary>
        void CloseTop();

        /// <summary>
        /// 处理"返回"输入。
        /// 返回 true 表示已被消费（调用方不应再处理）；false 表示当前在主界面，
        /// 调用方可执行"再按一次退出"。
        /// </summary>
        bool HandleBack();

        /// <summary>关闭所有面板（切场景时使用）。</summary>
        void CloseAll(UILayer? onlyLayer = null);

        T Get<T>() where T : UIPanelBase;
        bool IsOpen<T>() where T : UIPanelBase;

        /// <summary>当前栈深度（不含常驻层）。</summary>
        int StackDepth { get; }

        /// <summary>预加载面板资源（不实例化），用于降低首次打开耗时。</summary>
        UniTask PreloadAsync<T>() where T : UIPanelBase;
    }

    /// <summary>
    /// 层级判定工具。
    ///
    /// 集中放在一处，是为了让「UI 系统」「面板基类」用同一套规则。
    /// 如果两处各写一份判定，迟早会出现"这层有遮罩但不暂停下层"这种自相矛盾的状态 ——
    /// 表现出来就是"弹窗打开着，背景的按钮按不了但动画还在跑"。
    /// </summary>
    public static class UILayerUtil
    {
        /// <summary>
        /// 该层是否需要遮罩 —— 这个判定同时就是"该层面板是否应暂停下层"的判据。
        ///
        /// 判据只有一句：**这一层是"覆盖在游戏画面上的界面"，还是"游戏画面本身的一部分"？**
        ///   · 覆盖层（Normal / Popup / Overlay）→ 需要遮罩，打开时暂停下层
        ///   · 常驻层（Background / Main / Toast / Loading / Debug）→ 不需要遮罩
        ///
        /// 为什么 Main 层不能有遮罩：主界面与 HUD 是最底层。
        /// 给它们套遮罩会同时导致两件事 —— 画面被莫名压暗、主界面自己的按钮点不动。
        /// 这是个很容易踩的坑，而且在冒烟测试里一眼就能看出来。
        ///
        /// 为什么 Toast 不需要：飘字是"叠加信息"，既不拦截输入，也不该让下层停下来。
        /// </summary>
        public static bool NeedsMask(UILayer layer)
        {
            return layer == UILayer.Normal
                || layer == UILayer.Popup
                || layer == UILayer.Overlay;
        }
    }
}
