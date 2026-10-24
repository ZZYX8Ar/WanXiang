// ============================================================================
//  WanXiang/Battle/StatusAura —— 元素状态光环（2026-10-24）
//  ---------------------------------------------------------------------------
//  用途：单位脚下的一圈"状态色光"——灼烧（橙红跳动）、冻结（冷蓝霜雾）、
//       湿（蓝绿水波）、中毒/侵蚀（黄绿）。
//  · 纯程序化（只借一张噪声图），不额外占贴图；
//  · 强度 `_Strength` 跟层数走（1~3 层）；
//  · 双 pass（Universal2D + SRPDefaultUnlit），避免 2D Renderer 下不渲染。
//  ⚠ 画在**立绘之下**（sortingOrder = body - 1），不遮血条、不挡判定。
// ============================================================================
Shader "WanXiang/Battle/StatusAura"
{
    Properties
    {
        [PerRendererData] _MainTex ("White", 2D) = "white" {}
        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _Color ("Status Color", Color) = (1, 0.5, 0.2, 1)
        _Strength ("Strength", Range(0,1)) = 0.7
        _Speed ("Speed", Float) = 1.0
        _Ring ("Ring Radius", Range(0.1,0.7)) = 0.42
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Aura2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Strength;
                float _Speed;
                float _Ring;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 d = i.uv - 0.5;
                float r = length(d) * 2.0;

                // 环带：中心留空（脚踩在圈里），外缘柔化
                float ring = smoothstep(_Ring - 0.22, _Ring, r) * (1.0 - smoothstep(_Ring, _Ring + 0.30, r));

                // 噪声扰动：像火苗/水波在动
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           i.uv * 2.4 + float2(t * 0.12, -t * 0.09)).r;
                float flick = 0.72 + 0.28 * sin(t * 3.1 + n * 9.0);

                float a = ring * (_Strength) * flick * (0.55 + n * 0.65);
                // 环内侧一点微光，让"脚下发光"更明显
                a += smoothstep(_Ring, 0.0, r) * 0.05 * _Strength;

                float3 col = i.color.rgb * (0.9 + n * 0.5);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "AuraSRP"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Strength;
                float _Speed;
                float _Ring;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 d = i.uv - 0.5;
                float r = length(d) * 2.0;
                float ring = smoothstep(_Ring - 0.22, _Ring, r) * (1.0 - smoothstep(_Ring, _Ring + 0.30, r));
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           i.uv * 2.4 + float2(t * 0.12, -t * 0.09)).r;
                float flick = 0.72 + 0.28 * sin(t * 3.1 + n * 9.0);
                float a = ring * _Strength * flick * (0.55 + n * 0.65);
                a += smoothstep(_Ring, 0.0, r) * 0.05 * _Strength;
                float3 col = i.color.rgb * (0.9 + n * 0.5);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
