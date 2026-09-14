// ============================================================================
//  迷你 JSON 解析器（编辑器工具自用）
//  ---------------------------------------------------------------------------
//  为什么不用 Newtonsoft / JsonUtility：
//    - 工程没装 Newtonsoft，为一个导入器加包依赖不值当；
//    - JsonUtility 无法表达 beasts.json 的「对象数组 + 动态键」结构。
//  这个解析器只服务导入器：输入是 extract_gdd_data.py 生成的规整 JSON，
//  输出 Dictionary<string, object> / List<object> / string / double / bool / null。
//  **不是通用运行时库**，别拿到热更层去用。
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WanXiang.Editor.FusionTool
{
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var value = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length)
                throw new System.FormatException($"JSON 末尾有多余内容（位置 {i}）");
            return value;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new System.FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new System.FormatException($"JSON 期望 ':'（位置 {i}）");
                i++;
                obj[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return obj; }
                throw new System.FormatException($"JSON 期望 ',' 或 '}}'（位置 {i}）");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var arr = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return arr; }
                throw new System.FormatException($"JSON 期望 ',' 或 ']'（位置 {i}）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new System.FormatException($"JSON 期望字符串（位置 {i}）");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new System.FormatException("JSON \\u 转义截断");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: throw new System.FormatException($"未知转义 \\{e}");
                    }
                }
                else sb.Append(c);
            }
            throw new System.FormatException("字符串未闭合");
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new System.FormatException($"JSON 期望 {literal}（位置 {i}）");
            i += literal.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        /// <summary>便捷取值（导入器用）。</summary>
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static string Str(object o) => o as string;
        public static double Num(object o) => o is double d ? d : 0.0;
    }
}
