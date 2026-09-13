// ============================================================================
//  WanXiang · 表结构签名（Schema Hash）
//  ---------------------------------------------------------------------------
//  解决参考代码中"反射按字段顺序读表 → 字段顺序改变会静默读错"的问题。
//
//  原理：把「字段名 + 字段类型 + 字段顺序」拼成一个字符串，算出稳定的 32 位哈希，
//        写进二进制文件头。加载时用代码里的字段定义重算一次并比对：
//          - 一致   → 正常读取
//          - 不一致 → 立刻抛异常，明确指出是哪张表的结构变了
//
//  这样做的价值：把一类「线上表现为数值莫名其妙不对、排查成本极高」
//  的隐性 bug，转换成「启动时一眼可见的显式错误」。
//
//  使用 FNV-1a 而非 MD5/SHA：我们只需要"变化检测"，不需要密码学强度，
//  FNV-1a 无依赖、无分配、计算极快。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace WanXiang.Core.Serialization
{
    public static class WXSchemaHash
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// 由「字段名 + 类型名」序列计算哈希。
        /// 顺序参与计算 —— 字段换位置也会导致哈希变化，这正是我们要的。
        /// </summary>
        public static int Compute(params (string name, string type)[] fields)
        {
            if (fields == null || fields.Length == 0) return 0;

            var sb = new StringBuilder(64);
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(fields[i].name).Append(':').Append(fields[i].type);
            }
            return Compute(sb.ToString());
        }

        /// <summary>由签名串计算哈希。签名串格式："id:int|name:string|atk:int"</summary>
        public static int Compute(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return 0;

            unchecked
            {
                uint hash = FnvOffsetBasis;
                // 直接对 UTF8 字节计算，避免中文/特殊字符带来的歧义
                var bytes = Encoding.UTF8.GetBytes(signature);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return (int)hash;
            }
        }

        /// <summary>对一组签名（多张表）计算整体哈希，用于版本清单。</summary>
        public static int ComputeSet(IEnumerable<string> signatures)
        {
            var list = new List<string>(signatures);
            list.Sort(StringComparer.Ordinal);   // 排序保证顺序无关
            return Compute(string.Join(";", list));
        }

        /// <summary>
        /// 生成人类可读的字段签名串。生成器（编辑器工具）与运行时都会调用它，
        /// 两边的字段列表必须由同一份 Excel 表头产生，从而保证一致。
        /// </summary>
        public static string BuildSignature(IEnumerable<KeyValuePair<string, string>> fields)
        {
            var sb = new StringBuilder(64);
            bool first = true;
            foreach (var kv in fields)
            {
                if (!first) sb.Append('|');
                first = false;
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            return sb.ToString();
        }
    }
}
