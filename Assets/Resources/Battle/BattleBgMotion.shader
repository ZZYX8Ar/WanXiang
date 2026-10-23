// ============================================================================
//  WanXiang/BattleBgMotion —— 战斗背景"活起来"的 shader（URP 14 / 2D Renderer 兼容）
//  ---------------------------------------------------------------------------
//  2026-10-23 用户："叠一层雾把背景糊了，看不出原画" ⇒ 不再用"白色雾层叠加"。
//  改成**在背景贴图本身上做**：
//   ① UV 微扰（噪声驱动）—— 画面像隔着空气在看，不改变明暗；
//   ② 只对**中暗部**生效（亮度掩膜），纸面留白区域一点都不动 ⇒ 不会发灰发白；
//   ③ 水面波光：只在指定高度以下、按噪声调制**加一点点亮度**（max 3%），不做整屏提亮；
//   ④ 呼吸：整体亮度 ±2%（乘性），不是叠白色。
//
//  ⚠ 用法：SpriteRenderer.material = 此材质，_MainTex = 背景 Sprite 的贴图。
//  ⚠ 因为要采样贴图自身，材质必须按"每张背景一个实例"创建（不要共享）。
// ============================================================================
Shader "WanXiang/BattleBgMotion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _NoiseScale ("Noise Scale", Float) = 3.0
        _NoiseSpeed ("Noise Speed", Float) = 0.02

        _Distort ("UV Distort (world)", Float) = 0.0035
        _Breath ("Brightness Breath", Float) = 0.02
        _BreathSpeed ("Breath Speed", Float) = 0.10

        _DarkMaskLo ("Dark Mask Lo", Float) = 0.35
        _DarkMaskHi ("Dark Mask Hi", Float) = 0.80

        _WaterY ("Water Line (0..1)", Float) = 0.35
        _WaterStrength ("Water Strength", Float) = 0.035
        _WaterSpeed ("Water Speed", Float) = 0.35

        _InkPulse ("Ink Pulse", Float) = 0.015
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BgMotion"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);   SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _NoiseTex_ST;
                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _Distort;
                float  _Breath;
                float  _BreathSpeed;
                float  _DarkMaskLo;
                float  _DarkMaskHi;
                float  _WaterY;
                float  _WaterStrength;
                float  _WaterSpeed;
                float  _InkPulse;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // 采样噪声（两张不同缩放叠加，避免看出平铺）
            float sampleNoise(float2 uv, float t)
            {
                float2 nuv = uv * _NoiseScale + float2(t * _NoiseSpeed, -t * _NoiseSpeed * 0.6);
                float a = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r;
                float b = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv * 2.7 + float2(-t * 0.013, t * 0.01)).r;
                return saturate(a * 0.65 + b * 0.35);
            }

            float4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;

                // ---- ① 噪声 + UV 微扰（只扰动 0.3% 视口，肉眼是"空气在动"，不是画面在抖）----
                float n  = sampleNoise(i.uv, t);
                float2 duv = i.uv + (n - 0.5) * _Distort;

                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, duv);

                // ---- ② 亮度掩膜：越亮（纸面）越不参与动效 ----
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                float darkMask = saturate((_DarkMaskHi - lum) / max(0.001, _DarkMaskHi - _DarkMaskLo));

                // ---- ③ 呼吸：乘性亮度 ±2%（不会发白）----
                float breath = 1.0 + sin(t * _BreathSpeed * 6.2831) * _Breath;
                c.rgb *= breath;

                // ---- ④ 墨色微脉冲：暗部随噪声轻微加深/变浅（墨在纸里洇）----
                float ink = (n - 0.5) * _InkPulse * darkMask;
                c.rgb *= (1.0 + ink);

                // ---- ⑤ 水面波光：只在 _WaterY 以下，按噪声细纹加一点点亮 ----
                //   注意用 smoothstep 做软边界，硬边界会看出"水平分割线"。
                float waterBand = smoothstep(_WaterY + 0.06, _WaterY - 0.06, i.uv.y);
                float ripple = sin((i.uv.x * 60.0) + t * _WaterSpeed * 3.0 + n * 8.0);
                ripple = smoothstep(0.55, 1.0, ripple) * waterBand * _WaterStrength;
                c.rgb += ripple * float3(0.9, 0.97, 1.0) * darkMask;

                c *= i.color;      // SpriteRenderer.color / 材质 tint
                return c;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
