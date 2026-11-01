// 立绘 + 元素描边（2026-11-01）
// 直接给**本体**换材质：同一个 mesh、同一套 uv，所以描边一定和立绘对齐。
// _Strength = 0 时与 Sprites/Default 等价（不开描边）。
Shader "WanXiang/SpriteAura"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color      ("Tint", Color) = (1,1,1,1)
        _AuraColor  ("Aura Color", Color) = (1,1,1,0)
        _Strength   ("Aura Strength", Range(0,1)) = 0
        _RimWidth   ("Rim Width (uv)", Range(0.0005,0.02)) = 0.003
        _Inner      ("Inner Glow", Range(0,0.6)) = 0.10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" "PreviewType"="Plane" }
        Cull Off Lighting Off ZWrite Off Blend One OneMinusSrcAlpha   // 预乘 alpha（Sprites/Default 同款）

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color, _AuraColor;
            float _Strength, _RimWidth, _Inner;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // 轮廓：与 4 邻域的最大 alpha 差
                float a  = c.a;
                float a1 = tex2D(_MainTex, i.uv + float2( _RimWidth, 0)).a * i.color.a;
                float a2 = tex2D(_MainTex, i.uv + float2(-_RimWidth, 0)).a * i.color.a;
                float a3 = tex2D(_MainTex, i.uv + float2(0,  _RimWidth)).a * i.color.a;
                float a4 = tex2D(_MainTex, i.uv + float2(0, -_RimWidth)).a * i.color.a;
                float mn = min(min(a1,a2), min(a3,a4));
                float rim = saturate((a - mn) * 6.0);

                // 内部薄辉光
                float inner = a * _Inner;
                float glow = saturate((rim + inner) * _Strength);

                // 描边叠在立绘上（不破坏原色）
                float3 rgb = lerp(c.rgb, _AuraColor.rgb, saturate(glow * 1.4));
                float  alpha = saturate(c.a + glow * _AuraColor.a * 0.9);
                return fixed4(rgb * alpha, alpha);     // 预乘输出
            }
            ENDCG
        }
    }
}
