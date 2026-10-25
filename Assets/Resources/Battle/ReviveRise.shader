// ============================================================================
//  WanXiang/Battle/ReviveRise —— 复活光柱（2026-10-25）
//  ---------------------------------------------------------------------------
//  用户："如果有可以复活的给他们一个复活的特效/Shader"（Boss 一两回合后复活）
//
//  一次 0.9s 的演出：
//   · 脚下一圈**扩散光环**（从中心向外）；
//   · 身上一道**竖向光柱**（下亮上淡，带噪声流动）；
//   · 整体在尾段淡出。
//  双 pass（Universal2D + SRPDefaultUnlit），2D Renderer 下不会整块不渲染。
// ============================================================================
Shader "WanXiang/Battle/ReviveRise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _Color ("Color", Color) = (1, 0.90, 0.62, 1)
        _Progress ("Progress", Range(0,1)) = 0
        _RingStrength ("Ring Strength", Range(0,2)) = 0.9
        _PillarStrength ("Pillar Strength", Range(0,2)) = 0.8
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
            Name "Revive2D"
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
                float _Progress;
                float _RingStrength;
                float _PillarStrength;
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
                float2 d = i.uv - float2(0.5, 0.18);      // 环心偏下（脚的位置）
                float r = length(float2(d.x, d.y * 0.55)) * 2.0;

                // 扩散环：半径随进度外扩，宽度固定
                float rr = lerp(0.15, 1.15, _Progress);
                float ring = 1.0 - saturate(abs(r - rr) / 0.16);

                // 竖向光柱：水平窄、上端淡出，噪声让光"流动"
                float pillarX = 1.0 - saturate(abs(i.uv.x - 0.5) / 0.14);
                float pillarY = saturate(1.0 - i.uv.y) * smoothstep(0.0, 0.15, i.uv.y);
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           float2(i.uv.x * 3.0, i.uv.y * 2.0 - t * 1.6)).r;
                float pillar = pillarX * pillarY * (0.55 + n * 0.85);

                float fade = 1.0 - smoothstep(0.62, 1.0, _Progress);
                float a = (ring * _RingStrength + pillar * _PillarStrength) * fade;

                float3 col = i.color.rgb * (1.0 + ring * 0.8);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ReviveSRP"
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
                float _Progress;
                float _RingStrength;
                float _PillarStrength;
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
                float2 d = i.uv - float2(0.5, 0.18);
                float r = length(float2(d.x, d.y * 0.55)) * 2.0;
                float rr = lerp(0.15, 1.15, _Progress);
                float ring = 1.0 - saturate(abs(r - rr) / 0.16);
                float pillarX = 1.0 - saturate(abs(i.uv.x - 0.5) / 0.14);
                float pillarY = saturate(1.0 - i.uv.y) * smoothstep(0.0, 0.15, i.uv.y);
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                           float2(i.uv.x * 3.0, i.uv.y * 2.0 - t * 1.6)).r;
                float pillar = pillarX * pillarY * (0.55 + n * 0.85);
                float fade = 1.0 - smoothstep(0.62, 1.0, _Progress);
                float a = (ring * _RingStrength + pillar * _PillarStrength) * fade;
                float3 col = i.color.rgb * (1.0 + ring * 0.8);
                return float4(col, saturate(a) * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
