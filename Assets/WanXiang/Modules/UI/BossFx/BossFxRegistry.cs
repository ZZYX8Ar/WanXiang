// 万相 · Boss 特效注册表（表现层，2026-11-15）
// 加特效 = Register 一行；删特效 = 删注册行，行为归零不报错。
using System;
using System.Collections.Generic;

namespace WanXiang.Modules.UI.BossFx
{
    /// <summary>机制演出上下文（只读，表现层不得写回战斗状态）。</summary>
    public readonly struct FxContext
    {
        public readonly string Kind;
        public readonly string Note;
        public readonly string BossId;
        public FxContext(string kind, string note, string bossId)
        { Kind = kind; Note = note; BossId = bossId; }
    }

    public interface IBossFx
    {
        void Play(FxContext ctx);
    }

    /// <summary>注册表：mechanicKind → 演出器。Core 只写 [KIND] 标记，这里派发。</summary>
    public static class BossFxRegistry
    {
        private static readonly Dictionary<string, IBossFx> Map = new Dictionary<string, IBossFx>(8);

        public static void Register(string kind, IBossFx fx)
        {
            if (string.IsNullOrEmpty(kind) || fx == null) return;
            Map[kind] = fx;
        }

        public static void Dispatch(FxContext ctx)
        {
            IBossFx fx;
            if (!Map.TryGetValue(ctx.Kind, out fx)) return;
            try { fx.Play(ctx); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[BossFx] 演出异常 kind=" + ctx.Kind + "：" + e.Message); }
        }

        public static int Count => Map.Count;

        /// <summary>从 BattleLog.Note 解析机制标记（"[DICE]…" / "[BOSSFX:xxx]…"）。</summary>
        public static bool TryParse(string note, out string kind, out string payload)
        {
            kind = payload = "";
            if (string.IsNullOrEmpty(note)) return false;
            if (note.StartsWith("[DICE]")) { kind = "Dice"; payload = note.Substring(6); return true; }
            if (note.StartsWith("[BOSSFX:"))
            {
                int end = note.IndexOf(']', 8);
                if (end <= 8) return false;
                kind = note.Substring(8, end - 8);
                payload = note.Substring(end + 1);
                return true;
            }
            return false;
        }
    }
}
