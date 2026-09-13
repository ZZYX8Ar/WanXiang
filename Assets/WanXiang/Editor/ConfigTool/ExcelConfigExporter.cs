// ============================================================================
//  WanXiang · 配置表导出工具（编辑器）
//  ---------------------------------------------------------------------------
//  流程：Excel(.xlsx) → C# 数据类 + 二进制(.bytes) → 交由 Addressables 分发
//
//  Excel 表结构约定（列从 A 开始，行从 1 开始）：
//    第 1 行  字段名      id / name / element / atk        （英文，生成的字段名）
//    第 2 行  中文注释    编号 / 名称 / 五行 / 攻击          （仅生成注释，不参与逻辑）
//    第 3 行  类型        int / string / int / float        （显式声明，见下方支持列表）
//    第 4 行  主键标记     key  /  （空）                     （有且仅有一列标 key）
//    第 5 行+ 数据                                              （每行一条记录）
//
//  第 3 行的显式类型声明是本工具相对参考代码的关键改进：
//    原方案靠反射猜类型，把 int 改成 float 不会报错、只会读出垃圾数据。
//    显式声明后，类型是契约的一部分，SchemaHash 会捕捉任何变化。
//
//  支持类型：
//    int / long / float / bool / string
//    int[] / float[] / string[]   —— 单元格内用分号分隔，如 "1;2;3"
//
//  ── 启用状态：默认【不参与编译】 ─────────────────────────────────────────────
//  本工具依赖 NPOI 读写 .xlsx，而 NPOI 官方并不支持 Unity（它依赖 System.Drawing，
//  Unity 下需要额外的替代实现），把 5 个 DLL 塞进工程会带来不必要的依赖风险。
//
//  且配置文件方案已定为 Luban（见 Docs/ARCHITECTURE.md §3），Luban 是独立命令行
//  工具，不需要在 Unity 工程内放 NPOI。因此本文件作为【备选保留】，默认不编译。
//
//  需要启用时（例如 Luban 方案回退）：
//    1. 从 NuGet 下载 NPOI 的 .NET Standard 版本，解包后把下列 DLL 放进 Assets/Plugins/NPOI/
//         NPOI.dll · NPOI.OOXML.dll · NPOI.OpenXml4Net.dll · NPOI.OpenXmlFormats.dll
//         ICSharpCode.SharpZipLib.dll
//    2. Player Settings → Other Settings → Scripting Define Symbols 添加
//         WANXIANG_NPOI_EXPORTER
//    3. 菜单入口会自动出现（见文件末尾的 MenuItem）
// ============================================================================

#if UNITY_EDITOR && WANXIANG_NPOI_EXPORTER

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEditor;
using UnityEngine;
using WanXiang.Core.Serialization;

namespace WanXiang.Editor.ConfigTool
{
    public static class ExcelConfigExporter
    {
        // ------------------------------------------------------------------
        // 路径与常量
        // ------------------------------------------------------------------

        /// <summary>Excel 源表目录（相对工程根目录）</summary>
        public const string ExcelDir = "Config/Excel";

        /// <summary>生成的 C# 代码目录</summary>
        public const string CodeOutDir = "Assets/WanXiang/Config/Generated";

        /// <summary>生成的二进制目录（建议将其所在文件夹加入 Addressables Remote Group）</summary>
        public const string DataOutDir = "Assets/GameRes/Config/Binary";

        private const int RowFieldName = 0;   // 第 1 行
        private const int RowComment = 1;   // 第 2 行
        private const int RowType = 2;   // 第 3 行
        private const int RowKeyFlag = 3;   // 第 4 行
        private const int RowDataStart = 4;   // 第 5 行起

        // ------------------------------------------------------------------
        // 菜单入口
        // ------------------------------------------------------------------

