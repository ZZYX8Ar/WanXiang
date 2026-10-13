// ============================================================================
//  万相 · 技能特效 · 冲击波 shader（SkillRing）
//  ---------------------------------------------------------------------------
//  终结技 / 觉醒技演出的「冲击波」：一道/多道能量环由内向外扩张、变薄、消散。
//    · 环半径随 _Progress 外扩，厚度随之收细（能量在扩散中耗散）
//    · _RingRings 道环错相位扩散，觉醒技给双环更厚重
//  环由 uv 解析生成；_MainTex 只作柔边 alpha 底。真正的"扩张"由 GameObject 缩放
//  与 shader 进度双驱动（缩放给体量、进度给衰减节奏）。
// ============================================================================
Shader "WanXiang/Skill/Ring"
{
    Properties
    {
        [PerRendererData] _MainTex   ("Sprite Texture", 2D) = "white" {}
        _Color        ("Element Color", Color) = (1,1,1,1)
        _Progress     ("Progress 0..1", Range(0,1)) = 0
        _Intensity    ("Intensity", Range(0,4)) = 1
        _RingRings    ("Ring Count", Range(1,3)) = 1
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
            float _RingRings;

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
                float baseA = tex2D(_MainTex, i.uv).a;
                float prog = saturate(_Progress);

                float a = 0.0;
                for (int k = 0; k < 3; k++)
                {
                    float fk = (float)k;
                    if (fk >= _RingRings) break;
                    float pk = saturate(prog + fk * 0.18);       // 后一道稍慢
                    float r = lerp(0.08, 0.98, pk);
                    float thick = lerp(0.20, 0.035, pk);
                    float band = smoothstep(thick, 0.0, abs(d - r));
                    float fade = (1.0 - pk) * (1.0 - pk);
                    a += band * fade * 1.1;
                }

                a *= baseA * _Intensity;
                float3 hot = lerp(i.color.rgb, float3(1, 1, 1), 0.15);
                fixed4 c = fixed4(hot, a);
                clip(a - 0.004);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
