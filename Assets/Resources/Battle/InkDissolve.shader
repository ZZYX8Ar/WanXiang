// ============================================================================
//  WanXiang/Battle/InkDissolve —— 单位死亡「墨蚀」溶解（T13）
//  ---------------------------------------------------------------------------
//  2026-10-24 用户："T13 墨迹溶解可以有，敌人或者我方死亡了可以使用"
//
//  表现：不是淡出，而是**墨从边缘啃进来**，被啃掉的地方露出纸底；
//        溶解前沿有一道「湿边」（加深 + 少量外扩），比纯 alpha 淡出更像水墨。
//
//  实现要点：
//   · _Progress 0→1；噪声（双频）决定溶解前沿；前沿宽度 _EdgeWidth；
//   · 湿边颜色 _EdgeColor 与强度 _EdgeStrength（沿着前沿一圈）；
//   · 保留顶点色/材质色相乘（SpriteRenderer.color 仍可控）；
//   · 双 pass（Universal2D + SRPDefaultUnlit）：只写一个 tag 在 2D Renderer 下可能整块不渲染。
// ============================================================================
Shader "WanXiang/Battle/InkDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NoiseTex ("Noise (R)", 2D) = "gray" {}

        _Progress ("Progress", Range(0,1)) = 0
        _NoiseScale ("Noise Scale", Float) = 3.5
        _EdgeWidth ("Wet Edge Width", Range(0.01,0.3)) = 0.10
        _EdgeColor ("Wet Edge Color", Color) = (0.10,0.09,0.08,1)
        _EdgeStrength ("Wet Edge Strength", Range(0,1)) = 0.55
        _Invert ("Invert Mask (1 = 从外向内)", Range(0,1)) = 1
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
            Name "InkDissolve2D"
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
                float4 _NoiseTex_ST;
                float _Progress;
                float _NoiseScale;
                float _EdgeWidth;
                float4 _EdgeColor;
                float _EdgeStrength;
                float _Invert;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 uvN:TEXCOORD1; float4 color:COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvN = v.uv * _NoiseScale;
                o.color = v.color * _Color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // 双频噪声做不规则溶解前沿
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uvN).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uvN * 2.9 + float2(0.31, 0.17)).r;
                float n  = n1 * 0.7 + n2 * 0.3;

                // 径向权重：默认从边缘往里蚀（中心最后消失）
                float2 d2 = i.uv - 0.5;
                float radial = saturate(length(d2) * 1.9);
                float maskInput = lerp(n, (n * 0.55 + radial * 0.45), _Invert);

                float threshold = lerp(-0.15, 1.05, _Progress);   // 0 = 全在，1 = 全无
                float visible = smoothstep(threshold, threshold + _EdgeWidth, maskInput);

                // 湿边：贴着前沿的一圈加深
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, abs(maskInput - threshold));
                edge = saturate(edge) * _EdgeStrength;

                c.rgb = lerp(c.rgb, _EdgeColor.rgb, edge * c.a);
                c.a *= visible;
                c *= i.color;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "InkDissolveSRP"
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
                float4 _NoiseTex_ST;
                float _Progress;
                float _NoiseScale;
                float _EdgeWidth;
                float4 _EdgeColor;
                float _EdgeStrength;
                float _Invert;
            CBUFFER_END

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 uvN:TEXCOORD1; float4 color:COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvN = v.uv * _NoiseScale;
                o.color = v.color * _Color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uvN).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uvN * 2.9 + float2(0.31, 0.17)).r;
                float n  = n1 * 0.7 + n2 * 0.3;

                float2 d2 = i.uv - 0.5;
                float radial = saturate(length(d2) * 1.9);
                float maskInput = lerp(n, (n * 0.55 + radial * 0.45), _Invert);

                float threshold = lerp(-0.15, 1.05, _Progress);
                float visible = smoothstep(threshold, threshold + _EdgeWidth, maskInput);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, abs(maskInput - threshold));
                edge = saturate(edge) * _EdgeStrength;

                c.rgb = lerp(c.rgb, _EdgeColor.rgb, edge * c.a);
                c.a *= visible;
                c *= i.color;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
