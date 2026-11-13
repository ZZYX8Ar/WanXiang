// 万相 · 捕获特效（UI Image 用，程序化扩散光环 + 中心柔光，不需要贴图）
// _Progress：0 = 起始，1 = 扩散到边缘；由 BattleCapturePresenter 用 DOTween 驱动。
Shader "WanXiang/CaptureBurst"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Progress", Range(0,1)) = 0
        _RingWidth ("Ring Width", Range(0.01,0.4)) = 0.10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane"
               "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            float4 _Color;
            float _Progress;
            float _RingWidth;

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;                       // 0 中心 → 1 边缘
                float r = _Progress;
                float ring = smoothstep(r - _RingWidth, r, d)
                           * (1.0 - smoothstep(r, r + _RingWidth, d));
                float core = saturate(1.0 - d * 3.2) * (1.0 - r);          // 中心柔光随扩散渐隐
                float a = saturate(ring * 1.35 + core * 0.5);
                float3 col = lerp(float3(1.0, 0.84, 0.42), float3(1.0, 0.97, 0.88), core);
                return fixed4(col * i.color.rgb * _Color.rgb, a * i.color.a * _Color.a);
            }
            ENDCG
        }
    }
}
