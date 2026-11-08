// ============================================================================
//  万相 · 舞台材质工厂（表现层重构第一刀）
//  ---------------------------------------------------------------------------
//  从 BattleStage2D.cs（4010 行）里抽出**材质创建**这一独立职责：
//    · 6 类立绘/特效材质集中在这里，参数不再散落在 4000 行脚本各处
//    · 注册表驱动：换 shader / 改参数 / 删效果 = 改一个注册项，调用方零改动
//    · 以后接「品级描边」「异色换色」只需 Register 一个新 Kind
//
//  ⚠ 行为等价于重构前的 `new Material(sh) + SetFloat/SetTexture`，
//    shader 取不到时返回 null ⇒ 调用方退回默认材质（静态图，不崩）。
//  ⛔ 本层只做"创建"，不做缓存 / 不持有单位状态（缓存仍在调用方）。
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanXiang.Battle.Presentation
{
    public enum StageMaterialKind
    {
        StatusOverlay = 0,   // 状态叠加（参数由调用方按状态 id 下发）
        Awaken = 1,          // 觉醒立绘：金描边 + 内发光
        Corrupt = 2,         // 魇化立绘：裂纹 + 脏污 + 紫染
        Aura = 3,            // 脚下附着光圈
        StatusAura = 4,      // 脚下状态光圈
        InkDissolve = 5,     // 墨蚀溶解
    }

    /// <summary>创建材质所需的外部资源（由调用方加载后传入）。</summary>
    public struct StageMaterialContext
    {
        public Shader Shader;        // 已加载的 shader（优先用；为空则工厂自己找）
        public Texture NoiseTexture; // FX_motion_fog
        public Texture MainTexture;  // 立绘本体（InkDissolve 的 _MainTex）
    }

    public static class StageMaterialFactory
    {
        public delegate Material Builder(StageMaterialContext ctx);

        private static readonly Dictionary<StageMaterialKind, Builder> _builders =
            new Dictionary<StageMaterialKind, Builder>();

        public static int Count => _builders.Count;

        /// <summary>注册/替换一种材质创建方式。传 null = 注销（该效果自动失效，不报错）。</summary>
        public static void Register(StageMaterialKind kind, Builder builder)
        {
            if (builder == null) _builders.Remove(kind);
            else _builders[kind] = builder;
        }

        /// <summary>创建材质。未注册 / shader 缺失 / 不支持 ⇒ null（调用方负责回退）。</summary>
        public static Material Create(StageMaterialKind kind, StageMaterialContext ctx = default(StageMaterialContext))
        {
            Builder b;
            if (!_builders.TryGetValue(kind, out b) || b == null) return null;
            return b(ctx);
        }

        public static void Clear() => _builders.Clear();

        static StageMaterialFactory() { ResetToDefaults(); }

        /// <summary>内置默认：参数与重构前 BattleStage2D 中的写法逐项一致。</summary>
        public static void ResetToDefaults()
        {
            _builders.Clear();

            Register(StageMaterialKind.StatusOverlay, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader
                       : Resources.Load<Shader>("Shaders/StatusOverlay");
                if (sh == null) sh = Shader.Find("WanXiang/StatusOverlay");
                return sh != null ? new Material(sh) : null;
            });

            Register(StageMaterialKind.Awaken, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader : Shader.Find("WanXiang/AwakenSprite");
                if (sh == null) return null;
                var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                m.SetFloat("_Amount", 1f);
                m.SetFloat("_Rim", 0.004f);
                m.SetFloat("_Inner", 0.22f);
                return m;
            });

            Register(StageMaterialKind.Corrupt, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader : Shader.Find("WanXiang/CorruptSprite");
                if (sh == null) return null;
                var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                m.SetFloat("_Amount", 0.85f);
                m.SetFloat("_Dirt", 0.45f);
                m.SetFloat("_Pulse", 0.35f);
                // 裂纹随机化：每只敌人风格/密度/宽度/种子不同（保持原有随机调用顺序）
                bool mainStyle = UnityEngine.Random.value < 0.5f;
                m.SetFloat("_Style", mainStyle ? 1f : 0f);
                m.SetFloat("_CrackScale", UnityEngine.Random.Range(3.2f, 5.2f));
                m.SetFloat("_CrackWidth", UnityEngine.Random.Range(0.12f, 0.20f));
                m.SetFloat("_Seed", UnityEngine.Random.Range(0f, 10f));
                return m;
            });

            Register(StageMaterialKind.Aura, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader : Resources.Load<Shader>("Battle/StatusAura");
                if (sh == null) return null;
                var m = new Material(sh);
                if (ctx.NoiseTexture != null) m.SetTexture("_NoiseTex", ctx.NoiseTexture);
                m.SetFloat("_Speed", 0.8f);
                return m;
            });

            Register(StageMaterialKind.StatusAura, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader : Resources.Load<Shader>("Battle/StatusAura");
                if (sh == null) return null;
                var m = new Material(sh);
                if (ctx.NoiseTexture != null) m.SetTexture("_NoiseTex", ctx.NoiseTexture);
                m.SetFloat("_Speed", 1.0f);
                return m;
            });

            Register(StageMaterialKind.InkDissolve, ctx =>
            {
                var sh = ctx.Shader != null ? ctx.Shader : Resources.Load<Shader>("Battle/InkDissolve");
                if (sh == null || !sh.isSupported) return null;
                var m = new Material(sh);
                if (ctx.MainTexture != null) m.SetTexture("_MainTex", ctx.MainTexture);
                if (ctx.NoiseTexture != null) m.SetTexture("_NoiseTex", ctx.NoiseTexture);
                m.SetFloat("_NoiseScale", 3.5f);
                m.SetFloat("_EdgeWidth", 0.10f);
                m.SetFloat("_EdgeStrength", 0.55f);
                m.SetFloat("_Invert", 1f);
                m.SetColor("_EdgeColor", new Color(0.10f, 0.09f, 0.08f, 1f));
                m.SetFloat("_Progress", 0f);
                return m;
            });
        }
    }
}
