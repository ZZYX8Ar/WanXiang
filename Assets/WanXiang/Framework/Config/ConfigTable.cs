// ============================================================================
//  WanXiang · 配置表运行时框架
//  ---------------------------------------------------------------------------
//  替代参考代码中的 BinaryDataMgr，主要改动：
//
//    1. 文件带 Magic + 格式版本 + SchemaHash → 结构变更立刻报错，不再静默读错
//    2. 不依赖反射按字段顺序读取     → 由生成代码显式读写，顺序确定
//    3. 不走 Application.streamingAssetsPath（Android 上 File 读会失败）
//                                   → 由外部（Addressables）提供 byte[]，
//                                     本层只负责解析，与加载方式解耦
//    4. 单例 → 可注入的实例，便于接入 QFramework 的 IOC 与单元测试
//
//  二进制文件布局：
//    ┌──────────────────────────────────────────┐
//    │ Magic        "WXCF"           4 bytes    │
//    │ FormatVer    int              4 bytes    │  配置表格式版本
//    │ SchemaHash   int              4 bytes    │  字段签名哈希
//    │ RowCount     int              4 bytes    │
//    │ KeyField     string           4 + n      │  主键字段名
//    ├──────────────────────────────────────────┤
//    │ Rows         [RowCount × 行数据]          │
//    └──────────────────────────────────────────┘
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using WanXiang.Core.Serialization;

namespace WanXiang.Framework.Config
{
    /// <summary>
    /// 配置表二进制格式常量。导出工具（编辑器）与运行时共用同一份定义，
    /// 避免两端各自写死数值而后失配。
    /// </summary>
    public static class WXConfigFormat
    {
        /// <summary>文件魔数 "WXCF"（按小端存放）</summary>
        public const uint Magic = 0x46435857;

        /// <summary>二进制格式版本。仅当"存放方式"本身变化时递增；
        /// 表结构变化由 SchemaHash 负责捕捉，两者不要混用。</summary>
        public const int FormatVersion = 1;
    }

    /// <summary>配置表统一接口。所有由 Excel 生成的表都实现它。</summary>
    public interface IConfigTable
    {
        /// <summary>表名，与 Excel 文件名一致。</summary>
        string TableName { get; }

        /// <summary>由文件头读出的格式版本。</summary>
        int FormatVersion { get; }

        /// <summary>已加载的行数。</summary>
        int RowCount { get; }

        /// <summary>代码侧期望的字段签名哈希（由生成代码写入）。</summary>
        int ExpectedSchemaHash { get; }

        /// <summary>从字节流解析。data 由外部加载器提供（不关心来源）。</summary>
        void Load(byte[] data);

        /// <summary>清空数据，释放内存。</summary>
        void Unload();
    }

    /// <summary>
    /// 配置表基类。生成的表类继承它，只需实现 <see cref="ReadRow"/> 与
    /// <see cref="GetKey"/>，其余（头校验、索引构建、异常处理）由基类负责。
    /// </summary>
    public abstract class ConfigTableBase<TRow, TKey> : IConfigTable
    {
        /// <summary>文件魔数，定义见 <see cref="WXConfigFormat.Magic"/>。</summary>
        public const uint Magic = WXConfigFormat.Magic;

        protected readonly List<TRow> RowList = new List<TRow>(256);
        protected readonly Dictionary<TKey, TRow> RowMap = new Dictionary<TKey, TRow>(256);

        /// <summary>按行号顺序的全部数据。</summary>
        public IReadOnlyList<TRow> Rows => RowList;

        /// <summary>主键索引，O(1) 查表。</summary>
        public IReadOnlyDictionary<TKey, TRow> Map => RowMap;

        public int RowCount => RowList.Count;
        public int FormatVersion { get; private set; }

        public abstract string TableName { get; }
        public abstract int ExpectedSchemaHash { get; }

        /// <summary>读取一行。由生成代码实现，字段顺序必须与写入端一致。</summary>
        protected abstract TRow ReadRow(WXBinaryReader reader);

        /// <summary>取出主键。</summary>
        protected abstract TKey GetKey(TRow row);

        /// <summary>加载完成后的额外处理（建立二级索引、校验引用等）。</summary>
        protected virtual void OnAfterLoad() { }

