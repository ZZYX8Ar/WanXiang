// ============================================================================
//  万相 · 数据表 · 通用读表/解析工具（2026-11-17 代码骨架重构）
//  从 DataTables.cs 原样搬入，逻辑零改动。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace WanXiang.Modules.UI
{
    public static partial class DataTables
    {
        // ================================================================
        //  读表 / 解析工具
        // ================================================================

        private static List<string> Read(string name, out List<string> header)
        {
            header = null;
            var ta = Resources.Load<TextAsset>(Dir + name);
            if (ta == null)
            {
                Debug.LogWarning("[DataTables] 找不到 Resources/" + Dir + name + ".csv —— 该表用内置数值");
                return null;
            }
            var lines = SplitLines(ta.text);
            if (lines.Count < 2)
            {
                Problems.Add(name + ".csv：只有表头或为空");
                return null;
            }
            header = SplitLine(lines[0]);
            for (int i = 0; i < header.Count; i++) header[i] = header[i].Trim();

            var body = new List<string>(lines.Count - 1);
            for (int i = 1; i < lines.Count; i++)
                if (!string.IsNullOrWhiteSpace(lines[i])) body.Add(lines[i]);
            return body;
        }

        private static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            if (text == null) return list;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);   // Excel 存 UTF-8 会带 BOM
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    // CSV 里的 "" 是转义引号；这里只跟踪引号状态，引号本身留给 SplitLine 处理
                    inQuotes = !inQuotes;
                    sb.Append(c);
                }
                else if ((c == '\n' || c == '\r') && !inQuotes)
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    list.Add(sb.ToString());
                    sb.Length = 0;
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>RFC4180 单行拆分（支持引号包裹、"" 转义、引号内的逗号）。</summary>
        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            if (line == null) return cells;
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        private static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return Enum.TryParse(s.Trim(), true, out T v) ? v : fallback;
        }

        private static float Num(string s, float fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                ? v : fallback;
        }

        /// <summary>一行数据 + 列名索引；取值失败时把「文件 + 行号 + 原因」写进 problems。</summary>
        private sealed class Row
        {
            private readonly List<string> _header;
            private readonly List<string> _cells;
            private readonly string _file;
            private readonly int _line;
            private readonly List<string> _problems;

            public Row(List<string> header, string raw, string file, int line, List<string> problems)
            {
                _header = header;
                _cells = SplitLine(raw);
                _file = file;
                _line = line;
                _problems = problems;
            }

            public void Fail(string why) => _problems.Add(_file + " 第 " + _line + " 行：" + why);

            private string Raw(string col)
            {
                int idx = _header.IndexOf(col);
                if (idx < 0) { Fail("缺少列「" + col + "」"); return ""; }
                if (idx >= _cells.Count) return "";
                return _cells[idx].Trim();
            }

            public string Str(string col, string fallback = "")
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                // 导出器把换行写成字面 \n，这里还原
                return v.Replace("\\n", "\n");
            }

            public int Int(string col, int fallback)
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;
            }

            public float Float(string col, float fallback)
            {
                string v = Raw(col);
                if (string.IsNullOrEmpty(v)) return fallback;
                return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : fallback;
            }

            public bool Bool(string col, bool fallback)
            {
                string v = Raw(col).ToLowerInvariant();
                if (v == "") return fallback;
                if (v == "1" || v == "true" || v == "yes" || v == "y") return true;
                if (v == "0" || v == "false" || v == "no" || v == "n") return false;
                Fail("布尔值无法识别：" + v);
                return fallback;
            }

            public T Enum<T>(string col, T fallback) where T : struct
                => ParseEnum(Raw(col), fallback);
        }
    }
}
