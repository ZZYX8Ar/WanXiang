// ============================================================================
//  万相 · 道痕分享码（B2-c / 纯编解码）
//  ---------------------------------------------------------------------------
//  "把一条道痕发给好友"（设计 v2 §7.3）：`WX1-<urlsafeBase64( id:star,... )>`
//
//  · 只做编解码 + 合法性校验，**不碰存档、不碰 UI**（导入后怎么存由调用方决定）
//  · 解码侧全查：前缀 / base64 / 段格式 / 星级 1..3 / id 必须在道痕表里 / 数量上限
//  · 未知 id 直接判 false（宁可拒收，也不让脏数据进存档）
//  ⛔ 不做签名（单机分享，篡改只影响自己），与 MetaSaveCode 的取舍一致。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace WanXiang.Battle.Core
{
    public static class DaoMarkCode
    {
        public const string Prefix = "WX1-";
        public const int MaxMarks = 3;      // 一条分享码最多带 3 条道痕

        /// <summary>编码。空列表 ⇒ 返回空串（调用方不应展示分享按钮）。</summary>
        public static string Encode(IReadOnlyList<KeyValuePair<string, int>> marks)
        {
            if (marks == null || marks.Count == 0) return "";
            var sb = new StringBuilder(64);
            int n = 0;
            for (int i = 0; i < marks.Count && n < MaxMarks; i++)
            {
                string id = marks[i].Key;
                int star = marks[i].Value;
                if (string.IsNullOrEmpty(id) || star < 1 || star > 3) continue;
                if (n > 0) sb.Append(',');
                sb.Append(id).Append(':').Append(star);
                n++;
            }
            if (n == 0) return "";
            return Prefix + ToUrlSafeBase64(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        /// <summary>
        /// 解码 + 全量校验。失败返回 false，不抛异常。
        /// <paramref name="requireKnown"/> = true 时要求 id 在道痕表里（线上用）；
        /// 纯格式测试可传 false（表未装也能验证编解码本身）。
        /// </summary>
        public static bool TryDecode(string code, out List<KeyValuePair<string, int>> marks,
                                     bool requireKnown = true)
        {
            marks = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(code) || !code.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            string payload = code.Substring(Prefix.Length);
            byte[] raw;
            if (!TryFromUrlSafeBase64(payload, out raw)) return false;

            string text;
            try { text = Encoding.UTF8.GetString(raw); }
            catch { return false; }
            if (string.IsNullOrEmpty(text)) return false;

            var parts = text.Split(',');
            if (parts.Length > MaxMarks) return false;

            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                int colon = p.LastIndexOf(':');
                if (colon <= 0 || colon >= p.Length - 1) return false;
                string id = p.Substring(0, colon);
                int star;
                if (!int.TryParse(p.Substring(colon + 1), out star)) return false;
                if (star < 1 || star > 3) return false;
                if (requireKnown && DaoMarkRules.For(id) == null) return false;   // 未知 id 直接拒收
                marks.Add(new KeyValuePair<string, int>(id, star));
            }
            return marks.Count > 0;
        }

        // ---- URL 安全 Base64（+ → -，/ → _，去掉 =）----
        private static string ToUrlSafeBase64(byte[] data)
            => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static bool TryFromUrlSafeBase64(string s, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: return false;                 // 非法长度
            }
            try { data = Convert.FromBase64String(s); return data != null && data.Length > 0; }
            catch { return false; }
        }
    }
}
