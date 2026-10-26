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

            fixed4 _Color; float _Progress, _RingWidth, _Streaks, _Intensity;

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
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
