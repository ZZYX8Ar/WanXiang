// ============================================================================
//  WanXiang · UI 绑定标注
//  ---------------------------------------------------------------------------
//  数组字段的节点命名标注。编辑器校验器（UIBindValidationMenu）用它把
//  「字段 ↔ 节点」契约变成一键检查：
//      [BindArray("Cell_{0}", 9)]
//      [SerializeField] private RectTransform[] _cells;   // ⇒ Cell_0 … Cell_8
//  拖漏一个节点，打包前就会得到红字，而不是运行时空引用。
// ============================================================================

using System;

namespace WanXiang.Framework.UI
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class BindArrayAttribute : Attribute
    {
        /// <summary>节点名格式串，如 "Cell_{0}"。</summary>
        public string Format { get; }

        /// <summary>节点数量。</summary>
        public int Count { get; }

        public BindArrayAttribute(string format, int count)
        {
            Format = format;
            Count = count;
        }
    }
}