        [MenuItem("WanXiang/配置表/导出全部表", priority = 100)]
        public static void ExportAll()
        {
            string absExcelDir = Path.Combine(GetProjectRoot(), ExcelDir);

            if (!Directory.Exists(absExcelDir))
            {
                EditorUtility.DisplayDialog("配置表导出",
                    $"未找到 Excel 目录：\n{absExcelDir}\n\n请先创建该目录并放入 .xlsx 表文件。", "知道了");
                return;
            }

            var files = Directory.GetFiles(absExcelDir, "*.xlsx", SearchOption.AllDirectories);
            var valid = new List<string>();
            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("~$")) continue;                 // Excel 临时锁文件
                if (name.StartsWith("_")) continue;                  // 下划线开头视为草稿，跳过
                valid.Add(f);
            }

            if (valid.Count == 0)
            {
                EditorUtility.DisplayDialog("配置表导出", "未找到可导出的 .xlsx 文件（_ 开头的会被跳过）。", "知道了");
                return;
            }

            EnsureDir(CodeOutDir);
            EnsureDir(DataOutDir);

            var errors = new List<string>();
            var summary = new StringBuilder();
            int okCount = 0;

            try
            {
                for (int i = 0; i < valid.Count; i++)
                {
                    string path = valid[i];
                    string tableName = Path.GetFileNameWithoutExtension(path);

                    bool cancel = EditorUtility.DisplayProgressBar(
                        "导出配置表", $"{tableName}  ({i + 1}/{valid.Count})", (float)i / valid.Count);
                    if (cancel) break;

                    try
                    {
                        var table = ParseExcel(path, tableName);
                        Validate(table);

                        string code = GenerateCode(table);
                        byte[] binary = GenerateBinary(table);

                        string codePath = Path.Combine(GetProjectRoot(), CodeOutDir, tableName + ".g.cs");
                        string dataPath = Path.Combine(GetProjectRoot(), DataOutDir, tableName + ".bytes");

                        File.WriteAllText(codePath, code, new UTF8Encoding(false));
                        File.WriteAllBytes(dataPath, binary);

                        okCount++;
                        summary.AppendLine(
                            $"  ✓ {tableName,-24} {table.Rows.Count,5} 行 · " +
                            $"{binary.Length / 1024f,7:F1} KB · Schema 0x{table.SchemaHash:X8}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"[{tableName}] {ex.Message}");
                        summary.AppendLine($"  ✗ {tableName,-24} 失败：{ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            var report = new StringBuilder();
            report.AppendLine($"导出完成：成功 {okCount} / 共 {valid.Count} 张表\n");
            report.AppendLine(summary.ToString());

            if (errors.Count > 0)
            {
                report.AppendLine("─────────────────────────────");
                report.AppendLine("以下问题需要修复：");
                foreach (var e in errors) report.AppendLine("  " + e);
                Debug.LogError("[配置表导出]\n" + report);
                EditorUtility.DisplayDialog("配置表导出 · 有错误", report.ToString(), "知道了");
            }
            else
            {
                Debug.Log("[配置表导出]\n" + report);
                EditorUtility.DisplayDialog("配置表导出 · 成功", report.ToString(), "好");
            }
        }

        [MenuItem("WanXiang/配置表/校验全部表（不导出）", priority = 101)]
        public static void ValidateAll()
        {
            string absExcelDir = Path.Combine(GetProjectRoot(), ExcelDir);
            if (!Directory.Exists(absExcelDir))
            {
                EditorUtility.DisplayDialog("配置表校验", "未找到 Excel 目录。", "知道了");
                return;
            }

            var files = Directory.GetFiles(absExcelDir, "*.xlsx", SearchOption.AllDirectories);
            var report = new StringBuilder();
            int ok = 0, bad = 0;

            foreach (var path in files)
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith("~$") || name.StartsWith("_")) continue;

                string tableName = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var table = ParseExcel(path, tableName);
                    Validate(table);
                    ok++;
                    report.AppendLine($"  ✓ {tableName}  ({table.Rows.Count} 行)");
                }
                catch (Exception ex)
                {
                    bad++;
                    report.AppendLine($"  ✗ {tableName}\n      {ex.Message}");
                }
            }

            report.Insert(0, $"校验完成：通过 {ok}，失败 {bad}\n\n");
            if (bad > 0) Debug.LogError("[配置表校验]\n" + report);
            else Debug.Log("[配置表校验]\n" + report);
            EditorUtility.DisplayDialog("配置表校验", report.ToString(), "好");
        }

        // ------------------------------------------------------------------
        // 解析
        // ------------------------------------------------------------------

        private class FieldDef
        {
            public int Column;
            public string Name;
            public string Comment;
            public string TypeText;     // Excel 中声明的原始类型文本
            public FieldType Type;      // 解析后的类型枚举
            public string CsType;       // 生成的 C# 类型
            public bool IsKey;
        }

        private enum FieldType { Int, Long, Float, Bool, String, IntArray, FloatArray, StringArray }

        private class TableDef
        {
            public string Name;
            public string Comment;
            public List<FieldDef> Fields = new List<FieldDef>();
            public List<List<string>> Rows = new List<List<string>>();
            public FieldDef KeyField;
            public int SchemaHash;

            public string Signature
            {
                get
                {
                    var parts = new List<string>();
                    foreach (var f in Fields) parts.Add($"{f.Name}:{f.TypeText}");
                    return string.Join("|", parts);
                }
            }
        }

        private static TableDef ParseExcel(string path, string tableName)
        {
            var table = new TableDef { Name = tableName };

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0);
                if (sheet == null) throw new Exception("找不到第一个工作表（Sheet0）。");

                IRow rowField = sheet.GetRow(RowFieldName);
                if (rowField == null) throw new Exception($"第 {RowFieldName + 1} 行（字段名）不存在。");

                IRow rowComment = sheet.GetRow(RowComment);
                IRow rowType = sheet.GetRow(RowType);
                IRow rowKey = sheet.GetRow(RowKeyFlag);
                if (rowType == null) throw new Exception($"第 {RowType + 1} 行（类型）不存在。");

                int lastCell = rowField.LastCellNum;
                for (int c = 0; c < lastCell; c++)
                {
                    string fieldName = GetCellText(rowField, c).Trim();
                    if (string.IsNullOrEmpty(fieldName)) continue;      // 空列：跳过

                    string typeText = GetCellText(rowType, c).Trim();
                    if (string.IsNullOrEmpty(typeText))
                        throw new Exception($"列 {ColName(c)}（字段 {fieldName}）缺少类型声明。");

                    var field = new FieldDef
                    {
                        Column = c,
                        Name = fieldName,
                        Comment = rowComment != null ? GetCellText(rowComment, c).Trim() : "",
                        TypeText = typeText,
                        Type = ParseType(typeText, fieldName),
                        IsKey = rowKey != null && GetCellText(rowKey, c).Trim().ToLower() == "key",
                    };
                    field.CsType = ToCsType(field.Type);
                    table.Fields.Add(field);
                }

                if (table.Fields.Count == 0) throw new Exception("未解析到任何字段。");

                // 主键
                foreach (var f in table.Fields) if (f.IsKey) { table.KeyField = f; break; }
                if (table.KeyField == null)
                {
                    // 未标注时默认第一列为主键，并给出提示
                    table.KeyField = table.Fields[0];
                    Debug.LogWarning($"[{tableName}] 未标注主键（第 {RowKeyFlag + 1} 行写 key），" +
                                     $"已默认使用首列「{table.KeyField.Name}」。");
                }

                // 数据行
                for (int r = RowDataStart; r <= sheet.LastRowNum; r++)
                {
                    IRow row = sheet.GetRow(r);
                    if (row == null) continue;

                    var values = new List<string>(table.Fields.Count);
                    bool allEmpty = true;
                    foreach (var f in table.Fields)
                    {
                        string v = GetCellText(row, f.Column).Trim();
                        values.Add(v);
                        if (!string.IsNullOrEmpty(v)) allEmpty = false;
                    }

                    if (allEmpty) continue;      // 整行为空：跳过（便于表尾留白）
                    table.Rows.Add(values);
                }
            }

