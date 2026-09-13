// ============================================================================
//  WanXiang · 二进制序列化核心
//  ---------------------------------------------------------------------------
//  零依赖（仅 System），可直接拷入任何 Unity 工程使用。
//
//  设计目标：
//    1. 替代 BinaryFormatter —— Unity 2023.1+ 已移除该 API，且其存在
//       反序列化 RCE 风险，存档格式一旦上线无法更换。
//    2. 显式字段顺序 —— 不依赖反射的 GetFields() 顺序，字段顺序由生成代码
//       与 SchemaHash 双重保证，顺序改变会在加载时立刻报错，而非静默读错。
//    3. Little-Endian 定长编码 —— Unity 全部目标平台均为小端，省去判断。
//    4. 无 GC 压力读取 —— 读取路径尽量复用缓冲区。
//
//  文件布局约定（配置表 / 存档均在外层自行加头，本层只负责"值"的读写）：
//    每个字段按写入顺序连续存放，调用方必须保证读写顺序一致。
//    该一致性由 SchemaHash 在加载时校验（见 WXSchemaHash）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WanXiang.Core.Serialization
{
    /// <summary>
    /// 二进制写入器。所有写入为 Little-Endian 定长（string 为长度前缀 + UTF8）。
    /// </summary>
    public sealed class WXBinaryWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly byte[] _scratch = new byte[8];
        private bool _disposed;

        public WXBinaryWriter(Stream stream, int capacity = 4096)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (capacity > 0)
            {
                // MemoryStream 预留容量，减少扩容拷贝
                if (_stream is MemoryStream ms && ms.Capacity < capacity)
                {
                    ms.Capacity = capacity;
                }
            }
        }

        public long Position => _stream.Position;
        public Stream BaseStream => _stream;

        // ------------------------------------------------------------------
        // 基础类型
        // ------------------------------------------------------------------

        public void Write(bool value)
        {
            _scratch[0] = (byte)(value ? 1 : 0);
            _stream.Write(_scratch, 0, 1);
        }

        public void Write(byte value)
        {
            _scratch[0] = value;
            _stream.Write(_scratch, 0, 1);
        }

        public void Write(short value)
        {
            _scratch[0] = (byte)value;
            _scratch[1] = (byte)(value >> 8);
            _stream.Write(_scratch, 0, 2);
        }

        public void Write(int value)
        {
            _scratch[0] = (byte)value;
            _scratch[1] = (byte)(value >> 8);
            _scratch[2] = (byte)(value >> 16);
            _scratch[3] = (byte)(value >> 24);
            _stream.Write(_scratch, 0, 4);
        }

        public void Write(uint value) => Write(unchecked((int)value));

        public void Write(long value)
        {
            Write(unchecked((int)(value & 0xFFFFFFFF)));
            Write(unchecked((int)(value >> 32)));
        }

        public void Write(float value) => Write(BitConverter.SingleToInt32Bits(value));

        public void Write(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            Write(bits);
        }

        // ------------------------------------------------------------------
        // 字符串
        // ------------------------------------------------------------------

        /// <summary>写入字符串。null 与空串会被区分：null 写长度 -1。</summary>
        public void Write(string value)
        {
            if (value == null)
            {
                Write(-1);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            Write(byteCount);
            if (byteCount == 0) return;

            // 短字符串走栈缓冲区，避免每次分配数组。
            // 注：此处不用条件表达式混合 stackalloc 与 new[]，因为二者类型不同
            // （Span<byte> vs byte[]），在 Unity 的 C# 版本下推导易出问题。
            if (byteCount <= 256)
            {
                Span<byte> buffer = stackalloc byte[256];
                Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
                _stream.Write(buffer.Slice(0, byteCount));
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                _stream.Write(bytes, 0, bytes.Length);
            }
        }

        // ------------------------------------------------------------------
        // 容器
        // ------------------------------------------------------------------

        public void Write(byte[] value)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Length);
            if (value.Length > 0) _stream.Write(value, 0, value.Length);
        }

        public void Write(IList<int> value)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Count);
            for (int i = 0; i < value.Count; i++) Write(value[i]);
        }

        public void Write(IList<float> value)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Count);
            for (int i = 0; i < value.Count; i++) Write(value[i]);
        }

        public void Write(IList<string> value)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Count);
            for (int i = 0; i < value.Count; i++) Write(value[i]);
        }

        public void Write<T>(IList<T> value, Action<WXBinaryWriter, T> writer)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Count);
            for (int i = 0; i < value.Count; i++) writer(this, value[i]);
        }

        public void Write<K, V>(IDictionary<K, V> value,
                                Action<WXBinaryWriter, K> keyWriter,
                                Action<WXBinaryWriter, V> valueWriter)
        {
            if (value == null) { Write(-1); return; }
            Write(value.Count);
            foreach (var kv in value)
            {
                keyWriter(this, kv.Key);
                valueWriter(this, kv.Value);
            }
        }

        /// <summary>写入枚举。底层按 int 存储。</summary>
        public void WriteEnum<TEnum>(TEnum value) where TEnum : struct, Enum
            => Write(Convert.ToInt32(value));

        public void Flush() => _stream.Flush();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream?.Flush();
        }
    }

    /// <summary>
    /// 二进制读取器。越界一律抛 <see cref="WXBinaryFormatException"/>，
    /// 不返回默认值 —— 静默的错误数据比崩溃更难排查。
    /// </summary>
    public sealed class WXBinaryReader : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly int _length;
        private int _position;
        private bool _disposed;

        public WXBinaryReader(byte[] buffer, int offset = 0, int length = -1)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _position = offset;
            _length = length < 0 ? buffer.Length : Math.Min(buffer.Length, offset + length);
        }

        public int Position => _position;
        public int Length => _length;
        public int Remaining => _length - _position;

        private void Ensure(int bytes)
        {
            if (_position + bytes > _length)
            {
                throw new WXBinaryFormatException(
                    $"二进制数据不完整：需要 {bytes} 字节，" +
                    $"位置 {_position}，总长 {_length}。" +
                    $"通常是文件损坏、版本不匹配或读写顺序不一致。");
            }
        }

        public bool ReadBool()
        {
            Ensure(1);
            return _buffer[_position++] != 0;
        }

        public byte ReadByte()
        {
            Ensure(1);
            return _buffer[_position++];
        }

        public short ReadInt16()
        {
            Ensure(2);
            short v = (short)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return v;
        }

        public int ReadInt32()
        {
            Ensure(4);
            int v = _buffer[_position]
                  | (_buffer[_position + 1] << 8)
                  | (_buffer[_position + 2] << 16)
                  | (_buffer[_position + 3] << 24);
            _position += 4;
            return v;
        }

        public uint ReadUInt32() => unchecked((uint)ReadInt32());

        public long ReadInt64()
        {
            long low = (uint)ReadInt32();
            long high = ReadInt32();
            return low | (high << 32);
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        /// <summary>读取字符串。长度 -1 表示 null。</summary>
        public string ReadString()
        {
            int byteCount = ReadInt32();
            if (byteCount == -1) return null;
            if (byteCount == 0) return string.Empty;
            if (byteCount < 0)
            {
                throw new WXBinaryFormatException($"字符串长度非法：{byteCount}");
            }

            Ensure(byteCount);
            string s = Encoding.UTF8.GetString(_buffer, _position, byteCount);
            _position += byteCount;
            return s;
        }

        public byte[] ReadBytes()
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"字节数组长度非法：{count}");
            Ensure(count);
            var result = new byte[count];
            Buffer.BlockCopy(_buffer, _position, result, 0, count);
            _position += count;
            return result;
        }

        public List<int> ReadIntList()
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"列表长度非法：{count}");
            var list = new List<int>(count);
            for (int i = 0; i < count; i++) list.Add(ReadInt32());
            return list;
        }

        public List<float> ReadFloatList()
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"列表长度非法：{count}");
            var list = new List<float>(count);
            for (int i = 0; i < count; i++) list.Add(ReadSingle());
            return list;
        }

        public List<string> ReadStringList()
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"列表长度非法：{count}");
            var list = new List<string>(count);
            for (int i = 0; i < count; i++) list.Add(ReadString());
            return list;
        }

        public List<T> ReadList<T>(Func<WXBinaryReader, T> reader)
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"列表长度非法：{count}");
            var list = new List<T>(count);
            for (int i = 0; i < count; i++) list.Add(reader(this));
            return list;
        }

        public Dictionary<K, V> ReadDictionary<K, V>(
            Func<WXBinaryReader, K> keyReader,
            Func<WXBinaryReader, V> valueReader)
        {
            int count = ReadInt32();
            if (count == -1) return null;
            if (count < 0) throw new WXBinaryFormatException($"字典长度非法：{count}");
            var dict = new Dictionary<K, V>(count);
            for (int i = 0; i < count; i++)
            {
                var k = keyReader(this);
                var v = valueReader(this);
                dict[k] = v;
            }
            return dict;
        }

        public TEnum ReadEnum<TEnum>() where TEnum : struct, Enum
            => (TEnum)Enum.ToObject(typeof(TEnum), ReadInt32());

        public void Skip(int bytes) { Ensure(bytes); _position += bytes; }

        public void Dispose() { _disposed = true; }
    }

    /// <summary>二进制格式异常。用于区分"数据损坏/版本不匹配"与普通运行时异常。</summary>
    public class WXBinaryFormatException : Exception
    {
        public WXBinaryFormatException(string message) : base(message) { }
        public WXBinaryFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
