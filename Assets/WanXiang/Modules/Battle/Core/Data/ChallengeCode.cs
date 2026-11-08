// ============================================================================
//  万相 · 挑战分享码（B5，设计 v2 §7.3：WX1C-）
//  ---------------------------------------------------------------------------
//  "把我的开局挑战发给好友"：`WX1C-<urlsafeBase64( squadId:level )>`
//  好友导入后，选分队面板自动预填分队与难度（Btn_Start 仍由好友自己按）。
//  与道痕分享码（WX1-，DaoMarkCode）同构：前缀区分、全量校验、失败返回 false 不抛异常。
// ============================================================================
using System;
using System.Text;

namespace WanXiang.Battle.Core
{
    public static class ChallengeCode
    {
        public const string Prefix = "WX1C-";
        public const int MaxLevel = 18;

        /// <summary>编码分队 + 难度。level 夹取 1~18；squadId 必须非空。</summary>
        public static string Encode(string squadId, int level)
        {
            if (string.IsNullOrEmpty(squadId)) return null;
            int lv = level < 1 ? 1 : (level > MaxLevel ? MaxLevel : level);
            string payload = squadId + ":" + lv;
            return Prefix + Convert.ToBase64String(
                Encoding.UTF8.GetBytes(payload)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>解码。前缀/内容/难度范围任一不成立 → false（不抛异常）。</summary>
        public static bool TryDecode(string code, out string squadId, out int level)
        {
            squadId = null; level = 0;
            if (string.IsNullOrEmpty(code) || !code.StartsWith(Prefix)) return false;
            var b64 = code.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4) { case 2: b64 += "=="; break; case 3: b64 += "="; break; }
            byte[] raw;
            try { raw = Convert.FromBase64String(b64); }
            catch (FormatException) { return false; }
            string payload = Encoding.UTF8.GetString(raw);
            int colon = payload.LastIndexOf(':');
            if (colon <= 0 || colon == payload.Length - 1) return false;
            string sid = payload.Substring(0, colon);
            if (!int.TryParse(payload.Substring(colon + 1), out int lv)) return false;
            if (lv < 1 || lv > MaxLevel) return false;
            if (string.IsNullOrEmpty(sid)) return false;
            squadId = sid; level = lv;
            return true;
        }
    }
}
