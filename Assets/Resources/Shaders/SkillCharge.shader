// ============================================================================
//  万相 · 技能特效 · 蓄力 shader（SkillCharge）
//  ---------------------------------------------------------------------------
//  终结技 / 觉醒技演出的「蓄力」段：能量向中心收拢。
//    · 中央核心随进度增强并脉动（一闪一闪，像气在聚）
//    · 一道能量环由外向内收缩（0.9 → 0.15），收到核心即爆发
//  全部几何由 uv 解析算出（不依赖无缝贴图）；_MainTex 只提供一朵柔边作 alpha 底。
//
//  ⚠ 与 StatusOverlay 同规矩：Resources/Shaders 下、SpriteRenderer 用、
//    顶点色 × _Tint、Blend SrcAlpha OneMinusSrcAlpha、ZWrite Off、clip。
//  ⚠ 进度 _Progress 由 C# 按 0..1 下发（见 SkillFxPlayer）。
// ============================================================================
Shader "WanXiang/Skill/Charge"
{
    Properties
    {
        [PerRendererData] _MainTex   ("Sprite Texture", 2D) = "white" {}
        _Color        ("Element Color", Color) = (1,1,1,1)
        _Progress     ("Progress 0..1", Range(0,1)) = 0
        _Intensity    ("Intensity", Range(0,3)) = 1
        _RingRings    ("Ring Count", Range(1,4)) = 1
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
                float2 p = (i.uv - 0.5) * 2.0;          // 中心 0，边 1
                float d = length(p);

                float baseA = tex2D(_MainTex, i.uv).a;   // 柔边底
                float prog  = saturate(_Progress);

                // ① 核心：越聚越亮，并叠加高频脉动（气在抖）
                float core = smoothstep(0.42, 0.0, d);
                float flick = 0.55 + 0.45 * sin(prog * 22.0);
                float coreA = core * (0.35 + 0.85 * prog) * flick;

                // ② 收缩能量环：半径 0.85 → 0.12；多环则错相位一起收
                float ringA = 0.0;
                for (int k = 0; k < 4; k++)
                {
                    float fk = (float)k;
                    if (fk >= _RingRings) break;
                    float phase = frac(prog * 1.0 + fk * 0.28);
                    float r = lerp(0.85, 0.10, phase);
                    float thick = lerp(0.16, 0.05, phase);
                    float band = smoothstep(thick, 0.0, abs(d - r));
                    ringA += band * (1.0 - prog * 0.3) * 0.9;
                }

                float a = (coreA + ringA) * baseA * _Intensity;
                a *= 1.0 - smoothstep(0.9, 1.0, prog);    // 蓄力末尾整体收敛（交给爆发）
                a *= saturate(prog * 4.0);                 // 起手别太突兀

                // 颜色：核心偏白热，外圈保留元素色
                float3 hot = lerp(i.color.rgb, float3(1, 1, 1), core * 0.6);
                fixed4 c = fixed4(hot * (0.7 + 0.6 * prog), a);
                clip(a - 0.004);
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
