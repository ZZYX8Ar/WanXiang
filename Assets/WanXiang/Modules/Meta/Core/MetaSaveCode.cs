// ============================================================================
//  万相 · 局外存档码（GDD 1.3「分享 Base64 代码」；GDD 6.2 的分享码同一套路）
//  ---------------------------------------------------------------------------
//  与 ShareCode（对局阵容码）同风格：**紧凑二进制 → URL 安全 Base64**。
//  用途不同所以不共用编码器：对局码是"一局输入"，存档码是"长期进度"。
//
//  布局（v1）：
//    [0]     版本
//    [1..8]  局外种子（8 字节小端）
//    [9..10] 灵卵余额（2 字节，上限 65535）
//    [11..12]已孵次数（2 字节）
//    [13..14]局数（2 字节）
//    [15..16]通关数（2 字节）
//    [17]    最远幕（1..5）
//    [18]    宿主数量 n，随后 n 字节下标
//    [..]    灵魂数量 m，随后 m 字节下标
//
//  合法性校验（解码侧全查，任一不成立就 false，绝不带病读档）：
//    版本一致 / 长度吻合 / 下标 < 255 / 解锁列表无重复 / 最远幕在 1..5。
//  与 ShareCode 一致：**不做签名**（单机进度，篡改只影响自己），
//  只保证"解出来的东西自洽"。
// ============================================================================

using System;
using System.Text;

namespace WanXiang.Meta
{
    public static class MetaSaveCode
    {
        public const byte CurrentVersion = 1;
        public const int MaxIndex = 254;        // 下标用 1 字节，255 留作哨兵
        public const int MaxEggs = 65535;

        public static string Encode(MetaState st)
        {
            if (st == null) return null;
            if (st.Eggs < 0 || st.Eggs > MaxEggs) return null;
            if (st.BestActReached < 0 || st.BestActReached > 5) return null;
            if (st.UnlockedHosts.Count > 255 || st.UnlockedSouls.Count > 255) return null;
            if (!IndicesValid(st.UnlockedHosts) || !IndicesValid(st.UnlockedSouls)) return null;

            // 19 个固定字节 + 宿主下标 + 1 个灵魂数量字节 + 灵魂下标
            int size = 20 + st.UnlockedHosts.Count + st.UnlockedSouls.Count;
            var b = new byte[size];
            b[0] = CurrentVersion;
            WriteU64(b, 1, st.Seed);
            WriteU16(b, 9, (ushort)st.Eggs);
            WriteU16(b, 11, (ushort)System.Math.Min(st.HatchCount, MaxEggs));
            WriteU16(b, 13, (ushort)System.Math.Min(st.RunsPlayed, MaxEggs));
            WriteU16(b, 15, (ushort)System.Math.Min(st.RunsCompleted, MaxEggs));
            b[17] = (byte)st.BestActReached;
            b[18] = (byte)st.UnlockedHosts.Count;
            int p = 19;
            for (int i = 0; i < st.UnlockedHosts.Count; i++) b[p++] = (byte)st.UnlockedHosts[i];
            b[p++] = (byte)st.UnlockedSouls.Count;
            for (int i = 0; i < st.UnlockedSouls.Count; i++) b[p++] = (byte)st.UnlockedSouls[i];

            return ToBase64Url(b);
        }

        public static bool TryDecode(string code, out MetaState state)
        {
            state = null;
            if (string.IsNullOrEmpty(code)) return false;

            byte[] b;
            try { b = FromBase64Url(code); }
            catch (FormatException) { return false; }
            if (b == null || b.Length < 20) return false;
            if (b[0] != CurrentVersion) return false;

            var st = new MetaState(ReadU64(b, 1));
            st.Eggs = ReadU16(b, 9);
            st.HatchCount = ReadU16(b, 11);
            st.RunsPlayed = ReadU16(b, 13);
            st.RunsCompleted = ReadU16(b, 15);
            st.BestActReached = b[17];
            if (st.BestActReached > 5) return false;

            int p = 18;
            int hostCount = b[p++];
            if (p + hostCount > b.Length) return false;
            for (int i = 0; i < hostCount; i++) st.UnlockedHosts.Add(b[p++]);
            if (p >= b.Length) return false;
            int soulCount = b[p++];
            if (p + soulCount > b.Length) return false;
            for (int i = 0; i < soulCount; i++) st.UnlockedSouls.Add(b[p++]);

            // 解码侧再把"自洽性"过一遍：长度必须正好用完（多出来的字节 = 被塞过东西）
            if (p != b.Length) return false;
            if (!IndicesValid(st.UnlockedHosts) || !IndicesValid(st.UnlockedSouls)) return false;
            if (st.RunsCompleted > st.RunsPlayed) return false;   // 通关数不可能多于局数

            state = st;
            return true;
        }

        // ---- 小工具 ----

        private static bool IndicesValid(System.Collections.Generic.List<int> list)
        {
            var seen = new bool[256];
            for (int i = 0; i < list.Count; i++)
            {
                int v = list[i];
                if (v < 0 || v > MaxIndex) return false;
                if (seen[v]) return false;      // 重复解锁是脏档
                seen[v] = true;
            }
            return true;
        }

        private static void WriteU16(byte[] b, int at, ushort v)
        {
            b[at] = (byte)(v & 0xFF);
            b[at + 1] = (byte)(v >> 8);
        }

        private static void WriteU64(byte[] b, int at, ulong v)
        {
            for (int i = 0; i < 8; i++) b[at + i] = (byte)((v >> (8 * i)) & 0xFF);
        }

        private static ushort ReadU16(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));

        private static ulong ReadU64(byte[] b, int at)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)b[at + i] << (8 * i);
            return v;
        }

        /// <summary>URL/聊天安全 Base64（+→- / →_，去掉 = 填充）—— 与 ShareCode 同一处理。</summary>
        private static string ToBase64Url(byte[] data)
            => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static byte[] FromBase64Url(string s)
        {
            string t = s.Replace('-', '+').Replace('_', '/');
            int pad = t.Length % 4;
            if (pad == 1) return null;
            if (pad > 0) t = t.PadRight(t.Length + (4 - pad), '=');
            return Convert.FromBase64String(t);
        }
    }
}
