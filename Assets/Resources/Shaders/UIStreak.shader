// ============================================================================
//  万相 · UI 拖尾 Shader（v2）
//  ---------------------------------------------------------------------------
//  2026-10-12 用户："这个横扫还是不太行啊，能不能自己去网上参考一下"
//
//  v1 的问题（用户截图：尾迹又粗又实、像一束白条）：
//    渐隐逻辑写成了"硬分段" —— 只有 uv.x > _Head 的 30% 算头部不渐隐，
//    剩下 70% **完全不透明** ⇒ 看起来是一根实心白棍。
//
//  v2 改动（参考 Unity UI 流光/拖尾的通用做法）：
//    ① **整条平滑衰减**：`a *= pow(uv.x, _FadePow)`
//       —— uv.x=1(头) 不衰减，uv.x=0(尾) 衰减到 0，中间平滑过渡。
//       不再用 step() 硬切，避免出现"一段实一段虚"的折线感。
//    ② 尾部提亮（速度感）改成按 `1-fade` 加权，不再整段提亮。
//    ③ _Head 仅保留为"头部保底不透明度"（避免头部过细消失），不再是分段开关。
//
//  ⚠ UGUI 自定义 shader 注意：Image 的 vertex color 会传到片元（i.color），
//    必须乘进去，否则运行时改 color 无效；Tags 要 Transparent + IgnoreProjector，
//    Blend 用 SrcAlpha OneMinusSrcAlpha（UGUI 标准混合）。
// ============================================================================

Shader "WanXiang/UI/Streak"
{
    Properties
    {
        _MainTex      ("Texture", 2D) = "white" {}
        _Color        ("Tint", Color) = (1, 1, 1, 1)
        // 尾迹衰减指数：越大尾越短促（1 = 线性渐隐）
        _FadePow      ("Fade Power", Range(0.5, 6)) = 2.6
        // 头部保底不透明度（防止头部过细时整条消失）
        _HeadFloor    ("Head Floor", Range(0, 1)) = 0.85
        // 整体不透明度
        _StreakAlpha  ("Streak Alpha", Range(0, 1)) = 0.75
        // 尾部提亮强度（0 = 不提亮）
        _TailGlow     ("Tail Glow", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Default"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float  _FadePow;
            float  _HeadFloor;
            float  _StreakAlpha;
            float  _TailGlow;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * i.color * _Color;

                // ★ 整条平滑衰减：头(uv.x=1)实 → 尾(uv.x=0)虚，中间 pow 平滑过渡
                float fade = pow(saturate(i.uv.x), _FadePow);
                // 头部保底：fade 太小时用 _HeadFloor 兜一下，避免细头消失
                float a = max(fade * _HeadFloor, fade);
                col.a *= a * _StreakAlpha;

                // 尾部按衰减程度提亮（越靠尾越亮一点，做速度感）
                float glow = (1.0 - fade) * _TailGlow;
                col.rgb = min(col.rgb * (1.0 + glow) + glow * 0.15, 1.0);

                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
