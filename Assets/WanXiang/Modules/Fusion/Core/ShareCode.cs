// ============================================================================
//  万相 · 分享码（STEP 3 第一片）
//  ---------------------------------------------------------------------------
//  GDD 6.2 的 SharePayload：阵容（异兽 + 灵魂 + 落位）+ 随机种子，
//  编成一段可贴进聊天框的字符串；对方导入后 **1:1 复现**你的队伍与对局结果。
//  复现的前提与战斗核心同源：融合零随机、战斗固定种子 ⇒ 同码同局。
//
//  编码管线的取舍（GDD 原文写 JsonUtility→Deflate→Base64）：
//  本实现改为**紧凑二进制 → Base64**。理由：25 字节的载荷塞进 Deflate
//  反而变大，JSON 字段名更是纯开销；90 字符预算轻松达成（5 单位约 36 字符）。
//  版本号字节保留 —— 将来字段变体走版本分支，老码不失效。
//
//  Base64 做了 URL/聊天安全替换（+→- / →_）：贴微信、评论区不会被转义吃掉。
//
//  ⚠ 防篡改口径（GDD 7 章风险表）：纯异步对战，篡改只影响自己，
//    所以只做**合法性校验**（版本 / 数量 / 索引越界 / 落位越界 / 落位重复），
//    不做签名。解码失败一律返回 false，绝不带病上阵。
// ============================================================================

namespace WanXiang.Fusion
{
    /// <summary>一个上阵位：内容表里的异兽下标 + 灵魂下标 + 棋盘格（0..8）。</summary>
    public struct ShareSlot
    {
        public int BeastIndex;
        public int SoulIndex;
        public int BoardSlot;
    }

    /// <summary>分享载荷。下标一律指**内容目录顺序**（导入器保持 GDD 表序，跨版本稳定）。</summary>
    public struct SharePayload
    {
        public byte Version;
        public int[] BeastIndices;   // 与 SoulIndices/BoardSlots 等长
        public int[] SoulIndices;
        public int[] BoardSlots;
        public ulong Seed;

        public int UnitCount => BeastIndices?.Length ?? 0;

        public ShareSlot GetSlot(int i) => new ShareSlot
        {
            BeastIndex = BeastIndices[i],
            SoulIndex = SoulIndices[i],
            BoardSlot = BoardSlots[i],
        };
    }

    public static class ShareCode
    {
        public const byte CurrentVersion = 1;
        public const int MaxUnits = 5;          // 3×3 棋盘单侧上限（GDD：5 只上阵）
        public const int MaxBoardSlot = 8;

        /// <summary>编码。载荷非法（数量 0 或超上限）返回 null —— 调用方负责给出可读错误。</summary>
        public static string Encode(SharePayload p)
        {
            if (p.BeastIndices == null || p.SoulIndices == null || p.BoardSlots == null)
                return null;
            int n = p.UnitCount;
            if (n < 1 || n > MaxUnits) return null;
            if (p.SoulIndices.Length != n || p.BoardSlots.Length != n) return null;

            // 编码器同样做合法性校验：**拒绝产出解码器会拒绝的载荷**（防呆而不是防人）。
            // byte 编码会静默截断，所以越界必须在编码期拦下。
            var seen = new bool[MaxBoardSlot + 1];
            for (int i = 0; i < n; i++)
            {
                if (p.BeastIndices[i] < 0 || p.BeastIndices[i] > byte.MaxValue) return null;
                if (p.SoulIndices[i] < 0 || p.SoulIndices[i] > byte.MaxValue) return null;
                if (p.BoardSlots[i] < 0 || p.BoardSlots[i] > MaxBoardSlot) return null;
                if (seen[p.BoardSlots[i]]) return null;   // 一格一兽
                seen[p.BoardSlots[i]] = true;
            }

            // 2 头部 + 3×n + 8 种子。n=5 时 25 字节 → Base64 约 36 字符。
            var bytes = new byte[2 + n * 3 + 8];
            bytes[0] = p.Version;
            bytes[1] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                int o = 2 + i * 3;
                bytes[o] = (byte)p.BeastIndices[i];
                bytes[o + 1] = (byte)p.SoulIndices[i];
                bytes[o + 2] = (byte)p.BoardSlots[i];
            }
            WriteU64(bytes, 2 + n * 3, p.Seed);

            return ToChatSafeBase64(bytes);
        }

        /// <summary>
        /// 解码并做合法性校验。任何不合法（版本不符 / 数量越界 / 索引越界 / 落位越界或重复 /
        /// 字符集损坏）都返回 false，输出默认值。
        /// </summary>
        /// <param name="beastCount">内容表异兽数量（越界校验用）。</param>
        /// <param name="soulCount">内容表灵魂数量。</param>
        public static bool TryDecode(string code, int beastCount, int soulCount, out SharePayload p)
        {
            p = default;
            if (string.IsNullOrEmpty(code)) return false;

            byte[] bytes = FromChatSafeBase64(code);
            if (bytes == null || bytes.Length < 10) return false;

            byte version = bytes[0];
            if (version != CurrentVersion) return false;

            int n = bytes[1];
            if (n < 1 || n > MaxUnits) return false;
            if (bytes.Length != 2 + n * 3 + 8) return false;

            var beasts = new int[n];
            var souls = new int[n];
            var slots = new int[n];
            var seen = new bool[MaxBoardSlot + 1];

            for (int i = 0; i < n; i++)
            {
                int o = 2 + i * 3;
                beasts[i] = bytes[o];
                souls[i] = bytes[o + 1];
                slots[i] = bytes[o + 2];

                if (beasts[i] >= beastCount) return false;   // byte 非负，只查上界
                if (souls[i] >= soulCount) return false;
                if (slots[i] > MaxBoardSlot) return false;
                if (seen[slots[i]]) return false;            // 一格一兽
                seen[slots[i]] = true;
            }

            p = new SharePayload
            {
                Version = version,
                BeastIndices = beasts,
                SoulIndices = souls,
                BoardSlots = slots,
                Seed = ReadU64(bytes, 2 + n * 3),
            };
            return true;
        }

        // ---- Base64（聊天安全变体：+→- / →_，保留 padding） ----

        internal static string ToChatSafeBase64(byte[] bytes)
        {
            var b64 = System.Convert.ToBase64String(bytes);
            return b64.Replace('+', '-').Replace('/', '_');
        }

        internal static byte[] FromChatSafeBase64(string s)
        {
            // 容忍用户手动改回标准 Base64：两种字符集都收。
            var std = s.Replace('-', '+').Replace('_', '/');
            try { return System.Convert.FromBase64String(std); }
            catch (System.FormatException) { return null; }
        }

        // ---- 定长小端编码（不依赖 BitConverter 的平台字节序） ----

        private static void WriteU64(byte[] b, int o, ulong v)
        {
            for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (i * 8));
        }

        private static ulong ReadU64(byte[] b, int o)
        {
            ulong v = 0;
            for (int i = 7; i >= 0; i--) v = (v << 8) | b[o + i];
            return v;
        }
    }
}
