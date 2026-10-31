// 单位元素附着描边（2026-10-31）
// 用法：与本体同一张 sprite 的第二个 SpriteRenderer，排序在立绘之上。
// 只画"边缘 + 内部薄辉光"，颜色由 _Color 给（元素色），呼吸由 _Pulse 驱动。
Shader "WanXiang/UnitAura"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color     ("Element Color", Color) = (0.6,0.9,1,1)
        _Strength  ("Strength", Range(0,2)) = 1
        _RimWidth  ("Rim Width (px)", Range(0.0005,0.02)) = 0.004
        _Inner     ("Inner Glow", Range(0,1)) = 0.18
        _Pulse     ("Pulse", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Strength, _RimWidth, _Inner, _Pulse;

            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;
                // 4 邻域取样做边缘检测（alpha 差 = 轮廓）
                float ax = tex2D(_MainTex, i.uv + float2(_RimWidth, 0)).a;
                float ay = tex2D(_MainTex, i.uv + float2(0, _RimWidth)).a;
                float ex = tex2D(_MainTex, i.uv - float2(_RimWidth, 0)).a;
                float ey = tex2D(_MainTex, i.uv - float2(0, _RimWidth)).a;
                float edge = saturate(a - min(min(ax, ay), min(ex, ey)));

                // 只在"本体内部"画，避免整块方块
                float inside = step(0.01, a);
                float rim = edge * 4.0 * inside;

                // 内部薄辉光 + 呼吸
                float breathe = 1.0 - _Pulse * 0.5 * (1.0 + sin(_Time.y * 3.0));
                float inner = a * _Inner * breathe;

                float alpha = saturate((rim + inner) * _Strength);
                fixed3 col = _Color.rgb * (0.85 + 0.35 * rim);
                return fixed4(col, alpha * _Color.a);
            }
            ENDCG
        }
    }
}
