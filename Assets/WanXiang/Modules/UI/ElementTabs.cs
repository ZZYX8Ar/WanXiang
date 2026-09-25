// ============================================================================
//  万相 · 五行分类 tab 的**唯一**口径
//  ---------------------------------------------------------------------------
//  「全部 + 木/火/土/金/水」这套 tab 在 局外培养 / 图鉴 / 残卷阁 三处都要用。
//  以前每个面板各写一份，写法还不一样 —— 直接踩了坑：
//
//  ⚠ `Element` 枚举的值是 **None=0, Wood=1, Fire=2, Earth=3, Metal=4, Water=5**
//    （见 Battle/Core/Data/Types.cs），**不是** 0 起算的五元素！
//    所以 `(int)Element == tab - 1` 是错的：
//      tab=1（木）⇒ 比的是 0 = None ⇒ 永远筛不出东西（列表空白）
//      tab=2（火）⇒ 比的是 1 = Wood ⇒ 点「火」显示木系
//    （图鉴 Panel_Codex 与残卷阁 Panel_Archive 都栽在这行上。）
//
//  统一走这里：任何新面板做五行筛选，**只调 ElementTabs.Matches / Names**，别再自己推下标。
// ============================================================================

namespace WanXiang.Modules.UI
{
    public static class ElementTabs
    {
        /// <summary>0 = 全部。</summary>
        public const int All = 0;

        /// <summary>tab 总数（全部 + 五行）。</summary>
        public const int Count = 6;

        /// <summary>tab 顺序：全部 / 木 / 火 / 土 / 金 / 水（与图鉴、局外培养一致）。</summary>
        public static readonly string[] Names = { "全部", "木", "火", "土", "金", "水" };

        /// <summary>该异兽是否属于第 tab 个页签（0 = 全部恒真）。</summary>
        public static bool Matches(WanXiang.Battle.Core.Element e, int tab)
        {
            switch (tab)
            {
                case 1: return e == WanXiang.Battle.Core.Element.Wood;
                case 2: return e == WanXiang.Battle.Core.Element.Fire;
                case 3: return e == WanXiang.Battle.Core.Element.Earth;
                case 4: return e == WanXiang.Battle.Core.Element.Metal;
                case 5: return e == WanXiang.Battle.Core.Element.Water;
                default: return true;      // 0 或越界 ⇒ 全部
            }
        }

        /// <summary>五行中文名（无属性返回「无」）。</summary>
        public static string Cn(WanXiang.Battle.Core.Element e)
        {
            switch (e)
            {
                case WanXiang.Battle.Core.Element.Wood: return "木";
                case WanXiang.Battle.Core.Element.Fire: return "火";
                case WanXiang.Battle.Core.Element.Earth: return "土";
                case WanXiang.Battle.Core.Element.Metal: return "金";
                case WanXiang.Battle.Core.Element.Water: return "水";
                default: return "无";
            }
        }
    }
}
