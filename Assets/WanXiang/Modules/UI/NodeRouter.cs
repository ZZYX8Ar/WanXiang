// ============================================================================
//  万相 · 节点路由注册表（表现层重构 R5-b）
//  ---------------------------------------------------------------------------
//  从 CampaignPanel(3212 行) 的 switch(kind) 抽出的**可增删路由层**。
//
//  · 加节点 = Register 一行；删节点 = Remove / 传 null，落到默认「战斗节点 → 布阵」
//  · 换行为 = 改委托；注销全部 = Clear()
//  · 实例级持有（不是静态全局）⇒ 面板销毁后委托随之释放，不泄漏
//  · 复杂节点（Tale/Forge 等含业务逻辑）暂留原 switch，可逐个搬进 handler
//
//  ⛔ 不持有面板之外的状态，不做存档/推进；只负责"这个节点交给谁处理"。
// ============================================================================
using System.Collections.Generic;

namespace WanXiang.Modules.UI
{
    public delegate void NodeRouteHandler(WanXiang.Run.RunState run);

    public sealed class NodeRouter
    {
        private readonly Dictionary<WanXiang.Campaign.NodeKind, NodeRouteHandler> _handlers =
            new Dictionary<WanXiang.Campaign.NodeKind, NodeRouteHandler>();

        public int Count => _handlers.Count;

        /// <summary>注册/替换。传 null = 注销（该节点自动回退默认流程，不报错）。</summary>
        public void Register(WanXiang.Campaign.NodeKind kind, NodeRouteHandler handler)
        {
            if (handler == null) _handlers.Remove(kind);
            else _handlers[kind] = handler;
        }

        public void Remove(WanXiang.Campaign.NodeKind kind) => _handlers.Remove(kind);

        public bool Contains(WanXiang.Campaign.NodeKind kind) => _handlers.ContainsKey(kind);

        /// <summary>true = 已由注册表处理（调用方应 return）。</summary>
        public bool TryRoute(WanXiang.Campaign.NodeKind kind, WanXiang.Run.RunState run)
        {
            NodeRouteHandler h;
            if (!_handlers.TryGetValue(kind, out h) || h == null) return false;
            h(run);
            return true;
        }

        public void Clear() => _handlers.Clear();
    }
}
