// ============================================================================
//  WanXiang · UI 框架 · 面板元数据注册表
//  ---------------------------------------------------------------------------
//  把 [UIPanel] 特性解析成运行时元数据，并缓存下来。
//
//  为什么要缓存：面板的层级/缓存策略在整个运行期是不变的，
//  而 Attribute.GetCustomAttribute 走的是反射，每次开关面板都调用一次是浪费。
//  更重要的是：把它集中到一处，将来要做"启动时全量校验面板配置"（比如检测
//  两个面板推导出了同一个资源 key、或 Popup 层面板没配遮罩）只有一个地方要改。
//
//  线程约定：只允许主线程调用。UI 系统不会在工作线程里开关面板，
//  这里刻意不加锁 —— 加了反而会掩盖"有人在非主线程动 UI"这个更严重的错误。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Framework.UI
{
    /// <summary>面板元数据注册表。</summary>
    public static class UIPanelRegistry
    {
        private static readonly Dictionary<Type, UIPanelMeta> Cache =
            new Dictionary<Type, UIPanelMeta>(64);

        /// <summary>获取面板元数据（带缓存）。</summary>
        public static UIPanelMeta Get<T>() where T : UIPanelBase => Get(typeof(T));

        /// <summary>获取面板元数据（带缓存）。</summary>
        public static UIPanelMeta Get(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (Cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var meta = Build(type);
            Cache[type] = meta;
            return meta;
        }

        /// <summary>清空缓存。Editor 下重新编译后调用，避免拿到旧的 Attribute 数据。</summary>
        public static void Clear()
        {
            Cache.Clear();
        }

        /// <summary>
        /// 全量扫描项目中所有面板类型，做一次配置一致性校验。
        /// 建议在 Editor 的 Play 前或 CI 阶段调用 —— 把"两个面板撞了资源 key"
        /// 这种问题拦在打包前，而不是等 QA 打开那个界面才白屏。
        /// </summary>
        public static List<string> Validate()
        {
            var problems = new List<string>();
            var keyOwners = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var type in EnumeratePanelTypes())
            {
                var meta = Get(type);

                if (string.IsNullOrEmpty(meta.Key))
                {
                    problems.Add($"{type.Name}：资源 key 为空。");
                    continue;
                }

                if (keyOwners.TryGetValue(meta.Key, out var owner))
                {
                    problems.Add(
                        $"资源 key 冲突：{type.Name} 与 {owner.Name} 都推导出 \"{meta.Key}\"。 " +
                        $"请在其中一个面板上用 [UIPanel(\"Panel_Xxx\")] 显式指定。");
                }
                else
                {
                    keyOwners.Add(meta.Key, type);
                }

                // Transient + Resident 以外的层配 Resident 策略通常是笔误
                if (meta.CachePolicy == UICachePolicy.Resident &&
                    meta.Layer == UILayer.Popup)
                {
                    problems.Add(
                        $"{type.Name}：Popup 层面板配了 Resident 缓存策略，" +
                        $"弹窗常驻会一直占内存且遮罩状态容易出错，建议改为 Transient。");
                }
            }

            return problems;
        }

        /// <summary>
        /// 枚举所有可实例化的面板类型。
        ///
        /// 用 AppDomain 而不是 typeof(UIPanelBase).Assembly —— 因为业务模块通常会用
        /// asmdef 做程序集隔离（这也是我们推荐的架构分层做法），
        /// 那样面板类根本不在框架所在的程序集里，只扫一个程序集会漏掉全部业务面板。
        /// </summary>
        private static IEnumerable<Type> EnumeratePanelTypes()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (Exception)
                {
                    // 动态程序集（如 Roslyn 编译出来的）取类型会抛异常，跳过即可
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    var t = types[j];
                    if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                    if (!typeof(UIPanelBase).IsAssignableFrom(t)) continue;
                    yield return t;
                }
            }
        }

        // ------------------------------------------------------------------

        private static UIPanelMeta Build(Type type)
        {
            var attr = FindAttribute(type);

            var meta = new UIPanelMeta
            {
                Key = ResolveKey(type, attr),
                Layer = attr != null ? attr.Layer : UILayer.Normal,
                CachePolicy = attr != null ? attr.CachePolicy : UICachePolicy.Cached,
                CloseOnMaskClick = attr != null ? attr.CloseOnMaskClick : true,
                FullScreen = attr != null ? attr.FullScreen : true,
            };

            // 非全屏面板点遮罩关闭是合理的默认；全屏面板若配了 CloseOnMaskClick，
            // 效果是"点面板外的空白区域也能关"，同样合理，这里不做干预。
            return meta;
        }

        private static string ResolveKey(Type type, UIPanelAttribute attr)
        {
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Key))
            {
                return attr.Key.Trim();
            }
            return UIPanelBase.DeriveDefaultKey(type);
        }

        /// <summary>
        /// 沿基类链查找特性。
        ///
        /// UIPanelAttribute 声明了 Inherited = false（每个具体面板都必须自己表态，
        /// 而不是悄悄继承中间基类的层级）。但完全不继承又会带来一个坑：
        /// 有人写了个 PopupPanelBase 标好 [UIPanel(Layer = Popup)]，
        /// 派生类忘了标，结果面板跑到 Normal 层去了 —— 而且没有任何报错。
        /// 所以这里手动往上找一层作为兜底，兼顾"显式优先"与"不静默出错"。
        /// </summary>
        private static UIPanelAttribute FindAttribute(Type type)
        {
            var current = type;
            while (current != null &&
                   current != typeof(object) &&
                   current != typeof(MonoBehaviour))
            {
                var attr = (UIPanelAttribute)Attribute.GetCustomAttribute(
                    current, typeof(UIPanelAttribute), inherit: false);
                if (attr != null) return attr;

                if (current == typeof(UIPanelBase)) break;
                current = current.BaseType;
            }
            return null;
        }
    }
}
