// ============================================================================
//  万相 · 战斗表现层 · 色板
//  ---------------------------------------------------------------------------
//  颜色**不在这里发明**，全部来自 Docs/Design/data/palette.json（GDD 第五章 +
//  美术资产生产方案）。理由不是"守规范"，而是灰盒阶段就有真实意义：
//
//    灰盒要回答的是"战斗好不好看"。如果灰盒用的是随机色，
//    那么"宣纸底 + 墨描边 + 五行主体色"这套色彩关系根本没被验证过 ——
//    等美术进场才发现"五个属性挤在一起分不清谁是谁"，那时改的是机制不是颜色。
//
//  所以这个类做两件事：
//    ① 把 palette.json 里用得到的色值收在一处（编辑器灰盒窗口与运行时棋盘视图
//       **共用同一个源** —— 之前编辑器窗口自己抄了一份，那是最容易分叉的写法）
//    ② 提供 <see cref="VerifyAgainstDoc"/>：拿 palette.json 的原文来核对，
//       色值被改坏时能立刻发现，而不是等美术说"这个绿不对"
//
//  ⚠ 唯一一个不来自文档的是「阵亡灰」。文档没定义"死人该是什么颜色"，
//    灰盒自己取了一个降饱和的暖灰，明确标在 <see cref="GrayboxOnly"/> 里，
//    不参与文档核对 —— 免得下一个人以为它是漏登记的正式色。
// ============================================================================

using UnityEngine;
using WanXiang.Battle.Core;

namespace WanXiang.Battle.Presentation
{
    public static class BattlePalette
    {
        // ================================================================
        //  基础
        // ================================================================

        /// <summary>把 "#RRGGBB" 转成 Color。解析失败返回洋红 —— 画面上出现洋红就是硬错误，
        /// 比悄悄回退成白色更容易被发现。</summary>
        public static Color Hex(string h)
        {
            if (string.IsNullOrEmpty(h) || h.Length < 7) return Color.magenta;
            return new Color(
                System.Convert.ToInt32(h.Substring(1, 2), 16) / 255f,
                System.Convert.ToInt32(h.Substring(3, 2), 16) / 255f,
                System.Convert.ToInt32(h.Substring(5, 2), 16) / 255f,
                1f);
        }

        public static Color Mix(Color a, Color b, float t)
            => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ================================================================
        //  palette.json「墨」组
        // ================================================================

        /// <summary>焦墨 #1A1410 —— 最深描边、文字投影。血条底槽用它。</summary>
        public static readonly Color Soot = Hex("#1A1410");

        /// <summary>墨色 #2A2118 —— 全局主描边、正文文字。</summary>
        public static readonly Color Ink = Hex("#2A2118");

        /// <summary>淡墨 #6B6157 —— 次级文字、未激活图标。</summary>
        public static readonly Color InkSoft = Hex("#6B6157");

        /// <summary>宣纸 #F5F0E6 —— 全局背景底色。</summary>
        public static readonly Color Paper = Hex("#F5F0E6");

        /// <summary>素绢 #E9E2D2 —— 卡片底、面板底。</summary>
        public static readonly Color Silk = Hex("#E9E2D2");

        // ================================================================
        //  palette.json「金饰」组
        // ================================================================

        /// <summary>鎏金 #C9A063 —— 玄品边框、按钮描边、纹样主色。相生连线与怒气条用它。</summary>
        public static readonly Color Gold = Hex("#C9A063");

        /// <summary>赤金 #D4A94E —— 神品光效、稀有掉落。</summary>
        public static readonly Color GoldRich = Hex("#D4A94E");

        // ================================================================
        //  语义色（都取自文档，只是按用途起了名字）
        // ================================================================

        /// <summary>朱砂 #C8352C（火属性主体色）—— 暴击、受击高亮。</summary>
        public static readonly Color Crimson = Hex("#C8352C");

        /// <summary>松花 #BCE672（木属性高光）—— 护盾、生机、治疗。</summary>
        public static readonly Color Vital = Hex("#BCE672");

        /// <summary>⚠ 灰盒自定，**不在 palette.json 里**。阵亡单位的降饱和暖灰。</summary>
        public static readonly Color Dead = Hex("#9A948A");

        /// <summary>空格的底色。素绢稍微压暗一点，让"空位"和"有人的格子"一眼分开。</summary>
        public static readonly Color CellEmpty = Mix(Silk, Ink, 0.06f);

        // ================================================================
        //  五行主体色：下标 = (int)Element
        // ================================================================

        /// <summary>
        /// 五行主体色。<c>None</c> 用淡墨 —— 它不该出现在正常战斗里，
        /// 真出现了应该"看着就不对"。
        /// </summary>
        public static readonly Color[] Element =
        {
            Hex("#6B6157"),   // None  · 淡墨
            Hex("#789262"),   // 木 · 竹青
            Hex("#C8352C"),   // 火 · 朱砂
            Hex("#A26C43"),   // 土 · 赭石
            Hex("#C8CFD3"),   // 金 · 银鼠
            Hex("#42506B"),   // 水 · 黛蓝
        };

        public static Color OfElement(Element e)
        {
            int i = (int)e;
            if (i < 0 || i >= Element.Length) return Element[0];
            return Element[i];
        }

        // ================================================================
        //  与 palette.json 对账
        // ================================================================

        /// <summary>本类**声称**取自 palette.json 的全部色值。</summary>
        public static readonly string[] DocHexes =
        {
            "#1A1410", // 焦墨
            "#2A2118", // 墨色
            "#6B6157", // 淡墨
            "#F5F0E6", // 宣纸
            "#E9E2D2", // 素绢
            "#C9A063", // 鎏金
            "#D4A94E", // 赤金
            "#C8352C", // 朱砂（火 · 主体）
            "#BCE672", // 松花（木 · 高光）
            "#789262", // 竹青（木 · 主体）
            "#A26C43", // 赭石（土 · 主体）
            "#C8CFD3", // 银鼠（金 · 主体）
            "#42506B", // 黛蓝（水 · 主体）
        };

        /// <summary>明确标为"灰盒自定"的色值。它们**不该**出现在 palette.json 里。</summary>
        public static readonly string[] GrayboxOnly =
        {
            "#9A948A", // 阵亡灰
        };

        /// <summary>
        /// 拿 palette.json 的原文核对：本类引用的每个色值都必须真的在文档里。
        /// 返回 null 表示通过，否则返回人话描述的差异。
        /// <para>
        /// 为什么是"搜字符串"而不是解析 JSON：这里只需要回答"这个色值还算不算文档里的"，
        /// 引一个 JSON 库来干这件事，收益为零、维护成本不为零。
        /// </para>
        /// </summary>
        public static string VerifyAgainstDoc(string jsonText)
        {
            if (string.IsNullOrEmpty(jsonText)) return "palette.json 读不到（路径或编码不对）";

            var missing = new System.Text.StringBuilder();
            for (int i = 0; i < DocHexes.Length; i++)
            {
                // 大小写不敏感：文档里统一大写，但别人改文档时未必记得
                if (jsonText.IndexOf(DocHexes[i], System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (missing.Length > 0) missing.Append('、');
                missing.Append(DocHexes[i]);
            }
            if (missing.Length > 0)
                return "这些色值在 palette.json 里找不到了：" + missing + "（要么文档改了、要么这里抄错了）";

            return null;
        }
    }
}
