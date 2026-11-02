// 觉醒立绘（2026-11-02）
// 进化后的异兽：金色内发光 + 边缘描金 + 缓慢流光。_Amount=0 时与普通精灵一致。
Shader "WanXiang/AwakenSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color    ("Tint", Color) = (1,1,1,1)
        _Amount   ("Awaken", Range(0,1)) = 1
        _Gold     ("Gold", Color) = (1.0, 0.82, 0.38, 1)
        _Rim      ("Rim Width (uv)", Range(0.001,0.02)) = 0.004
        _Inner    ("Inner Glow", Range(0,1)) = 0.22
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color, _Gold;
            float _Amount, _Rim, _Inner;

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; o.color = v.color * _Color; return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float a = c.a;
                if (_Amount <= 0.001) return fixed4(c.rgb * a, a);

                // 边缘：与 4 邻域的 alpha 差（同一张图，绝不会错位）
                float a1 = tex2D(_MainTex, i.uv + float2( _Rim, 0)).a;
                float a2 = tex2D(_MainTex, i.uv + float2(-_Rim, 0)).a;
                float a3 = tex2D(_MainTex, i.uv + float2(0,  _Rim)).a;
                float a4 = tex2D(_MainTex, i.uv + float2(0, -_Rim)).a;
                float edge = saturate((a - min(min(a1,a2), min(a3,a4))) * 5.0);

                // 内部薄辉光 + 流光（自上而下扫过）
                float flow = 0.5 + 0.5 * sin((i.uv.y - _Time.y * 0.25) * 12.566);
                float inner = a * _Inner * (0.55 + 0.45 * flow);

                float g = saturate((edge + inner) * _Amount);
                float3 rgb = lerp(c.rgb, _Gold.rgb, g);
                return fixed4(rgb * a, a);
            }
            ENDCG
        }
    }
}
