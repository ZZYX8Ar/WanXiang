// ============================================================================
//  万相 · UI 亮度抠白（Luma Key）
//  ---------------------------------------------------------------------------
//  干什么：把一张"白底/宣纸底"的图按亮度抠成透明 —— 亮部（纸/雾）透明、暗部（山/草/墨）不透明，
//          于是**生成的水彩图层可以真正分层叠加**（不用做任何本地抠图/修补）。
//  用在：主界面地图的"远山峰层""近景草地层"（与中景底图组成三层，配合 HomeMapJourney 分层视差）。
//
//  ⚠ UGUI 自定义 shader 约定（同 UIStreak / UIPerspective）：Transparent + IgnoreProjector、
//    Blend SrcAlpha OneMinusSrcAlpha、片元乘 i.color 与 _Color。
// ============================================================================

Shader "WanXiang/UI/LumaKey"
{
    Properties
    {
        _MainTex   ("Texture", 2D) = "white" {}
        _Color     ("Tint", Color) = (1, 1, 1, 1)
        // 亮度高于 _KeyWhite 的像素 → 完全透明（纸/雾）
        _KeyWhite  ("Key White", Range(0, 1)) = 0.82
        // 亮度低于 _KeyBlack 的像素 → 完全不透明（墨/山/草）
        _KeyBlack  ("Key Black", Range(0, 1)) = 0.52
        _Opacity   ("Opacity", Range(0, 1)) = 1
        // 上/下边缘渐隐（避免图层边界出现硬边）
        _EdgeFadeY ("Edge Fade Y", Range(0, 0.5)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Default"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f      { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color;
            float  _KeyWhite;
            float  _KeyBlack;
            float  _Opacity;
            float  _EdgeFadeY;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                float luma = dot(tex.rgb, float3(0.299, 0.587, 0.114));

                // 亮度 → 不透明度：亮(≥_KeyWhite)=0，暗(≤_KeyBlack)=1，中间平滑
                float k = smoothstep(_KeyWhite, _KeyBlack, luma);

                fixed4 col = tex * i.color * _Color;
                col.a *= k * _Opacity;

                // 上/下边缘渐隐（可选）
                float fy = max(_EdgeFadeY, 0.0001);
                float ey = smoothstep(0.0, fy, i.uv.y) * smoothstep(0.0, fy, 1.0 - i.uv.y);
                col.a *= ey;

                return col;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