            table.SchemaHash = WXSchemaHash.Compute(table.Signature);
            return table;
        }

        private static FieldType ParseType(string text, string fieldName)
        {
            switch (text.ToLowerInvariant())
            {
                case "int": return FieldType.Int;
                case "long": return FieldType.Long;
                case "float": return FieldType.Float;
                case "bool": return FieldType.Bool;
                case "string": return FieldType.String;
                case "int[]": return FieldType.IntArray;
                case "float[]": return FieldType.FloatArray;
                case "string[]": return FieldType.StringArray;
                default:
                    throw new Exception(
                        $"字段「{fieldName}」的类型「{text}」不支持。" +
                        $"可用：int / long / float / bool / string / int[] / float[] / string[]");
            }
        }

        private static string ToCsType(FieldType t)
        {
            switch (t)
            {
                case FieldType.Int: return "int";
                case FieldType.Long: return "long";
                case FieldType.Float: return "float";
                case FieldType.Bool: return "bool";
                case FieldType.String: return "string";
                case FieldType.IntArray: return "List<int>";
                case FieldType.FloatArray: return "List<float>";
                case FieldType.StringArray: return "List<string>";
                default: return "string";
            }
        }

        // ------------------------------------------------------------------
        // 校验
        // ------------------------------------------------------------------

        private static void Validate(TableDef table)
        {
            if (table.Rows.Count == 0)
                throw new Exception("表内没有数据行。");

            var keyIndex = table.Fields.IndexOf(table.KeyField);
            var seen = new HashSet<string>();

            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                int excelRow = i + RowDataStart + 1;

                // 主键非空 + 不重复
                string keyValue = row[keyIndex];
                if (string.IsNullOrEmpty(keyValue))
                    throw new Exception($"第 {excelRow} 行：主键「{table.KeyField.Name}」为空。");

                if (!seen.Add(keyValue))
                    throw new Exception($"第 {excelRow} 行：主键「{keyValue}」与前面重复。");

                // 类型可解析性
                foreach (var f in table.Fields)
                {
                    string v = row[f.Column];
                    if (string.IsNullOrEmpty(v)) continue;

                    bool ok = true;
                    switch (f.Type)
                    {
                        case FieldType.Int: ok = int.TryParse(v, out _); break;
                        case FieldType.Long: ok = long.TryParse(v, out _); break;
                        case FieldType.Float: ok = float.TryParse(v, out _); break;
                        case FieldType.Bool: ok = IsBoolText(v); break;
                        case FieldType.IntArray:
                            ok = TryParseArray(v, s => int.TryParse(s, out _)); break;
                        case FieldType.FloatArray:
                            ok = TryParseArray(v, s => float.TryParse(s, out _)); break;
                    }

                    if (!ok)
                        throw new Exception(
                            $"第 {excelRow} 行「{f.Name}」的值「{v}」无法按 {f.TypeText} 解析。");
                }
            }
        }

        private static bool IsBoolText(string v)
        {
            v = v.ToLowerInvariant();
            return v == "1" || v == "0" || v == "true" || v == "false" || v == "是" || v == "否";
        }

        private static bool TryParseArray(string text, Func<string, bool> parseOne)
        {
            foreach (var part in text.Split(';'))
            {
                var s = part.Trim();
                if (s.Length == 0) continue;
                if (!parseOne(s)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 生成 C# 代码
        // ------------------------------------------------------------------

        private static string GenerateCode(TableDef table)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     此文件由 WanXiang 配置表工具生成，禁止手动修改。");
            sb.AppendLine($"//     来源：Config/Excel/{table.Name}.xlsx");
            sb.AppendLine($"//     签名：{table.Signature}");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using WanXiang.Core.Serialization;");
            sb.AppendLine("using WanXiang.Framework.Config;");
            sb.AppendLine();
            sb.AppendLine("namespace WanXiang.Config.Generated");
            sb.AppendLine("{");

            // ---- 数据行类 ----
            sb.AppendLine($"    /// <summary>{table.Name} 表的数据行。</summary>");
            sb.AppendLine($"    public sealed class {table.Name}Row");
            sb.AppendLine("    {");
            foreach (var f in table.Fields)
            {
                if (!string.IsNullOrEmpty(f.Comment))
                    sb.AppendLine($"        /// <summary>{f.Comment}</summary>");
                sb.AppendLine($"        public {f.CsType} {f.Name};");
            }
            sb.AppendLine();

            // 读取
            sb.AppendLine("        internal static " + table.Name + "Row Read(WXBinaryReader r)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return new {table.Name}Row");
            sb.AppendLine("            {");
            foreach (var f in table.Fields)
                sb.AppendLine($"                {f.Name} = {ReadExpr(f)},");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 写入（供导出工具与存档调试使用）
            sb.AppendLine("        internal static void Write(WXBinaryWriter w, " + table.Name + "Row row)");
            sb.AppendLine("        {");
            foreach (var f in table.Fields)
                sb.AppendLine($"            {WriteExpr(f)}");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ---- 表类 ----
            sb.AppendLine($"    /// <summary>{table.Name} 表。主键：{table.KeyField.Name}（{table.KeyField.CsType}）</summary>");
            sb.AppendLine($"    public sealed class {table.Name}Table : ConfigTableBase<{table.Name}Row, {table.KeyField.CsType}>");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const string NAME = \"{table.Name}\";");
            sb.AppendLine();
            sb.AppendLine($"        public override string TableName => NAME;");
            sb.AppendLine();
            sb.AppendLine("        // 字段签名哈希：Excel 表头字段（名称/类型/顺序）变化时会随之改变，");
            sb.AppendLine("        // 加载时与文件头比对，不匹配立即报错。");
            sb.AppendLine($"        public override int ExpectedSchemaHash => unchecked((int)0x{table.SchemaHash:X8}u);");
            sb.AppendLine();
            sb.AppendLine($"        protected override {table.Name}Row ReadRow(WXBinaryReader r) => {table.Name}Row.Read(r);");
            sb.AppendLine();
            sb.AppendLine($"        protected override {table.KeyField.CsType} GetKey({table.Name}Row row) => row.{table.KeyField.Name};");
            sb.AppendLine("    }");

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ReadExpr(FieldDef f)
        {
            switch (f.Type)
            {
                case FieldType.Int: return "r.ReadInt32()";
                case FieldType.Long: return "r.ReadInt64()";
                case FieldType.Float: return "r.ReadSingle()";
                case FieldType.Bool: return "r.ReadBool()";
                case FieldType.String: return "r.ReadString()";
                case FieldType.IntArray: return "r.ReadIntList()";
                case FieldType.FloatArray: return "r.ReadFloatList()";
                case FieldType.StringArray: return "r.ReadStringList()";
                default: return "r.ReadString()";
            }
        }

        private static string WriteExpr(FieldDef f)
        {
            switch (f.Type)
            {
                case FieldType.Int: return $"w.Write(row.{f.Name});";
                case FieldType.Long: return $"w.Write(row.{f.Name});";
                case FieldType.Float: return $"w.Write(row.{f.Name});";
                case FieldType.Bool: return $"w.Write(row.{f.Name});";
                case FieldType.String: return $"w.Write(row.{f.Name});";
                case FieldType.IntArray:
                case FieldType.FloatArray:
                case FieldType.StringArray:
                    // 数组直接按 List 写入（WXBinaryWriter 有对应重载）
                    return $"w.Write(row.{f.Name});";
                default: return $"w.Write(row.{f.Name});";
            }
        }

        // ------------------------------------------------------------------
        // 生成二进制
        // ------------------------------------------------------------------

        private static byte[] GenerateBinary(TableDef table)
        {
            using (var ms = new MemoryStream(4096))
            {
                var writer = new WXBinaryWriter(ms);

                // ---- 头 ----
                writer.Write(WXConfigFormat.Magic);
                writer.Write(WXConfigFormat.FormatVersion);
                writer.Write(table.SchemaHash);           // 字段签名哈希
                writer.Write(table.Rows.Count);           // 行数
                writer.Write(table.KeyField.Name);        // 主键字段名

                // ---- 数据 ----
                foreach (var row in table.Rows)
                {
                    foreach (var f in table.Fields)
                    {
                        string v = row[f.Column];
                        WriteValue(writer, f, v);
                    }
                }

                writer.Flush();
                return ms.ToArray();
            }
        }

        private static void WriteValue(WXBinaryWriter w, FieldDef f, string text)
        {
            switch (f.Type)
            {
                case FieldType.Int:
                    w.Write(string.IsNullOrEmpty(text) ? 0 : int.Parse(text));
                    break;
                case FieldType.Long:
                    w.Write(string.IsNullOrEmpty(text) ? 0L : long.Parse(text));
                    break;
                case FieldType.Float:
                    w.Write(string.IsNullOrEmpty(text) ? 0f : float.Parse(text));
                    break;
                case FieldType.Bool:
                    w.Write(!string.IsNullOrEmpty(text) && ParseBool(text));
                    break;
                case FieldType.String:
                    w.Write(text ?? string.Empty);
                    break;
                case FieldType.IntArray:
                case FieldType.FloatArray:
                case FieldType.StringArray:
                    WriteArray(w, f, text);
                    break;
            }
        }

        private static bool ParseBool(string v)
        {
            v = v.ToLowerInvariant();
            return v == "1" || v == "true" || v == "是";
        }

        private static void WriteArray(WXBinaryWriter w, FieldDef f, string text)
        {
            var parts = string.IsNullOrEmpty(text)
                ? new string[0]
                : text.Split(';');

            // 过滤空项，避免 "1;;2" 产生 0 值
            var cleaned = new List<string>();
            foreach (var p in parts)
            {
                var s = p.Trim();
                if (s.Length > 0) cleaned.Add(s);
            }

            w.Write(cleaned.Count);
            foreach (var s in cleaned)
            {
                switch (f.Type)
                {
                    case FieldType.IntArray: w.Write(int.Parse(s)); break;
                    case FieldType.FloatArray: w.Write(float.Parse(s)); break;
                    case FieldType.StringArray: w.Write(s); break;
                }
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        private static string GetCellText(IRow row, int col)
        {
            if (row == null) return string.Empty;
            ICell cell = row.GetCell(col);
            if (cell == null) return string.Empty;

            switch (cell.CellType)
            {
                case CellType.String: return cell.StringCellValue ?? "";
                case CellType.Numeric:
                    double d = cell.NumericCellValue;
                    // 整数值不显示小数点，避免 "1" 变成 "1.0" 导致 int 解析失败
                    if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15)
                        return ((long)Math.Round(d)).ToString();
                    return d.ToString("R");
                case CellType.Boolean: return cell.BooleanCellValue ? "true" : "false";
                case CellType.Formula:
                    try { return cell.StringCellValue ?? ""; }
                    catch { return cell.NumericCellValue.ToString("R"); }
                default: return string.Empty;
            }
        }

        private static string ColName(int index)
        {
            var sb = new StringBuilder();
            int i = index;
            while (i >= 0)
            {
                sb.Insert(0, (char)('A' + i % 26));
                i = i / 26 - 1;
            }
            return sb.ToString();
        }

        private static string GetProjectRoot()
            => Directory.GetParent(Application.dataPath).FullName;

        private static void EnsureDir(string relative)
        {
            string abs = Path.Combine(GetProjectRoot(), relative);
            if (!Directory.Exists(abs)) Directory.CreateDirectory(abs);
        }
    }
}

#endif