// ============================================================================
//  WanXiang/BattleBgMotion —— 战斗背景动效 v2（URP 14 / 2D Renderer）
//  2026-10-23：用户要"好看、常见的动效"，贴图可程序化画。
//  效果（全部作用在**背景贴图本身**，不叠白雾）：
//   ① 云影掠过（乘性压暗 ≤10%，只在上半部）
//   ② 丁达尔光束（斜向光带缓慢漂移，加色 ≤6%）
//   ③ 水面波光 + 倒影 UV 波动（水位线以下）
//   ④ 墨呼吸（UV 0.25% 微扰 + 暗部 ±2%）
//   ⑤ 纸面保护：亮度掩膜，纸白区域几乎不动 ⇒ 不发灰
//  ⚠ 两个 pass 内容一致（Universal2D / SRPDefaultUnlit），避免"背景整个不渲染"。
// ============================================================================
Shader "WanXiang/BattleBgMotion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _CloudTex ("Cloud (R)", 2D) = "gray" {}
        _Phase ("Time Phase", Float) = 0
        _Distort ("UV Distort", Float) = 0.0025
        _InkPulse ("Ink Pulse", Float) = 0.02
        _CloudSpeed ("Cloud Speed", Float) = 0.010
        _CloudStrength ("Cloud Strength", Float) = 0.12
        _CloudScale ("Cloud Scale", Float) = 1.2
        _CloudY ("Cloud Band Y", Float) = 0.60
        _RayStrength ("Ray Strength", Float) = 0.07
        _RaySpeed ("Ray Speed", Float) = 0.05
        _RayTilt ("Ray Tilt", Float) = 1.4
        _WaterY ("Water Line", Float) = 0.42
        _WaterWobble ("Water Wobble", Float) = 0.006
        _WaterShine ("Water Shine", Float) = 0.12
        _PaperGuard ("Paper Guard", Float) = 0.90

        // ---- 追加：竖向雾带 / 纸纹颗粒 / 暗角（2026-10-23）----
        _FogBandY ("Fog Band Y", Float) = 0.52
        _FogBandH ("Fog Band Height", Float) = 0.10
        _FogStrength ("Fog Strength", Float) = 0.10
        _FogSpeed ("Fog Speed", Float) = 0.015
        _Grain ("Paper Grain", Float) = 0.035
        _GrainSpeed ("Grain Speed", Float) = 0.6
        _Vignette ("Vignette", Float) = 0.10
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
            Name "BgMotion2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_CloudTex);  SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _NoiseTex_ST;
                float4 _CloudTex_ST;
                float _Phase;
                float _Distort;
                float _InkPulse;
                float _CloudSpeed;
                float _CloudStrength;
                float _CloudScale;
                float _CloudY;
                float _RayStrength;
                float _RaySpeed;
                float _RayTilt;
                float _WaterY;
                float _WaterWobble;
                float _WaterShine;
                float _PaperGuard;
                float _FogBandY;
                float _FogBandH;
                float _FogStrength;
                float _FogSpeed;
                float _Grain;
                float _GrainSpeed;
                float _Vignette;
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

            float4 BgColor(v2f i)
            {
                float t = _Time.y + _Phase;
                float2 uv = i.uv;

                // ---- 墨呼吸：UV 极微扰（只影响"看"，不影响明度）----
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 2.0 + float2(t * 0.010, -t * 0.008)).r;
                float2 duv = uv + (n - 0.5) * _Distort;

                // ---- 水面：水位线以下让倒影跟着波走（二次采样，肉眼能看到"水在动"）----
                float waterBand = smoothstep(_WaterY + 0.05, _WaterY - 0.05, uv.y);
                float2 wuv = duv + float2(sin(uv.y * 70.0 + t * 1.8) * _WaterWobble,
                                          cos(uv.x * 40.0 + t * 1.3) * _WaterWobble * 0.5);
                float4 cDry = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, duv);
                float4 cWet = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, wuv);
                float4 c = lerp(cDry, cWet, waterBand);

                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                float paper = saturate((_PaperGuard - lum) / 0.40);   // 越亮越接近 0

                // ---- ① 云影掠过（乘性压暗，绝不加白）----
                float cloud = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex,
                                               uv * _CloudScale + float2(t * _CloudSpeed, t * _CloudSpeed * 0.15)).r;
                float band = saturate(1.0 - abs(uv.y - _CloudY) / 0.85);
                float shade = 1.0 - _CloudStrength * smoothstep(0.42, 0.90, cloud) * paper * band;
                c.rgb *= shade;

                // ---- ④ 墨色微脉冲（暗部）----
                c.rgb *= 1.0 + (n - 0.5) * _InkPulse * paper;

                // ---- ③ 水面波光（细碎高光）----
                float wave = sin(uv.x * 46.0 + uv.y * 9.0 + t * 1.9 + n * 7.0);
                float shine = smoothstep(0.72, 1.0, wave) * waterBand * _WaterShine * paper;
                c.rgb += shine * float3(0.95, 0.99, 1.0);

                // ---- ② 丁达尔光束（斜向、缓慢漂移）----
                float ray = frac((uv.x + uv.y * _RayTilt) * 2.0 + t * _RaySpeed);
                float rays = smoothstep(0.90, 1.0, ray) * smoothstep(0.30, 0.85, uv.y) * _RayStrength;
                c.rgb += rays * float3(1.0, 0.96, 0.86) * paper;

                // ---- ⑥ 竖向雾带（贴着地平线的一条，缓慢横移；只加一点点亮度）----
                //   参考：TinyPlay/VerticalFog、marcozakaria/Vertical Fog 的"带状雾"做法。
                float bandY = exp(-pow((uv.y - _FogBandY) / max(0.001, _FogBandH), 2.0));
                float fogN = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex,
                                              uv * float2(2.2, 0.6) + float2(t * _FogSpeed, 0.0)).r;
                c.rgb += _FogStrength * bandY * smoothstep(0.35, 0.85, fogN) * float3(1.0, 0.99, 0.96);

                // ---- ⑦ 纸纹颗粒（高频细噪，乘性；让它像纸不像"AI 图"）----
                float grain = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                               uv * 18.0 + float2(t * _GrainSpeed * 0.03, t * _GrainSpeed * 0.021)).r;
                c.rgb *= 1.0 + (grain - 0.5) * _Grain;

                // ---- ⑧ 暗角（四角压暗，随呼吸极轻微起伏）----
                float2 vd = uv - 0.5;
                float vig = saturate(1.0 - dot(vd, vd) * 2.4);
                c.rgb *= lerp(1.0 - _Vignette, 1.0, vig);

                c *= i.color;
                return c;
            }

            float4 frag (v2f i) : SV_Target { return BgColor(i); }
            ENDHLSL
        }

        Pass
        {
            Name "BgMotionSRP"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_CloudTex);  SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _NoiseTex_ST;
                float4 _CloudTex_ST;
                float _Phase;
                float _Distort;
                float _InkPulse;
                float _CloudSpeed;
                float _CloudStrength;
                float _CloudScale;
                float _CloudY;
                float _RayStrength;
                float _RaySpeed;
                float _RayTilt;
                float _WaterY;
                float _WaterWobble;
                float _WaterShine;
                float _PaperGuard;
                float _FogBandY;
                float _FogBandH;
                float _FogStrength;
                float _FogSpeed;
                float _Grain;
                float _GrainSpeed;
                float _Vignette;
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

            float4 BgColor(v2f i)
            {
                float t = _Time.y + _Phase;
                float2 uv = i.uv;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * 2.0 + float2(t * 0.010, -t * 0.008)).r;
                float2 duv = uv + (n - 0.5) * _Distort;

                float waterBand = smoothstep(_WaterY + 0.05, _WaterY - 0.05, uv.y);
                float2 wuv = duv + float2(sin(uv.y * 70.0 + t * 1.8) * _WaterWobble,
                                          cos(uv.x * 40.0 + t * 1.3) * _WaterWobble * 0.5);
                float4 cDry = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, duv);
                float4 cWet = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, wuv);
                float4 c = lerp(cDry, cWet, waterBand);

                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                float paper = saturate((_PaperGuard - lum) / 0.40);

                float cloud = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex,
                                               uv * _CloudScale + float2(t * _CloudSpeed, t * _CloudSpeed * 0.15)).r;
                float band = saturate(1.0 - abs(uv.y - _CloudY) / 0.85);
                c.rgb *= 1.0 - _CloudStrength * smoothstep(0.42, 0.90, cloud) * paper * band;
                c.rgb *= 1.0 + (n - 0.5) * _InkPulse * paper;

                float wave = sin(uv.x * 46.0 + uv.y * 9.0 + t * 1.9 + n * 7.0);
                float shine = smoothstep(0.72, 1.0, wave) * waterBand * _WaterShine * paper;
                c.rgb += shine * float3(0.95, 0.99, 1.0);

                float ray = frac((uv.x + uv.y * _RayTilt) * 2.0 + t * _RaySpeed);
                float rays = smoothstep(0.90, 1.0, ray) * smoothstep(0.30, 0.85, uv.y) * _RayStrength;
                c.rgb += rays * float3(1.0, 0.96, 0.86) * paper;

                // ---- ⑥ 竖向雾带（贴着地平线的一条，缓慢横移；只加一点点亮度）----
                //   参考：TinyPlay/VerticalFog、marcozakaria/Vertical Fog 的"带状雾"做法。
                float bandY = exp(-pow((uv.y - _FogBandY) / max(0.001, _FogBandH), 2.0));
                float fogN = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex,
                                              uv * float2(2.2, 0.6) + float2(t * _FogSpeed, 0.0)).r;
                c.rgb += _FogStrength * bandY * smoothstep(0.35, 0.85, fogN) * float3(1.0, 0.99, 0.96);

                // ---- ⑦ 纸纹颗粒（高频细噪，乘性；让它像纸不像"AI 图"）----
                float grain = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                               uv * 18.0 + float2(t * _GrainSpeed * 0.03, t * _GrainSpeed * 0.021)).r;
                c.rgb *= 1.0 + (grain - 0.5) * _Grain;

                // ---- ⑧ 暗角（四角压暗，随呼吸极轻微起伏）----
                float2 vd = uv - 0.5;
                float vig = saturate(1.0 - dot(vd, vd) * 2.4);
                c.rgb *= lerp(1.0 - _Vignette, 1.0, vig);

                c *= i.color;
                return c;
            }

            float4 frag (v2f i) : SV_Target { return BgColor(i); }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/2D/Sprite-Unlit-Default"
}
