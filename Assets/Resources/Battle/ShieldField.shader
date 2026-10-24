// ============================================================================
//  WanXiang/Battle/ShieldField —— 护盾膜（T5，2026-10-24 重写）
//  ---------------------------------------------------------------------------
//  ⛔ 第一版两个坑（都已实测）：
//   ① 按方形 uv 算遮罩 ⇒ 整块变"青色方板"；
//   ② 网格线 alpha 只有 ~0.47，乘系数后实显 ~6% ⇒ 肉眼看等于没有。
//  ⇒ 现在：圆形膜面 + 亮轮廓环 + 极淡网格（单位立绘只有 ~0.15 世界尺寸，靠轮廓读取）。
//  双 pass（Universal2D + SRPDefaultUnlit），避免 2D Renderer 下整块不渲染。
// ============================================================================
Shader "WanXiang/Battle/ShieldField"
{
    Properties
    {
        [PerRendererData] _MainTex ("Hex Grid", 2D) = "white" {}
        _Color ("Tint", Color) = (0.72, 0.94, 1, 1)
        _Strength ("Strength", Range(0,1)) = 0.8
        _ScrollSpeed ("Scroll Speed", Float) = 0.06
        _BaseAlpha ("Base Alpha", Range(0,1)) = 1.0
        _HitPhase ("Hit Ripple (0..1)", Range(0,1)) = 0
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
                float r = length(i.uv - 0.5) * 2.0;
                float body = saturate(1.0 - smoothstep(0.60, 1.0, r));
                float ring = smoothstep(0.58, 0.88, r) * (1.0 - smoothstep(0.88, 1.0, r));

                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * 3.0 + float2(0.0, t * _ScrollSpeed));
                float grid = tex.a * 1.4;

                float ripple = 0.0;
                if (_HitPhase > 0.001)
                {
                    float rd = abs(r - (1.0 - _HitPhase));
                    ripple = saturate(1.0 - rd / 0.16) * _HitPhase;
                }

                float a = (body * 0.16 + ring * 0.85 + grid * body * 0.55 + ripple * 0.75) * _BaseAlpha * _Strength;
                float3 col = float3(0.72, 0.94, 1.0) * (0.85 + ring * 0.55) + ripple * float3(1.0, 0.97, 0.88);
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
                float r = length(i.uv - 0.5) * 2.0;
                float body = saturate(1.0 - smoothstep(0.60, 1.0, r));
                float ring = smoothstep(0.58, 0.88, r) * (1.0 - smoothstep(0.88, 1.0, r));
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * 3.0 + float2(0.0, t * _ScrollSpeed));
                float grid = tex.a * 1.4;
                float ripple = 0.0;
                if (_HitPhase > 0.001)
                {
                    float rd = abs(r - (1.0 - _HitPhase));
                    ripple = saturate(1.0 - rd / 0.16) * _HitPhase;
                }
                float a = (body * 0.16 + ring * 0.85 + grid * body * 0.55 + ripple * 0.75) * _BaseAlpha * _Strength;
                float3 col = float3(0.72, 0.94, 1.0) * (0.85 + ring * 0.55) + ripple * float3(1.0, 0.97, 0.88);
                return float4(col * i.color.rgb, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
