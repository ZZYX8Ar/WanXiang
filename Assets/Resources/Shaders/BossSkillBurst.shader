// 首领技能爆发（2026-10-26）
// 环形冲击 + 放射条纹 + 元素色，_Progress 0→1 单次播放。
Shader "WanXiang/BossSkillBurst"
{
    Properties
    {
        _Color     ("Color", Color) = (1,0.8,0.5,1)
        _Progress  ("Progress", Range(0,1)) = 0
        _RingWidth ("Ring Width", Range(0.01,0.5)) = 0.12
        _Streaks   ("Streak Count", Range(0,48)) = 16
        _Intensity ("Intensity", Range(0,3)) = 1
        _Shape     ("Shape (0环1刃2晶3涡)", Range(0,3)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One   // 加色混合：在黑场上更"炸"
        ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color; float _Progress, _RingWidth, _Streaks, _Intensity, _Shape;

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float ang = atan2(p.y, p.x);

                // 扩散的环：半径随进度推进，环带外扩变淡
                float ring = 1.0 - smoothstep(0.0, _RingWidth, abs(r - _Progress));
                ring *= (1.0 - _Progress) * 1.4;

                // 放射条纹：按角度量化，随时间衰减
                float s = abs(frac(ang / 6.28318 * _Streaks) - 0.5) * 2.0;
                float streak = pow(1.0 - s, 6.0) * max(0.0, 1.0 - r) * (1.0 - _Progress) * 0.55;

                // 中心爆点（前 20% 时间最亮）
                float core = smoothstep(0.35, 0.0, r) * saturate(1.0 - _Progress * 3.0);

                float a = saturate((ring + streak + core) * _Intensity);

                // ---- 形状变体 ----
                int shape = (int)round(_Shape);
                if (shape == 1)
                {
                    // 双刃：左右两道水平刃光（白魍 双子）
                    float blades = smoothstep(0.10, 0.0, abs(abs(p.y) - _Progress * 0.55))
                                 + smoothstep(0.06, 0.0, abs(abs(p.y) - _Progress * 0.30));
                    float cut = blades * (1.0 - _Progress) * max(0.0, 1.0 - abs(p.x)) * 1.6;
                    a = saturate((ring * 0.35 + cut + core * 1.2) * _Intensity);
                }
                else if (shape == 2)
                {
                    // 六角冰晶：6 条主棱 + 12 条副棱
                    float ang6 = frac(ang / 6.28318 * 6.0);
                    float facet = pow(1.0 - abs(ang6 - 0.5) * 2.0, 10.0);
                    float crystal = facet * saturate(1.0 - abs(r - _Progress * 0.9) * 2.2) * (1.0 - _Progress);
                    a = saturate((ring * 0.5 + crystal * 1.8 + core) * _Intensity);
                }
                else if (shape == 3)
                {
                    // 混沌漩涡：条纹随半径扭转
                    float swirlAng = ang + r * 6.0;
                    float ss = abs(frac(swirlAng / 6.28318 * _Streaks) - 0.5) * 2.0;
                    float spiral = pow(1.0 - ss, 4.0) * max(0.0, 1.0 - r) * (1.0 - _Progress);
                    float eye = smoothstep(0.30, 0.0, r) * saturate(1.0 - _Progress * 2.0);
                    a = saturate((ring * 0.4 + spiral * 1.4 + eye * 1.3) * _Intensity);
                }
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
