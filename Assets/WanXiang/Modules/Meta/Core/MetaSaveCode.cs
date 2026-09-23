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
//  v3 追加：头 21 字节后的"培养段"（n + n×(idLen+id+lv+ev)）——
//    异兽局外等级与觉醒标记，跨局永久。
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
        public const byte CurrentVersion = 4;   // v4：新增精魄（进化材料）
        public const int MaxIndex = 254;        // 下标用 1 字节，255 留作哨兵
        public const int MaxEggs = 65535;

        public static string Encode(MetaState st)
        {
            if (st == null) return null;
            if (st.Eggs < 0 || st.Eggs > MaxEggs) return null;
            if (st.BestActReached < 0 || st.BestActReached > 5) return null;
            if (st.UnlockedHosts.Count > 255 || st.UnlockedSouls.Count > 255) return null;
            if (!IndicesValid(st.UnlockedHosts) || !IndicesValid(st.UnlockedSouls)) return null;

            // 头 21 字节 + 培养段 + 宿主下标 + 灵魂下标
            var idBytes = new System.Collections.Generic.List<byte[]>(st.BeastIds.Count);
            int beastBytes = 0;
            int n = System.Math.Min(st.BeastIds.Count,
                                    System.Math.Min(st.BeastLevels.Count, st.BeastEvolved.Count));
            for (int i = 0; i < n; i++)
            {
                var raw = Encoding.UTF8.GetBytes(st.BeastIds[i] ?? "");
                if (raw.Length > 250) return null;          // id 过长 = 脏档
                idBytes.Add(raw);
                beastBytes += 1 + raw.Length + 1 + 1;       // idLen + id + level + evolved
            }

            // ⚠ 必须算准：写入的是  BeastCount(1) + beastBytes + HostCount(1) + hosts + SoulCount(1) + souls
            //   ⇒ 固定头 21 + beastBytes + hosts + souls + 2（曾少算 2 字节 → IndexOutOfRange，存档写不进去）
            // ⚠ 固定字节要逐个数清：头 22 + BeastCount(1) + HostCount(1) + SoulCount(1) = 25
            int size = 25 + beastBytes + st.UnlockedHosts.Count + st.UnlockedSouls.Count;   // v4: 头22+3个计数
            var b = new byte[size];
            b[0] = CurrentVersion;
            WriteU64(b, 1, st.Seed);
            WriteU16(b, 9, (ushort)st.Eggs);
            WriteU16(b, 11, (ushort)System.Math.Min(st.HatchCount, MaxEggs));
            WriteU16(b, 13, (ushort)System.Math.Min(st.RunsPlayed, MaxEggs));
            WriteU16(b, 15, (ushort)System.Math.Min(st.RunsCompleted, MaxEggs));
            b[17] = (byte)st.BestActReached;
            WriteU16(b, 18, (ushort)System.Math.Min(System.Math.Max(0, st.Ink), MaxEggs));

            WriteU16(b, 20, (ushort)System.Math.Min(System.Math.Max(0, st.Essence), MaxEggs));   // ★ v4 精魄

            int p = 22;
            b[p++] = (byte)n;                               // ★ v3 培养数量
            for (int i = 0; i < n; i++)
            {
                var raw = idBytes[i];
                b[p++] = (byte)raw.Length;
                for (int k = 0; k < raw.Length; k++) b[p++] = raw[k];
                b[p++] = (byte)System.Math.Min(255, System.Math.Max(0, st.BeastLevels[i]));
                b[p++] = (byte)(st.BeastEvolved[i] ? 1 : 0);
            }

            b[p++] = (byte)st.UnlockedHosts.Count;
            for (int i = 0; i < st.UnlockedHosts.Count; i++) b[p++] = (byte)st.UnlockedHosts[i];
            b[p++] = (byte)st.UnlockedSouls.Count;
            for (int i = 0; i < st.UnlockedSouls.Count; i++) b[p++] = (byte)st.UnlockedSouls[i];

            // ★ 自检：写满即正确（size 靠手数很容易差 1~2 字节，这里兜底并把错误暴露出来）
            if (p != size) return null;

            return ToBase64Url(b);
        }

        public static bool TryDecode(string code, out MetaState state)
        {
            state = null;
            if (string.IsNullOrEmpty(code)) return false;

            byte[] b;
            try { b = FromBase64Url(code); }
            catch (FormatException) { return false; }
            if (b == null || b.Length < 22) return false;   // v4 头部 22 字节
            if (b[0] != CurrentVersion) return false;

            var st = new MetaState(ReadU64(b, 1));
            st.Eggs = ReadU16(b, 9);
            st.HatchCount = ReadU16(b, 11);
            st.RunsPlayed = ReadU16(b, 13);
            st.RunsCompleted = ReadU16(b, 15);
            st.BestActReached = b[17];
            if (st.BestActReached > 5) return false;
            st.Ink = ReadU16(b, 18);            // ★ v2 墨铊

            st.Essence = ReadU16(b, 20);                    // ★ v4 精魄

            int p = 22;
            int beastCount = b[p++];                        // ★ v3 培养段
            if (p > b.Length) return false;
            for (int i = 0; i < beastCount; i++)
            {
                if (p >= b.Length) return false;
                int idLen = b[p++];
                if (idLen == 0 || p + idLen > b.Length) return false;
                string id = Encoding.UTF8.GetString(b, p, idLen);
                p += idLen;
                if (p + 1 >= b.Length) return false;        // 还需 level + evolved
                int lv = b[p++];
                bool ev = b[p++] != 0;
                st.BeastIds.Add(id);
                st.BeastLevels.Add(lv);
                st.BeastEvolved.Add(ev);
            }

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
