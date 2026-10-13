// ============================================================================
//  万相 · 技能特效 · 爆发 shader（SkillBurst）
//  ---------------------------------------------------------------------------
//  终结技 / 觉醒技演出的「爆发」段：核心炸开 + 元素射线放射。
//    · 核心光斑由内向外扩张并迅速衰减（炸那一下）
//    · _RayCount 道元素射线从中心放射，随时间展开后淡出
//    · 整层高亮偏白热，越靠核心越白
//  几何全解析（不依赖无缝贴图）；_MainTex 只提供柔边 alpha 底。
//
//  ⚠ 同 StatusOverlay 规矩：Resources/Shaders、SpriteRenderer、顶点色 × _Color、
//    Blend SrcAlpha OneMinusSrcAlpha、ZWrite Off、clip。_Progress 由 C# 下发。
// ============================================================================
Shader "WanXiang/Skill/Burst"
{
    Properties
    {
        [PerRendererData] _MainTex   ("Sprite Texture", 2D) = "white" {}
        _Color        ("Element Color", Color) = (1,1,1,1)
        _Progress     ("Progress 0..1", Range(0,1)) = 0
        _Intensity    ("Intensity", Range(0,4)) = 1
        _RayCount     ("Ray Count", Range(3,24)) = 9
        _RaySharp     ("Ray Sharpness", Range(1,64)) = 14
        _Seed         ("Seed", Range(0,10)) = 0
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
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Progress;
            float _Intensity;
            float _RayCount;
            float _RaySharp;
            float _Seed;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float d = length(p);
                float ang = atan2(p.y, p.x);
                float baseA = tex2D(_MainTex, i.uv).a;
                float prog = saturate(_Progress);

                // ① 核心：半径随进度外扩，亮度起手最猛随后急衰减
                float coreR = 0.18 + prog * 0.85;
                float core = smoothstep(coreR + 0.30, coreR - 0.05, d);
                float punch = pow(1.0 - prog, 1.6);        // 炸那一下
                float coreA = core * punch * 1.6;

                // ② 射线：随角度呈尖刺，随进度从核心向外展开后淡出
                float spin = ang + _Seed + prog * 0.8;     // 缓慢旋转
                float spikes = pow(max(0.0, sin(spin * _RayCount)), _RaySharp);
                float rayR = 0.15 + prog * 1.05;           // 射线推进的前沿
                float rayMask = smoothstep(rayR, rayR - 0.5, d) * smoothstep(0.0, 0.25, d);
                float rayA = spikes * rayMask * (1.0 - prog) * 0.55;

                float a = (coreA + rayA) * baseA * _Intensity;
                a *= 1.0 - smoothstep(0.82, 1.0, prog);

                float3 hot = lerp(i.color.rgb, float3(1, 1, 1), clamp(core * 0.75 + punch * 0.35, 0.0, 1.0));
                fixed4 c = fixed4(hot, a);
                clip(a - 0.004);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
