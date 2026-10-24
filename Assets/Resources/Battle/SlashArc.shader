// ============================================================================
//  WanXiang/Battle/SlashArc —— 斩击弧光（T6，2026-10-24）
//  ---------------------------------------------------------------------------
//  用途：普攻 / 战技出手瞬间，从出手者朝目标甩一道**笔刷弧光**（0.18s）。
//   · _Progress 0→1：沿弧"扫过去"（用 uv.x 做遮罩），尾部渐隐；
//   · 边缘发白提亮（Arc Edge），主体用元素色；
//   · 双 pass（Universal2D + SRPDefaultUnlit），2D Renderer 下不会整块不渲染。
//  ⚠ 贴图 `FX_slash_arc` 是程序化笔刷（外弧粗、两端收细、带飞白），世界尺寸由代码按
//    出手者与目标的距离定，不写死。
// ============================================================================
Shader "WanXiang/Battle/SlashArc"
{
    Properties
    {
        [PerRendererData] _MainTex ("Arc", 2D) = "white" {}
        _Color ("Element Color", Color) = (0.95, 0.85, 0.70, 1)
        _Progress ("Progress", Range(0,1)) = 0
        _Soft ("Softness", Range(0.01,0.6)) = 0.18
        _EdgeBoost ("Edge Boost", Range(0,2)) = 0.9
        _Alpha ("Alpha", Range(0,1)) = 1
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
            Name "Slash2D"
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
                float _Progress;
                float _Soft;
                float _EdgeBoost;
                float _Alpha;
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
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // 沿 x "扫过"：前沿是斜的（y 越大越早），像刀锋略斜切过去
                float front = _Progress * 1.6 - 0.25 + (i.uv.y - 0.5) * 0.12;
                float cover = 1.0 - smoothstep(front - _Soft, front, i.uv.x);

                // 尾部渐隐：越靠近起点越淡（拖尾感）
                float tail = smoothstep(0.0, 0.35, _Progress) * (1.0 - smoothstep(front - 0.45, front, i.uv.x) * 0.65);

                float a = tex.a * cover * tail * _Alpha;
                // 前沿提亮：一条窄亮边（刀光"过曝"的那一下）
                float edge = (1.0 - smoothstep(0.0, _Soft * 0.9, abs(i.uv.x - front))) * 0.55 * _EdgeBoost;

                float3 col = tex.rgb * i.color.rgb * (1.0 + edge * 2.2);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SlashSRP"
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
                float _Progress;
                float _Soft;
                float _EdgeBoost;
                float _Alpha;
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
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float front = _Progress * 1.6 - 0.25 + (i.uv.y - 0.5) * 0.12;
                float cover = 1.0 - smoothstep(front - _Soft, front, i.uv.x);
                float tail = smoothstep(0.0, 0.35, _Progress) * (1.0 - smoothstep(front - 0.45, front, i.uv.x) * 0.65);
                float a = tex.a * cover * tail * _Alpha;
                float edge = (1.0 - smoothstep(0.0, _Soft * 0.9, abs(i.uv.x - front))) * 0.55 * _EdgeBoost;
                float3 col = tex.rgb * i.color.rgb * (1.0 + edge * 2.2);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
