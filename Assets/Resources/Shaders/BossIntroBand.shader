// 首领登场用的斜切色带（2026-10-26）
// 无光照、透明混合；沿 U 方向做透明度渐隐，V 方向做细边高光，带轻微流动噪声。
Shader "WanXiang/BossIntroBand"
{
    Properties
    {
        _Color      ("Base", Color) = (0.55,0.75,0.55,0.30)
        _EdgeColor  ("Edge", Color) = (0.95,0.95,0.85,0.85)
        _EdgeWidth  ("Edge Width", Range(0,0.2)) = 0.03
        _Progress   ("Reveal", Range(0,1)) = 1
        _Flow       ("Flow", Range(0,5)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Color, _EdgeColor;
            float _EdgeWidth, _Progress, _Flow;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p){ return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }

            fixed4 frag (v2f i) : SV_Target
            {
                // 从左向右揭开
                float reveal = smoothstep(_Progress - 0.05, _Progress, i.uv.x);
                float body = _Color.a * reveal;

                // 上沿高光（细边）
                float top = smoothstep(_EdgeWidth, 0.0, abs(i.uv.y - 0.86));
                float bot = smoothstep(_EdgeWidth, 0.0, abs(i.uv.y - 0.14));

                // 轻微流动噪声，避免死板
                float n = hash(floor(i.uv * float2(120.0, 6.0) + _Time.y * _Flow));
                body *= 0.85 + 0.15 * n;

                fixed3 col = lerp(_Color.rgb, _EdgeColor.rgb, saturate(top + bot));
                float a = saturate(body + (top + bot) * _EdgeColor.a * 0.9);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
