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

        /// <summary>
        /// 每只的**养成**（v2 起）：低 4 位 = 等级(1..15)、第 4 位 = 是否进化。
        /// ★ 用户 2026-10-06："好友对战要体现把异兽养成多厉害" —— 不带这个，
        ///   双方就都是**默认面板**，那个玩法就没有意义了（原先确实没带，是缺口不是取舍）。
        /// 为空 = 旧码 ⇒ 一律按默认（等级 1 / 未进化）。
        /// </summary>
        public byte[] Growth;

        /// <summary>
        /// **遗物的整体倍率**（v3 起）：把这一局攒的遗物**折算成一个标量**带过来
        /// （用户 2026-10-06 定案）。⛔ 只折**数值部分**（全队倍率 × 本队每只的
        /// 五行/定位/异兽加成，取均值），**不重放**遗物的机制类效果（复活 / 开局灵力 /
        /// 改技能 / 授予劫象）—— 那要先接整套 mods 管线，收益/风险不划算。
        /// 1f = 无遗物。
        /// </summary>
        public float RelicMul;

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
        public const byte CurrentVersion = 3;   // v3：加入"遗物整体倍率"1 字节（v1/v2 码仍可解）
        public const int MaxUnits = 5;          // 3×3 棋盘单侧上限（GDD：5 只上阵）
        public const int MaxBoardSlot = 8;

        /// <summary>默认养成字节：等级 1、未进化。</summary>
        public const byte DefaultGrowth = 1;

        /// <summary>遗物倍率量化：步长 0.05、范围 1.00~13.75（1 字节）。0 字节 = 1.0。</summary>
        public static byte PackRelicMul(float mul)
        {
            if (mul < 1f) mul = 1f;
            int q = (int)System.Math.Round((mul - 1f) * 20f);
            if (q < 0) q = 0;
            if (q > 255) q = 255;
            return (byte)q;
        }

        /// <summary>解出遗物整体倍率（缺字节 ⇒ 1.0）。</summary>
        public static float UnpackRelicMul(byte b) { return 1f + b / 20f; }

        /// <summary>打包"等级 + 进化"到一个字节：低 4 位等级(夹 1..15)、第 4 位进化。</summary>
        public static byte PackGrowth(int level, bool evolved)
        {
            int lv = System.Math.Max(1, System.Math.Min(15, level));
            return (byte)(lv | (evolved ? 0x10 : 0));
        }

        /// <summary>拆出等级与进化。空/越界一律回落到默认（等级 1 / 未进化）。</summary>
        public static void UnpackGrowth(byte g, out int level, out bool evolved)
        {
            int lv = g & 0x0F;
            level = lv < 1 ? 1 : lv;
            evolved = (g & 0x10) != 0;
        }

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

            // v3：2 头部 + 3×n + **n 养成** + **1 遗物倍率** + 8 种子。n=5 时 31 字节 → Base64 约 44 字符。
            var bytes = new byte[2 + n * 3 + n + 1 + 8];
            bytes[0] = CurrentVersion;   // ⛔ 版本由编码器决定，别信调用方传的（否则产出自己解不开的码）
            bytes[1] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                int o = 2 + i * 3;
                bytes[o] = (byte)p.BeastIndices[i];
                bytes[o + 1] = (byte)p.SoulIndices[i];
                bytes[o + 2] = (byte)p.BoardSlots[i];
                bytes[2 + n * 3 + i] = (p.Growth != null && i < p.Growth.Length)
                    ? p.Growth[i] : DefaultGrowth;
            }
            bytes[2 + n * 3 + n] = PackRelicMul(p.RelicMul <= 0f ? 1f : p.RelicMul);
            WriteU64(bytes, 2 + n * 3 + n + 1, p.Seed);

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
            // ⚠ v1（裸阵容）/ v2（带养成）/ v3（带养成 + 遗物倍率）都要能解 —— 老分享码不能失效。
            if (version < 1 || version > CurrentVersion) return false;

            int n = bytes[1];
            if (n < 1 || n > MaxUnits) return false;
            int expect = 2 + n * 3 + 8;                       // v1
            if (version >= 2) expect += n;                    // + 养成
            if (version >= 3) expect += 1;                    // + 遗物倍率
            if (bytes.Length != expect) return false;

            var beasts = new int[n];
            var souls = new int[n];
            var slots = new int[n];
            var growth = new byte[n];
            var seen = new bool[MaxBoardSlot + 1];

            for (int i = 0; i < n; i++)
            {
                int o = 2 + i * 3;
                beasts[i] = bytes[o];
                souls[i] = bytes[o + 1];
                slots[i] = bytes[o + 2];
                growth[i] = (version >= 2) ? bytes[2 + n * 3 + i] : DefaultGrowth;

                if (beasts[i] >= beastCount) return false;   // byte 非负，只查上界
                if (souls[i] >= soulCount) return false;
                if (slots[i] > MaxBoardSlot) return false;
                if (seen[slots[i]]) return false;            // 一格一兽
                seen[slots[i]] = true;
            }

            int seedAt = 2 + n * 3 + (version >= 2 ? n : 0) + (version >= 3 ? 1 : 0);
            p = new SharePayload
            {
                Version = version,
                BeastIndices = beasts,
                SoulIndices = souls,
                BoardSlots = slots,
                Growth = growth,
                RelicMul = (version >= 3) ? UnpackRelicMul(bytes[2 + n * 3 + n]) : 1f,
                Seed = ReadU64(bytes, seedAt),
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
