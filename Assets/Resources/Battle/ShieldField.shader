// ============================================================================
//  WanXiang/Battle/ShieldField —— 护盾膜（T5，2026-10-24）
//  ---------------------------------------------------------------------------
//  设计：贴在单位身上的半透明六边形膜，破盾时碎裂淡出。
//   · _Strength 0..1 由护盾值驱动（越高越亮）；
//   · 网格缓慢上移（"能量在流动"），边缘更亮（UV 距中心的径向加强）；
//   · 命中涟漪：_HitPhase 由代码在受击时置 1，之后自己衰减 —— 一圈扩散亮环；
//   · 双 pass（Universal2D + SRPDefaultUnlit），避免 2D Renderer 下整块不渲染。
//  ⚠ 不遮挡判定：alpha 很低（基础 0.28 上限），sortingOrder 压在立绘之上但透明。
// ============================================================================
Shader "WanXiang/Battle/ShieldField"
{
    Properties
    {
        [PerRendererData] _MainTex ("Hex Grid", 2D) = "white" {}
        _Color ("Tint", Color) = (0.62, 0.90, 1.0, 1)
        _Strength ("Strength", Range(0,1)) = 0.7
        _ScrollSpeed ("Scroll Speed", Float) = 0.06
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.28
        _RimPower ("Rim Power", Range(0.5,6)) = 2.2
        _HitPhase ("Hit Ripple (0..1)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Shield2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Strength;
                float _ScrollSpeed;
                float _BaseAlpha;
                float _RimPower;
                float _HitPhase;
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
                float t = _Time.y;
                // 网格缓慢上移（能量流动），UV 重复两次让格子更密
                float2 uv = i.uv * 2.0 + float2(sin(t * 0.7) * 0.02, t * _ScrollSpeed);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // 边缘光：离中心越远越亮（圆膜感）
                float2 d = i.uv - 0.5;
                float r = saturate(length(d) * 2.0);
                float rim = pow(saturate(r), _RimPower);

                // 命中涟漪：一圈向外扩散的亮环（_HitPhase 由代码衰减）
                float ripple = 0.0;
                if (_HitPhase > 0.001)
                {
                    float ring = abs(r - (1.0 - _HitPhase));
                    ripple = saturate(1.0 - ring / 0.12) * _HitPhase;
                }

                float a = (tex.a * (0.55 + rim * 0.75) + rim * 0.20) * _BaseAlpha * _Strength;
                a += ripple * 0.55;

                float3 col = tex.rgb * (0.7 + rim * 0.5) + ripple * float3(1.0, 0.96, 0.85);
                return float4(col * i.color.rgb, saturate(a) * i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShieldSRP"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Strength;
                float _ScrollSpeed;
                float _BaseAlpha;
                float _RimPower;
                float _HitPhase;
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
                float t = _Time.y;
                float2 uv = i.uv * 2.0 + float2(sin(t * 0.7) * 0.02, t * _ScrollSpeed);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float2 d = i.uv - 0.5;
                float r = saturate(length(d) * 2.0);
                float rim = pow(saturate(r), _RimPower);
                float ripple = 0.0;
                if (_HitPhase > 0.001)
                {
                    float ring = abs(r - (1.0 - _HitPhase));
                    ripple = saturate(1.0 - ring / 0.12) * _HitPhase;
                }
                float a = (tex.a * (0.55 + rim * 0.75) + rim * 0.20) * _BaseAlpha * _Strength;
                a += ripple * 0.55;
                float3 col = tex.rgb * (0.7 + rim * 0.5) + ripple * float3(1.0, 0.96, 0.85);
                return float4(col * i.color.rgb, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
