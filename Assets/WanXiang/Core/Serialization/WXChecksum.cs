// ============================================================================
//  WanXiang · 校验和（CRC32）
//  ---------------------------------------------------------------------------
//  用途：
//    1. 存档完整性校验 —— 手机上玩家可能随时杀进程，写入中途被打断会留下
//       半截文件。有 CRC32 就能识别出来并回退到备份，而不是把损坏数据
//       当成合法存档读进来（那会静默破坏玩家数据）。
//    2. 配置表完整性校验 —— 下载中断导致的半截文件同样需要识别。
//
//  标准 CRC-32 (IEEE 802.3)，多项式 0xEDB88320（反射形式）。
//  与 zip / png 使用的实现一致，便于用外部工具交叉验证。
// ============================================================================

using System;

namespace WanXiang.Core.Serialization
{
    public static class WXChecksum
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        public static uint Compute(byte[] data)
        {
            if (data == null) return 0;
            return Compute(data, 0, data.Length);
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            unchecked
            {
                uint crc = 0xFFFFFFFFu;
                int end = offset + count;
                for (int i = offset; i < end; i++)
                {
                    crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
                }
                return crc ^ 0xFFFFFFFFu;
            }
        }

        /// <summary>增量计算（用于流式写入时边写边算）。</summary>
        public static uint Begin() => 0xFFFFFFFFu;

        public static uint Update(uint crc, byte[] data, int offset, int count)
        {
            unchecked
            {
                int end = offset + count;
                for (int i = offset; i < end; i++)
                {
                    crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
                }
                return crc;
            }
        }

        public static uint End(uint crc) => crc ^ 0xFFFFFFFFu;
    }
}