        public void Load(byte[] data)
        {
            if (data == null || data.Length < 16)
            {
                throw new WXBinaryFormatException($"[{TableName}] 配置表数据为空或过短，无法解析。");
            }

            Unload();

            try
            {
                var reader = new WXBinaryReader(data);

                // ---- 1. 魔数校验：防误读其它类型文件 ----
                uint magic = reader.ReadUInt32();
                if (magic != Magic)
                {
                    throw new WXBinaryFormatException(
                        $"[{TableName}] 文件标识错误：期望 0x{Magic:X8}，实际 0x{magic:X8}。" +
                        $"可能读取了错误的文件，或文件已损坏。");
                }

                // ---- 2. 格式版本 ----
                FormatVersion = reader.ReadInt32();

                // ---- 3. 表结构校验（核心改进）----
                int fileSchemaHash = reader.ReadInt32();
                if (fileSchemaHash != ExpectedSchemaHash)
                {
                    throw new WXBinaryFormatException(
                        $"[{TableName}] 表结构不匹配！\n" +
                        $"  文件中的 SchemaHash : {fileSchemaHash}\n" +
                        $"  代码期望的 SchemaHash: {ExpectedSchemaHash}\n" +
                        $"说明：Excel 表的字段（名称/类型/顺序）已被修改，" +
                        $"但运行时读取代码未同步重新生成。\n" +
                        $"处理：请重新执行配置表导出工具。");
                }

                // ---- 4. 行数 ----
                int rowCount = reader.ReadInt32();
                if (rowCount < 0 || rowCount > 1_000_000)
                {
                    throw new WXBinaryFormatException(
                        $"[{TableName}] 行数异常：{rowCount}。文件可能已损坏。");
                }

                // ---- 5. 主键字段名（仅用于日志与调试）----
                string keyField = reader.ReadString();

                // ---- 6. 逐行读取 ----
                RowList.Capacity = Math.Max(RowList.Capacity, rowCount);
                RowMap.Clear();

                for (int i = 0; i < rowCount; i++)
                {
                    TRow row;
                    try
                    {
                        row = ReadRow(reader);
                    }
                    catch (Exception ex) when (!(ex is WXBinaryFormatException))
                    {
                        throw new WXBinaryFormatException(
                            $"[{TableName}] 第 {i} 行（0-based）读取失败，位置 {reader.Position}。", ex);
                    }

                    RowList.Add(row);

                    TKey key = GetKey(row);
                    // 用 `is null` 而非 `== null`：对值类型主键（int 等）不产生装箱
                    if (key is null)
                    {
                        throw new WXBinaryFormatException(
                            $"[{TableName}] 第 {i} 行的主键字段「{keyField}」为空。");
                    }
                    if (RowMap.ContainsKey(key))
                    {
                        throw new WXBinaryFormatException(
                            $"[{TableName}] 主键重复：{key}。Excel 表中同一主键只能出现一次。");
                    }
                    RowMap.Add(key, row);
                }

                OnAfterLoad();
            }
            catch (WXBinaryFormatException)
            {
                Unload();
                throw;      // 格式异常直接向上抛，不做任何"容错性兜底"
            }
            catch (Exception ex)
            {
                Unload();
                throw new WXBinaryFormatException($"[{TableName}] 加载失败。", ex);
            }
        }

        public void Unload()
        {
            RowList.Clear();
            RowMap.Clear();
        }

        // ------------------------------------------------------------------
        // 查询辅助
        // ------------------------------------------------------------------

        /// <summary>按主键查询。键不存在会抛异常 —— 配置错误应在开发期暴露。</summary>
        public TRow Get(TKey key)
        {
            if (RowMap.TryGetValue(key, out var row)) return row;
            throw new KeyNotFoundException(
                $"[{TableName}] 未找到主键 {key}。请检查 Excel 表是否缺少该行，" +
                $"或代码中引用的 ID 是否已废弃。");
        }

        /// <summary>安全查询。</summary>
        public bool TryGet(TKey key, out TRow row) => RowMap.TryGetValue(key, out row);

        public bool Contains(TKey key) => RowMap.ContainsKey(key);
    }
}
